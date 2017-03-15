// <copyright file="PlayServicesResolver.cs" company="Google Inc.">
// Copyright (C) 2015 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>

namespace GooglePlayServices
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using Google.JarResolver;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Play services resolver.  This is a background post processor
    /// that copies over the Google play services .aar files that
    /// plugins have declared as dependencies.  If the Unity version is less than
    /// 5, aar files are not supported so this class 'explodes' the aar file into
    /// a plugin directory.  Once the version of Unity is upgraded, the exploded
    /// files are removed in favor of the .aar files.
    /// </summary>
    [InitializeOnLoad]
    public class PlayServicesResolver : AssetPostprocessor
    {
        /// <summary>
        /// The instance to the play services support object.
        /// </summary>
        private static PlayServicesSupport svcSupport;

        /// <summar>
        /// Selects between different types of IResolver implementations that can be used.
        /// </summary>
        public enum ResolverType
        {
            Default,            // Standard versioned resolver
            GradlePrebuild,     // Used if registered and enabled in the settings.
        }

        /// <summary>
        /// The resolver to use, injected to allow for version updating.
        /// </summary>
        private static Dictionary<ResolverType, IResolver> _resolvers =
            new Dictionary<ResolverType, IResolver>();


        /// <summary>
        /// Flag used to prevent re-entrant auto-resolution.
        /// </summary>
        private static bool autoResolving = false;

        /// <summary>
        /// Flag used to prevent re-entrant resolution when a build configuration changes.
        /// </summary>
        private static bool buildConfigChanged = false;

        /// <summary>
        /// Seconds to wait until re-resolving dependencies after the bundle ID has changed.
        /// </summary>
        private const int bundleUpdateDelaySeconds = 3;

        /// <summary>
        /// Last time the bundle ID was checked.
        /// </summary>
        private static DateTime lastBundleIdPollTime = DateTime.Now;

        /// <summary>
        /// Last bundle ID value.
        /// </summary>
        private static string lastBundleId = UnityCompat.ApplicationId;

        /// <summary>
        /// Last value of bundle ID since the last time OnBundleId() was called.
        /// </summary>
        private static string bundleId = UnityCompat.ApplicationId;

        /// <summary>
        /// Arguments for the bundle ID update event.
        /// </summary>
        public class BundleIdChangedEventArgs : EventArgs {
            /// <summary>
            /// Current project Bundle ID.
            /// </summary>
            public string BundleId { get; set; }

            /// <summary>
            /// Bundle ID when this event was last fired.
            /// </summary>
            public string PreviousBundleId { get; set; }
        }

        /// <summary>
        /// Event which is fired when the bundle ID is updated.
        /// </summary>
        public static event EventHandler<BundleIdChangedEventArgs> BundleIdChanged;

        /// <summary>
        /// The value of GradleBuildEnabled when PollBuildSystem() was called.
        /// </summary>
        private static bool previousGradleBuildEnabled = false;

        /// <summary>
        /// The value of ProjectExportEnabled when PollBuildSystem() was called.
        /// </summary>
        private static bool previousProjectExportEnabled = false;

        /// <summary>
        /// Asset label applied to files managed by this plugin.
        /// </summary>
        private const string ManagedAssetLabel = "gpsr";

        /// <summary>
        /// Get a boolean property from UnityEditor.EditorUserBuildSettings.
        /// </summary>
        /// Properties are introduced over successive versions of Unity so use reflection to
        /// retrieve them.
        private static object GetEditorUserBuildSettingsProperty(string name,
                                                                 object defaultValue)
        {
            var editorUserBuildSettingsType = typeof(UnityEditor.EditorUserBuildSettings);
            var property = editorUserBuildSettingsType.GetProperty(name);
            if (property != null)
            {
                var value = property.GetValue(null, null);
                if (value != null) return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Whether the Gradle build system is enabled.
        /// </summary>
        public static bool GradleBuildEnabled
        {
            get
            {
                return GetEditorUserBuildSettingsProperty(
                    "androidBuildSystem", "").ToString().Equals("Gradle");
            }
        }

        /// <summary>
        /// Whether project export is enabled.
        /// </summary>
        public static bool ProjectExportEnabled
        {
            get
            {
                var value = GetEditorUserBuildSettingsProperty("exportAsGoogleAndroidProject",
                                                               null);
                return value == null ? false : (bool)value;
            }
        }

        /// <summary>
        /// If Gradle project export is enabled.
        /// </summary>
        public static bool GradleProjectExportEnabled {
            get {
                return PlayServicesResolver.GradleBuildEnabled &&
                    PlayServicesResolver.ProjectExportEnabled;
            }
        }

        /// <summary>
        /// Arguments for the Android build system changed event.
        /// </summary>
        public class AndroidBuildSystemChangedArgs : EventArgs {
            /// <summary>
            /// Gradle was selected as the build system when this event was fired.
            /// </summary>
            public bool GradleBuildEnabled { get; set; }

            /// <summary>
            /// Whether Gradle was selected as the build system the last time this event was fired.
            /// </summary>
            public bool PreviousGradleBuildEnabled { get; set; }

            /// <summary>
            /// Project export was selected when this event was fired.
            /// </summary>
            public bool ProjectExportEnabled { get; set; }

            /// <summary>
            /// Whether project export was selected when this event was fired.
            /// </summary>
            public bool PreviousProjectExportEnabled { get; set; }
        }

        /// <summary>
        /// Event which is fired when the Android build system changes.
        /// </summary>
        public static event EventHandler<AndroidBuildSystemChangedArgs> AndroidBuildSystemChanged;

        /// <summary>
        /// Name of the property on UnityEditor.PlayerSettings.Android which describes the
        /// target ABI.
        /// </summary>
        private const string ANDROID_TARGET_DEVICE_ABI_PROPERTY_NAME = "targetDevice";

        /// <summary>
        /// Default target ABI for Android projects.
        /// </summary>
        internal const string DEFAULT_ANDROID_TARGET_DEVICE_ABI = "fat";

        /// <summary>
        /// The string value of UnityEditor.PlayerSettings.Android.targetDevice when
        /// PollTargetDevice() was called.
        /// </summary>
        private static string previousAndroidTargetDeviceAbi = DEFAULT_ANDROID_TARGET_DEVICE_ABI;

        /// <summary>
        /// Get a string representation of the target ABI.
        /// </summary>
        /// This uses reflection to retrieve the property as it is not present in Unity version
        /// less than 5.x.
        internal static string AndroidTargetDeviceAbi {
            get {
                var targetDeviceAbi = DEFAULT_ANDROID_TARGET_DEVICE_ABI;
                var property = typeof(UnityEditor.PlayerSettings.Android).GetProperty(
                    ANDROID_TARGET_DEVICE_ABI_PROPERTY_NAME);
                if (property != null) {
                    var value = property.GetValue(null, null);
                    if (value != null) targetDeviceAbi = value.ToString();
                }
                return targetDeviceAbi.ToLower();
            }
        }

        /// <summary>
        /// Arguments for the Android build system changed event.
        /// </summary>
        public class AndroidTargetDeviceAbiChangedArgs : EventArgs {
            /// <summary>
            /// Target device ABI before it changed.
            /// </summary>
            public string PreviousAndroidTargetDeviceAbi { get; set; }

            /// <summary>
            /// Target device ABI when this event was fired.
            /// </summary>
            public string AndroidTargetDeviceAbi { get; set; }
        }

        /// <summary>
        /// Event which is fired when the Android target device ABI changes.
        /// </summary>
        public static event EventHandler<AndroidTargetDeviceAbiChangedArgs>
            AndroidTargetDeviceAbiChanged;

        /// <summary>
        /// Whether the PlayServicesResolver() class is initialized.  This is false until the
        /// first editor update is complete.
        /// </summary>
        internal static bool Initialized { get; private set; }

        /// <summary>
        /// Initializes the <see cref="GooglePlayServices.PlayServicesResolver"/> class.
        /// </summary>
        static PlayServicesResolver()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                RegisterResolver(new ResolverVer1_1());
                RegisterResolver(new GradlePreBuildResolver(), ResolverType.GradlePrebuild);

                svcSupport = PlayServicesSupport.CreateInstance(
                    "PlayServicesResolver",
                    EditorPrefs.GetString("AndroidSdkRoot"),
                    "ProjectSettings",
                    logMessageWithLevel: (string message, PlayServicesSupport.LogLevel level) => {
                        switch (level) {
                            case PlayServicesSupport.LogLevel.Info:
                                UnityEngine.Debug.Log(message);
                                break;
                            case PlayServicesSupport.LogLevel.Warning:
                                UnityEngine.Debug.LogWarning(message);
                                break;
                            case PlayServicesSupport.LogLevel.Error:
                                UnityEngine.Debug.LogError(message);
                                break;
                            default:
                                break;
                        }
                    });

                EditorApplication.update -= AutoResolve;
                EditorApplication.update += AutoResolve;
                BundleIdChanged -= ResolveOnBundleIdChanged;
                BundleIdChanged += ResolveOnBundleIdChanged;
                AndroidBuildSystemChanged -= ResolveOnBuildSystemChanged;
                AndroidBuildSystemChanged += ResolveOnBuildSystemChanged;
                AndroidTargetDeviceAbiChanged -= ResolveOnTargetDeviceAbiChanged;
                AndroidTargetDeviceAbiChanged += ResolveOnTargetDeviceAbiChanged;
            }
            EditorApplication.update -= PollBundleId;
            EditorApplication.update += PollBundleId;
            EditorApplication.update -= PollBuildSystem;
            EditorApplication.update += PollBuildSystem;
            EditorApplication.update -= PollTargetDeviceAbi;
            EditorApplication.update += PollTargetDeviceAbi;
            EditorApplication.update -= InitializationComplete;
            EditorApplication.update += InitializationComplete;
            OnSettingsChanged();
        }

        /// <summary>
        /// Called from EditorApplication.update to signal the class has been initialized.
        /// </summary>
        private static void InitializationComplete() {
            Initialized = true;
        }

        /// <summary>
        /// Registers the resolver.
        /// </summary>
        /// <remarks>
        /// The resolver with the greatest version number is retained
        /// </remarks>
        /// <returns>The resolver.</returns>
        /// <param name="resolverImpl">Resolver impl.</param>
        public static IResolver RegisterResolver(IResolver resolverImpl,
                                                 ResolverType resolverType=ResolverType.Default)
        {
            if (resolverImpl == null)
            {
                return Resolver;
            }

            IResolver destResolver;
            if (!_resolvers.TryGetValue(resolverType, out destResolver) ||
                destResolver.Version() < resolverImpl.Version())
            {
                _resolvers[resolverType] = resolverImpl;
            }
            return resolverImpl;
        }

        private static ResolverType CurrentResolverType
        {
            get {
                return GooglePlayServices.SettingsDialog.PrebuildWithGradle ?
                    ResolverType.GradlePrebuild : ResolverType.Default;
            }
        }

        /// <summary>
        /// Gets the resolver.
        /// </summary>
        /// <value>The resolver.</value>
        public static IResolver Resolver
        {
            get
            {
                IResolver resolver = null;
                if (GooglePlayServices.SettingsDialog.PrebuildWithGradle) {
                    _resolvers.TryGetValue(ResolverType.GradlePrebuild, out resolver);
                }
                if (resolver == null) {
                    _resolvers.TryGetValue(ResolverType.Default, out resolver);
                }
                return resolver;
            }
        }


        /// <summary>
        /// Patterns of files that are monitored to trigger auto resolution.
        /// </summary>
        private static HashSet<Regex> autoResolveFilePatterns = new HashSet<Regex>();

        /// <summary>
        /// Assets that have been imported since the last auto resolution.
        /// </summary>
        private static HashSet<string> importedAssetsSinceLastResolve = new HashSet<string>();

        /// <summary>
        /// Add file patterns to monitor to trigger auto resolution.
        /// </summary>
        /// <param name="patterns">Set of file patterns to monitor to trigger auto
        /// resolution.</param>
        public static void AddAutoResolutionFilePatterns(IEnumerable<Regex> patterns) {
            autoResolveFilePatterns.UnionWith(patterns);
        }

        /// <summary>
        /// Check the set of recently imported assets to see whether resolution should be
        /// triggered.
        /// </summary>
        private static void CheckImportedAssets() {
            var filesToCheck = new HashSet<string>(importedAssetsSinceLastResolve);
            importedAssetsSinceLastResolve.Clear();
            bool resolve = false;
            foreach (var asset in filesToCheck) {
                foreach (var pattern in autoResolveFilePatterns) {
                    if (pattern.Match(asset).Success) {
                        PlayServicesSupport.Log(
                            String.Format("Found asset {0} matching {1}, attempting " +
                                          "auto-resolution.",
                                          asset, pattern.ToString()),
                            level: PlayServicesSupport.LogLevel.Info, verbose: true);
                        resolve = true;
                        break;
                    }
                }
            }
            if (resolve) AutoResolve();
        }

        /// <summary>
        /// Called by Unity when all assets have been updated. This
        /// is used to kick off resolving the dependendencies declared.
        /// </summary>
        /// <param name="importedAssets">Imported assets. (unused)</param>
        /// <param name="deletedAssets">Deleted assets. (unused)</param>
        /// <param name="movedAssets">Moved assets. (unused)</param>
        /// <param name="movedFromAssetPaths">Moved from asset paths. (unused)</param>
        private static void OnPostprocessAllAssets(string[] importedAssets,
                                                   string[] deletedAssets,
                                                   string[] movedAssets,
                                                   string[] movedFromAssetPaths) {
            if (Resolver != null && Resolver.AutomaticResolutionEnabled()) {
                // If anything has been removed from the packaging directory schedule resolution.
                foreach (string asset in deletedAssets) {
                    if (asset.StartsWith(GooglePlayServices.SettingsDialog.PackageDir)) {
                        EditorApplication.update -= AutoResolve;
                        EditorApplication.update += AutoResolve;
                        return;
                    }
                }
                // Schedule a check of imported assets.
                if (importedAssets.Length > 0 && autoResolveFilePatterns.Count > 0) {
                    importedAssetsSinceLastResolve = new HashSet<string>(importedAssets);
                    EditorApplication.update -= CheckImportedAssets;
                    EditorApplication.update += CheckImportedAssets;
                    return;
                }
            }
        }

        /// <summary>
        /// Resolve dependencies if auto-resolution is enabled.
        /// </summary>
        private static void AutoResolve() {
            if (!autoResolving) {
                if (Resolver.AutomaticResolutionEnabled()) {
                    // Prevent resolution on the call to OnPostprocessAllAssets().
                    autoResolving = true;
                    Resolve();
                    autoResolving = false;
                } else if (!PlayServicesSupport.InBatchMode &&
                           GooglePlayServices.SettingsDialog.AutoResolutionDisabledWarning &&
                           PlayServicesSupport.GetAllDependencies().Count > 0) {
                    switch (EditorUtility.DisplayDialogComplex(
                        "Warning: Auto-resolution of Android dependencies is disabled!",
                        "Would you like to enable auto-resolution of Android dependencies?\n\n" +
                        "With auto-resolution of Android dependencies disabled you must " +
                        "manually resolve dependencies using the " +
                        "\"Assets > Play Services Resolver > Android Resolver > " +
                        "Resolve Client Jars\" menu item.\n\nFailure to resolve Android " +
                        "dependencies will result in an non-functional application.",
                        "Yes", "Not Now", "Silence Warning")) {
                        case 0:  // Yes
                            GooglePlayServices.SettingsDialog.EnableAutoResolution = true;
                            break;
                        case 1:  // Not now
                            break;
                        case 2:  // Ignore
                            GooglePlayServices.SettingsDialog.AutoResolutionDisabledWarning =
                                false;
                            break;
                    }
                }
            }
            EditorApplication.update -= AutoResolve;
        }

        /// <summary>
        /// Determine which packages - if any - should be re-resolved.
        /// </summary>
        /// <returns>Array of packages that should be re-resolved, null otherwise.</returns>
        delegate string[] ReevaluatePackages();

        /// <summary>
        /// Auto-resolve if any packages need to be resolved.
        /// </summary>
        private static void Reresolve() {
            if (Resolver.AutomaticResolutionEnabled()) {
                buildConfigChanged = true;
                if (DeleteFiles(Resolver.OnBuildSettings())) AutoResolve();
                buildConfigChanged = false;
            }
        }

        /// <summary>
        /// If the user changes the bundle ID, perform resolution again.
        /// </summary>
        private static void ResolveOnBundleIdChanged(object sender,
                                                     BundleIdChangedEventArgs args) {
            Reresolve();
        }

        /// <summary>
        /// If the user changes the bundle ID, perform resolution again.
        /// </summary>
        private static void PollBundleId()
        {
            string currentBundleId = UnityCompat.ApplicationId;
            DateTime currentPollTime = DateTime.Now;
            if (currentBundleId != bundleId)
            {
                // If the bundle ID setting hasn't changed for a while.
                if (currentBundleId == lastBundleId)
                {
                    if (currentPollTime.Subtract(lastBundleIdPollTime).Seconds >=
                        bundleUpdateDelaySeconds)
                    {
                        if (BundleIdChanged != null) {
                            BundleIdChanged(null,
                                            new BundleIdChangedEventArgs {
                                                PreviousBundleId = bundleId,
                                                BundleId = currentBundleId
                                            });
                        }
                        bundleId = currentBundleId;
                    }
                }
                else
                {
                    lastBundleId = currentBundleId;
                    lastBundleIdPollTime = currentPollTime;
                }
            }
        }

        /// <summary>
        /// If the user changes the Android build system, perform resolution again.
        /// </summary>
        private static void ResolveOnBuildSystemChanged(object sender,
                                                        AndroidBuildSystemChangedArgs args) {
            Reresolve();
        }

        /// <summary>
        /// Poll the Android build system selection for changes.
        /// </summary>
        private static void PollBuildSystem()
        {
            bool gradleBuildEnabled = GradleBuildEnabled;
            bool projectExportEnabled = ProjectExportEnabled;
            if (previousGradleBuildEnabled != gradleBuildEnabled ||
                previousProjectExportEnabled != projectExportEnabled)
            {
                if (AndroidBuildSystemChanged != null)
                {
                    AndroidBuildSystemChanged(null, new AndroidBuildSystemChangedArgs {
                            GradleBuildEnabled = gradleBuildEnabled,
                            PreviousGradleBuildEnabled = previousGradleBuildEnabled,
                            ProjectExportEnabled = projectExportEnabled,
                            PreviousProjectExportEnabled = previousProjectExportEnabled,
                        });
                }
                previousGradleBuildEnabled = gradleBuildEnabled;
                previousProjectExportEnabled = projectExportEnabled;
            }
        }

        /// <summary>
        /// Poll the target device ABI for changes.
        /// </summary>
        private static void PollTargetDeviceAbi() {
            string currentAbi = AndroidTargetDeviceAbi;
            if (currentAbi != previousAndroidTargetDeviceAbi &&
                AndroidTargetDeviceAbiChanged != null) {
                AndroidTargetDeviceAbiChanged(null, new AndroidTargetDeviceAbiChangedArgs {
                        PreviousAndroidTargetDeviceAbi = previousAndroidTargetDeviceAbi,
                        AndroidTargetDeviceAbi = currentAbi
                    });
            }
            previousAndroidTargetDeviceAbi = currentAbi;
        }

        /// <summary>
        /// Hide shared libraries from Unity's build system that do not target the currently
        /// selected ABI.
        /// </summary>
        private static void ResolveOnTargetDeviceAbiChanged(
                object sender, AndroidTargetDeviceAbiChangedArgs args) {
            Reresolve();
        }

        /// <summary>
        /// Delete the specified array of files and directories.
        /// </summary>
        /// <param name="filenames">Array of files or directories to delete.</param>
        /// <returns>true if files are deleted, false otherwise.</returns>
        private static bool DeleteFiles(string[] filenames)
        {
            if (filenames == null) return false;
            bool deletedFiles = false;
            foreach (string artifact in filenames) {
                deletedFiles |= PlayServicesSupport.DeleteExistingFileOrDirectory(
                    artifact, includeMetaFiles: true);
            }
            if (deletedFiles) AssetDatabase.Refresh();
            return deletedFiles;
        }

        /// <summary>
        /// Resolve dependencies.
        /// </summary>
        /// <param name="resolutionComplete">Delegate called when resolution is complete.</param>
        private static void Resolve(System.Action resolutionComplete = null)
        {
            if (!buildConfigChanged) DeleteFiles(Resolver.OnBuildSettings());
            System.IO.Directory.CreateDirectory(GooglePlayServices.SettingsDialog.PackageDir);
            Resolver.DoResolution(svcSupport, GooglePlayServices.SettingsDialog.PackageDir,
                                  (oldDependency, newDependency) => {
                                      return Resolver.ShouldReplaceDependency(oldDependency,
                                                                              newDependency);
                                  },
                                  () => {
                                      AssetDatabase.Refresh();
                                      if (resolutionComplete != null) resolutionComplete();
                                  });
        }

        /// <summary>
        /// Display a dialog explaining that the resolver is disabled in the current configuration.
        /// </summary>
        private static void NotAvailableDialog() {
            EditorUtility.DisplayDialog("Play Services Resolver.",
                                        "Resolver not enabled. " +
                                        "Android platform must be selected.",
                                        "OK");

        }

        /// <summary>
        /// Add a menu item for resolving the jars manually.
        /// </summary>
        [MenuItem("Assets/Play Services Resolver/Android Resolver/Settings")]
        public static void SettingsDialog()
        {
            if (Resolver != null)
            {
                Resolver.ShowSettingsDialog();
            }
            else
            {
                DefaultResolver.ShowSettings();
            }
        }

        /// <summary>
        /// Add a menu item for resolving the jars manually.
        /// </summary>
        [MenuItem("Assets/Play Services Resolver/Android Resolver/Resolve Client Jars")]
        public static void MenuResolve()
        {
            if (Resolver == null) {
                NotAvailableDialog();
                return;
            }
            Resolve(() => { EditorUtility.DisplayDialog("Android Jar Dependencies",
                                                        "Resolution Complete", "OK"); });
        }

        /// <summary>
        /// Called when settings change.
        /// </summary>
        internal static void OnSettingsChanged() {
            PlayServicesSupport.verboseLogging = GooglePlayServices.SettingsDialog.VerboseLogging;
            if (Initialized) {
                if (Resolver != null) AutoResolve();
            }
        }

        /// <summary>
        /// Handles the overwrite confirmation.
        /// </summary>
        /// <returns><c>true</c>, if overwrite confirmation was handled, <c>false</c> otherwise.</returns>
        /// <param name="oldDep">Old dependency.</param>
        /// <param name="newDep">New dependency replacing old.</param>
        public static bool HandleOverwriteConfirmation(Dependency oldDep, Dependency newDep)
        {
            // Don't prompt overwriting the same version, just do it.
            if (oldDep.BestVersion != newDep.BestVersion)
            {
                string msg = "Replace " + oldDep.Artifact + " version " +
                             oldDep.BestVersion + " with version " + newDep.BestVersion + "?";
                return EditorUtility.DisplayDialog("Android Jar Dependencies",
                    msg,"Replace","Keep");
            }
            return true;
        }

        /// <summary>
        /// Label a set of assets that should be managed by this plugin.
        /// </summary>
        /// <param name="assetPaths">Set of assets to label.</param>
        /// <param name="displayWarning">Whether to display a warning if assets can't be
        /// labeled.</param>
        /// <param name="recursive">Whether to label assets in subdirectories of the specified
        /// assetPaths.</param>
        /// <returns>List of assets that could not be labeled.</returns>
        internal static HashSet<string> LabelAssets(IEnumerable<string> assetPaths,
                                                    bool displayWarning = true,
                                                    bool recursive = false) {
            var assetsWithoutAssetImporter = new HashSet<string>(assetPaths);
            if (assetsWithoutAssetImporter.Count == 0) return assetsWithoutAssetImporter;
            var projectDataFolder = Path.GetFullPath(Application.dataPath);
            foreach (var assetPath in new List<string>(assetsWithoutAssetImporter)) {
                // Ignore asset meta files which are used to store the labels and files that
                // are not in the project.
                var fullAssetPath = Path.GetFullPath(assetPath);
                if (assetPath.EndsWith(".meta") || !fullAssetPath.StartsWith(projectDataFolder)) {
                    assetsWithoutAssetImporter.Remove(assetPath);
                    continue;
                }

                // Get the relative path of this asset.
                var relativeAssetPath = Path.Combine(
                    Path.GetFileName(projectDataFolder),
                    fullAssetPath.Substring(projectDataFolder.Length +1));

                // If the asset is a directory, add labels to the contents.
                if (recursive && Directory.Exists(relativeAssetPath)) {
                    assetsWithoutAssetImporter.UnionWith(
                        LabelAssets(Directory.GetFileSystemEntries(relativeAssetPath),
                                    displayWarning: false));
                }

                // It's likely files have been added or removed without using AssetDatabase methods
                // so (re)import the asset to make sure it's in the AssetDatabase.
                AssetDatabase.ImportAsset(relativeAssetPath,
                                          options: ImportAssetOptions.ForceSynchronousImport);

                // Add the label to the asset.
                AssetImporter importer = AssetImporter.GetAtPath(relativeAssetPath);
                if (importer != null) {
                    var labels = new HashSet<string>(AssetDatabase.GetLabels(importer));
                    labels.Add(ManagedAssetLabel);
                    AssetDatabase.SetLabels(importer, (new List<string>(labels)).ToArray());
                    assetsWithoutAssetImporter.Remove(assetPath);
                }
            }
            if (assetsWithoutAssetImporter.Count > 0 && displayWarning) {
                Debug.LogWarning(String.Format(
                    "Failed to add tracking label {0} to some assets.\n\n" +
                    "The following files will not be managed by this module:\n" +
                    "{1}\n", ManagedAssetLabel,
                    String.Join("\n", new List<string>(assetsWithoutAssetImporter).ToArray())));
            }
            return assetsWithoutAssetImporter;
        }

        /// <summary>
        /// Find the set of assets managed by this plugin.
        /// </summary>
        internal static IEnumerable<string> FindLabeledAssets() {
            foreach (string assetGuid in AssetDatabase.FindAssets("l:" + ManagedAssetLabel)) {
                yield return AssetDatabase.GUIDToAssetPath(assetGuid);
            }
        }
    }
}

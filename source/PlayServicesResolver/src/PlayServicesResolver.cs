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

        /// <summary>
        /// The resolver to use, injected to allow for version updating.
        /// </summary>
        private static IResolver _resolver;

        /// <summary>
        /// Flag used to prevent re-entrant auto-resolution.
        /// </summary>
        private static bool autoResolving = false;

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
        private static string lastBundleId = PlayerSettings.bundleIdentifier;

        /// <summary>
        /// Last value of bundle ID since the last time OnBundleId() was called.
        /// </summary>
        private static string bundleId = PlayerSettings.bundleIdentifier;

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
        /// Initializes the <see cref="GooglePlayServices.PlayServicesResolver"/> class.
        /// </summary>
        static PlayServicesResolver()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                svcSupport = PlayServicesSupport.CreateInstance(
                    "PlayServicesResolver",
                    EditorPrefs.GetString("AndroidSdkRoot"),
                    "ProjectSettings",
                    logger: UnityEngine.Debug.Log);

                EditorApplication.update -= AutoResolve;
                EditorApplication.update += AutoResolve;
                BundleIdChanged -= ResolveOnBundleIdChanged;
                BundleIdChanged += ResolveOnBundleIdChanged;
                AndroidBuildSystemChanged -= ResolveOnBuildSystemChanged;
                AndroidBuildSystemChanged += ResolveOnBuildSystemChanged;
            }
            EditorApplication.update -= PollBundleId;
            EditorApplication.update += PollBundleId;
            EditorApplication.update -= PollBuildSystem;
            EditorApplication.update += PollBuildSystem;
        }

        /// <summary>
        /// Registers the resolver.
        /// </summary>
        /// <remarks>
        /// The resolver with the greatest version number is retained
        /// </remarks>
        /// <returns>The resolver.</returns>
        /// <param name="resolverImpl">Resolver impl.</param>
        public static IResolver RegisterResolver(IResolver resolverImpl)
        {
            if (resolverImpl == null)
            {
                return _resolver;
            }
            if (_resolver == null || _resolver.Version() < resolverImpl.Version())
            {
                _resolver = resolverImpl;
            }
            return _resolver;
        }

        /// <summary>
        /// Gets the resolver.
        /// </summary>
        /// <value>The resolver.</value>
        public static IResolver Resolver
        {
            get
            {
                return _resolver;
            }
        }

        /// <summary>
        /// Called by Unity when all assets have been updated. This
        /// is used to kick off resolving the dependendencies declared.
        /// </summary>
        /// <param name="importedAssets">Imported assets. (unused)</param>
        /// <param name="deletedAssets">Deleted assets. (unused)</param>
        /// <param name="movedAssets">Moved assets. (unused)</param>
        /// <param name="movedFromAssetPaths">Moved from asset paths. (unused)</param>
        static void OnPostprocessAllAssets(string[] importedAssets,
                                           string[] deletedAssets,
                                           string[] movedAssets,
                                           string[] movedFromAssetPaths)
        {
            if (Resolver == null) return;

            if (Resolver.ShouldAutoResolve(importedAssets, deletedAssets,
                                           movedAssets, movedFromAssetPaths))
            {
                AutoResolve();
            }
        }

        /// <summary>
        /// Resolve dependencies if auto-resolution is enabled.
        /// </summary>
        private static void AutoResolve()
        {
            if (Resolver.AutomaticResolutionEnabled() && !autoResolving)
            {
                EditorApplication.update -= AutoResolve;
                // Prevent resolution on the call to OnPostprocessAllAssets().
                autoResolving = true;
                Resolve();
                autoResolving = false;
            }
        }

        /// <summary>
        /// If the user changes the bundle ID, perform resolution again.
        /// </summary>
        private static void ResolveOnBundleIdChanged(object sender, BundleIdChangedEventArgs args)
        {
            if (Resolver.AutomaticResolutionEnabled())
            {
                if (DeleteFiles(Resolver.OnBundleId(args.BundleId))) AutoResolve();
            }
        }

        /// <summary>
        /// If the user changes the bundle ID, perform resolution again.
        /// </summary>
        private static void PollBundleId()
        {
            string currentBundleId = PlayerSettings.bundleIdentifier;
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
                                                        AndroidBuildSystemChangedArgs args)
        {
            AutoResolve();
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
        /// Delete the specified array of files and directories.
        /// </summary>
        /// <param name="filenames">Array of files or directories to delete.</param>
        /// <returns>true if files are deleted, false otherwise.</returns>
        private static bool DeleteFiles(string[] filenames)
        {
            if (filenames == null) return false;
            foreach (string artifact in filenames)
            {
                PlayServicesSupport.DeleteExistingFileOrDirectory(artifact,
                                                                  includeMetaFiles: true);
            }
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>
        /// Resolve dependencies.
        /// </summary>
        /// <param name="resolutionComplete">Delegate called when resolution is complete.</param>
        private static void Resolve(System.Action resolutionComplete = null)
        {
            DeleteFiles(Resolver.OnBundleId(PlayerSettings.bundleIdentifier));
            System.IO.Directory.CreateDirectory(GooglePlayServices.SettingsDialog.PackageDir);
            Resolver.DoResolution(svcSupport, GooglePlayServices.SettingsDialog.PackageDir,
                                  HandleOverwriteConfirmation,
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
    }
}

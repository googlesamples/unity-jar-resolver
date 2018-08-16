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
    using System.Threading;
    using System.Xml;
    using Google;
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
        /// Saves the current state of dependencies in the project and allows the caller to
        /// compare the current state vs. the previous state of dependencies in the project.
        /// </summary>
        internal class DependencyState {
            /// <summary>
            /// Set of dependencies and the expected files in the project.
            /// </summary>
            private static string DEPENDENCY_STATE_FILE = Path.Combine(
                "ProjectSettings", "AndroidResolverDependencies.xml");

            /// <summary>
            /// Set of Android packages (AARs / JARs) referenced by this DependencyState.
            /// These are in the Maven style format "group:artifact:version".
            /// </summary>
            public HashSet<string> Packages { get; internal set; }

            /// <summary>
            /// Set of files referenced by this DependencyState.
            /// </summary>
            public HashSet<string> Files { get; internal set; }

            /// <summary>
            /// Determine the current state of the project.
            /// </summary>
            /// <returns>DependencyState instance with data derived from the current
            /// project.</returns>
            public static DependencyState GetState() {
                return new DependencyState {
                    Packages = new HashSet<string>(PlayServicesSupport.GetAllDependencies().Keys),
                    Files = new HashSet<string>(PlayServicesResolver.FindLabeledAssets())
                };
            }

            /// <summary>
            /// Sort a string hashset.
            /// </summary>
            /// <param name="setToSort">Set to sort and return via an enumerable.</param>
            private static IEnumerable<string> SortSet(HashSet<string> setToSort) {
                var sorted = new SortedDictionary<string, bool>();
                foreach (var value in setToSort) sorted[value] = true;
                return sorted.Keys;
            }

            /// <summary>
            /// Write this object to DEPENDENCY_STATE_FILE.
            /// </summary>
            public void WriteToFile() {
                try {
                    Directory.CreateDirectory(Path.GetDirectoryName(DEPENDENCY_STATE_FILE));
                    using (var writer = new XmlTextWriter(new StreamWriter(DEPENDENCY_STATE_FILE)) {
                            Formatting = Formatting.Indented,
                        }) {
                        writer.WriteStartElement("dependencies");
                        writer.WriteStartElement("packages");
                        foreach (var dependencyKey in SortSet(Packages)) {
                            writer.WriteStartElement("package");
                            writer.WriteValue(dependencyKey);
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                        writer.WriteStartElement("files");
                        foreach (var assetPath in SortSet(Files)) {
                            writer.WriteStartElement("file");
                            writer.WriteValue(assetPath);
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                        writer.Flush();
                        writer.Close();
                    }

                } catch (Exception e) {
                    Log(String.Format(
                        "Unable to update dependency file {0} ({1})\n" +
                        "If auto-resolution is enabled, it is likely to be retriggered " +
                        "when any operation triggers resolution.", DEPENDENCY_STATE_FILE, e),
                        level: LogLevel.Warning);
                }
            }

            /// <summary>
            /// Read the state from DEPENDENCY_STATE_FILE.
            /// </summary>
            /// <returns>DependencyState instance read from DEPENDENCY_STATE_FILE.  null is
            /// returned if the file isn't found.</returns>
            ///
            /// This parses files in the following format:
            /// <dependencies>
            ///   <packages>
            ///     <package>group:artifact:version</package>
            ///     ...
            ///   </packages>
            ///   <files>
            ///     <file>package_filename</file>
            ///     ...
            ///   </files>
            /// </dependencies>
            public static DependencyState ReadFromFile() {
                var packages = new HashSet<string>();
                var files = new HashSet<string>();
                if (!XmlUtilities.ParseXmlTextFileElements(
                    DEPENDENCY_STATE_FILE, logger,
                    (reader, elementName, isStart, parentElementName, elementNameStack) => {
                        if (isStart) {
                            if (elementName == "dependencies" && parentElementName == "") {
                                return true;
                            } else if ((elementName == "packages" || elementName == "files") &&
                                       parentElementName == "dependencies") {
                                return true;
                            } else if (elementName == "package" &&
                                       parentElementName == "packages") {
                                if (isStart && reader.Read() &&
                                    reader.NodeType == XmlNodeType.Text) {
                                    packages.Add(reader.ReadContentAsString());
                                }
                                return true;
                            } else if (elementName == "file" && parentElementName == "files") {
                                if (isStart && reader.Read() &&
                                    reader.NodeType == XmlNodeType.Text) {
                                    files.Add(reader.ReadContentAsString());
                                }
                                return true;
                            }
                        }
                        return false;
                    })) {
                    return null;
                }
                return new DependencyState {
                    Packages = packages,
                    Files = files
                };
            }

            /// <summary>
            /// Compare with this object.
            /// </summary>
            /// <param name="obj">Object to compare with.</param>
            /// <returns>true if both objects have the same contents, false otherwise.</returns>
            public override bool Equals(System.Object obj) {
                var state = obj as DependencyState;
                return state != null && Packages.SetEquals(state.Packages) &&
                    Files.SetEquals(state.Files);
            }

            /// <summary>
            /// Generate a hash of this object.
            /// </summary>
            /// <returns>Hash of this object.</returns>
            public override int GetHashCode() {
                int hash = 0;
                foreach (var file in Files) {
                    hash ^= file.GetHashCode();
                }
                foreach (var pkg in Packages) {
                    hash ^= pkg.GetHashCode();
                }
                return hash;
            }

            /// <summary>
            /// Convert a hashset to a sorted comma separated string.
            /// </summary>
            /// <returns>Comma separated string.</returns>
            private static string SetToString(HashSet<string> setToConvert) {
                return String.Join(", ", (new List<string>(SortSet(setToConvert))).ToArray());
            }

            /// <summary>
            /// Display dependencies as a string.
            /// </summary>
            /// <returns>Human readable string.</returns>
            public override string ToString() {
                return String.Format("packages=({0}), files=({1})", SetToString(Packages),
                                     SetToString(Files));
            }
        }

        /// <summary>
        /// Polls a value and signals a callback with the change after the specified delay
        /// time.
        /// </summary>
        private class PropertyPoller<T> {
            /// <summary>
            /// Delegate that is called when a value changes.
            /// </summary>
            /// <param name="previousValue">Previous value of the property that
            /// changed.</param>
            /// <param name="currentValue">Current value of the property that
            /// changed.</param>
            public delegate void Changed(T previousValue, T currentValue);

            // Previous value of the property.
            private T previousValue = default(T);
            // Previous value of the property when it was last polled.
            private T previousPollValue = default(T);
            // Last time the property was polled.
            private DateTime previousPollTime = DateTime.Now;
            // Time to wait before signalling a change.
            private int delayTimeInSeconds;
            // Name of the property being polled.
            private string propertyName;
            // Previous time we checked the property value for a change.
            private DateTime previousCheckTime = DateTime.Now;
            // Time to wait before checking a property.
            private int checkIntervalInSeconds;

            /// <summary>
            /// Create the poller.
            /// </summary>
            /// <param name="initialValue">Initial value of the property being polled.</param>
            /// <param name="propertyName">Name of the property being polled.</param>
            /// <param name="delayTimeInSeconds">Time to wait before signalling that the value
            /// has changed.</param>
            /// <param name="checkIntervalInSeconds">Time to check the value of the property for
            /// changes.<param>
            public PropertyPoller(T initialValue, string propertyName,
                                  int delayTimeInSeconds = 3,
                                  int checkIntervalInSeconds = 1) {
                previousValue = initialValue;
                this.propertyName = propertyName;
                this.delayTimeInSeconds = delayTimeInSeconds;
                this.checkIntervalInSeconds = checkIntervalInSeconds;
            }

            /// <summary>
            /// Poll the specified value for changes.
            /// </summary>
            /// <param name="currentValue">Value being polled.</param>
            /// <param name="changed">Delegate that is called if the value changes.</param>
            public void Poll(T currentValue, Changed changed) {
                var currentTime = DateTime.Now;
                if (currentTime.Subtract(previousCheckTime).TotalSeconds <
                    checkIntervalInSeconds) {
                    return;
                }
                previousCheckTime = currentTime;
                if (!currentValue.Equals(previousValue)) {
                    if (currentValue.Equals(previousPollValue)) {
                        if (currentTime.Subtract(previousPollTime).TotalSeconds >=
                            delayTimeInSeconds) {
                            Log(String.Format("{0} changed: {1} -> {2}", propertyName,
                                              previousValue, currentValue),
                                level: LogLevel.Verbose);
                            changed(previousValue, currentValue);
                            previousValue = currentValue;
                        }
                    } else {
                        previousPollValue = currentValue;
                        previousPollTime = currentTime;
                    }
                }
            }
        }

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
        }

        /// <summary>
        /// The resolver to use, injected to allow for version updating.
        /// </summary>
        private static Dictionary<ResolverType, IResolver> _resolvers =
            new Dictionary<ResolverType, IResolver>();


        /// <summary>
        /// Resoluton job.
        /// This class is used to enqueue a resolution job to execute on the main thread by
        /// ScheduleResolve().
        /// It keeps track of whether the job was started via auto-resolution or explicitly.
        /// If the job was started via auto-resolution it is removed from the currently scheduled
        /// set of jobs when a new job is started via ScheduleResolve().
        /// </summary>
        private class ResolutionJob {

            /// <summary>
            /// Whether this is an auto-resolution job,
            /// </summary>
            public bool IsAutoResolveJob { get; private set; }

            /// <summary>
            /// Action to execute to resolve.
            /// </summary>
            public Action Job { get; private set; }

            /// <summary>
            /// Initialize this instance.
            /// </summary>
            /// <param name="isAutoResolveJob">Whether this is an auto-resolution job.</param>
            /// <param name="job">Action to execute to resolve.</param>
            public ResolutionJob(bool isAutoResolveJob, Action job) {
                IsAutoResolveJob = isAutoResolveJob;
                Job = job;
            }
        }

        /// <summary>
        /// Queue of resolution jobs to execute.
        /// </summary>
        private static List<ResolutionJob> resolutionJobs = new List<ResolutionJob>();

        /// <summary>
        /// Flag used to prevent re-entrant auto-resolution.
        /// </summary>
        private static bool autoResolving = false;

        /// <summary>
        /// ID of the job that executes AutoResolve.
        /// </summary>
        private static int autoResolveJobId = 0;

        /// <summary>
        /// Polls for changes in the bundle ID.
        /// </summary>
        private static PropertyPoller<string> bundleIdPoller = new PropertyPoller<string>(
            UnityCompat.ApplicationId, "Bundle ID");

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
        /// Value of the InstallAndroidPackages before settings were changed.
        /// </summary>
        private static bool previousInstallAndroidPackages =
            GooglePlayServices.SettingsDialog.InstallAndroidPackages;

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
        /// Current build system settings.
        /// </summary>
        private struct AndroidBuildSystemSettings {
            /// <summary>
            // Whether the Gradle build is enabled.
            /// </summary>
            public bool GradleBuildEnabled { get; private set; }

            /// <summary>
            // Whether project export is enabled.
            /// </summary>
            public bool ProjectExportEnabled { get; private set; }

            /// <summary>
            /// Get the current build settings.
            /// </summary>
            public static AndroidBuildSystemSettings Current {
                get {
                    return new AndroidBuildSystemSettings {
                        GradleBuildEnabled = PlayServicesResolver.GradleBuildEnabled,
                        ProjectExportEnabled = PlayServicesResolver.ProjectExportEnabled
                    };
                }
            }

            /// <summary>
            // Compare with another AndroidBuildSystemSettings.
            /// </summary>
            /// <param name="obj">Object to compare with.</param>
            /// <returns>true if the object is the same as this, false otherwise.</returns>
            public override bool Equals(System.Object obj) {
                var other = (AndroidBuildSystemSettings)obj;
                return other.GradleBuildEnabled == GradleBuildEnabled &&
                    other.ProjectExportEnabled == ProjectExportEnabled;
            }

            /// <summary>
            /// Generate a hash of this object.
            /// </summary>
            /// <returns>Hash of this object.</returns>
            public override int GetHashCode() {
                return GradleBuildEnabled.GetHashCode() ^ ProjectExportEnabled.GetHashCode();
            }


            /// <summary>
            /// Convert this object to a string.
            /// </summary>
            /// <returns>String representation.</returns>
            public override string ToString() {
                return String.Format("[GradleBuildEnabled={0} ProjectExportEnabled={1}]",
                                     GradleBuildEnabled, ProjectExportEnabled);
            }
        }

        /// <summary>
        /// Polls for changes in build system settings.
        /// </summary>
        private static PropertyPoller<AndroidBuildSystemSettings> androidBuildSystemPoller =
            new PropertyPoller<AndroidBuildSystemSettings>(
                AndroidBuildSystemSettings.Current, "Android Build Settings");

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
        /// Polls for changes in the Android device ABI.
        /// </summary>
        private static PropertyPoller<AndroidAbis> androidAbisPoller =
            new PropertyPoller<AndroidAbis>(new AndroidAbis(), "Android Target Device ABI");

        /// <summary>
        /// Logger for this module.
        /// </summary>
        internal static Google.Logger logger = new Google.Logger();

        /// <summary>
        /// Arguments for the Android build system changed event.
        /// </summary>
        public class AndroidAbisChangedArgs : EventArgs {
            /// <summary>
            /// Target device ABI before it changed.
            /// </summary>
            public string PreviousAndroidAbis { get; set; }

            /// <summary>
            /// Target device ABI when this event was fired.
            /// </summary>
            public string AndroidAbis { get; set; }
        }

        /// <summary>
        /// Event which is fired when the Android target device ABI changes.
        /// </summary>
        public static event EventHandler<AndroidAbisChangedArgs> AndroidAbisChanged;

        // Parses dependencies from XML dependency files.
        private static AndroidXmlDependencies xmlDependencies = new AndroidXmlDependencies();

        // Last error logged by LogDelegate().
        private static string lastError = null;

        /// <summary>
        /// Get the Android SDK directory.
        /// </summary>
        public static string AndroidSdkRoot {
            get { return EditorPrefs.GetString("AndroidSdkRoot"); }
        }

        /// <summary>
        /// Polls for changes in AndroidSdkRoot.
        /// </summary>
        private static PropertyPoller<string> androidSdkRootPoller =
            new PropertyPoller<string>(AndroidSdkRoot, "Android SDK Path");

        /// <summary>
        /// Arguments for the AndroidSdkRootChanged event.
        /// </summary>
        public class AndroidSdkRootChangedArgs : EventArgs {
            /// <summary>
            /// AndroidSdkRoot before it changed.
            /// </summary>
            public string PreviousAndroidSdkRoot { get; set; }

            /// <summary>
            ///  AndroidSdkRoot when this event was fired.
            /// </summary>
            public string AndroidSdkRoot { get; set; }
        }

        /// <summary>
        /// Event which is fired when the Android SDK root changes.
        /// </summary>
        public static event EventHandler<AndroidSdkRootChangedArgs> AndroidSdkRootChanged;

        /// <summary>
        /// Initializes the <see cref="GooglePlayServices.PlayServicesResolver"/> class.
        /// </summary>
        static PlayServicesResolver() {
            // Create the resolver.
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) {
                RegisterResolver(new ResolverVer1_1());
                // Monitor Android dependency XML files to perform auto-resolution.
                AddAutoResolutionFilePatterns(xmlDependencies.fileRegularExpressions);

                svcSupport = PlayServicesSupport.CreateInstance(
                    "PlayServicesResolver",
                    AndroidSdkRoot,
                    "ProjectSettings",
                    logMessageWithLevel: LogDelegate);
            }
            // Initialize settings and resolve if required.
            OnSettingsChanged();

            // Setup events for auto resolution.
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) {
                BundleIdChanged += ResolveOnBundleIdChanged;
                AndroidBuildSystemChanged += ResolveOnBuildSystemChanged;
                AndroidAbisChanged += ResolveOnAndroidAbisChanged;
                AndroidSdkRootChanged += ResolveOnAndroidSdkRootChange;
                ScheduleAutoResolve();
            }

            // Register events to monitor build system changes for the Android Resolver and other
            // plugins.
            RunOnMainThread.OnUpdate += PollBundleId;
            RunOnMainThread.OnUpdate += PollBuildSystem;
            RunOnMainThread.OnUpdate += PollAndroidAbis;
            RunOnMainThread.OnUpdate += PollAndroidSdkRoot;
        }

        /// <summary>
        /// Called from PlayServicesSupport to log a message.
        /// </summary>
        internal static void LogDelegate(string message, PlayServicesSupport.LogLevel level) {
            Google.LogLevel loggerLogLevel = Google.LogLevel.Info;
            switch (level) {
                case PlayServicesSupport.LogLevel.Info:
                    loggerLogLevel = Google.LogLevel.Info;
                    break;
                case PlayServicesSupport.LogLevel.Warning:
                    loggerLogLevel = Google.LogLevel.Warning;
                    break;
                case PlayServicesSupport.LogLevel.Error:
                    loggerLogLevel = Google.LogLevel.Error;
                    break;
                default:
                    break;
            }
            Log(message, level: loggerLogLevel);
        }

        /// <summary>
        /// Log a filtered message to Unity log, error messages are stored in
        /// PlayServicesSupport.lastError.
        /// </summary>
        /// <param name="message">String to write to the log.</param>
        /// <param name="level">Severity of the message, if this is below the currently selected
        /// Level property the message will not be logged.</param>
        internal static void Log(string message, Google.LogLevel level = LogLevel.Info) {
            if (level == LogLevel.Error) lastError = message;
            logger.Log(message, level: level);
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
                return ResolverType.Default;
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
                        Log(String.Format("Found asset {0} matching {1}, attempting " +
                                          "auto-resolution.",
                                          asset, pattern.ToString()),
                            level: LogLevel.Verbose);
                        resolve = true;
                        break;
                    }
                }
            }
            if (resolve) ScheduleAutoResolve();
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
            if (Resolver != null) {
                // If the manifest changed, try patching it.
                var manifestPath = FileUtils.NormalizePathSeparators(
                    GooglePlayServices.SettingsDialog.AndroidManifestPath);
                foreach (var importedAsset in importedAssets) {
                    if (FileUtils.NormalizePathSeparators(importedAsset) == manifestPath) {
                        PatchAndroidManifest(UnityCompat.ApplicationId, null);
                        break;
                    }
                }

                if (Resolver.AutomaticResolutionEnabled()) {
                    // If anything has been removed from the packaging directory schedule
                    // resolution.
                    foreach (string asset in deletedAssets) {
                        if (asset.StartsWith(GooglePlayServices.SettingsDialog.PackageDir)) {
                            ScheduleAutoResolve();
                            return;
                        }
                    }
                    // Schedule a check of imported assets.
                    if (importedAssets.Length > 0 && autoResolveFilePatterns.Count > 0) {
                        importedAssetsSinceLastResolve = new HashSet<string>(importedAssets);
                        CheckImportedAssets();
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// If auto-resolution is enabled, run resolution synchronously before building the
        /// application.
        /// </summary>
        [UnityEditor.Callbacks.PostProcessSceneAttribute(0)]
        private static void OnPostProcessScene() {
            if (Resolver.AutomaticResolutionEnabled()) {
                Log("Starting auto-resolution before scene build...");
                bool success = ResolveSync(false);
                Log(String.Format("Android resolution {0}.", success ? "succeeded" : "failed"),
                    level: LogLevel.Verbose);
            }
        }

        /// <summary>
        /// Schedule auto-resolution.
        /// </summary>
        /// <param name="delayInMilliseconds">Time to wait until running AutoResolve().
        /// Defaults to 1 second.</param>
        private static void ScheduleAutoResolve(double delayInMilliseconds = 1000.0) {
            lock (typeof(PlayServicesResolver)) {
                if (!autoResolving) {
                    RunOnMainThread.Cancel(autoResolveJobId);
                    autoResolveJobId = RunOnMainThread.Schedule(
                        () => {
                            lock (typeof(PlayServicesResolver)) {
                                autoResolving = true;
                            }
                            AutoResolve(() => {
                                    lock (typeof(PlayServicesResolver)) {
                                        autoResolving = false;
                                        autoResolveJobId = 0;
                                    }
                                });
                        },
                        delayInMilliseconds);
                }
            }
        }

        /// <summary>
        /// Resolve dependencies if auto-resolution is enabled.
        /// </summary>
        /// <param name="resolutionComplete">Called when resolution is complete.</param>
        private static void AutoResolve(Action resolutionComplete) {
            if (Resolver.AutomaticResolutionEnabled()) {
                ScheduleResolve(
                    false, (success) => {
                        if (resolutionComplete != null) resolutionComplete();
                    }, true);
            } else if (!ExecutionEnvironment.InBatchMode &&
                       GooglePlayServices.SettingsDialog.AutoResolutionDisabledWarning &&
                       PlayServicesSupport.GetAllDependencies().Count > 0) {
                switch (EditorUtility.DisplayDialogComplex(
                    "Warning: Auto-resolution of Android dependencies is disabled!",
                    "Would you like to enable auto-resolution of Android dependencies?\n\n" +
                    "With auto-resolution of Android dependencies disabled you must " +
                    "manually resolve dependencies using the " +
                    "\"Assets > Play Services Resolver > Android Resolver > " +
                    "Resolve\" menu item.\n\nFailure to resolve Android " +
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
                resolutionComplete();
            }
        }

        /// <summary>
        /// Auto-resolve if any packages need to be resolved.
        /// </summary>
        private static void Reresolve() {
            if (Resolver.AutomaticResolutionEnabled()) {
                if (DeleteFiles(Resolver.OnBuildSettings())) ScheduleAutoResolve();
            }
        }

        /// <summary>
        /// Replace attribute values in a tree of XmlElement objects.
        /// </summary>
        /// <param name="node">Element to traverse.</param>
        /// <param name="attributeValueReplacements">Dictionary of attribute values to replace where
        /// each value in the dictionary replaces the manifest attribute value corresponding to
        /// the key in the dictionary.  For example, {"foo", "bar"} results in all instances of
        /// "foo" attribute values being replaced with "bar".  NOTE: Partial replacements are
        /// applied if the attribute value starts with the dictionary key.  For example,
        /// {"com.my.app", "com.another.app"} will change...
        /// * com.my.app.service --> com.another.app.service
        /// * foo.com.my.app.service --> foo.com.my.app.service (unchanged)
        /// </param>
        /// <param name="path">Path of this node in the hierachy of nodes. For example:
        /// given node is "<c>" in "<a><b><c>" this should be "a/b/c".  If this
        /// value is null the name of the current node is used.</param>
        /// <returns>true if any replacements are applied, false otherwise.</returns>
        private static bool ReplaceVariablesInXmlElementTree(
                XmlElement node,
                Dictionary<string, string> attributeValueReplacements,
                string path = null) {
            bool replacementsApplied = false;
            if (path == null) path = node.Name;
            // Build a dictionary of attribute value replacements.
            var attributeNamesAndValues = new Dictionary<string, string>();
            foreach (var attribute in node.Attributes) {
                // Skip non-XmlAttribute objects.
                var xmlAttribute = attribute as XmlAttribute;
                if (xmlAttribute == null) continue;
                var attributeName = xmlAttribute.Name;
                var attributeValue = xmlAttribute.Value;
                foreach (var kv in attributeValueReplacements) {
                    bool isVariable = kv.Key.StartsWith("${") && kv.Key.EndsWith("}");
                    if ((attributeValue.StartsWith(kv.Key) ||
                         (isVariable && attributeValue.Contains(kv.Key))) &&
                        attributeValue != kv.Value) {
                        // If this is performing a variable replacement, replace all instances of
                        // the variable e.g:
                        // If we're doing replacing ${foo} with "baz" and the attribute value is
                        // "fee.${foo}.bar.${foo}" this yields "fee.baz.bar.baz"
                        //
                        // If this is performing a literal value replacement, replace only the
                        // first instance of the value at the start of the string e.g:
                        // If we're replacing a.neat.game.rocks with a.neater.game.rocks and the
                        // attribute value is "a.neat.game.rocks.part1" this yields
                        // "a.neater.game.rocks.part1".
                        attributeNamesAndValues[attributeName] =
                            isVariable ?
                                attributeValue.Replace(kv.Key, kv.Value) :
                                kv.Value + attributeValue.Substring(kv.Key.Length);
                        break;
                    }
                }
            }
            // Replace attribute values.
            foreach (var kv in attributeNamesAndValues) {
                Log(String.Format("Replacing element: {0} attribute: {1} value: {2} --> {3}",
                                  path, kv.Key, node.GetAttribute(kv.Key), kv.Value),
                    level: LogLevel.Verbose);
                node.SetAttribute(kv.Key, kv.Value);
                replacementsApplied = true;
            }
            // Traverse child tree and apply replacements.
            foreach (var child in node.ChildNodes) {
                // Comment elements cannot be cast to XmlElement so ignore them.
                var childElement = child as XmlElement;
                if (childElement == null) continue;
                replacementsApplied |= ReplaceVariablesInXmlElementTree(
                    childElement, attributeValueReplacements,
                    path: path + "/" + childElement.Name);
            }
            return replacementsApplied;
        }

        /// <summary>
        /// Replaces the variables in the AndroidManifest file.
        /// </summary>
        /// <param name="androidManifestPath">Path of the manifest file.</param>
        /// <param name="bundleId">Bundle ID used to replace instances of ${applicationId} in
        /// attribute values.</param>
        /// <param name="attributeValueReplacements">Dictionary of attribute values to replace where
        /// each value in the dictionary replaces the manifest attribute value corresponding to
        /// the key in the dictionary.  For example, {"foo", "bar"} results in all instances of
        /// "foo" attribute values being replaced with "bar".  NOTE: Partial replacements are
        /// applied if the attribute value starts with the dictionary key.  For example,
        /// {"com.my.app", "com.another.app"} will change...
        /// * com.my.app.service --> com.another.app.service
        /// * foo.com.my.app.service --> foo.com.my.app.service (unchanged)
        /// </param>
        internal static void ReplaceVariablesInAndroidManifest(
                string androidManifestPath, string bundleId,
                Dictionary<string, string> attributeValueReplacements) {
            if (!File.Exists(androidManifestPath)) return;
            attributeValueReplacements["${applicationId}"] = bundleId;

            // Read manifest.
            Log(String.Format("Reading AndroidManifest {0}", androidManifestPath),
                level: LogLevel.Verbose);
            var manifest = new XmlDocument();
            using (var stream = new StreamReader(androidManifestPath)) {
                manifest.Load(stream);
            }

            // Log list of replacements that will be applied.
            var replacementsStringList = new List<string>();
            foreach (var kv in attributeValueReplacements) {
                replacementsStringList.Add(String.Format("{0} --> {1}", kv.Key, kv.Value));
            }
            Log(String.Format("Will apply attribute value replacements:\n{0}",
                              String.Join("\n", replacementsStringList.ToArray())),
                level: LogLevel.Verbose);

            // Apply replacements.
            if (ReplaceVariablesInXmlElementTree(manifest.DocumentElement,
                                                 attributeValueReplacements)) {
                // Write out modified XML document.
                using (var xmlWriter = XmlWriter.Create(
                        androidManifestPath,
                        new XmlWriterSettings {
                            Indent = true,
                            IndentChars = "  ",
                            NewLineChars = "\n",
                            NewLineHandling = NewLineHandling.Replace
                        })) {
                    manifest.Save(xmlWriter);
                }
                Log(String.Format("Saved changes to {0}", androidManifestPath),
                    level: LogLevel.Verbose);
            }
        }

        /// <summary>
        /// Apply variable expansion in the AndroidManifest.xml file.
        /// </summary>
        private static void PatchAndroidManifest(string bundleId, string previousBundleId) {
            if (!GooglePlayServices.SettingsDialog.PatchAndroidManifest) return;
            // We only need to patch the manifest in Unity 2018 and above.
            if (Google.VersionHandler.GetUnityVersionMajorMinor() < 2018.0f) return;
            var replacements = new Dictionary<string, string>();
            Log(String.Format("Patch Android Manifest with new bundle ID {0} -> {1}",
                              previousBundleId, bundleId),
                level: LogLevel.Verbose);
            if (!(String.IsNullOrEmpty(previousBundleId) || String.IsNullOrEmpty(bundleId))) {
                replacements[previousBundleId] = bundleId;
            }
            ReplaceVariablesInAndroidManifest(GooglePlayServices.SettingsDialog.AndroidManifestPath,
                                              bundleId, replacements);
        }

        /// <summary>
        /// If the user changes the bundle ID, perform resolution again.
        /// </summary>
        private static void ResolveOnBundleIdChanged(object sender,
                                                     BundleIdChangedEventArgs args) {
            PatchAndroidManifest(args.BundleId, args.PreviousBundleId);
            Reresolve();
        }

        /// <summary>
        /// If the user changes the bundle ID, perform resolution again.
        /// </summary>
        private static void PollBundleId() {
            bundleIdPoller.Poll(UnityCompat.ApplicationId, (previousValue, currentValue) => {
                    if (BundleIdChanged != null) {
                        BundleIdChanged(null, new BundleIdChangedEventArgs {
                                PreviousBundleId = previousValue,
                                BundleId = currentValue
                            });
                    }
                });
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
        private static void PollBuildSystem() {
            androidBuildSystemPoller.Poll(
                AndroidBuildSystemSettings.Current,
                (previousValue, currentValue) => {
                    if (AndroidBuildSystemChanged != null) {
                        AndroidBuildSystemChanged(null, new AndroidBuildSystemChangedArgs {
                                GradleBuildEnabled = currentValue.GradleBuildEnabled,
                                PreviousGradleBuildEnabled = previousValue.GradleBuildEnabled,
                                ProjectExportEnabled = currentValue.ProjectExportEnabled,
                                PreviousProjectExportEnabled = previousValue.ProjectExportEnabled,
                            });
                    }
                });
        }

        /// <summary>
        /// Poll the Android ABIs for changes.
        /// </summary>
        private static void PollAndroidAbis() {
            androidAbisPoller.Poll(AndroidAbis.Current, (previousValue, currentValue) => {
                    if (AndroidAbisChanged != null) {
                        AndroidAbisChanged(null, new AndroidAbisChangedArgs {
                                PreviousAndroidAbis = previousValue.ToString(),
                                AndroidAbis = currentValue.ToString()
                            });
                    }
                });
        }

        /// <summary>
        /// Hide shared libraries from Unity's build system that do not target the currently
        /// selected ABI.
        /// </summary>
        private static void ResolveOnAndroidAbisChanged(
                object sender, AndroidAbisChangedArgs args) {
            Reresolve();
        }

        /// <summary>
        /// Poll the Android SDK path for changes.
        /// </summary>
        private static void PollAndroidSdkRoot() {
            androidSdkRootPoller.Poll(AndroidSdkRoot, (previousValue, currentValue) => {
                    if (AndroidSdkRootChanged != null) {
                        AndroidSdkRootChanged(null, new AndroidSdkRootChangedArgs {
                                PreviousAndroidSdkRoot = previousValue,
                                AndroidSdkRoot = currentValue
                            });
                    }
                });
        }

        /// <summary>
        /// Run Android resolution when the Android SDK path changes.
        /// </summary>
        private static void ResolveOnAndroidSdkRootChange(
                object sender, AndroidSdkRootChangedArgs args) {
            ScheduleResolve(true, null, true);
        }

        /// <summary>
        /// Delete the specified array of files and directories.
        /// </summary>
        /// <param name="filenames">Files or directories to delete.</param>
        /// <returns>true if files are deleted, false otherwise.</returns>
        private static bool DeleteFiles(IEnumerable<string> filenames)
        {
            if (filenames == null) return false;
            bool deletedFiles = false;
            foreach (string artifact in filenames) {
                deletedFiles |= FileUtils.DeleteExistingFileOrDirectory(artifact);
            }
            if (deletedFiles) AssetDatabase.Refresh();
            return deletedFiles;
        }

        /// <summary>
        /// Signal completion of a resolve job.
        /// </summary>
        /// <param name="completion">Action to call.</param>
        private static void SignalResolveJobComplete(Action completion) {
            resolutionJobs.RemoveAll((jobInfo) => { return jobInfo == null; });
            completion();
            ExecuteNextResolveJob();
        }

        /// <summary>
        /// Execute the next resolve job on the queue.
        /// </summary>
        private static void ExecuteNextResolveJob() {
            Action nextJob = null;
            lock (resolutionJobs) {
                while (resolutionJobs.Count > 0) {
                    // Remove any terminators from the queue.
                    var jobInfo = resolutionJobs[0];
                    resolutionJobs.RemoveAt(0);
                    if (jobInfo != null) {
                        nextJob = jobInfo.Job;
                        // Keep an item in the queue to indicate resolution is in progress.
                        resolutionJobs.Add(null);
                        break;
                    }
                }
            }
            if (nextJob != null) nextJob();
        }

        /// <summary>
        /// Resolve dependencies.  If resolution is currently active this queues up the requested
        /// resolution action to execute when the current resolution is complete.
        /// </summary>
        /// <param name="resolutionComplete">Delegate called when resolution is complete.</param>
        /// <param name="forceResolution">Whether resolution should be executed when no dependencies
        /// have changed.  This is useful if a dependency specifies a wildcard in the version
        /// expression.</param>
        /// <param name="resolutionCompleteWithResult">Delegate called when resolution is complete
        /// with a parameter that indicates whether it succeeded or failed.</param>
        public static void Resolve(Action resolutionComplete = null,
                                   bool forceResolution = false,
                                   Action<bool> resolutionCompleteWithResult = null) {
            ScheduleResolve(forceResolution,
                            (success) => {
                                if (resolutionComplete != null) {
                                    resolutionComplete();
                                }
                                if (resolutionCompleteWithResult != null) {
                                    resolutionCompleteWithResult(success);
                                }
                            }, false);
        }

        /// <summary>
        /// Resolve dependencies synchronously.
        /// </summary>
        /// <param name="forceResolution">Whether resolution should be executed when no dependencies
        /// have changed.  This is useful if a dependency specifies a wildcard in the version
        /// expression.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool ResolveSync(bool forceResolution) {
            bool successful = false;
            var completeEvent = new ManualResetEvent(false);
            ScheduleResolve(forceResolution, (success) => {
                    successful = success;
                    completeEvent.Set();
                }, false);
            // We poll from this thread to pump the update queue if the scheduled job isn't
            // executed immediately.
            while (true) {
                RunOnMainThread.TryExecuteAll();
                if (completeEvent.WaitOne(100 /* 100ms poll interval */)) {
                    break;
                }
            }
            return successful;
        }

        /// <summary>
        /// Schedule resolution of dependencies.  If resolution is currently active this queues up
        /// the requested resolution action to execute when the current resolution is complete.
        /// All queued auto-resolution jobs are canceled before enqueuing a new job.
        /// </summary>
        /// <param name="forceResolution">Whether resolution should be executed when no dependencies
        /// have changed.  This is useful if a dependency specifies a wildcard in the version
        /// expression.</param>
        /// <param name="resolutionCompleteWithResult">Delegate called when resolution is complete
        /// with a parameter that indicates whether it succeeded or failed.</param>
        /// <param name="isAutoResolveJob">Whether this is an auto-resolution job.</param>
        private static void ScheduleResolve(bool forceResolution,
                                            Action<bool> resolutionCompleteWithResult,
                                            bool isAutoResolveJob) {
            bool firstJob;
            lock (resolutionJobs) {
                // Remove the scheduled action which enqueues an auto-resolve job.
                RunOnMainThread.Cancel(autoResolveJobId);
                autoResolveJobId = 0;
                // Remove any enqueued auto-resolve jobs.
                resolutionJobs.RemoveAll((jobInfo) => {
                        return jobInfo != null && jobInfo.IsAutoResolveJob;
                    });
                firstJob = resolutionJobs.Count == 0;

                resolutionJobs.Add(
                    new ResolutionJob(
                        isAutoResolveJob,
                        () => {
                            ResolveUnsafe(resolutionComplete: (success) => {
                                    SignalResolveJobComplete(() => {
                                            if (resolutionCompleteWithResult != null) {
                                                resolutionCompleteWithResult(success);
                                            }
                                        });
                                },
                                forceResolution: forceResolution);
                        }));
            }
            if (firstJob) ExecuteNextResolveJob();
        }

        /// <summary>
        /// Resolve dependencies.
        /// </summary>
        /// <param name="resolutionComplete">Delegate called when resolution is complete
        /// with a parameter that indicates whether it succeeded or failed.</param>
        /// <param name="forceResolution">Whether resolution should be executed when no dependencies
        /// have changed.  This is useful if a dependency specifies a wildcard in the version
        /// expression.</param>
        private static void ResolveUnsafe(Action<bool> resolutionComplete = null,
                                          bool forceResolution = false)
        {
            JavaUtilities.CheckJdkForApiLevel();

            DeleteFiles(Resolver.OnBuildSettings());

            xmlDependencies.ReadAll(logger);

            // If no dependencies are present, skip the resolution step.
            if (PlayServicesSupport.GetAllDependencies().Count == 0) {
                if (resolutionComplete != null) {
                    resolutionComplete(true);
                }
                return;
            }

            if (forceResolution) {
                DeleteLabeledAssets();
            } else {
                // Only resolve if user specified dependencies changed or the output files
                // differ to what is present in the project.
                var currentState = DependencyState.GetState();
                var previousState = DependencyState.ReadFromFile();
                if (previousState != null) {
                    if (currentState.Equals(previousState)) {
                        if (resolutionComplete != null) resolutionComplete(true);
                        return;
                    }
                    Log(String.Format("Android dependencies changed from:\n" +
                                      "{0}\n\n" +
                                      "to:\n" +
                                      "{1}\n",
                                      previousState.ToString(),
                                      currentState.ToString()),
                        level: LogLevel.Verbose);
                    // Delete all labeled assets to make sure we don't leave any stale transitive
                    // dependencies in the project.
                    DeleteLabeledAssets();
                }
            }

            System.IO.Directory.CreateDirectory(GooglePlayServices.SettingsDialog.PackageDir);
            Log(String.Format("Resolving the following dependencies:\n{0}\n",
                              String.Join("\n", (new List<string>(
                                  PlayServicesSupport.GetAllDependencies().Keys)).ToArray())),
                level: LogLevel.Verbose);

            lastError = "";
            Resolver.DoResolution(
                svcSupport, GooglePlayServices.SettingsDialog.PackageDir,
                () => {
                    RunOnMainThread.Run(() => {
                            bool succeeded = String.IsNullOrEmpty(lastError);
                            AssetDatabase.Refresh();
                            DependencyState.GetState().WriteToFile();
                            Log(String.Format("Resolution {0}.\n\n{1}",
                                              succeeded ? "Succeeded" : "Failed",
                                              lastError), level: LogLevel.Verbose);
                            if (resolutionComplete != null) {
                                RunOnMainThread.Run(() => { resolutionComplete(succeeded); });
                            }
                        });
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
        /// Interactive resolution of dependencies.
        /// </summary>
        private static void ExecuteMenuResolve(bool forceResolution) {
            if (Resolver == null) {
                NotAvailableDialog();
                return;
            }
            ScheduleResolve(
                forceResolution, (success) => {
                    EditorUtility.DisplayDialog(
                        "Android Dependencies",
                        String.Format("Resolution {0}", success ? "Succeeded" :
                                      "Failed!\n\nYour application will not run, see " +
                                      "the log for details."), "OK");
                }, false);
        }

        /// <summary>
        /// Add a menu item for resolving the jars manually.
        /// </summary>
        [MenuItem("Assets/Play Services Resolver/Android Resolver/Resolve")]
        public static void MenuResolve() {
            ExecuteMenuResolve(false);
        }

        /// <summary>
        /// Add a menu item to force resolve the jars manually.
        /// </summary>
        [MenuItem("Assets/Play Services Resolver/Android Resolver/Force Resolve")]
        public static void MenuForceResolve() {
            ExecuteMenuResolve(true);
        }

        /// <summary>
        /// Called when settings change.
        /// </summary>
        internal static void OnSettingsChanged() {
            if (previousInstallAndroidPackages !=
                GooglePlayServices.SettingsDialog.InstallAndroidPackages) {
                DeleteLabeledAssets();
            }
            previousInstallAndroidPackages =
                GooglePlayServices.SettingsDialog.InstallAndroidPackages;
            PlayServicesSupport.verboseLogging =
                GooglePlayServices.SettingsDialog.VerboseLogging ||
                ExecutionEnvironment.InBatchMode;
            logger.Verbose = GooglePlayServices.SettingsDialog.VerboseLogging;
            if (Resolver != null) {
                PatchAndroidManifest(UnityCompat.ApplicationId, null);
                ScheduleAutoResolve(delayInMilliseconds: 0);
            }
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
                Log(String.Format(
                    "Failed to add tracking label {0} to some assets.\n\n" +
                    "The following files will not be managed by this module:\n" +
                    "{1}\n", ManagedAssetLabel,
                    String.Join("\n", new List<string>(assetsWithoutAssetImporter).ToArray())),
                    level: LogLevel.Warning);
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

        /// <summary>
        /// Delete the full set of assets managed from this plugin.
        /// This is used for uninstalling or switching between resolvers which maintain a different
        /// set of assets.
        /// </summary>
        internal static void DeleteLabeledAssets() {
            DeleteFiles(PlayServicesResolver.FindLabeledAssets());
        }

        /// <summary>
        /// Extract a zip file to the specified directory.
        /// </summary>
        /// <param name="zipFile">Name of the zip file to extract.</param>
        /// <param name="extractFilenames">List of files to extract from the archive.  If this array
        /// is empty or null all files are extracted.</param>
        /// <param name="outputDirectory">Directory to extract the zip file to.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal static bool ExtractZip(string zipFile, string[] extractFilenames,
                                        string outputDirectory) {
            try {
                string zipPath = Path.GetFullPath(zipFile);
                string extractFilesArg = extractFilenames != null && extractFilenames.Length > 0 ?
                    String.Format("\"{0}\"", String.Join("\" \"", extractFilenames)) : "";
                Log(String.Format("Extracting {0} ({1}) to {2}", zipFile, extractFilesArg,
                                  outputDirectory), level: LogLevel.Verbose);
                CommandLine.Result result = CommandLine.Run(
                    JavaUtilities.JarBinaryPath,
                    String.Format(" xvf \"{0}\" {1}", zipPath, extractFilesArg),
                    workingDirectory: outputDirectory);
                if (result.exitCode != 0) {
                    Log(String.Format("Error extracting \"{0}\"\n" +
                                      "{1}", zipPath, result.message), level: LogLevel.Error);
                    return false;
                }
            }
            catch (Exception e) {
                Log(String.Format("Failed with exception {0}", e.ToString()));
                throw e;
            }
            return true;
        }
    }
}

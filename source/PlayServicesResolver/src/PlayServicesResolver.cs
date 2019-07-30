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

namespace GooglePlayServices {
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
    // Unforunately, SettingsDialog is a public method of this object so collides with
    // GooglePlayServices.SettingsDialog when used from this class, so alias this as
    // SettingsDialogObj.
    using SettingsDialogObj = GooglePlayServices.SettingsDialog;


    /// <summary>
    /// Play services resolver.  This is a background post processor
    /// that copies over the Google play services .aar files that
    /// plugins have declared as dependencies.  If the Unity version is less than
    /// 5, aar files are not supported so this class 'explodes' the aar file into
    /// a plugin directory.  Once the version of Unity is upgraded, the exploded
    /// files are removed in favor of the .aar files.
    /// </summary>
    [InitializeOnLoad]
    public class PlayServicesResolver : AssetPostprocessor {
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
            /// Settings used to resolve these dependencies.
            /// </summary>
            public Dictionary<string, string> Settings { get; internal set; }

            /// <summary>
            /// Determine the current state of the project.
            /// </summary>
            /// <returns>DependencyState instance with data derived from the current
            /// project.</returns>
            public static DependencyState GetState() {
                return new DependencyState {
                    Packages = new HashSet<string>(PlayServicesSupport.GetAllDependencies().Keys),
                    Files = new HashSet<string>(PlayServicesResolver.FindLabeledAssets()),
                    Settings = PlayServicesResolver.GetResolutionSettings(),
                };
            }

            /// <summary>
            /// Sort a string hashset.
            /// </summary>
            /// <param name="setToSort">Set to sort and return via an enumerable.</param>
            private static IEnumerable<string> SortSet(IEnumerable<string> setToSort) {
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
                    if (!FileUtils.CheckoutFile(DEPENDENCY_STATE_FILE, logger)) {
                        logger.Log(String.Format(
                            "Unable to checkout '{0}'.  Resolution results can't be saved, " +
                            "disabling auto-resolution.", DEPENDENCY_STATE_FILE), LogLevel.Error);
                        SettingsDialogObj.EnableAutoResolution = false;
                        return;
                    }
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
                        writer.WriteStartElement("settings");
                        foreach (var settingKey in SortSet(Settings.Keys)) {
                            writer.WriteStartElement("setting");
                            writer.WriteAttributeString("name", settingKey);
                            writer.WriteAttributeString("value", Settings[settingKey]);
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
                var settings = new Dictionary<string, string>();
                if (!XmlUtilities.ParseXmlTextFileElements(
                    DEPENDENCY_STATE_FILE, logger,
                    (reader, elementName, isStart, parentElementName, elementNameStack) => {
                        if (isStart) {
                            if (elementName == "dependencies" && parentElementName == "") {
                                return true;
                            } else if ((elementName == "packages" || elementName == "files" ||
                                        elementName == "settings") &&
                                       parentElementName == "dependencies") {
                                return true;
                            } else if (elementName == "package" &&
                                       parentElementName == "packages") {
                                if (reader.Read() && reader.NodeType == XmlNodeType.Text) {
                                    packages.Add(reader.ReadContentAsString());
                                }
                                return true;
                            } else if (elementName == "file" && parentElementName == "files") {
                                if (reader.Read() && reader.NodeType == XmlNodeType.Text) {
                                    files.Add(reader.ReadContentAsString());
                                }
                                return true;
                            } else if (elementName == "setting" &&
                                       parentElementName == "settings") {
                                if (isStart) {
                                    string name = reader.GetAttribute("name");
                                    string value = reader.GetAttribute("value");
                                    if (!String.IsNullOrEmpty(name) &&
                                        !String.IsNullOrEmpty(value)) {
                                        settings[name] = value;
                                    }
                                }
                                return true;
                            }
                        }
                        return false;
                    })) {
                    return null;
                }
                return new DependencyState() {
                    Packages = packages,
                    Files = files,
                    Settings = settings
                };
            }

            /// <summary>
            /// Compare with this object.
            /// </summary>
            /// <param name="obj">Object to compare with.</param>
            /// <returns>true if both objects have the same contents, false otherwise.</returns>
            public override bool Equals(System.Object obj) {
                var state = obj as DependencyState;
                bool settingsTheSame = state != null && Settings.Count == state.Settings.Count;
                if (settingsTheSame) {
                    foreach (var kv in Settings) {
                        string value;
                        settingsTheSame = state.Settings.TryGetValue(kv.Key, out value) &&
                            value == kv.Value;
                        if (!settingsTheSame) break;
                    }
                }
                return settingsTheSame && Packages.SetEquals(state.Packages) &&
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
                foreach (var setting in Settings.Values) {
                    hash ^= setting.GetHashCode();
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
            /// Convert a dictionary to a sorted comma separated string.
            /// </summary>
            /// <returns>Comma separated string in the form key=value.<returns>
            private static string DictionaryToString(Dictionary<string, string> dict) {
                var components = new List<string>();
                foreach (var key in SortSet(dict.Keys)) {
                    components.Add(String.Format("{0}={1}", key, dict[key]));
                }
                return String.Join(", ", components.ToArray());
            }

            /// <summary>
            /// Display dependencies as a string.
            /// </summary>
            /// <returns>Human readable string.</returns>
            public override string ToString() {
                return String.Format("packages=({0}), files=({1}) settings=({2})",
                                     SetToString(Packages), SetToString(Files),
                                     DictionaryToString(Settings));
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

            // Whether the previous value has been initialized.
            private bool previousValueInitialized = false;
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
            /// <param name="propertyName">Name of the property being polled.</param>
            /// <param name="delayTimeInSeconds">Time to wait before signalling that the value
            /// has changed.</param>
            /// <param name="checkIntervalInSeconds">Time to check the value of the property for
            /// changes.<param>
            public PropertyPoller(string propertyName,
                                  int delayTimeInSeconds = 3,
                                  int checkIntervalInSeconds = 1) {
                this.propertyName = propertyName;
                this.delayTimeInSeconds = delayTimeInSeconds;
                this.checkIntervalInSeconds = checkIntervalInSeconds;
            }

            /// <summary>
            /// Poll the specified value for changes.
            /// </summary>
            /// <param name="getCurrentValue">Delegate that returns the value being polled.</param>
            /// <param name="changed">Delegate that is called if the value changes.</param>
            public void Poll(Func<T> getCurrentValue, Changed changed) {
                var currentTime = DateTime.Now;
                if (currentTime.Subtract(previousCheckTime).TotalSeconds <
                    checkIntervalInSeconds) {
                    return;
                }
                previousCheckTime = currentTime;
                T currentValue = getCurrentValue();
                // If the poller isn't initailized, store the current value before polling for
                // changes.
                if (!previousValueInitialized) {
                    previousValueInitialized = true;
                    previousValue = currentValue;
                    return;
                }
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
        /// Namespace for embedded resources packaged from the PlayServicesResolver/scripts
        /// directory.
        /// </summary>
        internal const string EMBEDDED_RESOURCES_NAMESPACE = "PlayServicesResolver.scripts.";

        /// <summary>
        /// The instance to the play services support object.
        /// </summary>
        private static PlayServicesSupport svcSupport;

        /// <summary>
        /// Resolver that uses Gradle to download libraries and embed them within a Unity project.
        /// </summary>
        private static GradleResolver gradleResolver;

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
        private static PropertyPoller<string> bundleIdPoller =
            new PropertyPoller<string>("Bundle ID");

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
        /// Asset label applied to files managed by this plugin.
        /// </summary>
        private const string ManagedAssetLabel = "gpsr";

        /// <summary>
        /// Get a boolean property from UnityEditor.EditorUserBuildSettings.
        /// </summary>
        /// Properties are introduced over successive versions of Unity so use reflection to
        /// retrieve them.
        private static object GetEditorUserBuildSettingsProperty(string name,
                                                                 object defaultValue) {
            var editorUserBuildSettingsType = typeof(UnityEditor.EditorUserBuildSettings);
            var property = editorUserBuildSettingsType.GetProperty(name);
            if (property != null) {
                var value = property.GetValue(null, null);
                if (value != null) return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Whether the Gradle build system is enabled.
        /// </summary>
        public static bool GradleBuildEnabled {
            get {
                return GetEditorUserBuildSettingsProperty(
                    "androidBuildSystem", "").ToString().Equals("Gradle");
            }
        }

        /// <summary>
        /// Whether the Gradle template is enabled.
        /// </summary>
        public static bool GradleTemplateEnabled {
            get {
                return GradleBuildEnabled && File.Exists(GradleTemplateResolver.GradleTemplatePath);
            }
        }


        // Backing store for GradleVersion property.
        private static string gradleVersion = null;
        // Extracts a version number from a gradle distribution jar file.
        private static Regex gradleJarVersionExtract = new Regex(@"^gradle-core-([0-9.]+)\.jar$");

        /// <summary>
        /// Get / set the Gradle version.
        /// This property is populated when it's first read by parsing the version number of the
        /// gradle-core-*.jar in the AndroidPlayer directory.
        /// </summary>
        public static string GradleVersion {
            set { gradleVersion = value; }
            get {
                if (!String.IsNullOrEmpty(gradleVersion)) return gradleVersion;
                var engineDir = AndroidPlaybackEngineDirectory;
                if (String.IsNullOrEmpty(engineDir)) return null;

                var gradleLibDir =
                    Path.Combine(Path.Combine(Path.Combine(engineDir, "Tools"), "gradle"), "lib");
                if (Directory.Exists(gradleLibDir)) {
                    foreach (var path in Directory.GetFiles(gradleLibDir, "gradle-core-*.jar",
                                                            SearchOption.TopDirectoryOnly)) {
                        var match = gradleJarVersionExtract.Match(Path.GetFileName(path));
                        if (match != null && match.Success) {
                            gradleVersion = match.Result("$1");
                            break;
                        }
                    }
                }
                return gradleVersion;
            }
        }

        // Backing store for the AndroidGradlePluginVersion property.
        private static string androidGradlePluginVersion = null;
        // Modification time of mainTemplate.gradle the last time it was searched for the Android
        // Gradle plugin version.
        private static DateTime mainTemplateLastWriteTime = default(DateTime);
        // Extracts an Android Gradle Plugin version number from the contents of a *.gradle file.
        private static Regex androidGradlePluginVersionExtract = new Regex(
            @"['""]com\.android\.tools\.build:gradle:([^']+)['""]$");

        /// <summary>
        /// Get the Android Gradle Plugin version used by Unity.
        /// </summary>
        public static string AndroidGradlePluginVersion {
            set {
                androidGradlePluginVersion = value;
                mainTemplateLastWriteTime = DateTime.Now;
            }
            get {
                // If the gradle template changed, read the plugin version again.
                var mainTemplateGradlePath = GradleTemplateResolver.GradleTemplatePath;
                if (File.Exists(mainTemplateGradlePath)) {
                    var lastWriteTime = File.GetLastWriteTime(mainTemplateGradlePath);
                    if (lastWriteTime.CompareTo(mainTemplateLastWriteTime) > 0) {
                        androidGradlePluginVersion = null;
                    }
                }
                // If the plugin version is cached, return it.
                if (!String.IsNullOrEmpty(androidGradlePluginVersion)) {
                    return androidGradlePluginVersion;
                }
                // Search the gradle templates for the plugin version.
                var engineDir = AndroidPlaybackEngineDirectory;
                if (String.IsNullOrEmpty(engineDir)) return null;
                var gradleTemplateDir =
                    Path.Combine(Path.Combine(engineDir, "Tools"), "GradleTemplates");
                if (Directory.Exists(gradleTemplateDir)) {
                    var gradleTemplates = new List<string>();
                    if (File.Exists(mainTemplateGradlePath)) {
                        gradleTemplates.Add(mainTemplateGradlePath);
                    }
                    gradleTemplates.AddRange(Directory.GetFiles(gradleTemplateDir, "*.gradle",
                                                                SearchOption.TopDirectoryOnly));
                    foreach (var path in gradleTemplates) {
                        foreach (var line in File.ReadAllLines(path)) {
                            var match = androidGradlePluginVersionExtract.Match(line);
                            if (match != null && match.Success) {
                                androidGradlePluginVersion = match.Result("$1");
                                break;
                            }
                        }
                        if (!String.IsNullOrEmpty(androidGradlePluginVersion)) break;
                    }
                }
                Log(String.Format("Detected Android Gradle Plugin Version {0}.",
                                  androidGradlePluginVersion),
                    level: LogLevel.Verbose);
                return androidGradlePluginVersion;
            }
        }

        /// <summary>
        /// Whether project export is enabled.
        /// </summary>
        public static bool ProjectExportEnabled {
            get {
                var value = GetEditorUserBuildSettingsProperty("exportAsGoogleAndroidProject",
                    null);
                return value == null ? false : (bool) value;
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
            /// Whether the Gradle template is enabled.
            /// </summary>
            public bool GradleTemplateEnabled { get; private set; }

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
                        GradleTemplateEnabled = PlayServicesResolver.GradleTemplateEnabled,
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
                    other.GradleTemplateEnabled == GradleTemplateEnabled &&
                    other.ProjectExportEnabled == ProjectExportEnabled;
            }

            /// <summary>
            /// Generate a hash of this object.
            /// </summary>
            /// <returns>Hash of this object.</returns>
            public override int GetHashCode() {
                return GradleBuildEnabled.GetHashCode() ^ GradleTemplateEnabled.GetHashCode() ^
                    ProjectExportEnabled.GetHashCode();
            }


            /// <summary>
            /// Convert this object to a string.
            /// </summary>
            /// <returns>String representation.</returns>
            public override string ToString() {
                return String.Format("[GradleBuildEnabled={0} GradleTemplateEnabled={1} " +
                                     "ProjectExportEnabled={2}]",
                                     GradleBuildEnabled, GradleTemplateEnabled,
                                     ProjectExportEnabled);
            }
        }

        /// <summary>
        /// Polls for changes in build system settings.
        /// </summary>
        private static PropertyPoller<AndroidBuildSystemSettings> androidBuildSystemPoller =
            new PropertyPoller<AndroidBuildSystemSettings>("Android Build Settings");

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
            /// Whether a custom Gradle template is enabled.
            /// This will only be true if GradleBuildEnabled is also true.
            /// </summary>
            public bool GradleTemplateEnabled { get; set; }

            /// <summary>
            /// Whether a custom Gradle template was enabled the last time this event was fired.
            /// </summary>
            public bool PreviousGradleTemplateEnabled { get; set; }

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
            new PropertyPoller<AndroidAbis>("Android Target Device ABI");

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
            get {
                var sdkPath = EditorPrefs.GetString("AndroidSdkRoot");
                // Unity 2019.x added installation of the Android SDK in the AndroidPlayer directory
                // so fallback to searching for it there.
                if (String.IsNullOrEmpty(sdkPath) || EditorPrefs.GetBool("SdkUseEmbedded")) {
                    var androidPlayerDir = AndroidPlaybackEngineDirectory;
                    if (!String.IsNullOrEmpty(androidPlayerDir)) {
                        var androidPlayerSdkDir = Path.Combine(androidPlayerDir, "SDK");
                        if (Directory.Exists(androidPlayerSdkDir)) sdkPath = androidPlayerSdkDir;
                    }
                }
                return sdkPath;
            }
        }

        /// <summary>
        /// Polls for changes in AndroidSdkRoot.
        /// </summary>
        private static PropertyPoller<string> androidSdkRootPoller =
            new PropertyPoller<string>("Android SDK Path");

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

        // Backing store to cache AndroidPlaybackEngineDirectory.
        // This is set to either null (Android player not installed) or the path of the
        // playback engine directory when AndroidPlaybackEngineDirectory is first accessed.
        private static string androidPlaybackEngineDirectory = "";

        /// <summary>
        /// Get the Android playback engine directory.
        /// </summary>
        /// <returns>Get the playback engine directory.</returns>
        public static string AndroidPlaybackEngineDirectory {
            get {
                if (androidPlaybackEngineDirectory != null &&
                    androidPlaybackEngineDirectory == "") {
                    try {
                        androidPlaybackEngineDirectory =
                            (string)VersionHandler.InvokeStaticMethod(
                                typeof(BuildPipeline), "GetPlaybackEngineDirectory",
                                new object[] { BuildTarget.Android, BuildOptions.None });
                    } catch (Exception) {
                        androidPlaybackEngineDirectory = null;
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                            if (assembly.GetName().Name == "UnityEditor.Android.Extensions") {
                                androidPlaybackEngineDirectory =
                                    Path.GetDirectoryName(assembly.Location);
                                break;
                            }
                        }
                    }
                }
                return androidPlaybackEngineDirectory;
            }
        }

        // Backing store for the GradleWrapper property.
        private static GradleWrapper gradleWrapper = new GradleWrapper(
            typeof(PlayServicesResolver).Assembly,
            PlayServicesResolver.EMBEDDED_RESOURCES_NAMESPACE + "gradle-template.zip",
            Path.Combine(FileUtils.ProjectTemporaryDirectory, "PlayServicesResolverGradle"));

        /// <summary>
        /// Class to interface with the embedded Gradle wrapper.
        /// </summary>
        internal static GradleWrapper Gradle { get { return gradleWrapper; } }

        /// <summary>
        /// Returns true if automatic resolution is enabled.
        /// Auto-resolution is never enabled in batch mode.  Each build setting change must be
        /// manually followed by DoResolution().
        /// </summary>
        public static bool AutomaticResolutionEnabled {
            get {
                return SettingsDialogObj.EnableAutoResolution &&
                    !ExecutionEnvironment.InBatchMode;
            }
        }

        /// <summary>
        /// Initializes the <see cref="GooglePlayServices.PlayServicesResolver"/> class.
        /// </summary>
        static PlayServicesResolver() {
            // Create the resolver.
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) {
                gradleResolver = new GradleResolver();
                // Monitor Android dependency XML files to perform auto-resolution.
                AddAutoResolutionFilePatterns(xmlDependencies.fileRegularExpressions);

                svcSupport = PlayServicesSupport.CreateInstance(
                    "PlayServicesResolver",
                    AndroidSdkRoot,
                    "ProjectSettings",
                    logMessageWithLevel: LogDelegate);
            }
            RunOnMainThread.OnUpdate -= PollBundleId;
            RunOnMainThread.OnUpdate += PollBundleId;

            // Initialize settings and resolve if required.
            OnSettingsChanged();

            // Setup events for auto resolution.
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) {
                BundleIdChanged += ResolveOnBundleIdChanged;
                AndroidBuildSystemChanged += ResolveOnBuildSystemChanged;
                AndroidAbisChanged += ResolveOnAndroidAbisChanged;
                AndroidSdkRootChanged += ResolveOnAndroidSdkRootChange;
                Reresolve();

                if (SettingsDialogObj.EnableAutoResolution) LinkAutoResolution();
            }

        }

        // Unregister events to monitor build system changes for the Android Resolver and other
        // plugins.
        public static void UnlinkAutoResolution() {
            RunOnMainThread.OnUpdate -= PollBuildSystem;
            RunOnMainThread.OnUpdate -= PollAndroidAbis;
            RunOnMainThread.OnUpdate -= PollAndroidSdkRoot;
        }

        // Register events to monitor build system changes for the Android Resolver and other
        // plugins.
        public static void LinkAutoResolution() {
            UnlinkAutoResolution();
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
        /// Get the application ID for the Android build target.
        /// </summary>
        /// <returns>Application / bundle ID for the Android build target.</returns>
        internal static string GetAndroidApplicationId() {
            return UnityCompat.GetApplicationId(BuildTarget.Android);
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
        /// Patterns of files that are monitored to trigger auto resolution.
        /// </summary>
        private static HashSet<Regex> autoResolveFilePatterns = new HashSet<Regex>();

        /// <summary>
        /// Add file patterns to monitor to trigger auto resolution.
        /// </summary>
        /// <param name="patterns">Set of file patterns to monitor to trigger auto
        /// resolution.</param>
        public static void AddAutoResolutionFilePatterns(IEnumerable<Regex> patterns) {
            autoResolveFilePatterns.UnionWith(patterns);
        }

        /// <summary>
        /// Utility function to check a set of files to see whether resolution should be
        /// triggered.
        /// </summary>
        /// <value>True if auto-resolve was triggered.</value>
        private static bool CheckFilesForAutoResolution(HashSet<string> filesToCheck) {
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
            if (resolve) Reresolve();

            return resolve;
        }

        /// <summary>
        /// Called by Unity when all assets have been updated. This
        /// is used to kick off resolving the dependencies declared.
        /// </summary>
        /// <param name="importedAssets">Imported assets. (unused)</param>
        /// <param name="deletedAssets">Deleted assets. (unused)</param>
        /// <param name="movedAssets">Moved assets. (unused)</param>
        /// <param name="movedFromAssetPaths">Moved from asset paths. (unused)</param>
        private static void OnPostprocessAllAssets(string[] importedAssets,
                                                   string[] deletedAssets,
                                                   string[] movedAssets,
                                                   string[] movedFromAssetPaths) {
            if (gradleResolver != null) {
                // If the manifest changed, try patching it.
                var manifestPath = FileUtils.NormalizePathSeparators(
                    SettingsDialogObj.AndroidManifestPath);
                foreach (var importedAsset in importedAssets) {
                    if (FileUtils.NormalizePathSeparators(importedAsset) == manifestPath) {
                        PatchAndroidManifest(GetAndroidApplicationId(), null);
                        break;
                    }
                }

                if (AutomaticResolutionEnabled) {
                    // If anything has been removed from the packaging directory schedule
                    // resolution.
                    foreach (string asset in deletedAssets) {
                        if (asset.StartsWith(SettingsDialogObj.PackageDir)) {
                            Reresolve();
                            return;
                        }
                    }
                    // Schedule a check of imported assets.
                    if (importedAssets.Length > 0 && autoResolveFilePatterns.Count > 0) {
                        if (CheckFilesForAutoResolution(new HashSet<string>(importedAssets))) {
                            return;
                        }
                    }
                    // Check deleted assets to see if we need to trigger an auto-resolve.
                    if (deletedAssets.Length > 0 && autoResolveFilePatterns.Count > 0) {
                        if (CheckFilesForAutoResolution(new HashSet<string>(deletedAssets))) {
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Number of scenes processed by OnPostProcessScene() since this DLL was loaded into the
        /// app domain.
        /// This is present so that it's possible to easily log the number of times
        /// OnPostProcessScene() is called when building for the selected target platform.
        /// If this value ends up larger than the total number of scenes included in the build
        /// then the behavior of PostProcessSceneAttribute has changed and should be investigated.
        /// If this value continues to increase between each build for a target platform then
        /// Unity's behavior has changed such that this module is no longer being reloaded in the
        /// app domain so we can't rely upon this method of detecting the first scene in the build.
        /// </summary>
        private static int scenesProcessed = 0;

        /// <summary>
        /// If auto-resolution is enabled, run resolution synchronously before building the
        /// application.
        /// </summary>
        [UnityEditor.Callbacks.PostProcessSceneAttribute(0)]
        private static void OnPostProcessScene() {
            // If we're in the editor play mode, do nothing.
            if (UnityEngine.Application.isPlaying) return;
            // If the Android resolver isn't enabled or automatic resolution is disabled,
            // do nothing.
            if (gradleResolver == null || !SettingsDialogObj.AutoResolveOnBuild) {
                return;
            }
            // If post-processing has already been executed since this module was loaded, don't
            // do so again.
            scenesProcessed++;
            if (scenesProcessed > 1) return;
            Log("Starting auto-resolution before scene build...", level: LogLevel.Verbose);
            bool success = ResolveSync(false, true);
            Log(String.Format("Android resolution {0}.", success ? "succeeded" : "failed"),
                    level: LogLevel.Verbose);
        }

        /// <summary>
        /// Schedule auto-resolution.
        /// </summary>
        /// <param name="delayInMilliseconds">Time to wait until running AutoResolve().
        /// Defaults to 1 second.</param>
        private static void ScheduleAutoResolve(double delayInMilliseconds = 1000.0) {
            lock (typeof(PlayServicesResolver)) {
                if (autoResolving) {
                    return;
                }

                RunOnMainThread.Cancel(autoResolveJobId);
                autoResolveJobId = RunOnMainThread.Schedule(() => {
                    lock (typeof(PlayServicesResolver)) {
                        autoResolving = true;
                    }

                    int delaySec = GooglePlayServices.SettingsDialog.AutoResolutionDelay;
                    DateTimeOffset resolveTime = DateTimeOffset.Now.AddSeconds(delaySec);
                    bool shouldResolve = true;
                    RunOnMainThread.PollOnUpdateUntilComplete(() => {
                        // Only run AutoResolve() if we have a valid autoResolveJobId.
                        // If autoResolveJobId is 0, ScheduleResolve()
                        // has already been run and we should not run AutoResolve()
                        // again.
                        if(autoResolveJobId == 0)
                            return true;

                        DateTimeOffset now  = DateTimeOffset.Now;
                        if (resolveTime > now && PlayServicesResolver.AutomaticResolutionEnabled) {
                            float countDown = (float)(resolveTime - now).TotalSeconds;
                            if(EditorUtility.DisplayCancelableProgressBar("Skip dependency?","Auto Resolve Dependency in : " + (int)countDown,countDown / delaySec)) {
                                resolveTime = now;
                                shouldResolve   = false;
                            }

                            return false;
                        }
                        
                        EditorUtility.ClearProgressBar();

                        if (EditorApplication.isCompiling) return false;
                        if (shouldResolve) {
                            AutoResolve(() => {
                                lock (typeof(PlayServicesResolver)) {
                                    autoResolving = false;
                                    autoResolveJobId = 0;
                                }
                            });
                        }
                        return true;
                    });
                },delayInMilliseconds);
            }
        }

        /// <summary>
        /// Resolve dependencies if auto-resolution is enabled.
        /// </summary>
        /// <param name="resolutionComplete">Called when resolution is complete.</param>
        private static void AutoResolve(Action resolutionComplete) {
            if (AutomaticResolutionEnabled) {
                ScheduleResolve(
                    false, false, (success) => {
                        if (resolutionComplete != null) resolutionComplete();
                    }, true);
            }
            else if (!ExecutionEnvironment.InBatchMode &&
                     SettingsDialogObj.AutoResolutionDisabledWarning &&
                     PlayServicesSupport.GetAllDependencies().Count > 0) {
                Debug.LogWarning("Warning: Auto-resolution of Android dependencies is disabled! " +
                                 "Ensure you have manually run the resolver before building." +
                                 "\n\nWith auto-resolution of Android dependencies disabled you " +
                                 "must manually resolve dependencies using the " +
                                 "\"Assets > Play Services Resolver > Android Resolver > " +
                                 "Resolve\" menu item.\n\nFailure to resolve Android " +
                                 "dependencies will result in an non-functional " +
                                 "application.\n\nTo enable auto-resolution, navigate to " +
                                 "\"Assets > Play Services Resolver > Android Resolver > " +
                                 "Settings\" and check \"Enable Auto-resolution\"");
                resolutionComplete();
            }
        }

        /// <summary>
        /// Auto-resolve if any packages need to be resolved.
        /// </summary>
        private static void Reresolve() {
            if (AutomaticResolutionEnabled && gradleResolver != null) {
                ScheduleAutoResolve();
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
        /// <param name="path">Path of this node in the hierarchy of nodes. For example:
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
            if (!SettingsDialogObj.PatchAndroidManifest) return;
            // We only need to patch the manifest in Unity 2018 and above.
            if (Google.VersionHandler.GetUnityVersionMajorMinor() < 2018.0f) return;
            var replacements = new Dictionary<string, string>();
            Log(String.Format("Patch Android Manifest with new bundle ID {0} -> {1}",
                              previousBundleId, bundleId),
                level: LogLevel.Verbose);
            if (!(String.IsNullOrEmpty(previousBundleId) || String.IsNullOrEmpty(bundleId))) {
                replacements[previousBundleId] = bundleId;
            }
            ReplaceVariablesInAndroidManifest(SettingsDialogObj.AndroidManifestPath,
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
            bundleIdPoller.Poll(() => GetAndroidApplicationId(), (previousValue, currentValue) => {
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
                () => AndroidBuildSystemSettings.Current,
                (previousValue, currentValue) => {
                    if (AndroidBuildSystemChanged != null) {
                        AndroidBuildSystemChanged(null, new AndroidBuildSystemChangedArgs {
                                GradleBuildEnabled = currentValue.GradleBuildEnabled,
                                PreviousGradleBuildEnabled = previousValue.GradleBuildEnabled,
                                GradleTemplateEnabled = currentValue.GradleTemplateEnabled,
                                PreviousGradleTemplateEnabled = previousValue.GradleTemplateEnabled,
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
            androidAbisPoller.Poll(() => AndroidAbis.Current, (previousValue, currentValue) => {
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
            androidSdkRootPoller.Poll(() => AndroidSdkRoot, (previousValue, currentValue) => {
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
            ScheduleResolve(true, false, null, true);
        }

        /// <summary>
        /// Delete the specified array of files and directories.
        /// </summary>
        /// <param name="filenames">Files or directories to delete.</param>
        /// <returns>true if files are deleted, false otherwise.</returns>
        private static bool DeleteFiles(IEnumerable<string> filenames)
        {
            if (filenames == null) return false;
            var failedToDelete = new List<string>();
            foreach (string artifact in filenames) {
                failedToDelete.AddRange(FileUtils.DeleteExistingFileOrDirectory(artifact));
            }
            var deleteError = FileUtils.FormatError("Failed to delete files:", failedToDelete);
            if (!String.IsNullOrEmpty(deleteError)) {
                Log(deleteError, level: LogLevel.Warning);
            }
            bool deletedFiles = failedToDelete.Count != (new List<string>(filenames)).Count;
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
            ScheduleResolve(forceResolution, false,
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
        /// Wait for a ManualResetEvent to complete.
        /// </summary>
        /// <param name="eventToPoll">Event to poll until it's complete.</param>
        private static void PollManualResetEvent(ManualResetEvent eventToPoll) {
            // We poll from this thread to pump the update queue.
            while (true) {
                RunOnMainThread.TryExecuteAll();
                if (eventToPoll.WaitOne(100 /* 100ms poll interval */)) {
                    break;
                }
            }
        }

        /// <summary>
        /// Resolve dependencies synchronously.
        /// </summary>
        /// <param name="forceResolution">Whether resolution should be executed when no dependencies
        /// have changed.  This is useful if a dependency specifies a wildcard in the version
        /// expression.</param>
        /// <param name="closeWindowOnCompletion">Whether to unconditionally close the resolution
        /// window when complete.</param>
        /// <returns>true if successful, false otherwise.</returns>
        private static bool ResolveSync(bool forceResolution, bool closeWindowOnCompletion) {
            bool successful = false;
            var completeEvent = new ManualResetEvent(false);
            ScheduleResolve(forceResolution, closeWindowOnCompletion, (success) => {
                    successful = success;
                    completeEvent.Set();
                }, false);
            PollManualResetEvent(completeEvent);
            return successful;
        }

        /// <summary>
        /// Resolve dependencies synchronously.
        /// </summary>
        /// <param name="forceResolution">Whether resolution should be executed when no dependencies
        /// have changed.  This is useful if a dependency specifies a wildcard in the version
        /// expression.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool ResolveSync(bool forceResolution) {
            return ResolveSync(forceResolution, false);
        }

        /// <summary>
        /// Remove libraries references from Gradle template files and patch POM files to work
        /// with the Gradle template.
        /// </summary>
        private static void DeleteResolvedLibrariesFromGradleTemplate() {
            LocalMavenRepository.PatchPomFilesInLocalRepos(
                PlayServicesSupport.GetAllDependencies().Values);
            if (GradleTemplateEnabled) {
                GradleTemplateResolver.InjectDependencies(new List<Dependency>());
            }
        }

        /// <summary>
        /// Delete all resolved libraries asynchronously.
        /// </summary>
        /// <param name="complete">Delegate called when delete is complete.</param>
        public static void DeleteResolvedLibraries(System.Action complete = null) {
            RunOnMainThread.Schedule(() => {
                    if (AutomaticResolutionEnabled) {
                        Log("Disabling auto-resolution to prevent libraries from being " +
                            "resolved after deletion.", level: LogLevel.Warning);
                        SettingsDialogObj.EnableAutoResolution = false;
                    }
                    DeleteLabeledAssets();
                    DeleteResolvedLibrariesFromGradleTemplate();
                    if (complete != null) complete();
                }, 0);
        }

        /// <summary>
        /// Delete all resolved libraries synchronously.
        /// </summary>
        public static void DeleteResolvedLibrariesSync() {
            var completeEvent = new ManualResetEvent(false);
            DeleteResolvedLibraries(complete: () => { completeEvent.Set(); });
            PollManualResetEvent(completeEvent);
        }

        /// <summary>
        /// Schedule resolution of dependencies.  If resolution is currently active this queues up
        /// the requested resolution action to execute when the current resolution is complete.
        /// All queued auto-resolution jobs are canceled before enqueuing a new job.
        /// </summary>
        /// <param name="forceResolution">Whether resolution should be executed when no dependencies
        /// have changed.  This is useful if a dependency specifies a wildcard in the version
        /// expression.</param>
        /// <param name="closeWindowOnCompletion">Whether to unconditionally close the resolution
        /// window when complete.</param>
        /// <param name="resolutionCompleteWithResult">Delegate called when resolution is complete
        /// with a parameter that indicates whether it succeeded or failed.</param>
        /// <param name="isAutoResolveJob">Whether this is an auto-resolution job.</param>
        private static void ScheduleResolve(bool forceResolution, bool closeWindowOnCompletion,
                                            Action<bool> resolutionCompleteWithResult,
                                            bool isAutoResolveJob) {
            bool firstJob;
            lock (resolutionJobs) {
                // Remove the scheduled action which enqueues an auto-resolve job.
                RunOnMainThread.Cancel(autoResolveJobId);
                autoResolveJobId = 0;
                // Remove any enqueued auto-resolve jobs.
                resolutionJobs.RemoveAll((jobInfo) => jobInfo == null || jobInfo.IsAutoResolveJob);
                firstJob = resolutionJobs.Count == 0;

                resolutionJobs.Add(
                    new ResolutionJob(
                        isAutoResolveJob,
                        () => {
                            ResolveUnsafe(
                                (success) => {
                                    SignalResolveJobComplete(() => {
                                            if (resolutionCompleteWithResult != null) {
                                                resolutionCompleteWithResult(success);
                                            }
                                        });
                                },
                                forceResolution,
                                isAutoResolveJob,
                                closeWindowOnCompletion);
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
        /// <param name="isAutoResolveJob">Whether this is an auto-resolution job.</param>
        /// <param name="closeWindowOnCompletion">Whether to unconditionally close the resolution
        /// window when complete.</param>
        private static void ResolveUnsafe(Action<bool> resolutionComplete,
                                          bool forceResolution, bool isAutoResolveJob,
                                          bool closeWindowOnCompletion) {
            JavaUtilities.CheckJdkForApiLevel();
            CanEnableJetifierOrPromptUser("");

            // If the internal build system is being used and AAR explosion is disabled the build
            // is going to fail so warn and enable explosion.
            if (!AndroidBuildSystemSettings.Current.GradleBuildEnabled &&
                !SettingsDialogObj.ExplodeAars) {
                Log("AAR explosion *must* be enabled when the internal build " +
                    "system is selected, otherwise the build will very likely fail. " +
                    "Enabling the 'explode AARs' setting.", level: LogLevel.Warning);
                SettingsDialogObj.ExplodeAars = true;
            }

            xmlDependencies.ReadAll(logger);

            // If no dependencies are present, skip the resolution step.
            if (PlayServicesSupport.GetAllDependencies().Count == 0) {
                Log("No dependencies found.", level: LogLevel.Verbose);
                if (PlayServicesResolver.FindLabeledAssets() != null) {
                    Log("Stale dependencies exist. Deleting assets...", level: LogLevel.Verbose);
                    DeleteLabeledAssets();
                }
                DeleteResolvedLibrariesFromGradleTemplate();
                if (resolutionComplete != null) {
                    resolutionComplete(true);
                }
                return;
            }

            // If we are not in auto-resolution mode and not in batch mode
            // prompt the user to see if they want to resolve dependencies
            // now or later.
            if (SettingsDialogObj.PromptBeforeAutoResolution &&
                isAutoResolveJob &&
                !ExecutionEnvironment.InBatchMode) {
                bool shouldResolve = false;
                AlertModal alert = new AlertModal {
                    Title = "Enable Android Auto-resolution?",
                    Message = "The Play Services Resolver has detected a change " +
                              " and would to resolve conflicts and download Android dependencies." +
                              "\n\n\"Disable Auto-Resolution\" will require manually " +
                              "running resolution using \"Assets > Play Services Resolver " +
                              "> Android Resolver > Resolve\" menu item. Failure to " +
                              "resolve Android dependencies will result " +
                              "in an non-functional application." +
                              "\n\nEnable auto-resolution again via " +
                              "\"Assets > Play Services Resolver " +
                              "> Android Resolver > Settings.",
                    Ok = new AlertModal.LabeledAction {
                        Label = "Enable",
                        DelegateAction = () => {
                            shouldResolve = true;
                            SettingsDialogObj.PromptBeforeAutoResolution = false;
                        }
                    },
                    Cancel = new AlertModal.LabeledAction {
                        Label = "Disable",
                        DelegateAction = () => {
                            SettingsDialogObj.EnableAutoResolution = false;
                            SettingsDialogObj.PromptBeforeAutoResolution = false;
                            shouldResolve = false;
                        }
                    }
                };

                alert.Display();

                if (!shouldResolve) {
                    if (resolutionComplete != null) {
                        resolutionComplete(false);
                    }

                    return;
                }
            }

            if (forceResolution) {
                Log("Forcing resolution...", level: LogLevel.Verbose);
                DeleteLabeledAssets();
            } else {
                Log("Checking for changes from previous resolution...", level: LogLevel.Verbose);
                // Only resolve if user specified dependencies changed or the output files
                // differ to what is present in the project.
                var currentState = DependencyState.GetState();
                var previousState = DependencyState.ReadFromFile();
                if (previousState != null) {
                    if (currentState.Equals(previousState)) {
                        Log("No changes found, resolution skipped.", level: LogLevel.Verbose);
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
                } else {
                    Log("Failed to parse previous resolution state, running resolution...",
                        level: LogLevel.Verbose);
                }
            }

            System.IO.Directory.CreateDirectory(SettingsDialogObj.PackageDir);
            Log(String.Format("Resolving the following dependencies:\n{0}\n",
                              String.Join("\n", (new List<string>(
                                  PlayServicesSupport.GetAllDependencies().Keys)).ToArray())),
                level: LogLevel.Verbose);

            // Writes the current dependency state and reports whether resolution was successful.
            Action<bool, string> finishResolution = (bool succeeded, string error) => {
                AssetDatabase.Refresh();
                DependencyState.GetState().WriteToFile();
                Log(String.Format("Resolution {0}.\n\n{1}",
                                  succeeded ? "Succeeded" : "Failed",
                                  error), level: LogLevel.Verbose);
                if (resolutionComplete != null) {
                    RunOnMainThread.Run(() => { resolutionComplete(succeeded); });
                }
            };

            // If a gradle template is present but patching is disabled, remove managed libraries
            // from the template.
            if (GradleTemplateEnabled &&
                !SettingsDialogObj.PatchMainTemplateGradle) {
                DeleteResolvedLibrariesFromGradleTemplate();
            }

            if (GradleTemplateEnabled &&
                SettingsDialogObj.PatchMainTemplateGradle) {
                RunOnMainThread.Run(() => {
                        finishResolution(GradleTemplateResolver.InjectDependencies(
                            PlayServicesSupport.GetAllDependencies().Values), "");
                    });
            } else {
                lastError = "";
                gradleResolver.DoResolution(
                    SettingsDialogObj.PackageDir,
                    closeWindowOnCompletion,
                    () => {
                        RunOnMainThread.Run(() => {
                                finishResolution(String.IsNullOrEmpty(lastError), lastError);
                            });
                    });
            }
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
        /// Link to the documentation.
        /// </summary>
        [MenuItem("Assets/Play Services Resolver/Android Resolver/Documentation")]
        public static void OpenDocumentation() {
            Application.OpenURL(VersionHandlerImpl.DocumentationUrl("#android-resolver-usage"));
        }

        /// <summary>
        /// Add a menu item for resolving the jars manually.
        /// </summary>
        [MenuItem("Assets/Play Services Resolver/Android Resolver/Settings")]
        public static void SettingsDialog()
        {
            SettingsDialog window = (SettingsDialog)EditorWindow.GetWindow(
                typeof(SettingsDialog), true, "Android Resolver Settings");
            window.Initialize();
            window.Show();
        }

        /// <summary>
        /// Interactive resolution of dependencies.
        /// </summary>
        private static void ExecuteMenuResolve(bool forceResolution) {
            if (gradleResolver == null) {
                NotAvailableDialog();
                return;
            }
            ScheduleResolve(
                forceResolution, false, (success) => {
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
        /// Add a menu item to clear all resolved libraries.
        /// </summary>
        [MenuItem("Assets/Play Services Resolver/Android Resolver/Delete Resolved Libraries")]
        public static void MenuDeleteResolvedLibraries() {
            DeleteResolvedLibrariesSync();
        }

        /// <summary>
        /// If dependencies is specified return the value, otherwise refresh from the project and
        /// return the parsed dependencies.
        /// </summary>
        /// <returns>List of Android library dependencies.</returns>
        private static IEnumerable<Dependency> GetOrReadDependencies(
                IEnumerable<Dependency> dependencies) {
            if (dependencies == null) {
                xmlDependencies.ReadAll(logger);
                dependencies = PlayServicesSupport.GetAllDependencies().Values;
            }
            return dependencies;
        }

        /// <summary>
        /// Get the list of Android package specs referenced by the project and the sources they're
        /// loaded from.
        /// </summary>
        /// <returns>List of package spec, source pairs.</returns>
        public static IList<KeyValuePair<string, string>> GetPackageSpecs(
                IEnumerable<Dependency> dependencies = null) {
            return new List<KeyValuePair<string, string>>(new SortedList<string, string>(
                GradleResolver.DependenciesToPackageSpecs(GetOrReadDependencies(dependencies))));
        }

        /// <summary>
        /// Get the list of Maven repo URIs required for Android libraries in this project.
        /// </summary>
        /// <returns>List of repo, source pairs.</returns>
        public static IList<KeyValuePair<string, string>> GetRepos(
                IEnumerable<Dependency> dependencies = null) {
            return GradleResolver.DependenciesToRepoUris(GetOrReadDependencies(dependencies));
        }

        /// <summary>
        /// Get the included dependency repos as lines that can be included in a Gradle file.
        /// </summary>
        /// <returns>Lines that can be included in a gradle file.</returns>
        internal static IList<string> GradleMavenReposLines(ICollection<Dependency> dependencies) {
            var lines = new List<string>();
            if (dependencies.Count > 0) {
                var exportEnabled = GradleProjectExportEnabled;
                var projectPath = FileUtils.PosixPathSeparators(Path.GetFullPath("."));
                var projectFileUri = GradleResolver.RepoPathToUri(projectPath);
                lines.Add("([rootProject] + (rootProject.subprojects as List)).each { project ->");
                lines.Add("    project.repositories {");
                // projectPath will point to the Unity project root directory as Unity will
                // generate the root Gradle project in "Temp/gradleOut" when *not* exporting a
                // gradle project.
                lines.Add(String.Format(
                          "        def unityProjectPath = \"{0}\" + " +
                          "file(rootProject.projectDir.path + \"/../../\").absolutePath",
                          GradleWrapper.FILE_SCHEME));
                lines.Add("        maven {");
                lines.Add("            url \"https://maven.google.com\"");
                lines.Add("        }");
                foreach (var repoAndSources in GetRepos(dependencies: dependencies)) {
                    string repoUri;
                    if (repoAndSources.Key.StartsWith(projectFileUri) && !exportEnabled) {
                        repoUri = String.Format(
                            "(unityProjectPath + \"/{0}\")",
                            repoAndSources.Key.Substring(projectFileUri.Length + 1));
                    } else {
                        repoUri = String.Format("\"{0}\"", repoAndSources.Key);
                    }
                    lines.Add("        maven {");
                    lines.Add(String.Format("            url {0} // {1}", repoUri,
                                            repoAndSources.Value));
                    lines.Add("        }");
                }
                lines.Add("        mavenLocal()");
                lines.Add("        jcenter()");
                lines.Add("        mavenCentral()");
                lines.Add("    }");
                lines.Add("}");
            }
            return lines;
        }

        /// <summary>
        /// Get the included dependencies as lines that can be included in a Gradle file.
        /// </summary>
        /// <param name="dependencies">Set of dependencies to convert to package specs.</param>
        /// <param name="includeDependenciesBlock">Whether to include the "dependencies {" block
        /// scope in the returned list.</param>
        /// <returns>Lines that can be included in a gradle file.</returns>
        internal static IList<string> GradleDependenciesLines(
                ICollection<Dependency> dependencies, bool includeDependenciesBlock = true) {
            var lines = new List<string>();
            if (dependencies.Count > 0) {
                // Select the appropriate dependency include statement based upon the Gradle
                // version.  "implementation" was introduced in Gradle 3.4 that is used by the
                // Android Gradle plugin 3.0.0 and newer:
                // https://docs.gradle.org/3.4/release-notes.html#the-java-library-plugin
                // https://developer.android.com/studio/releases/gradle-plugin#3-0-0
                var version = GradleVersion;
                var includeStatement =
                    !String.IsNullOrEmpty(version) &&
                    (new Dependency.VersionComparer()).Compare("3.4", version) >= 0 ?
                    "implementation" : "compile";
                if (includeDependenciesBlock) lines.Add("dependencies {");
                foreach (var packageSpecAndSources in GetPackageSpecs(dependencies: dependencies)) {
                    lines.Add(String.Format(
                        "    {0} '{1}' // {2}", includeStatement, packageSpecAndSources.Key,
                        packageSpecAndSources.Value));
                }
                if (includeDependenciesBlock) lines.Add("}");
            }
            return lines;
        }

        // Extracts the ABI from a native library path in an AAR.
        // In an AAR, native libraries are under the jni directory, in an APK they're placed under
        // the lib directory.
        private static Regex nativeLibraryPath = new Regex(@"^jni/([^/]+)/.*\.so$");

        /// <summary>
        /// Get the Android packaging options as lines that can be included in a Gradle file.
        /// </summary>
        internal static IList<string> PackagingOptionsLines(ICollection<Dependency> dependencies) {
            var lines = new List<string>();
            if (dependencies.Count > 0) {
                var currentAbis = AndroidAbis.Current.ToSet();
                var excludeFiles = new HashSet<string>();
                // Support for wildcard based excludes were added in Android Gradle plugin 3.0.0
                // which requires Gradle 4.1+.  Also, version 2.3.0 was locked to Gradle 2.1.+ so
                // it's possible to infer the Android Gradle plugin from the Gradle version.
                var version = GradleVersion;
                var wildcardExcludesSupported =
                    !String.IsNullOrEmpty(version) &&
                    (new Dependency.VersionComparer()).Compare("4.1", version) >= 0;
                if (wildcardExcludesSupported) {
                    var allAbis = new HashSet<string>(AndroidAbis.AllSupported);
                    allAbis.ExceptWith(currentAbis);
                    foreach (var abi in allAbis) {
                        excludeFiles.Add(String.Format("/lib/{0}/**", abi));
                    }
                } else {
                    // Android Gradle plugin 2.x only supported exclusion of packaged files using
                    // APK relative paths.
                    foreach (var aar in LocalMavenRepository.FindAarsInLocalRepos(dependencies)) {
                        foreach (var path in ListZip(aar)) {
                            var posixPath = FileUtils.PosixPathSeparators(path);
                            var match = nativeLibraryPath.Match(posixPath);
                            if (match != null && match.Success) {
                                var abi = match.Result("$1");
                                if (!currentAbis.Contains(abi)) {
                                    excludeFiles.Add(
                                        String.Format(
                                            "lib/{0}", posixPath.Substring("jni/".Length)));
                                }
                            }
                        }
                    }
                }
                if (excludeFiles.Count > 0) {
                    var sortedExcludeFiles = new List<string>(excludeFiles);
                    sortedExcludeFiles.Sort();
                    lines.Add("android {");
                    lines.Add("  packagingOptions {");
                    foreach (var filename in sortedExcludeFiles) {
                        // Unity's Android extension replaces ** in the template with an empty
                        // string presumably due to the token expansion it performs.  It's not
                        // possible to escape the expansion so we workaround it by concatenating
                        // strings.
                        lines.Add(String.Format("      exclude ('{0}')",
                                                filename.Replace("**", "*' + '*")));
                    }
                    lines.Add("  }");
                    lines.Add("}");
                }
            }
            return lines;
        }

        /// <summary>
        /// Display the set of dependncies / libraries currently included in the project.
        /// This prints out the set of libraries in a form that can be easily included in a Gradle
        /// script.  This does not resolve dependency conflicts, it simply displays what is included
        /// by plugins in the project.
        /// </summary>
        [MenuItem("Assets/Play Services Resolver/Android Resolver/Display Libraries")]
        public static void MenuDisplayLibraries() {
            xmlDependencies.ReadAll(logger);
            var dependencies = PlayServicesSupport.GetAllDependencies().Values;

            var lines = new List<string>();
            lines.AddRange(GradleMavenReposLines(dependencies));
            lines.AddRange(GradleDependenciesLines(dependencies));
            lines.AddRange(PackagingOptionsLines(dependencies));
            var dependenciesString = String.Join("\n", lines.ToArray());
            Log(dependenciesString);
            var window = TextAreaDialog.CreateTextAreaDialog("Android Libraries");
            window.bodyText = dependenciesString;
            window.Show();
        }

        /// <summary>
        /// Label a set of assets that should be managed by this plugin.
        /// </summary>
        /// <param name="assetPaths">Set of assets to label.</param>
        /// <param name="complete">Called when the operation is complete with the set of assets
        /// that could not be labeled.</param>
        /// <param name="synchronous">Whether to block until asset labeling is complete.</param>
        /// <param name="progressUpdate">Called with the progress (0..1) and message that indicates
        /// processing progress.</param>
        /// <param name="displayWarning">Whether to display a warning if assets can't be
        /// labeled.</param>
        /// <param name="recursive">Whether to label assets in subdirectories of the specified
        /// assetPaths.</param>
        /// <returns></returns>
        internal static void LabelAssets(IEnumerable<string> assetPaths,
                                         Action<HashSet<string>> complete = null,
                                         bool synchronous = true,
                                         Action<float, string> progressUpdate = null,
                                         bool displayWarning = true,
                                         bool recursive = false) {
            var assetsWithoutAssetImporter = new HashSet<string>();
            var projectDataFolder = Path.GetFullPath(Application.dataPath);
            var assetsToProcess = new List<string>(assetPaths);
            int totalAssets = assetsToProcess.Count;
            RunOnMainThread.PollOnUpdateUntilComplete(() => {
                    var remainingAssets = assetsToProcess.Count;
                    // Processing is already complete.
                    if (remainingAssets == 0) return true;
                    var assetPath = assetsToProcess[0];
                    assetsToProcess.RemoveAt(0);
                    // Ignore asset meta files which are used to store the labels and files that
                    // are not in the project.
                    var fullAssetPath = Path.GetFullPath(assetPath);
                    if (assetPath.EndsWith(".meta") ||
                        !fullAssetPath.StartsWith(projectDataFolder)) {
                        return false;
                    }

                    // Get the relative path of this asset.
                    var relativeAssetPath = Path.Combine(
                        Path.GetFileName(projectDataFolder),
                        fullAssetPath.Substring(projectDataFolder.Length +1));

                    if (progressUpdate != null) {
                        progressUpdate((float)(totalAssets - remainingAssets) /
                                       (float)totalAssets, relativeAssetPath);
                    }

                    // If the asset is a directory, add labels to the contents.
                    if (recursive && Directory.Exists(relativeAssetPath)) {
                        var contents = new List<string>(
                            Directory.GetFileSystemEntries(relativeAssetPath));
                        totalAssets += contents.Count;
                        assetsToProcess.AddRange(contents);
                        return false;
                    }

                    // It's likely files have been added or removed without using AssetDatabase
                    // methods so (re)import the asset to make sure it's in the AssetDatabase.
                    AssetDatabase.ImportAsset(relativeAssetPath,
                                              options: ImportAssetOptions.ForceSynchronousImport);

                    // Add the label to the asset.
                    AssetImporter importer = AssetImporter.GetAtPath(relativeAssetPath);
                    if (importer != null) {
                        var labels = new HashSet<string>(AssetDatabase.GetLabels(importer));
                        labels.Add(ManagedAssetLabel);
                        AssetDatabase.SetLabels(importer, (new List<string>(labels)).ToArray());
                    } else {
                        assetsWithoutAssetImporter.Add(assetPath);
                    }

                    // Display summary of processing and call the completion function.
                    if (assetsToProcess.Count == 0) {
                        if (assetsWithoutAssetImporter.Count > 0 && displayWarning) {
                            Log(String.Format(
                                "Failed to add tracking label {0} to some assets.\n\n" +
                                "The following files will not be managed by this module:\n" +
                                "{1}\n", ManagedAssetLabel,
                                String.Join(
                                    "\n", new List<string>(assetsWithoutAssetImporter).ToArray())),
                                level: LogLevel.Warning);
                        }
                        if (complete != null) complete(assetsWithoutAssetImporter);
                        return true;
                    }
                    return false;
                }, synchronous: synchronous);
        }

        /// <summary>
        /// Find the set of assets managed by this plugin.
        /// </summary>
        internal static IEnumerable<string> FindLabeledAssets() {
            return VersionHandlerImpl.SearchAssetDatabase("l:" + ManagedAssetLabel);
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
        /// Called when settings change.
        /// </summary>
        internal static void OnSettingsChanged() {
            PlayServicesSupport.verboseLogging =
                SettingsDialogObj.VerboseLogging ||
                ExecutionEnvironment.InBatchMode;
            logger.Verbose = SettingsDialogObj.VerboseLogging;
            if (gradleResolver != null) {
                PatchAndroidManifest(GetAndroidApplicationId(), null);
                Reresolve();
            }
        }

        /// <summary>
        /// Retrieves the current value of settings that should cause resolution if they change.
        /// </summary>
        /// <returns>Map of name to value for settings that affect resolution.</returns>
        internal static Dictionary<string, string> GetResolutionSettings() {
            var buildSystemSettings = AndroidBuildSystemSettings.Current;
            var androidAbis = AndroidAbis.Current;
            return new Dictionary<string, string> {
                {"installAndroidPackages", SettingsDialogObj.InstallAndroidPackages.ToString()},
                {"packageDir", SettingsDialogObj.PackageDir.ToString()},
                {"explodeAars", SettingsDialogObj.ExplodeAars.ToString()},
                {"patchAndroidManifest", SettingsDialogObj.PatchAndroidManifest.ToString()},
                {"patchMainTemplateGradle", SettingsDialogObj.PatchMainTemplateGradle.ToString()},
                {"useJetifier", SettingsDialogObj.UseJetifier.ToString()},
                {"bundleId", GetAndroidApplicationId()},
                {"gradleBuildEnabled", buildSystemSettings.GradleBuildEnabled.ToString()},
                {"gradleTemplateEnabled", buildSystemSettings.GradleTemplateEnabled.ToString()},
                {"projectExportEnabled", buildSystemSettings.ProjectExportEnabled.ToString()},
                {"androidAbis", androidAbis.ToString()},
            };
        }

        // Matches Jetpack (AndroidX) library filenames.
        private static Regex androidXLibrary = new Regex(@"^androidx\..*\.(jar|aar)$");

        /// <summary>
        /// Determine whether a list of files contains Jetpack libraries.
        /// </summary>
        /// <returns>true if any files are androidx libraries, false otherwise.</returns>
        internal static bool FilesContainJetpackLibraries(IEnumerable<string> filenames) {
            foreach (var path in filenames) {
                var match = androidXLibrary.Match(Path.GetFileName(path));
                if (match != null && match.Success) return true;
            }
            return false;
        }

        /// <summary>
        /// Prompt the user to change Unity's build settings, if the Jetifier is enabled but not
        /// supported with Unity's current build settings.
        /// </summary>
        /// <param name="titlePrefix">Prefix added to dialogs shown by this method.</param>
        /// <returns>true if the Jetifier is enabled, false otherwise</returns>
        internal static bool CanEnableJetifierOrPromptUser(string titlePrefix) {
            bool useJetifier = SettingsDialogObj.UseJetifier;
            if (!useJetifier || ExecutionEnvironment.InBatchMode) return useJetifier;
            // Minimum Android Gradle Plugin required to use the Jetifier.
            const string MinimumAndroidGradlePluginVersionForJetifier = "3.2.0";
            if (useJetifier && GradleTemplateEnabled && SettingsDialogObj.PatchMainTemplateGradle) {
                var version = AndroidGradlePluginVersion;
                if ((new Dependency.VersionComparer()).Compare(
                        MinimumAndroidGradlePluginVersionForJetifier, version) < 0) {
                    switch (EditorUtility.DisplayDialogComplex(
                        titlePrefix + "Enable Jetifier?",
                        String.Format(
                            "Jetifier for Jetpack (AndroidX) libraries is only " +
                            "available with Android Gradle Plugin (AGP) version {0}. " +
                            "This Unity installation uses version {1} which does not include the " +
                            "Jetifier and therefore will not apply transformations to change " +
                            "all legacy Android Support Library references to use Jetpack " +
                            "(AndroidX).\n\n" +
                            "It's possible to use the Jetifier on Android Resolver managed " +
                            "dependencies by disabling mainTemplate.gradle patching.",
                            MinimumAndroidGradlePluginVersionForJetifier, version),
                        "Disable Jetifier", "Ignore", "Disable mainTemplate.gradle patching")) {
                        case 0:  // Disable Jetifier
                            useJetifier = false;
                            break;
                        case 1:  // Ignore
                            break;
                        case 2:  // Disable mainTemplate.gradle patching
                            SettingsDialogObj.PatchMainTemplateGradle = false;
                            break;
                    }
                }
            }

            // Minimum target Android API level required to use Jetpack / AndroidX.
            const int MinimumApiLevelForJetpack = 28;
            int apiLevel = UnityCompat.GetAndroidTargetSDKVersion();
            if (useJetifier && apiLevel < MinimumApiLevelForJetpack) {
                switch (EditorUtility.DisplayDialogComplex(
                    titlePrefix + "Enable Jetpack?",
                    String.Format(
                        "Jetpack (AndroidX) libraries are only supported when targeting Android " +
                        "API {0} and above.  The currently selected target API level is {1}.\n\n" +
                        "Would you like to set the project's target API level to {0}?",
                        MinimumApiLevelForJetpack,
                        apiLevel > 0 ? apiLevel.ToString() : "auto (max. installed)"),
                    "Yes", "No", "Disable Jetifier")) {
                    case 0:  // Yes
                        bool setSdkVersion = UnityCompat.SetAndroidTargetSDKVersion(
                            MinimumApiLevelForJetpack);
                        if (!setSdkVersion) {
                            // Get the highest installed SDK version to see whether it's
                            // suitable.
                            if (UnityCompat.FindNewestInstalledAndroidSDKVersion() >=
                                MinimumApiLevelForJetpack) {
                                // Set the mode to "auto" to use the latest installed
                                // version.
                                setSdkVersion = UnityCompat.SetAndroidTargetSDKVersion(-1);
                            }
                        }
                        if (!setSdkVersion) {
                            PlayServicesResolver.Log(
                                String.Format(
                                    "Failed to set the Android Target API level to {0}, " +
                                    "disabled the Jetifier.", MinimumApiLevelForJetpack),
                                level: LogLevel.Error);
                            useJetifier = false;
                        }
                        break;
                    case 1:  // No
                        // Don't change the settings but report that AndroidX will not work.
                        return false;
                    case 2:  // Disable Jetifier
                        useJetifier = false;
                        break;
                }
            }
            SettingsDialogObj.UseJetifier = useJetifier;
            return useJetifier;
        }

        /// <summary>
        /// List the contents of a zip file.
        /// </summary>
        /// <param name="zipFile">Name of the zip file to query.</param>
        /// <returns>List of zip file contents.</returns>
        internal static IEnumerable<string> ListZip(string zipFile) {
            var contents = new List<string>();
            try {
                string zipPath = Path.GetFullPath(zipFile);
                Log(String.Format("Listing {0}", zipFile), level: LogLevel.Verbose);
                CommandLine.Result result = CommandLine.Run(
                    JavaUtilities.JarBinaryPath, String.Format(" tf \"{0}\"", zipPath));
                if (result.exitCode == 0) {
                    foreach (var path in CommandLine.SplitLines(result.stdout)) {
                        contents.Add(FileUtils.PosixPathSeparators(path));
                    }
                } else {
                    Log(String.Format("Error listing \"{0}\"\n" +
                                      "{1}", zipPath, result.message), level: LogLevel.Error);
                }
            }
            catch (Exception e) {
                Log(String.Format("Failed with exception {0}", e.ToString()));
                throw e;
            }
            return contents;
        }

        /// <summary>
        /// Extract a zip file to the specified directory.
        /// </summary>
        /// <param name="zipFile">Name of the zip file to extract.</param>
        /// <param name="extractFilenames">Enumerable of files to extract from the archive. If this
        /// array is empty or null all files are extracted.</param>
        /// <param name="outputDirectory">Directory to extract the zip file to.</param>
        /// <param name="update">If true, this will only extract the zip file if the target paths
        /// are older than the source paths. If this is false or no extractFilenames are specified
        /// this method will always extract files from the zip file overwriting the target
        /// files.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal static bool ExtractZip(string zipFile, IEnumerable<string> extractFilenames,
                                        string outputDirectory, bool update) {
            try {
                string zipPath = Path.GetFullPath(zipFile);
                var zipFileModificationTime = File.GetLastWriteTime(zipPath);
                string extractFilesArg = "";
                if (extractFilenames != null) {
                    bool outOfDate = !update;
                    if (update) {
                        foreach (var filename in extractFilenames) {
                            var path = Path.Combine(outputDirectory, filename);
                            if (!File.Exists(path) ||
                                zipFileModificationTime.CompareTo(
                                    File.GetLastWriteTime(path)) > 0) {
                                outOfDate = true;
                                break;
                            }
                        }
                    }
                    // If everything is up to date there is nothing to do.
                    if (!outOfDate) return true;
                    extractFilesArg = String.Format("\"{0}\"", String.Join("\" \"",
                                          (new List<string>(extractFilenames)).ToArray()));
                }
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

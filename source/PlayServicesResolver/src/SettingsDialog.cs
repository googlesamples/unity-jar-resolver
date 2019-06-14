// <copyright file="SettingsDialog.cs" company="Google Inc.">
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
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using Google;

    /// <summary>
    /// Settings dialog for PlayServices Resolver.
    /// </summary>
    public class SettingsDialog : EditorWindow {
        /// <summary>
        /// Loads / saves settings for this dialog.
        /// </summary>
        private class Settings {
            internal bool enableAutoResolution;
            internal bool autoResolveOnBuild;
            internal bool useGradleDaemon;
            internal bool installAndroidPackages;
            internal string packageDir;
            internal bool explodeAars;
            internal bool patchAndroidManifest;
            internal bool patchMainTemplateGradle;
            internal bool useJetifier;
            internal bool verboseLogging;
            internal bool autoResolutionDisabledWarning;
            internal bool promptBeforeAutoResolution;
            internal bool useProjectSettings;

            /// <summary>
            /// Load settings into the dialog.
            /// </summary>
            internal Settings() {
                enableAutoResolution = SettingsDialog.EnableAutoResolution;
                autoResolveOnBuild = SettingsDialog.AutoResolveOnBuild;
                useGradleDaemon = SettingsDialog.UseGradleDaemon;
                installAndroidPackages = SettingsDialog.InstallAndroidPackages;
                packageDir = SettingsDialog.PackageDir;
                explodeAars = SettingsDialog.ExplodeAars;
                patchAndroidManifest = SettingsDialog.PatchAndroidManifest;
                patchMainTemplateGradle = SettingsDialog.PatchMainTemplateGradle;
                useJetifier = SettingsDialog.UseJetifier;
                verboseLogging = SettingsDialog.VerboseLogging;
                autoResolutionDisabledWarning = SettingsDialog.AutoResolutionDisabledWarning;
                promptBeforeAutoResolution = SettingsDialog.PromptBeforeAutoResolution;
                useProjectSettings = SettingsDialog.UseProjectSettings;
            }

            /// <summary>
            /// Save dialog settings to preferences.
            /// </summary>
            internal void Save() {
                SettingsDialog.UseGradleDaemon = useGradleDaemon;
                SettingsDialog.EnableAutoResolution = enableAutoResolution;
                SettingsDialog.AutoResolveOnBuild = autoResolveOnBuild;
                SettingsDialog.InstallAndroidPackages = installAndroidPackages;
                if (SettingsDialog.ConfigurablePackageDir) SettingsDialog.PackageDir = packageDir;
                SettingsDialog.ExplodeAars = explodeAars;
                SettingsDialog.PatchAndroidManifest = patchAndroidManifest;
                SettingsDialog.PatchMainTemplateGradle = patchMainTemplateGradle;
                SettingsDialog.UseJetifier = useJetifier;
                SettingsDialog.VerboseLogging = verboseLogging;
                SettingsDialog.AutoResolutionDisabledWarning = autoResolutionDisabledWarning;
                SettingsDialog.PromptBeforeAutoResolution = promptBeforeAutoResolution;
                SettingsDialog.UseProjectSettings = useProjectSettings;
            }
        }

        const string Namespace = "GooglePlayServices.";
        private const string AutoResolveKey = Namespace + "AutoResolverEnabled";
        private const string AutoResolveOnBuildKey = Namespace + "AutoResolveOnBuild";
        private const string PackageInstallKey = Namespace + "AndroidPackageInstallationEnabled";
        private const string PackageDirKey = Namespace + "PackageDirectory";
        private const string ExplodeAarsKey = Namespace + "ExplodeAars";
        private const string PatchAndroidManifestKey = Namespace + "PatchAndroidManifest";
        private const string PatchMainTemplateGradleKey = Namespace + "PatchMainTemplateGradle";
        private const string UseJetifierKey = Namespace + "UseJetifier";
        private const string VerboseLoggingKey = Namespace + "VerboseLogging";
        private const string AutoResolutionDisabledWarningKey =
            Namespace + "AutoResolutionDisabledWarning";
        private const string PromptBeforeAutoResolutionKey =
            Namespace + "PromptBeforeAutoResolution";
        private const string UseGradleDaemonKey = Namespace + "UseGradleDaemon";

        // List of preference keys, used to restore default settings.
        private static string[] PreferenceKeys = new[] {
            AutoResolveKey,
            AutoResolveOnBuildKey,
            PackageInstallKey,
            PackageDirKey,
            ExplodeAarsKey,
            PatchAndroidManifestKey,
            PatchMainTemplateGradleKey,
            UseJetifierKey,
            VerboseLoggingKey,
            AutoResolutionDisabledWarningKey,
            PromptBeforeAutoResolutionKey,
            UseGradleDaemonKey
        };

        internal const string AndroidPluginsDir = "Assets/Plugins/Android";

        // Unfortunately, Unity currently does not search recursively search subdirectories of
        // AndroidPluginsDir for Android library plugins.  When this is supported - or we come up
        // with a workaround - this can be enabled.
        static bool ConfigurablePackageDir = false;
        static string DefaultPackageDir = AndroidPluginsDir;

        private Settings settings;

        private static ProjectSettings projectSettings = new ProjectSettings(Namespace);

        // Previously validated package directory.
        private static string previouslyValidatedPackageDir;

        private Vector2 scrollPosition = new Vector2(0, 0);

        /// <summary>
        /// Reset settings of this plugin to default values.
        /// </summary>
        internal static void RestoreDefaultSettings() {
            projectSettings.DeleteKeys(PreferenceKeys);
        }

        internal static bool EnableAutoResolution {
            set {
                projectSettings.SetBool(AutoResolveKey, value);
                if (value) {
                    PlayServicesResolver.LinkAutoResolution();
                } else {
                    PlayServicesResolver.UnlinkAutoResolution();
                }
            }
            get { return projectSettings.GetBool(AutoResolveKey, true); }
        }

        internal static bool AutoResolveOnBuild {
            set {
                projectSettings.SetBool(AutoResolveOnBuildKey, value);
            }
            get { return projectSettings.GetBool(AutoResolveOnBuildKey, true); }
        }

        internal static bool UseGradleDaemon {
            private set { projectSettings.SetBool(UseGradleDaemonKey, value); }
            get { return projectSettings.GetBool(UseGradleDaemonKey, false); }
        }

        internal static bool InstallAndroidPackages {
            private set { projectSettings.SetBool(PackageInstallKey, value); }
            get { return projectSettings.GetBool(PackageInstallKey, true); }
        }


        internal static string PackageDir {
            private set { projectSettings.SetString(PackageDirKey, value); }
            get {
                return ValidatePackageDir(
                    ConfigurablePackageDir ?
                        (projectSettings.GetString(PackageDirKey, DefaultPackageDir)) :
                        DefaultPackageDir);
            }
        }

        internal static bool AutoResolutionDisabledWarning {
            set { projectSettings.SetBool(AutoResolutionDisabledWarningKey, value); }
            get { return projectSettings.GetBool(AutoResolutionDisabledWarningKey, true); }
        }

        /// <summary>
        /// This setting is not exposed in the Settings menu but is
        /// leveraged by the PlayServicesResolver to determine whether to
        /// display a prompt.
        /// </summary>
        internal static bool PromptBeforeAutoResolution {
            set {
                projectSettings.SetBool(PromptBeforeAutoResolutionKey, value);
            }
            get { return projectSettings.GetBool(PromptBeforeAutoResolutionKey, true); }
        }

        internal static bool UseProjectSettings {
            get { return projectSettings.UseProjectSettings; }
            set { projectSettings.UseProjectSettings = value; }
        }

        // Whether AARs that use variable expansion should be exploded when Gradle builds are
        // enabled.
        internal static bool ExplodeAars {
            set { projectSettings.SetBool(ExplodeAarsKey, value); }
            get { return projectSettings.GetBool(ExplodeAarsKey, true); }
        }

        internal static string AndroidManifestPath {
            get { return Path.Combine(PackageDir, "AndroidManifest.xml"); }
        }

        internal static bool PatchAndroidManifest {
            set { projectSettings.SetBool(PatchAndroidManifestKey, value); }
            get { return projectSettings.GetBool(PatchAndroidManifestKey, true); }
        }

        internal static bool PatchMainTemplateGradle {
            set { projectSettings.SetBool(PatchMainTemplateGradleKey, value); }
            get { return projectSettings.GetBool(PatchMainTemplateGradleKey, true); }
        }

        internal static bool UseJetifier {
            set { projectSettings.SetBool(UseJetifierKey, value); }
            get { return projectSettings.GetBool(UseJetifierKey, false); }
        }

        internal static bool VerboseLogging {
            private set { projectSettings.SetBool(VerboseLoggingKey, value); }
            get { return projectSettings.GetBool(VerboseLoggingKey, false); }
        }

        internal static string ValidatePackageDir(string directory) {
            // Make sure the package directory starts with the same name.
            // This is case insensitive to handle cases where developers rename Unity
            // project directories on Windows (which has a case insensitive file system by
            // default) then they use the project on OSX / Linux.
            if (!directory.ToLowerInvariant().StartsWith(AndroidPluginsDir.ToLower())) {
                directory = AndroidPluginsDir;
            }
            directory = FileUtils.NormalizePathSeparators(directory);
            var searchDirectory = FileUtils.FindDirectoryByCaseInsensitivePath(directory);
            if (String.IsNullOrEmpty(searchDirectory)) searchDirectory = directory;
            if (directory != searchDirectory &&
                (previouslyValidatedPackageDir == null ||
                 searchDirectory != previouslyValidatedPackageDir)) {
                PlayServicesResolver.Log(
                    String.Format("Resolving to Android package directory {0} instead of the " +
                                  "requested target directory {1}\n" +
                                  "\n" +
                                  "Is {0} in a different case to {1} ?\n",
                                  searchDirectory, directory), level: LogLevel.Warning);
                directory = searchDirectory;
            } else if ((previouslyValidatedPackageDir == null ||
                        searchDirectory != previouslyValidatedPackageDir) &&
                       searchDirectory == null) {
                PlayServicesResolver.Log(
                    String.Format("Android package directory {0} not found.",
                                  directory), level: LogLevel.Warning);
            }
            previouslyValidatedPackageDir = searchDirectory;

            return directory;
        }

        public void Initialize() {
            minSize = new Vector2(425, 455);
            position = new Rect(UnityEngine.Screen.width / 3, UnityEngine.Screen.height / 3,
                                minSize.x, minSize.y);
        }

        public void LoadSettings() {
            settings = new Settings();
        }

        public void OnEnable() {
            LoadSettings();
        }

        /// <summary>
        /// Called when the GUI should be rendered.
        /// </summary>
        public void OnGUI() {
            GUILayout.BeginVertical();
            GUILayout.Label(String.Format("Android Resolver (version {0}.{1}.{2})",
                                          AndroidResolverVersionNumber.Value.Major,
                                          AndroidResolverVersionNumber.Value.Minor,
                                          AndroidResolverVersionNumber.Value.Build));
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUI.skin.label.wordWrap = true;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Use Gradle Daemon", EditorStyles.boldLabel);
            settings.useGradleDaemon = EditorGUILayout.Toggle(settings.useGradleDaemon);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                settings.useGradleDaemon ?
                ("Gradle Daemon will be used to fetch dependencies.  " +
                 "This is faster but can be flakey in some environments.") :
                ("Gradle Daemon will not be used.  This is slow but reliable."));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Enable Auto-Resolution", EditorStyles.boldLabel);
            settings.enableAutoResolution = EditorGUILayout.Toggle(settings.enableAutoResolution);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                settings.enableAutoResolution ?
                ("Android libraries will be downloaded and processed in the editor.") :
                ("Android libraries will *not* be downloaded or processed in the editor."));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Enable Resolution On Build", EditorStyles.boldLabel);
            settings.autoResolveOnBuild = EditorGUILayout.Toggle(settings.autoResolveOnBuild);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                settings.autoResolveOnBuild ?
                ("Android libraries will be downloaded and processed in a pre-build step.") :
                ("Android libraries will *not* be downloaded or processed in a pre-build step."));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Install Android Packages", EditorStyles.boldLabel);
            settings.installAndroidPackages =
                EditorGUILayout.Toggle(settings.installAndroidPackages);
            GUILayout.EndHorizontal();

            if (ConfigurablePackageDir) {
                GUILayout.BeginHorizontal();
                string previousPackageDir = settings.packageDir;
                GUILayout.Label("Package Directory", EditorStyles.boldLabel);
                if (GUILayout.Button("Browse")) {
                    string path = EditorUtility.OpenFolderPanel("Set Package Directory",
                                                                PackageDir, "");
                    int startOfPath = path.IndexOf(AndroidPluginsDir);
                    settings.packageDir = startOfPath < 0 ? "" :
                        path.Substring(startOfPath, path.Length - startOfPath);;
                }
                if (!previousPackageDir.Equals(settings.packageDir)) {
                    settings.packageDir = ValidatePackageDir(settings.packageDir);
                }
                GUILayout.EndHorizontal();
                settings.packageDir = EditorGUILayout.TextField(settings.packageDir);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Explode AARs", EditorStyles.boldLabel);
            settings.explodeAars = EditorGUILayout.Toggle(settings.explodeAars);
            GUILayout.EndHorizontal();
            if (settings.explodeAars) {
                GUILayout.Label("AARs will be exploded (unpacked) when ${applicationId} " +
                                "variable replacement is required in an AAR's " +
                                "AndroidManifest.xml or a single target ABI is selected " +
                                "without a compatible build system.");
            } else {
                GUILayout.Label("AAR explosion will be disabled in exported Gradle builds " +
                                "(Unity 5.5 and above). You will need to set " +
                                "android.defaultConfig.applicationId to your bundle ID in your " +
                                "build.gradle to generate a functional APK.");
            }

            // Disable the ability to toggle the auto-resolution disabled warning
            // when auto resolution is enabled.
            EditorGUI.BeginDisabledGroup(settings.enableAutoResolution ||
                                         settings.autoResolveOnBuild);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto-Resolution Disabled Warning", EditorStyles.boldLabel);
            settings.autoResolutionDisabledWarning =
                EditorGUILayout.Toggle(settings.autoResolutionDisabledWarning);
            GUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            // Disable the ability to toggle the auto-resolution disabled warning
            // when auto resolution is enabled.
            EditorGUI.BeginDisabledGroup(!settings.enableAutoResolution);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Prompt Before Auto-Resolution", EditorStyles.boldLabel);
            settings.promptBeforeAutoResolution =
                EditorGUILayout.Toggle(settings.promptBeforeAutoResolution);
            GUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Patch AndroidManifest.xml", EditorStyles.boldLabel);
            settings.patchAndroidManifest = EditorGUILayout.Toggle(settings.patchAndroidManifest);
            GUILayout.EndHorizontal();
            if (settings.patchAndroidManifest) {
                GUILayout.Label(String.Format(
                    "Instances of \"applicationId\" variable references will be replaced in " +
                    "{0} with the bundle ID.  If the bundle ID " +
                    "is changed the previous bundle ID will be replaced with the new " +
                    "bundle ID by the plugin.\n\n" +
                    "This works around a bug in Unity 2018.x where the " +
                    "\"applicationId\" variable is not replaced correctly.",
                    AndroidManifestPath));
            } else {
                GUILayout.Label(String.Format(
                    "{0} is not modified.\n\n" +
                    "If you're using Unity 2018.x and have an AndroidManifest.xml " +
                    "that uses the \"applicationId\" variable, your build may fail.",
                    AndroidManifestPath));
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Patch mainTemplate.gradle", EditorStyles.boldLabel);
            settings.patchMainTemplateGradle =
                EditorGUILayout.Toggle(settings.patchMainTemplateGradle);
            GUILayout.EndHorizontal();
            if (settings.patchMainTemplateGradle) {
                GUILayout.Label(
                    "If Gradle builds are enabled and a mainTemplate.gradle file is present, " +
                    "the mainTemplate.gradle file will be patched with dependencies managed " +
                    "by the Android Resolver.");
            } else {
                GUILayout.Label(String.Format(
                    "If Gradle builds are enabled and a mainTemplate.gradle file is present, " +
                    "the mainTemplate.gradle file will not be modified.  Instead dependencies " +
                    "managed by the Android Resolver will be added to the project under {0}",
                    settings.packageDir));
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Use Jetifier.", EditorStyles.boldLabel);
            settings.useJetifier = EditorGUILayout.Toggle(settings.useJetifier);
            GUILayout.EndHorizontal();
            if (settings.useJetifier) {
                GUILayout.Label(
                    "Legacy Android support libraries and references to them from other " +
                    "libraries will be rewritten to use Jetpack using the Jetifier tool. " +
                    "Enabling option allows an application to use Android Jetpack " +
                    "when other libraries in the project use the Android support libraries.");
            } else {
                GUILayout.Label(
                    "Class References to legacy Android support libraries (pre-Jetpack) will be " +
                    "left unmodified in the project. This will possibly result in broken Android " +
                    "builds when mixing legacy Android support libraries and Jetpack libraries.");
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Verbose Logging", EditorStyles.boldLabel);
            settings.verboseLogging = EditorGUILayout.Toggle(settings.verboseLogging);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Use project settings", EditorStyles.boldLabel);
            settings.useProjectSettings = EditorGUILayout.Toggle(settings.useProjectSettings);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            if (GUILayout.Button("Reset to Defaults")) {
                // Load default settings into the dialog but preserve the state in the user's
                // saved preferences.
                var backupSettings = new Settings();
                RestoreDefaultSettings();
                LoadSettings();
                backupSettings.Save();
            }

            GUILayout.BeginHorizontal();
            bool closeWindow = GUILayout.Button("Cancel");
            bool ok = GUILayout.Button("OK");
            closeWindow |= ok;
            if (ok) {
                settings.Save();
                PlayServicesResolver.OnSettingsChanged();
            }
            if (closeWindow) Close();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}

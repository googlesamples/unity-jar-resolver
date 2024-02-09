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
    using System.Collections.Generic;
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
            internal bool patchPropertiesTemplateGradle;
            internal bool patchSettingsTemplateGradle;
            internal bool useFullCustomMavenRepoPathWhenExport;
            internal bool useFullCustomMavenRepoPathWhenNotExport;
            internal string localMavenRepoDir;
            internal bool useJetifier;
            internal bool verboseLogging;
            internal bool autoResolutionDisabledWarning;
            internal bool promptBeforeAutoResolution;
            internal bool useProjectSettings;
            internal bool userRejectedGradleUpgrade;
            internal EditorMeasurement.Settings analyticsSettings;

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
                patchPropertiesTemplateGradle = SettingsDialog.PatchPropertiesTemplateGradle;
                patchSettingsTemplateGradle = SettingsDialog.PatchSettingsTemplateGradle;
                useFullCustomMavenRepoPathWhenExport = SettingsDialog.UseFullCustomMavenRepoPathWhenExport;
                useFullCustomMavenRepoPathWhenNotExport = SettingsDialog.UseFullCustomMavenRepoPathWhenNotExport;
                localMavenRepoDir = SettingsDialog.LocalMavenRepoDir;
                useJetifier = SettingsDialog.UseJetifier;
                verboseLogging = SettingsDialog.VerboseLogging;
                autoResolutionDisabledWarning = SettingsDialog.AutoResolutionDisabledWarning;
                promptBeforeAutoResolution = SettingsDialog.PromptBeforeAutoResolution;
                useProjectSettings = SettingsDialog.UseProjectSettings;
                userRejectedGradleUpgrade = SettingsDialog.UserRejectedGradleUpgrade;
                analyticsSettings = new EditorMeasurement.Settings(PlayServicesResolver.analytics);
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
                SettingsDialog.PatchPropertiesTemplateGradle = patchPropertiesTemplateGradle;
                SettingsDialog.PatchSettingsTemplateGradle = patchSettingsTemplateGradle;
                SettingsDialog.UseFullCustomMavenRepoPathWhenExport = useFullCustomMavenRepoPathWhenExport;
                SettingsDialog.UseFullCustomMavenRepoPathWhenNotExport = useFullCustomMavenRepoPathWhenNotExport;
                SettingsDialog.LocalMavenRepoDir = localMavenRepoDir;
                SettingsDialog.UseJetifier = useJetifier;
                SettingsDialog.VerboseLogging = verboseLogging;
                SettingsDialog.AutoResolutionDisabledWarning = autoResolutionDisabledWarning;
                SettingsDialog.PromptBeforeAutoResolution = promptBeforeAutoResolution;
                SettingsDialog.UseProjectSettings = useProjectSettings;
                SettingsDialog.UserRejectedGradleUpgrade = userRejectedGradleUpgrade;
                analyticsSettings.Save();
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
        private const string PatchPropertiesTemplateGradleKey = Namespace + "PatchPropertiesTemplateGradle";
        private const string PatchSettingsTemplateGradleKey = Namespace + "PatchSettingsTemplateGradle";
        private const string UseFullCustomMavenRepoPathWhenExportKey = Namespace + "UseFullCustomMavenRepoPathWhenExport";
        private const string UseFullCustomMavenRepoPathWhenNotExportKey = Namespace + "UseFullCustomMavenRepoPathWhenNotExport";
        private const string LocalMavenRepoDirKey = Namespace + "LocalMavenRepoDir";
        private const string UseJetifierKey = Namespace + "UseJetifier";
        private const string VerboseLoggingKey = Namespace + "VerboseLogging";
        private const string AutoResolutionDisabledWarningKey =
            Namespace + "AutoResolutionDisabledWarning";
        private const string PromptBeforeAutoResolutionKey =
            Namespace + "PromptBeforeAutoResolution";
        private const string UseGradleDaemonKey = Namespace + "UseGradleDaemon";
        private const string UserRejectedGradleUpgradeKey = Namespace + "UserRejectedGradleUpgrade";

        // List of preference keys, used to restore default settings.
        private static string[] PreferenceKeys = new[] {
            AutoResolveKey,
            AutoResolveOnBuildKey,
            PackageInstallKey,
            PackageDirKey,
            ExplodeAarsKey,
            PatchAndroidManifestKey,
            PatchMainTemplateGradleKey,
            PatchPropertiesTemplateGradleKey,
            PatchSettingsTemplateGradleKey,
            UseFullCustomMavenRepoPathWhenExportKey,
            UseFullCustomMavenRepoPathWhenNotExportKey,
            LocalMavenRepoDirKey,
            UseJetifierKey,
            VerboseLoggingKey,
            AutoResolutionDisabledWarningKey,
            PromptBeforeAutoResolutionKey,
            UseGradleDaemonKey,
            UserRejectedGradleUpgradeKey
        };

        internal const string AndroidPluginsDir = "Assets/Plugins/Android";

        // Unfortunately, Unity currently does not search recursively search subdirectories of
        // AndroidPluginsDir for Android library plugins.  When this is supported - or we come up
        // with a workaround - this can be enabled.
        static bool ConfigurablePackageDir = false;
        static string DefaultPackageDir = AndroidPluginsDir;
        static string DefaultLocalMavenRepoDir = "Assets/GeneratedLocalRepo";

        private Settings settings;

        internal static ProjectSettings projectSettings = new ProjectSettings(Namespace);

        // Previously validated package directory.
        private static string previouslyValidatedPackageDir;

        private Vector2 scrollPosition = new Vector2(0, 0);

        /// <summary>
        /// Reset settings of this plugin to default values.
        /// </summary>
        internal static void RestoreDefaultSettings() {
            projectSettings.DeleteKeys(PreferenceKeys);
            PlayServicesResolver.analytics.RestoreDefaultSettings();
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
                return FileUtils.PosixPathSeparators(ValidatePackageDir(
                    ConfigurablePackageDir ?
                        (projectSettings.GetString(PackageDirKey, DefaultPackageDir)) :
                    DefaultPackageDir));
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
            get {
                return FileUtils.PosixPathSeparators(
                    Path.Combine(PackageDir, "AndroidManifest.xml"));
            }
        }

        internal static bool PatchAndroidManifest {
            set { projectSettings.SetBool(PatchAndroidManifestKey, value); }
            get { return projectSettings.GetBool(PatchAndroidManifestKey, true); }
        }

        internal static bool PatchMainTemplateGradle {
            set { projectSettings.SetBool(PatchMainTemplateGradleKey, value); }
            get { return projectSettings.GetBool(PatchMainTemplateGradleKey, true); }
        }

        internal static bool PatchPropertiesTemplateGradle {
            set { projectSettings.SetBool(PatchPropertiesTemplateGradleKey, value); }
            get { return projectSettings.GetBool(PatchPropertiesTemplateGradleKey, true); }
        }

        internal static bool PatchSettingsTemplateGradle {
            set { projectSettings.SetBool(PatchSettingsTemplateGradleKey, value); }
            get { return projectSettings.GetBool(PatchSettingsTemplateGradleKey, true); }
        }

        internal static bool UseFullCustomMavenRepoPathWhenExport {
            set { projectSettings.SetBool(UseFullCustomMavenRepoPathWhenExportKey, value); }
            get { return projectSettings.GetBool(UseFullCustomMavenRepoPathWhenExportKey, true); }
        }

        internal static bool UseFullCustomMavenRepoPathWhenNotExport {
            set { projectSettings.SetBool(UseFullCustomMavenRepoPathWhenNotExportKey, value); }
            get { return projectSettings.GetBool(UseFullCustomMavenRepoPathWhenNotExportKey, false); }
        }

        internal static string LocalMavenRepoDir {
            private set { projectSettings.SetString(LocalMavenRepoDirKey, value); }
            get {
                return FileUtils.PosixPathSeparators(ValidateLocalMavenRepoDir(
                        projectSettings.GetString(LocalMavenRepoDirKey, DefaultLocalMavenRepoDir)));
            }
        }

        internal static bool UseJetifier {
            set { projectSettings.SetBool(UseJetifierKey, value); }
            get { return projectSettings.GetBool(UseJetifierKey, true); }
        }

        internal static bool VerboseLogging {
            private set { projectSettings.SetBool(VerboseLoggingKey, value); }
            get { return projectSettings.GetBool(VerboseLoggingKey, false); }
        }

        internal static bool UserRejectedGradleUpgrade {
            set { projectSettings.SetBool(UserRejectedGradleUpgradeKey, value); }
            get { return projectSettings.GetBool(UserRejectedGradleUpgradeKey, false); }
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

        internal static string ValidateLocalMavenRepoDir(string directory) {
            if (!directory.ToLowerInvariant().StartsWith(FileUtils.ASSETS_FOLDER.ToLower())) {
                directory = DefaultLocalMavenRepoDir;
            }
            directory = FileUtils.NormalizePathSeparators(directory);

            // TODO: Remove these restrictions
            // Cannot set to be under "Assets/Plugins/Android".  Seems like all .aar and .pom
            // genereted under this folder will be removed in gradle template mode after
            // being generated.  Need to investigate why.
            if (directory.StartsWith(AndroidPluginsDir)) {
                directory = DefaultLocalMavenRepoDir;
                PlayServicesResolver.Log(String.Format(
                        "Currently LocalMavenRepoDir does not work at any folder " +
                        "under \"Assets/Plugins/Android\""), level: LogLevel.Warning);
            }
            if (directory.EndsWith(Path.DirectorySeparatorChar.ToString())) {
                directory = directory.Substring(0, directory.Length - 1);
            }

            return directory;
        }

        public void Initialize() {
            minSize = new Vector2(425, 510);
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
            GUI.skin.label.wordWrap = true;

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
                    settings.packageDir = FileUtils.PosixPathSeparators(
                        startOfPath < 0 ? "" : path.Substring(startOfPath,
                                                              path.Length - startOfPath));
                }
                if (!previousPackageDir.Equals(settings.packageDir)) {
                    settings.packageDir = ValidatePackageDir(settings.packageDir);
                }
                GUILayout.EndHorizontal();
                settings.packageDir = FileUtils.PosixPathSeparators(
                    EditorGUILayout.TextField(settings.packageDir));
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
                GUILayout.Label("AAR explosion will be disabled." +
                                "You may need to set " +
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
            GUILayout.Label("Disable MainTemplate Gradle prompt", EditorStyles.boldLabel);
            settings.userRejectedGradleUpgrade =
                EditorGUILayout.Toggle(settings.userRejectedGradleUpgrade);
            GUILayout.EndHorizontal();

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

            if (settings.patchMainTemplateGradle) {
                GUILayout.Label("Use Full Custom Local Maven Repo Path", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("  When building Android app through Unity", EditorStyles.boldLabel);
                settings.useFullCustomMavenRepoPathWhenNotExport =
                    EditorGUILayout.Toggle(settings.useFullCustomMavenRepoPathWhenNotExport);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("  When exporting Android project", EditorStyles.boldLabel);
                settings.useFullCustomMavenRepoPathWhenExport =
                    EditorGUILayout.Toggle(settings.useFullCustomMavenRepoPathWhenExport);
                GUILayout.EndHorizontal();

                GUILayout.Label(
                    "EDM4U can inject custom local Maven repo to Gradle template files " +
                    "differnetly depending on whether 'Export Project' in Build Settings is " +
                    "enabled or not.\n" +
                    "If checked, custom local Maven repo path will look like the following. " +
                    "This is best if the Unity project is always under the same path, or when " +
                    "Unity editor has bugs which fail to resolve template variables like " +
                    "'**DIR_UNITYPROJECT**'");
                GUILayout.Box(
                    "  maven {\n" +
                    "    url \"file:////path/to/myUnityProject/path/to/m2repository\"\n" +
                    "  }", EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label(
                    "If unchecked, custom local Maven repo path will look like the following. " +
                    "This is best if the Unity projects locates in different folders on " +
                    "different workstations. 'unityProjectPath' will be resolved at build time " +
                    "using template variables like '**DIR_UNITYPROJECT**'");
                GUILayout.Box(
                    "  def unityProjectPath = $/file:///**DIR_UNITYPROJECT**/$.replace(\"\\\", \"/\")\n" +
                    "  maven {\n" +
                    "    url (unityProjectPath + \"/path/to/m2repository\")\n" +
                    "  }", EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label(
                    "Note that EDM4U always uses full path if the custom local Maven repo is NOT " +
                    "under Unity project folder.");

                GUILayout.BeginHorizontal();
                string previousDir = settings.localMavenRepoDir;
                GUILayout.Label("Local Maven Repo Directory", EditorStyles.boldLabel);
                if (GUILayout.Button("Browse")) {
                    string path = EditorUtility.OpenFolderPanel("Set Local Maven Repo Directory",
                                                                settings.localMavenRepoDir, "");
                    int startOfPath = path.IndexOf(
                        FileUtils.ASSETS_FOLDER + Path.DirectorySeparatorChar);
                    settings.localMavenRepoDir = FileUtils.PosixPathSeparators(
                        startOfPath < 0 ? DefaultLocalMavenRepoDir :
                        path.Substring(startOfPath, path.Length - startOfPath));
                }
                if (!previousDir.Equals(settings.localMavenRepoDir)) {
                    settings.localMavenRepoDir =
                        ValidateLocalMavenRepoDir(settings.localMavenRepoDir);
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    "Please pick a folder under Assets folder.  Currently it won't work at " +
                    "any folder under \"Assets/Plugins/Android\"");
                settings.localMavenRepoDir = FileUtils.PosixPathSeparators(
                        ValidateLocalMavenRepoDir(EditorGUILayout.TextField(
                                settings.localMavenRepoDir)));
            }

            if (settings.useJetifier) {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Patch gradleTemplate.properties", EditorStyles.boldLabel);
                settings.patchPropertiesTemplateGradle = EditorGUILayout.Toggle(settings.patchPropertiesTemplateGradle);
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    "For Unity 2019.3 and above, it is recommended to enable Jetifier " +
                    "and AndroidX via gradleTemplate.properties. Please enable " +
                    "Custom Gradle Properties Template' found under 'Player Settings > " +
                    "Settings for Android > Publishing Settings' menu item. " +
                    "This has no effect in older versions of Unity.");
            }

            if (settings.patchMainTemplateGradle) {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Copy and patch settingsTemplate.gradle from 2022.2", EditorStyles.boldLabel);
                settings.patchSettingsTemplateGradle = EditorGUILayout.Toggle(settings.patchSettingsTemplateGradle);
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    "For Unity 2022.2 and above, any additional Maven repositories should be " +
                    "specified in settingsTemplate.gradle. If checked, EDM4U will also copy " +
                    "settingsTemplate.gradle from Unity engine folder.");
            }

            settings.analyticsSettings.RenderGui();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Verbose Logging", EditorStyles.boldLabel);
            settings.verboseLogging = EditorGUILayout.Toggle(settings.verboseLogging);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Use project settings", EditorStyles.boldLabel);
            settings.useProjectSettings = EditorGUILayout.Toggle(settings.useProjectSettings);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            EditorGUILayout.EndScrollView();

            GUILayout.BeginVertical();
            GUILayout.Space(10);

            if (GUILayout.Button("Reset to Defaults")) {
                // Load default settings into the dialog but preserve the state in the user's
                // saved preferences.
                var backupSettings = new Settings();
                RestoreDefaultSettings();
                PlayServicesResolver.analytics.Report("settings/reset", "Settings Reset");
                LoadSettings();
                backupSettings.Save();
            }

            GUILayout.BeginHorizontal();
            bool closeWindow = GUILayout.Button("Cancel");
            if (closeWindow) {
                PlayServicesResolver.analytics.Report("settings/cancel", "Settings Cancel");
            }
            bool ok = GUILayout.Button("OK");
            closeWindow |= ok;
            if (ok) {
                PlayServicesResolver.analytics.Report(
                    "settings/save",
                    new KeyValuePair<string, string>[] {
                        new KeyValuePair<string, string>(
                            "useGradleDaemon",
                            SettingsDialog.UseGradleDaemon.ToString()),
                        new KeyValuePair<string, string>(
                            "enableAutoResolution",
                            SettingsDialog.EnableAutoResolution.ToString()),
                        new KeyValuePair<string, string>(
                            "installAndroidPackages",
                            SettingsDialog.InstallAndroidPackages.ToString()),
                        new KeyValuePair<string, string>(
                            "explodeAars",
                            SettingsDialog.ExplodeAars.ToString()),
                        new KeyValuePair<string, string>(
                            "patchAndroidManifest",
                            SettingsDialog.PatchAndroidManifest.ToString()),
                        new KeyValuePair<string, string>(
                            "UseFullCustomMavenRepoPathWhenNotExport",
                            SettingsDialog.UseFullCustomMavenRepoPathWhenNotExport.ToString()),
                        new KeyValuePair<string, string>(
                            "UseFullCustomMavenRepoPathWhenExport",
                            SettingsDialog.UseFullCustomMavenRepoPathWhenExport.ToString()),
                        new KeyValuePair<string, string>(
                            "localMavenRepoDir",
                            SettingsDialog.LocalMavenRepoDir.ToString()),
                        new KeyValuePair<string, string>(
                            "useJetifier",
                            SettingsDialog.UseJetifier.ToString()),
                        new KeyValuePair<string, string>(
                            "verboseLogging",
                            SettingsDialog.VerboseLogging.ToString()),
                        new KeyValuePair<string, string>(
                            "autoResolutionDisabledWarning",
                            SettingsDialog.AutoResolutionDisabledWarning.ToString()),
                        new KeyValuePair<string, string>(
                            "promptBeforeAutoResolution",
                            SettingsDialog.PromptBeforeAutoResolution.ToString()),
                        new KeyValuePair<string, string>(
                            "patchMainTemplateGradle",
                            SettingsDialog.PatchMainTemplateGradle.ToString()),
                        new KeyValuePair<string, string>(
                            "patchPropertiesTemplateGradle",
                            SettingsDialog.PatchPropertiesTemplateGradle.ToString()),
                        new KeyValuePair<string, string>(
                            "patchSettingsTemplateGradle",
                            SettingsDialog.PatchSettingsTemplateGradle.ToString()),
                        new KeyValuePair<string, string>(
                            "userRejectedGradleUpgrade",
                            SettingsDialog.UserRejectedGradleUpgrade.ToString()),
                    },
                    "Settings Save");

                settings.Save();
                PlayServicesResolver.OnSettingsChanged();
            }
            if (closeWindow) Close();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.EndVertical();
        }
    }
}

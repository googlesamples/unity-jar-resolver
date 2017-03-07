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

namespace GooglePlayServices
{
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Settings dialog for PlayServices Resolver.
    /// </summary>
    public class SettingsDialog : EditorWindow
    {
        const string Namespace = "GooglePlayServices.";
        private const string AutoResolveKey = Namespace + "AutoResolverEnabled";
        private const string PrebuildWithGradleKey = Namespace + "PrebuildWithGradle";
        private const string PackageInstallKey = Namespace + "AndroidPackageInstallationEnabled";
        private const string PackageDirKey = Namespace + "PackageDirectory";
        private const string ExplodeAarsKey = Namespace + "ExplodeAars";
        private const string VerboseLoggingKey = Namespace + "VerboseLogging";
        private const string AutoResolutionDisabledWarningKey =
            Namespace + "AutoResolutionDisabledWarning";

        private const string AndroidPluginsDir = "Assets/Plugins/Android";

        // Unfortunately, Unity currently does not search recursively search subdirectories of
        // AndroidPluginsDir for Android library plugins.  When this is supported - or we come up
        // with a workaround - this can be enabled.
        static bool ConfigurablePackageDir = false;
        static string DefaultPackageDir = AndroidPluginsDir;

        internal static bool EnableAutoResolution {
            set { EditorPrefs.SetBool(AutoResolveKey, value); }
            get { return EditorPrefs.GetBool(AutoResolveKey, true); }
        }

        internal static bool PrebuildWithGradle {
            private set { EditorPrefs.SetBool(PrebuildWithGradleKey, value); }
            get { return EditorPrefs.GetBool(PrebuildWithGradleKey, false); }
        }

        internal static bool InstallAndroidPackages {
            private set { EditorPrefs.SetBool(PackageInstallKey, value); }
            get { return EditorPrefs.GetBool(PackageInstallKey, true); }
        }

        internal static string PackageDir {
            private set { EditorPrefs.SetString(PackageDirKey, value); }
            get {
                return ConfigurablePackageDir ?
                    ValidatePackageDir(EditorPrefs.GetString(PackageDirKey, DefaultPackageDir)) :
                    DefaultPackageDir;
            }
        }

        internal static bool AutoResolutionDisabledWarning {
            set { EditorPrefs.SetBool(AutoResolutionDisabledWarningKey, value); }
            get { return EditorPrefs.GetBool(AutoResolutionDisabledWarningKey, true); }
        }

        // Whether AARs that use variable expansion should be exploded when Gradle builds are
        // enabled.
        internal static bool ExplodeAars {
            private set { EditorPrefs.SetBool(ExplodeAarsKey, value); }
            get { return EditorPrefs.GetBool(ExplodeAarsKey, true); }
        }

        internal static bool VerboseLogging {
            private set { EditorPrefs.SetBool(VerboseLoggingKey, value); }
            get { return EditorPrefs.GetBool(VerboseLoggingKey, false); }
        }

        internal static string ValidatePackageDir(string directory) {
            if (!directory.StartsWith(AndroidPluginsDir)) {
                directory = AndroidPluginsDir;
            }
            return directory;
        }

        bool enableAutoResolution;
        bool prebuildWithGradle;
        bool installAndroidPackages;
        string packageDir;
        bool explodeAars;
        bool verboseLogging;
        bool autoResolutionDisabledWarning;

        public void Initialize()
        {
            minSize = new Vector2(300, 200);
            position = new Rect(UnityEngine.Screen.width / 3, UnityEngine.Screen.height / 3,
                                minSize.x, minSize.y);
        }

        public void OnEnable()
        {
            enableAutoResolution = EnableAutoResolution;
            prebuildWithGradle = PrebuildWithGradle;
            installAndroidPackages = InstallAndroidPackages;
            packageDir = PackageDir;
            explodeAars = ExplodeAars;
            verboseLogging = VerboseLogging;
            autoResolutionDisabledWarning = AutoResolutionDisabledWarning;
        }

        /// <summary>
        /// Called when the GUI should be rendered.
        /// </summary>
        public void OnGUI()
        {
            GUI.skin.label.wordWrap = true;
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Prebuild With Gradle (Experimental)", EditorStyles.boldLabel);
            prebuildWithGradle = EditorGUILayout.Toggle(prebuildWithGradle);
            GUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(prebuildWithGradle == true);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Enable Auto-Resolution", EditorStyles.boldLabel);
            enableAutoResolution = EditorGUILayout.Toggle(enableAutoResolution);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Install Android Packages", EditorStyles.boldLabel);
            installAndroidPackages = EditorGUILayout.Toggle(installAndroidPackages);
            GUILayout.EndHorizontal();


            if (ConfigurablePackageDir) {
                GUILayout.BeginHorizontal();
                string previousPackageDir = packageDir;
                GUILayout.Label("Package Directory", EditorStyles.boldLabel);
                if (GUILayout.Button("Browse")) {
                    string path = EditorUtility.OpenFolderPanel("Set Package Directory",
                                                                PackageDir, "");
                    int startOfPath = path.IndexOf(AndroidPluginsDir);
                    if (startOfPath < 0) {
                        packageDir = "";
                    } else {
                        packageDir = path.Substring(startOfPath, path.Length - startOfPath);
                    }
                }
                if (!previousPackageDir.Equals(packageDir)) {
                    packageDir = ValidatePackageDir(packageDir);
                }
                GUILayout.EndHorizontal();
                packageDir = EditorGUILayout.TextField(packageDir);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Explode AARs", EditorStyles.boldLabel);
            explodeAars = EditorGUILayout.Toggle(explodeAars);
            GUILayout.EndHorizontal();
            if (explodeAars) {
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
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(enableAutoResolution);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto-Resolution Disabled Warning", EditorStyles.boldLabel);
            autoResolutionDisabledWarning = EditorGUILayout.Toggle(autoResolutionDisabledWarning);
            GUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Verbose Logging", EditorStyles.boldLabel);
            verboseLogging = EditorGUILayout.Toggle(verboseLogging);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            bool closeWindow = GUILayout.Button("Cancel");
            bool ok = GUILayout.Button("OK");
            closeWindow |= ok;
            if (ok)
            {
                PrebuildWithGradle = prebuildWithGradle;
                EnableAutoResolution = enableAutoResolution;
                InstallAndroidPackages = installAndroidPackages;
                if (ConfigurablePackageDir) PackageDir = packageDir;
                ExplodeAars = explodeAars;
                VerboseLogging = verboseLogging;
                AutoResolutionDisabledWarning = autoResolutionDisabledWarning;
                PlayServicesResolver.OnSettingsChanged();
            }
            if (closeWindow) Close();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}


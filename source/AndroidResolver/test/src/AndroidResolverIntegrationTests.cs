// <copyright file="AndroidResolverIntegrationTests.cs" company="Google Inc.">
// Copyright (C) 2018 Google Inc. All Rights Reserved.
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

using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

using Google.JarResolver;
using GooglePlayServices;

namespace Google {

public class AndroidResolverIntegrationTests {

    /// <summary>
    /// EditorUserBuildSettings property which controls the Android build system.
    /// </summary>
    private const string ANDROID_BUILD_SYSTEM = "androidBuildSystem";

    /// <summary>
    /// EditorUserBuildSettings property which controls whether an Android project is exported.
    /// </summary>
    private const string EXPORT_ANDROID_PROJECT = "exportAsGoogleAndroidProject";

    /// <summary>
    /// The name of the file, without extension, that will serve as a template for dynamically
    /// adding additional dependencies.
    /// </summary>
    private const string ADDITIONAL_DEPENDENCIES_FILENAME = "TestAdditionalDependencies";

    /// <summary>
    /// The name of the file, without extension, that will serve as a template for dynamically
    /// adding additional dependencies with a duplicate package with a different version.
    /// </summary>
    private const string ADDITIONAL_DUPLICATE_DEPENDENCIES_FILENAME = "TestAdditionalDuplicateDependencies";

    /// <summary>
    /// Disabled application Gradle template file.
    /// </summary>
    private const string GRADLE_TEMPLATE_DISABLED =
        "Assets/Plugins/Android/mainTemplateDISABLED.gradle";

    /// <summary>
    /// Disabled library Gradle template file.
    /// </summary>
    private const string GRADLE_TEMPLATE_LIBRARY_DISABLED =
        "Assets/Plugins/Android/mainTemplateLibraryDISABLED.gradle";

    /// <summary>
    /// Disabled Gradle properties template file.
    /// </summary>
    private const string GRADLE_TEMPLATE_PROPERTIES_DISABLED =
        "Assets/Plugins/Android/gradleTemplateDISABLED.properties";

    /// <summary>
    /// Disabled Gradle settings template file.
    /// </summary>
    private const string GRADLE_TEMPLATE_SETTINGS_DISABLED =
        "Assets/Plugins/Android/settingsTemplateDISABLED.gradle";

    /// <summary>
    /// <summary>
    /// Enabled Gradle template file.
    /// </summary>
    private const string GRADLE_TEMPLATE_ENABLED = "Assets/Plugins/Android/mainTemplate.gradle";

    /// <summary>
    /// <summary>
    /// Enabled Gradle template properties file.
    /// </summary>
    private const string GRADLE_TEMPLATE_PROPERTIES_ENABLED = "Assets/Plugins/Android/gradleTemplate.properties";

    /// <summary>
    /// <summary>
    /// Enabled Gradle settings properties file.
    /// </summary>
    private const string GRADLE_TEMPLATE_SETTINGS_ENABLED = "Assets/Plugins/Android/settingsTemplate.gradle";

    /// <summary>
    /// Configure tests to run.
    /// </summary>
    [IntegrationTester.Initializer]
    public static void ConfigureTestCases() {
        // The default application name is different in different versions of Unity. Set the value
        // in the beginning of the test to ensure all AndroidManifest.xml are using the same
        // application name across different versions of Unity.
        UnityCompat.SetApplicationId(UnityEditor.BuildTarget.Android, "com.Company.ProductName");

        // Set of files to ignore (relative to the Assets/Plugins/Android directory) in all tests
        // that do not use the Gradle template.
        var nonGradleTemplateFilesToIgnore = new HashSet<string>() {
            Path.GetFileName(GRADLE_TEMPLATE_DISABLED),
            Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED),
            Path.GetFileName(GRADLE_TEMPLATE_PROPERTIES_DISABLED),
            Path.GetFileName(GRADLE_TEMPLATE_SETTINGS_DISABLED)
        };

        // Set of files to ignore (relative to the Assets/Plugins/Android directory) in all tests
        // that do not use the Gradle template.
        var unity2022WithoutJetifierGradleTemplateFilesToIgnore = new HashSet<string> {
            Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED),
            Path.GetFileName(GRADLE_TEMPLATE_PROPERTIES_DISABLED),
        };
        var defaultGradleTemplateFilesToIgnore = new HashSet<string> {
            Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED),
            Path.GetFileName(GRADLE_TEMPLATE_PROPERTIES_DISABLED),
            Path.GetFileName(GRADLE_TEMPLATE_SETTINGS_DISABLED),
        };


        UnityEngine.Debug.Log("Setting up test cases for execution.");
        IntegrationTester.Runner.ScheduleTestCases(new [] {
                // This *must* be the first test case as other test cases depend upon it.
                new IntegrationTester.TestCase {
                    Name = "ValidateAndroidTargetSelected",
                    Method = ValidateAndroidTargetSelected,
                },
                new IntegrationTester.TestCase {
                    Name = "SetupDependencies",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();

                        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);
                        ValidateDependencies(testCaseResult);
                        testCaseComplete(testCaseResult);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveForGradleBuildSystemWithTemplate",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();

                        string expectedAssetsDir = null;
                        string gradleTemplateSettings = null;
                        HashSet<string> filesToIgnore = null;
                        if (UnityChangeMavenInSettings_2022_2) {
                            // For Unity >= 2022.2, Maven repo need to be injected to
                            // Gradle Settings Template, instead of Gradle Main Template.
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplate_2022_2";
                            gradleTemplateSettings = GRADLE_TEMPLATE_SETTINGS_DISABLED;
                            filesToIgnore = unity2022WithoutJetifierGradleTemplateFilesToIgnore;
                        } else {
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplate";
                            filesToIgnore = defaultGradleTemplateFilesToIgnore;
                        }

                        ResolveWithGradleTemplate(
                            GRADLE_TEMPLATE_DISABLED,
                            expectedAssetsDir,
                            testCase, testCaseComplete,
                            otherExpectedFiles: new [] {
                                "Assets/GeneratedLocalRepo/Firebase/m2repository/com/google/" +
                                "firebase/firebase-app-unity/5.1.1/firebase-app-unity-5.1.1.aar" },
                            filesToIgnore: filesToIgnore,
                            deleteGradleTemplateSettings: true,
                            gradleTemplateSettings: gradleTemplateSettings);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveForGradleBuildSystemWithDuplicatePackages",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        // Add 2 additional dependency files (each file contains a single package
                        // but with different versions).
                        UpdateAdditionalDependenciesFile(true, ADDITIONAL_DEPENDENCIES_FILENAME);
                        UpdateAdditionalDependenciesFile(true, ADDITIONAL_DUPLICATE_DEPENDENCIES_FILENAME);

                        string expectedAssetsDir = null;
                        string gradleTemplateSettings = null;
                        HashSet<string> filesToIgnore = null;
                        if (UnityChangeMavenInSettings_2022_2) {
                            // For Unity >= 2022.2, Maven repo need to be injected to
                            // Gradle Settings Template, instead of Gradle Main Template.
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplateDuplicatePackages_2022_2";
                            gradleTemplateSettings = GRADLE_TEMPLATE_SETTINGS_DISABLED;
                            filesToIgnore = unity2022WithoutJetifierGradleTemplateFilesToIgnore;
                        } else {
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplateDuplicatePackages";
                            filesToIgnore = defaultGradleTemplateFilesToIgnore;
                        }

                        ResolveWithGradleTemplate(
                            GRADLE_TEMPLATE_DISABLED,
                            expectedAssetsDir,
                            testCase, testCaseComplete,
                            otherExpectedFiles: new [] {
                                "Assets/GeneratedLocalRepo/Firebase/m2repository/com/google/" +
                                "firebase/firebase-app-unity/5.1.1/firebase-app-unity-5.1.1.aar" },
                            filesToIgnore: filesToIgnore,
                            deleteGradleTemplateSettings: true,
                            gradleTemplateSettings: gradleTemplateSettings);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolverForGradleBuildSystemWithTemplateUsingJetifier",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        GooglePlayServices.SettingsDialog.UseJetifier = true;

                        string expectedAssetsDir = null;
                        string gradleTemplateProperties = null;
                        string gradleTemplateSettings = null;
                        HashSet<string> filesToIgnore = null;
                        if (UnityChangeMavenInSettings_2022_2) {
                            // For Unity >= 2022.2, Maven repo need to be injected to
                            // Gradle Settings Template, instead of Gradle Main Template.
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplateJetifier_2022_2";
                            gradleTemplateProperties = GRADLE_TEMPLATE_PROPERTIES_DISABLED;
                            gradleTemplateSettings = GRADLE_TEMPLATE_SETTINGS_DISABLED;
                            filesToIgnore = new HashSet<string> {
                                Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED),
                            };
                        } else if (UnityChangeJetifierInProperties_2019_3) {
                            // For Unity >= 2019.3f, Jetifier is enabled for the build
                            // via gradle properties.
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplateJetifier_2019_3";
                            gradleTemplateProperties = GRADLE_TEMPLATE_PROPERTIES_DISABLED;
                            filesToIgnore = new HashSet<string> {
                                Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED),
                                Path.GetFileName(GRADLE_TEMPLATE_SETTINGS_DISABLED),
                            };
                        } else {
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplateJetifier";
                            filesToIgnore = new HashSet<string> {
                                Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED),
                                Path.GetFileName(GRADLE_TEMPLATE_PROPERTIES_DISABLED),
                                Path.GetFileName(GRADLE_TEMPLATE_SETTINGS_DISABLED),
                            };
                        }

                        ResolveWithGradleTemplate(
                            GRADLE_TEMPLATE_DISABLED,
                            expectedAssetsDir,
                            testCase, testCaseComplete,
                            otherExpectedFiles: new [] {
                                "Assets/GeneratedLocalRepo/Firebase/m2repository/com/google/" +
                                "firebase/firebase-app-unity/5.1.1/firebase-app-unity-5.1.1.aar" },
                            filesToIgnore: filesToIgnore,
                            deleteGradleTemplateProperties: true,
                            gradleTemplateProperties: gradleTemplateProperties,
                            deleteGradleTemplateSettings: true,
                            gradleTemplateSettings: gradleTemplateSettings);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveForGradleBuildSystemLibraryWithTemplate",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();

                        string expectedAssetsDir = null;
                        string gradleTemplateSettings = null;
                        HashSet<string> filesToIgnore = null;
                        if (UnityChangeMavenInSettings_2022_2) {
                            // For Unity >= 2022.2, Maven repo need to be injected to
                            // Gradle Settings Template, instead of Gradle Main Template.
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplateLibrary_2022_2";
                            gradleTemplateSettings = GRADLE_TEMPLATE_SETTINGS_DISABLED;
                            filesToIgnore = new HashSet<string> {
                                Path.GetFileName(GRADLE_TEMPLATE_DISABLED),
                                Path.GetFileName(GRADLE_TEMPLATE_PROPERTIES_DISABLED),
                            };
                        } else {
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplateLibrary";
                            filesToIgnore = new HashSet<string> {
                                Path.GetFileName(GRADLE_TEMPLATE_DISABLED),
                                Path.GetFileName(GRADLE_TEMPLATE_PROPERTIES_DISABLED),
                                Path.GetFileName(GRADLE_TEMPLATE_SETTINGS_DISABLED),
                            };
                        }

                        ResolveWithGradleTemplate(
                            GRADLE_TEMPLATE_LIBRARY_DISABLED,
                            expectedAssetsDir,
                            testCase, testCaseComplete,
                            otherExpectedFiles: new [] {
                                "Assets/GeneratedLocalRepo/Firebase/m2repository/com/google/" +
                                "firebase/firebase-app-unity/5.1.1/firebase-app-unity-5.1.1.aar" },
                            filesToIgnore: filesToIgnore,
                            deleteGradleTemplateSettings: true,
                            gradleTemplateSettings: gradleTemplateSettings);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveForGradleBuildSystemWithTemplateEmpty",
                    Method = (testCase, testCaseComplete) => {
                        string enabledDependencies =
                            "Assets/ExternalDependencyManager/Editor/TestDependencies.xml";
                        string disabledDependencies =
                            "Assets/ExternalDependencyManager/Editor/TestDependenciesDISABLED.xml";
                        Action enableDependencies = () => {
                            UnityEditor.AssetDatabase.MoveAsset(disabledDependencies,
                                                                enabledDependencies);
                        };
                        try {
                            // Disable all XML dependencies.
                            var error = UnityEditor.AssetDatabase.MoveAsset(enabledDependencies,
                                                                            disabledDependencies);
                            if (!String.IsNullOrEmpty(error)) {
                                testCaseComplete(new IntegrationTester.TestCaseResult(testCase) {
                                        ErrorMessages = new List<string>() { error } });
                                return;
                            }
                            ClearAllDependencies();
                            ResolveWithGradleTemplate(
                                GRADLE_TEMPLATE_DISABLED,
                                "ExpectedArtifacts/NoExport/GradleTemplateEmpty",
                                testCase, (testCaseResult) => {
                                    enableDependencies();
                                    testCaseComplete(testCaseResult);
                                },
                                filesToIgnore: new HashSet<string> {
                                    Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED),
                                    Path.GetFileName(GRADLE_TEMPLATE_PROPERTIES_DISABLED),
                                    Path.GetFileName(GRADLE_TEMPLATE_SETTINGS_DISABLED)
                                });
                        } finally {
                            enableDependencies();
                        }
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveForGradleBuildSystem",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        Resolve("Gradle", false, "ExpectedArtifacts/NoExport/Gradle",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveForGradleBuildSystemSync",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        Resolve("Gradle", false, "ExpectedArtifacts/NoExport/Gradle",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete,
                                synchronous: true);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveForGradleBuildSystemAndExport",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        Resolve("Gradle", true, "ExpectedArtifacts/Export/Gradle",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveAddedDependencies",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        UpdateAdditionalDependenciesFile(true);
                        Resolve("Gradle", true, "ExpectedArtifacts/Export/GradleAddedDeps",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveRemovedDependencies",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        // Add the additional dependencies file then immediately remove it.
                        UpdateAdditionalDependenciesFile(true);
                        UpdateAdditionalDependenciesFile(false);
                        Resolve("Gradle", true, "ExpectedArtifacts/Export/Gradle",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "DeleteResolvedLibraries",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        Resolve("Gradle", true, "ExpectedArtifacts/Export/Gradle",
                                null, nonGradleTemplateFilesToIgnore,
                                testCase, (testCaseResult) => {
                                    PlayServicesResolver.DeleteResolvedLibrariesSync();
                                    var unexpectedFilesMessage = new List<string>();
                                    var resolvedFiles = ListFiles("Assets/Plugins/Android",
                                                                  nonGradleTemplateFilesToIgnore);
                                    if (resolvedFiles.Count > 0) {
                                        unexpectedFilesMessage.Add("Libraries not deleted!");
                                        foreach (var filename in resolvedFiles.Values) {
                                            unexpectedFilesMessage.Add(filename);
                                        }
                                    }
                                    testCaseResult.ErrorMessages.AddRange(unexpectedFilesMessage);
                                    testCaseComplete(testCaseResult);
                                },
                                synchronous: true);
                    }
                },
                new IntegrationTester.TestCase {
                    Name = "ResolveForGradleBuildSystemWithTemplateDeleteLibraries",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();

                        string expectedAssetsDir = null;
                        string gradleTemplateSettings = null;
                        HashSet<string> filesToIgnore = null;
                        if (UnityChangeMavenInSettings_2022_2) {
                            // For Unity >= 2022.2, Maven repo need to be injected to
                            // Gradle Settings Template, instead of Gradle Main Template.
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplate_2022_2";
                            gradleTemplateSettings = GRADLE_TEMPLATE_SETTINGS_DISABLED;
                            filesToIgnore = unity2022WithoutJetifierGradleTemplateFilesToIgnore;
                        } else {
                            expectedAssetsDir = "ExpectedArtifacts/NoExport/GradleTemplate";
                            filesToIgnore = defaultGradleTemplateFilesToIgnore;
                        }

                        ResolveWithGradleTemplate(
                            GRADLE_TEMPLATE_DISABLED,
                            expectedAssetsDir,
                            testCase, (testCaseResult) => {
                                PlayServicesResolver.DeleteResolvedLibrariesSync();
                                string expectedAssetsDirEmpty = null;
                                if (UnityChangeMavenInSettings_2022_2) {
                                    // For Unity >= 2022.2, Maven repo need to be injected to
                                    // Gradle Settings Template, instead of Gradle Main Template.
                                    expectedAssetsDirEmpty = "ExpectedArtifacts/NoExport/GradleTemplateEmpty_2022_2";
                                } else {
                                    expectedAssetsDirEmpty = "ExpectedArtifacts/NoExport/GradleTemplateEmpty";
                                }
                                testCaseResult.ErrorMessages.AddRange(CompareDirectoryContents(
                                            expectedAssetsDirEmpty,
                                            "Assets/Plugins/Android", filesToIgnore));
                                if (File.Exists(GRADLE_TEMPLATE_ENABLED)) {
                                    File.Delete(GRADLE_TEMPLATE_ENABLED);
                                }
                                if (File.Exists(GRADLE_TEMPLATE_SETTINGS_ENABLED)) {
                                    File.Delete(GRADLE_TEMPLATE_SETTINGS_ENABLED);
                                }
                                testCaseComplete(testCaseResult);
                            },
                            deleteGradleTemplate: false,
                            filesToIgnore: filesToIgnore,
                            deleteGradleTemplateSettings: false,
                            gradleTemplateSettings: gradleTemplateSettings);
                    }
                },
            });

        // Internal build system for Android is removed in Unity 2019, even
        // UnityEditor.AndroidBuildSystem.Internal still exist.
        if (IntegrationTester.Runner.UnityVersion < 2019.0f) {
            IntegrationTester.Runner.ScheduleTestCases(new [] {
                    new IntegrationTester.TestCase {
                        Name = "ResolveForInternalBuildSystem",
                        Method = (testCase, testCaseComplete) => {
                            ClearAllDependencies();
                            SetupDependencies();
                            Resolve("Internal", false, AarsWithNativeLibrariesSupported ?
                                    "ExpectedArtifacts/NoExport/InternalNativeAars" :
                                    "ExpectedArtifacts/NoExport/InternalNativeAarsExploded",
                                    null, nonGradleTemplateFilesToIgnore, testCase,
                                    testCaseComplete);
                        }
                    },
                    new IntegrationTester.TestCase {
                        Name = "ResolveForInternalBuildSystemUsingJetifier",
                        Method = (testCase, testCaseComplete) => {
                            ClearAllDependencies();
                            SetupDependencies();
                            GooglePlayServices.SettingsDialog.UseJetifier = true;
                            Resolve("Internal", false, AarsWithNativeLibrariesSupported ?
                                    "ExpectedArtifacts/NoExport/InternalNativeAarsJetifier" :
                                    "ExpectedArtifacts/NoExport/InternalNativeAarsExplodedJetifier",
                                    null, nonGradleTemplateFilesToIgnore, testCase,
                                    testCaseComplete);
                        }
                    },
                });
        }

        // Test resolution with Android ABI filtering.
        if (IntegrationTester.Runner.UnityVersion >= 2018.0f) {
            IntegrationTester.Runner.ScheduleTestCase(
                new IntegrationTester.TestCase {
                    Name = "ResolverForGradleBuildSystemUsingAbisArmeabiv7aAndArm64",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        Resolve("Gradle", false,
                                "ExpectedArtifacts/NoExport/GradleArmeabiv7aArm64",
                                "armeabi-v7a, arm64-v8a", nonGradleTemplateFilesToIgnore,
                                testCase, testCaseComplete);
                    }
                });
        } else if (IntegrationTester.Runner.UnityVersion >= 5.0f) {
            IntegrationTester.Runner.ScheduleTestCase(
                new IntegrationTester.TestCase {
                    Name = "ResolverForGradleBuildSystemUsingAbisArmeabiv7a",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        Resolve("Gradle", false,
                                "ExpectedArtifacts/NoExport/GradleArmeabiv7a",
                                "armeabi-v7a", nonGradleTemplateFilesToIgnore,
                                testCase, testCaseComplete);
                    }
                });
        }
    }

    /// <summary>
    /// Whether Current Unity Version requires Maven Repo in Gradle Settings Template.
    /// </summary>
    private static bool UnityChangeMavenInSettings_2022_2 {
        get { return IntegrationTester.Runner.UnityVersion >= 2022.2f; }
    }

    /// <summary>
    /// Whether Current Unity Version requires Jetifier enabling in Gradle Properties Template.
    /// </summary>
    private static bool UnityChangeJetifierInProperties_2019_3 {
        get { return IntegrationTester.Runner.UnityVersion >= 2019.3f; }
    }

    /// <summary>
    /// Whether the Gradle builds are supported by the current version of Unity.
    /// </summary>
    private static bool GradleBuildSupported {
        get { return IntegrationTester.Runner.UnityVersion >= 5.5f; }
    }

    /// <summary>
    /// Whether the current version of Unity requires AARs with native artifacts to be converted
    /// to ant / eclipse projects.
    /// </summary>
    private static bool AarsWithNativeLibrariesSupported {
        get { return IntegrationTester.Runner.UnityVersion < 2017.0f; }
    }

    /// <summary>
    /// Get a property from UnityEditor.EditorUserBuildSettings.
    /// </summary>
    /// Properties are introduced over successive versions of Unity so use reflection to
    /// retrieve them.
    /// <returns>Property value.</returns>
    private static object GetEditorUserBuildSettingsProperty(string name,
                                                             object defaultValue) {
        var property = typeof(UnityEditor.EditorUserBuildSettings).GetProperty(name);
        if (property != null) {
            var value = property.GetValue(null, null);
            if (value != null) return value;
        }
        return defaultValue;
    }

    /// <summary>
    /// Set a property on UnityEditor.EditorUserBuildSettings.
    /// </summary>
    /// <returns>true if set, false otherwise.</returns>
    private static bool SetEditorUserBuildSettingsProperty(string name, object value) {
        var property = typeof(UnityEditor.EditorUserBuildSettings).GetProperty(name);
        if (property == null) return false;
        try {
            property.SetValue(null, value, null);
        } catch (ArgumentException) {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Encode a string as a value of the AndroidBuildSystem enum type.
    /// </summary>
    private static object StringToAndroidBuildSystemValue(string value) {
        var androidBuildSystemType = Google.VersionHandler.FindClass(
            "UnityEditor", "UnityEditor.AndroidBuildSystem");
        if (androidBuildSystemType == null) return null;
        return Enum.Parse(androidBuildSystemType, value);
    }

    /// <summary>
    /// Make sure the Android platform is selected for testing.
    /// </summary>
    private static void ValidateAndroidTargetSelected(
            IntegrationTester.TestCase testCase,
            Action<IntegrationTester.TestCaseResult> testCaseComplete) {
        if (UnityEditor.EditorUserBuildSettings.activeBuildTarget !=
            UnityEditor.BuildTarget.Android) {
            IntegrationTester.Runner.LogTestCaseResult(
                new IntegrationTester.TestCaseResult(testCase) {
                    ErrorMessages = new List<string>() { "Target platform must be Android" }
                });
            IntegrationTester.Runner.LogSummaryAndExit();
        }

        // Verify if PlayServicesResolver properties are working properly.
        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);

        if (String.IsNullOrEmpty(PlayServicesResolver.AndroidGradlePluginVersion)) {
            testCaseResult.ErrorMessages.Add(String.Format(
                "PlayServicesResolver.AndroidGradlePluginVersion is empty or null"));
        }

        if (String.IsNullOrEmpty(PlayServicesResolver.GradleVersion)) {
            testCaseResult.ErrorMessages.Add(String.Format(
                "PlayServicesResolver.GradleVersion is empty or null"));
        }

        // Also, set the internal Gradle version to a deterministic version number.  This controls
        // how gradle template snippets are generated by GradleTemplateResolver.
        PlayServicesResolver.GradleVersion = "2.14";
        testCaseComplete(testCaseResult);
    }

    /// <summary>
    /// Clear *all* dependencies.
    /// This removes all programmatically added dependencies before running a test.
    /// A developer typically shouldn't be doing this, instead they should be changing the
    /// *Dependencies.xml files in the project to force the dependencies to be read again.
    /// This also removes the additional dependencies file.
    /// </summary>
    private static void ClearAllDependencies() {
        UnityEngine.Debug.Log("Clear all loaded dependencies");
        GooglePlayServices.SettingsDialog.UseJetifier = false;
        GooglePlayServices.SettingsDialog.PatchPropertiesTemplateGradle = false;
        GooglePlayServices.SettingsDialog.PatchSettingsTemplateGradle = false;

        GooglePlayServices.SettingsDialog.UserRejectedGradleUpgrade = true;

        PlayServicesSupport.ResetDependencies();
        UpdateAdditionalDependenciesFile(false, ADDITIONAL_DEPENDENCIES_FILENAME);
        UpdateAdditionalDependenciesFile(false, ADDITIONAL_DUPLICATE_DEPENDENCIES_FILENAME);
    }

    /// <summary>
    /// Programmatically add dependencies.
    /// NOTE: This is the deprecated way of adding dependencies and will likely be removed in
    /// future.
    /// </summary>
    private static void SetupDependencies() {
        PlayServicesSupport.CreateInstance("Test", null, "ProjectSettings").DependOn(
            "com.google.firebase", "firebase-common", "16.0.0");
    }

    /// <summary>
    /// Validate Android libraries and repos are setup correctly.
    /// </summary>
    /// <param name="testCaseResult">TestCaseResult instance to add errors to if this method
    /// fails. </param>
    private static void ValidateDependencies(IntegrationTester.TestCaseResult testCaseResult) {
        // Validate set dependencies are present.
        CompareKeyValuePairLists(
            new List<KeyValuePair<string, string>>() {
                new KeyValuePair<string, string>(
                    "com.android.support:support-annotations:26.1.0",
                    "Assets/ExternalDependencyManager/Editor/TestDependencies.xml:4"),
                new KeyValuePair<string, string>(
                    "com.google.firebase:firebase-app-unity:5.1.1",
                    "Assets/ExternalDependencyManager/Editor/TestDependencies.xml:10"),
                new KeyValuePair<string, string>(
                    "com.google.firebase:firebase-common:16.0.0",
                    "Google.AndroidResolverIntegrationTests.SetupDependencies"),
                new KeyValuePair<string, string>(
                    "org.test.psr:classifier:1.0.1:foo@aar",
                    "Assets/ExternalDependencyManager/Editor/TestDependencies.xml:12"),
            },
            PlayServicesResolver.GetPackageSpecs(),
            "Package Specs", testCaseResult);
        // Validate configured repos are present.
        CompareKeyValuePairLists(
            new List<KeyValuePair<string, string>>() {
                new KeyValuePair<string, string>(
                    "file:///my/nonexistant/test/repo",
                    "Assets/ExternalDependencyManager/Editor/TestDependencies.xml:17"),
                new KeyValuePair<string, string>(
                    "file:///" + Path.GetFullPath("project_relative_path/repo").Replace("\\", "/"),
                    "Assets/ExternalDependencyManager/Editor/TestDependencies.xml:17"),
                new KeyValuePair<string, string>(
                    "file:///" + Path.GetFullPath(
                       "Assets/Firebase/m2repository").Replace("\\", "/"),
                    "Assets/ExternalDependencyManager/Editor/TestDependencies.xml:10")
            },
            PlayServicesResolver.GetRepos(),
            "Repos", testCaseResult);
    }

    /// <summary>
    /// Compare two ordered lists.
    /// </summary>
    /// <param name="expectedList">Expected list.</param>
    /// <param name="testList">List to compare with expectedList.</param>
    /// <param name="listDescription">Human readable description of both lists.</param>
    /// <param name="testCaseResult">TestCaseResult instance to add errors to if lists do not
    /// match.</param>
    private static void CompareKeyValuePairLists(
            IList<KeyValuePair<string, string>> expectedList,
            IList<KeyValuePair<string, string>> testList, string listDescription,
            IntegrationTester.TestCaseResult testCaseResult) {
        if (expectedList.Count != testList.Count) {
            testCaseResult.ErrorMessages.Add(String.Format(
                "Returned list of {0} is an unexpected size {1} vs {2}",
                listDescription, testList.Count, expectedList.Count));
            return;
        }
        for (int i = 0; i < expectedList.Count; ++i) {
            var expected = expectedList[i];
            var test = testList[i];
            if (expected.Key != test.Key || expected.Value != test.Value) {
                testCaseResult.ErrorMessages.Add(String.Format(
                    "Element {0} of list {1} ({2} {3}) mismatches the expected value ({4} {5})",
                    i, listDescription, test.Key, test.Value, expected.Key, expected.Value));
            }
        }
    }

    /// <summary>
    /// Programmatically add/remove dependencies by copying/deleting a template file.
    /// The change will be processed by the plugin after the UnityEditor.AssetDatabase.Refresh()
    /// call.
    /// </summary>
    /// <param name="addDependencyFile">If true, will copy the template file to an XML file if it
    /// doesn't exist. If false, delete the XML file if it exists.</param>
    /// <param name="filename">Name of the template file (without extension) to
    /// create an XML from. </param>
    private static void UpdateAdditionalDependenciesFile(
            bool addDependencyFile,
            string filename=ADDITIONAL_DEPENDENCIES_FILENAME) {
        string currentDirectory = Directory.GetCurrentDirectory();
        string editorPath = Path.Combine(currentDirectory,
                                         "Assets/ExternalDependencyManager/Editor/");

        string templateFilePath = Path.Combine(editorPath, filename+
            ".template");
        string xmlFilePath = Path.Combine(editorPath, filename+ ".xml");
        if (addDependencyFile && !File.Exists(xmlFilePath)) {
            if (!File.Exists(templateFilePath)) {
                UnityEngine.Debug.LogError("Could not find file: " + templateFilePath);
                return;
            }

            UnityEngine.Debug.Log("Adding Dependencies file: " + xmlFilePath);
            File.Copy(templateFilePath, xmlFilePath);
            UnityEditor.AssetDatabase.Refresh();
        } else if (!addDependencyFile && File.Exists(xmlFilePath)) {
            UnityEngine.Debug.Log("Removing Dependencies file: " + xmlFilePath);
            File.Delete(xmlFilePath);
            File.Delete(xmlFilePath + ".meta");
            UnityEditor.AssetDatabase.Refresh();
        }
    }

    /// <summary>
    /// Asynchronously run the Android Resolver and validate the result with
    /// ValidateAndroidResolution.
    /// </summary>
    /// <param name="androidBuildSystem">Android build system to select.</param>
    /// <param name="exportProject">Whether Android project export should be enabled.</param>
    /// <param name="expectedAssetsDir">Directory that contains the assets expected from the
    /// resolution step.</param>
    /// <param name="targetAbis">String of Android ABIs to target or null if the default ABIs
    /// should be selected.</param>
    /// <param name="filesToIgnore">Set of files to relative to the generatedAssetsDir.</param>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called with the test result.</param>
    /// <param name="synchronous">Whether the resolution should be executed synchronously.</param>
    private static void Resolve(string androidBuildSystem, bool exportProject,
                                string expectedAssetsDir, string targetAbis,
                                ICollection<string> filesToIgnore,
                                IntegrationTester.TestCase testCase,
                                Action<IntegrationTester.TestCaseResult> testCaseComplete,
                                bool synchronous = false) {
        // Set the Android target ABIs.
        GooglePlayServices.AndroidAbis.CurrentString = targetAbis;
        // Try setting the build system if this version of Unity supports it.
        if (!GradleBuildSupported && androidBuildSystem == "Gradle") {
            testCaseComplete(new IntegrationTester.TestCaseResult(testCase) {
                    Skipped = true,
                    ErrorMessages = new List<string> {
                        "Unity version does not support Gradle builds."
                    }
                });
            return;
        }
        if (!(SetEditorUserBuildSettingsProperty(
                ANDROID_BUILD_SYSTEM, StringToAndroidBuildSystemValue(androidBuildSystem)) &&
              GetEditorUserBuildSettingsProperty(
                ANDROID_BUILD_SYSTEM, androidBuildSystem).ToString() == androidBuildSystem)) {
            testCaseComplete(new IntegrationTester.TestCaseResult(testCase) {
                    ErrorMessages = new List<string> {
                        String.Format("Unable to set AndroidBuildSystem to {0}.",
                                      androidBuildSystem)
                    }
                });
            return;
        }
        // Configure project export setting.
        if (!(SetEditorUserBuildSettingsProperty(EXPORT_ANDROID_PROJECT, exportProject) &&
              (bool)GetEditorUserBuildSettingsProperty(EXPORT_ANDROID_PROJECT,
                                                       exportProject) == exportProject)) {
            testCaseComplete(new IntegrationTester.TestCaseResult(testCase) {
                    ErrorMessages = new List<string> {
                        String.Format("Unable to set Android export project to {0}.",
                                      exportProject)
                    }
                });
        }

        // Resolve dependencies.
        Action<bool> completeWithResult = (bool complete) => {
            IntegrationTester.Runner.ExecuteTestCase(
                testCase,
                () => {
                    testCaseComplete(new IntegrationTester.TestCaseResult(testCase) {
                            ErrorMessages = ValidateAndroidResolution(expectedAssetsDir, complete,
                                                                      filesToIgnore)
                        });
                }, true);
        };
        if (synchronous) {
            bool success = PlayServicesResolver.ResolveSync(true);
            completeWithResult(success);
        } else {
            PlayServicesResolver.Resolve(resolutionCompleteWithResult: completeWithResult);
        }
    }

    /// <summary>
    /// Resolve for Gradle using a template .gradle file.
    /// </summary>
    /// <param name="gradleTemplate">Gradle template to use.</param>
    /// <param name="expectedAssetsDir">Directory that contains the assets expected from the
    /// resolution step.</param>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called with the test result.</param>
    /// <param name="otherExpectedFiles">Set of additional files that are expected in the
    /// project.</param>
    /// <param name="deleteGradleTemplate">Whether to delete the gradle template before
    /// testCaseComplete is called.</param>
    /// <param name="filesToIgnore">Set of files to relative to the generatedAssetsDir.</param>
    /// <param name="gradleTemplateProperties">Gradle template properties to use.</param>
    /// <param name="deleteGradleTemplateProperties">Whether to delete the gradle template
    /// properties before testCaseComplete is called.</param>
    /// <param name="gradleTemplateSettings">Gradle settings template to use.</param>
    /// <param name="deleteGradleTemplateSettings">Whether to delete the gradle settings template
    /// before testCaseComplete is called.</param>
    private static void ResolveWithGradleTemplate(
            string gradleTemplate,
            string expectedAssetsDir,
            IntegrationTester.TestCase testCase,
            Action<IntegrationTester.TestCaseResult> testCaseComplete,
            IEnumerable<string> otherExpectedFiles = null,
            bool deleteGradleTemplateProperties = true,
            ICollection<string> filesToIgnore = null,
            bool deleteGradleTemplate = true,
            string gradleTemplateProperties = null,
            bool deleteGradleTemplateSettings = true,
            string gradleTemplateSettings = null) {
        var cleanUpFiles = new List<string>();
        if (deleteGradleTemplate) cleanUpFiles.Add(GRADLE_TEMPLATE_ENABLED);
        if (deleteGradleTemplateProperties) cleanUpFiles.Add(GRADLE_TEMPLATE_PROPERTIES_ENABLED);
        if (deleteGradleTemplateSettings) cleanUpFiles.Add(GRADLE_TEMPLATE_SETTINGS_ENABLED);
        if (otherExpectedFiles != null) cleanUpFiles.AddRange(otherExpectedFiles);
        Action cleanUpTestCase = () => {
            foreach (var filename in cleanUpFiles) {
                if (File.Exists(filename)) File.Delete(filename);
            }
        };
        try {
            GooglePlayServices.SettingsDialog.PatchMainTemplateGradle = true;
            File.Copy(gradleTemplate, GRADLE_TEMPLATE_ENABLED);
            if (gradleTemplateProperties != null) {
                GooglePlayServices.SettingsDialog.PatchPropertiesTemplateGradle = true;
                File.Copy(gradleTemplateProperties, GRADLE_TEMPLATE_PROPERTIES_ENABLED);
            }
            if (gradleTemplateSettings != null) {
                GooglePlayServices.SettingsDialog.PatchSettingsTemplateGradle = true;
                File.Copy(gradleTemplateSettings, GRADLE_TEMPLATE_SETTINGS_ENABLED);
            }
            Resolve("Gradle", false, expectedAssetsDir, null, filesToIgnore, testCase,
                    (IntegrationTester.TestCaseResult testCaseResult) => {
                        if (otherExpectedFiles != null) {
                            foreach (var expectedFile in otherExpectedFiles) {
                                if (!File.Exists(expectedFile)) {
                                    testCaseResult.ErrorMessages.Add(String.Format("{0} not found",
                                                                                   expectedFile));
                                }
                            }
                        }
                        cleanUpTestCase();
                        testCaseComplete(testCaseResult);
                    }, synchronous: true);
        } catch (Exception ex) {
            var testCaseResult = new IntegrationTester.TestCaseResult(testCase);
            testCaseResult.ErrorMessages.Add(ex.ToString());
            cleanUpTestCase();
            testCaseComplete(testCaseResult);
        }
    }

    /// <summary>
    /// Get a list of files under a directory indexed by the path relative to the directory.
    /// This filters all Unity .meta files from the resultant list.
    /// </summary>
    /// <param name="searchDir">Directory to search.</param>
    /// <param name="filesToIgnore">Set of files to relative to the generatedAssetsDir.</param>
    /// <param name="relativeDir">Root path for relative filenames.  This should be any directory
    /// under the specified searchDir argument.  If this is null, searchDir is used.</param>
    /// <returns>Dictionary of file paths mapped to relative file paths.</returns>
    private static Dictionary<string, string> ListFiles(string searchDir,
                                                        ICollection<string> filesToIgnore,
                                                        string relativeDir = null) {
        var foundFiles = new Dictionary<string, string>();
        relativeDir = relativeDir != null ? relativeDir : searchDir;
        foreach (var path in Directory.GetFiles(searchDir)) {
            var relativeFilename = path.Substring(relativeDir.Length + 1);
            // Skip files that should be ignored.
            if (path.EndsWith(".meta") ||
                (filesToIgnore != null && filesToIgnore.Contains(relativeFilename))) {
                continue;
            }
            foundFiles[relativeFilename] = path;
        }
        foreach (var path in Directory.GetDirectories(searchDir)) {
            foreach (var kv in ListFiles(path, filesToIgnore, relativeDir)) {
                foundFiles[kv.Key] = kv.Value;
            }
        }
        return foundFiles;
    }

    /// <summary>
    /// Extract a zip file.
    /// </summary>
    /// <param name="zipFile">File to extract.</param>
    /// <param name="failureMessages">List to add any failure messages to.</param>
    /// <returns>Directory containing unzipped files if successful, null otherwise.</returns>
    private static string ExtractZip(string zipFile, List<string> failureMessages) {
        string outputDir = Path.Combine(Path.Combine(Path.GetTempPath(),
                                                           Path.GetRandomFileName()),
                                              Path.GetFileName(zipFile));
        Directory.CreateDirectory(outputDir);
        // This uses reflection to access an internal method for testing purposes.
        // ExtractZip is not part of the public API.
        bool successful = PlayServicesResolver.ExtractZip(zipFile, null, outputDir, false);
        if (!successful) {
            failureMessages.Add(String.Format("Unable to extract {0} to {1}",
                                              zipFile, outputDir));
            Directory.Delete(outputDir, true);
            return null;
        }
        return outputDir;
    }

    /// <summary>
    /// Compare the contents of two directories.
    /// </summary>
    /// <param name="expectedAssetsDir">Directory that contains expected assets.</param>
    /// <param name="generatedAssetsDir">Directory that contains generated assets.</param>
    /// <param name="filesToIgnore">Set of files to relative to the generatedAssetsDir.</param>
    /// <returns>List of errors.  If validation was successful the list will be empty.</returns>
    private static List<string> CompareDirectoryContents(string expectedAssetsDir,
                                                         string generatedAssetsDir,
                                                         ICollection<string> filesToIgnore) {
        var failureMessages = new List<string>();
        // Get the set of expected artifact paths and resolved artifact paths.
        var expectedAndResolvedArtifactsByFilename =
            new Dictionary<string, KeyValuePair<string, string>>();
        foreach (var kv in ListFiles(expectedAssetsDir, null)) {
            expectedAndResolvedArtifactsByFilename[kv.Key] =
                new KeyValuePair<string, string>(kv.Value, null);
        }
        foreach (var kv in ListFiles(generatedAssetsDir, filesToIgnore)) {
            KeyValuePair<string, string> expectedResolved;
            if (expectedAndResolvedArtifactsByFilename.TryGetValue(kv.Key,
                                                                   out expectedResolved)) {
                expectedAndResolvedArtifactsByFilename[kv.Key] =
                    new KeyValuePair<string, string>(expectedResolved.Key, kv.Value);
            } else {
                failureMessages.Add(String.Format("Found unexpected artifact {0}", kv.Value));
            }
        }
        // Report all missing files.
        foreach (var kv in expectedAndResolvedArtifactsByFilename) {
            var expectedResolved = kv.Value;
            if (expectedResolved.Value == null) {
                failureMessages.Add(String.Format("Missing expected artifact {0}", kv.Key));
            }
        }

        // Compare contents of all expected and resolved files.
        foreach (var expectedResolved in expectedAndResolvedArtifactsByFilename.Values) {
            var expectedFile = expectedResolved.Key;
            var resolvedFile = expectedResolved.Value;
            if (resolvedFile == null) continue;
            // If zip (jar / aar) files are recompacted they will differ due to change in timestamps
            // and file ordering, so extract them and compare the results.
            bool isZipFile = false;
            foreach (var extension in new [] { ".aar", ".jar" }) {
                if (expectedFile.EndsWith(extension)) {
                    isZipFile = true;
                    break;
                }
            }
            var expectedContents = File.ReadAllBytes(expectedFile);
            var resolvedContents = File.ReadAllBytes(resolvedFile);
            if (!expectedContents.SequenceEqual(resolvedContents)) {
                if (isZipFile) {
                    // Extract both files and compare the contents.
                    string[] extractedDirectories = new string[] { null, null };
                    try {
                        var expectedDir = ExtractZip(expectedFile, failureMessages);
                        extractedDirectories[0] = expectedDir;
                        var resolvedDir = ExtractZip(resolvedFile, failureMessages);
                        extractedDirectories[1] = resolvedDir;
                        if (expectedDir != null && resolvedDir != null) {
                            var zipDirCompareFailures = CompareDirectoryContents(expectedDir,
                                                                                 resolvedDir, null);
                            if (zipDirCompareFailures.Count > 0) {
                                failureMessages.Add(String.Format("Artifact {0} does not match {1}",
                                                                  resolvedFile, expectedFile));
                                failureMessages.AddRange(zipDirCompareFailures);
                            }
                        }
                    } finally {
                        foreach (var directory in extractedDirectories) {
                            if (directory != null) Directory.Delete(directory, true);
                        }
                    }
                } else {
                    bool differs = true;
                    // Determine whether to display the file as a string.
                    bool displayContents = false;
                    string expectedContentsAsString = "(binary)";
                    string resolvedContentsAsString = expectedContentsAsString;
                    string resolvedExtension = Path.GetExtension(resolvedFile).ToLower();
                    foreach (var extension in new[] { ".xml", ".txt", ".gradle", ".properties" }) {
                        if (resolvedExtension == extension) {
                            displayContents = true;
                            break;
                        }
                    }
                    if (displayContents) {
                        // Compare ignoring leading and trailing whitespace.
                        expectedContentsAsString =
                            System.Text.Encoding.Default.GetString(expectedContents).Trim();
                        resolvedContentsAsString =
                            System.Text.Encoding.Default.GetString(resolvedContents).Trim();
                        differs = expectedContentsAsString != resolvedContentsAsString;
                    }
                    if (differs) {
                        // Log an error.
                        failureMessages.Add(String.Format(
                            "Artifact {0} does not match contents of {1}\n" +
                            "--- {0} -------\n" +
                            "{2}\n" +
                            "--- {0} end ---\n" +
                            "--- {1} -------\n" +
                            "{3}\n" +
                            "--- {1} -------\n",
                            resolvedFile, expectedFile, resolvedContentsAsString,
                            expectedContentsAsString));
                    }
                }
            }
        }
        return failureMessages;
    }

    /// <summary>
    /// Called when android dependency resolution is complete.
    /// </summary>
    /// <param name="expectedAssetsDir">Directory that contains the assets expected from the
    /// resolution step.</param>
    /// <param name="result">true if resolution completed successfully, false otherwise.</param>
    /// <param name="filesToIgnore">Set of files to relative to the generatedAssetsDir.</param>
    /// <returns>List of errors.  If validation was successful the list will be empty.</returns>
    private static List<string> ValidateAndroidResolution(string expectedAssetsDir, bool result,
                                                          ICollection<string> filesToIgnore) {
        var failureMessages = new List<string>();
        if (!result) {
            failureMessages.Add(String.Format("Android resolver reported a failure {0}", result));
        }
        failureMessages.AddRange(CompareDirectoryContents(expectedAssetsDir,
                                                          "Assets/Plugins/Android", filesToIgnore));
        return failureMessages;
    }
}

}

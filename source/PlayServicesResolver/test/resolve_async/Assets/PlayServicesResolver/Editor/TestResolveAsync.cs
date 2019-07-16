// <copyright file="TestResolveAsync.cs" company="Google Inc.">
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

[UnityEditor.InitializeOnLoad]
public class TestResolveAsync {

    /// <summary>
    /// Test case class.
    ///
    /// This specifies a test case to execute.  Each test case has a name which is used to
    /// log the result of the test and the method to execute as part of the test case.
    /// </summary>
    class TestCase {

        /// <summary>
        /// Test case delegate.
        /// </summary>
        /// <param name="testCase">Object executing this method.</param>
        /// <param name="testCaseComplete">Called when the test case is complete.</param>
        public delegate void MethodDelegate(TestCase testCase,
                                            Action<TestCaseResult> testCaseComplete);

        /// <summary>
        /// Name of the test case.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Delegate that runs the test case logic.
        /// </summary>
        public MethodDelegate Method { get; set; }
    }

    /// <summary>
    /// Result of a test.
    /// </summary>
    class TestCaseResult {

        /// <summary>
        /// Initialize the class.
        /// </summary>
        public TestCaseResult(TestCase testCase) {
            TestCaseName = testCase.Name;
            ErrorMessages = new List<string>();
            Skipped = false;
        }

        /// <summary>
        /// Name of the test case.  This does not need to be set by the test case.
        /// </summary>
        public string TestCaseName { private get; set; }

        /// <summary>
        /// Error messages reported by a test case failure.
        /// </summary>
        public List<string> ErrorMessages { get; set; }

        /// <summary>
        /// Whether the test case was skipped.
        /// </summary>
        public bool Skipped { get; set; }

        /// <summary>
        /// Whether the test case succeeded.
        /// </summary>
        public bool Succeeded {
            get {
                return Skipped || ErrorMessages == null || ErrorMessages.Count == 0;
            }
        }

        /// <summary>
        /// Format the result as a string.
        /// </summary>
        /// <param name="includeFailureMessages">Include failure messages in the list.</param>
        public string FormatString(bool includeFailureMessages) {
            return String.Format("Test {0}: {1}{2}", TestCaseName,
                                 Skipped ? "SKIPPED" : Succeeded ? "PASSED" : "FAILED",
                                 includeFailureMessages && ErrorMessages != null &&
                                 ErrorMessages.Count > 0 ?
                                     "\n" + String.Join("\n", ErrorMessages.ToArray()) : "");
        }
    }

    /// <summary>
    /// Executed test case names and failure messages (if any).
    /// </summary>
    private static List<TestCaseResult> testCaseResults = new List<TestCaseResult>();

    /// <summary>
    /// Set of test cases to execute.
    /// </summary>
    private static List<TestCase> testCases = new List<TestCase>();

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
    /// Enabled Gradle template file.
    /// </summary>
    private const string GRADLE_TEMPLATE_ENABLED = "Assets/Plugins/Android/mainTemplate.gradle";

    /// <summary>
    /// Major / minor Unity version numbers.
    /// </summary>
    private static float unityVersion;

    /// <summary>
    /// This module can be executed multiple times when the Version Handler is enabling
    /// so this method uses a temporary file to determine whether the module has been executed
    /// once in a Unity session.
    /// </summary>
    /// <returns>true if the module was previously initialized, false otherwise.</returns>
    private static bool SetInitialized() {
        const string INITIALIZED_PATH = "Temp/TestEnabledCallbackInitialized";
        if (File.Exists(INITIALIZED_PATH)) return true;
        File.WriteAllText(INITIALIZED_PATH, "Ready");
        return false;
    }

    /// <summary>
    /// Register a method to call when the Version Handler has enabled all plugins in the project.
    /// </summary>
    static TestResolveAsync() {
        unityVersion = Google.VersionHandler.GetUnityVersionMajorMinor();
        // Disable stack traces for more condensed logs.
        UnityEngine.Application.stackTraceLogType = UnityEngine.StackTraceLogType.None;

        // Set of files to ignore (relative to the Assets/Plugins/Android directory) in all tests
        // that do not use the Gradle template.
        var nonGradleTemplateFilesToIgnore = new HashSet<string>() {
            Path.GetFileName(GRADLE_TEMPLATE_DISABLED),
            Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED)
        };

        UnityEngine.Debug.Log("Setting up test cases for execution.");
        testCases.AddRange(new [] {
                // This *must* be the first test case as other test cases depend upon it.
                new TestCase {
                    Name = "ValidateAndroidTargetSelected",
                    Method = ValidateAndroidTargetSelected,
                },
                new TestCase {
                    Name = "SetupDependencies",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();

                        var testCaseResult = new TestCaseResult(testCase);
                        ValidateDependencies(testCaseResult);
                        testCaseComplete(testCaseResult);
                    }
                },
                new TestCase {
                    Name = "ResolveForGradleBuildSystemWithTemplate",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();

                        ResolveWithGradleTemplate(
                            GRADLE_TEMPLATE_DISABLED,
                            "ExpectedArtifacts/NoExport/GradleTemplate",
                            testCase, testCaseComplete,
                            otherExpectedFiles: new [] {
                                "Assets/Firebase/m2repository/com/google/firebase/" +
                                "firebase-app-unity/5.1.1/firebase-app-unity-5.1.1.aar" },
                            filesToIgnore: new HashSet<string> {
                                Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED)
                            });
                    }
                },
                new TestCase {
                    Name = "ResolverForGradleBuildSystemWithTemplateUsingJetifier",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        UseJetifier = true;

                        ResolveWithGradleTemplate(
                            GRADLE_TEMPLATE_DISABLED,
                            "ExpectedArtifacts/NoExport/GradleTemplateJetifier",
                            testCase, testCaseComplete,
                            otherExpectedFiles: new [] {
                                "Assets/Firebase/m2repository/com/google/firebase/" +
                                "firebase-app-unity/5.1.1/firebase-app-unity-5.1.1.aar" },
                            filesToIgnore: new HashSet<string> {
                                Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED)
                            });
                    }
                },
                new TestCase {
                    Name = "ResolveForGradleBuildSystemLibraryWithTemplate",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();

                        ResolveWithGradleTemplate(
                            GRADLE_TEMPLATE_LIBRARY_DISABLED,
                            "ExpectedArtifacts/NoExport/GradleTemplateLibrary",
                            testCase, testCaseComplete,
                            otherExpectedFiles: new [] {
                                "Assets/Firebase/m2repository/com/google/firebase/" +
                                "firebase-app-unity/5.1.1/firebase-app-unity-5.1.1.aar" },
                            filesToIgnore: new HashSet<string> {
                                Path.GetFileName(GRADLE_TEMPLATE_DISABLED)
                            });
                    }
                },
                new TestCase {
                    Name = "ResolveForGradleBuildSystemWithTemplateEmpty",
                    Method = (testCase, testCaseComplete) => {
                        string enabledDependencies =
                            "Assets/PlayServicesResolver/Editor/TestDependencies.xml";
                        string disabledDependencies =
                            "Assets/PlayServicesResolver/Editor/TestDependenciesDISABLED.xml";
                        Action enableDependencies = () => {
                            UnityEditor.AssetDatabase.MoveAsset(disabledDependencies,
                                                                enabledDependencies);
                        };
                        try {
                            // Disable all XML dependencies.
                            var error = UnityEditor.AssetDatabase.MoveAsset(enabledDependencies,
                                                                            disabledDependencies);
                            if (!String.IsNullOrEmpty(error)) {
                                testCaseComplete(new TestCaseResult(testCase) {
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
                                    Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED)
                                });
                        } finally {
                            enableDependencies();
                        }
                    }
                },
                new TestCase {
                    Name = "ResolveForGradleBuildSystem",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        Resolve("Gradle", false, "ExpectedArtifacts/NoExport/Gradle",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete);
                    }
                },
                new TestCase {
                    Name = "ResolveForGradleBuildSystemSync",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        Resolve("Gradle", false, "ExpectedArtifacts/NoExport/Gradle",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete,
                                synchronous: true);
                    }
                },
                new TestCase {
                    Name = "ResolveForInternalBuildSystem",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        Resolve("Internal", false,
                                AarsWithNativeLibrariesSupported ?
                                    "ExpectedArtifacts/NoExport/InternalNativeAars" :
                                    "ExpectedArtifacts/NoExport/InternalNativeAarsExploded",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete);
                    }
                },
                new TestCase {
                    Name = "ResolveForInternalBuildSystemUsingJetifier",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        UseJetifier = true;
                        Resolve("Internal", false,
                                AarsWithNativeLibrariesSupported ?
                                    "ExpectedArtifacts/NoExport/InternalNativeAarsJetifier" :
                                    "ExpectedArtifacts/NoExport/InternalNativeAarsExplodedJetifier",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete);
                    }
                },
                new TestCase {
                    Name = "ResolveForGradleBuildSystemAndExport",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        Resolve("Gradle", true, "ExpectedArtifacts/Export/Gradle",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete);
                    }
                },
                new TestCase {
                    Name = "ResolveAddedDependencies",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        UpdateAdditionalDependenciesFile(true);
                        Resolve("Gradle", true, "ExpectedArtifacts/Export/GradleAddedDeps",
                                null, nonGradleTemplateFilesToIgnore, testCase, testCaseComplete);
                    }
                },
                new TestCase {
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
                new TestCase {
                    Name = "DeleteResolvedLibraries",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        Resolve("Gradle", true, "ExpectedArtifacts/Export/Gradle",
                                null, nonGradleTemplateFilesToIgnore,
                                testCase, (testCaseResult) => {
                                    Google.VersionHandler.InvokeStaticMethod(
                                        AndroidResolverClass, "DeleteResolvedLibrariesSync", null);
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
                new TestCase {
                    Name = "ResolveForGradleBuildSystemWithTemplateDeleteLibraries",
                    Method = (testCase, testCaseComplete) => {
                        ClearAllDependencies();
                        SetupDependencies();
                        var filesToIgnore = new HashSet<string> {
                            Path.GetFileName(GRADLE_TEMPLATE_LIBRARY_DISABLED)
                        };

                        ResolveWithGradleTemplate(
                            GRADLE_TEMPLATE_DISABLED,
                            "ExpectedArtifacts/NoExport/GradleTemplate",
                            testCase, (testCaseResult) => {
                                Google.VersionHandler.InvokeStaticMethod(
                                        AndroidResolverClass, "DeleteResolvedLibrariesSync", null);
                                testCaseResult.ErrorMessages.AddRange(CompareDirectoryContents(
                                            "ExpectedArtifacts/NoExport/GradleTemplateEmpty",
                                            "Assets/Plugins/Android", filesToIgnore));
                                if (File.Exists(GRADLE_TEMPLATE_ENABLED)) {
                                    File.Delete(GRADLE_TEMPLATE_ENABLED);
                                }
                                testCaseComplete(testCaseResult);
                            },
                            deleteGradleTemplate: false,
                            filesToIgnore: filesToIgnore);
                    }
                },
            });

        // Test resolution with Android ABI filtering.
        if (unityVersion >= 2018.0f) {
            testCases.AddRange(new [] {
                    new TestCase {
                        Name = "ResolverForGradleBuildSystemUsingAbisArmeabiv7aAndArm64",
                        Method = (testCase, testCaseComplete) => {
                            ClearAllDependencies();
                            Resolve("Gradle", false,
                                    "ExpectedArtifacts/NoExport/GradleArmeabiv7aArm64",
                                    "armeabi-v7a, arm64-v8a", nonGradleTemplateFilesToIgnore,
                                    testCase, testCaseComplete);
                        }
                    }
                });
        } else if (unityVersion >= 5.0f) {
            testCases.AddRange(new [] {
                    new TestCase {
                        Name = "ResolverForGradleBuildSystemUsingAbisArmeabiv7a",
                        Method = (testCase, testCaseComplete) => {
                            ClearAllDependencies();
                            Resolve("Gradle", false,
                                    "ExpectedArtifacts/NoExport/GradleArmeabiv7a",
                                    "armeabi-v7a", nonGradleTemplateFilesToIgnore,
                                    testCase, testCaseComplete);
                        }
                    }
                });
        }

        UnityEngine.Debug.Log("Set up callback on Version Handler completion.");
        Google.VersionHandler.UpdateCompleteMethods = new [] {
            ":TestResolveAsync:VersionHandlerReady"
        };
        UnityEngine.Debug.Log("Enable plugin using the Version Handler.");
        Google.VersionHandler.UpdateNow();
    }

    /// <summary>
    /// Whether the Gradle builds are supported by the current version of Unity.
    /// </summary>
    private static bool GradleBuildSupported {
        get { return unityVersion >= 5.5f; }
    }

    /// <summary>
    /// Whether the current version of Unity requires AARs with native artifacts to be converted
    /// to ant / eclipse projects.
    /// </summary>
    private static bool AarsWithNativeLibrariesSupported {
        get { return unityVersion < 2017.0f; }
    }

    /// <summary>
    /// Get property that gets and sets Android ABIs.
    /// </summary>
    private static PropertyInfo AndroidAbisCurrentStringProperty {
        get {
            return Google.VersionHandler.FindClass(
                "Google.JarResolver", "GooglePlayServices.AndroidAbis").GetProperty(
                    "CurrentString");
        }
    }

    /// <summary>
    /// Set Android ABIs.
    /// </summary>
    private static string AndroidAbisCurrentString {
        set { AndroidAbisCurrentStringProperty.SetValue(null, value, null); }
        get { return (string)AndroidAbisCurrentStringProperty.GetValue(null, null); }
    }

    /// <summary>
    /// Get the property that gets and sets whether the Jetifier is enabled.
    /// </summary>
    private static PropertyInfo UseJetifierBoolProperty {
        get {
            return Google.VersionHandler.FindClass(
                "Google.JarResolver", "GooglePlayServices.SettingsDialog").GetProperty(
                    "UseJetifier", BindingFlags.Static | BindingFlags.NonPublic);
        }
    }

    /// <summary>
    /// Get / set jetifier setting.
    /// </summary>
    private static bool UseJetifier {
        set { UseJetifierBoolProperty.SetValue(null, value, null); }
        get { return (bool)UseJetifierBoolProperty.GetValue(null, null); }
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
    /// Log test result summary and quit the application.
    /// </summary>
    private static void LogSummaryAndExit() {
        bool passed = true;
        var testSummaryLines = new List<string>();
        foreach (var testCaseResult in testCaseResults) {
            testSummaryLines.Add(testCaseResult.FormatString(false));
            passed &= testCaseResult.Succeeded;
        }
        UnityEngine.Debug.Log(String.Format("Test(s) {0}.\n{1}", passed ? "PASSED" : "FAILED",
                                            String.Join("\n", testSummaryLines.ToArray())));
        UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
    }

    /// <summary>
    /// Log a test case result with error details.
    /// </summary>
    /// <param name="testCaseResult">Result to log.</param>
    private static void LogTestCaseResult(TestCaseResult testCaseResult) {
        testCaseResults.Add(testCaseResult);
        UnityEngine.Debug.Log(testCaseResult.FormatString(true));
    }

    /// <summary>
    /// Execute a function for a test case catching any exceptions and logging the result.
    /// </summary>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseAction">Action to execute.</param>
    /// <param name="executeNext">Whether to execute the next test case if the specified action
    /// fails.</param>
    /// <returns>true if the action executed without any exceptions, false otherwise.</returns>
    private static bool ExecuteTestCase(TestCase testCase, Action testCaseAction,
                                        bool executeNext) {
        bool succeeded = true;
        try {
            testCaseAction();
        } catch (Exception e) {
            LogTestCaseResult(new TestCaseResult(testCase) {
                    ErrorMessages = new List<string> { e.ToString() }
                });
            succeeded = false;
        }
        if (!succeeded && executeNext) ExecuteNextTestCase();
        return succeeded;
    }

    /// <summary>
    /// Execute the next queued test case.
    /// </summary>
    private static void ExecuteNextTestCase() {
        bool executeNext;
        do {
            executeNext = false;
            if (testCases.Count > 0) {
                var testCase = testCases[0];
                testCases.RemoveAt(0);
                UnityEngine.Debug.Log(String.Format("Test {0} starting...", testCase.Name));
                // If the test threw an exception on this thread, execute the next test case
                // in a loop.
                executeNext = !ExecuteTestCase(
                    testCase,
                    () => {
                        testCase.Method(testCase, (testCaseResult) => {
                                UnityEngine.Debug.Log(String.Format("Test {0} complete",
                                                                    testCase.Name));
                                testCaseResult.TestCaseName = testCase.Name;
                                LogTestCaseResult(testCaseResult);
                                ExecuteNextTestCase();
                            });
                    }, false);
            } else {
                LogSummaryAndExit();
            }
        } while (executeNext);
    }

    /// <summary>
    /// Called when the Version Handler has enabled all managed plugins in a project.
    /// </summary>
    public static void VersionHandlerReady() {
        UnityEngine.Debug.Log("VersionHandler is ready.");
        Google.VersionHandler.UpdateCompleteMethods = null;
        // If this has already been initialize this session, do not start tests again.
        if (SetInitialized()) return;
        // Start executing tests.
        ExecuteNextTestCase();
    }

    /// <summary>
    /// Make sure the Android platform is selected for testing.
    /// </summary>
    private static void ValidateAndroidTargetSelected(TestCase testCase,
                                                      Action<TestCaseResult> testCaseComplete) {
        if (UnityEditor.EditorUserBuildSettings.activeBuildTarget !=
            UnityEditor.BuildTarget.Android) {
            LogTestCaseResult(new TestCaseResult(testCase) {
                    ErrorMessages = new List<string>() { "Target platform must be Android" }
                });
            LogSummaryAndExit();
        }
        // Also, set the internal Gradle version to a deterministic version number.  This controls
        // how gradle template snippets are generated by GradleTemplateResolver.
        var resolver = Google.VersionHandler.FindClass("Google.JarResolver",
                                                       "GooglePlayServices.PlayServicesResolver");
        resolver.GetProperty("GradleVersion").SetValue(null, "2.14", null);
        testCaseComplete(new TestCaseResult(testCase));
    }

    /// <summary>
    /// Get the Android Resolver support instance.
    /// NOTE: This is deprecated and only exposed for testing.
    /// </summary>
    private static object AndroidResolverSupport {
        get {
            // Get the deprecated dependency management API.
            return Google.VersionHandler.InvokeStaticMethod(
                    Google.VersionHandler.FindClass(
                        "Google.JarResolver", "Google.JarResolver.PlayServicesSupport"),
                 "CreateInstance", new object[] { "Test", null, "ProjectSettings" });
        }
    }

    /// <summary>
    /// Cached Android Resolver class.
    /// </summary>
    private static Type androidResolverClass = null;

    /// <summary>
    /// Get the Android Resolver class.
    /// </summary>
    private static Type AndroidResolverClass {
        get {
            androidResolverClass = androidResolverClass ?? Google.VersionHandler.FindClass(
                "Google.JarResolver", "GooglePlayServices.PlayServicesResolver");
            return androidResolverClass;
        }
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
        UseJetifier = false;
        AndroidResolverSupport.GetType().GetMethod(
            "ResetDependencies",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        UpdateAdditionalDependenciesFile(false);
    }

    /// <summary>
    /// Programmatically add dependencies.
    /// NOTE: This is the deprecated way of adding dependencies and will likely be removed in
    /// future.
    /// </summary>
    private static void SetupDependencies() {
        Google.VersionHandler.InvokeInstanceMethod(
            AndroidResolverSupport, "DependOn",
            new object[] { "com.google.firebase", "firebase-common", "16.0.0" });
    }

    /// <summary>
    /// Validate Android libraries and repos are setup correctly.
    /// </summary>
    /// <param name="testCaseResult">TestCaseResult instance to add errors to if this method
    /// fails. </param>
    private static void ValidateDependencies(TestCaseResult testCaseResult) {
        // Validate set dependencies are present.
        CompareKeyValuePairLists(
            new List<KeyValuePair<string, string>>() {
                new KeyValuePair<string, string>(
                    "com.android.support:support-annotations:26.1.0",
                    "Assets/PlayServicesResolver/Editor/TestDependencies.xml:4"),
                new KeyValuePair<string, string>(
                    "com.google.firebase:firebase-app-unity:5.1.1",
                    "Assets/PlayServicesResolver/Editor/TestDependencies.xml:10"),
                new KeyValuePair<string, string>(
                    "com.google.firebase:firebase-common:16.0.0",
                    "TestResolveAsync.SetupDependencies()")
            },
            (IList<KeyValuePair<string, string>>)Google.VersionHandler.InvokeStaticMethod(
                AndroidResolverClass, "GetPackageSpecs", null),
            "Package Specs", testCaseResult);
        // Validate configured repos are present.
        CompareKeyValuePairLists(
            new List<KeyValuePair<string, string>>() {
                new KeyValuePair<string, string>(
                    "file:///my/nonexistant/test/repo",
                    "Assets/PlayServicesResolver/Editor/TestDependencies.xml:15"),
                new KeyValuePair<string, string>(
                    "file:///" + Path.GetFullPath("project_relative_path/repo").Replace("\\", "/"),
                    "Assets/PlayServicesResolver/Editor/TestDependencies.xml:15"),
                new KeyValuePair<string, string>(
                    "file:///" + Path.GetFullPath(
                       "Assets/Firebase/m2repository").Replace("\\", "/"),
                    "Assets/PlayServicesResolver/Editor/TestDependencies.xml:10")
            },
            (IList<KeyValuePair<string, string>>)Google.VersionHandler.InvokeStaticMethod(
                AndroidResolverClass, "GetRepos", null),
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
            TestCaseResult testCaseResult) {
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
    private static void UpdateAdditionalDependenciesFile(bool addDependencyFile) {
        string currentDirectory = Directory.GetCurrentDirectory();
        string editorPath = Path.Combine(currentDirectory, "Assets/PlayServicesResolver/Editor/");

        string templateFilePath = Path.Combine(editorPath, ADDITIONAL_DEPENDENCIES_FILENAME +
            ".template");
        string xmlFilePath = Path.Combine(editorPath, ADDITIONAL_DEPENDENCIES_FILENAME + ".xml");
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
                                TestCase testCase, Action<TestCaseResult> testCaseComplete,
                                bool synchronous = false) {
        // Set the Android target ABIs.
        AndroidAbisCurrentString = targetAbis;
        // Try setting the build system if this version of Unity supports it.
        if (!GradleBuildSupported && androidBuildSystem == "Gradle") {
            testCaseComplete(new TestCaseResult(testCase) {
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
            testCaseComplete(new TestCaseResult(testCase) {
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
            testCaseComplete(new TestCaseResult(testCase) {
                    ErrorMessages = new List<string> {
                        String.Format("Unable to set Android export project to {0}.",
                                      exportProject)
                    }
                });
        }

        // Resolve dependencies.
        Action<bool> completeWithResult = (bool complete) => {
            ExecuteTestCase(
                testCase,
                () => {
                    testCaseComplete(new TestCaseResult(testCase) {
                            ErrorMessages = ValidateAndroidResolution(expectedAssetsDir, complete,
                                                                      filesToIgnore)
                        });
                }, true);
        };
        if (synchronous) {
            bool success = (bool)Google.VersionHandler.InvokeStaticMethod(
                AndroidResolverClass, "ResolveSync", args: new object[] { true },
                namedArgs: null);
            completeWithResult(success);
        } else {
            Google.VersionHandler.InvokeStaticMethod(
                AndroidResolverClass, "Resolve", args: null,
                namedArgs: new Dictionary<string, object>() {
                    {"resolutionCompleteWithResult", completeWithResult}
                });
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
    private static void ResolveWithGradleTemplate(string gradleTemplate,
                                                  string expectedAssetsDir,
                                                  TestCase testCase,
                                                  Action<TestCaseResult> testCaseComplete,
                                                  IEnumerable<string> otherExpectedFiles = null,
                                                  bool deleteGradleTemplate = true,
                                                  ICollection<string> filesToIgnore = null) {
        var cleanUpFiles = new List<string>();
        if (deleteGradleTemplate) cleanUpFiles.Add(GRADLE_TEMPLATE_ENABLED);
        if (otherExpectedFiles != null) cleanUpFiles.AddRange(otherExpectedFiles);
        Action cleanUpTestCase = () => {
            foreach (var filename in cleanUpFiles) {
                if (File.Exists(filename)) File.Delete(filename);
            }
        };
        try {
            File.Copy(gradleTemplate, GRADLE_TEMPLATE_ENABLED);
            Resolve("Gradle", false, expectedAssetsDir, null, filesToIgnore, testCase,
                    (TestCaseResult testCaseResult) => {
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
            var testCaseResult = new TestCaseResult(testCase);
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
        bool successful = (bool)AndroidResolverClass.GetMethod(
            "ExtractZip", BindingFlags.Static | BindingFlags.NonPublic).Invoke(
                null, new object[]{ zipFile, null, outputDir, false });
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
                    foreach (var extension in new[] { ".xml", ".txt", ".gradle" }) {
                        if (resolvedExtension == extension) {
                            displayContents = true;
                            break;
                        }
                    }
                    if (displayContents) {
                        expectedContentsAsString =
                            System.Text.Encoding.Default.GetString(expectedContents);
                        resolvedContentsAsString =
                            System.Text.Encoding.Default.GetString(resolvedContents);
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

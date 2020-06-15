// <copyright file="Runner.cs" company="Google LLC">
// Copyright (C) 2020 Google Inc. All Rights Reserved.
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
using System.IO;
using System.Reflection;
using System.Xml;

using Google;

namespace Google.IntegrationTester {

    /// <summary>
    /// Should be applied to "static void Method()" methods that will be called when the Runner is
    /// ready to be initialized with test cases.
    /// </summary>
    public class InitializerAttribute : Attribute {}

    /// <summary>
    /// Can be applied to static methods that conform to TestCase.MethodDelegate which will add the
    /// methods to the set of tests cases to execute.
    /// </summary>
    public class TestCaseAttribute : Attribute {}

    /// <summary>
    /// Runs a series of asynchronous test cases in the Unity Editor.
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    public static class Runner {

        /// <summary>
        /// Executed test case names and failure messages (if any).
        /// </summary>
        private static List<TestCaseResult> testCaseResults = new List<TestCaseResult>();

        /// <summary>
        /// Set of test cases to execute.
        /// </summary>
        private static List<TestCase> testCases = new List<TestCase>();

        /// <summary>
        /// Backing store for UnityVersion.
        /// </summary>
        private static float unityVersion;

        /// <summary>
        /// Get the current Unity version.
        /// </summary>
        public static float UnityVersion { get { return unityVersion; } }

        /// <summary>
        /// Whether the default initializer was called.
        /// </summary>
        private static bool defaultInitializerCalled = false;

        /// <summary>
        /// Whether the default test case was called.
        /// </summary>
        private static bool defaultTestCaseCalled = false;

        /// <summary>
        /// File to store snapshot of test case results.
        /// </summary>
        private static string TestCaseResultsFilename = "Temp/GvhRunnerTestCaseResults.xml";

        /// <summary>
        /// Register a method to call when the Version Handler has enabled all plugins in the
        /// project.
        /// </summary>
        static Runner() {
            // Disable stack traces for more condensed logs.
            try {
                foreach (var logType in
                         new [] { UnityEngine.LogType.Log, UnityEngine.LogType.Warning }) {
                    // Unity 2017 and above have the Application.SetStackTraceLogType to configure
                    // stack traces per log level.
                VersionHandler.InvokeStaticMethod(
                        typeof(UnityEngine.Application), "SetStackTraceLogType",
                        new object[] { logType, UnityEngine.StackTraceLogType.None });
                }
            } catch (Exception) {
                // Fallback to the legacy method.
                UnityEngine.Application.stackTraceLogType = UnityEngine.StackTraceLogType.None;
            }

            UnityEngine.Debug.Log("Set up callback on Version Handler completion.");
            Google.VersionHandler.UpdateCompleteMethods = new [] {
                "Google.IntegrationTester:Google.IntegrationTester.Runner:VersionHandlerReady"
            };
            UnityEngine.Debug.Log("Enable plugin using the Version Handler.");
            Google.VersionHandler.UpdateNow();
        }

        /// <summary>
        /// Add a set of test cases to the list to be executed.
        /// </summary>
        /// <param name="tests">Test cases to add to the list to execute.</param>
        public static void ScheduleTestCases(IEnumerable<TestCase> tests) {
            testCases.AddRange(tests);
        }

        /// <summary>
        /// Add a single test case to the list to be executed.
        /// </summary>
        /// <param name="test">Test case to add to the list to execute.</param>
        public static void ScheduleTestCase(TestCase test) {
            testCases.Add(test);
        }

        /// <summary>
        /// Called when the Version Handler has enabled all managed plugins in a project.
        /// </summary>
        public static void VersionHandlerReady() {
            UnityEngine.Debug.Log("VersionHandler is ready.");
            Google.VersionHandler.UpdateCompleteMethods = null;
            // Start executing tests.
            ConfigureTestCases();
            RunOnMainThread.Run(() => { ExecuteNextTestCase(); }, runNow: false);
        }

        /// <summary>
        /// Configure tests to run.
        /// </summary>
        /// <remarks>
        /// Finds and calls all initialization methods with the InitializerAttribute.
        /// </remarks>
        private static void ConfigureTestCases() {
            unityVersion = Google.VersionHandler.GetUnityVersionMajorMinor();

            // Gather test initializers and test case methods.
            var initializerMethods = new List<MethodInfo>();
            var testCaseMethods = new List<MethodInfo>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                foreach (var type in assembly.GetTypes()) {
                    IEnumerable<MethodInfo> methods;
                    try {
                        methods = type.GetMethods();
                    } catch (Exception) {
                        // TargetInvocationException, TypeLoadException and others can be thrown
                        // when retrieving the methods of some .NET assemblies
                        // (e.g System.Web.UI.WebControls.ModelDataSourceView) so ignore them.
                        continue;
                    }
                    foreach (var method in methods) {
                        foreach (var attribute in method.GetCustomAttributes(true)) {
                            if (attribute is InitializerAttribute) {
                                initializerMethods.Add(method);
                                break;
                            } else if (attribute is TestCaseAttribute) {
                                testCaseMethods.Add(method);
                                break;
                            }
                        }
                    }
                }
            }

            bool initializationSuccessful = true;
            foreach (var initializer in initializerMethods) {
                try {
                    initializer.Invoke(null, null);
                } catch (Exception e) {
                    UnityEngine.Debug.Log(String.Format("FAILED: Unable to initialize {0} ({1})",
                                                        initializer.Name, e));
                    initializationSuccessful = false;
                }
            }

            // Try adding test cases to the list to execute.
            foreach (var testCaseMethod in testCaseMethods) {
                try {
                    var testCaseMethodDelegate = (TestCase.MethodDelegate)Delegate.CreateDelegate(
                        typeof(TestCase.MethodDelegate), null, testCaseMethod, true);
                    ScheduleTestCase(new TestCase() {
                            Name = testCaseMethod.Name,
                            Method = testCaseMethodDelegate
                        });
                } catch (Exception e) {
                    UnityEngine.Debug.Log(String.Format(
                        "FAILED: Test case {0} does not implement TestCase.MethodDelegate ({1})",
                        testCaseMethod.Name, e));
                    initializationSuccessful = false;
                }
            }

            // Restore for all executed test cases, restore results and remove all pending test
            // cases that are complete.
            var executedTestCaseNames = new HashSet<string>();
            foreach (var executedTestCase in ReadTestCaseResults()) {
                testCaseResults.Add(executedTestCase);
                executedTestCaseNames.Add(executedTestCase.TestCaseName);
            }
            var filteredTestCases = new List<TestCase>();
            foreach (var testCase in testCases) {
                if (!executedTestCaseNames.Contains(testCase.Name)) {
                    filteredTestCases.Add(testCase);
                }
            }
            defaultTestCaseCalled = executedTestCaseNames.Contains("DefaultTestCase");
            testCases = filteredTestCases;

            if (!defaultInitializerCalled) {
                UnityEngine.Debug.Log("FAILED: Default Initializer not called.");
                initializationSuccessful = false;
            }

            if (!initializationSuccessful) Exit(false);
        }

        /// <summary>
        /// Default initializer to test the Initializer attribute.
        /// </summary>
        [Initializer]
        public static void DefaultInitializer() {
            defaultInitializerCalled = true;
        }

        /// <summary>
        /// Default test case to test the TestCase attribute.
        /// </summary>
        /// <param name="testCase">Object executing this method.</param>
        /// <param name="testCaseComplete">Called when the test case is complete.</param>
        [TestCase]
        public static void DefaultTestCase(TestCase testCase,
                                           Action<TestCaseResult> testCaseComplete) {
            defaultTestCaseCalled = true;
            testCaseComplete(new TestCaseResult(testCase));
        }

        /// <summary>
        /// Exit the application if the -gvh_noexitontestcompletion command line flag isn't set.
        /// </summary>
        /// <param name="passed">Whether the tests passed.</param>
        private static void Exit(bool passed) {
            if (!Environment.CommandLine.ToLower().Contains("-gvh_noexitontestcompletion")) {
                UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        /// <summary>
        /// Log test result summary and quit the application.
        /// </summary>
        public static void LogSummaryAndExit() {
            bool passed = true;
            var testSummaryLines = new List<string>();
            if (!defaultTestCaseCalled) {
                testSummaryLines.Add("Default test case not called");
                passed = false;
            }
            foreach (var testCaseResult in testCaseResults) {
                testSummaryLines.Add(testCaseResult.FormatString(false));
                passed &= testCaseResult.Succeeded;
            }
            UnityEngine.Debug.Log(String.Format("Test(s) {0}.\n{1}", passed ? "PASSED" : "FAILED",
                                                String.Join("\n", testSummaryLines.ToArray())));
            Exit(passed);
        }

        /// <summary>
        /// Read test case results from the journal.
        /// </summary>
        /// <returns>List of TestCaseResults.</returns>
        private static List<TestCaseResult> ReadTestCaseResults() {
            var readTestCaseResults = new List<TestCaseResult>();
            if (!File.Exists(TestCaseResultsFilename)) return readTestCaseResults;

            bool successful = XmlUtilities.ParseXmlTextFileElements(
                TestCaseResultsFilename, new Logger(),
                (XmlTextReader reader, string elementName, bool isStart, string parentElementName,
                 List<string> elementNameStack) => {
                    TestCaseResult currentTestCaseResult = null;
                    int testCaseResultsCount = readTestCaseResults.Count;
                    if (testCaseResultsCount > 0) {
                        currentTestCaseResult = readTestCaseResults[testCaseResultsCount - 1];
                    }
                    if (elementName == "TestCaseResults" && parentElementName == "") {
                        if (isStart) {
                            readTestCaseResults.Clear();
                        }
                        return true;
                    } else if (elementName == "TestCaseResult" &&
                               parentElementName == "TestCaseResults") {
                        if (isStart) {
                            readTestCaseResults.Add(new TestCaseResult(new TestCase()));
                        }
                        return true;
                    } else if (elementName == "TestCaseName" &&
                               parentElementName == "TestCaseResult") {
                        if (isStart && reader.Read() && reader.NodeType == XmlNodeType.Text) {
                            currentTestCaseResult.TestCaseName = reader.ReadContentAsString();
                        }
                        return true;
                    } else if (elementName == "Skipped" && parentElementName == "TestCaseResult") {
                        if (isStart && reader.Read() && reader.NodeType == XmlNodeType.Text) {
                            currentTestCaseResult.Skipped = reader.ReadContentAsBoolean();
                        }
                        return true;
                    } else if (elementName == "ErrorMessages" &&
                               parentElementName == "TestCaseResult") {
                        return true;
                    } else if (elementName == "ErrorMessage" &&
                               parentElementName == "ErrorMessages") {
                        if (isStart && reader.Read() && reader.NodeType == XmlNodeType.Text) {
                            currentTestCaseResult.ErrorMessages.Add(reader.ReadContentAsString());
                        }
                        return true;
                    }
                    return false;
                });
            if (!successful) {
                UnityEngine.Debug.LogWarning(
                    String.Format("Failed while reading {0}, test execution will restart if the " +
                                  "app domain is reloaded.", TestCaseResultsFilename));
            }
            return readTestCaseResults;
        }

        /// <summary>
        /// Log a test case result to the journal so that it isn't executed again if the app
        /// domain is reloaded.
        /// </summary>
        private static bool WriteTestCaseResult(TestCaseResult testCaseResult) {
            var existingTestCaseResults = ReadTestCaseResults();
            existingTestCaseResults.Add(testCaseResult);
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(TestCaseResultsFilename));
                using (var writer = new XmlTextWriter(new StreamWriter(TestCaseResultsFilename)) {
                        Formatting = Formatting.Indented
                    }) {
                    writer.WriteStartElement("TestCaseResults");
                    foreach (var result in existingTestCaseResults) {
                        writer.WriteStartElement("TestCaseResult");
                        if (!String.IsNullOrEmpty(result.TestCaseName)) {
                            writer.WriteStartElement("TestCaseName");
                            writer.WriteValue(result.TestCaseName);
                            writer.WriteEndElement();
                        }
                        writer.WriteStartElement("Skipped");
                        writer.WriteValue(result.Skipped);
                        writer.WriteEndElement();
                        if (result.ErrorMessages.Count > 0) {
                            writer.WriteStartElement("ErrorMessages");
                            foreach (var errorMessage in result.ErrorMessages) {
                                writer.WriteStartElement("ErrorMessage");
                                writer.WriteValue(errorMessage);
                                writer.WriteEndElement();
                            }
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.Flush();
                    writer.Close();
                }
            } catch (Exception e) {
                UnityEngine.Debug.LogWarning(
                    String.Format("Failed while writing {0} ({1}), test execution will restart " +
                                  "if the app domain is reloaded.", TestCaseResultsFilename, e));
                return false;
            }
            return true;
        }

        /// <summary>
        /// Log a test case result with error details.
        /// </summary>
        /// <param name="testCaseResult">Result to log.</param>
        public static void LogTestCaseResult(TestCaseResult testCaseResult) {
            testCaseResults.Add(testCaseResult);
            UnityEngine.Debug.Log(testCaseResult.FormatString(true));
            WriteTestCaseResult(testCaseResult);
        }

        /// <summary>
        /// Execute a function for a test case catching any exceptions and logging the result.
        /// </summary>
        /// <param name="testCase">Object executing this method.</param>
        /// <param name="testCaseAction">Action to execute.</param>
        /// <param name="executeNext">Whether to execute the next test case if the specified action
        /// fails.</param>
        /// <returns>true if the action executed without any exceptions, false otherwise.</returns>
        public static bool ExecuteTestCase(TestCase testCase, Action testCaseAction,
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
            if (!succeeded && executeNext) {
                RunOnMainThread.Run(() => { ExecuteNextTestCase(); });
            }
            return succeeded;
        }

        /// <summary>
        /// Execute the next queued test case.
        /// </summary>
        private static void ExecuteNextTestCase() {
            bool executeNext;
            do {
                executeNext = false;
                UnityEngine.Debug.Log(String.Format("Remaining test cases {0}", testCases.Count));
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
                                    RunOnMainThread.Run(() => { ExecuteNextTestCase(); });
                                });
                        }, false);
                } else {
                    LogSummaryAndExit();
                }
            } while (executeNext);
        }
    }
}

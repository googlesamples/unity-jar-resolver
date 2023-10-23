// <copyright file="PackageManagerClientTests.cs" company="Google LLC">
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
using System.Collections;
using System.Reflection;
using System.IO;

using Google;

namespace Google.PackageManagerClientIntegrationTests {

/// <summary>
/// Integration tests for PackageManagerClient.
/// </summary>
public static class PackageManagerClientTests {

    /// <summary>
    /// Whether the Unity Package Manager is available in the current version of Unity.
    /// </summary>
    private static bool UpmAvailable {
        get { return ExecutionEnvironment.VersionMajorMinor >= 2017.3; }
    }

    /// <summary>
    /// Whether the SearchAll method is available in the Unity Package Manager.
    /// </summary>
    private static bool UpmSearchAllAvailable {
        get { return ExecutionEnvironment.VersionMajorMinor >= 2018.0; }
    }

    /// <summary>
    /// Determine whether the available method is returning the package manager in the current
    /// version of Unity.
    /// </summary>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called when the test case is complete.</param>
    [IntegrationTester.TestCase]
    public static void TestAvailable(IntegrationTester.TestCase testCase,
                                     Action<IntegrationTester.TestCaseResult> testCaseComplete) {
        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);
        if (UpmAvailable != PackageManagerClient.Available) {
            testCaseResult.ErrorMessages.Add(String.Format("PackageManagerClient.Available " +
                                                           "returned {0}, expected {1}",
                                                           PackageManagerClient.Available,
                                                           UpmAvailable));
        }
        testCaseComplete(testCaseResult);
    }

    /// <summary>
    /// Convert a list of packages to a list of strings.
    /// </summary>
    /// <param name="packageInfos">List of PackageInfo instances to convert to strings.</param>
    /// <returns>List of string representation of the specified packages.</returns>
    private static List<string> PackageInfoListToStringList(
            IEnumerable<PackageManagerClient.PackageInfo> packageInfos) {
        var packageInfosAsStrings = new List<string>();
        if (packageInfos != null) {
            foreach (var pkg in packageInfos) packageInfosAsStrings.Add(pkg.ToString());
        }
        return packageInfosAsStrings;
    }

    /// <summary>
    /// Convert a list of packages to a list of string package names.
    /// </summary>
    /// <param name="packageInfos">List of PackageInfo instances to convert to strings.</param>
    /// <returns>List of package names of the specified packages.</returns>
    private static List<string> PackageInfoListToNameList(
            IEnumerable<PackageManagerClient.PackageInfo> packageInfos) {
        var packageInfosAsStrings = new List<string>();
        if (packageInfos != null) {
            foreach (var pkg in packageInfos) packageInfosAsStrings.Add(pkg.Name);
        }
        return packageInfosAsStrings;
    }

    /// <summary>
    /// Make sure a set of package names are present in the specified list of PackageInfo objects.
    /// </summary>
    /// <param name="expectedPackageNames">List of package names expected in the list.</param>
    /// <param name="packageInfos">List of PackageInfo instances to search.</param>
    /// <param name="testCaseResult">TestCaseResult to add an error message to if the expected
    /// packages aren't found in the packageInfos list.</param>
    /// <param name="errorMessagePrefix">String to add to the start of the error message if
    /// expectedPackageNames are not in packageInfos.</param>
    private static void CheckPackageNamesInPackageInfos(
            List<string> expectedPackageNames,
            IEnumerable<PackageManagerClient.PackageInfo> packageInfos,
            IntegrationTester.TestCaseResult testCaseResult,
            string errorMessagePrefix) {
        var packageNames = PackageInfoListToNameList(packageInfos);
        if (!(new HashSet<string>(packageNames)).IsSupersetOf(expectedPackageNames)) {
            testCaseResult.ErrorMessages.Add(String.Format(
                "{0}, package names [{1}] not found in:\n{2}\n",
                errorMessagePrefix, String.Join(", ", expectedPackageNames.ToArray()),
                String.Join("\n", packageNames.ToArray())));
        }
    }

    /// <summary>
    /// List packages installed in the project.
    /// </summary>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called when the test case is complete.</param>
    [IntegrationTester.TestCase]
    public static void TestListInstalledPackages(
            IntegrationTester.TestCase testCase,
            Action<IntegrationTester.TestCaseResult> testCaseComplete) {
        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);
        PackageManagerClient.ListInstalledPackages((result) => {
                // Unity 2017.x doesn't install any default packages.
                if (ExecutionEnvironment.VersionMajorMinor >= 2018.0) {
                    // Make sure a subset of the default packages installed in all a newly created
                    // Unity project are present in the returned list.
                    CheckPackageNamesInPackageInfos(
                        new List<string>() {
                            "com.unity.modules.audio",
                            "com.unity.modules.physics"
                        },
                        result.Packages, testCaseResult, "Found an unexpected set of packages");
                }
                var message = String.Format(
                    "Error: '{0}', PackageInfos:\n{1}\n",
                    result.Error,
                    String.Join("\n", PackageInfoListToStringList(result.Packages).ToArray()));
                if (!String.IsNullOrEmpty(result.Error.ToString())) {
                    testCaseResult.ErrorMessages.Add(message);
                } else {
                    UnityEngine.Debug.Log(message);
                }
                testCaseComplete(testCaseResult);
            });
    }

    /// <summary>
    /// Search for all available packages.
    /// </summary>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called when the test case is complete.</param>
    [IntegrationTester.TestCase]
    public static void TestSearchAvailablePackagesAll(
            IntegrationTester.TestCase testCase,
            Action<IntegrationTester.TestCaseResult> testCaseComplete) {
        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);
        PackageManagerClient.SearchAvailablePackages(
            (result) => {
                // Make sure common optional Unity packages are returned in the search result.
                if (UpmSearchAllAvailable) {
                    CheckPackageNamesInPackageInfos(
                        new List<string>() {
                            "com.unity.2d.animation",
                            "com.unity.test-framework"
                        },
                        result.Packages, testCaseResult,
                        "SearchAvailablePackages returned an unexpected set of packages");
                }

                var message = String.Format(
                    "Error: '{0}', PackageInfos:\n{1}\n", result.Error,
                    String.Join("\n", PackageInfoListToStringList(result.Packages).ToArray()));
                if (!String.IsNullOrEmpty(result.Error.ToString())) {
                    testCaseResult.ErrorMessages.Add(message);
                } else {
                    UnityEngine.Debug.Log(message);
                }
                testCaseComplete(testCaseResult);
            });
    }

    /// <summary>
    /// Search for a set of available packages.
    /// </summary>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called when the test case is complete.</param>
    [IntegrationTester.TestCase]
    public static void TestSearchAvailablePackages(
            IntegrationTester.TestCase testCase,
            Action<IntegrationTester.TestCaseResult> testCaseComplete) {
        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);
        var progressLines = new List<string>();
        PackageManagerClient.SearchAvailablePackages(
            new [] {
                "com.unity.ads",
                "com.unity.analytics@2.0.13"
            },
            (result) => {
                var expectedPackageNameByQuery = UpmAvailable ?
                    new Dictionary<string, string>() {
                        {"com.unity.ads", "com.unity.ads"},
                        {"com.unity.analytics@2.0.13", "com.unity.analytics"},
                    } : new Dictionary<string, string>();

                // Make sure all expected queries were performed.
                var queriesPerformed = new HashSet<string>(result.Keys);
                if (!queriesPerformed.SetEquals(expectedPackageNameByQuery.Keys)) {
                    testCaseResult.ErrorMessages.Add(
                        String.Format(
                            "Search returned a subset of queries [{0}] vs. expected [{1}]",
                            String.Join(", ", (new List<string>(queriesPerformed)).ToArray()),
                            String.Join(", ", (new List<string>(
                                expectedPackageNameByQuery.Keys)).ToArray())));
                }

                var packageResults = new List<string>();
                foreach (var kv in result) {
                    var searchQuery = kv.Key;
                    var searchResult = kv.Value;
                    packageResults.Add(String.Format(
                        "{0}, Error: '{1}':\n{2}\n", searchQuery, searchResult.Error,
                        String.Join("\n",
                                    PackageInfoListToStringList(searchResult.Packages).ToArray())));

                    if (!String.IsNullOrEmpty(searchResult.Error.ToString())) {
                        testCaseResult.ErrorMessages.Add(
                            String.Format("Failed when searching for '{0}', Error '{1}'",
                                          searchQuery, searchResult.Error.ToString()));
                    }

                    // Make sure returned packages match the search pattern.
                    string expectedPackageName;
                    if (expectedPackageNameByQuery.TryGetValue(searchQuery,
                                                               out expectedPackageName)) {
                        CheckPackageNamesInPackageInfos(
                            new List<string>() { expectedPackageName }, searchResult.Packages,
                            testCaseResult,
                            String.Format("Returned an unexpected list of for search query '{0}'",
                                          searchQuery));
                    } else {
                        testCaseResult.ErrorMessages.Add(
                            String.Format("Unexpected search result returned '{0}'", searchQuery));
                    }
                }

                // Make sure progress was reported.
                if (progressLines.Count == 0) {
                    testCaseResult.ErrorMessages.Add("No progress reported");
                }

                var message = String.Format(String.Join("\n", packageResults.ToArray()));
                if (testCaseResult.ErrorMessages.Count == 0) {
                    UnityEngine.Debug.Log(message);
                } else {
                    testCaseResult.ErrorMessages.Add(message);
                }
                testCaseComplete(testCaseResult);
            },
            progress: (value, item) => {
                progressLines.Add(String.Format("Progress: {0}: {1}", value, item));
            });
    }

    /// <summary>
    /// Check a package manager change result for errors.
    /// </summary>
    /// <param name="error">Error to check.</param>
    /// <param name="packageName">Name of the changed package.</param>
    /// <param name="expectedPackageName">Expected installed / removed package name.</param>
    /// <param name="testCaseResult">TestCaseResult to add an error message to if the result
    /// indicates a failure or doesn't match the expected package name.</param>
    /// <returns>String description of the result.</returns>
    private static string CheckChangeResult(
            PackageManagerClient.Error error, string packageName,
            string expectedPackageName, IntegrationTester.TestCaseResult testCaseResult) {
        var message = String.Format("Error '{0}', Package Installed '{1}'", error, packageName);
        if (!String.IsNullOrEmpty(error.ToString())) {
            testCaseResult.ErrorMessages.Add(message);
        }

        if (packageName != expectedPackageName) {
            testCaseResult.ErrorMessages.Add(String.Format(
                "Unexpected package installed '{0}' vs. '{1}', Error '{2}'",
                packageName, expectedPackageName, error));
        }
        return message;
    }

    /// <summary>
    /// Add a package.
    /// </summary>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called when the test case is complete.</param>
    [IntegrationTester.TestCase]
    public static void TestAddPackage(
            IntegrationTester.TestCase testCase,
            Action<IntegrationTester.TestCaseResult> testCaseComplete) {
        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);
        const string installPackage = "com.unity.analytics";
        PackageManagerClient.AddPackage(
            installPackage,
            (result) => {
                var message = UpmAvailable ? CheckChangeResult(
                    result.Error, result.Package != null ? result.Package.Name : null,
                    installPackage, testCaseResult) : "";
                if (testCaseResult.ErrorMessages.Count == 0) {
                    UnityEngine.Debug.Log(message);
                }
                testCaseComplete(testCaseResult);
            });
    }

    /// <summary>
    /// Add and remove a package.
    /// </summary>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called when the test case is complete.</param>
    [IntegrationTester.TestCase]
    public static void TestAddAndRemovePackage(
            IntegrationTester.TestCase testCase,
            Action<IntegrationTester.TestCaseResult> testCaseComplete) {
        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);
        const string packageToModify = "com.unity.ads";
        PackageManagerClient.AddPackage(
            packageToModify,
            (result) => {
                if (UpmAvailable) {
                    CheckChangeResult(result.Error,
                                      result.Package != null ? result.Package.Name : null,
                                      packageToModify, testCaseResult);
                }
            });

        PackageManagerClient.RemovePackage(
            packageToModify,
            (result) => {
                var message = UpmAvailable ?
                    CheckChangeResult(result.Error, result.PackageId, packageToModify,
                                      testCaseResult) : "";
                if (testCaseResult.ErrorMessages.Count == 0) {
                    UnityEngine.Debug.Log(message);
                }
                testCaseComplete(testCaseResult);
            });
    }
}


}

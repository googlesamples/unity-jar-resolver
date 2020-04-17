// <copyright file="PackageMigratorIntegrationTests.cs" company="Google LLC">
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

// Remove this define when EDM with package migration is available on the Unity Package Manager.
#define EDM_WITH_MIGRATION_NOT_AVAILABLE_ON_UPM

using System;
using System.Collections.Generic;

using Google;

namespace Google.PackageMigratorIntegrationTests {

/// <summary>
/// Integration tests for PackageMigrator.
/// </summary>
public static class PackageManagerTests {

    /// <summary>
    /// Initialize logging and expose GPR to UPM.
    /// </summary>
    [IntegrationTester.Initializer]
    public static void Initialize() {
        // Enable verbose logging.
        PackageManagerResolver.logger.Level = LogLevel.Verbose;

        // Ensure the game package registry is added for the test.
        PackageManagerResolver.UpdateManifest(
            PackageManagerResolver.ManifestModificationMode.Add,
            promptBeforeAction: false,
            showDisableButton: false);
    }

    /// <summary>
    /// If UPM scoped registries aren't available, expect a task failure or report an error and
    /// complete the specified test.
    /// </summary>
    /// <param name="completionError">Error string returned by a completed task.</param>
    /// <param name="testCaseResult">Test case result to update.</param>
    /// <param name="testCaseComplete">Called when the test case is complete.</param>
    /// <returns>true if the task is complete, false otherwise.</returns>
    private static bool CompleteIfNotAvailable(
            string completionError,
            IntegrationTester.TestCaseResult testCaseResult,
            Action<IntegrationTester.TestCaseResult> testCaseComplete) {
        // If scoped registries support isn't available, expect this to fail.
        if (!(PackageManagerClient.Available &&
              PackageManagerResolver.ScopedRegistriesSupported)) {
            if (String.IsNullOrEmpty(completionError)) {
                testCaseResult.ErrorMessages.Add("Expected failure but returned no error");
            }
            testCaseComplete(testCaseResult);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Test searching for an EDM package to migrate that is the same version or newer than the
    /// installed package.
    /// </summary>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called when the test case is complete.</param>
    [IntegrationTester.TestCase]
    public static void TestFindPackagesToMigrate(
            IntegrationTester.TestCase testCase,
            Action<IntegrationTester.TestCaseResult> testCaseComplete) {
        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);

        // Collect progress reports.
        var progressLog = new List<KeyValuePair<float, string>>();
        PackageMigrator.PackageMap.FindPackagesProgressDelegate reportFindProgress =
            (progressValue, description) => {
            UnityEngine.Debug.Log(String.Format("Find progress {0}, {1}", progressValue,
                                                description));
            progressLog.Add(new KeyValuePair<float, string>(progressValue, description));
        };

        PackageMigrator.PackageMap.FindPackagesToMigrate((error, packageMaps) => {
                if (CompleteIfNotAvailable(error, testCaseResult, testCaseComplete)) return;

                if (!String.IsNullOrEmpty(error)) {
                    testCaseResult.ErrorMessages.Add(String.Format("Failed with error {0}", error));
                }

                var packageMapStrings = new List<string>();
                var packageMapsByUpmName = new Dictionary<string, PackageMigrator.PackageMap>();
                foreach (var packageMap in packageMaps) {
                    packageMapsByUpmName[packageMap.AvailablePackageManagerPackageInfo.Name] =
                        packageMap;
                    packageMapStrings.Add(packageMap.ToString());
                }

                // Version-less mapping of UPM package name to Version Handler package names.
                var expectedVhNameAndUpmNames = new KeyValuePair<string, string>[] {
                    new KeyValuePair<string, string>("External Dependency Manager",
                                                     "com.google.external-dependency-manager"),
                    new KeyValuePair<string, string>("Firebase Authentication",
                                                     "com.google.firebase.auth")
                };
                foreach (var vhNameAndUpmName in expectedVhNameAndUpmNames) {
                    string expectedVhName = vhNameAndUpmName.Key;
                    string expectedUpmName = vhNameAndUpmName.Value;
                    PackageMigrator.PackageMap packageMap;
                    if (packageMapsByUpmName.TryGetValue(expectedUpmName, out packageMap)) {
                        if (packageMap.VersionHandlerPackageName != expectedVhName) {
                            testCaseResult.ErrorMessages.Add(String.Format(
                                "Unexpected Version Handler package name '{0}' vs. '{1}' for '{2}'",
                                packageMap.VersionHandlerPackageName, expectedVhName,
                                expectedUpmName));
                        }
                        if (packageMap.AvailablePackageManagerPackageInfo.CalculateVersion() <
                            packageMap.VersionHandlerPackageCalculatedVersion) {
                            testCaseResult.ErrorMessages.Add(String.Format(
                                "Returned older UPM package {0} than VH package {1} for " +
                                "{2} --> {3}",
                                packageMap.AvailablePackageManagerPackageInfo.Version,
                                packageMap.VersionHandlerPackageVersion, expectedVhName,
                                expectedUpmName));
                        }
                    } else {
                        testCaseResult.ErrorMessages.Add(String.Format(
                            "Package map {0} --> {1} not found.", expectedVhName, expectedUpmName));
                    }
                }

                if (packageMaps.Count != expectedVhNameAndUpmNames.Length) {
                    testCaseResult.ErrorMessages.Add(
                        String.Format("Migrator returned unexpected package maps:\n{0}",
                                      String.Join("\n", packageMapStrings.ToArray())));
                }

                if (progressLog.Count == 0) {
                    testCaseResult.ErrorMessages.Add("No progress updates");
                }
                testCaseComplete(testCaseResult);
            }, reportFindProgress);
    }

    /// <summary>
    /// Test migration.
    /// </summary>
    /// <param name="testCase">Object executing this method.</param>
    /// <param name="testCaseComplete">Called when the test case is complete.</param>
    [IntegrationTester.TestCase]
    public static void TestMigration(
            IntegrationTester.TestCase testCase,
            Action<IntegrationTester.TestCaseResult> testCaseComplete) {
#if EDM_WITH_MIGRATION_NOT_AVAILABLE_ON_UPM
        testCaseComplete(new IntegrationTester.TestCaseResult(testCase) { Skipped = true });
        return;
#endif
        var testCaseResult = new IntegrationTester.TestCaseResult(testCase);
        PackageMigrator.TryMigration((error) => {
                if (CompleteIfNotAvailable(error, testCaseResult, testCaseComplete)) return;
                if (!String.IsNullOrEmpty(error)) {
                    testCaseResult.ErrorMessages.Add(String.Format(
                        "Migration failed with error {0}", error));
                }

                // Make sure only expected version handler packages are still installed.
                var expectedVersionHandlerPackages = new HashSet<string>() {
                    "Firebase Realtime Database"
                };
                var manifestsByPackageName = new HashSet<string>(
                    VersionHandlerImpl.ManifestReferences.
                        FindAndReadManifestsInAssetsFolderByPackageName().Keys);
                manifestsByPackageName.ExceptWith(expectedVersionHandlerPackages);
                if (manifestsByPackageName.Count > 0) {
                    testCaseResult.ErrorMessages.Add(String.Format(
                        "Unexpected version handler packages found in the project:\n{0}",
                        (new List<string>(manifestsByPackageName)).ToArray()));
                }

                // Make sure the expected UPM packages are installed.
                PackageManagerClient.ListInstalledPackages((listResult) => {
                        var installedPackageNames = new HashSet<string>();
                        foreach (var pkg in listResult.Packages) {
                            installedPackageNames.Add(pkg.Name);
                        }

                        // Make sure expected UPM packages are installed.
                        var expectedPackageNames = new List<string>() {
                            "com.google.external-dependency-manager",
                            "com.google.firebase.auth"
                        };
                        if (installedPackageNames.IsSupersetOf(expectedPackageNames)) {
                            testCaseResult.ErrorMessages.Add(String.Format(
                                "Expected packages [{0}] not installed",
                                String.Join(", ", expectedPackageNames.ToArray())));
                        }

                        testCaseComplete(testCaseResult);
                    });
            });
    }
}

}

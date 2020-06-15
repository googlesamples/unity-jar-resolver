// <copyright file="AndroidSdkManager.cs" company="Google Inc.">
// Copyright (C) 2017 Google Inc. All Rights Reserved.
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
    using Google;
    using Google.JarResolver;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Subset of Android SDK package metadata required for installation.
    /// </summary>
    internal class AndroidSdkPackageNameVersion {
        /// Converts and old "android" package manager package name to a new "sdkmanager" package
        /// name.
        private static Dictionary<string, string> OLD_TO_NEW_PACKAGE_NAME_PREFIX =
            new Dictionary<string, string> { { "extra;", "extras;" } };
        /// List package name components to not convert hyphens / semi-colons.
        /// Some package names contain hyphens and therefore can't simply be converted by
        /// replacing - with ;.  This list of name components is used to preserved when
        /// converting between new and legacy package names.
        /// This list was dervied from:
        /// sdkmanager --verbose --list | \
        ///    grep -vE '^(Info:|  |---)' | \
        ///    grep '-' | \
        ///    tr ';' '\n' | \
        ///    sort | \
        ///    uniq | \
        ///    grep '-' | \
        ///    sed 's/.*/"&",/'
        private static List<Regex> PRESERVED_PACKAGE_NAME_COMPONENTS = new List<Regex> {
            new Regex("\\d+.\\d+.\\d+-[a-zA-Z0-9]+"),
            new Regex("add-ons"),
            new Regex("addon-google_apis-google-\\d+"),
            new Regex("android-\\d+"),
            new Regex("arm64-v8a"),
            new Regex("armeabi-v7a"),
            new Regex("build-tools"),
            new Regex("constraint-layout"),
            new Regex("constraint-layout-solver"),
            new Regex("ndk-bundle"),
            new Regex("platform-tools"),
            new Regex("system-images"),
            new Regex("support-[a-zA-Z0-9]+"),
        };

        /// <summary>
        /// Name of the package.
        /// </summary>
        public string Name { set; get; }

        /// <summary>
        /// Escape components that should not be converted by LegacyName.
        /// </summary>
        /// <returns>Escaped package name.</returns>
        private string EscapeComponents(string packageName) {
            foreach (var componentRegex in PRESERVED_PACKAGE_NAME_COMPONENTS) {
                var match = componentRegex.Match(packageName);
                if (match.Success) {
                    var prefix = packageName.Substring(0, match.Index);
                    var postfix = packageName.Substring(match.Index + match.Length);
                    // Exclamation marks are guaranteed - at the moment - to not be
                    // part of a package name / path.
                    packageName = prefix + match.Value.Replace("-", "!") + postfix;
                }
            }
            return packageName;
        }

        /// <summary>
        /// Un-escaped components that should not be converted by LegacyName.
        /// </summary>
        /// <returns>Un-escaped package name.</returns>
        private string UnescapeComponents(string packageName) {
            return packageName.Replace("!", "-");
        }

        /// <summary>
        /// Convert to / from a legacy package name.
        /// </summary>
        public string LegacyName {
            set {
                var packageName = UnescapeComponents(EscapeComponents(value).Replace("-", ";"));
                foreach (var kv in OLD_TO_NEW_PACKAGE_NAME_PREFIX) {
                    if (packageName.StartsWith(kv.Key)) {
                        packageName = kv.Value + packageName.Substring(kv.Key.Length);
                        break;
                    }
                }
                Name = packageName;
            }

            get {
                var packageName = Name;
                foreach (var kv in OLD_TO_NEW_PACKAGE_NAME_PREFIX) {
                    if (packageName.StartsWith(kv.Value)) {
                        packageName = kv.Key + packageName.Substring(kv.Value.Length);
                        break;
                    }
                }
                return packageName.Replace(";", "-");
            }
        }

        /// <summary>
        /// Convert to / from a package path to name.
        /// </summary>
        /// Android SDK package names are derived from their path relative to the SDK directory.
        public string Path {
            set {
                Name = value.Replace("\\", "/").Replace("/", ";");
            }
            get {
                return Name.Replace(";", System.IO.Path.PathSeparator.ToString());
            }
        }

        /// <summary>
        /// String representation of the package version.
        /// </summary>
        public string VersionString { set; get; }

        /// <summary>
        /// 64-bit integer representation of the package version.
        /// </summary>
        public long Version { get { return ConvertVersionStringToInteger(VersionString); } }

        /// <summary>
        /// Get a string representation of this object.
        /// </summary>
        public override string ToString() {
            return String.Format("{0} ({1})", Name, VersionString);
        }

        /// <summary>
        /// Hash the name of this package.
        /// </summary>
        public override int GetHashCode() {
            return Name.GetHashCode();
        }

        /// <summary>
        /// Compares two package names.
        /// </summary>
        /// <param name="obj">Object to compare with.</param>
        /// <returns>true if both objects have the same name, false otherwise.</returns>
        public override bool Equals(System.Object obj) {
            var pkg = obj as AndroidSdkPackageNameVersion;
            return pkg != null && pkg.Name == Name;
        }

        /// <summary>
        /// Convert an N component version string into an integer.
        /// </summary>
        /// <param name="versionString">Version string to convert.</param>
        /// <param name="componentMultiplier">Value to multiply each component by.</param>
        /// <returns>An integer representation of the version string.</returns>
        public static long ConvertVersionStringToInteger(string versionString,
                                                         long componentMultiplier = 1000000) {
            if (String.IsNullOrEmpty(versionString)) return 0;
            var components = versionString.Split(new [] { '.' });
            long versionInteger = 0;
            long currentMultiplier = 1;
            Array.Reverse(components);
            foreach (var component in components) {
                long componentInteger = 0;
                try {
                    componentInteger = Convert.ToInt64(component);
                } catch (FormatException) {
                    PlayServicesResolver.Log(
                        String.Format("Unable to convert version string {0} to " +
                                      "integer value", versionString),
                        level: LogLevel.Warning);
                    return 0;
                }
                versionInteger += (componentInteger * currentMultiplier);
                currentMultiplier *= componentMultiplier;
            }
            return versionInteger;
        }

        /// <summary>
        /// Convert a list of package name / versions to a bulleted string list.
        /// </summary>
        /// <param name="packages">List of packages to write to a string.</param>
        /// <returns>Bulleted list of package name / versions.</returns>
        public static string ListToString(
                IEnumerable<AndroidSdkPackageNameVersion> packages) {
            var packageAndVersion = new List<string>();
            foreach (var pkg in packages) {
                packageAndVersion.Add(String.Format(
                    "* {0} {1}", pkg.Name,
                    !String.IsNullOrEmpty(pkg.VersionString) ?
                        String.Format("({0})", pkg.VersionString) : ""));
            }
            return String.Join("\n", packageAndVersion.ToArray());
        }
    }

    /// <summary>
    /// Describes an Android SDK package.
    /// </summary>
    internal class AndroidSdkPackage : AndroidSdkPackageNameVersion {

        /// <summary>
        /// Human readable description of the package.
        /// </summary>
        public string Description { set; get; }

        /// <summary>
        /// Whether the package is installed.
        /// </summary>
        public bool Installed { set; get; }

        /// <summary>
        /// Read package metadata from the source.properties file within the specified directory.
        /// </summary>
        /// <param name="sdkDirectory">Android SDK directory to query.</param>
        /// <param name="packageDirectory">Directory containing the package relative to
        /// sdkDirectory.</param>
        public static AndroidSdkPackage ReadFromSourceProperties(string sdkDirectory,
                                                                 string packageDirectory) {
            var propertiesPath = System.IO.Path.Combine(
                sdkDirectory, System.IO.Path.Combine(packageDirectory, "source.properties"));
            string propertiesText = null;
            try {
                propertiesText = File.ReadAllText(propertiesPath);
            } catch (Exception e) {
                PlayServicesResolver.Log(String.Format("Unable to read {0}\n{1}\n",
                                                      propertiesPath, e.ToString()),
                                         level: LogLevel.Verbose);
                return null;
            }
            // Unfortunately the package name is simply based upon the path within the SDK.
            var sdkPackage = new AndroidSdkPackage { Path = packageDirectory };
            const string VERSION_FIELD_NAME = "Pkg.Revision=";
            const string DESCRIPTION_FIELD_NAME = "Pkg.Desc=";
            foreach (var rawLine in CommandLine.SplitLines(propertiesText)) {
                var line = rawLine.Trim();
                // Ignore comments.
                if (line.StartsWith("#")) continue;
                // Parse fields
                if (line.StartsWith(VERSION_FIELD_NAME)) {
                    sdkPackage.VersionString = line.Substring(VERSION_FIELD_NAME.Length);
                } else if (line.StartsWith(DESCRIPTION_FIELD_NAME)) {
                    sdkPackage.Description = line.Substring(DESCRIPTION_FIELD_NAME.Length);
                }
            }
            return sdkPackage;
        }
    }

    /// <summary>
    /// Collection of AndroidSdkPackage instances indexed by package name.
    /// </summary>
    internal class AndroidSdkPackageCollection {
        private Dictionary<string, List<AndroidSdkPackage>> packages =
            new Dictionary<string, List<AndroidSdkPackage>>();

        /// <summary>
        /// Get the set of package names in the collection.
        /// </summary>
        public List<string> PackageNames {
            get { return new List<string>(packages.Keys); }
        }

        /// <summary>
        /// Get the list of package metadata by package name.
        /// </summary>
        /// <returns>List of package metadata.</returns>
        public List<AndroidSdkPackage> this[string packageName] {
            get {
                List<AndroidSdkPackage> packageList = null;
                if (!packages.TryGetValue(packageName, out packageList)) {
                    packageList = new List<AndroidSdkPackage>();
                    packages[packageName] = packageList;
                }
                return packageList;
            }
        }

        /// <summary>
        /// Get the most recent available version of a specified package.
        /// </summary>
        /// <returns>The package if it's available, null otherwise.</returns>
        public AndroidSdkPackage GetMostRecentAvailablePackage(string packageName) {
            var packagesByVersion = new SortedDictionary<long, AndroidSdkPackage>();
            foreach (var sdkPackage in this[packageName]) {
                packagesByVersion[sdkPackage.Version] = sdkPackage;
            }
            if (packagesByVersion.Count == 0) return null;
            AndroidSdkPackage mostRecentPackage = null;
            foreach (var pkg in packagesByVersion.Values) mostRecentPackage = pkg;
            return mostRecentPackage;
        }

        /// <summary>
        /// Get installed package metadata by package name.
        /// </summary>
        /// <returns>The package if it's installed, null otherwise.</returns>
        public AndroidSdkPackage GetInstalledPackage(string packageName) {
            foreach (var sdkPackage in this[packageName]) {
                if (sdkPackage.Installed) return sdkPackage;
            }
            return null;
        }
    }

    /// <summary>
    /// Interface used to interact with Android SDK managers.
    /// </summary>
    internal interface IAndroidSdkManager {
        /// <summary>
        /// Use the package manager to retrieve the set of installed and available packages.
        /// </summary>
        /// <param name="complete">Called when the query is complete.</param>
        void QueryPackages(Action<AndroidSdkPackageCollection> complete);

        /// <summary>
        /// Install a set of packages.
        /// </summary>
        /// <param name="packages">Set of packages to install / upgrade.</param>
        /// <param name="complete">Called when installation is complete.</param>
        void InstallPackages(HashSet<AndroidSdkPackageNameVersion> packages,
                             Action<bool> complete);
    }

    // Answers Android SDK manager license questions.
    internal class LicenseResponder : CommandLine.LineReader {
        // String to match in order to respond.
        private string question;
        // Response to provide to the question.
        private string response;

        /// <summary>
        /// Initialize the class to respond "yes" or "no" to license questions.
        /// </summary>
        /// <param name="question">Question to respond to.</param>
        /// <param name="response">Response to provide.</param>
        public LicenseResponder(string question, string response) {
            this.question = question;
            this.response = response;
            LineHandler += CheckAndRespond;
        }

        // Respond license questions with the "response".
        public void CheckAndRespond(Process process, StreamWriter stdin,
                                    CommandLine.StreamData data) {
            if (process.HasExited) return;

            if ((data.data != null && data.text.Contains(question)) ||
                CommandLine.LineReader.Aggregate(GetBufferedData(0)).text.Contains(question)) {
                Flush();
                // Ignore I/O exceptions as this could race with the process exiting.
                try {
                    foreach (byte b in System.Text.Encoding.UTF8.GetBytes(
                                 response + System.Environment.NewLine)) {
                        stdin.BaseStream.WriteByte(b);
                    }
                    stdin.BaseStream.Flush();
                } catch (System.IO.IOException) {
                }
            }
        }
    }

    /// <summary>
    /// Utility methods for implementations of IAndroidSdkManager.
    /// </summary>
    internal class SdkManagerUtil {

        /// <summary>
        /// Message displayed if a package query operation fails.
        /// </summary>
        const string PACKAGES_MISSING =
            "Unable to determine which Android packages are installed.\n{0}";

        /// <summary>
        /// Title of the installation dialog.
        /// </summary>
        private const string DIALOG_TITLE = "Installing Android SDK packages";

        /// <summary>
        /// Use the package manager to retrieve the set of installed and available packages.
        /// </summary>
        /// <param name="toolPath">Tool to run.</param>
        /// <param name="toolArguments">Arguments to pass to the tool.</param>
        /// <param name="complete">Called when the query is complete.</param>
        public static void QueryPackages(string toolPath, string toolArguments,
                                         Action<CommandLine.Result> complete) {
            PlayServicesResolver.analytics.Report("/androidsdkmanager/querypackages",
                                                  "Android SDK Manager: Query Packages");
            var window = CommandLineDialog.CreateCommandLineDialog(
                "Querying Android SDK packages");
            PlayServicesResolver.Log(String.Format("Query Android SDK packages\n" +
                                                   "\n" +
                                                   "{0} {1}\n",
                                                   toolPath, toolArguments),
                                     level: LogLevel.Verbose);
            window.summaryText = "Getting Installed Android SDK packages.";
            window.modal = false;
            window.progressTitle = window.summaryText;
            window.autoScrollToBottom = true;
            window.logger = PlayServicesResolver.logger;
            window.RunAsync(
                toolPath, toolArguments,
                (CommandLine.Result result) => {
                    window.Close();
                    if (result.exitCode == 0) {
                        PlayServicesResolver.analytics.Report(
                            "/androidsdkmanager/querypackages/success",
                            "Android SDK Manager: Query Packages Succeeded");
                    } else {
                        PlayServicesResolver.analytics.Report(
                            "/androidsdkmanager/querypackages/failed",
                            "Android SDK Manager: Query Packages Failed");
                        PlayServicesResolver.Log(String.Format(PACKAGES_MISSING, result.message));
                    }
                    complete(result);
                },
                maxProgressLines: 50);
            window.Show();
        }

        /// <summary>
        /// Retrieve package licenses, display license dialog and then install packages.
        /// </summary>
        /// <param name="toolPath">Tool to run.</param>
        /// <param name="toolArguments">Arguments to pass to the tool.</param>
        /// <param name="packages">List of package versions to install / upgrade.</param>
        /// <param name="licenseQuestion">License question to respond to.</param>
        /// <param name="licenseAgree">String used to agree to a license.</param>
        /// <param name="licenseDecline">String used to decline a license.</param>
        /// <param name="licenseTextHeader">Regex which matches the line which is the start of a
        /// license agreement.</param>
        /// <param name="complete">Called when installation is complete.</param>
        public static void InstallPackages(
                string toolPath, string toolArguments,
                HashSet<AndroidSdkPackageNameVersion> packages,
                string licenseQuestion, string licenseAgree, string licenseDecline,
                Regex licenseTextHeader, Action<bool> complete) {
            PlayServicesResolver.analytics.Report("/androidsdkmanager/installpackages",
                                                  "Android SDK Manager: Install Packages");
            PlayServicesResolver.Log(String.Format("Install Android SDK packages\n" +
                                                   "\n" +
                                                   "{0} {1}\n",
                                                   toolPath, toolArguments),
                                     level: LogLevel.Verbose);
            // Display the license retrieval dialog.
            DisplayInstallLicenseDialog(
                toolPath, toolArguments, true,
                new LicenseResponder(licenseQuestion, licenseDecline), packages,
                (CommandLine.Result licensesResult) => {
                    if (licensesResult.exitCode != 0) {
                        complete(false);
                        return;
                    }
                    // Get the license text.
                    var licensesLines = new List<string>();
                    bool foundLicenses = false;
                    foreach (var line in CommandLine.SplitLines(licensesResult.stdout)) {
                        foundLicenses = foundLicenses || licenseTextHeader.Match(line).Success;
                        if (foundLicenses) licensesLines.Add(line);
                    }
                    if (licensesLines.Count == 0) {
                        LogInstallLicenseResult(toolPath, toolArguments, false, packages,
                                                licensesResult);
                        complete(true);
                        return;
                    }
                    // Display the license agreement dialog.
                    DisplayLicensesDialog(
                        String.Join("\n", licensesLines.ToArray()),
                        (bool agreed) => {
                            if (!agreed) {
                                complete(false);
                                return;
                            }
                            // Finally install the packages agreeing to the license questions.
                            DisplayInstallLicenseDialog(
                                toolPath, toolArguments, false,
                                new LicenseResponder(licenseQuestion, licenseAgree), packages,
                                (CommandLine.Result installResult) => {
                                    complete(installResult.exitCode == 0);
                                });
                        });
                });
        }

        /// <summary>
        /// Log a message that describes the installation / license fetching operation.
        /// </summary>
        /// <param name="toolPath">Tool that was executed.</param>
        /// <param name="toolArguments">Arguments to passed to the tool.</param>
        /// <param name="retrievingLicenses">Whether the command is retrieving licenses.</param>
        /// <param name="packages">List of package versions to install / upgrade.</param>
        /// <param name="toolResult">Result of the tool's execution.</param>
        private static void LogInstallLicenseResult(
                string toolPath, string toolArguments, bool retrievingLicenses,
                IEnumerable<AndroidSdkPackageNameVersion> packages,
                CommandLine.Result toolResult) {
            bool succeeded = toolResult.exitCode == 0;
            if (!retrievingLicenses || !succeeded) {
                var failedMessage = retrievingLicenses ?
                    "Failed to retrieve Android SDK package licenses.\n\n" +
                    "Aborted installation of the following packages:\n" :
                    "Android package installation failed.\n\n" +
                    "Failed when installing the following packages:\n";
                PlayServicesResolver.Log(
                    String.Format(
                        "{0}\n" +
                        "{1}\n\n" +
                        "{2}\n",
                        succeeded ? "Successfully installed Android packages.\n\n" : failedMessage,
                        AndroidSdkPackageNameVersion.ListToString(packages),
                        toolResult.message),
                    level: succeeded ? LogLevel.Info : LogLevel.Warning);
                var analyticsParameters = new KeyValuePair<string, string>[] {
                    new KeyValuePair<string, string>(
                        "numPackages",
                        new List<AndroidSdkPackageNameVersion>(packages).Count.ToString())
                };
                if (succeeded) {
                    PlayServicesResolver.analytics.Report(
                        "/androidsdkmanager/installpackages/success", analyticsParameters,
                        "Android SDK Manager: Install Packages Successful");
                } else {
                    PlayServicesResolver.analytics.Report(
                        "/androidsdkmanager/installpackages/failed", analyticsParameters,
                        "Android SDK Manager: Install Packages Failed");
                }
            }
        }

        /// <summary>
        /// Open a install / license window and execute a command.
        /// </summary>
        /// <param name="toolPath">Tool to run.</param>
        /// <param name="toolArguments">Arguments to pass to the tool.</param>
        /// <param name="retrievingLicenses">Whether the command is retrieving licenses.</param>
        /// <param name="licenseResponder">Responds to license questions.</param>
        /// <param name="packages">List of package versions to install / upgrade.</param>
        /// <param name="complete">Called when installation is complete.</param>
        private static void DisplayInstallLicenseDialog(
                string toolPath, string toolArguments, bool retrievingLicenses,
                LicenseResponder licenseResponder,
                IEnumerable<AndroidSdkPackageNameVersion> packages,
                Action<CommandLine.Result> complete) {
            var summary = retrievingLicenses ?
                "Attempting Android SDK package installation..." : DIALOG_TITLE + "...";
            var window = CommandLineDialog.CreateCommandLineDialog(DIALOG_TITLE);
            window.summaryText = summary;
            window.modal = false;
            window.bodyText = String.Format("{0} {1}\n\n", toolPath, toolArguments);
            window.progressTitle = window.summaryText;
            window.autoScrollToBottom = true;
            window.logger = PlayServicesResolver.logger;
            CommandLine.IOHandler ioHandler = null;
            if (licenseResponder != null) ioHandler = licenseResponder.AggregateLine;
            PlayServicesResolver.Log(String.Format("{0} {1}", toolPath, toolArguments),
                                     level: LogLevel.Verbose);
            window.RunAsync(
                toolPath, toolArguments,
                (CommandLine.Result result) => {
                    window.Close();
                    LogInstallLicenseResult(toolPath, toolArguments, retrievingLicenses, packages,
                                            result);
                    complete(result);
                },
                ioHandler: ioHandler,
                maxProgressLines: retrievingLicenses ? 250 : 500);
            window.Show();
        }

        /// <summary>
        /// Display license dialog.
        /// </summary>
        /// <param name="licenses">String containing the licenses to display.</param>
        /// <param name="complete">Called when the user agrees / disagrees to the licenses.</param>
        private static void DisplayLicensesDialog(string licenses, Action<bool> complete) {
            var window = CommandLineDialog.CreateCommandLineDialog(DIALOG_TITLE);
            window.summaryText = "License agreement(s) required to install Android SDK packages";
            window.modal = false;
            window.bodyText = licenses;
            window.yesText = "agree";
            window.noText = "decline";
            window.result = false;
            window.logger = PlayServicesResolver.logger;
            window.Repaint();
            window.buttonClicked = (TextAreaDialog dialog) => {
                window.Close();
                if (!dialog.result) {
                    complete(false);
                    return;
                }
                complete(true);
            };
            window.Show();
        }
    }

    /// <summary>
    /// Interacts with the legacy Android SDK manager "android".
    /// </summary>
    internal class AndroidToolSdkManager : IAndroidSdkManager {
        /// Name of the SDK manager command line tool.
        public const string TOOL_NAME = "android";

        /// <summary>
        /// Extracts the package identifer from the SDK list output.
        /// </summary>
        private static Regex PACKAGE_ID_REGEX = new Regex(
            "^id:\\W+\\d+\\W+or\\W+\"([^\"]+)\"");

        /// <summary>
        /// Extracts the package description from the SDK list output.
        /// </summary>
        private static Regex PACKAGE_DESCRIPTION_REGEX = new Regex(
            "^\\WDesc:\\W+(.+)");

        /// <summary>
        /// Extracts the install location from the SDK list output.
        /// </summary>
        private static Regex PACKAGE_INSTALL_LOCATION_REGEX = new Regex(
            "^\\W+Install[^:]+:\\W+([^ ]+)");

        // Path to the SDK manager tool.
        private string toolPath;
        // Path to the Android SDK.
        private string sdkPath;

        /// <summary>
        /// Initialize this instance.
        /// </summary>
        /// <param name="toolPath">Path of the android tool.</param>
        /// <param name="sdkPath">Required to validate that a package is really installed.</param>
        public AndroidToolSdkManager(string toolPath, string sdkPath) {
            this.toolPath = toolPath;
            this.sdkPath = sdkPath;
        }

        /// <summary>
        /// Determines whether this is the legacy tool or the sdkmanager wrapper.
        /// </summary>
        public bool IsWrapper {
            get {
                // It's only possible to differentiate between the "android" package manager or
                // sdkmanager wrapper by searching the output string for "deprecated" which is
                // present in the wrapper.
                var result = CommandLine.Run(
                    toolPath, "list sdk",
                    envVars: new Dictionary<string, string> { { "USE_SDK_WRAPPER", "1" } });
                if (result.stdout.IndexOf("deprecated") >= 0) {
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Parse "android list sdk -u -e -a" output.
        /// </summary>
        private AndroidSdkPackageCollection ParseAndroidListSdkOutput(
                string androidListSdkOutput) {
            var packages = new AndroidSdkPackageCollection();
            AndroidSdkPackage currentPackage = null;
            foreach (string line in CommandLine.SplitLines(androidListSdkOutput)) {
                // Check for the start of a new package entry.
                if (line.StartsWith("---")) {
                    currentPackage = null;
                    continue;
                }
                Match match;
                // If this is the start of a package description, add a package.
                match = PACKAGE_ID_REGEX.Match(line);
                if (match.Success) {
                    // TODO(smiles): Convert the legacy package name to a new package name.
                    currentPackage = new AndroidSdkPackage { LegacyName = match.Groups[1].Value };
                    packages[currentPackage.Name].Add(currentPackage);
                    continue;
                }
                if (currentPackage == null) continue;

                // Add a package description.
                match = PACKAGE_DESCRIPTION_REGEX.Match(line);
                if (match.Success) {
                    currentPackage.Description = match.Groups[1].Value;
                    continue;
                }
                // Parse the install path and record whether the package is installed.
                match = PACKAGE_INSTALL_LOCATION_REGEX.Match(line);
                if (match.Success) {
                    currentPackage.Installed = File.Exists(
                        Path.Combine(Path.Combine(sdkPath, match.Groups[1].Value),
                                     "source.properties"));
                }
            }
            return packages;
        }

        /// <summary>
        /// Use the package manager to retrieve the set of installed and available packages.
        /// </summary>
        /// <param name="complete">Called when the query is complete.</param>
        public void QueryPackages(Action<AndroidSdkPackageCollection> complete) {
            SdkManagerUtil.QueryPackages(
                toolPath, "list sdk -u -e -a",
                (CommandLine.Result result) => {
                    complete(result.exitCode == 0 ?
                             ParseAndroidListSdkOutput(result.stdout) : null);
                });
        }

        /// <summary>
        /// Install a set of packages.
        /// </summary>
        /// <param name="packages">List of package versions to install / upgrade.</param>
        /// <param name="complete">Called when installation is complete.</param>
        public void InstallPackages(HashSet<AndroidSdkPackageNameVersion> packages,
                                    Action<bool> complete) {
            var packageNames = new List<string>();
            foreach (var pkg in packages) packageNames.Add(pkg.LegacyName);
            SdkManagerUtil.InstallPackages(
                toolPath, String.Format(
                    "update sdk -a -u -t {0}", String.Join(",", packageNames.ToArray())),
                packages, "Do you accept the license", "yes", "no",
                new Regex("^--------"), complete);
        }
    }

    /// <summary>
    /// Interacts with the Android SDK manager "sdkmanager".
    /// </summary>
    internal class SdkManager : IAndroidSdkManager {
        /// Name of the SDK manager command line tool.
        public const string TOOL_NAME = "sdkmanager";

        // Marker followed by the list of installed packages.
        private const string INSTALLED_PACKAGES_HEADER = "installed packages:";
        // Marker followed by the list of available packages.
        private const string AVAILABLE_PACKAGES_HEADER = "available packages:";
        // Marker followed by the list of available package updates.
        private const string AVAILABLE_UPDATES_HEADER = "available updates:";

        /// Minimum version of the package that supports the --verbose flag.
        public static long MINIMUM_VERSION_FOR_VERBOSE_OUTPUT =
            AndroidSdkPackageNameVersion.ConvertVersionStringToInteger("26.0.2");

        // Path to the SDK manager tool.
        private string toolPath;
        // Metadata for this package.
        private AndroidSdkPackage toolsPackage;

        /// <summary>
        /// Initialize this instance.
        /// </summary>
        /// <param name="toolPath">Path of the android tool.</param>
        public SdkManager(string toolPath) {
            this.toolPath = toolPath;
            var toolsDir = Path.GetDirectoryName(Path.GetDirectoryName(toolPath));
            var sdkDir = Path.GetDirectoryName(toolsDir);
            toolsPackage = AndroidSdkPackage.ReadFromSourceProperties(
                sdkDir, toolsDir.Substring((sdkDir + Path.PathSeparator).Length));
        }

        /// <summary>
        /// Read the metadata for the package that contains the package manager.
        /// </summary>
        public AndroidSdkPackage Package { get { return toolsPackage; } }

        /// <summary>
        /// Parse "sdkmanager --list --verbose" output.
        /// NOTE: The --verbose output format is only reported by sdkmanager 26.0.2 and above.
        /// </summary>
        private AndroidSdkPackageCollection ParseListVerboseOutput(
                string sdkManagerListVerboseOutput) {
            var packages = new AndroidSdkPackageCollection();
            // Whether we're parsing a set of packages.
            bool parsingPackages = false;
            // Whether we're parsing within the set of installed packages vs. available packages.
            bool parsingInstalledPackages = false;
            // Fields of the package being parsed.
            AndroidSdkPackage currentPackage = null;
            foreach (var rawLine in CommandLine.SplitLines(sdkManagerListVerboseOutput)) {
                var line = rawLine.Trim();
                var lowerCaseLine = line.ToLower();
                if (lowerCaseLine == AVAILABLE_UPDATES_HEADER) {
                    parsingPackages = false;
                    continue;
                }
                bool installedPackagesLine = lowerCaseLine == INSTALLED_PACKAGES_HEADER;
                bool availablePackagesLine = lowerCaseLine == AVAILABLE_PACKAGES_HEADER;
                if (installedPackagesLine || availablePackagesLine) {
                    parsingPackages = true;
                    parsingInstalledPackages = installedPackagesLine;
                    continue;
                } else if (line.StartsWith("---")) {
                    // Ignore section separators.
                    continue;
                } else if (String.IsNullOrEmpty(line)) {
                    if (currentPackage != null &&
                        !(String.IsNullOrEmpty(currentPackage.Name) ||
                          String.IsNullOrEmpty(currentPackage.VersionString))) {
                        packages[currentPackage.Name].Add(currentPackage);
                    }
                    currentPackage = null;
                    continue;
                } else if (!parsingPackages) {
                    continue;
                }
                // Fields of the package are indented.
                bool indentedLine = rawLine.StartsWith("    ");
                if (!indentedLine) {
                    // If this isn't an indented line it should be a package name.
                    if (currentPackage == null) {
                        currentPackage = new AndroidSdkPackage {
                            Name = line,
                            Installed = parsingInstalledPackages
                        };
                    }
                } else if (currentPackage != null) {
                    // Parse the package field.
                    var fieldSeparatorIndex = line.IndexOf(":");
                    if (fieldSeparatorIndex >= 0) {
                        var fieldName = line.Substring(0, fieldSeparatorIndex).Trim().ToLower();
                        var fieldValue = line.Substring(fieldSeparatorIndex + 1).Trim();
                        if (fieldName == "description") {
                            currentPackage.Description = fieldValue;
                        } else if (fieldName == "version") {
                            currentPackage.VersionString = fieldValue;
                        }
                    }
                }
            }
            return packages;
        }

        /// <summary>
        /// Parse "sdkmanager --list" output.
        /// </summary>
        /// <returns>Dictionary of packages bucketed by package name</returns>
        private AndroidSdkPackageCollection ParseListOutput(
                string sdkManagerListOutput) {
            var packages = new AndroidSdkPackageCollection();
            // Whether we're parsing a set of packages.
            bool parsingPackages = false;
            // Whether we're parsing within the set of installed packages vs. available packages.
            bool parsingInstalledPackages = false;
            // Whether we're parsing the contents of the package table vs. the header.
            bool inPackageTable = false;
            foreach (var rawLine in CommandLine.SplitLines(sdkManagerListOutput)) {
                var line = rawLine.Trim();
                var lowerCaseLine = line.ToLower();
                if (lowerCaseLine == AVAILABLE_UPDATES_HEADER) {
                    parsingPackages = false;
                    continue;
                }
                bool installedPackagesLine = lowerCaseLine == INSTALLED_PACKAGES_HEADER;
                bool availablePackagesLine = lowerCaseLine == AVAILABLE_PACKAGES_HEADER;
                if (installedPackagesLine || availablePackagesLine) {
                    parsingPackages = true;
                    parsingInstalledPackages = installedPackagesLine;
                    inPackageTable = false;
                    continue;
                }
                if (!parsingPackages) continue;
                if (!inPackageTable) {
                    // If we've reached end of the table header, start parsing the set of packages.
                    if (line.StartsWith("----")) {
                        inPackageTable = true;
                    }
                    continue;
                }
                // Split into the fields package_name|version|description|location.
                // Where "location" is an optional field that contains the install path.
                var rawTokens = line.Split(new [] { '|' });
                if (rawTokens.Length < 3 || String.IsNullOrEmpty(line)) {
                    parsingPackages = false;
                    continue;
                }
                // Each field is surrounded by whitespace so trim the fields.
                string[] tokens = new string[rawTokens.Length];
                for (int i = 0; i < rawTokens.Length; ++i) {
                    tokens[i] = rawTokens[i].Trim();
                }
                var packageName = tokens[0];
                packages[packageName].Add(new AndroidSdkPackage {
                        Name = packageName,
                        Description = tokens[2],
                        VersionString = tokens[1],
                        Installed = parsingInstalledPackages
                    });
            }
            return packages;
        }

        /// <summary>
        /// Use the package manager to retrieve the set of installed and available packages.
        /// </summary>
        /// <param name="complete">Called when the query is complete.</param>
        public void QueryPackages(Action<AndroidSdkPackageCollection> complete) {
            bool useVerbose = Package != null &&
                Package.Version >= MINIMUM_VERSION_FOR_VERBOSE_OUTPUT;
            SdkManagerUtil.QueryPackages(
                toolPath, "--list" + (useVerbose ? " --verbose" : ""),
                (CommandLine.Result result) => {
                    complete(result.exitCode == 0 ?
                                useVerbose ? ParseListVerboseOutput(result.stdout) :
                                   ParseListOutput(result.stdout) :
                             null);
                });
        }

        /// <summary>
        /// Install a set of packages.
        /// </summary>
        /// <param name="packages">List of package versions to install / upgrade.</param>
        /// <param name="complete">Called when installation is complete.</param>
        public void InstallPackages(HashSet<AndroidSdkPackageNameVersion> packages,
                                    Action<bool> complete) {
            var packagesString = AndroidSdkPackageNameVersion.ListToString(packages);
            // TODO: Remove this dialog when the package manager provides feedback while
            // downloading.
            DialogWindow.Display(
                "Missing Android SDK packages",
                String.Format(
                    "Android SDK packages need to be installed:\n" +
                    "{0}\n" +
                    "\n" +
                    "The install process can be *slow* and does not provide any feedback " +
                    "which may lead you to think Unity has hung / crashed.  Would you like " +
                    "to wait for these package to be installed?",
                    packagesString),
                DialogWindow.Option.Selected0, "Yes", "No",
                complete: (selectedOption) => {
                    if (selectedOption == DialogWindow.Option.Selected0) {
                        PlayServicesResolver.Log(
                            "User cancelled installation of Android SDK tools package.",
                            level: LogLevel.Warning);
                        complete(false);
                        return;
                    }
                    var packageNames = new List<string>();
                    foreach (var pkg in packages) packageNames.Add(pkg.Name);
                    SdkManagerUtil.InstallPackages(toolPath,
                                                   String.Join(" ", packageNames.ToArray()),
                                                   packages, "Accept? (y/N):", "y", "N",
                                                   new Regex("^License\\W+[^ ]+:"), complete);
                });
        }
    }

    /// <summary>
    /// Interacts with the available Android SDK package manager.
    /// </summary>
    internal class AndroidSdkManager {
        /// <summary>
        /// Find a tool in the Android SDK.
        /// </summary>
        /// <param name="toolName">Name of the tool to search for.</param>
        /// <param name="sdkPath">SDK path to search for the tool.  If this is null or empty, the
        /// system path is searched instead.</param>
        /// <returns>String with the path to the tool if found, null otherwise.</returns>
        private static string FindAndroidSdkTool(string toolName, string sdkPath = null) {
            if (String.IsNullOrEmpty(sdkPath)) {
                PlayServicesResolver.Log(String.Format(
                    "{0}\n" +
                    "Falling back to searching for the Android SDK tool {1} in the system path.",
                    PlayServicesSupport.AndroidSdkConfigurationError, toolName));
            } else {
                var extensions = new List<string> { CommandLine.GetExecutableExtension() };
                if (UnityEngine.RuntimePlatform.WindowsEditor ==
                    UnityEngine.Application.platform) {
                    extensions.AddRange(new [] { ".bat", ".cmd" });
                }
                foreach (var dir in new [] { "tools", Path.Combine("tools", "bin") }) {
                    foreach (var extension in extensions) {
                        var currentPath = Path.Combine(sdkPath,
                                                       Path.Combine(dir, toolName + extension));
                        if (File.Exists(currentPath)) {
                            return currentPath;
                        }
                    }
                }
            }
            var toolPath = CommandLine.FindExecutable(toolName);
            return toolPath != null && File.Exists(toolPath) ? toolPath : null;
        }

        /// <summary>
        /// Log an error and complete a Create() operation.
        /// </summary>
        /// <param name="complete">Action called with null.</param>
        private static void CreateFailed(Action<IAndroidSdkManager> complete) {
            PlayServicesResolver.Log(String.Format(
                "Unable to find either the {0} or {1} command line tool.\n\n" +
                "It is not possible to query or install Android SDK packages.\n" +
                "To resolve this issue, open the Android Package Manager" +
                "and install the latest tools package.",
                SdkManager.TOOL_NAME, AndroidToolSdkManager.TOOL_NAME));
            complete(null);
        }

        /// <summary>
        /// Create an instance of this class.
        ///
        /// If the package manager is out of date, the user is prompted to update it.
        /// </summary>
        /// <param name="complete">Used to report a AndroidSdkManager instance if a SDK manager is
        /// available, returns null otherwise.</param>
        public static void Create(string sdkPath, Action<IAndroidSdkManager> complete) {
            // Search for the new package manager
            var sdkManagerTool = FindAndroidSdkTool(SdkManager.TOOL_NAME, sdkPath: sdkPath);
            if (sdkManagerTool != null) {
                var sdkManager = new SdkManager(sdkManagerTool);
                var sdkManagerPackage = sdkManager.Package;
                if (sdkManagerPackage != null) {
                    // If the package manager is out of date, try updating it.
                    if (sdkManagerPackage.Version < SdkManager.MINIMUM_VERSION_FOR_VERBOSE_OUTPUT) {
                        sdkManager.QueryPackages(
                            (AndroidSdkPackageCollection packages) => {
                                sdkManagerPackage = packages.GetMostRecentAvailablePackage(
                                    sdkManagerPackage.Name);
                                if (sdkManagerPackage != null) {
                                    sdkManager.InstallPackages(
                                        new HashSet<AndroidSdkPackageNameVersion>(
                                            new [] { sdkManagerPackage }),
                                        (bool success) => {
                                            complete(success ? sdkManager : null);
                                        });
                                } else {
                                    CreateFailed(complete);
                                }
                            });
                    } else {
                        complete(sdkManager);
                    }
                    return;
                }
            }

            // Search for the legacy package manager.
            var androidTool = FindAndroidSdkTool("android", sdkPath: sdkPath);
            if (androidTool != null) {
                var sdkManager = new AndroidToolSdkManager(androidTool, sdkPath);
                if (!sdkManager.IsWrapper) {
                    complete(sdkManager);
                    return;
                }
            }
            CreateFailed(complete);
        }
    }
}

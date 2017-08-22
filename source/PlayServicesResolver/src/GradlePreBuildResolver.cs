// <copyright file="GradlePreBuildResolver.cs" company="Google Inc.">
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
using Google.JarResolver;
using UnityEditor;
using UnityEngine;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GooglePlayServices {
/// <summary>
/// </summary>
class GradlePreBuildResolver : DefaultResolver {
    private static string OLD_GRADLE_SCRIPT_LOCATION = Path.Combine("Assets",
                                                                    "PlayServicesResolver");
    private static string GRADLE_SCRIPT_LOCATION = Path.Combine(
        "Temp", "PlayServicesResolverGradlePrebuild");
    private const string GENERATE_GRADLE_EXE_GENERIC = "generate_gradle_prebuild.py";
    private const string GENERATE_GRADLE_EXE_WINDOWS = "generate_gradle_prebuild.exe";
    private const string GRADLE_TEMPLATE_TEMPLATE_ZIP = "gradle-template.zip";
    private const string VOLATILE_PATHS_JSON = "volatile_paths.json";
    private static string GENERATE_GRADLE_BUILD_PATH = Path.Combine("Temp", "GenGradle");
    private static string GENERATE_CONFIG_PATH = Path.Combine("Temp", "config.json");
    private static string PROGUARD_UNITY_CONFIG = "proguard-unity.txt";
    private static string PROGUARD_MSG_FIX_CONFIG = "proguard-messaging-workaround.txt";
    private const string GENERATE_GRADLE_OUTPUT_DIR = "MergedDependencies";
    private const int TESTED_BUILD_TOOLS_VERSION_MAJOR = 26;
    private const int TESTED_BUILD_TOOLS_VERSION_MINOR = 0;
    private const int TESTED_BUILD_TOOLS_VERSION_REV = 0;

    /// <summary>
    /// Unpack the prebuild scripts from embedded resources.
    /// </summary>
    public static void ExtractPrebuildScripts() {
        var resourceNameAndTargetPaths = new List<KeyValuePair<string, string>>();
        var extractRequired = !Directory.Exists(GRADLE_SCRIPT_LOCATION);
        bool removedOldScripts = false;
        foreach (var script in new [] { GENERATE_GRADLE_EXE_GENERIC, GENERATE_GRADLE_EXE_WINDOWS,
                                        GRADLE_TEMPLATE_TEMPLATE_ZIP, PROGUARD_UNITY_CONFIG,
                                        PROGUARD_MSG_FIX_CONFIG, VOLATILE_PATHS_JSON }) {
            var resourceNameAndTargetPath = new KeyValuePair<string, string>(
                EMBEDDED_RESOURCES_NAMESPACE + script,
                Path.Combine(GRADLE_SCRIPT_LOCATION, script));
            resourceNameAndTargetPaths.Add(resourceNameAndTargetPath);
            extractRequired |= !File.Exists(resourceNameAndTargetPath.Value);
            // Clean up old prebuild scripts if they exist.
            var oldGradleScript = Path.Combine(OLD_GRADLE_SCRIPT_LOCATION, script);
            if (File.Exists(oldGradleScript)) {
                File.Delete(oldGradleScript);
                removedOldScripts = true;
            }
        }
        if (extractRequired) ExtractResources(resourceNameAndTargetPaths);
        if (removedOldScripts) AssetDatabase.Refresh();
    }

    // This is a basic JSON dictionary formater. It takes in a dictionary and returns a string
    // suitable for inside a JSON {} dictionary.
    // Given a C# dictionary of strings, it quotes and formats the key and value pairs for JSON:
    //  "key": "value"
    // the separator allows you to write it like:
    //  ", "   =>   "key1": "value1", "key2": "value2"
    //  ",\n": =>   "key1": "value1",
    //              "key2": "value2"
    // The indent level is useful in the latter example, to indent each item by a given
    // number of spaces.
    private string ToJSONDictionary(IDictionary<string, string> dict, string separator = ",\n",
                                    int indentLevel = 4) {
        var jsonOut = new List<string>();
        var indent = new String(' ', indentLevel);
        foreach (var pair in dict) {
            jsonOut.Add(String.Format("{0}\"{1}\": \"{2}\"", indent, pair.Key, pair.Value));
        }
        return String.Join(separator, jsonOut.ToArray());
    }

    // Given an iterable "list" of strings, return a string of these strings quoted and
    // coma delimited, for use inside a json [] array.
    private string ToJSONList(IEnumerable<string> list, string separator = ", ",
                              int indentLevel = 0, bool jsonObj = false) {
        // if the string is a json object ie. "[]",
        // then we shouldn't use quotes around it.
        var quote = jsonObj ? "" : "\"";
        var jsonOut = new List<string>();
        var indent = new String(' ', indentLevel);
        foreach (var item in list) {
            jsonOut.Add(String.Format("{0}{2}{1}{2}", indent, item, quote));
        }
        return String.Join(separator, jsonOut.ToArray());
    }

    // Grabs the parts of the version that are useful and puts them into an array.
    // This also fixes dynamic versions that do not include a decimal to be compatible with
    // gradle's accepted version formatting.
    private string[] DepsVersionAsArray(Dependency dep) {
        int plus_pos = dep.Version.IndexOf('+');
        int dot_pos = dep.Version.IndexOf('.');
        // 1.+, and 1.0+ is accepted, but 1+ is not.
        // need to fix #+ to #.+
        string ver = dep.Version;
        if (plus_pos >= 0 && dot_pos < 0) {
            ver = ver.Insert(plus_pos, ".");
        }
        return new string[] {dep.Group, dep.Artifact, ver};
    }

    // Handles the platform specific differences of executing the generate gradle script, and
    // creating the dialog responsible for showing the progress of the execution.
    // Any errors are reported to the console as well.
    /// <param name="args">Arguments to be passed to the generate gradle script tool.</param>
    private static void RunGenGradleScript(string args, CommandLine.CompletionHandler completedHandler) {
        ExtractPrebuildScripts();
        // b/35663224 Combine execute-python-exe which handles the windows logic.
        bool onWindows =
            UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor;
        string command = "\"" + Path.Combine(GRADLE_SCRIPT_LOCATION,
            onWindows ? GENERATE_GRADLE_EXE_WINDOWS : GENERATE_GRADLE_EXE_GENERIC) + "\"";
        if (!onWindows) {
            args = command + args;
            command = CommandLine.FindExecutable("python");
        }

        CommandLineDialog window = CommandLineDialog.CreateCommandLineDialog(
            "Resolving Jars.");
        window.modal = false;
        window.summaryText = "Generating and running Gradle prebuild.";
        window.progressTitle = window.summaryText;
        window.autoScrollToBottom = true;
        window.RunAsync(command, args,
            (result) => {
                if (result.exitCode != 0) {
                    Debug.LogError("Error somewhere in the process of creating the gradle build, " +
                       "executing it, and copying the outputs.\n" +
                       "This will break dependency resolution and your build will not run.\n" +
                       "See the output below for possible gradle build errors. The most likely " +
                       "cases are: an invalid bundleID (which you can correct in the Android " +
                       "Player Settings), or a failure to determine the Android SDK platform " +
                       "and build tools verison (you can verify that you have a valid android " +
                       "SDK path in the Unity preferences.\n" +
                       "If you're not able to diagnose the error, please report a bug at: " +
                       "https://github.com/googlesamples/unity-jar-resolver/issues" +
                       "A possible work-around is to turn off the " +
                       "\"Gradle Prebuild\" from the Jar Resolver Settings.\n\n" +
                       "Error (" + result.exitCode + "):\n" + result.stdout + result.stderr);
                    window.bodyText += "\n\nResolution Failed.";
                } else {
                    window.bodyText += "\n\nResolution Complete.";
                }
                window.noText = "Close";
                // After adding the button we need to scroll down a little more.
                window.scrollPosition.y = Mathf.Infinity;
                window.Repaint();
                completedHandler(result);
                if (result.exitCode == 0) {
                    window.Close();
                }
            }, maxProgressLines: 50);
        window.Show();
    }

    /// <summary>
    /// Find the latest build-tools minor version matching a major version.
    /// </summary>
    /// <remarks>
    /// This is intended to find the latest version of the build-tools for a given platform.
    /// For example with platform android-25, it will search the AndroidSdkPackageCollection of
    /// available packages and returns the latest version string for build tools such as "25.0.3".
    /// </remarks>
    /// <param name="packages">available packages from the android sdk manager query.</param>
    /// <param name="majorVersion">Platform version to match for the major version.</param>
    /// <returns>Semantic version (http://semver.org/), ie. 26.0.0</returns>
    public static string GetLatestMinorBuildToolsVersion(AndroidSdkPackageCollection packages,
                                                         int majorVersion) {
        Regex buildToolsRegex = new Regex(@"^build-tools;(\d+)\.(\d+)\.(\d+)(?:-.*)?$",
                                          RegexOptions.Compiled | RegexOptions.Multiline);
        int latestVersion = 0;
        string latestVersionString = null;
        foreach (Match match in buildToolsRegex.Matches(
            String.Join("\n", packages.PackageNames.ToArray()))) {
            int thisVersion = Int32.Parse(match.Groups[1].Value) * 1000000 +
                              Int32.Parse(match.Groups[2].Value) * 1000 +
                              Int32.Parse(match.Groups[3].Value);
            if (Int32.Parse(match.Groups[1].Value) == majorVersion &&
                thisVersion > latestVersion) {
                latestVersion = thisVersion;
                latestVersionString = String.Format("{0}.{1}.{2}", match.Groups[1].Value,
                                                    match.Groups[2].Value,
                                                    match.Groups[3].Value);
            }
        }
        return latestVersionString;
    }

    /// <summary>
    /// Search the AndroidSdkPackageCollection for the latest platform version.
    /// </summary>
    /// <remarks>
    /// This finds the latest installed android platform, and returns the version.
    /// </remarks>
    /// <param name="packages">available packages from the android sdk manager query.</param>
    /// <returns>sdk version (ie. 24 for Android 7.0 Nougat, 25 for Android 7.1 Nougat)</returns>
    public static int GetLatestInstalledAndroidPlatformVersion(
            AndroidSdkPackageCollection packages) {
        Regex buildToolsRegex = new Regex(@"^platforms;android-(\d+)$",
                                          RegexOptions.Compiled | RegexOptions.Multiline);
        int latestVersion = 0;
        foreach (Match match in buildToolsRegex.Matches(
            String.Join("\n", packages.PackageNames.ToArray()))) {
            int thisVersion = Int32.Parse(match.Groups[1].Value);
            if (thisVersion > latestVersion) {
                if (packages.GetInstalledPackage(match.Groups[0].Value) != null) {
                    latestVersion = thisVersion;
                }
            }
        }
        return latestVersion;
    }

    // Private method to avoid too deeply nested code in "DoResolution".
    private void GradleResolve(AndroidSdkPackageCollection packages,
                               PlayServicesSupport svcSupport, string destinationDirectory,
                               System.Action resolutionComplete) {
        string errorOutro = "make sure you have the latest version of this plugin and if you " +
                "still get this error, report it in a a bug here:\n" +
                "https://github.com/googlesamples/unity-jar-resolver/issues\n";
        string errorIntro = null;

        int targetSdkVersion = UnityCompat.GetAndroidTargetSDKVersion();
        string buildToolsVersion = null;
        if (targetSdkVersion < 0) {
            // A value of -1 means the targetSDK Version enum returned "Auto"
            // instead of an actual version, so it's up to us to actually figure it out.
            targetSdkVersion = GetLatestInstalledAndroidPlatformVersion(packages);
            PlayServicesSupport.Log(
                String.Format("TargetSDK is set to Auto-detect, and the latest Platform has been " +
                    "detected as: android-{0}", targetSdkVersion),
                level: PlayServicesSupport.LogLevel.Info, verbose: true);

            errorIntro = String.Format("The Target SDK is set to automatically pick the highest " +
                "installed platform in the Android Player Settings, which appears to be " +
                "\"android-{0}\". This requires build-tools with at least the same version, " +
                "however ", targetSdkVersion);

        } else {
            errorIntro = String.Format("The target SDK version is set in the Android Player " +
                "Settings to \"android-{0}\" which requires build tools with " +
                "at least the same version, however ", targetSdkVersion);

        }

        // You can use a higher version of the build-tools than your compileSdkVersion, in order
        // to pick up new/better compiler while not changing what you build your app against. --Xav
        // https://stackoverflow.com/a/24523113
        // Implicitly Xav is also saying, you can't use a build tool version less than the
        // platform level you're building. This is backed up from testing.
        if (targetSdkVersion > TESTED_BUILD_TOOLS_VERSION_MAJOR) {
            buildToolsVersion = GetLatestMinorBuildToolsVersion(packages, targetSdkVersion);

            if (buildToolsVersion == null) {
                PlayServicesSupport.Log(errorIntro + String.Format("no build-tools are available " +
                    "at this level in the sdk manager. This plugin has been tested with " +
                    "platforms up to android-{0} using build-tools {0}.{1}.{2}. You can try " +
                    "selecting a lower targetSdkVersion in the Android Player Settings.  Please ",
                    TESTED_BUILD_TOOLS_VERSION_MAJOR, TESTED_BUILD_TOOLS_VERSION_MINOR,
                    TESTED_BUILD_TOOLS_VERSION_REV) + errorOutro,
                    level: PlayServicesSupport.LogLevel.Error);
                return;
            } else {
                PlayServicesSupport.Log(errorIntro + String.Format("this plugin has only been " +
                    "tested with build-tools up to version {0}.{1}.{2}. Corresponding " +
                    "build-tools version {3} will be used, however this is untested with this " +
                    "plugin and MAY NOT WORK! If you have trouble, please select a target SDK " +
                    "version at or below \"android-{0}\". If you need to get this working with " +
                    "the latest platform, please ",
                    TESTED_BUILD_TOOLS_VERSION_MAJOR,
                    TESTED_BUILD_TOOLS_VERSION_MINOR,
                    TESTED_BUILD_TOOLS_VERSION_REV,
                    buildToolsVersion) + errorOutro, level: PlayServicesSupport.LogLevel.Warning);
            }
        }

        if (buildToolsVersion == null) {
            // Use the tested build tools version, which we know will be able to handle
            // this targetSDK version.
            buildToolsVersion = String.Format("{0}.{1}.{2}", TESTED_BUILD_TOOLS_VERSION_MAJOR,
                                                             TESTED_BUILD_TOOLS_VERSION_MINOR,
                                                             TESTED_BUILD_TOOLS_VERSION_REV);
            // We don't have to bother with checking if it's installed because gradle actually
            // does that for us.
        }

        string minSdkVersion = UnityCompat.GetAndroidMinSDKVersion().ToString();

        var config = new Dictionary<string, string>() {
            {"app_id", UnityCompat.ApplicationId},
            {"sdk_version", targetSdkVersion.ToString()},
            {"min_sdk_version", minSdkVersion},
            {"build_tools_version", buildToolsVersion},
            {"android_sdk_dir", svcSupport.SDK}
        };

        // This creates an enumerable of strings with the json lines for each dep like this:
        // "[\"namespace\", \"package\", \"version\"]"
        var dependencies = svcSupport.LoadDependencies(true, true, false);
        var depLines = from d in dependencies
            select "[" + ToJSONList(DepsVersionAsArray(d.Value)) + "]";
        // Get a flattened list of dependencies, excluding any with the "$SDK" path variable,
        // since those will automatically be included in the gradle build.
        var repoLines = new HashSet<string>(
            dependencies.SelectMany(d => d.Value.Repositories)
                        .Where(s => !s.Contains(PlayServicesSupport.SdkVariable)));

        var proguard_config_paths = new List<string>() {
            Path.Combine(GRADLE_SCRIPT_LOCATION, PROGUARD_UNITY_CONFIG),
            Path.Combine(GRADLE_SCRIPT_LOCATION, PROGUARD_MSG_FIX_CONFIG)
        };

        // Build the full json config as a string.
        string json_config = @"{{
""config"": {{
{0}
}},
""project_deps"": [
{1}
],
""extra_m2repositories"": [
{2}
],
""extra_proguard_configs"": [
{3}
]
}}";

        json_config = String.Format(json_config, ToJSONDictionary(config),
                                    ToJSONList(depLines, ",\n", 4, true),
                                    ToJSONList(repoLines, ",\n", 4),
                                    ToJSONList(proguard_config_paths, ",\n", 4));

        // Escape any literal backslashes (such as those from paths on windows), since we want to
        // preserve them when reading the config as backslashes and not interpret them
        // as escape characters.
        json_config = json_config.Replace(@"\", @"\\");

        System.IO.File.WriteAllText(GENERATE_CONFIG_PATH, json_config);
        var outDir = Path.Combine(destinationDirectory, GENERATE_GRADLE_OUTPUT_DIR);

        RunGenGradleScript(
            " -c \"" + GENERATE_CONFIG_PATH + "\"" +
            " -b \"" + GENERATE_GRADLE_BUILD_PATH + "\"" +
            " -o \"" + outDir + "\"",
            (result) => {
                if (result.exitCode == 0) {
                    var currentAbi = PlayServicesResolver.AndroidTargetDeviceAbi;
                    var activeAbis = GetSelectedABIDirs(currentAbi);
                    var libsDir = Path.Combine(outDir, "libs");
                    if (Directory.Exists(libsDir)) {
                        foreach (var directory in Directory.GetDirectories(libsDir)) {
                            var abiDir = Path.GetFileName(directory).ToLower();
                            if (!activeAbis.Contains(abiDir)) {
                                PlayServicesSupport.DeleteExistingFileOrDirectory(
                                    directory, includeMetaFiles: true);
                            }
                        }
                    }
                    if (Directory.Exists(outDir)) {
                        PlayServicesResolver.LabelAssets( new [] { outDir }, true, true );
                    }
                    AssetDatabase.Refresh();
                    resolutionComplete();
                }
            });
    }

    /// <summary>
    /// Does the resolution of the play-services aars.
    /// </summary>
    /// <param name="svcSupport">Svc support.</param>
    /// <param name="destinationDirectory">Destination directory.</param>
    /// <param name="handleOverwriteConfirmation">Handle overwrite confirmation.</param>
    /// <param name="resolutionComplete">Delegate called when resolution is complete.</param>
    public override void DoResolution(PlayServicesSupport svcSupport, string destinationDirectory,
            PlayServicesSupport.OverwriteConfirmation handleOverwriteConfirmation,
            System.Action resolutionComplete) {

        var sdkPath = svcSupport.SDK;
        // Find / upgrade the Android SDK manager.
        AndroidSdkManager.Create(
            sdkPath,
            (IAndroidSdkManager sdkManager) => {
                if (sdkManager == null) {
                    PlayServicesSupport.Log(
                        String.Format("Unable to find the Android SDK manager tool."),
                        level: PlayServicesSupport.LogLevel.Error);
                    return;
                }

                // Get the set of available and installed packages.
                sdkManager.QueryPackages(
                    (AndroidSdkPackageCollection packages) => {
                        if (packages == null) {
                            PlayServicesSupport.Log(
                                String.Format("No packages returned from the Android SDK Manager."),
                                level: PlayServicesSupport.LogLevel.Error);
                            return;
                        }

                        GradleResolve(packages, svcSupport, destinationDirectory,
                                      resolutionComplete);
                    });
            }
        );
    }
}
}

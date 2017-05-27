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

namespace GooglePlayServices {
/// <summary>
/// </summary>
class GradlePreBuildResolver : DefaultResolver {
    private static string GRADLE_SCRIPT_LOCATION = Path.Combine("Assets","PlayServicesResolver");
    private const string GENERATE_GRADLE_EXE_GENERIC = "generate_gradle_prebuild.py";
    private const string GENERATE_GRADLE_EXE_WINDOWS = "generate_gradle_prebuild.exe";
    private static string GENERATE_GRADLE_BUILD_PATH = Path.Combine("Temp", "GenGradle");
    private static string GENERATE_CONFIG_PATH = Path.Combine("Temp", "config.json");
    private static string PROGUARD_UNITY_CONFIG = "proguard-unity.txt";
    private static string PROGUARD_MSG_FIX_CONFIG = "proguard-messaging-workaround.txt";
    private const string GENERATE_GRADLE_OUTPUT_DIR = "MergedDependencies";

    /// <summary>
    /// Checks based on the asset changes, if resolution should occur.
    /// </summary>
    /// <remarks>
    /// The resolution only happens if a script file (.cs, or .js) was imported
    /// or if an Android plugin was deleted.  This allows for changes to
    /// assets that do not affect the dependencies to happen without processing.
    /// This also avoids an infinite loop when a version of a dependency is
    /// deleted during resolution.
    /// </remarks>
    /// <returns><c>true</c>, if auto resolution should happen, <c>false</c> otherwise.</returns>
    /// <param name="importedAssets">Imported assets.</param>
    /// <param name="deletedAssets">Deleted assets.</param>
    /// <param name="movedAssets">Moved assets.</param>
    /// <param name="movedFromAssetPaths">Moved from asset paths.</param>
    [Obsolete]
    public override bool ShouldAutoResolve(string[] importedAssets, string[] deletedAssets,
                                           string[] movedAssets, string[] movedFromAssetPaths) {
        return false;
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
    private static void RunGenGradleScript(string args) {
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
        window.RunAsync(
            command, args,
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
                window.buttonClicked = (TextAreaDialog dialog) => {
                    if (!dialog.result) {
                        window.Close();
                    }
                };
            },
            maxProgressLines: 50);
        window.Show();
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
        string targetSdkVersion = UnityCompat.GetAndroidPlatform().ToString();
        string minSdkVersion = UnityCompat.GetAndroidMinSDKVersion().ToString();
        string buildToolsVersion = UnityCompat.GetAndroidBuildToolsVersion();

        var config = new Dictionary<string, string>() {
            {"app_id", UnityCompat.ApplicationId},
            {"sdk_version", targetSdkVersion},
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

        RunGenGradleScript(
            " -c \"" + GENERATE_CONFIG_PATH + "\"" +
            " -b \"" + GENERATE_GRADLE_BUILD_PATH + "\"" +
            " -o \"" + Path.Combine(destinationDirectory, GENERATE_GRADLE_OUTPUT_DIR) + "\"");
    }
}
}

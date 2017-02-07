// <copyright file="VersionHandler.cs" company="Google Inc.">
// Copyright (C) 2016 Google Inc. All Rights Reserved.
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

#if UNITY_IOS
using GooglePlayServices;
using Google.JarResolver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Google {

public static class IOSResolver {
    /// <summary>
    /// Reference to a Cocoapod.
    /// </summary>
    private class Pod {
        /// <summary>
        /// Name of the pod.
        /// </summary>
        public string name = null;

        /// <summary>
        /// Version specification string.
        /// If it ends with "+" the specified version up to the next major
        /// version is selected.
        /// If "LATEST", null or empty this pulls the latest revision.
        /// A version number "1.2.3" selects a specific version number.
        /// </summary>
        public string version = null;

        /// <summary>
        /// Whether this pod has been compiled with bitcode enabled.
        ///
        /// If any pods are present which have bitcode disabled, bitcode is
        /// disabled for an entire project.
        /// </summary>
        public bool bitcodeEnabled = true;

        /// <summary>
        /// Minimum target SDK revision required by this pod.
        /// In the form major.minor
        /// </summary>
        public string minTargetSdk = null;

        /// <summary>
        /// Format a "pod" line for a Podfile.
        /// </summary>
        public string PodFilePodLine {
            get {
                string versionExpression = "";
                if (!String.IsNullOrEmpty(version) &&
                    !version.Equals("LATEST")) {
                    if (version.EndsWith("+")) {
                        versionExpression = String.Format(
                            ", '~> {0}'",
                            version.Substring(0, version.Length - 1));
                    } else {
                        versionExpression = String.Format(", '{0}'", version);
                    }
                }
                return String.Format("pod '{0}'{1}", name, versionExpression);
            }
        }

        /// <summary>
        /// Create a pod reference.
        /// </summary>
        /// <param name="name">Name of the pod.</param>
        /// <param name="version">Version of the pod.</param>
        /// <param name="bitcodeEnabled">Whether this pod was compiled with
        /// bitcode.</param>
        /// <param name="minTargetSdk">Minimum target SDK revision required by
        /// this pod.</param>
        public Pod(string name, string version, bool bitcodeEnabled,
                   string minTargetSdk) {
            this.name = name;
            this.version = version;
            this.bitcodeEnabled = bitcodeEnabled;
            this.minTargetSdk = minTargetSdk;
        }

        /// <summary>
        /// Convert min target SDK to an integer in the form
        // (major * 10) + minor.
        /// </summary>
        /// <return>Numeric minimum SDK revision required by this pod.</return>
        public int MinTargetSdkToVersion() {
            string sdkString =
                String.IsNullOrEmpty(minTargetSdk) ? "0.0" : minTargetSdk;
            if (!sdkString.Contains(".")) {
                sdkString = sdkString + ".0";
            }
            return IOSResolver.TargetSdkStringToVersion(sdkString);
        }

        /// <summary>
        /// Given a list of pods bucket them into a dictionary sorted by
        /// min SDK version.  Pods which specify no minimum version (e.g 0)
        /// are ignored.
        /// </summary>
        /// <param name="pods">Enumerable of pods to query.</param>
        /// <returns>Sorted dictionary of lists of pod names bucketed by
        /// minimum required SDK version.</returns>
        public static SortedDictionary<int, List<string>>
                BucketByMinSdkVersion(IEnumerable<Pod> pods) {
            var buckets = new SortedDictionary<int, List<string>>();
            foreach (var pod in pods) {
                int minVersion = pod.MinTargetSdkToVersion();
                if (minVersion == 0) {
                    continue;
                }
                List<string> nameList = null;
                if (!buckets.TryGetValue(minVersion, out nameList)) {
                    nameList = new List<string>();
                }
                nameList.Add(pod.name);
                buckets[minVersion] = nameList;
            }
            return buckets;
        }
    }

    // Dictionary of pods to install in the generated Xcode project.
    private static SortedDictionary<string, Pod> pods =
        new SortedDictionary<string, Pod>();

    // Order of post processing operations.
    private const int BUILD_ORDER_PATCH_PROJECT = 1;
    private const int BUILD_ORDER_GEN_PODFILE = 2;
    private const int BUILD_ORDER_INSTALL_PODS = 3;
    private const int BUILD_ORDER_UPDATE_DEPS = 4;

    // Installation instructions for the Cocoapods command line tool.
    private const string COCOAPOD_INSTALL_INSTRUCTIONS = (
        "You can install cocoapods with the Ruby gem package manager:\n" +
        " > sudo gem install -n /usr/local/bin cocoapods\n" +
        " > pod setup");

    // Pod executable filename.
    private static string POD_EXECUTABLE = "pod";
    // Default paths to search for the "pod" command before falling back to
    // querying the Ruby Gem tool for the environment.
    private static string[] POD_SEARCH_PATHS = new string[] {
        "/usr/local/bin",
        "/usr/bin",
    };

    // Extensions of pod source files to include in the project.
    private static HashSet<string> SOURCE_FILE_EXTENSIONS = new HashSet<string>(
        new string[] {
            ".h",
            ".c",
            ".cc",
            ".cpp",
            ".mm",
            ".m",
        });

    /// <summary>
    /// Name of the Xcode project generated by Unity.
    /// </summary>
    public const string PROJECT_NAME = "Unity-iPhone";

    /// <summary>
    /// Main executable target of the Xcode project generated by Unity.
    /// </summary>
    public static string TARGET_NAME = null;

    // Keys in the editor preferences which control the behavior of this module.
    private const string PREFERENCE_ENABLED = "Google.IOSResolver.Enabled";

    /// <summary>
    /// Whether verbose logging is enabled.
    /// </summary>
    internal static bool verboseLogging = false;

    // Whether the xcode extension was successfully loaded.
    private static bool iOSXcodeExtensionLoaded = true;

    private static string IOS_PLAYBACK_ENGINES_PATH =
        Path.Combine("PlaybackEngines", "iOSSupport");

    // Directory containing downloaded Cocoapods relative to the project
    // directory.
    private const string PODS_DIR = "Pods";
    // Name of the project within PODS_DIR that references downloaded Cocoapods.
    private const string PODS_PROJECT_NAME = "Pods";

    // Version of the Cocoapods installation.
    private static string podsVersion = "";

    // Default iOS target SDK if the selected version is invalid.
    private const int DEFAULT_TARGET_SDK = 82;
    // Valid iOS target SDK version.
    private static Regex TARGET_SDK_REGEX = new Regex("^[0-9]+\\.[0-9]$");

    // Search for a file up to a maximum search depth stopping the
    // depth first search each time the specified file is found.
    private static List<string> FindFile(
            string searchPath, string fileToFind, int maxDepth,
            int currentDepth = 0) {
        if (Path.GetFileName(searchPath) == fileToFind) {
            return new List<string> { searchPath };
        } else if (maxDepth == currentDepth) {
            return new List<string>();
        }
        var foundFiles = new List<string>();
        foreach (var file in Directory.GetFiles(searchPath)) {
            if (Path.GetFileName(file) == fileToFind) {
                foundFiles.Add(file);
            }
        }
        foreach (var dir in Directory.GetDirectories(searchPath)) {
            foundFiles.AddRange(FindFile(dir, fileToFind, maxDepth,
                                         currentDepth: currentDepth + 1));
        }
        return foundFiles;
    }

    // Try to load the Xcode editor extension.
    private static Assembly ResolveUnityEditoriOSXcodeExtension(
            object sender, ResolveEventArgs args)
    {
        // The UnityEditor.iOS.Extensions.Xcode.dll has the wrong name baked
        // into the assembly so references end up resolving as
        // Unity.iOS.Extensions.Xcode.  Catch this and redirect the load to
        // the UnityEditor.iOS.Extensions.Xcode.
        string assemblyName = (new AssemblyName(args.Name)).Name;
        if (!(assemblyName.Equals("Unity.iOS.Extensions.Xcode") ||
              assemblyName.Equals("UnityEditor.iOS.Extensions.Xcode"))) {
            return null;
        }
        Log("Trying to load assembly: " + assemblyName, verbose: true);
        iOSXcodeExtensionLoaded = false;
        string fixedAssemblyName =
            assemblyName.Replace("Unity.", "UnityEditor.") + ".dll";
        Log("Redirecting to assembly name: " + fixedAssemblyName,
            verbose: true);

        // Get the managed DLLs folder.
        string folderPath = Path.GetDirectoryName(
            Assembly.GetAssembly(
                typeof(UnityEditor.AssetPostprocessor)).Location);
        // Try searching a common install location.
        folderPath = Path.Combine(
            (new DirectoryInfo(folderPath)).Parent.FullName,
            IOS_PLAYBACK_ENGINES_PATH);
        string assemblyPath = Path.Combine(folderPath, fixedAssemblyName);
        if (!File.Exists(assemblyPath)) {
            string searchPath = (new DirectoryInfo(folderPath)).FullName;
            if (UnityEngine.RuntimePlatform.OSXEditor ==
                UnityEngine.Application.platform) {
                // Unity likes to move their DLLs around between releases to
                // keep us on our toes, so search for the DLL under the
                // package path.
                searchPath = Path.GetDirectoryName(
                    searchPath.Substring(0, searchPath.LastIndexOf(".app")));
            } else {
                // Search under the Data directory.
                searchPath = Path.GetDirectoryName(
                    searchPath.Substring(
                        0, searchPath.LastIndexOf(
                            "Data" + Path.DirectorySeparatorChar.ToString())));
            }
            Log("Searching for assembly under " + searchPath, verbose: true);
            var files = FindFile(searchPath, fixedAssemblyName, 5);
            if (files.Count > 0) assemblyPath = files.ToArray()[0];
        }
        // Try to load the assembly.
        if (!File.Exists(assemblyPath)) {
            Log(assemblyPath + " does not exist", verbose: true);
            return null;
        }
        Log("Loading " + assemblyPath, verbose: true);
        Assembly assembly = Assembly.LoadFrom(assemblyPath);
        if (assembly != null) {
            Log("Load succeeded from " + assemblyPath, verbose: true);
            iOSXcodeExtensionLoaded = true;
        }
        return assembly;
    }

    /// <summary>
    /// Initialize the module.
    /// </summary>
    static IOSResolver() {
        // NOTE: We can't reference the UnityEditor.iOS.Xcode module in this
        // method as the Mono runtime in Unity 4 and below requires all
        // dependencies of a method are loaded before the method is executed
        // so we install the DLL loader first then try using the Xcode module.
        RemapXcodeExtension();
        // NOTE: It's not possible to catch exceptions a missing reference
        // to the UnityEditor.iOS.Xcode assembly in this method as the runtime
        // will attempt to load the assemebly before the method is executed so
        // we handle exceptions here.
        try {
            InitializeTargetName();
        } catch (Exception exception) {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS) {
                Log("Failed: " + exception.ToString(), level: LogLevel.Error);
                if (exception is FileNotFoundException ||
                    exception is TypeInitializationException ||
                    exception is TargetInvocationException) {
                    // It's likely we failed to load the iOS Xcode extension.
                    Debug.LogWarning(
                        "Failed to load the " +
                        "UnityEditor.iOS.Extensions.Xcode dll.  " +
                        "Is iOS support installed?");
                } else {
                    throw exception;
                }
            }
        }
    }

    /// <summary>
    /// Initialize the TARGET_NAME property.
    /// </summary>
    private static void InitializeTargetName() {
        TARGET_NAME = UnityEditor.iOS.Xcode.PBXProject.GetUnityTargetName();
    }

    // Fix loading of the Xcode extension dll.
    public static void RemapXcodeExtension() {
        AppDomain.CurrentDomain.AssemblyResolve -=
            ResolveUnityEditoriOSXcodeExtension;
        AppDomain.CurrentDomain.AssemblyResolve +=
            ResolveUnityEditoriOSXcodeExtension;
    }

    /// <summary>
    /// Enable / disable iOS dependency injection.
    /// </summary>
    public static bool Enabled {
        get { return EditorPrefs.GetBool(PREFERENCE_ENABLED,
                                         defaultValue: true) &&
                iOSXcodeExtensionLoaded; }
        set { EditorPrefs.SetBool(PREFERENCE_ENABLED, value); }
    }

    /// <summary>
    /// Log severity.
    /// </summary>
    internal enum LogLevel {
        Info,
        Warning,
        Error,
    };

    /// <summary>
    /// Log a message.
    /// </summary>
    internal static void Log(string message, bool verbose = false,
                             LogLevel level = LogLevel.Info) {
        if (!verbose || verboseLogging) {
            switch (level) {
                case LogLevel.Info:
                    Debug.Log(message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                    Debug.LogError(message);
                    break;
            }
        }
    }

    /// <summary>
    /// Determine whether a Pod is present in the list of dependencies.
    /// </summary>
    public static bool PodPresent(string pod) {
        return (new List<string>(pods.Keys)).Contains(pod);
    }

    /// <summary>
    /// Whether to inject iOS dependencies in the Unity generated Xcode
    /// project.
    /// </summary>
    private static bool InjectDependencies() {
        return EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS &&
            Enabled && pods.Count > 0;
    }

    /// <summary>
    /// Tells the app what pod dependencies are needed.
    /// This is called from a deps file in each API to aggregate all of the
    /// dependencies to automate the Podfile generation.
    /// </summary>
    /// <param name="podName">pod path, for example "Google-Mobile-Ads-SDK" to
    /// be included</param>
    /// <param name="version">Version specification.  See Pod.version.</param>
    /// <param name="bitcodeEnabled">Whether the pod was compiled with bitcode
    /// enabled.  If this is set to false on a pod, the entire project will
    /// be configured with bitcode disabled.</param>
    /// <param name="minTargetSdk">Minimum SDK revision required by this
    /// pod.</param>
    public static void AddPod(string podName, string version = null,
                              bool bitcodeEnabled = true,
                              string minTargetSdk = null) {
        Log("AddPod - name: " + podName +
            " version: " + (version ?? "null") +
            " bitcode: " + bitcodeEnabled.ToString() +
            " sdk: " + (minTargetSdk ?? "null"),
            verbose: true);
        var pod = new Pod(podName, version, bitcodeEnabled, minTargetSdk);
        pods[podName] = pod;
        UpdateTargetSdk(pod);
    }

    /// <summary>
    /// Update the iOS target SDK if it's lower than the minimum SDK
    /// version specified by the pod.
    /// </summary>
    /// <param name="pod">Pod to query for the minimum supported version.
    /// </param>
    /// <param name="notifyUser">Whether to write to the log to notify the
    /// user of a build setting change.</param>
    /// <returns>true if the SDK version was changed, false
    /// otherwise.</returns>
    private static bool UpdateTargetSdk(Pod pod,
                                        bool notifyUser = true) {
        int currentVersion = TargetSdkVersion;
        int minVersion = pod.MinTargetSdkToVersion();
        if (currentVersion >= minVersion) {
            return false;
        }
        if (notifyUser) {
            string oldSdk = TargetSdk;
            TargetSdkVersion = minVersion;
            Log("iOS Target SDK changed from " + oldSdk + " to " +
                TargetSdk + " required by the " + pod.name + " pod");
        }
        return true;
    }

    /// <summary>
    /// Update the target SDK if it's required.
    /// </summary>
    /// <returns>true if the SDK was updated, false otherwise.</returns>
    public static bool UpdateTargetSdk() {
        var minVersionAndPodNames = TargetSdkNeedsUpdate();
        if (minVersionAndPodNames.Value != null) {
            var minVersionString =
                TargetSdkVersionToString(minVersionAndPodNames.Key);
            var update = EditorUtility.DisplayDialog(
                "Unsupported Target SDK",
                "Target SDK selected in the iOS Player Settings (" +
                TargetSdk + ") is not supported by the Cocoapods " +
                "included in this project. " +
                "The build will very likely fail. The minimum supported " +
                "version is \"" + minVersionString + "\" " +
                "required by pods (" +
                String.Join(", ", minVersionAndPodNames.Value.ToArray()) +
                ").\n" +
                "Would you like to update the target SDK version?",
                "Yes", cancel: "No");
            if (update) {
                TargetSdkVersion = minVersionAndPodNames.Key;
                string errorString = (
                    "Target SDK has been updated from " + TargetSdk +
                    " to " + minVersionString + ".  You must restart the " +
                    "build for this change to take effect.");
                EditorUtility.DisplayDialog(
                    "Target SDK updated.", errorString, "OK");
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Determine whether the target SDK needs to be updated based upon pod
    /// dependencies.
    /// </summary>
    /// <returns>Key value pair of minimum SDK version (key) and
    /// a list of pod names that require it (value) if the currently
    /// selected target SDK version does not satify pod requirements, the list
    /// (value) is null otherwise.</returns>
    private static KeyValuePair<int, List<string>> TargetSdkNeedsUpdate() {
        var kvpair = new KeyValuePair<int, List<string>>(0, null);
        var podListsByVersion = Pod.BucketByMinSdkVersion(pods.Values);
        if (podListsByVersion.Count == 0) {
            return kvpair;
        }
        KeyValuePair<int, List<string>> minVersionAndPodName = kvpair;
        foreach (var versionAndPodList in podListsByVersion) {
            minVersionAndPodName = versionAndPodList;
            break;
        }
        int currentVersion = TargetSdkVersion;
        if (currentVersion >= minVersionAndPodName.Key) {
            return kvpair;
        }
        return minVersionAndPodName;
    }

    // Get the path of an xcode project relative to the specified directory.
    private static string GetProjectPath(string relativeTo,
                                         string projectName) {
        return Path.Combine(relativeTo,
                            Path.Combine(projectName + ".xcodeproj",
                                         "project.pbxproj"));
    }

    /// <summary>
    /// Get the generated xcode project path relative to the specified
    /// directory.
    /// </summary>
    /// <param name="relativeTo">Path the project is relative to.</param>
    public static string GetProjectPath(string relativeTo) {
        return GetProjectPath(relativeTo, PROJECT_NAME);
    }

    /// <summary>
    /// Get or set the Unity iOS target SDK version string (e.g "7.1")
    /// build setting.
    /// </summary>
    static string TargetSdk {
        get {
            string name = null;
            var iosSettingsType = typeof(UnityEditor.PlayerSettings.iOS);
            // Read the version (Unity 5.5 and above).
            var osVersionProperty = iosSettingsType.GetProperty(
                   "targetOSVersionString");
            if (osVersionProperty != null) {
                name = (string)osVersionProperty.GetValue(null, null);
            }
            if (name == null) {
                // Read the version (deprecated in Unity 5.5).
                osVersionProperty = iosSettingsType.GetProperty(
                   "targetOSVersion");
                if (osVersionProperty != null) {
                    var osVersionValue =
                        osVersionProperty.GetValue(null, null);
                    if (osVersionValue != null) {
                        name = Enum.GetName(osVersionValue.GetType(),
                                            osVersionValue);
                    }
                }
            }
            if (String.IsNullOrEmpty(name)) {
                // Versions 8.2 and above do not have enum symbols
                // The values in Unity 5.4.1f1:
                // 8.2 == 32
                // 8.3 == 34
                // 8.4 == 36
                // 9.0 == 38
                // 9.1 == 40
                // Since these are undocumented just report
                // 8.2 as selected for the moment.
                return TargetSdkVersionToString(DEFAULT_TARGET_SDK);
            }
            return name.Trim().Replace("iOS_", "").Replace("_", ".");
        }

        set {
            var iosSettingsType = typeof(UnityEditor.PlayerSettings.iOS);
            // Write the version (Unity 5.5 and above).
            var osVersionProperty =
                iosSettingsType.GetProperty("targetOSVersionString");
            if (osVersionProperty != null) {
                osVersionProperty.SetValue(null, value, null);
            } else {
                osVersionProperty =
                    iosSettingsType.GetProperty("targetOSVersion");
                osVersionProperty.SetValue(
                    null,
                    Enum.Parse(osVersionProperty.PropertyType,
                               "iOS_" + value.Replace(".", "_")),
                    null);
            }
        }
    }

    /// <summary>
    /// Get or set the Unity iOS target SDK using a version number (e.g 71
    /// is equivalent to "7.1").
    /// </summary>
    static int TargetSdkVersion {
        get { return TargetSdkStringToVersion(TargetSdk); }
        set { TargetSdk = TargetSdkVersionToString(value); }
    }

    /// <summary>
    /// Convert a target SDK string into a value of the form
    // (major * 10) + minor.
    /// </summary>
    /// <returns>Integer representation of the SDK.</returns>
    internal static int TargetSdkStringToVersion(string targetSdk) {
        if (TARGET_SDK_REGEX.IsMatch(targetSdk)) {
            try {
                return Convert.ToInt32(targetSdk.Replace(".", ""));
            } catch (FormatException) {
                // Conversion failed, drop through.
            }
        }
        Log(String.Format(
            "Invalid iOS target SDK version configured \"{0}\".\n" +
            "\n" +
            "Please change this to a valid SDK version (e.g {1}) in:\n" +
            "  Player Settings -> Other Settings --> " +
            "Target Minimum iOS Version\n",
            targetSdk, TargetSdkVersionToString(DEFAULT_TARGET_SDK)),
            level: LogLevel.Warning);
        return DEFAULT_TARGET_SDK;

    }

    /// <summary>
    /// Convert an integer target SDK value into a string.
    /// </summary>
    /// <returns>String version number.</returns>
    internal static string TargetSdkVersionToString(int version) {
        int major = version / 10;
        int minor = version % 10;
        return major.ToString() + "." + minor.ToString();
    }

    /// <summary>
    /// Determine whether any pods need bitcode disabled.
    /// </summary>
    /// <returns>List of pod names with bitcode disabled.</return>
    private static List<string> FindPodsWithBitcodeDisabled() {
        var disabled = new List<string>();
        foreach (var pod in pods.Values) {
            if (!pod.bitcodeEnabled) {
                disabled.Add(pod.name);
            }
        }
        return disabled;
    }

    /// <summary>
    /// Post-processing build step to patch the generated project files.
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_PATCH_PROJECT)]
    public static void OnPostProcessPatchProject(BuildTarget buildTarget,
                                                 string pathToBuiltProject) {
        if (!InjectDependencies()) return;
        PatchProject(buildTarget, pathToBuiltProject);
    }

    // Implementation of OnPostProcessPatchProject().
    // NOTE: This is separate from the post-processing method to prevent the
    // Mono runtime from loading the Xcode API before calling the post
    // processing step.
    internal static void PatchProject(
            BuildTarget buildTarget, string pathToBuiltProject) {
        var podsWithoutBitcode = FindPodsWithBitcodeDisabled();
        bool bitcodeDisabled = podsWithoutBitcode.Count > 0;
        if (bitcodeDisabled) {
            Log("Bitcode is disabled due to the following Cocoapods (" +
                String.Join(", ", podsWithoutBitcode.ToArray()) + ")",
                level: LogLevel.Warning);
        }
        // Configure project settings for Cocoapods.
        string pbxprojPath = GetProjectPath(pathToBuiltProject);
        var project = new UnityEditor.iOS.Xcode.PBXProject();
        project.ReadFromString(File.ReadAllText(pbxprojPath));
        string target = project.TargetGuidByName(TARGET_NAME);
        project.SetBuildProperty(target, "CLANG_ENABLE_MODULES", "YES");
        project.AddBuildProperty(target, "OTHER_LDFLAGS", "$(inherited)");
        project.AddBuildProperty(target, "OTHER_CFLAGS", "$(inherited)");
        project.AddBuildProperty(target, "HEADER_SEARCH_PATHS",
                                 "$(inherited)");
        project.AddBuildProperty(target, "FRAMEWORK_SEARCH_PATHS",
                                 "$(inherited)");
        project.AddBuildProperty(target, "FRAMEWORK_SEARCH_PATHS",
                                 "$(PROJECT_DIR)/Frameworks");
        project.AddBuildProperty(target, "OTHER_LDFLAGS", "-ObjC");
        // GTMSessionFetcher requires Obj-C exceptions.
        project.SetBuildProperty(target, "GCC_ENABLE_OBJC_EXCEPTIONS", "YES");
        if (bitcodeDisabled) {
            project.AddBuildProperty(target, "ENABLE_BITCODE", "NO");
        }
        File.WriteAllText(pbxprojPath, project.WriteToString());
    }

    /// <summary>
    /// Post-processing build step to generate the podfile for ios.
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_GEN_PODFILE)]
    public static void OnPostProcessGenPodfile(BuildTarget buildTarget,
                                               string pathToBuiltProject) {
        if (!InjectDependencies()) return;
        GenPodfile(buildTarget, pathToBuiltProject);
    }

    // Implementation of OnPostProcessGenPodfile().
    // NOTE: This is separate from the post-processing method to prevent the
    // Mono runtime from loading the Xcode API before calling the post
    // processing step.
    public static void GenPodfile(BuildTarget buildTarget,
                                  string pathToBuiltProject) {
        using (StreamWriter file =
               new StreamWriter(Path.Combine(pathToBuiltProject, "Podfile"))) {
            file.Write("source 'https://github.com/CocoaPods/Specs.git'\n" +
                "install! 'cocoapods', :integrate_targets => false\n" +
                string.Format("platform :ios, '{0}'\n\n", TargetSdk) +
                "target '" + TARGET_NAME + "' do\n"
            );
            foreach(var pod in pods.Values) {
                file.WriteLine(pod.PodFilePodLine);
            }
            file.WriteLine("end");
        }
    }

    /// <summary>
    /// Find the "pod" tool.
    /// </summary>
    /// <returns>Path to the pod tool if successful, null otherwise.</returns>
    private static string FindPodTool() {
        foreach (string path in POD_SEARCH_PATHS) {
            string podPath = Path.Combine(path, POD_EXECUTABLE);
            Log("Searching for cocoapods tool in " + podPath,
                verbose: true);
            if (File.Exists(podPath)) {
                Log("Found cocoapods tool in " + podPath, verbose: true);
                return podPath;
            }
        }
        Log("Querying gems for cocoapods install path", verbose: true);
        var result = CommandLine.Run("gem", "environment");
        if (result.exitCode == 0) {
            // gem environment outputs YAML for all config variables,
            // the following code only parses the executable dir from the
            // output.
            const string executableDir = "- EXECUTABLE DIRECTORY:";
            char[] variableSeparator = new char[] { ':' };
            foreach (var line in result.stdout.Split(
                         new char[] { '\r', '\n' })) {
                if (line.Trim().StartsWith(executableDir)) {
                    string path = line.Split(variableSeparator)[1].Trim();
                    string podPath = Path.Combine(path, POD_EXECUTABLE);
                    Log("Checking gems install path for cocoapods tool " +
                        podPath, verbose: true);
                    if (File.Exists(podPath)) {
                        Log("Found cocoapods tool in " + podPath,
                            verbose: true);
                        return podPath;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Finds and executes the pod command on the command line, using the
    /// correct environment.
    /// </summary>
    /// <param name="podArgs">Arguments passed to the pod command.</param>
    /// <param name="pathToBuiltProject">The path to the unity project, given
    /// from the unity [PostProcessBuildAttribute()] function.</param>
    /// <returns>The CommandLine.Result from running the command.</returns>
    private static CommandLine.Result RunPodCommand(string podArgs,
                                                    string pathToBuiltProject) {
        string pod_command = FindPodTool();
        if (String.IsNullOrEmpty(pod_command)) {
            CommandLine.Result r = new CommandLine.Result();
            r.exitCode = 1;
            r.stderr = "'pod' command not found; unable to generate a usable" +
                " Xcode project. " + COCOAPOD_INSTALL_INSTRUCTIONS;
            Log(r.stderr, level: LogLevel.Error);
            return r;
        }

        return CommandLine.Run(
            pod_command, podArgs, pathToBuiltProject,
            // cocoapods seems to require this, or it spits out a warning.
            envVars: new Dictionary<string,string>() {
                {"LANG", (System.Environment.GetEnvironmentVariable("LANG") ??
                    "en_US.UTF-8").Split('.')[0] + ".UTF-8"}
            });
    }

    /// <summary>
    /// Downloads all of the framework dependencies using pods.
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_INSTALL_PODS)]
    public static void OnPostProcessInstallPods(BuildTarget buildTarget,
                                                string pathToBuiltProject) {
        if (!InjectDependencies()) return;
        if (UpdateTargetSdk()) return;

        // Require at least version 1.0.0
        CommandLine.Result result;
        result = RunPodCommand("--version", pathToBuiltProject);
        if (result.exitCode == 0) podsVersion = result.stdout.Trim();

        if (result.exitCode != 0 || podsVersion[0] == '0') {
            Log("Error running cocoapods. Please ensure you have at least " +
                "version 1.0.0.  " + COCOAPOD_INSTALL_INSTRUCTIONS + "\n\n" +
                "'" + POD_EXECUTABLE + " --version' returned status: " +
                result.exitCode.ToString() + "\n" +
                "output: " + result.stdout + "\n\n" +
                result.stderr, level: LogLevel.Error);
            return;
        }

        result = RunPodCommand("install", pathToBuiltProject);

        // If pod installation failed it may be due to an out of date pod repo.
        // We'll attempt to resolve the error by updating the pod repo -
        // which is a slow operation - and retrying pod installation.
        if (result.exitCode != 0) {
            CommandLine.Result repoUpdateResult =
                RunPodCommand("repo update", pathToBuiltProject);
            bool repoUpdateSucceeded = repoUpdateResult.exitCode == 0;

            // Second attempt result.
            // This is isolated in case it fails, so we can just report the
            // original failure.
            CommandLine.Result result2;
            result2 = RunPodCommand("install", pathToBuiltProject);

            // If the repo update still didn't fix the problem...
            if (result2.exitCode != 0) {
                Log("iOS framework addition failed due to a " +
                    "Cocoapods installation failure. This will will likely " +
                    "result in an non-functional Xcode project.\n\n" +
                    "After the failure, \"pod repo update\" " +
                    "was executed and " +
                    (repoUpdateSucceeded ? "succeeded. " : "failed. ") +
                    "\"pod install\" was then attempted again, and still " +
                    "failed. This may be due to a broken Cocoapods " +
                    "installation. See: " +
                    "https://guides.cocoapods.org/using/troubleshooting.html " +
                    "for potential solutions.\n\n" +
                    "pod install output:\n\n" + result.stdout +
                    "\n\n" + result.stderr +
                    "\n\n\n" +
                    "pod repo update output:\n\n" + repoUpdateResult.stdout +
                    "\n\n" + repoUpdateResult.stderr, level: LogLevel.Error);
                return;
            }
        }
    }

    // Get a list of files relative to the specified directory matching the
    // specified set of extensions.
    internal static List<string> FindFilesWithExtensions(
            string directory, HashSet<string> extensions) {
        var outputList = new List<string>();
        foreach (string subdir in Directory.GetDirectories(directory)) {
            outputList.AddRange(FindFilesWithExtensions(subdir, extensions));
        }
        foreach (string filename in Directory.GetFiles(directory)) {
            string extension = Path.GetExtension(filename);
            if (extensions.Contains(extension)) outputList.Add(filename);
        }
        return outputList;
    }

    /// <summary>
    /// Finds the frameworks downloaded by cocoapods in the Pods directory
    /// and adds them to the project.
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_UPDATE_DEPS)]
    public static void OnPostProcessUpdateProjectDeps(
            BuildTarget buildTarget, string pathToBuiltProject) {
        if (!InjectDependencies()) return;
        UpdateProjectDeps(buildTarget, pathToBuiltProject);
    }

    // Implementation of OnPostProcessUpdateProjectDeps().
    // NOTE: This is separate from the post-processing method to prevent the
    // Mono runtime from loading the Xcode API before calling the post
    // processing step.
    public static void UpdateProjectDeps(
            BuildTarget buildTarget, string pathToBuiltProject) {
        // If the Pods directory does not exist, the pod download step
        // failed.
        var podsDir = Path.Combine(pathToBuiltProject, PODS_DIR);
        if (!Directory.Exists(podsDir)) return;

        Directory.CreateDirectory(Path.Combine(pathToBuiltProject,
                                               "Frameworks"));
        Directory.CreateDirectory(Path.Combine(pathToBuiltProject,
                                               "Resources"));

        string pbxprojPath = GetProjectPath(pathToBuiltProject);
        var project = new UnityEditor.iOS.Xcode.PBXProject();
        project.ReadFromString(File.ReadAllText(pbxprojPath));
        string target = project.TargetGuidByName(TARGET_NAME);

        HashSet<string> frameworks = new HashSet<string>();
        HashSet<string> linkFlags = new HashSet<string>();
        foreach (var frameworkFullPath in
                 Directory.GetDirectories(podsDir, "*.framework",
                                          SearchOption.AllDirectories)) {
            string frameworkName = new DirectoryInfo(frameworkFullPath).Name;
            string destFrameworkPath = Path.Combine("Frameworks",
                                                    frameworkName);
            string destFrameworkFullPath = Path.Combine(pathToBuiltProject,
                                                        destFrameworkPath);
            // Only move this framework if it contains a library.
            // Skip frameworks that consist of just resources, they're handled
            // in a separate import step.
            if (!File.Exists(Path.Combine(
                    frameworkFullPath,
                    Path.GetFileName(frameworkFullPath)
                        .Replace(".framework", "")))) {
                continue;
            }

            PlayServicesSupport.DeleteExistingFileOrDirectory(
                destFrameworkFullPath);
            Directory.Move(frameworkFullPath, destFrameworkFullPath);
            project.AddFileToBuild(
                target,
                project.AddFile(destFrameworkPath,
                                destFrameworkPath,
                                UnityEditor.iOS.Xcode.PBXSourceTree.Source));

            string moduleMapPath =
                Path.Combine(Path.Combine(destFrameworkFullPath, "Modules"),
                             "module.modulemap");

            if (File.Exists(moduleMapPath)) {
                // Parse the modulemap, format spec here:
                // http://clang.llvm.org/docs/Modules.html#module-map-language
                using (StreamReader moduleMapFile =
                       new StreamReader(moduleMapPath)) {
                    string line;
                    char[] delim = {' '};
                    while ((line = moduleMapFile.ReadLine()) != null) {
                        string[] items = line.TrimStart(delim).Split(delim, 2);
                        if (items.Length > 1) {
                            if (items[0] == "link") {
                                if (items[1].StartsWith("framework")) {
                                    items = items[1].Split(delim, 2);
                                    frameworks.Add(items[1].Trim(
                                        new char[] {'\"'}) + ".framework");
                                } else {
                                    linkFlags.Add("-l" + items[1]);
                                }
                            }
                        }
                    }
                }
            }

            string resourcesFolder = Path.Combine(destFrameworkFullPath,
                                                  "Resources");
            if (Directory.Exists(resourcesFolder)) {
                string[] resFiles = Directory.GetFiles(resourcesFolder);
                string[] resFolders =
                    Directory.GetDirectories(resourcesFolder);
                foreach (var resFile in resFiles) {
                    string destFile = Path.Combine("Resources",
                                                   Path.GetFileName(resFile));
                    File.Copy(resFile, Path.Combine(pathToBuiltProject,
                                                    destFile), true);
                    project.AddFileToBuild(
                        target, project.AddFile(
                            destFile, destFile,
                            UnityEditor.iOS.Xcode.PBXSourceTree.Source));
                }
                foreach (var resFolder in resFolders) {
                    string destFolder =
                        Path.Combine("Resources",
                                     new DirectoryInfo(resFolder).Name);
                    string destFolderFullPath =
                        Path.Combine(pathToBuiltProject, destFolder);
                    PlayServicesSupport.DeleteExistingFileOrDirectory(
                        destFolderFullPath);
                    Directory.Move(resFolder, destFolderFullPath);
                    project.AddFileToBuild(
                        target, project.AddFile(
                            destFolder, destFolder,
                            UnityEditor.iOS.Xcode.PBXSourceTree.Source));
                }
            }
        }

        foreach (var framework in frameworks) {
            project.AddFrameworkToProject(target, framework, false);
        }
        foreach (var linkFlag in linkFlags) {
            project.AddBuildProperty(target, "OTHER_LDFLAGS", linkFlag);
        }

        // Add all source files found under the pods directory to the project.
        // This is a very crude way of partially supporting source pods.
        var podPathToProjectPaths = new Dictionary<string, string>();
        // Find pod source files and map them to paths relative to the target
        // Xcode project.
        foreach (var filename in
                 FindFilesWithExtensions(podsDir, SOURCE_FILE_EXTENSIONS)) {
            // Save the path relative to the target project for each path
            // relative to the generated pods Xcode project.
            // +1 in the following expressions to strip the file separator.
            podPathToProjectPaths[filename.Substring(podsDir.Length + 1)] =
                filename.Substring(pathToBuiltProject.Length + 1);
        }
        // Add a reference to each source file in the target project.
        foreach (var podPathProjectPath in podPathToProjectPaths) {
            project.AddFileToBuild(
                target,
                project.AddFile(podPathProjectPath.Value,
                                podPathProjectPath.Value,
                                UnityEditor.iOS.Xcode.PBXSourceTree.Source));
        }

        // Attempt to read per-file compile / build settings from the Pods
        // project.
        var podsProjectPath = GetProjectPath(podsDir, PODS_PROJECT_NAME);
        if (File.Exists(podsProjectPath)) {
            var podsProject = new UnityEditor.iOS.Xcode.PBXProject();
            podsProject.ReadFromString(File.ReadAllText(podsProjectPath));
            foreach (var directory in Directory.GetDirectories(podsDir)) {
                // Each pod will have a top level directory under the pods dir
                // named after the pod.  Also, some pods have build targets in
                // the xcode project where each build target has the same name
                // as the pod such that pod Foo is in directory Foo with build
                // target Foo.  Since we can't read the build targets from the
                // generated Xcode project using Unity's API, we scan the Xcode
                // project for targets to optionally retrieve build settings
                // for each source file the settings can be applied in the
                // target project.
                var podTargetName = Path.GetFileName(directory);
                var podTargetGuid =
                    podsProject.TargetGuidByName(podTargetName);
                Log(String.Format("Looking for target: {0} guid: {1}",
                                  podTargetName, podTargetGuid ?? "null"),
                    verbose: true);
                if (podTargetGuid == null) continue;
                foreach (var podPathProjectPath in podPathToProjectPaths) {
                    var podSourceFileGuid = podsProject.FindFileGuidByRealPath(
                        podPathProjectPath.Key);
                    if (podSourceFileGuid == null) continue;
                    var podSourceFileCompileFlags =
                        podsProject.GetCompileFlagsForFile(podTargetGuid,
                                                           podSourceFileGuid);
                    if (podSourceFileCompileFlags == null) {
                        continue;
                    }
                    var targetSourceFileGuid =
                        project.FindFileGuidByProjectPath(
                            podPathProjectPath.Value);
                    if (targetSourceFileGuid == null) {
                        Log("Unable to find " + podPathProjectPath.Value +
                            " in generated project", level: LogLevel.Warning);
                        continue;
                    }
                    Log(String.Format(
                            "Setting {0} compile flags to ({1})",
                            podPathProjectPath.Key,
                            String.Join(", ",
                                        podSourceFileCompileFlags.ToArray())),
                        verbose: true);
                    project.SetCompileFlagsForFile(
                        target, targetSourceFileGuid,
                        podSourceFileCompileFlags);
                }
            }
        } else if (File.Exists(podsProjectPath + ".xml")) {
            // If neither the Pod pbxproj or pbxproj.xml are present pod
            // install failed earlier and an error has already been report.
            Log("Old Cocoapods installation detected (version: " +
                podsVersion + ").  Unable to include " +
                "source pods, your project will not build.\n" +
                "\n" +
                "Older versions of the pod tool generate xml format Xcode " +
                "projects which can not be read by Unity's xcodeapi.  To " +
                "resolve this issue update Cocoapods to at least version " +
                "1.1.0\n\n" +
                COCOAPOD_INSTALL_INSTRUCTIONS,
                level: LogLevel.Error);
        }

        File.WriteAllText(pbxprojPath, project.WriteToString());
    }
}

}  // namespace Google

#endif  // UNITY_IOS

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
using System.Threading;
using System.Xml;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Google {

[InitializeOnLoad]
public class IOSResolver : AssetPostprocessor {
    /// <summary>
    /// Reference to a Cocoapod.
    /// </summary>
    private class Pod {
        /// <summary>
        /// Name of the pod.
        /// </summary>
        public string name = null;

        /// <summary>
        /// This is a preformatted version expression for pod declarations.
        ///
        /// See: https://guides.cocoapods.org/syntax/podfile.html#pod
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
        /// Additional sources (repositories) to search for this pod.
        ///
        /// Since order is important sources specified in this list
        /// are interleaved across each Pod added to the resolver.
        /// e.g Pod1.source[0], Pod2.source[0] ...
        ////    Pod1.source[1], Pod2.source[1] etc.
        ///
        /// See: https://guides.cocoapods.org/syntax/podfile.html#source
        /// </summary>
        public List<string> sources = new List<string>() {
            "https://github.com/CocoaPods/Specs.git"
        };

        /// <summary>
        /// Minimum target SDK revision required by this pod.
        /// In the form major.minor
        /// </summary>
        public string minTargetSdk = null;

        /// <summary>
        /// Tag that indicates where this was created.
        /// </summary>
        public string createdBy = System.Environment.StackTrace;

        /// <summary>
        /// Whether this pod was read from an XML dependencies file.
        /// </summary>
        public bool fromXmlFile = false;

        /// <summary>
        /// Format a "pod" line for a Podfile.
        /// </summary>
        public string PodFilePodLine {
            get {
                string versionString = "";
                if (!String.IsNullOrEmpty(version)) {
                    versionString = String.Format(", '{0}'", version);
                }
                return String.Format("pod '{0}'{1}", name, versionString);
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
        /// <param name="sources">List of sources to search for all pods.
        /// Each source is a URL that is injected in the source section of a Podfile
        /// See https://guides.cocoapods.org/syntax/podfile.html#source for the description of
        /// a source.</param>
        public Pod(string name, string version, bool bitcodeEnabled,
                   string minTargetSdk, IEnumerable<string> sources) {
            this.name = name;
            this.version = version;
            this.bitcodeEnabled = bitcodeEnabled;
            this.minTargetSdk = minTargetSdk;
            if (sources != null) {
                var allSources = new List<string>(sources);
                allSources.AddRange(this.sources);
                this.sources = allSources;
            }
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

    private class IOSXmlDependencies : XmlDependencies {

        // Adapter method for PlayServicesSupport.LogMessageWithLevel.
        internal static void LogMessage(string message, PlayServicesSupport.LogLevel level) {
            LogLevel iosLevel = LogLevel.Info;
            switch (level) {
                case PlayServicesSupport.LogLevel.Info:
                    iosLevel = LogLevel.Info;
                    break;
                case PlayServicesSupport.LogLevel.Warning:
                    iosLevel = LogLevel.Warning;
                    break;
                case PlayServicesSupport.LogLevel.Error:
                    iosLevel = LogLevel.Error;
                    break;
            }
            IOSResolver.Log(message, level: iosLevel);
        }

        public IOSXmlDependencies() {
            dependencyType = "iOS dependencies";
        }

        /// <summary>
        /// Read XML declared dependencies.
        /// </summary>
        /// <param name="filename">File to read.</param>
        /// <param name="logger">Logging delegate.</param>
        ///
        /// Parses dependencies in the form:
        ///
        /// <dependencies>
        ///   <iosPods>
        ///     <iosPod name="name"
        ///             version="versionSpec"
        ///             bitcodeEnabled="enabled"
        ///             minTargetSdk="sdk">
        ///       <sources>
        ///         <source>uriToPodSource</source>
        ///       </sources>
        ///     </iosPod>
        ///   </iosPods>
        /// </dependencies>
        protected override bool Read(string filename,
                                     PlayServicesSupport.LogMessageWithLevel logger) {
            IOSResolver.Log(String.Format("Reading iOS dependency XML file {0}", filename),
                            verbose: true);
            var sources = new List<string>();
            var trueStrings = new HashSet<string> { "true", "1" };
            var falseStrings = new HashSet<string> { "false", "0" };
            string podName = null;
            string versionSpec = null;
            bool bitcodeEnabled = true;
            string minTargetSdk = null;
            if (!XmlUtilities.ParseXmlTextFileElements(
                filename, logger,
                (reader, elementName, isStart, parentElementName, elementNameStack) => {
                    if (elementName == "dependencies" && parentElementName == "") {
                        return true;
                    } else if (elementName == "iosPods" &&
                               (parentElementName == "dependencies" ||
                                parentElementName == "")) {
                        return true;
                    } else if (elementName == "iosPod" &&
                               parentElementName == "iosPods") {
                        if (isStart) {
                            podName = reader.GetAttribute("name");
                            versionSpec = reader.GetAttribute("version");
                            var bitcodeEnabledString =
                                (reader.GetAttribute("bitcode") ?? "").ToLower();
                            bitcodeEnabled = trueStrings.Contains(bitcodeEnabledString) ||
                                falseStrings.Contains(bitcodeEnabledString) || true;
                            minTargetSdk = reader.GetAttribute("minTargetSdk");
                            sources = new List<string>();
                            if (podName == null) {
                                logger(
                                    String.Format("Pod name not specified while reading {0}:{1}\n",
                                                  filename, reader.LineNumber),
                                    level: PlayServicesSupport.LogLevel.Warning);
                                return false;
                            }
                        } else {
                            AddPodInternal(podName, preformattedVersion: versionSpec,
                                           bitcodeEnabled: bitcodeEnabled,
                                           minTargetSdk: minTargetSdk,
                                           sources: sources,
                                           overwriteExistingPod: false,
                                           createdBy: String.Format("{0}:{1}",
                                                                    filename, reader.LineNumber),
                                           fromXmlFile: true);
                        }
                        return true;
                    } else if (elementName == "sources" &&
                               parentElementName == "iosPod") {
                        return true;
                    } else if (elementName == "source" &&
                               parentElementName == "sources") {
                        if (isStart && reader.Read() && reader.NodeType == XmlNodeType.Text) {
                            sources.Add(reader.ReadContentAsString());
                        }
                        return true;
                    }
                    return false;
                })) {
                return false;
            }
            return true;
        }
    }

    // Dictionary of pods to install in the generated Xcode project.
    private static SortedDictionary<string, Pod> pods =
        new SortedDictionary<string, Pod>();

    // Order of post processing operations.
    private const int BUILD_ORDER_CHECK_COCOAPODS_INSTALL = 1;
    private const int BUILD_ORDER_PATCH_PROJECT = 2;
    private const int BUILD_ORDER_GEN_PODFILE = 3;
    private const int BUILD_ORDER_INSTALL_PODS = 4;
    private const int BUILD_ORDER_UPDATE_DEPS = 5;

    // This is appended to the Podfile filename to store a backup of the original Podfile.
    // ie. "Podfile_Unity".
    private const string UNITY_PODFILE_BACKUP_POSTFIX = "_Unity.backup";

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
    // Ruby Gem executable filename.
    private static string GEM_EXECUTABLE = "gem";

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

    // Build configuration names in Unity generated Xcode projects.  These are required before
    // Unity 2017 where PBXProject.BuildConfigNames was introduced.
    private static string[] BUILD_CONFIG_NAMES = new string[] {
        "Debug",
        "Release",
        "ReleaseForProfiling",
        "ReleaseForRunning",
        "ReleaseForTesting",
    };

    /// <summary>
    /// Main executable target of the Xcode project generated by Unity.
    /// </summary>
    public static string TARGET_NAME = null;

    // Keys in the editor preferences which control the behavior of this module.
    private const string PREFERENCE_NAMESPACE = "Google.IOSResolver.";
    // Whether Legacy Cocoapod installation (project level) is enabled.
    private const string PREFERENCE_COCOAPODS_INSTALL_ENABLED = PREFERENCE_NAMESPACE + "Enabled";
    // Whether Cocoapod uses project files, workspace files, or none (Unity 5.6+ only)
    private const string PREFERENCE_COCOAPODS_INTEGRATION_METHOD =
        PREFERENCE_NAMESPACE + "CocoapodsIntegrationMethod";
    // Whether the Podfile generation is enabled.
    private const string PREFERENCE_PODFILE_GENERATION_ENABLED =
        PREFERENCE_NAMESPACE + "PodfileEnabled";
    // Whether verbose logging is enabled.
    private const string PREFERENCE_VERBOSE_LOGGING_ENABLED =
        PREFERENCE_NAMESPACE + "VerboseLoggingEnabled";
    // Whether execution of the pod tool is performed via the shell.
    private const string PREFERENCE_POD_TOOL_EXECUTION_VIA_SHELL_ENABLED =
        PREFERENCE_NAMESPACE + "PodToolExecutionViaShellEnabled";
    // Whether to try to install Cocoapods tools when iOS is selected as the target platform.
    private const string PREFERENCE_AUTO_POD_TOOL_INSTALL_IN_EDITOR =
        PREFERENCE_NAMESPACE + "AutoPodToolInstallInEditor";
    // A nag prompt disabler setting for turning on workspace integration.
    private const string PREFERENCE_WARN_UPGRADE_WORKSPACE =
        PREFERENCE_NAMESPACE + "UpgradeToWorkspaceWarningDisabled";
    // List of preference keys, used to restore default settings.
    private static string[] PREFERENCE_KEYS = new [] {
        PREFERENCE_COCOAPODS_INSTALL_ENABLED,
        PREFERENCE_COCOAPODS_INTEGRATION_METHOD,
        PREFERENCE_PODFILE_GENERATION_ENABLED,
        PREFERENCE_VERBOSE_LOGGING_ENABLED,
        PREFERENCE_POD_TOOL_EXECUTION_VIA_SHELL_ENABLED,
        PREFERENCE_AUTO_POD_TOOL_INSTALL_IN_EDITOR,
        PREFERENCE_WARN_UPGRADE_WORKSPACE
    };

    // Whether the xcode extension was successfully loaded.
    private static bool iOSXcodeExtensionLoaded = true;
    // Whether a functioning Cocoapods install is present.
    private static bool cocoapodsToolsInstallPresent = false;

    private static string IOS_PLAYBACK_ENGINES_PATH =
        Path.Combine("PlaybackEngines", "iOSSupport");

    // Directory containing downloaded Cocoapods relative to the project
    // directory.
    private const string PODS_DIR = "Pods";
    // Name of the project within PODS_DIR that references downloaded Cocoapods.
    private const string PODS_PROJECT_NAME = "Pods";
    // Prefix for static library filenames.
    private const string LIBRARY_FILENAME_PREFIX = "lib";
    // Extension for static library filenames.
    private const string LIBRARY_FILENAME_EXTENSION = ".a";

    // Version of the Cocoapods installation.
    private static string podsVersion = "";

    private static string PODFILE_GENERATED_COMMENT = "# IOSResolver Generated Podfile";

    // Default iOS target SDK if the selected version is invalid.
    private const int DEFAULT_TARGET_SDK = 82;
    // Valid iOS target SDK version.
    private static Regex TARGET_SDK_REGEX = new Regex("^[0-9]+\\.[0-9]$");

    // Current window being used for a long running shell command.
    private static CommandLineDialog commandLineDialog = null;
    // Mutex for access to commandLineDialog.
    private static System.Object commandLineDialogLock = new System.Object();

    // Regex for parsing comma separated values, as used in the pod dependency specification.
    private static Regex CSV_SPLIT_REGEX = new Regex(@"(?:^|,\s*)'([^']*)'", RegexOptions.Compiled);

    // Parses a source URL from a Podfile.
    private static Regex PODFILE_SOURCE_REGEX = new Regex(@"^\s*source\s+'([^']*)'");

    // Parses dependencies from XML dependency files.
    private static IOSXmlDependencies xmlDependencies = new IOSXmlDependencies();

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

        // If Cocoapod tool auto-installation is enabled try installing on the first update of
        // the editor when the editor environment has been initialized.
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS &&
            AutoPodToolInstallInEditorEnabled && CocoapodsIntegrationEnabled && !InBatchMode) {
            EditorApplication.update -= AutoInstallCocoapods;
            EditorApplication.update += AutoInstallCocoapods;
        }


        // Prompt the user to use workspaces if they aren't at least using project level
        // integration.
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS &&
            (CocoapodsIntegrationMethod)EditorPrefs.GetInt(PREFERENCE_COCOAPODS_INTEGRATION_METHOD,
                CocoapodsIntegrationUpgradeDefault) == CocoapodsIntegrationMethod.None &&
            !InBatchMode && !UpgradeToWorkspaceWarningDisabled) {

            switch (EditorUtility.DisplayDialogComplex(
                "Warning: Cocoapods integration is disabled!",
                "Would you like to enable Cocoapods integration with workspaces?\n\n" +
                "Unity 5.6+ now supports loading workspaces generated from Cocoapods.\n" +
                "If you enable this, and still use Unity less than 5.6, it will fallback " +
                "to integrating Cocoapods with the .xcodeproj file.\n",
                "Yes", "Not Now", "Silence Warning")) {
                case 0:  // Yes
                    EditorPrefs.SetInt(PREFERENCE_COCOAPODS_INTEGRATION_METHOD,
                                       (int)CocoapodsIntegrationMethod.Workspace);
                    break;
                case 1:  // Not now
                    break;
                case 2:  // Ignore
                    UpgradeToWorkspaceWarningDisabled = true;
                    break;
            }
        }

        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS) {
            RefreshXmlDependencies();
        }
    }

    // Display the iOS resolver settings menu.
    [MenuItem("Assets/Play Services Resolver/iOS Resolver/Settings")]
    public static void SettingsDialog() {
        IOSResolverSettingsDialog window = (IOSResolverSettingsDialog)
            EditorWindow.GetWindow(typeof(IOSResolverSettingsDialog), true,
                                   "iOS Resolver Settings");
        window.Initialize();
        window.Show();
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
    /// Reset settings of this plugin to default values.
    /// </summary>
    internal static void RestoreDefaultSettings() {
        VersionHandlerImpl.RestoreDefaultSettings(PREFERENCE_KEYS);
    }

    /// <summary>
    /// The method used to integrate Cocoapods with the build.
    /// </summary>
    public enum CocoapodsIntegrationMethod {
        None = 0,
        Project,
        Workspace
    };

    /// <summary>
    /// When first upgrading, decide on workspace integration based on previous settings.
    /// </summary>
    private static int CocoapodsIntegrationUpgradeDefault {
        get {
            return LegacyCocoapodsInstallEnabled ?
                (int)CocoapodsIntegrationMethod.Workspace :
                (int)CocoapodsIntegrationMethod.Project;
        }
    }

    /// <summary>
    /// IOSResolver Unity Preferences setting indicating which Cocoapods integration method to use.
    /// </summary>
    public static CocoapodsIntegrationMethod CocoapodsIntegrationMethodPref {
        get {
            return (CocoapodsIntegrationMethod)EditorPrefs.GetInt(
                PREFERENCE_COCOAPODS_INTEGRATION_METHOD,
                defaultValue: CocoapodsIntegrationUpgradeDefault);
        }
        set { EditorPrefs.SetInt(PREFERENCE_COCOAPODS_INTEGRATION_METHOD, (int)value); }
    }

    /// <summary>
    /// Deprecated: Enable / disable Cocoapods installation.
    /// Please use CocoapodsIntegrationEnabled instead.
    /// </summary>
    [System.Obsolete("CocoapodsInstallEnabled is deprecated, please use " +
                     "CocoapodsIntegrationEnabled instead.")]
    public static bool CocoapodsInstallEnabled {
        get { return LegacyCocoapodsInstallEnabled; }
        set { LegacyCocoapodsInstallEnabled = value; }
    }

    /// <summary>
    /// A formerly used setting for project integration.
    /// It's kept as a private function to seed the default for the new setting:
    /// CocoapodsIntegrationEnabled.
    /// </summary>
    private static bool LegacyCocoapodsInstallEnabled {
        get { return EditorPrefs.GetBool(PREFERENCE_COCOAPODS_INSTALL_ENABLED,
                                         defaultValue: true); }
        set { EditorPrefs.SetBool(PREFERENCE_COCOAPODS_INSTALL_ENABLED, value); }
    }

    /// <summary>
    /// Enable / disable Podfile generation.
    /// </summary>
    public static bool PodfileGenerationEnabled {
        get { return EditorPrefs.GetBool(PREFERENCE_PODFILE_GENERATION_ENABLED,
                                         defaultValue: true); }
        set { EditorPrefs.SetBool(PREFERENCE_PODFILE_GENERATION_ENABLED, value); }
    }

    /// <summary>
    /// Enable / disable execution of the pod tool via the shell.
    /// </summary>
    public static bool PodToolExecutionViaShellEnabled {
        get { return EditorPrefs.GetBool(PREFERENCE_POD_TOOL_EXECUTION_VIA_SHELL_ENABLED,
                                         defaultValue: false); }
        set { EditorPrefs.SetBool(PREFERENCE_POD_TOOL_EXECUTION_VIA_SHELL_ENABLED, value); }
    }

    /// <summary>
    /// Enable automated pod tool installation in the editor.  This is only performed when the
    /// editor isn't launched in batch mode.
    /// </summary>
    public static bool AutoPodToolInstallInEditorEnabled {
        get { return EditorPrefs.GetBool(PREFERENCE_AUTO_POD_TOOL_INSTALL_IN_EDITOR,
                                         defaultValue: true); }
        set { EditorPrefs.SetBool(PREFERENCE_AUTO_POD_TOOL_INSTALL_IN_EDITOR, value); }
    }

    /// <summary>
    /// Get / set the nag prompt disabler setting for turning on workspace integration.
    /// </summary>
    public static bool UpgradeToWorkspaceWarningDisabled {
        get { return EditorPrefs.GetBool(PREFERENCE_WARN_UPGRADE_WORKSPACE,
                                         defaultValue: false); }
        set { EditorPrefs.SetBool(PREFERENCE_WARN_UPGRADE_WORKSPACE, value); }
    }

    /// <summary>
    /// Enable / disable verbose logging.
    /// </summary>
    public static bool VerboseLoggingEnabled {
        get { return EditorPrefs.GetBool(PREFERENCE_VERBOSE_LOGGING_ENABLED,
                                         defaultValue: false); }
        set { EditorPrefs.SetBool(PREFERENCE_VERBOSE_LOGGING_ENABLED, value); }
    }


    /// <summary>
    /// Determine whether it's possible to perform iOS dependency injection.
    /// </summary>
    public static bool Enabled { get { return iOSXcodeExtensionLoaded; } }

    /// <summary>
    /// Whether the editor was launched in batch mode.
    /// </summary>
    private static bool InBatchMode {
        get { return System.Environment.CommandLine.Contains("-batchmode"); }
    }

    private const float epsilon = 1e-7f;

    /// <summary>
    /// Whether or not Unity can load a workspace file if it's present.
    /// </summary>
    private static bool UnityCanLoadWorkspace {
        get {
            // Unity started supporting workspace loading in the released version of Unity 5.6
            // but not in the beta. So check if this is exactly 5.6, but also beta.
            if (Math.Abs(
                    VersionHandler.GetUnityVersionMajorMinor() - 5.6f) < epsilon) {
                // Unity non-beta versions look like 5.6.0f1 while beta versions look like:
                // 5.6.0b11, so looking for the b in the string (especially confined to 5.6),
                // should be sufficient for determining that it's the beta.
                if (UnityEngine.Application.unityVersion.Contains(".0b")) {
                    return false;
                }
            }
            // If Unity was launched from Unity Cloud Build the build pipeline does not
            // open the xcworkspace so we need to force project level integration of frameworks.
            if (System.Environment.CommandLine.Contains("-bvrbuildtarget")) {
                return false;
            }
            return (VersionHandler.GetUnityVersionMajorMinor() >= 5.6f - epsilon);
        }
    }

    /// <summary>
    /// Whether or not we should do Xcode workspace level integration of cocoapods.
    /// False if the Unity version doesn't support loading workspaces.
    /// </summary>
    private static bool CocoapodsWorkspaceIntegrationEnabled {
        get {
            return UnityCanLoadWorkspace &&
            CocoapodsIntegrationMethodPref == CocoapodsIntegrationMethod.Workspace;
        }
    }

    /// <summary>
    /// Whether or not we should do Xcode project level integration of cocoapods.
    /// True if configured for project integration or workspace integration is enabled but using
    /// an older version of Unity that doesn't support loading workspaces (as a fallback).
    /// </summary>
    private static bool CocoapodsProjectIntegrationEnabled {
        get {
            return CocoapodsIntegrationMethodPref == CocoapodsIntegrationMethod.Project ||
                (!UnityCanLoadWorkspace &&
                CocoapodsIntegrationMethodPref == CocoapodsIntegrationMethod.Workspace);
        }
    }

    /// <summary>
    /// Whether or not we are integrating the pod dependencies into an Xcode build that Unity loads.
    /// </summary>
    public static bool CocoapodsIntegrationEnabled {
        get {
            return EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS &&
                CocoapodsIntegrationMethodPref != CocoapodsIntegrationMethod.None;
        }
    }

    /// <summary>
    /// Log severity.
    /// </summary>
    internal enum LogLevel {
        Info,
        Warning,
        Error,
    };

    private delegate void LogMessageDelegate(string message, bool verbose = false,
                                            LogLevel level = LogLevel.Info);

    /// <summary>
    /// Log a message.
    /// </summary>
    /// <param name="message">Message to log.</param>
    /// <param name="verbose">Whether the message should only be displayed if verbose logging is
    /// enabled.</param>
    /// <param name="level">Severity of the message.</param>
    internal static void Log(string message, bool verbose = false,
                             LogLevel level = LogLevel.Info) {
        if (!verbose || VerboseLoggingEnabled || InBatchMode) {
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
    /// Display a message in a dialog and log to the console.
    /// </summary>
    internal static void LogToDialog(string message, bool verbose = false,
                             LogLevel level = LogLevel.Info) {
        if (!verbose) EditorUtility.DisplayDialog("iOS Resolver", message, "OK");
        Log(message, verbose: verbose, level: level);
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
    /// Convert dependency version specifications to the version expression used by pods.
    /// </summary>
    /// <param name="dependencyVersion">
    /// Version specification string.
    ///
    /// If it ends with "+" the specified version up to the next major
    /// version is selected.
    /// If "LATEST", null or empty this pulls the latest revision.
    /// A version number "1.2.3" selects a specific version number.
    /// </param>
    /// <returns>The version expression formatted for pod dependencies.
    /// For example, "1.2.3+" would become "~> 1.2.3".</returns>
    private static string PodVersionExpressionFromVersionDep(string dependencyVersion) {
        if (String.IsNullOrEmpty(dependencyVersion) || dependencyVersion.Equals("LATEST")) {
            return null;
        }
        if (dependencyVersion.EndsWith("+")) {
            return String.Format("~> {0}",
                dependencyVersion.Substring(0, dependencyVersion.Length - 1));
        }
        return dependencyVersion;
    }

    /// <summary>
    /// Tells the app what pod dependencies are needed.
    /// This is called from a deps file in each API to aggregate all of the
    /// dependencies to automate the Podfile generation.
    /// </summary>
    /// <param name="podName">pod path, for example "Google-Mobile-Ads-SDK" to
    /// be included</param>
    /// <param name="version">Version specification.
    /// See PodVersionExpressionFromVersionDep for how the version string is processed.</param>
    /// <param name="bitcodeEnabled">Whether the pod was compiled with bitcode
    /// enabled.  If this is set to false on a pod, the entire project will
    /// be configured with bitcode disabled.</param>
    /// <param name="minTargetSdk">Minimum SDK revision required by this
    /// pod.</param>
    /// <param name="sources">List of sources to search for all pods.
    /// Each source is a URL that is injected in the source section of a Podfile
    /// See https://guides.cocoapods.org/syntax/podfile.html#source for the description of
    /// a source.</param>
    public static void AddPod(string podName, string version = null,
                              bool bitcodeEnabled = true,
                              string minTargetSdk = null,
                              IEnumerable<string> sources = null) {
        AddPodInternal(podName, preformattedVersion: PodVersionExpressionFromVersionDep(version),
                       bitcodeEnabled: bitcodeEnabled, minTargetSdk: minTargetSdk,
                       sources: sources);
    }

    /// <summary>
    /// Same as AddPod except the version string is used in the pod declaration directly.
    /// See AddPod.
    /// </summary>
    /// <param name="podName">pod path, for example "Google-Mobile-Ads-SDK" to
    /// be included</param>
    /// <param name="preformattedVersion">Podfile version specification similar to what is
    /// returned by PodVersionExpressionFromVersionDep().</param>
    /// <param name="bitcodeEnabled">Whether the pod was compiled with bitcode
    /// enabled.  If this is set to false on a pod, the entire project will
    /// be configured with bitcode disabled.</param>
    /// <param name="minTargetSdk">Minimum SDK revision required by this
    /// pod.</param>
    /// <param name="sources">List of sources to search for all pods.
    /// Each source is a URL that is injected in the source section of a Podfile
    /// See https://guides.cocoapods.org/syntax/podfile.html#source for the description of
    /// a source.</param>
    /// <param name="overwriteExistingPod">Overwrite an existing pod.</param>
    /// <param name="createdBy">Tag of the object that added this pod.</param>
    /// <param name="fromXmlFile">Whether this was added via an XML dependency.</param>
    private static void AddPodInternal(string podName, string preformattedVersion = null,
                                       bool bitcodeEnabled = true,
                                       string minTargetSdk = null,
                                       IEnumerable<string> sources = null,
                                       bool overwriteExistingPod = true,
                                       string createdBy = null,
                                       bool fromXmlFile = false) {
        if (!overwriteExistingPod && pods.ContainsKey(podName)) {
            Log(String.Format("Pod {0} already present, ignoring.\n" +
                              "Original declaration {1}\n" +
                              "Ignored declarion {2}\n", podName,
                              pods[podName].createdBy, createdBy ?? "(unknown)"),
                level: LogLevel.Warning);
            return;
        }
        var pod = new Pod(podName, preformattedVersion, bitcodeEnabled, minTargetSdk, sources);
        pod.createdBy = createdBy ?? pod.createdBy;
        pod.fromXmlFile = fromXmlFile;
        pods[podName] = pod;
        Log(String.Format(
            "AddPod - name: {0} version: {1} bitcode: {2} sdk: {3} sources: {4}\n" +
            "createdBy: {5}\n\n",
            podName, preformattedVersion ?? "null", bitcodeEnabled.ToString(),
            minTargetSdk ?? "null",
            sources != null ? String.Join(", ", (new List<string>(sources)).ToArray()) : "(null)",
            createdBy ?? pod.createdBy),
            verbose: true);

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
    /// Menu item that installs Cocoapods if it's not already installed.
    /// </summary>
    [MenuItem("Assets/Play Services Resolver/iOS Resolver/Install Cocoapods")]
    public static void InstallCocoapodsMenu() {
        InstallCocoapodsInteractive();
    }

    /// <summary>
    /// Auto install Cocoapods tools if they're not already installed.
    /// </summary>
    public static void AutoInstallCocoapods() {
        InstallCocoapodsInteractive(displayAlreadyInstalled: false);
        EditorApplication.update -= AutoInstallCocoapods;
    }

    /// <summary>
    /// Interactively installs Cocoapods if it's not already installed.
    /// </summary>
    public static void InstallCocoapodsInteractive(bool displayAlreadyInstalled = true) {
        bool installCocoapods = true;
        lock (commandLineDialogLock) {
            if (commandLineDialog != null) {
                // If the installation is still in progress, display the dialog.
                commandLineDialog.Show();
                installCocoapods = false;
            }
        }
        if (installCocoapods) {
            InstallCocoapods(true, ".", displayAlreadyInstalled: displayAlreadyInstalled);
        }
    }

    /// <summary>
    /// Determine whether a gem (Ruby package) is installed.
    /// </summary>
    /// <param name="gemPackageName">Name of the package to check.</param>
    /// <param name="logMessage">Delegate use to log a failure message if the package manager
    /// returns an error code.</param>
    /// <returns>true if the package is installed, false otherwise.</returns>
    private static bool QueryGemInstalled(string gemPackageName,
                                          LogMessageDelegate logMessage = null) {
        logMessage = logMessage ?? Log;
        logMessage(String.Format("Determine whether Ruby Gem {0} is installed", gemPackageName),
                   verbose: true);
        var query = String.Format("list {0} --no-versions", gemPackageName);
        var result = RunCommand(GEM_EXECUTABLE, query);
        if (result.exitCode == 0) {
            foreach (var line in result.stdout.Split(new string[] { Environment.NewLine },
                                                     StringSplitOptions.None)) {
                if (line == gemPackageName) {
                    logMessage(String.Format("{0} is installed", gemPackageName), verbose: true);
                    return true;
                }
            }
        } else {
            logMessage(
                String.Format("Unable to determine whether the {0} gem is " +
                              "installed, will attempt to install anyway.\n\n" +
                              "'{1} {2}' failed with error code ({3}):\n" +
                              "{4}\n" +
                              "{5}\n",
                              gemPackageName, GEM_EXECUTABLE, query, result.exitCode,
                              result.stdout, result.stderr),
                level: LogLevel.Warning);
        }
        return false;
    }

    /// <summary>
    /// Install Cocoapods if it's not already installed.
    /// </summary>
    /// <param name="interactive">Whether this method should display information in pop-up
    /// dialogs.</param>
    /// <param name="workingDirectory">Where to run the pod tool's setup command.</param>
    /// <param name="displayAlreadyInstalled">Whether to display whether the tools are already
    /// installed.</param>
    public static void InstallCocoapods(bool interactive, string workingDirectory,
                                        bool displayAlreadyInstalled = true) {
        cocoapodsToolsInstallPresent = false;
        // Cocoapod tools are currently only available on OSX, don't attempt to install them
        // otherwise.
        if (UnityEngine.RuntimePlatform.OSXEditor != UnityEngine.Application.platform) {
            return;
        }

        LogMessageDelegate logMessage = null;
        if (interactive) {
            logMessage = LogToDialog;
        } else {
            logMessage = Log;
        }

        var podToolPath = FindPodTool();
        if (!String.IsNullOrEmpty(podToolPath)) {
            var installationFoundMessage = "Cocoapods installation detected " + podToolPath;
            if (displayAlreadyInstalled) logMessage(installationFoundMessage);
            cocoapodsToolsInstallPresent = true;
            return;
        }

        var complete = new AutoResetEvent(false);
        var commonInstallErrorMessage =
            "It will not be possible to install Cocoapods in the generated Xcode " +
            "project which will result in link errors when building your " +
            "application.\n\n" +
            "For more information see:\n" +
            "  https://guides.cocoapods.org/using/getting-started.html\n\n";

        // Log the set of install pods.
        RunCommand(GEM_EXECUTABLE, "list");

        // Gem is being executed in an RVM directory it's already configured to perform a
        // user install.  When RVM is configured "--user-install" ends up installing gems
        // in the wrong directory such that they're not visible to either the package manager
        // or Ruby.
        var gemEnvironment = ReadGemsEnvironment();
        string installArgs = "--user-install";
        if (gemEnvironment != null) {
            List<string> installationDir;
            if (gemEnvironment.TryGetValue("INSTALLATION DIRECTORY", out installationDir)) {
                foreach (var dir in installationDir) {
                    if (dir.IndexOf("/.rvm/") >= 0) {
                        installArgs = "";
                        break;
                    }
                }
            }
        }
        if (VerboseLoggingEnabled || InBatchMode) {
            installArgs += " --verbose";
        }

        var commandList = new List<CommandItem>();
        if (!QueryGemInstalled("activesupport", logMessage: logMessage)) {
            // Workaround activesupport (dependency of the Cocoapods gem) requiring
            // Ruby 2.2.2 and above.
            // https://github.com/CocoaPods/CocoaPods/issues/4711
            commandList.Add(
                new CommandItem {
                    Command = GEM_EXECUTABLE,
                    Arguments = "install activesupport -v 4.2.6 " + installArgs
                });
        }
        commandList.Add(new CommandItem {
                Command = GEM_EXECUTABLE,
                Arguments = "install cocoapods " + installArgs
            });
        commandList.Add(new CommandItem { Command = POD_EXECUTABLE, Arguments = "setup" });

        RunCommandsAsync(
            commandList.ToArray(),
            (int commandIndex, CommandItem[] commands, CommandLine.Result result,
                CommandLineDialog dialog) => {
                var lastCommand = commands[commandIndex];
                commandIndex += 1;
                if (result.exitCode != 0) {
                    logMessage(String.Format(
                        "Failed to install Cocoapods for the current user.\n\n" +
                        "{0}\n" +
                        "'{1} {2}' failed with code ({3}):\n" +
                        "{4}\n\n" +
                        "{5}\n",
                        commonInstallErrorMessage, lastCommand.Command,
                        lastCommand.Arguments, result.exitCode, result.stdout,
                        result.stderr), level: LogLevel.Error);
                    complete.Set();
                    return -1;
                }
                // Pod setup process (should be the last command in the list).
                if (commandIndex == commands.Length - 1) {
                    podToolPath = FindPodTool();
                    if (String.IsNullOrEmpty(podToolPath)) {
                        logMessage(String.Format(
                            "'{0} {1}' succeeded but the {2} tool cannot be found.\n\n" +
                            "{3}\n", lastCommand.Command, lastCommand.Arguments,
                            POD_EXECUTABLE, commonInstallErrorMessage), level: LogLevel.Error);
                        complete.Set();
                        return -1;
                    }
                    if (dialog != null) {
                        dialog.bodyText += ("\n\nDownloading Cocoapods Master Repository\n" +
                                            "(this can take a while)\n");
                    }
                    commands[commandIndex].Command = podToolPath;
                } else if (commandIndex == commands.Length) {
                    complete.Set();
                    logMessage("Cocoapods tools successfully installed.");
                    cocoapodsToolsInstallPresent = true;
                }
                return commandIndex;
            }, displayDialog: interactive, summaryText: "Installing Cocoapods...");

        // If this wasn't started interactively, block until execution is complete.
        if (!interactive) complete.WaitOne();
    }

    /// <summary>
    /// If Cocoapod installation is enabled, prompt the user to install Cocoapods if it's not
    /// present on the machine.
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_CHECK_COCOAPODS_INSTALL)]
    public static void OnPostProcessEnsurePodsInstallation(BuildTarget buildTarget,
                                                           string pathToBuiltProject) {
        if (!CocoapodsIntegrationEnabled) return;
        InstallCocoapods(false, pathToBuiltProject);
    }

    /// <summary>
    /// Post-processing build step to patch the generated project files.
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_PATCH_PROJECT)]
    public static void OnPostProcessPatchProject(BuildTarget buildTarget,
                                                 string pathToBuiltProject) {
        if (!InjectDependencies() || !PodfileGenerationEnabled ||
            !CocoapodsProjectIntegrationEnabled || !cocoapodsToolsInstallPresent) {
            return;
        }
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
        project.AddBuildProperty(target, "HEADER_SEARCH_PATHS",
                                 "$(PROJECT_DIR)/" + PODS_DIR + "/Headers/Public");
        project.AddBuildProperty(target, "FRAMEWORK_SEARCH_PATHS",
                                 "$(inherited)");
        project.AddBuildProperty(target, "FRAMEWORK_SEARCH_PATHS",
                                 "$(PROJECT_DIR)/Frameworks");
        project.AddBuildProperty(target, "LIBRARY_SEARCH_PATHS", "$(inherited)");
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
        if (!InjectDependencies() || !PodfileGenerationEnabled) return;
        GenPodfile(buildTarget, pathToBuiltProject);
    }

    /// <summary>
    /// Get the path to the generated Podfile.
    /// </summary>
    private static string GetPodfilePath(string pathToBuiltProject) {
        return Path.Combine(pathToBuiltProject, "Podfile");
    }

    /// <summary>
    /// Checks to see if a podfile, not written by the IOSResolver is present.
    /// </summary>
    /// <param name="suspectedUnityPodfilePath">The path we suspect is written by Unity. This is
    /// either the original file or a backup of the path.</param>
    /// <returns>The path to the Podfile, presumed to be generated by Unity.</returns>
    private static string FindExistingUnityPodfile(string suspectedUnityPodfilePath) {
        if (!File.Exists(suspectedUnityPodfilePath)) return null;

        System.IO.StreamReader podfile = new System.IO.StreamReader(suspectedUnityPodfilePath);
        string firstline = podfile.ReadLine();
        podfile.Close();
        // If the podfile written is one that we created, then we need to look for the backup of the
        // original Unity podfile. This is necessary for cases when the user does an "append build"
        // in Unity. Since we back up the original podfile, we'll re-parse it when regenerating
        // the dependencies this time around.
        if (firstline == null || firstline.StartsWith(PODFILE_GENERATED_COMMENT)) {
            return FindExistingUnityPodfile(suspectedUnityPodfilePath +
                                            UNITY_PODFILE_BACKUP_POSTFIX);
        }

        return suspectedUnityPodfilePath;
    }

    private static void ParseUnityDeps(string unityPodfilePath) {
        Log("Parse Unity deps from: " + unityPodfilePath, verbose: true);

        System.IO.StreamReader unityPodfile = new System.IO.StreamReader(unityPodfilePath);
        const string POD_TAG = "pod ";
        string line;

        // We are only interested in capturing the dependencies "Pod depName, depVersion", inside
        // of the specific target. However there can be nested targets such as for testing, so we're
        // counting the depth to determine when to capture the pods. Also we only ever enter the
        // first depth if we're in the exact right target.
        int capturingPodsDepth = 0;
        var sources = new List<string>();
        while ((line = unityPodfile.ReadLine()) != null) {
            line = line.Trim();
            var sourceLineMatch = PODFILE_SOURCE_REGEX.Match(line);
            if (sourceLineMatch.Groups.Count > 1) {
                sources.Add(sourceLineMatch.Groups[1].Value);
                continue;
            }
            if (line.StartsWith("target 'Unity-iPhone' do")) {
                capturingPodsDepth++;
                continue;
            }

            if (capturingPodsDepth == 0) continue;

            // handle other scopes roughly
            if (line.EndsWith(" do")) {
                capturingPodsDepth++;  // Ignore nested targets like tests
            } else if (line == "end") {
                capturingPodsDepth--;
            }

            if (capturingPodsDepth != 1) continue;

            if (line.StartsWith(POD_TAG)) {
                var matches = CSV_SPLIT_REGEX.Matches(line.Substring(POD_TAG.Length));
                if (matches.Count > 0) {

                    // Add the version as is, if it was present in the original podfile.
                    if (matches.Count > 1) {
                        string matchedName = matches[0].Groups[1].Captures[0].Value;
                        string matchedVersion = matches[1].Groups[1].Captures[0].Value;
                        Log(String.Format("Preserving Unity Pod: {0}\nat version: {1}", matchedName,
                                          matchedVersion), verbose: true);

                        AddPodInternal(matchedName, preformattedVersion: matchedVersion,
                                       bitcodeEnabled: true, sources: sources,
                                       overwriteExistingPod: false);
                    } else {
                        string matchedName = matches[0].Groups[1].Captures[0].Value;
                        Log(String.Format("Preserving Unity Pod: {0}", matchedName), verbose: true);

                        AddPodInternal(matchedName, sources: sources,
                                       overwriteExistingPod: false);
                    }
                }
            }
        }
        unityPodfile.Close();
    }

    /// <summary>
    /// Generate the sources section from the set of "pods" in this class.
    ///
    /// Each source is interleaved across each pod - removing duplicates - as Cocoapods searches
    /// each source in order for each pod.
    ///
    /// See Pod.sources for more information.
    /// </summary>
    /// <returns>String which contains the sources section of a Podfile.  For example, if the
    /// Pod instances referenced by this class contain sources...
    ///
    /// ["http://myrepo.com/Specs.git", "http://anotherrepo.com/Specs.git"]
    ///
    /// this returns the string...
    ///
    /// source 'http://myrepo.com/Specs.git'
    /// source 'http://anotherrepo.com/Specs.git'
    private static string GeneratePodfileSourcesSection() {
        var interleavedSourcesLines = new List<string>();
        var processedSources = new HashSet<string>();
        int sourceIndex = 0;
        bool sourcesAvailable;
        do {
            sourcesAvailable = false;
            foreach (var pod in pods.Values) {
                if (sourceIndex < pod.sources.Count) {
                    sourcesAvailable = true;
                    var source = pod.sources[sourceIndex];
                    if (processedSources.Add(source)) {
                        interleavedSourcesLines.Add(String.Format("source '{0}'", source));
                    }
                }
            }
            sourceIndex ++;
        } while (sourcesAvailable);
        return String.Join("\n", interleavedSourcesLines.ToArray()) + "\n";
    }

    // Implementation of OnPostProcessGenPodfile().
    // NOTE: This is separate from the post-processing method to prevent the
    // Mono runtime from loading the Xcode API before calling the post
    // processing step.
    public static void GenPodfile(BuildTarget buildTarget,
                                  string pathToBuiltProject) {
        string podfilePath = GetPodfilePath(pathToBuiltProject);

        string unityPodfile = FindExistingUnityPodfile(podfilePath);
        Log(String.Format("Detected Unity Podfile: {0}", unityPodfile), verbose: true);
        if (unityPodfile != null) {
            ParseUnityDeps(unityPodfile);
            if (podfilePath == unityPodfile) {
                string unityBackupPath = podfilePath + UNITY_PODFILE_BACKUP_POSTFIX;
                if (File.Exists(unityBackupPath)) {
                    File.Delete(unityBackupPath);
                }
                File.Move(podfilePath, unityBackupPath);
            }
        }

        Log(String.Format("Generating Podfile {0} with {1} integration.", podfilePath,
                          (CocoapodsWorkspaceIntegrationEnabled ? "Xcode workspace" :
                          (CocoapodsProjectIntegrationEnabled ? "Xcode project" : "no target"))),
            verbose: true);
        using (StreamWriter file = new StreamWriter(podfilePath)) {
            file.Write(GeneratePodfileSourcesSection() +
                       (CocoapodsProjectIntegrationEnabled ?
                        "install! 'cocoapods', :integrate_targets => false\n" : "") +
                       String.Format("platform :ios, '{0}'\n\n", TargetSdk) +
                       "target '" + TARGET_NAME + "' do\n");
            foreach(var pod in pods.Values) {
                file.WriteLine(pod.PodFilePodLine);
            }
            file.WriteLine("end");
        }
    }

    /// <summary>
    /// Read the Gems environment.
    /// </summary>
    /// <returns>Dictionary of environment properties or null if there was a problem reading
    /// the environment.</returns>
    private static Dictionary<string, List<string>> ReadGemsEnvironment() {
        var result = RunCommand(GEM_EXECUTABLE, "environment");
        if (result.exitCode != 0) {
            return null;
        }
        // gem environment outputs YAML for all config variables.  Perform some very rough YAML
        // parsing to get the environment into a usable form.
        var gemEnvironment = new Dictionary<string, List<string>>();
        const string listItemPrefix = "- ";
        int previousIndentSize = 0;
        List<string> currentList = null;
        char[] listToken = new char[] { ':' };
        foreach (var line in result.stdout.Split(new char[] { '\r', '\n' })) {
            var trimmedLine = line.Trim();
            var indentSize = line.Length - trimmedLine.Length;
            if (indentSize < previousIndentSize) currentList = null;

            if (trimmedLine.StartsWith(listItemPrefix)) {
                trimmedLine = trimmedLine.Substring(listItemPrefix.Length).Trim();
                if (currentList == null) {
                    var tokens = trimmedLine.Split(listToken);
                    currentList = new List<string>();
                    gemEnvironment[tokens[0].Trim()] = currentList;
                    var value = tokens.Length == 2 ? tokens[1].Trim() : null;
                    if (!String.IsNullOrEmpty(value)) {
                        currentList.Add(value);
                        currentList = null;
                    }
                } else if (indentSize >= previousIndentSize) {
                    currentList.Add(trimmedLine);
                }
            } else {
                currentList = null;
            }
            previousIndentSize = indentSize;
        }
        return gemEnvironment;
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
        var environment = ReadGemsEnvironment();
        if (environment != null) {
            const string executableDir = "EXECUTABLE DIRECTORY";
            foreach (string environmentVariable in new [] { executableDir, "GEM PATHS" }) {
                List<string> paths;
                if (environment.TryGetValue(environmentVariable, out paths)) {
                    foreach (var path in paths) {
                        var binPath = environmentVariable == executableDir ? path :
                            Path.Combine(path, "bin");
                        var podPath = Path.Combine(binPath, POD_EXECUTABLE);
                        Log("Checking gems install path for cocoapods tool " + podPath,
                            verbose: true);
                        if (File.Exists(podPath)) {
                            Log("Found cocoapods tool in " + podPath, verbose: true);
                            return podPath;
                        }
                    }
                }
            }
        }
        return null;
    }


    /// <summary>
    /// Command line command to execute.
    /// </summary>
    private class CommandItem {
        /// <summary>
        /// Command to excecute.
        /// </summary>
        public string Command { get; set; }
        /// <summary>
        /// Arguments for the command.
        /// </summary>
        public string Arguments { get; set; }
        /// <summary>
        /// Directory to execute the command.
        /// </summary>
        public string WorkingDirectory { get; set; }
        /// <summary>
        /// Get a string representation of the command line.
        /// </summary>
        public override string ToString() {
            return String.Format("{0} {1}", Command, Arguments ?? "");
        }
    };

    /// <summary>
    /// Called when one of the commands complete in RunCommandsAsync().
    /// </summary>
    /// <param name="commandIndex">Index of the completed command in commands.</param>
    /// <param name="commands">Array of commands being executed.</param>
    /// <param name="result">Result of the last command.</param>
    /// <param name="dialog">Dialog box, if the command was executed in a dialog.</param>
    /// <returns>Reference to the next command in the list to execute,
    /// -1 or commands.Length to stop execution.</returns>
    private delegate int CommandItemCompletionHandler(
         int commandIndex, CommandItem[] commands,
         CommandLine.Result result, CommandLineDialog dialog);

    /// <summary>
    /// Container for a delegate which enables a lambda to reference itself.
    /// </summary>
    private class DelegateContainer<T> {
        /// <summary>
        /// Delegate method associated with the container.  This enables the
        /// following pattern:
        ///
        /// var container = new DelegateContainer<CommandLine.CompletionHandler>();
        /// container.Handler = (CommandLine.Result result) => { RunNext(container.Handler); };
        /// </summary>
        public T Handler { get; set; }
    }

    /// <summary>
    /// Write the result of a command to the log.
    /// </summary>
    /// <param name="command">Command that was executed.</param>
    /// <param name="result">Result of the command.</param>
    private static void LogCommandLineResult(string command, CommandLine.Result result) {
        Log(String.Format("'{0}' completed with code {1}\n\n" +
                          "{2}\n" +
                          "{3}\n", command, result.exitCode, result.stdout, result.stderr),
            verbose: true);
    }

    /// <summary>
    /// Run a series of commands asynchronously optionally displaying a dialog.
    /// </summary>
    /// <param name="commands">Commands to execute.</param>
    /// <param name="completionDelegate">Called when the command is complete.</param>
    /// <param name="displayDialog">Whether to show a dialog while executing.</param>
    /// <param name="summaryText">Text to display at the top of the dialog.</param>
    private static void RunCommandsAsync(CommandItem[] commands,
                                         CommandItemCompletionHandler completionDelegate,
                                         bool displayDialog = false, string summaryText = null) {
        var envVars = new Dictionary<string,string>() {
            // Cocoapods requires a UTF-8 terminal, otherwise it displays a warning.
            {"LANG", (System.Environment.GetEnvironmentVariable("LANG") ??
                      "en_US.UTF-8").Split('.')[0] + ".UTF-8"},
            {"PATH", ("/usr/local/bin:" +
                      (System.Environment.GetEnvironmentVariable("PATH") ?? ""))},
        };

        if (displayDialog) {
            var dialog = CommandLineDialog.CreateCommandLineDialog("iOS Resolver");
            dialog.modal = false;
            dialog.autoScrollToBottom = true;
            dialog.bodyText = commands[0].ToString() + "\n";
            dialog.summaryText = summaryText ?? dialog.bodyText;

            int index = 0;
            var handlerContainer = new DelegateContainer<CommandLine.CompletionHandler>();
            handlerContainer.Handler = (CommandLine.Result asyncResult) => {
                var command = commands[index];
                LogCommandLineResult(command.ToString(), asyncResult);

                index = completionDelegate(index, commands, asyncResult, dialog);
                bool endOfCommandList = index < 0 || index >= commands.Length;
                if (endOfCommandList) {
                    // If this is the last command and it has completed successfully, close the
                    // dialog.
                    if (asyncResult.exitCode == 0) {
                        dialog.Close();
                    }
                    lock (commandLineDialogLock) {
                        commandLineDialog = null;
                    }
                } else {
                    command = commands[index];
                    var commandLogLine = command.ToString();
                    dialog.bodyText += "\n" + commandLogLine + "\n\n";
                    Log(commandLogLine, verbose: true);
                    dialog.RunAsync(command.Command, command.Arguments, handlerContainer.Handler,
                                    workingDirectory: command.WorkingDirectory,
                                    envVars: envVars);
                }
            };

            Log(commands[0].ToString(), verbose: true);
            dialog.RunAsync(
                commands[index].Command, commands[index].Arguments,
                handlerContainer.Handler, workingDirectory: commands[index].WorkingDirectory,
                envVars: envVars);
            dialog.Show();
            lock (commandLineDialogLock) {
                commandLineDialog = dialog;
            }
        } else {
            if (!String.IsNullOrEmpty(summaryText)) Log(summaryText);

            int index = 0;
            while (index >= 0 && index < commands.Length) {
                var command = commands[index];
                Log(command.ToString(), verbose: true);
                var result = CommandLine.RunViaShell(
                    command.Command, command.Arguments, workingDirectory: command.WorkingDirectory,
                    envVars: envVars, useShellExecution: PodToolExecutionViaShellEnabled);
                LogCommandLineResult(command.ToString(), result);
                index = completionDelegate(index, commands, result, null);
            }
        }
    }


    /// <summary>
    /// Run a command, optionally displaying a dialog.
    /// </summary>
    /// <param name="command">Command to execute.</param>
    /// <param name="commandArgs">Arguments passed to the command.</param>
    /// <param name="completionDelegate">Called when the command is complete.</param>
    /// <param name="workingDirectory">Where to run the command.</param>
    /// <param name="displayDialog">Whether to show a dialog while executing.</param>
    /// <param name="summaryText">Text to display at the top of the dialog.</param>
    private static void RunCommandAsync(string command, string commandArgs,
                                        CommandLine.CompletionHandler completionDelegate,
                                        string workingDirectory = null,
                                        bool displayDialog = false, string summaryText = null) {
        RunCommandsAsync(
            new [] { new CommandItem { Command = command, Arguments = commandArgs,
                                       WorkingDirectory = workingDirectory } },
            (int commandIndex, CommandItem[] commands, CommandLine.Result result,
             CommandLineDialog dialog) => {
                completionDelegate(result);
                return -1;
            }, displayDialog: displayDialog, summaryText: summaryText);
    }

    /// <summary>
    /// Run a command, optionally displaying a dialog.
    /// </summary>
    /// <param name="command">Command to execute.</param>
    /// <param name="commandArgs">Arguments passed to the command.</param>
    /// <param name="workingDirectory">Where to run the command.</param>
    /// <param name="displayDialog">Whether to show a dialog while executing.</param>
    /// <returns>The CommandLine.Result from running the command.</returns>
    private static CommandLine.Result RunCommand(string command, string commandArgs,
                                                 string workingDirectory = null,
                                                 bool displayDialog = false) {
        CommandLine.Result result = null;
        var complete = new AutoResetEvent(false);
        RunCommandAsync(command, commandArgs,
                        (CommandLine.Result asyncResult) => {
                            result = asyncResult;
                            complete.Set();
                        }, workingDirectory: workingDirectory, displayDialog: displayDialog);
        complete.WaitOne();
        return result;

    }

    /// <summary>
    /// Finds and executes the pod command on the command line, using the
    /// correct environment.
    /// </summary>
    /// <param name="podArgs">Arguments passed to the pod command.</param>
    /// <param name="pathToBuiltProject">The path to the unity project, given
    /// from the unity [PostProcessBuildAttribute()] function.</param>
    /// <param name="completionDelegate">Called when the command is complete.</param>
    /// <param name="displayDialog">Whether to execute in a dialog.</param>
    /// <param name="summaryText">Text to display at the top of the dialog.</param>
    private static void RunPodCommandAsync(
            string podArgs, string pathToBuiltProject,
            CommandLine.CompletionHandler completionDelegate,
            bool displayDialog = false, string summaryText = null) {
        string podCommand = FindPodTool();
        if (String.IsNullOrEmpty(podCommand)) {
            var result = new CommandLine.Result();
            result.exitCode = 1;
            result.stderr = String.Format(
                "'{0}' command not found; unable to generate a usable Xcode project.\n{1}",
                POD_EXECUTABLE, COCOAPOD_INSTALL_INSTRUCTIONS);
            Log(result.stderr, level: LogLevel.Error);
            completionDelegate(result);
        }
        RunCommandAsync(podCommand, podArgs, completionDelegate,
                        workingDirectory: pathToBuiltProject, displayDialog: displayDialog,
                        summaryText: summaryText);
    }

    /// <summary>
    /// Finds and executes the pod command on the command line, using the
    /// correct environment.
    /// </summary>
    /// <param name="podArgs">Arguments passed to the pod command.</param>
    /// <param name="pathToBuiltProject">The path to the unity project, given
    /// from the unity [PostProcessBuildAttribute()] function.</param>
    /// <param name="displayDialog">Whether to execute in a dialog.</param>
    /// <returns>The CommandLine.Result from running the command.</returns>
    private static CommandLine.Result RunPodCommand(
            string podArgs, string pathToBuiltProject, bool displayDialog = false) {
        CommandLine.Result result = null;
        var complete = new AutoResetEvent(false);
        RunPodCommandAsync(podArgs, pathToBuiltProject,
                           (CommandLine.Result asyncResult) => {
                               result = asyncResult;
                               complete.Set();
                           }, displayDialog: displayDialog);
        complete.WaitOne();
        return result;
    }

    /// <summary>
    /// Downloads all of the framework dependencies using pods.
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_INSTALL_PODS)]
    public static void OnPostProcessInstallPods(BuildTarget buildTarget,
                                                string pathToBuiltProject) {
        if (!InjectDependencies() || !PodfileGenerationEnabled) return;
        if (UpdateTargetSdk()) return;
        if (!CocoapodsIntegrationEnabled || !cocoapodsToolsInstallPresent) {
            Log(String.Format(
                "Cocoapod installation is disabled.\n" +
                "If Cocoapods are not installed in your project it will not link.\n\n" +
                "The command '{0} install' must be executed from the {1} directory to generate " +
                "a Xcode workspace that includes the Cocoapods referenced by {2}.\n" +
                "For more information see:\n" +
                "  https://guides.cocoapods.org/using/using-cocoapods.html\n\n",
                POD_EXECUTABLE, pathToBuiltProject, GetPodfilePath(pathToBuiltProject)),
                level: LogLevel.Warning);
            return;
        }

        // Require at least version 1.0.0
        CommandLine.Result result;
        result = RunPodCommand("--version", pathToBuiltProject);
        if (result.exitCode == 0) podsVersion = result.stdout.Trim();

        if (result.exitCode != 0 ||
            (!String.IsNullOrEmpty(podsVersion) && podsVersion[0] == '0')) {
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
        if (!InjectDependencies() || !PodfileGenerationEnabled ||
            !CocoapodsProjectIntegrationEnabled ||  // Early out for Workspace level integration.
            !cocoapodsToolsInstallPresent) {
            return;
        }

        UpdateProjectDeps(buildTarget, pathToBuiltProject);
    }

    // Handles the Xcode project level integration injection of scanned dependencies.
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

        // If Unity can load workspaces, and one has been generated, yet we're still
        // trying to patch the project file, then we have to actually get rid of the workspace
        // and warn the user about it.
        // We'll be taking the dependencies we scraped from the podfile and inserting them in the
        // project with this method anyway, so nothing should be lost.
        string workspacePath = Path.Combine(pathToBuiltProject, "Unity-iPhone.xcworkspace");
        if (UnityCanLoadWorkspace && CocoapodsProjectIntegrationEnabled &&
            Directory.Exists(workspacePath)) {
            Log("Removing the generated workspace to force Unity to directly load the " +
                "xcodeproj.\nSince Unity 5.6, Unity can now load workspace files generated " +
                "from Cocoapods integration, however the IOSResolver Settings are configured " +
                "to use project level integration. It's recommended that you use workspace " +
                "integration instead.\n" +
                "You can manage this setting from: Assets > Play Services Resolver > " +
                "iOS Resolver > Settings, using the Cocoapods Integration drop down menu.",
                level: LogLevel.Warning);
            Directory.Delete(workspacePath, true);
        }

        var pathToBuiltProjectFullPath = Path.GetFullPath(pathToBuiltProject);
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
            Log(String.Format("Inspecting framework {0}", frameworkFullPath), verbose: true);
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
                Log(String.Format("Ignoring framework {0}", frameworkFullPath), verbose: true);
                continue;
            }

            Log(String.Format("Moving framework {0} --> {1}", frameworkFullPath,
                              destFrameworkFullPath), verbose: true);
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
                Log(String.Format("Reading module map {0}", moduleMapPath), verbose: true);
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

            foreach (var resourcesSearchPath in
                     new [] { destFrameworkFullPath,
                              Path.GetDirectoryName(Path.GetDirectoryName(frameworkFullPath)) }) {
                string resourcesFolder = Path.Combine(resourcesSearchPath, "Resources");
                Log(String.Format("Looking for resources folder {0}", resourcesFolder),
                    verbose: true);
                if (Directory.Exists(resourcesFolder)) {
                    Log(String.Format("Found resources {0}", resourcesFolder), verbose: true);
                    string[] resFiles = Directory.GetFiles(resourcesFolder);
                    string[] resFolders =
                        Directory.GetDirectories(resourcesFolder);
                    foreach (var resFile in resFiles) {
                        string destFile = Path.Combine("Resources",
                                                       Path.GetFileName(resFile));
                        File.Copy(resFile, Path.Combine(pathToBuiltProject,
                                                        destFile), true);
                        Log(String.Format("Copying resource file {0} --> {1}", resourcesFolder,
                                          Path.Combine(pathToBuiltProject, destFile)),
                            verbose: true);
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
                        Log(String.Format("Moving resource directory {0} --> {1}", resFolder,
                                          destFolderFullPath), verbose: true);
                        Directory.Move(resFolder, destFolderFullPath);
                        project.AddFileToBuild(
                            target, project.AddFile(
                                destFolder, destFolder,
                                UnityEditor.iOS.Xcode.PBXSourceTree.Source));
                    }
                }
            }
        }

        // Add static libraries to the project.
        foreach (var libraryFullPath in Directory.GetFiles(Path.GetFullPath(podsDir),
                                                           LIBRARY_FILENAME_PREFIX + "*" +
                                                           LIBRARY_FILENAME_EXTENSION,
                                                           SearchOption.AllDirectories)) {
            string libraryRelativePath =
                libraryFullPath.Substring(pathToBuiltProjectFullPath.Length + 1);
            project.AddFileToBuild(
                target, project.AddFile(libraryRelativePath, libraryRelativePath,
                                        UnityEditor.iOS.Xcode.PBXSourceTree.Source));
            // Add the library to the linker command line removing the prefix and extension.
            var libraryBasename = Path.GetFileName(libraryRelativePath);
            linkFlags.Add("-l" + libraryBasename.Substring(
                LIBRARY_FILENAME_PREFIX.Length,
                libraryBasename.Length - (LIBRARY_FILENAME_PREFIX.Length +
                                          LIBRARY_FILENAME_EXTENSION.Length)));
            project.AddBuildProperty(
                target, "LIBRARY_SEARCH_PATHS",
                "$(PROJECT_DIR)/" + Path.GetDirectoryName(libraryRelativePath));
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
            // Some source pods (e.g Protobuf) can include files relative to the Pod root,
            // add include paths relative to the Pod's source files for this use case.
            project.UpdateBuildProperty(
                    new [] { target }, "HEADER_SEARCH_PATHS",
                    new [] { "$(SRCROOT)/" + Path.GetDirectoryName(podPathProjectPath.Value) },
                    new string[] {});
        }

        // Each source pod library target name shares the name of the directory containing
        // the sources.  e.g Pods/foo/stuff/afile.m would generate library "foo".
        // We track the names of the source pod libraries so that we can strip them out of
        // the link options later since we're building with no library as an intermediate.
        var sourcePodLibraries = new HashSet<string>();
        foreach (var podPathProjectPath in podPathToProjectPaths.Values) {
            sourcePodLibraries.Add(podPathProjectPath.Split(new [] { '/', '\\' })[1]);
        }

        // The BuildConfigNames property was introduced in Unity 2017, use it if it's available
        // otherwise fallback to a list of known build configurations.
        IEnumerable<string> configNames = null;
        var configNamesProperty = project.GetType().GetProperty("BuildConfigNames");
        if (configNamesProperty != null) {
            configNames = configNamesProperty.GetValue(null, null) as IEnumerable<string>;
        }
        configNames = configNames ?? BUILD_CONFIG_NAMES;

        // Maps each xcconfig file that should be applied to build configurations by the build
        // config's name prefix.
        const string XCCONFIG_PREFIX = "Pods-" + PROJECT_NAME;
        var xcconfigBasenameToBuildConfigPrefix = new Dictionary<string, string> {
            { XCCONFIG_PREFIX + ".debug.xcconfig", "Debug" },
            { XCCONFIG_PREFIX + ".release.xcconfig", "Release" },
        };
        Regex XCCONFIG_LINE_RE = new Regex(@"\s*(\S+)\s*=\s*(.*)");
        // Add xcconfig files to the project.
        foreach (var filename in
                 FindFilesWithExtensions(podsDir, new HashSet<string>(new [] { ".xcconfig" }))) {
            string buildConfigPrefix = null;
            // If this config shouldn't be applied to the project, skip it.
            if (!xcconfigBasenameToBuildConfigPrefix.TryGetValue(Path.GetFileName(filename),
                                                                 out buildConfigPrefix)) {
                continue;
            }

            // Retrieve the build config GUIDs this xcconfig should be appied to.
            var configGuidsByName = new Dictionary<string, string>();
            foreach (var configName in configNames) {
                if (configName.ToLower().StartsWith(buildConfigPrefix.ToLower())) {
                    var configGuid = project.BuildConfigByName(target, configName);
                    if (!String.IsNullOrEmpty(configGuid)) {
                        configGuidsByName[configGuid] = configName;
                    }
                }
            }
            // If no match configs exist, skip this xcconfig file.
            if (configGuidsByName.Count == 0) continue;

            // Unity's XcodeAPI doesn't expose a way to set the
            // baseConfigurationReference so instead we parse the xcconfig and add the
            // build properties manually.
            // Parser derived from https://pewpewthespells.com/blog/xcconfig_guide.html
            var buildSettings = new Dictionary<string, string>();
            foreach (var line in CommandLine.SplitLines(File.ReadAllText(filename))) {
                var stripped = line.Trim();
                if (stripped.StartsWith("//")) continue;
                // Remove trailing semicolon.
                if (stripped.EndsWith(";")) stripped = stripped.Substring(0, stripped.Length - 1);
                // Ignore empty lines.
                if (stripped.Trim().Length == 0) continue;
                // Display a warning and ignore include statements.
                if (stripped.StartsWith("#include")) {
                    Log(String.Format("{0} contains unsupported #include statement '{1}'",
                                      filename, stripped), level: LogLevel.Warning);
                    continue;
                }
                var match = XCCONFIG_LINE_RE.Match(stripped);
                if (!match.Success) {
                    Log(String.Format("{0} line '{1}' does not contain a variable assignment",
                                      filename, stripped), level: LogLevel.Warning);
                    continue;
                }
                buildSettings[match.Groups[1].Value] = match.Groups[2].Value;
            }

            // Since we're building source Pods within the context of the target project, remove
            // source pod library references from the link options as the intermediate libraries
            // will not exist.  We derived each source pod library name from the directory that
            // contains the source pod's source.
            string linkOptions = null;
            if (buildSettings.TryGetValue("OTHER_LDFLAGS", out linkOptions)) {
                var filteredLinkOptions = new List<string>();
                const string LIBRARY_OPTION = "-l";
                foreach (var option in linkOptions.Split()) {
                    // See https://clang.llvm.org/docs/ClangCommandLineReference.html#linker-flags
                    if (option.StartsWith(LIBRARY_OPTION) &&
                        sourcePodLibraries.Contains(
                            option.Substring(LIBRARY_OPTION.Length).Trim('\"').Trim('\''))) {
                        continue;
                    }
                    filteredLinkOptions.Add(option);
                }
                buildSettings["OTHER_LDFLAGS"] = String.Join(" ", filteredLinkOptions.ToArray());
            }

            // Add the build properties parsed from the xcconfig file to each configuration.
            foreach (var guidAndName in configGuidsByName) {
                foreach (var buildVariableAndValue in buildSettings) {
                    Log(String.Format(
                        "Applying build setting '{0} = {1}' to build config {2} ({3})",
                        buildVariableAndValue.Key, buildVariableAndValue.Value,
                        guidAndName.Value, guidAndName.Key), verbose: true);
                    project.AddBuildPropertyForConfig(guidAndName.Key, buildVariableAndValue.Key,
                                                      buildVariableAndValue.Value);
                }
            }
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

    /// <summary>
    /// Read XML dependencies.
    /// </summary>
    private static void RefreshXmlDependencies() {
        // Remove all pods that were added via XML dependencies.
        var podsToRemove = new List<string>();
        foreach (var podNameAndPod in pods) {
            if (podNameAndPod.Value.fromXmlFile) {
                podsToRemove.Add(podNameAndPod.Key);
            }
        }
        foreach (var podName in podsToRemove) {
            pods.Remove(podName);
        }
        // Read pod specifications from XML dependencies.
        xmlDependencies.ReadAll(IOSXmlDependencies.LogMessage);
    }

    /// <summary>
    /// Called by Unity when all assets have been updated. This
    /// is used to kick off resolving the dependendencies declared.
    /// </summary>
    /// <param name="importedAssets">Imported assets. (unused)</param>
    /// <param name="deletedAssets">Deleted assets. (unused)</param>
    /// <param name="movedAssets">Moved assets. (unused)</param>
    /// <param name="movedFromAssetPaths">Moved from asset paths. (unused)</param>
    private static void OnPostprocessAllAssets(string[] importedAssets,
                                               string[] deletedAssets,
                                               string[] movedAssets,
                                               string[] movedFromAssetPaths) {
        bool reloadXmlDependencies = false;
        var changedAssets = new HashSet<string>();
        foreach (var assetGroup in new [] { importedAssets,  deletedAssets, movedAssets}) {
            changedAssets.UnionWith(assetGroup);
        }
        foreach (var asset in changedAssets) {
            foreach (var regexp in xmlDependencies.fileRegularExpressions) {
                if (regexp.Match(asset).Success) {
                    reloadXmlDependencies = true;
                }
            }
        }
        if (reloadXmlDependencies) {
            RefreshXmlDependencies();
        }
    }
}

}  // namespace Google

#endif  // UNITY_IOS

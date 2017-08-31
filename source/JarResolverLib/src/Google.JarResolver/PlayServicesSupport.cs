// <copyright file="PlayServicesSupport.cs" company="Google Inc.">
// Copyright (C) 2014 Google Inc. All Rights Reserved.
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

namespace Google.JarResolver
{
    using System;
    using System.Diagnostics;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Xml;

    /// <summary>
    /// Play services support is a helper class for managing the Google play services
    /// and Android support libraries in a Unity project.  This is done by using
    /// the Maven repositories that are part of the Android SDK.  This class
    /// implements the logic of version checking, transitive dependencies, and
    /// updating a a directory to make sure that there is only one version of
    /// a dependency present.
    /// </summary>
    public class PlayServicesSupport
    {
        /// <summary>
        /// The name of the client.
        /// </summary>
        private string clientName;

        /// <summary>
        /// The path to the Android SDK.
        /// </summary>
        private static string userSdkPath;

        /// <summary>
        /// Log severity.
        /// </summary>
        public enum LogLevel {
            Info,
            Warning,
            Error,
        };

        /// <summary>
        /// Delegate used to specify a log method for this class.  If provided this class
        /// will log messages via this delegate.
        /// </summary>
        public delegate void LogMessage(string message);

        /// <summary>
        /// Delegate used to specify a log method for this class.  If provided this class
        /// will log messages via this delegate.
        /// </summary>
        /// <param name="message">Message to log.</param>
        /// <param name="level">Severity of the log message.</param>
        public delegate void LogMessageWithLevel(string message, LogLevel level);

        /// <summary>
        /// Log function delegate.  If set, this class will write log messages via this method.
        /// </summary>
        internal static LogMessageWithLevel logger;

        /// <summary>
        /// The repository paths.
        /// </summary>
        private List<string> repositoryPaths = new List<string>();

        /// <summary>
        /// Get the set of repository paths.
        /// </summary>
        internal List<string> RepositoryPaths { get { return new List<string>(repositoryPaths); } }

        /// <summary>
        /// The client dependencies map.  This is a proper subset of dependencyMap.
        /// </summary>
        private Dictionary<string, Dependency> clientDependenciesMap =
            new Dictionary<string, Dependency>();

        /// <summary>
        /// String that is expanded with the path of the Android SDK.
        /// </summary>
        internal const string SdkVariable = "$SDK";

        /// <summary>
        /// Extension of Unity metadata files.
        /// </summary>
        internal const string MetaExtension = ".meta";

        /// <summary>
        /// Error message displayed / logged when the Android SDK path isn't configured.
        /// </summary>
        public const string AndroidSdkConfigurationError =
            ("Android SDK path not set.  " +
             "Set the Android SDK property using the Unity " +
             "\"Edit > Preferences > External Tools\" menu option on Windows " +
             "or the \"Unity > Preferences > External Tools\" menu option on OSX. " +
             "Alternatively, set the ANDROID_HOME environment variable");

        /// <summary>
        /// Delegate used to prompt or notify the developer that an existing
        /// dependency file is being overwritten.
        /// </summary>
        /// <param name="oldDep">The dependency being replaced</param>
        /// <param name="newDep">The dependency to use</param>
        /// <returns>If <c>true</c> the replacement is performed.</returns>
        public delegate bool OverwriteConfirmation(Dependency oldDep, Dependency newDep);

        /// <summary>
        /// Delegate used to determine whether an AAR should be exploded.
        /// </summary>
        /// <param name="aarPath">Path to the AAR file to examine.</param>
        /// <returns>True if the AAR should be exploded, false otherwise.</returns>
        public delegate bool ExplodeAar(string aarPath);

        /// <summary>
        /// Whether the editor was launched in batch mode.
        /// </summary>
        internal static bool InBatchMode {
            get {
#if UNITY_EDITOR
                return System.Environment.CommandLine.Contains("-batchmode");
#else
                return true;
#endif  // UNITY_EDITOR
            }
        }

        // Map of common dependencies to Android SDK packages.
        private static List<KeyValuePair<Regex, string>> CommonPackages =
            new List<KeyValuePair<Regex, string>> {
                new KeyValuePair<Regex, string>(
                    new Regex("^com\\.android\\.support:support-.*"),
                    "extra-android-m2repository"),
                new KeyValuePair<Regex, string>(
                    new Regex("^com\\.google\\.android\\.gms:.*"),
                    "extra-google-m2repository"),
                new KeyValuePair<Regex, string>(
                    new Regex("^com\\.google\\.firebase:firebase-.*"),
                    "extra-google-m2repository")
        };

        private static string SDKInternal
        {
            get
            {
                var sdkPath = userSdkPath;
#if UNITY_EDITOR
                if (String.IsNullOrEmpty(sdkPath)) {
                    sdkPath = UnityEditor.EditorPrefs.GetString("AndroidSdkRoot");
                }
#endif  // UNITY_EDITOR
                if (string.IsNullOrEmpty(sdkPath)) {
                    sdkPath = System.Environment.GetEnvironmentVariable("ANDROID_HOME");
                }
                return sdkPath;
            }
        }

        const string PlayServicesVersionValidatorEnabledPreferenceKey =
            "PlayServicesSupport.PlayServicesVersionCheck";

        /// <summary>
        /// Whether the Play Services package version validator is enabled.
        /// </summary>
        public bool PlayServicesVersionValidatorEnabled {
            set {
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetBool(PlayServicesVersionValidatorEnabledPreferenceKey,
                                                value);
#endif  // UNITY_EDITOR
            }
            get {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(
                    PlayServicesVersionValidatorEnabledPreferenceKey, true);
#else
                return true;
#endif  // UNITY_EDITOR
            }
        }

        /// <summary>
        /// Gets the Android SDK.  If it is not set, the environment
        /// variable ANDROID_HOME is used.
        /// </summary>
        /// <value>The SD.</value>
        public string SDK { get { return PlayServicesSupport.SDKInternal; } }

        /// <summary>
        /// Whether verbose logging is enabled.
        /// </summary>
        internal static bool verboseLogging = false;

        // Set of currently created instances per client.
        public static Dictionary<string, PlayServicesSupport> instances =
            new Dictionary<string, PlayServicesSupport>();

        /// <summary>
        /// Creates an instance of PlayServicesSupport.  This instance is
        /// used to add dependencies for the calling client and invoke the resolution.
        /// </summary>
        /// <returns>The instance.</returns>
        /// <param name="clientName">Client name.  Must be a valid filename.
        /// This is used to uniquely identify
        /// the calling client so that dependencies can be associated with a specific
        /// client to help in resetting dependencies.</param>
        /// <param name="sdkPath">Sdk path for Android SDK.</param>
        /// <param name="settingsDirectory">This parameter is obsolete.</param>
        /// <param name="logger">Delegate used to write messages to the log.</param>
        /// <param name="logMessageWithLevel">Delegate used to write messages to the log.  If
        /// this is specified "logger" is ignored.</param>
        public static PlayServicesSupport CreateInstance(
                string clientName, string sdkPath, string settingsDirectory,
                LogMessage logger = null, LogMessageWithLevel logMessageWithLevel = null) {
            return CreateInstance(clientName, sdkPath, null, settingsDirectory, logger: logger,
                                  logMessageWithLevel: logMessageWithLevel);
        }

        /// <summary>
        /// Creates an instance of PlayServicesSupport.  This instance is
        /// used to add dependencies for the calling client and invoke the resolution.
        /// </summary>
        /// <returns>The instance.</returns>
        /// <param name="clientName">Client name.  Must be a valid filename.
        /// This is used to uniquely identify
        /// the calling client so that dependencies can be associated with a specific
        /// client to help in resetting dependencies.</param>
        /// <param name="sdkPath">Sdk path for Android SDK.</param>
        /// <param name="additionalRepositories">Array of additional repository paths. can be
        /// null</param>
        /// <param name="settingsDirectory">This parameter is obsolete.</param>
        /// <param name="logger">Delegate used to write messages to the log.</param>
        /// <param name="logMessageWithLevel">Delegate used to write messages to the log.  If
        /// this is specified "logger" is ignored.</param>
        internal static PlayServicesSupport CreateInstance(
                string clientName, string sdkPath, string[] additionalRepositories,
                string settingsDirectory, LogMessage logger = null,
                LogMessageWithLevel logMessageWithLevel = null)
        {
            PlayServicesSupport instance = new PlayServicesSupport();
            LogMessageWithLevel legacyLogger = (string message, LogLevel level) => {
                if (logger != null) logger(message);
            };
            PlayServicesSupport.logger =
                PlayServicesSupport.logger ?? (logMessageWithLevel ??
                                               (logger != null ? legacyLogger : null));
            // Only set the SDK path if it differs to what is configured in the editor or
            // via an environment variable.  The SDK path can be changed by the user before
            // this module is reloaded.
            if (!String.IsNullOrEmpty(sdkPath) && sdkPath != PlayServicesSupport.SDKInternal) {
                PlayServicesSupport.userSdkPath = sdkPath;
            }
            string badchars = new string(Path.GetInvalidFileNameChars());

            foreach (char ch in clientName)
            {
                if (badchars.IndexOf(ch) >= 0)
                {
                    throw new Exception("Invalid clientName: " + clientName);
                }
            }

            instance.clientName = clientName;

            var repoPaths = new List<string>();
            repoPaths.AddRange(additionalRepositories ?? new string[] {});
            // Add the standard repo paths from the Android SDK
            string sdkExtrasDir = Path.Combine(SdkVariable, "extras");
            repoPaths.AddRange(
                new string [] {
                    Path.Combine(sdkExtrasDir, Path.Combine("android","m2repository")),
                    Path.Combine(sdkExtrasDir, Path.Combine("google","m2repository"))
                });
            instance.repositoryPaths = UniqueList(repoPaths);
            instances[instance.clientName] = instance;
            return instance;
        }

        /// <summary>
        /// Log a message to the currently set logger.
        /// </summary>
        /// <param name="message">Message to log.</param>
        /// <param name="level">Severity of the log message.</param>
        /// <param name="verbose">Whether the message should only be displayed with verbose
        /// logging enabled.</param>
        internal static void Log(string message, LogLevel level = LogLevel.Info,
                                 bool verbose = false) {
            if (logger != null && (!verbose || verboseLogging || InBatchMode)) {
                logger(message, level);
            }
        }

        /// <summary>
        /// Delete a file or directory if it exists.
        /// </summary>
        /// <param name="path">Path to the file or directory to delete if it exists.</param>
        public static bool DeleteExistingFileOrDirectory(string path,
                                                         bool includeMetaFiles = false)
        {
            bool deletedFileOrDirectory = false;
            if (includeMetaFiles && !path.EndsWith(MetaExtension))
            {
                deletedFileOrDirectory = DeleteExistingFileOrDirectory(path + MetaExtension);
            }
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                di.Attributes &= ~FileAttributes.ReadOnly;
                foreach (string file in Directory.GetFileSystemEntries(path))
                {
                    DeleteExistingFileOrDirectory(file, includeMetaFiles: includeMetaFiles);
                }
                Directory.Delete(path);
                deletedFileOrDirectory = true;
            }
            else if (File.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                File.Delete(path);
                deletedFileOrDirectory = true;
            }
            return deletedFileOrDirectory;
        }

        /// <summary>
        /// Copy the contents of a directory to another directory.
        /// </summary>
        /// <param name="sourceDir">Path to copy the contents from.</param>
        /// <param name="targetDir">Path to copy to.</param>
        public static void CopyDirectory(string sourceDir, string targetDir)
        {
            Func<string, string> sourceToTargetPath = (path) => {
                return Path.Combine(targetDir, path.Substring(sourceDir.Length + 1));
            };
            foreach (string sourcePath in
                     Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(sourceToTargetPath(sourcePath));
            }
            foreach (string sourcePath in
                     Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (!sourcePath.EndsWith(PlayServicesSupport.MetaExtension))
                {
                    File.Copy(sourcePath, sourceToTargetPath(sourcePath));
                }
            }
        }

        /// <summary>
        /// Lookup common package IDs for a dependency.
        /// </summary>
        private static Dependency AddCommonPackageIds(Dependency dep) {
            if (dep.PackageIds != null) return dep;

            var packageNames = new List<string>();
            string[] packageIds = null;
            foreach (var kv in CommonPackages) {
                var match = kv.Key.Match(dep.Key);
                if (match.Success) {
                    packageNames.Add(kv.Value);
                    break;
                }
            }
            if (packageNames.Count > 0) packageIds = packageNames.ToArray();
            return new Dependency(dep.Group, dep.Artifact, dep.Version, packageIds: packageIds,
                                  repositories: dep.Repositories);
        }

        /// <summary>
        /// Adds a dependency to the project.
        /// </summary>
        /// <remarks>This method should be called for
        /// each library that is required.  Transitive dependencies are processed
        /// so only directly referenced libraries need to be added.
        /// <para>
        /// The version string can be contain a trailing + to indicate " or greater".
        /// Trailing 0s are implied.  For example:
        /// </para>
        /// <para>  1.0 means only version 1.0, but
        /// also matches 1.0.0.
        /// </para>
        /// <para>1.2.3+ means version 1.2.3 or 1.2.4, etc. but not 1.3.
        /// </para>
        /// <para>
        /// 0+ means any version.
        /// </para>
        /// <para>
        /// LATEST means the only the latest version.
        /// </para>
        /// </remarks>
        /// <param name="group">Group - the Group Id of the artiface</param>
        /// <param name="artifact">Artifact - Artifact Id</param>
        /// <param name="version">Version - the version constraint</param>
        /// <param name="packageIds">Optional list of Android SDK package identifiers.</param>
        /// <param name="repositories">List of additional repository directories to search for
        /// this artifact.</param>
        /// <param name="createdBy">Human readable string that describes where this dependency
        /// originated.</param>
        public void DependOn(string group, string artifact, string version,
                             string[] packageIds = null, string[] repositories = null,
                             string createdBy = null) {
            Log("DependOn - group: " + group +
                " artifact: " + artifact +
                " version: " + version +
                " packageIds: " +
                (packageIds != null ? String.Join(", ", packageIds) : "null") +
                " repositories: " +
                (repositories != null ? String.Join(", ", repositories) :
                 "null"),
                verbose: true);
            repositories = repositories ?? new string[] {};
            var depRepoList = new List<string>(repositories);
            depRepoList.AddRange(repositoryPaths);
            var dep = AddCommonPackageIds(new Dependency(
                group, artifact, version, packageIds: packageIds,
                repositories: UniqueList(depRepoList).ToArray(),
                createdBy: createdBy));
            clientDependenciesMap[dep.Key] = dep;
        }

        /// <summary>
        /// Get the current list of dependencies for all clients.
        /// </summary>
        /// <returns>Dictionary of Dependency instances indexed by Dependency.Key.</returns>
        public static Dictionary<string, Dependency> GetAllDependencies() {
            var allDependencies = new Dictionary<string, Dependency>();
            foreach (var instance in instances.Values) {
                foreach (var dependencyByKey in instance.clientDependenciesMap) {
                    allDependencies[dependencyByKey.Key] = new Dependency(dependencyByKey.Value);
                }
            }
            return allDependencies;
        }

        /// <summary>
        /// Clears the dependencies for this client.
        /// </summary>
        public void ClearDependencies()
        {
            clientDependenciesMap = new Dictionary<string, Dependency>();
        }

        /// <summary>
        /// Expand the set of transitive dependencies.
        /// </summary>
        /// <param name="dependencies">Dependencies to walk for the set of transitive
        /// dependencies.</param>
        /// <param name="repoPaths">Set of additional repo paths to search for the
        /// dependencies.</param>
        /// <param name="logErrors">Whether to report errors for missing dependencies.</param>
        /// <returns>Dictionary of Dependency instances indexed by Dependency.Key.</returns>
        public static Dictionary<string, Dependency> GetTransitiveDependencies(
                Dictionary<string, Dependency> dependencies, List<string> repoPaths = null,
                bool logErrors = true) {
            // Copy the set of dependencies.
            var transitiveDependencies = new Dictionary<string, Dependency>(dependencies);
            // Transitive dependencies that have not been queried for packages they're dependent
            // upon.
            var pendingTransitiveDependencies =
                new Dictionary<string, Dependency>(transitiveDependencies);
            // Set of keys of dependencies that have already been processed.
            var processedDependencies = new HashSet<string>();
            // Expand set of transitive dependencies into the dictionary of dependencies.
            while (pendingTransitiveDependencies.Count > 0) {
                var dependenciesToEvaluate = new Dictionary<string, Dependency>(
                    pendingTransitiveDependencies);
                foreach (var dependencyItem in dependenciesToEvaluate) {
                    pendingTransitiveDependencies.Remove(dependencyItem.Key);
                    processedDependencies.Add(dependencyItem.Key);

                    var combinedRepos =
                        new List<string>(dependencyItem.Value.Repositories ?? new string[] {});
                    if (repoPaths != null) combinedRepos.AddRange(repoPaths);
                    combinedRepos = UniqueList<string>(combinedRepos);

                    try {
                        foreach (var transitiveDependency in
                                 GetDependencies(dependencyItem.Value, combinedRepos,
                                                 logError: logErrors)) {
                            if (!processedDependencies.Contains(transitiveDependency.Key)) {
                                transitiveDependencies[transitiveDependency.Key] =
                                    transitiveDependency;
                                pendingTransitiveDependencies[transitiveDependency.Key] =
                                    transitiveDependency;
                            }
                        }
                    } catch (ResolutionException exception) {
                        foreach (var dep in exception.MissingDependencies) {
                            // This dependency is missing so stop traversal at this item.
                            transitiveDependencies[dep.Key] = AddCommonPackageIds(dep);
                            // Search the set of registered dependencies to see whether the user
                            // specified this explicitly.  This may allow the user to determine
                            // which packages to download in the Android SDK manager.
                            foreach (var instance in instances.Values) {
                                Dependency dependOnDependency;
                                if (instance.clientDependenciesMap.TryGetValue(
                                         dep.Key, out dependOnDependency)) {
                                    if (dependOnDependency.PackageIds != null) {
                                        transitiveDependencies[dep.Key] = dependOnDependency;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return transitiveDependencies;
        }

        /// <summary>
        /// Get the current set of dependencies referenced by the project.
        /// </summary>
        /// <param name="dependencies">Dependencies to search for in the specified
        /// directory.</param>
        /// <param name="destDirectory">Directory where dependencies are located in the
        /// project.</param>
        /// <param name="explodeAar">Delegate that determines whether a dependency should be
        /// exploded.  If a dependency is currently exploded but shouldn't be according to this
        /// delegate, the dependency is deleted.</param>
        /// <param name="repoPaths">Set of additional repo paths to search for the
        /// dependencies.</param>
        /// <returns>Dictionary indexed by Dependency.Key where each item is a tuple of
        /// Dependency instance and the path to the dependency in destDirectory.</returns>
        public static Dictionary<string, KeyValuePair<Dependency, string>> GetCurrentDependencies(
                Dictionary<string, Dependency> dependencies, string destDirectory,
                ExplodeAar explodeAar = null, List<string> repoPaths = null) {
            var currentDependencies = new Dictionary<string, KeyValuePair<Dependency, string>>();
            if (dependencies.Count == 0) return currentDependencies;

            // Get the transitive set of dependencies.
            var transitiveDependencies = GetTransitiveDependencies(dependencies,
                                                                   repoPaths: repoPaths,
                                                                   logErrors: false);
            // TODO(smiles): Need a callback that queries Unity's asset DB rather than touching
            // the filesystem here.
            string[] filesInDestDir = Directory.GetFileSystemEntries(destDirectory);
            foreach (var path in filesInDestDir)
            {
                // Ignore Unity's .meta files.
                if (path.EndsWith(MetaExtension)) continue;

                string filename = Path.GetFileName(path);
                // Strip the package extension from filenames.  Directories generated from
                // unpacked AARs do not have extensions.
                bool pathIsDirectory = Directory.Exists(path);
                string filenameWithoutExtension = pathIsDirectory ? filename :
                    Path.GetFileNameWithoutExtension(filename);

                foreach (var dep in transitiveDependencies.Values)
                {
                    // Get the set of artifacts matching artifact-*.
                    // The "-" is important to distinguish art-1.0.0 from artifact-1.0.0
                    // (or base- and basement-).
                    var match = System.Text.RegularExpressions.Regex.Match(
                        filenameWithoutExtension, String.Format("^{0}-.*", dep.Artifact));
                    if (match.Success)
                    {
                        Log(String.Format("Found potential Android package {0} {1}",
                                          pathIsDirectory ? "directory " : "file",
                                          filenameWithoutExtension), verbose: true);

                        // Extract the version from the filename.
                        // dep.Artifact is the name of the package (prefix)
                        string artifactVersion = ExtractVersionFromFileName(
                            filenameWithoutExtension.Substring(dep.Artifact.Length + 1)
                        );

                        if (artifactVersion != null)
                        {
                            bool reportDependency = true;
                            // If the AAR is exploded and it should not be, delete it and do not
                            // report this dependency.
                            if (pathIsDirectory && explodeAar != null)
                            {
                                string aarFile = dep.BestVersionArtifact;
                                if (aarFile != null && !explodeAar(aarFile))
                                {
                                    Log(String.Format(
                                        "Deleting exploded AAR ({0}) that should not be exploded.",
                                        aarFile), verbose: true);
                                    DeleteExistingFileOrDirectory(path,
                                                                  includeMetaFiles: true);
                                    reportDependency = false;
                                }
                            }
                            if (reportDependency)
                            {
                                Dependency currentDep = new Dependency(
                                    dep.Group, dep.Artifact, artifactVersion,
                                    packageIds: dep.PackageIds, repositories: dep.Repositories);
                                // Add the artifact version so BestVersion == Version.
                                currentDep.AddVersion(currentDep.Version);
                                currentDependencies[currentDep.Key] =
                                    new KeyValuePair<Dependency, string>(currentDep, path);
                            }
                            break;
                        }
                    }
                }
            }
            return currentDependencies;
        }

        /// <summary>
        /// Determine whether two lists of dependencies match.
        /// </summary>
        /// <param name="deps1">List of dependencies to compare, these should have concrete
        /// versions.</param>
        /// <param name="deps2">List of dependencies to compare.</param>
        /// <returns>true if both enumerables match, false otherwise.</returns>
        private static bool DependenciesEqual(List<Dependency> deps1,
                                              List<Dependency> deps2)
        {
            if (deps1.Count != deps2.Count) return false;
            // Dictionaries of dependencies indexed by versionless key.
            var set1 = new Dictionary<string, Dependency>();
            var set2 = new Dictionary<string, Dependency>();
            foreach (var dep in deps1) set1[dep.VersionlessKey] = dep;
            foreach (var dep in deps2) set2[dep.VersionlessKey] = dep;

            // Determine whether dependencies in each set are compatible.
            foreach (var kv in set1) {
                Dependency dep2;
                if (!set2.TryGetValue(kv.Key, out dep2)) return false;
                if (!dep2.IsAcceptableVersion(kv.Value.Version)) return false;
            }
            return true;
        }


        /// <summary>
        /// Find the set of artifacts in the project missing from the set of specified
        /// dependencies.
        /// </summary>
        /// <param name="destinationDirectory">Directory where dependencies are located in the
        /// project.  If this parameter is null, a dictionary of required dependencies is
        /// always returned.</param>
        /// <param name="dependencyPaths">If not null, the dictionary is populated with
        /// dependency paths in the destDirectory indexed by the Dependency.Key of each
        /// dependency.</param>
        /// <param name="explodeAar">Delegate that determines whether a dependency should be
        /// exploded.  If a dependency is currently exploded but shouldn't be according to this
        /// delegate, the dependency is deleted.</param>
        /// <returns>null if all dependencies are present, dictionary of all required dependencies
        /// otherwise.</returns>
        public Dictionary<string, Dependency> FindMissingDependencies(
                string destinationDirectory, ExplodeAar explodeAar = null) {
            Dictionary<string, string> paths;
            return FindMissingDependencyPaths(destinationDirectory, out paths,
                                              explodeAar: explodeAar);
        }

        /// <summary>
        /// Find the set of artifacts in the project missing from the set of specified
        /// dependencies.
        /// </summary>
        /// <param name="destinationDirectory">Directory where dependencies are located in the
        /// project.  If this parameter is null, a dictionary of required dependencies is
        /// always returned.</param>
        /// <param name="dependencyPaths">If not null, the dictionary is populated with
        /// dependency paths in the destDirectory indexed by the Dependency.Key of each
        /// dependency.</param>
        /// <param name="explodeAar">Delegate that determines whether a dependency should be
        /// exploded.  If a dependency is currently exploded but shouldn't be according to this
        /// delegate, the dependency is deleted.</param>
        /// <returns>null if all dependencies are present, dictionary of all required dependencies
        /// otherwise.</returns>
        internal Dictionary<string, Dependency> FindMissingDependencyPaths(
                string destinationDirectory, out Dictionary<string, string> dependencyPaths,
                ExplodeAar explodeAar = null) {
            Dictionary<string, Dependency> dependencyMap =
                GetTransitiveDependencies(LoadDependencies(true, keepMissing: true,
                                                           findCandidates: true),
                                          repoPaths: repositoryPaths, logErrors: false);
            dependencyPaths = null;
            // If a destination directory was specified, determine whether the dependencies
            // referenced by dependencyMap differ to what is present in the project.  If they
            // are the same, we can skip this entire method.
            if (destinationDirectory != null) {
                var currentDependencies = GetCurrentDependencies(
                    dependencyMap, destinationDirectory, explodeAar: explodeAar,
                    repoPaths: repositoryPaths);
                // Copy the destination path of each dependency into dependencyPaths.
                dependencyPaths = new Dictionary<string, string>();
                foreach (var currentDependency in currentDependencies) {
                    dependencyPaths[currentDependency.Key] = currentDependency.Value.Value;
                }
                var currentDependenciesList = new List<Dependency>();
                foreach (var kv in currentDependencies.Values) currentDependenciesList.Add(kv.Key);
                if (DependenciesEqual(currentDependenciesList,
                                      new List<Dependency>(dependencyMap.Values))) {
                    Log(String.Format(
                        "All dependencies up to date.\n\n" +
                        "Required:\n" +
                        "{0}", String.Join("\n",
                                           (new List<string>(dependencyMap.Keys)).ToArray())),
                        verbose: true);
                    return null;
                }
                var currentDependenciesSortedByKey = new List<string>(currentDependencies.Keys);
                currentDependenciesSortedByKey.Sort();
                var requiredDependenciesSortedByKey = new List<string>(dependencyMap.Keys);
                requiredDependenciesSortedByKey.Sort();
                Log(String.Format(
                    "Current Android packages mismatch required packages:\n\n" +
                    "Current:\n" +
                    "{0}\n\n" +
                    "Required:\n" +
                    "{1}\n",
                    String.Join("\n", currentDependenciesSortedByKey.ToArray()),
                    String.Join("\n", requiredDependenciesSortedByKey.ToArray())),
                    verbose: true);
            }
            return dependencyMap;
        }

        /// <summary>
        /// Some groups of dependencies are released in lock step.  This forces all
        /// dependencies within the specified set of groups to a common version.
        /// </summary>
        /// <param name="dependenciesToProcess">Set of dependencies to process indexed by
        /// Dependency.Key.</param>
        /// <param name="versionLockedGroupIds">Groups which contain packages that should all
        /// use the same version.</param>
        /// <param name="packageSetName">String used to summarize the set of matching packages in
        /// log messages.</param>
        /// <param name="artifactFilter">Regular expression which excludes packages by their
        /// the artifact name within the specified group.</param>
        /// <returns>List of dependencies where all packages that match versionLockedGroupIds
        /// are configured with the same version.</return>
        public List<Dependency> ProcessVersionLockedDependencies(
                IEnumerable<Dependency> dependenciesToProcess,
                HashSet<string> versionLockedGroupIds,
                string packageSetName, Regex artifactNameFilter = null) {
            var versionLockedPackages = new Dictionary<string, Dependency>();
            // Versions of each version locked package.
            var versionLockedPackageVersions = new HashSet<string>();
            foreach (var dependency in dependenciesToProcess) {
                if (!versionLockedGroupIds.Contains(dependency.Group) ||
                    (artifactNameFilter != null &&
                     artifactNameFilter.Match(dependency.Artifact).Success)) {
                    continue;
                }
                var dependencyWithConstraintsApplied = new Dependency(dependency);
                // If this results in no available versions for the specified set of
                // constraints, ignore this version.  The final resolution step will report
                // an error for the case of no matching versions.
                if (dependencyWithConstraintsApplied.RefineVersionRange(dependency)) {
                    versionLockedPackageVersions.UnionWith(new HashSet<string>(
                        dependencyWithConstraintsApplied.PossibleVersions));
                    versionLockedPackages[dependencyWithConstraintsApplied.Key] =
                        dependencyWithConstraintsApplied;
                }
            }
            // If no version locked packages are in the set, return the dependencies unmodified.
            if (versionLockedPackages.Count == 0) {
                return new List<Dependency>(dependenciesToProcess);
            }

            // Calculate the set of versions supported by all packages.
            var commonlySupportedVersions = new HashSet<string>(versionLockedPackageVersions);
            foreach (var dependency in versionLockedPackages.Values) {
                var supportedVersions = new HashSet<string>(dependency.PossibleVersions);
                commonlySupportedVersions.IntersectWith(supportedVersions);
            }

            var commonlySupportedVersionsFound = commonlySupportedVersions.Count > 0;
            var sortedVersions = new List<string>(
                 commonlySupportedVersionsFound ?
                 commonlySupportedVersions : versionLockedPackageVersions);
            sortedVersions.Sort(Dependency.versionComparer);
            var mostRecentSupportedVersion = sortedVersions[0];

            // Determine whether all dependencies support the most recent common version.
            bool allSupportCommonVersion = true;
            foreach (var dependency in (new List<Dependency>(versionLockedPackages.Values))) {
                var dependencyToCheckForMostRecentVersion = new Dependency(dependency);
                dependencyToCheckForMostRecentVersion.Version = mostRecentSupportedVersion;
                if (!dependencyToCheckForMostRecentVersion.RefineVersionRange(
                        dependencyToCheckForMostRecentVersion)) {
                    allSupportCommonVersion = false;
                    break;
                }
            }
            // If all dependencies support a common version, return the dependencies unmodified.
            if (allSupportCommonVersion) return new List<Dependency>(dependenciesToProcess);

            var dependencyKeyAndCreationLocations = new List<string>();
            foreach (var dependency in versionLockedPackages) {
                dependencyKeyAndCreationLocations.Add(String.Format(
                    "--- {0}\n{1}\n", dependency.Key, dependency.Value.CreatedBy));
            }

            Log(String.Format(
                    "{0} packages found with incompatible versions.\n" +
                    "\n" +
                    "All packages in {0} must be at the same version.\n" +
                    "Attempting to resolve the problem by using the most " +
                    "recent{1}version ({2}) of all packages.\n" +
                    "\n" +
                    "Found the following package references:\n" +
                    "{3}\n" +
                    "\n" +
                    "This functionality can be disabled using the {4} editor preference.\n\n" +
                    "Packages references are:\n" +
                    "{5}",
                    packageSetName, commonlySupportedVersionsFound ? " *compatible* " : " ",
                    mostRecentSupportedVersion,
                    String.Join("\n", new List<string>(versionLockedPackages.Keys).ToArray()),
                    PlayServicesVersionValidatorEnabledPreferenceKey,
                    String.Join("\n", dependencyKeyAndCreationLocations.ToArray())),
                level: commonlySupportedVersionsFound ? LogLevel.Info : LogLevel.Warning,
                verbose: commonlySupportedVersionsFound);

            // Set all dependencies to the version.
            var fixedDependencies = new Dictionary<string, Dependency>();
            foreach (var dependency in dependenciesToProcess) {
                if (versionLockedPackages.ContainsKey(dependency.Key)) {
                    dependency.Version = mostRecentSupportedVersion;
                }
                fixedDependencies[dependency.Key] = dependency;
            }
            return new List<Dependency>(LoadDependencies(fixedDependencies, repositoryPaths,
                                                         keepMissing: true).Values);
        }

        /// <summary>
        /// Performs the resolution process.  This determines the versions of the
        /// dependencies that should be used.  Transitive dependencies are also
        /// processed.
        /// </summary>
        /// <returns>The dependencies.  The key is the "versionless" key of the dependency.
        /// </returns>
        /// <param name="useLatest">If set to <c>true</c> use latest version of a conflicting
        /// dependency.
        /// if <c>false</c> a ResolutionException is thrown in the case of a conflict.</param>
        /// <param name="dependencyMap">Set of dependencies used to perform the resolution process
        /// indexed by Dependency.Key.  If this argument is null, this method returns an empty
        /// dictionary of candidates.</param>
        /// <param name="destDirectory">Directory dependencies will be copied to using
        /// CopyDependencies().</param>
        /// <param name="explodeAar">Delegate that determines whether a dependency should be
        /// exploded.  If a dependency is currently exploded but shouldn't be according to this
        /// delegate, the dependency is deleted.</param>
        internal Dictionary<string, Dependency> ResolveDependencies(
                bool useLatest, Dictionary<string, Dependency> dependencyMap,
                string destDirectory, ExplodeAar explodeAar) {
            Dictionary<string, Dependency> candidates = new Dictionary<string, Dependency>();
            if (dependencyMap == null) return candidates;

            // Set of each versioned dependencies for each version-less dependency key.
            // e.g if foo depends upon bar, map[bar] = {foo}.
            var reverseDependencyTree = new Dictionary<string, HashSet<string>>();
            var warnings = new HashSet<string>();

            // All dependencies are added to the "unresolved" list.
            var unresolved = new List<Dependency>();
            foreach (var dependency in dependencyMap.Values) {
                // Since the resolution process may need to be restarted with a different
                // set of baseline packages during resolution, we copy each dependency so the
                // starting state can be restored.
                var candidateDependency = FindCandidate(dependency);
                if (candidateDependency != null) {
                    unresolved.Add(new Dependency(candidateDependency));
                }
            }

            // To speed up the process of dependency resolution - and workaround the deficiencies
            // in the resolution logic - we can apply some things that we know about some
            // dependencies:
            // * com.google.android.gms.* packages are released a single set that typically are
            //   not compatible between revisions.  e.g If a user depends upon
            //   play-services-games:9.8.0 they'll also require play-services-base:9.8.0 etc.
            // * com.google.firebase.* packages are versioned in the same way as
            //   com.google.android.gms.* with dependencies upon the gms (Play Services)
            //   components.
            //
            // Given this knowledge, find all top level incompatible dependencies and select the
            // most recent compatible versions for all matching packages.
            if (PlayServicesVersionValidatorEnabled) {
                unresolved = new List<Dependency>(ProcessVersionLockedDependencies(
                    unresolved, new HashSet<string> { "com.google.android.gms",
                                                      "com.google.firebase" },
                    "Google Play Services", artifactNameFilter: new Regex("-unity$")));
            }

            // Copy unresolved dependencies into the map to be resolved.
            dependencyMap = new Dictionary<string, Dependency>();
            foreach (var dependency in unresolved) dependencyMap[dependency.Key] = dependency;

            do
            {
                Dictionary<string, Dependency> nextUnresolved =
                    new Dictionary<string, Dependency>();

                foreach (Dependency dep in unresolved)
                {
                    var currentDep = dep;
                    // Whether the dependency has been resolved and therefore should be removed
                    // from the unresolved list.
                    bool removeDep = true;

                    // check for existing candidate
                    Dependency candidate;
                    Dependency newCandidate;
                    if (candidates.TryGetValue(currentDep.VersionlessKey, out candidate))
                    {
                        if (currentDep.IsAcceptableVersion(candidate.BestVersion))
                        {
                            removeDep = true;

                            // save the most restrictive dependency in the
                            //  candidate
                            if (currentDep.IsNewer(candidate))
                            {
                                candidates[currentDep.VersionlessKey] = currentDep;
                            }
                        }
                        else
                        {
                            // in general, we need to iterate
                            removeDep = false;

                            // refine one or both dependencies if they are
                            // non-concrete.
                            bool possible = false;
                            if (currentDep.Version.Contains("+") && candidate.IsNewer(currentDep))
                            {
                                possible = currentDep.RefineVersionRange(candidate);
                            }

                            // only try if the candidate is less than the depenceny
                            if (candidate.Version.Contains("+") && currentDep.IsNewer(candidate))
                            {
                                possible = possible || candidate.RefineVersionRange(currentDep);
                            }

                            if (possible)
                            {
                                // add all the dependency constraints back to make
                                // sure all are met.
                                foreach (Dependency d in dependencyMap.Values)
                                {
                                    if (d.VersionlessKey == candidate.VersionlessKey)
                                    {
                                        if (!nextUnresolved.ContainsKey(d.Key))
                                        {
                                            nextUnresolved.Add(d.Key, d);
                                        }
                                    }
                                }
                            }
                            else if (!possible && useLatest)
                            {
                                // Reload versions of the dependency has they all have been
                                // removed.
                                newCandidate = (currentDep.IsNewer(candidate) ?
                                                currentDep : candidate);
                                newCandidate = newCandidate.HasPossibleVersions ? newCandidate :
                                    FindCandidate(newCandidate);
                                candidates[newCandidate.VersionlessKey] = newCandidate;
                                currentDep = newCandidate;
                                removeDep = true;
                                // Due to a dependency being included via multiple modules we track
                                // whether a warning has already been reported and make sure it's
                                // only reported once.
                                if (!warnings.Contains(currentDep.VersionlessKey)) {
                                    // If no parents of this dependency are found the app
                                    // must have specified the dependency.
                                    string requiredByString =
                                        currentDep.VersionlessKey + " required by (this app)";
                                    // Print dependencies to aid debugging.
                                    var dependenciesMessage = new List<string>();
                                    dependenciesMessage.Add("Found dependencies:");
                                    dependenciesMessage.Add(requiredByString);
                                    foreach (var kv in reverseDependencyTree) {
                                        string requiredByMessage =
                                            String.Format(
                                                "{0} required by ({1})",
                                                kv.Key,
                                                String.Join(
                                                    ", ",
                                                    (new List<string>(kv.Value)).ToArray()));
                                        dependenciesMessage.Add(requiredByMessage);
                                        if (kv.Key == currentDep.VersionlessKey) {
                                            requiredByString = requiredByMessage;
                                        }
                                    }
                                    Log(String.Format(
                                            "No compatible versions of {0}, will try using " +
                                            "the latest version {1}\n\n" +
                                            "{2}\n", requiredByString, currentDep.BestVersion,
                                            String.Join("\n", dependenciesMessage.ToArray())),
                                        level: LogLevel.Warning);
                                    warnings.Add(currentDep.VersionlessKey);
                                }
                            }
                            else if (!possible)
                            {
                                throw new ResolutionException(
                                    String.Format("Cannot resolve {0} and {1}", currentDep.Key,
                                                  candidate.Key),
                                    missingDependencies: new List<Dependency> {
                                                                  currentDep, candidate });
                            }
                        }
                    }
                    else
                    {
                        candidate = FindCandidate(currentDep);
                        if (candidate != null)
                        {
                            candidates.Add(candidate.VersionlessKey, candidate);
                            removeDep = true;
                        }
                        else
                        {
                            throw new ResolutionException(
                                String.Format("Cannot resolve {0}", currentDep.Key),
                                missingDependencies: new List<Dependency> { currentDep });
                        }
                    }

                    // If the dependency has been found.
                    if (removeDep)
                    {
                        // Add all transitive dependencies to resolution list.
                        foreach (Dependency d in GetDependencies(currentDep))
                        {
                            if (!nextUnresolved.ContainsKey(d.Key))
                            {
                                Log("For " + currentDep.Key + " adding dep " + d.Key,
                                    verbose: true);
                                HashSet<string> parentNames;
                                if (!reverseDependencyTree.TryGetValue(d.VersionlessKey,
                                                                       out parentNames)) {
                                    parentNames = new HashSet<string>();
                                }
                                parentNames.Add(currentDep.Key);
                                reverseDependencyTree[d.VersionlessKey] = parentNames;
                                nextUnresolved.Add(d.Key, d);
                            }
                        }
                    }
                    else
                    {
                        if (!nextUnresolved.ContainsKey(currentDep.Key))
                        {
                            nextUnresolved.Add(currentDep.Key, currentDep);
                        }
                    }
                }

                unresolved.Clear();
                unresolved.AddRange(nextUnresolved.Values);
                nextUnresolved.Clear();
            }
            while (unresolved.Count > 0);

            return candidates;
        }

        /// <summary>
        /// Performs the resolution process.  This determines the versions of the
        /// dependencies that should be used.  Transitive dependencies are also
        /// processed.
        /// </summary>
        /// <returns>The dependencies.  The key is the "versionless" key of the dependency.
        /// </returns>
        /// <param name="useLatest">If set to <c>true</c> use latest version of a conflicting
        /// dependency.
        /// if <c>false</c> a ResolutionException is thrown in the case of a conflict.</param>
        /// <param name="destDirectory">Directory dependencies will be copied to using
        /// CopyDependencies().</param>
        /// <param name="explodeAar">Delegate that determines whether a dependency should be
        /// exploded.  If a dependency is currently exploded but shouldn't be according to this
        /// delegate, the dependency is deleted.</param>
        public Dictionary<string, Dependency> ResolveDependencies(
                bool useLatest, string destDirectory = null, ExplodeAar explodeAar = null) {
            return ResolveDependencies(useLatest,
                                       FindMissingDependencies(destDirectory,
                                                               explodeAar: explodeAar),
                                       destDirectory, explodeAar);
        }

        /// <summary>
        /// Copies the dependencies from the repository to the specified directory.
        /// The destination directory is checked for an existing version of the
        /// dependency before copying.  The OverwriteConfirmation delegate is
        /// called for the first existing file or directory.  If the delegate
        /// returns true, the old dependency is deleted and the new one copied.
        /// </summary>
        /// <param name="dependencies">The dependencies to copy.</param>
        /// <param name="destDirectory">Destination directory.</param>
        /// <param name="confirmer">Confirmer - the delegate for confirming overwriting.</param>
        /// <returns>Dictionary of destination files copied keyed by their source paths.</return>
        public Dictionary<string, string> CopyDependencies(
            Dictionary<string, Dependency> dependencies,
            string destDirectory,
            OverwriteConfirmation confirmer)
        {
            var copiedFiles = new Dictionary<string, string>();
            if (!Directory.Exists(destDirectory)) Directory.CreateDirectory(destDirectory);

            // Build a dictionary of the source dependencies without the version in the key
            // to simplify looking up dependencies currently in the project based upon filenames.
            var currentDepsByVersionlessKey =
                new Dictionary<string, KeyValuePair<Dependency, string>>();
            foreach (var item in GetCurrentDependencies(dependencies, destDirectory,
                                                        repoPaths: repositoryPaths)) {
                currentDepsByVersionlessKey[item.Value.Key.VersionlessKey] = item.Value;
            }

            foreach (var dep in dependencies.Values) {
                KeyValuePair<Dependency, string> oldDepFilenamePair;
                if (currentDepsByVersionlessKey.TryGetValue(dep.VersionlessKey,
                                                            out oldDepFilenamePair)) {
                    string oldVersion = ExtractVersionFromFileName(
                        oldDepFilenamePair.Key.BestVersion);
                    string newVersion = ExtractVersionFromFileName(dep.BestVersion);
                    if ((oldVersion == null || (newVersion != null && oldVersion != newVersion)) &&
                        (confirmer == null || confirmer(oldDepFilenamePair.Key, dep))) {
                        DeleteExistingFileOrDirectory(oldDepFilenamePair.Value,
                                                      includeMetaFiles: true);
                    } else {
                        continue;
                    }
                }

                string aarFile = dep.BestVersionArtifact;
                if (aarFile != null) {
                    string baseName = Path.GetFileNameWithoutExtension(aarFile);
                    string extension = Path.GetExtension(aarFile);
                    string destName = Path.Combine(destDirectory, baseName) +
                        (extension == ".srcaar" ? ".aar" : extension);
                    string destNameUnpacked = Path.Combine(
                        destDirectory, Path.GetFileNameWithoutExtension(destName));
                    string existingName =
                        File.Exists(destName) ? destName :
                        Directory.Exists(destNameUnpacked) ? destNameUnpacked : null;

                    bool doCopy = true;
                    if (existingName != null) {
                        doCopy = File.GetLastWriteTime(existingName).CompareTo(
                            File.GetLastWriteTime(aarFile)) < 0;
                        if (doCopy) {
                            DeleteExistingFileOrDirectory(existingName,
                                                          includeMetaFiles: true);
                        }
                    }
                    if (doCopy) {
                        Log(String.Format("Copying Android dependency {0} --> {1}", aarFile,
                                          destName), verbose: true);
                        File.Copy(aarFile, destName);
                        copiedFiles[aarFile] = destName;
                    }
                } else {
                    throw new ResolutionException(
                        String.Format("Cannot find artifact for {0}", dep.Key),
                        missingDependencies: new List<Dependency> { dep });
                }
            }
            return copiedFiles;
        }

        /// <summary>
        /// Create a unique ordered list of items from a list with duplicate items.
        /// Only the first occurrence of items in the original list are kept.
        /// </summary>
        private static List<T> UniqueList<T>(List<T> list)
        {
            // We can't just copy hash to list since we are preserving order.
            HashSet<T> hashSet = new HashSet<T>();
            List<T> outputList = new List<T>();
            foreach (var item in list)
            {
                if (hashSet.Contains(item)) continue;
                hashSet.Add(item);
                outputList.Add(item);
            }
            return outputList;
        }

        /// <summary>
        /// Reads the maven metadata for an artifact.
        /// This reads the list of available versions.
        /// </summary>
        /// <param name="dep">Dependency to process</param>
        /// <param name="fname">file name of the metadata.</param>
        internal static void ProcessMetadata(Dependency dep, string fname)
        {
            XmlTextReader reader = new XmlTextReader(new StreamReader(fname));
            bool inVersions = false;
            var availableVersions = new List<string>();
            while (reader.Read())
            {
                if (reader.Name == "versions")
                {
                    inVersions = reader.IsStartElement();
                }
                else if (inVersions && reader.Name == "version")
                {
                    var version = reader.ReadString();
                    availableVersions.Add(version);
                    dep.AddVersion(version);
                }
            }
            Log(String.Format(
                "Read metadata for {0} found compatible versions ({1}) from available " +
                "versions ({2})",
                dep.Key, String.Join(", ", (new List<string>(dep.PossibleVersions)).ToArray()),
                String.Join(", ", availableVersions.ToArray())), verbose: true);

        }

        internal Dependency FindCandidate(Dependency dep, bool logMissing = true)
        {
            return FindCandidate(dep, repositoryPaths, logMissing: logMissing);
        }

        internal static Dependency FindCandidate(Dependency dep, List<string> repoPaths,
                                                 bool logMissing = true)
        {
            // If artifacts associated with dependencies have been found, return this dependency..
            if (!String.IsNullOrEmpty(dep.RepoPath) && dep.HasPossibleVersions) return dep;

            // Build a set of repositories to search for the dependency.
            List<string> searchPaths = new List<string>();
            if (!String.IsNullOrEmpty(dep.RepoPath)) searchPaths.Add(dep.RepoPath);
            searchPaths.AddRange(dep.Repositories ?? new string[] {});
            if (repoPaths != null) searchPaths.AddRange(repoPaths);

            // Search for the dependency.
            foreach(string repo in UniqueList(searchPaths))
            {
                string repoPath;
                if (repo.StartsWith(SdkVariable))
                {
                    if (String.IsNullOrEmpty(SDKInternal))
                    {
                        throw new ResolutionException(AndroidSdkConfigurationError);
                    }
                    repoPath = repo.Replace(SdkVariable, SDKInternal);
                }
                else
                {
                    repoPath = repo;
                }
                if (Directory.Exists(repoPath))
                {
                    Dependency d = FindCandidate(repoPath, dep);
                    if (d != null)
                    {
                        return d;
                    }
                }
                else
                {
                    Log("Repo not found: " + Path.GetFullPath(repoPath), verbose: true);
                }
            }
            if (logMissing) {
                Log(String.Format("Unable to find dependency {0} in paths ({1}).\n\n" +
                                  "{0} was referenced by:\n{2}\n\n",
                                  dep.Key, String.Join(", ", new List<string>(repoPaths).ToArray()),
                                  dep.CreatedBy), level: LogLevel.Warning);
            }
            return null;
        }

        /// <summary>
        /// Finds an acceptable candidate for the given dependency.
        /// </summary>
        /// <returns>The dependency modified so BestVersion returns the best version and all
        /// missing versions are removed from Dependency.PossibleVersions.</returns>
        /// <param name="repoPath">The path to the artifact repository.</param>
        /// <param name="dep">The dependency to find a specific version for.</param>
        internal static Dependency FindCandidate(string repoPath, Dependency dep)
        {
            Log(String.Format("Reading {0} from repository {1}", dep.Key, repoPath),
                verbose: true);
            string basePath = Path.Combine(dep.Group, dep.Artifact);
            basePath = basePath.Replace(".", Path.DirectorySeparatorChar.ToString());

            string metadataFile = Path.Combine(Path.Combine(repoPath, basePath),
                                               "maven-metadata.xml");
            if (File.Exists(metadataFile))
            {
                ProcessMetadata(dep, metadataFile);
                dep.RepoPath = repoPath;
                while (dep.HasPossibleVersions)
                {
                    // Check for the actual file existing, otherwise skip this version.
                    string aarFile = dep.BestVersionArtifact;
                    if (aarFile != null) return dep;
                    Log(String.Format("Artifact {0} not found for {1} version {2}",
                                      dep.BestVersionPath, dep.Key, dep.BestVersion),
                        verbose: true);
                    dep.RemovePossibleVersion(dep.BestVersion);
                }
            }
            return null;
        }

        internal IEnumerable<Dependency> GetDependencies(Dependency dep)
        {
            // Combine the instance repos with the repos defined for the dep.
            HashSet<string> combinedRepos = new HashSet<string>(repositoryPaths);
            if (dep.Repositories != null) {
                combinedRepos.UnionWith(dep.Repositories);
            }

            return GetDependencies(dep, new List<string>(combinedRepos));
        }

        /// <summary>
        /// Gets the dependencies of the given dependency.
        /// This is done by reading the .pom file for the BestVersion
        /// of the dependency.
        /// </summary>
        /// <returns>The dependencies.</returns>
        /// <param name="dep">Dependency to process</param>
        /// <param name="repoPaths">Set of additional repo paths to search for the
        /// dependencies.</param>
        /// <param name="logError">Log an error if the dependency is missing.</param>
        internal static IEnumerable<Dependency> GetDependencies(Dependency dep,
                                                                List<string> repoPaths,
                                                                bool logError = true)
        {
            List<Dependency> dependencyList = new List<Dependency>();
            var notFoundErrorMessage = String.Format(
                "No compatible versions of {0} found given the set of " +
                "required dependencies.\n\n{0} was referenced by:\n{1}\n\n",
                dep.Key, dep.CreatedBy);
            if (String.IsNullOrEmpty(dep.BestVersion))
            {
                if (logError) Log(notFoundErrorMessage, level: LogLevel.Error);
                return dependencyList;
            }

            string basename = dep.Artifact + "-" + dep.BestVersion + ".pom";
            string pomFile = Path.Combine(dep.BestVersionPath, basename);
            Log(String.Format(
                   "Reading Maven POM of {0}, pom: {1}, versions: {2}",
                   dep.VersionlessKey, pomFile,
                   String.Join(", ", (new List<string>(dep.PossibleVersions)).ToArray())),
                verbose: true);

            XmlTextReader reader = null;
            try {
                reader = new XmlTextReader(new StreamReader(pomFile));
            } catch (DirectoryNotFoundException) {
                if (logError) Log(notFoundErrorMessage, level: LogLevel.Error);
                return dependencyList;
            }
            bool inDependencies = false;
            bool inDep = false;
            string groupId = null;
            string artifactId = null;
            string version = null;
            var missingDependencies = new List<Dependency>();
            while (reader.Read())
            {
                if (reader.Name == "dependencies")
                {
                    inDependencies = reader.IsStartElement();
                }

                if (inDependencies && reader.Name == "dependency")
                {
                    inDep = reader.IsStartElement();
                }

                if (inDep && reader.Name == "groupId")
                {
                    groupId = reader.ReadString();
                }

                if (inDep && reader.Name == "artifactId")
                {
                    artifactId = reader.ReadString();
                }

                if (inDep && reader.Name == "version")
                {
                    version = reader.ReadString().Trim(new Char[] { '[', ']' });
                }

                // if we ended the dependency, add it
                if (!string.IsNullOrEmpty(artifactId) && !inDep)
                {
                    // Unfortunately, the Maven POM doesn't contain metadata to map the package
                    // to each Android SDK package ID so the list "packageIds" is left as null in
                    // this case.
                    Dependency searchDep = new Dependency(groupId, artifactId, version,
                                                          repositories: repoPaths.ToArray());
                    Dependency d = FindCandidate(searchDep, repoPaths);
                    if (d != null) {
                        dependencyList.Add(d);
                    } else {
                        missingDependencies.Add(searchDep);
                    }
                    groupId = null;
                    artifactId = null;
                    version = null;
                }
            }
            if (missingDependencies.Count > 0) {
                var depKeys = new List<string>();
                foreach (var missingDep in missingDependencies) depKeys.Add(missingDep.Key);
                foreach (var foundDep in dependencyList) depKeys.Add(foundDep.Key);
                throw new ResolutionException(
                    String.Format("Cannot find candidate artifacts for '{0}'",
                                  String.Join(", ", depKeys.ToArray())),
                    missingDependencies: missingDependencies);
            }
            return dependencyList;
        }

        /// <summary>
        /// Resets the dependencies. FOR TESTING ONLY!!!
        /// </summary>
        internal static void ResetDependencies()
        {
            if (instances != null) instances.Clear();
        }

        /// <summary>
        /// Read Maven package groups for the specified set of dependencies.
        /// </summary>
        /// <param name="dependencies">Dependencies to load.</param>
        /// <param name="repoPaths">Set of additional repo paths to search for the
        /// dependencies.</param>
        /// <param name="keepMissing">If false, missing dependencies result in a
        /// ResolutionException being thrown.  If true, each missing dependency is included in
        /// the returned set with RepoPath set to an empty string.</param>
        /// <param name="findCandidates">Search repositories for each candidate dependency.</param>
        /// <returns>Dictionary of dependencies with Dependency.PossibleVersions populated with
        /// available versions in the Maven repo.</returns>
        internal static Dictionary<string, Dependency> LoadDependencies(
                Dictionary<string, Dependency> dependencies, List<string> repoPaths,
                bool keepMissing = false, bool findCandidates = true) {
            Dictionary<string, Dependency> dependencyMap = new Dictionary<string, Dependency>();
            foreach (var dependencyItem in dependencies) {
                Dependency foundDependency =
                    findCandidates ? FindCandidate(dependencyItem.Value, repoPaths,
                                                   logMissing: !keepMissing) : null;
                if (foundDependency == null) {
                    if (!keepMissing) {
                        throw new ResolutionException(
                            String.Format("Cannot find candidate artifacts for {0}",
                                          dependencyItem.Value.Key),
                            missingDependencies: new List<Dependency> { dependencyItem.Value });
                    }
                    foundDependency = dependencyItem.Value;
                }
                dependencyMap[foundDependency.Key] = foundDependency;
            }
            return dependencyMap;
        }

        /// <summary>
        /// Loads the dependencies from the current PlayServicesSupport instances.
        /// </summary>
        /// <param name="allClients">If true, all client dependencies are loaded and returned
        /// </param>
        /// <param name="keepMissing">If false, missing dependencies result in a
        /// ResolutionException being thrown.  If true, each missing dependency is included in
        /// the returned set with RepoPath set to an empty string.</param>
        /// <param name="findCandidates">Search repositories for each candidate dependency.</param>
        /// <returns>Dictionary of dependencies</returns>
        public Dictionary<string, Dependency> LoadDependencies(
                bool allClients, bool keepMissing = false, bool findCandidates = true) {
            Dictionary<string, Dependency> dependencyMap =
                new Dictionary<string, Dependency>();
            // Aggregate dependencies of the specified set of PlayServicesSupport instances.
            PlayServicesSupport[] playServicesSupportInstances = allClients ?
                (new List<PlayServicesSupport>(instances.Values)).ToArray() : new [] { this };
            foreach (var instance in playServicesSupportInstances) {
                var newMap = LoadDependencies(
                    instance.clientDependenciesMap, instance.repositoryPaths,
                    keepMissing: keepMissing, findCandidates: findCandidates);
                foreach (var dependency in newMap) {
                    dependencyMap[dependency.Key] = dependency.Value;
                }
                instance.clientDependenciesMap = newMap;
            }
            return dependencyMap;
        }

        /// <summary>
        /// Extracts the version number from the filename handling filenames like foo-1.2.3-alpha.
        /// </summary>
        /// <param name="filename">File name without extension to extract from.</param>
        /// <returns>The version string if extracted successfully and null otherwise.</returns>
        private static string ExtractVersionFromFileName(string filename)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                filename, "^([0-9.]+)");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}

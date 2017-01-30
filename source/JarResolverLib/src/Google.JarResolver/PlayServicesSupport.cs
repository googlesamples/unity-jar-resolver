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
        private static string sdk;

        /// <summary>
        /// Delegate used to specify a log method for this class.  If provided this class
        /// will log messages via this delegate.
        /// </summary>
        public delegate void LogMessage(string message);

        /// <summary>
        /// Log function delegate.  If set, this class will write log messages via this method.
        /// </summary>
        internal static LogMessage logger;

        /// <summary>
        /// The repository paths.
        /// </summary>
        private List<string> repositoryPaths = new List<string>();

        /// <summary>
        /// The client dependencies map.  This is a proper subset of dependencyMap.
        /// </summary>
        private Dictionary<string, Dependency> clientDependenciesMap =
            new Dictionary<string, Dependency>();

        /// <summary>
        /// String that is expanded with the path of the Android SDK.
        /// </summary>
        private const string SdkVariable = "$SDK";

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

        private static string SDKInternal
        {
            get
            {
#if UNITY_EDITOR
                if (String.IsNullOrEmpty(sdk)) {
                    sdk = UnityEditor.EditorPrefs.GetString("AndroidSdkRoot");
                }
#endif  // UNITY_EDITOR
                if (string.IsNullOrEmpty(sdk)) {
                    sdk = System.Environment.GetEnvironmentVariable("ANDROID_HOME");
                }
                return sdk;
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
        public static PlayServicesSupport CreateInstance(
            string clientName,
            string sdkPath,
            string settingsDirectory,
            LogMessage logger = null)
        {
            return CreateInstance(clientName, sdkPath, null, settingsDirectory, logger: logger);
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
        internal static PlayServicesSupport CreateInstance(
            string clientName,
            string sdkPath,
            string[] additionalRepositories,
            string settingsDirectory,
            LogMessage logger = null)
        {
            PlayServicesSupport instance = new PlayServicesSupport();
            PlayServicesSupport.logger = PlayServicesSupport.logger ?? logger;
            PlayServicesSupport.sdk =
                String.IsNullOrEmpty(sdkPath) ? PlayServicesSupport.sdk : sdkPath;
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
        /// <remarks>
        ///   <para>
        ///     message is a string to write to the log.
        ///   </para>
        /// </remarks>
        internal static void Log(string message, bool verbose = false) {
            if (logger != null && (!verbose || verboseLogging)) {
                logger(message);
            }
        }

        /// <summary>
        /// Delete a file or directory if it exists.
        /// </summary>
        /// <param name="path">Path to the file or directory to delete if it exists.</param>
        public static void DeleteExistingFileOrDirectory(string path,
                                                         bool includeMetaFiles = false)
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                di.Attributes &= ~FileAttributes.ReadOnly;
                foreach (string file in Directory.GetFileSystemEntries(path))
                {
                    DeleteExistingFileOrDirectory(file, includeMetaFiles: includeMetaFiles);
                }
                Directory.Delete(path);
            }
            else if (File.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                File.Delete(path);
            }
            if (includeMetaFiles && !path.EndsWith(MetaExtension))
            {
                DeleteExistingFileOrDirectory(path + MetaExtension);
            }
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
        public void DependOn(string group, string artifact, string version,
                             string[] packageIds = null, string[] repositories = null)
        {
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
            var dep = new Dependency(group, artifact, version,
                                     packageIds: packageIds,
                                     repositories: UniqueList(depRepoList).ToArray());
            clientDependenciesMap[dep.Key] = dep;
        }

        /// <summary>
        /// Clears the dependencies for this client.
        /// </summary>
        public void ClearDependencies()
        {
            clientDependenciesMap = new Dictionary<string, Dependency>();
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
            ExplodeAar explodeAar = null, List<string> repoPaths = null)
        {
            var currentDependencies = new Dictionary<string, KeyValuePair<Dependency, string>>();
            if (dependencies.Count == 0) return currentDependencies;

            // Copy the set of dependencies.
            var transitiveDependencies = new Dictionary<string, Dependency>(dependencies);
            // Expand set of transitive dependencies into the dictionary of dependencies.
            foreach (var rootDependency in dependencies.Values)
            {
                foreach (var transitiveDependency in GetDependencies(rootDependency, repoPaths))
                {
                    transitiveDependencies[transitiveDependency.Key] = transitiveDependency;
                }
            }
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
                        // Extract the version from the filename.
                        // dep.Artifact is the name of the package (prefix)
                        // The regular expression extracts the version number from the filename
                        // handling filenames like foo-1.2.3-alpha.
                        match = System.Text.RegularExpressions.Regex.Match(
                            filenameWithoutExtension.Substring(
                                dep.Artifact.Length + 1), "^([0-9.]+)");
                        if (match.Success)
                        {
                            bool reportDependency = true;
                            // If the AAR is exploded and it should not be, delete it and do not
                            // report this dependency.
                            if (pathIsDirectory && explodeAar != null)
                            {
                                string aarFile = dep.BestVersionArtifact;
                                if (aarFile != null && !explodeAar(aarFile))
                                {
                                    DeleteExistingFileOrDirectory(path,
                                                                  includeMetaFiles: true);
                                    reportDependency = false;
                                }
                            }
                            if (reportDependency)
                            {
                                string artifactVersion = match.Groups[1].Value;
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
        /// Determine whether two lists of strings match.
        /// </summary>
        /// <param name="deps1">Enumerable of strings to compare..</param>
        /// <param name="deps2">Enumerable of strings to compare..</param>
        /// <returns>true if both enumerables match, false otherwise.</returns>
        public static bool DependenciesEqual(IEnumerable<string> deps1,
                                             IEnumerable<string> deps2)
        {
            var list1 = new List<string>(deps1);
            var list2 = new List<string>(deps2);
            list1.Sort();
            list2.Sort();
            if (list1.Count != list2.Count) return false;
            for (int i = 0; i < list1.Count; ++i)
            {
                if (list1[i] != list2[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Determine whether the current set of artifacts in the project matches the set of
        /// specified dependencies.
        /// </summary>
        /// <param name="destDirectory">Directory where dependencies are located in the
        /// project.  If this parameter is null, a dictionary of required dependencies is
        /// always returned.</param>
        /// <param name="explodeAar">Delegate that determines whether a dependency should be
        /// exploded.  If a dependency is currently exploded but shouldn't be according to this
        /// delegate, the dependency is deleted.</param>
        /// <returns>null if all dependencies are present, dictionary of all required dependencies
        /// otherwise.</returns>
        public Dictionary<string, Dependency> DependenciesPresent(string destDirectory,
                                                                  ExplodeAar explodeAar = null)
        {
            Dictionary<string, Dependency> dependencyMap =
                LoadDependencies(true, keepMissing: true, findCandidates: true);
            // If a destination directory was specified, determine whether the dependencies
            // referenced by dependencyMap differ to what is present in the project.  If they
            // are the same, we can skip this entire method.
            if (destDirectory != null)
            {
                if (DependenciesEqual(GetCurrentDependencies(dependencyMap, destDirectory,
                                                             explodeAar: explodeAar,
                                                             repoPaths: repositoryPaths).Keys,
                                      dependencyMap.Keys))
                {
                    Log("All dependencies up to date.", verbose: true);
                    return null;
                }
            }
            return dependencyMap;
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
            bool useLatest, string destDirectory = null, ExplodeAar explodeAar = null)
        {
            List<Dependency> unresolved = new List<Dependency>();

            Dictionary<string, Dependency> candidates = new Dictionary<string, Dependency>();

            Dictionary<string, Dependency> dependencyMap =
                DependenciesPresent(destDirectory, explodeAar: explodeAar);
            if (dependencyMap == null) return candidates;

            // Set of each versioned dependencies for each version-less dependency key.
            // e.g if foo depends upon bar, map[bar] = {foo}.
            var reverseDependencyTree = new Dictionary<string, HashSet<string>>();
            var warnings = new HashSet<string>();

            // All dependencies are added to the "unresolved" list.
            foreach (var dependency in dependencyMap.Values)
            {
                unresolved.Add(FindCandidate(dependency));
            }

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
                                    Log(String.Join("\n", dependenciesMessage.ToArray()));
                                    Log(String.Format(
                                        "WARNING: No compatible versions of {0}, will try using " +
                                        "the latest version {1}", requiredByString,
                                        currentDep.BestVersion));
                                    warnings.Add(currentDep.VersionlessKey);
                                }
                            }
                            else if (!possible)
                            {
                                throw new ResolutionException("Cannot resolve " +
                                    currentDep + " and " + candidate);
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
                            throw new ResolutionException("Cannot resolve " +
                                currentDep);
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
        /// Copies the dependencies from the repository to the specified directory.
        /// The destination directory is checked for an existing version of the
        /// dependency before copying.  The OverwriteConfirmation delegate is
        /// called for the first existing file or directory.  If the delegate
        /// returns true, the old dependency is deleted and the new one copied.
        /// </summary>
        /// <param name="dependencies">The dependencies to copy.</param>
        /// <param name="destDirectory">Destination directory.</param>
        /// <param name="confirmer">Confirmer - the delegate for confirming overwriting.</param>
        public void CopyDependencies(
            Dictionary<string, Dependency> dependencies,
            string destDirectory,
            OverwriteConfirmation confirmer)
        {
            if (!Directory.Exists(destDirectory))
            {
                Directory.CreateDirectory(destDirectory);
            }

            // Build a dictionary of the source dependencies without the version in the key
            // to simplify looking up dependencies currently in the project based upon filenames.
            var currentDepsByVersionlessKey =
                new Dictionary<string, KeyValuePair<Dependency, string>>();
            foreach (var item in GetCurrentDependencies(dependencies, destDirectory,
                                                        repoPaths: repositoryPaths))
            {
                currentDepsByVersionlessKey[item.Value.Key.VersionlessKey] = item.Value;
            }

            foreach (var dep in dependencies.Values)
            {
                KeyValuePair<Dependency, string> oldDepFilenamePair;
                if (currentDepsByVersionlessKey.TryGetValue(dep.VersionlessKey,
                                                            out oldDepFilenamePair))
                {
                    if (oldDepFilenamePair.Key.BestVersion != dep.BestVersion &&
                        (confirmer == null || confirmer(oldDepFilenamePair.Key, dep)))
                    {
                        DeleteExistingFileOrDirectory(oldDepFilenamePair.Value,
                                                      includeMetaFiles: true);
                    }
                    else
                    {
                        continue;
                    }
                }

                string aarFile = dep.BestVersionArtifact;

                if (aarFile != null)
                {
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
                    if (existingName != null)
                    {
                        doCopy = File.GetLastWriteTime(existingName).CompareTo(
                            File.GetLastWriteTime(aarFile)) < 0;
                        if (doCopy)
                        {
                            DeleteExistingFileOrDirectory(existingName,
                                                          includeMetaFiles: true);
                        }
                    }
                    if (doCopy)
                    {
                        File.Copy(aarFile, destName);
                    }
                }
                else
                {
                    throw new ResolutionException("Cannot find artifact for " + dep);
                }
            }
        }

        /// <summary>
        /// Create a unique sorted list of items from a list with duplicate items.
        /// </summary>
        private static List<T> UniqueList<T>(List<T> list)
        {
            HashSet<T> hashSet = new HashSet<T>();
            List<T> outputList = new List<T>(list);
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
            while (reader.Read())
            {
                if (reader.Name == "versions")
                {
                    inVersions = reader.IsStartElement();
                }
                else if (inVersions && reader.Name == "version")
                {
                    dep.AddVersion(reader.ReadString());
                }
            }
        }

        internal Dependency FindCandidate(Dependency dep)
        {
            return FindCandidate(dep, repositoryPaths);
        }

        internal static Dependency FindCandidate(Dependency dep, List<string> repoPaths)
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
                    Log("Repo not found: " + Path.GetFullPath(repoPath));
                }
            }
            Log(String.Format("ERROR: Unable to find dependency {0} in paths ({1}).\n\n" +
                              "{0} was referenced by:\n{2}\n\n",
                              dep.Key, String.Join(", ", new List<string>(repoPaths).ToArray()),
                              dep.CreatedBy));
            return null;
        }

        /// <summary>
        /// Finds an acceptable candidate for the given dependency.
        /// </summary>
        /// <returns>The dependency modified so BestVersion returns the best version.</returns>
        /// <param name="repoPath">The path to the artifact repository.</param>
        /// <param name="dep">The dependency to find a specific version for.</param>
        internal static Dependency FindCandidate(string repoPath, Dependency dep)
        {
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
                    Log(dep.Key + " version " + dep.BestVersion + " not available, ignoring.",
                        verbose: true);
                    dep.RemovePossibleVersion(dep.BestVersion);
                }
            }
            return null;
        }

        internal IEnumerable<Dependency> GetDependencies(Dependency dep)
        {
            return GetDependencies(dep, repositoryPaths);
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
        internal static IEnumerable<Dependency> GetDependencies(Dependency dep,
                                                                List<string> repoPaths)
        {
            List<Dependency> dependencyList = new List<Dependency>();
            if (String.IsNullOrEmpty(dep.BestVersion))
            {
                Log(String.Format("ERROR: No compatible versions of {0} found given the set of " +
                                  "required dependencies.\n\n{0} was referenced by:\n{1}\n\n",
                                  dep.Key, dep.CreatedBy));
                return dependencyList;
            }

            string basename = dep.Artifact + "-" + dep.BestVersion + ".pom";
            string pomFile = Path.Combine(dep.BestVersionPath, basename);
            Log("GetDependencies - reading pom of " + basename + " pom: " + pomFile + " " +
                " versions: " +
                String.Join(", ", (new List<string>(dep.PossibleVersions)).ToArray()),
                verbose: true);

            XmlTextReader reader = new XmlTextReader(new StreamReader(pomFile));
            bool inDependencies = false;
            bool inDep = false;
            string groupId = null;
            string artifactId = null;
            string version = null;
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
                    version = reader.ReadString();
                }

                // if we ended the dependency, add it
                if (!string.IsNullOrEmpty(artifactId) && !inDep)
                {
                    // Unfortunately, the Maven POM doesn't contain metadata to map the package
                    // to each Android SDK package ID so the list "packageIds" is left as null in
                    // this case.
                    Dependency d = FindCandidate(new Dependency(groupId, artifactId, version),
                                                 repoPaths);
                    if (d == null)
                    {
                        throw new ResolutionException("Cannot find candidate artifact for " +
                            groupId + ":" + artifactId + ":" + version);
                    }

                    groupId = null;
                    artifactId = null;
                    version = null;
                    dependencyList.Add(d);
                }
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
            bool allClients, bool keepMissing = false, bool findCandidates = true)
        {
            Dictionary<string, Dependency> dependencyMap =
                new Dictionary<string, Dependency>();
            // Aggregate dependencies of the specified set of PlayServicesSupport instances.
            PlayServicesSupport[] playServicesSupportInstances;
            playServicesSupportInstances = allClients ?
                (new List<PlayServicesSupport>(instances.Values)).ToArray() : new [] { this };;
            foreach (var instance in playServicesSupportInstances)
            {
                var newMap = new Dictionary<string, Dependency>();
                foreach (var dependencyItem in instance.clientDependenciesMap)
                {
                    Dependency foundDependency = null;
                    if (findCandidates)
                    {
                        foundDependency = instance.FindCandidate(dependencyItem.Value);
                    }
                    if (foundDependency == null)
                    {
                        if (!keepMissing)
                        {
                            throw new ResolutionException("Cannot find candidate artifact for " +
                                                          dependencyItem.Value.Key);
                        }
                        foundDependency = dependencyItem.Value;
                    }
                    newMap[foundDependency.Key] = foundDependency;
                    dependencyMap[foundDependency.Key] = foundDependency;
                }
                instance.clientDependenciesMap = newMap;
            }
            return dependencyMap;
        }
    }
}

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
        private string sdk;

        /// <summary>
        /// Delegate used to specify a log method for this class.  If provided this class
        /// will log messages via this delegate.
        /// </summary>
        public delegate void LogMessage(string message);

        /// <summary>
        /// Log function delegate.  If set, this class will write log messages via this method.
        /// </summary>
        private LogMessage logger;

        /// <summary>
        /// The settings directory.  Used to store the dependencies.
        /// </summary>
        private string settingsDirectory;

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
        /// Gets the Android SDK.  If it is not set, the environment
        /// variable ANDROID_HOME is used.
        /// </summary>
        /// <value>The SD.</value>
        public string SDK
        {
            get
            {
                if (sdk == null)
                {
                    sdk = System.Environment.GetEnvironmentVariable("ANDROID_HOME");
                }

                return sdk;
            }
        }

        /// <summary>
        /// Gets the name of the dependency file.
        /// </summary>
        /// <value>The name of the dependency file.</value>
        internal string DependencyFileName
        {
            get
            {
                return Path.Combine(
                    settingsDirectory,
                    "GoogleDependency" + clientName + ".xml");
            }
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
        /// <param name="settingsDirectory">The relative path to the directory
        /// to save the settings.  For Unity projects this is "ProjectSettings"</param>
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
        /// <param name="additionalRepositories">Array of additional repository paths. can be null</param>
        /// <param name="settingsDirectory">The relative path to the directory
        /// to save the settings.  For Unity projects this is "ProjectSettings"</param>
        /// <param name="logger">Delegate used to write messages to the log.</param>
        internal static PlayServicesSupport CreateInstance(
            string clientName,
            string sdkPath,
            string[] additionalRepositories,
            string settingsDirectory,
            LogMessage logger = null)
        {
            PlayServicesSupport instance = new PlayServicesSupport();
            instance.logger = logger;
            instance.sdk = sdkPath;
            string badchars = new string(Path.GetInvalidFileNameChars());

            foreach (char ch in clientName)
            {
                if (badchars.IndexOf(ch) >= 0)
                {
                    throw new Exception("Invalid clientName: " + clientName);
                }
            }

            instance.clientName = clientName;
            instance.settingsDirectory = settingsDirectory;

            // Add the standard repo paths from the Android SDK
            string sdkExtrasDir = Path.Combine("$SDK", "extras");
            instance.repositoryPaths.Add(Path.Combine(sdkExtrasDir,
                Path.Combine("android","m2repository")));
            instance.repositoryPaths.Add(Path.Combine(sdkExtrasDir,
                Path.Combine("google","m2repository")));
            if (additionalRepositories != null)
            {
                instance.repositoryPaths.AddRange(additionalRepositories);
            }
            instance.clientDependenciesMap = instance.LoadDependencies(false);
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
        internal void Log(string message) {
            if (logger != null) {
                logger(message);
            }
        }

        /// <summary>
        /// Delete a file or directory if it exists.
        /// </summary>
        /// <param name="path">Path to the file or directory to delete if it exists.</param>
        static internal void DeleteExistingFileOrDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                foreach (string file in Directory.GetFileSystemEntries(path))
                {
                    DeleteExistingFileOrDirectory(file);
                }
                Directory.Delete(path);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
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
        public void DependOn(string group, string artifact, string version,
                             string[] packageIds=null)
        {
            Dependency unresolvedDep = new Dependency(group, artifact, version,
                                                      packageIds: packageIds);
            Dependency dep = FindCandidate(unresolvedDep);
            dep = dep ?? unresolvedDep;

            clientDependenciesMap[dep.Key] = dep;

            PersistDependencies();
        }

        /// <summary>
        /// Clears the dependencies for this client.
        /// </summary>
        public void ClearDependencies()
        {
            DeleteExistingFileOrDirectory(DependencyFileName);
            clientDependenciesMap = LoadDependencies(false);
        }

        /// <summary>
        /// Performs the resolution process.  This determines the versions of the
        /// dependencies that should be used.  Transitive dependencies are also
        /// processed.
        /// </summary>
        /// <returns>The dependencies.  The key is the "versionless" key of the dependency.</returns>
        /// <param name="useLatest">If set to <c>true</c> use latest version of a conflicting dependency.
        /// if <c>false</c> a ResolutionException is thrown in the case of a conflict.</param>
        public Dictionary<string, Dependency> ResolveDependencies(bool useLatest)
        {
            List<Dependency> unresolved = new List<Dependency>();

            Dictionary<string, Dependency> candidates = new Dictionary<string, Dependency>();

            Dictionary<string, Dependency> dependencyMap = LoadDependencies(true);

            // All dependencies are added to the "unresolved" list.
            unresolved.AddRange(dependencyMap.Values);

            do
            {
                Dictionary<string, Dependency> nextUnresolved =
                    new Dictionary<string, Dependency>();

                foreach (Dependency dep in unresolved)
                {
                    bool remove = true;

                    // check for existing candidate
                    Dependency candidate;
                    Dependency newCandidate;
                    if (candidates.TryGetValue(dep.VersionlessKey, out candidate))
                    {
                        if (dep.IsAcceptableVersion(candidate.BestVersion))
                        {
                            remove = true;

                            // save the most restrictive dependency in the
                            //  candidate
                            if (dep.IsNewer(candidate))
                            {
                                candidates[dep.VersionlessKey] = dep;
                            }
                        }
                        else
                        {
                            // in general, we need to iterate
                            remove = false;

                            // refine one or both dependencies if they are
                            // non-concrete.
                            bool possible = false;
                            if (dep.Version.Contains("+") && candidate.IsNewer(dep))
                            {
                                possible = dep.RefineVersionRange(candidate);
                            }

                            // only try if the candidate is less than the depenceny
                            if (candidate.Version.Contains("+") && dep.IsNewer(candidate))
                            {
                                possible = possible || candidate.RefineVersionRange(dep);
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
                                newCandidate = dep.IsNewer(candidate) ? dep : candidate;
                                candidates[newCandidate.VersionlessKey] = newCandidate;
                                remove = true;
                            }
                            else if (!possible)
                            {
                                throw new ResolutionException("Cannot resolve " +
                                    dep + " and " + candidate);
                            }
                        }
                    }
                    else
                    {
                        candidate = FindCandidate(dep);
                        if (candidate != null)
                        {
                            candidates.Add(candidate.VersionlessKey, candidate);
                            remove = true;
                            foreach (Dependency d in GetDependencies(dep))
                            {
                                if (!nextUnresolved.ContainsKey(d.Key))
                                {
                                    nextUnresolved.Add(d.Key, d);
                                }
                            }
                        }
                        else
                        {
                            throw new ResolutionException("Cannot resolve " +
                                dep);
                        }
                    }

                    if (!remove)
                    {
                        if (!nextUnresolved.ContainsKey(dep.Key))
                        {
                            nextUnresolved.Add(dep.Key, dep);
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

            foreach (Dependency dep in dependencies.Values)
            {
                // match artifact-*.  The - is important to distinguish art-1.0.0
                // from artifact-1.0.0 (or base and basement).
                string[] dups =
                    Directory.GetFiles(destDirectory, dep.Artifact + "-*");
                bool doCopy = true;
                bool doCleanup = false;
                foreach (string s in dups)
                {
                    // skip the .meta files (a little Unity creeps in).
                    if (s.EndsWith(".meta"))
                    {
                        continue;
                    }

                    string existing =
                        Path.GetFileNameWithoutExtension(s);

                    // Make sure the file matches the expression "${artifact}-${version}".
                    // e.g foo-bar-1.2.3 would match the glob foo-1.2.3 but would reference
                    // a different artifact.
                    if (!System.Text.RegularExpressions.Regex.Match(
                            existing.Substring(dep.Artifact.Length + 1),
                            "^[0-9.]+$").Success)
                    {
                        continue;
                    }

                    string artifactName = null;
                    string artifactVersion = null;
                    int idx = existing.Length;
                    // handle artifacts like android-support-4.0.0-alpha.aar
                    while(artifactVersion == null && idx > 0) {
                        // the version is after the last -
                        idx = existing.LastIndexOf("-",idx);

                        if (idx >0) {
                            artifactName = existing.Substring(0, idx);
                            artifactVersion = existing.Substring(idx + 1);
                            if (!char.IsDigit(artifactVersion.ToCharArray()[0])) {
                                idx--;
                                artifactVersion = null;
                            }
                        }
                    }

                    Dependency oldDep = new Dependency(dep.Group, artifactName, artifactVersion,
                                                       packageIds: dep.PackageIds);

                    // add the artifact version so BestVersion == version.
                    oldDep.AddVersion(oldDep.Version);
                    // If the existing artifact matches the new dependency, don't modify it.
                    if (dep.Key == oldDep.Key) continue;
                    doCleanup = doCleanup || confirmer == null || confirmer(oldDep, dep);

                    if (doCleanup)
                    {
                        DeleteExistingFileOrDirectory(s);
                    }
                    else
                    {
                        doCopy = false;
                    }
                }

                if (doCopy)
                {
                    string aarFile = null;

                    // TODO(wilkinsonclay): get the extension from the pom file.
                    string[] packaging = { ".aar", ".jar" };

                    string baseName = null;
                    foreach (string ext in packaging)
                    {
                        string fname = Path.Combine(dep.BestVersionPath,
                            dep.Artifact + "-" + dep.BestVersion + ext);
                        if (File.Exists(fname))
                        {
                            baseName = dep.Artifact + "-" + dep.BestVersion + ext;
                            aarFile = fname;
                        }
                    }

                    if (aarFile != null)
                    {
                        string destName = Path.Combine(destDirectory, baseName);
                        string destNameUnpacked = Path.Combine(
                            destDirectory, Path.GetFileNameWithoutExtension(destName));
                        string existingName =
                            File.Exists(destName) ? destName :
                            Directory.Exists(destNameUnpacked) ? destNameUnpacked : null;

                        if (existingName != null)
                        {
                            doCopy = File.GetLastWriteTime(existingName).CompareTo(
                                File.GetLastWriteTime(aarFile)) < 0;
                            if (doCopy) DeleteExistingFileOrDirectory(existingName);
                        }
                        if (doCopy)
                        {
                            File.Copy(aarFile, destName);
                        }
                    }
                    else
                    {
                        throw new ResolutionException("Cannot find artifact for " +
                            dep);
                    }
                }
            }
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
            foreach(string repo in repositoryPaths)
            {
                string repoPath;
                if (repo.StartsWith("$SDK")) {
                    if (SDK == null || SDK.Length == 0)
                    {
                        throw new ResolutionException(AndroidSdkConfigurationError);
                    }
                    repoPath = repo.Replace("$SDK",SDK);
                }
                else {
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
            Log("ERROR: Unable to find dependency " + dep.Group + " " + dep.Artifact + " " +
                dep.Version + " in (" + String.Join(", ", repositoryPaths.ToArray()) + ")");
            return null;
        }

        /// <summary>
        /// Finds an acceptable candidate for the given dependency.
        /// </summary>
        /// <returns>The dependency modified so BestVersion returns the best version.</returns>
        /// <param name="repoPath">The path to the artifact repository.</param>
        /// <param name="dep">The dependency to find a specific version for.</param>
        internal Dependency FindCandidate(string repoPath, Dependency dep)
        {
            string basePath = Path.Combine(dep.Group, dep.Artifact);
            basePath = basePath.Replace(".", Path.DirectorySeparatorChar.ToString());

            string metadataFile = Path.Combine(Path.Combine(repoPath, basePath), "maven-metadata.xml");
            if (File.Exists(metadataFile))
            {
                ProcessMetadata(dep, metadataFile);
                dep.RepoPath = repoPath;
            }
            else
            {
                return null;
            }

            while (dep.HasPossibleVersions)
            {

                // TODO(wilkinsonclay): get the packaging from the pom.
                string[] packaging = { ".aar", ".jar" };

                // Check for the actual file existing, otherwise skip this version.
                foreach (string ext in packaging)
                {
                    string basename = dep.Artifact + "-" + dep.BestVersion + ext;
                    string fname = Path.Combine(dep.BestVersionPath, basename);

                    if (File.Exists(fname))
                    {
                        return dep;
                    }
                }

                dep.RemovePossibleVersion(dep.BestVersion);
            }

            return null;
        }

        /// <summary>
        /// Gets the dependencies of the given dependency.
        /// This is done by reading the .pom file for the BestVersion
        /// of the dependency.
        /// </summary>
        /// <returns>The dependencies.</returns>
        /// <param name="dep">Dependency to process</param>
        internal IEnumerable<Dependency> GetDependencies(Dependency dep)
        {
            List<Dependency> dependencyList = new List<Dependency>();

            string basename = dep.Artifact + "-" + dep.BestVersion + ".pom";
            string pomFile = Path.Combine(dep.BestVersionPath, basename);

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
                    Dependency d = FindCandidate(new Dependency(groupId, artifactId, version));
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
        internal void ResetDependencies()
        {
            string[] clientFiles = Directory.GetFiles(settingsDirectory, "GoogleDependency*");

            foreach (string depFile in clientFiles)
            {
                DeleteExistingFileOrDirectory(depFile);
            }

            ClearDependencies();
        }

        /// <summary>
        /// Loads the dependencies from the settings files.
        /// </summary>
        /// <param name="allClients">If true, all client dependencies are loaded and returned
        /// </param>
        /// <param name="keepMissing">If false, missing dependencies result in a
        /// ResolutionException being thrown.  If true, each missing dependency is included in
        /// the returned set with RepoPath set to an empty string.</param>
        /// <returns>Dictionary of dependencies</returns>
        public Dictionary<string, Dependency> LoadDependencies(bool allClients,
                                                               bool keepMissing=false)
        {
            Dictionary<string, Dependency> dependencyMap =
                new Dictionary<string, Dependency>();

            string[] clientFiles;

            if (allClients)
            {
                clientFiles = Directory.GetFiles(settingsDirectory, "GoogleDependency*");
            }
            else
            {
                clientFiles = new string[] { DependencyFileName };
            }

            foreach (string depFile in clientFiles)
            {
                if (!File.Exists(depFile))
                {
                    continue;
                }

                StreamReader sr = new StreamReader(depFile);
                XmlTextReader reader = new XmlTextReader(sr);

                bool inDependencies = false;
                bool inDep = false;
                string groupId = null;
                string artifactId = null;
                string versionId = null;
                string[] packageIds = null;

                while (reader.Read())
                {
                    if (reader.Name == "dependencies")
                    {
                        inDependencies = reader.IsStartElement();
                    }

                    if (inDependencies && reader.Name == "dependency")
                    {
                        inDep = reader.IsStartElement();
                        if (!inDep)
                        {
                            if (groupId != null && artifactId != null && versionId != null)
                            {
                                Dependency unresolvedDependency =
                                    new Dependency(groupId, artifactId, versionId,
                                                   packageIds: packageIds);
                                Dependency dep = FindCandidate(unresolvedDependency);
                                if (dep == null)
                                {
                                    if (keepMissing)
                                    {
                                        dep = unresolvedDependency;
                                    }
                                    else
                                    {
                                        throw new ResolutionException(
                                            "Cannot find candidate artifact for " +
                                            groupId + ":" + artifactId + ":" + versionId);
                                    }
                                }
                                if (!dependencyMap.ContainsKey(dep.Key))
                                {
                                    dependencyMap[dep.Key] = dep;
                                }
                            }
                            // Reset the dependency being read.
                            groupId = null;
                            artifactId = null;
                            versionId = null;
                            packageIds = null;
                        }
                    }

                    if (inDep)
                    {
                        if (reader.Name == "groupId")
                        {
                            groupId = reader.ReadString();
                        }
                        else if (reader.Name == "artifactId")
                        {
                            artifactId = reader.ReadString();
                        }
                        else if (reader.Name == "version")
                        {
                            versionId = reader.ReadString();
                        }
                        else if (reader.Name == "packageIds")
                        {
                            // ReadContentAs does not appear to work for string[] in Mono 2.0
                            // instead read the field as a string and split to retrieve each
                            // packageId from the set.
                            string packageId = reader.ReadString();
                            packageIds = packageId.Split(new char[] {' '});
                        }
                    }
                }
                reader.Close();
                sr.Close();
            }

            return dependencyMap;
        }

        /// <summary>
        /// Persists the dependencies to the settings file.
        /// </summary>
        internal void PersistDependencies()
        {
            DeleteExistingFileOrDirectory(DependencyFileName);

            StreamWriter sw = new StreamWriter(DependencyFileName);

            XmlTextWriter writer = new XmlTextWriter(sw);

            writer.WriteStartElement("dependencies");
            foreach (Dependency dep in clientDependenciesMap.Values)
            {
                writer.WriteStartElement("dependency");
                writer.WriteStartElement("groupId");
                writer.WriteString(dep.Group);
                writer.WriteEndElement();

                writer.WriteStartElement("artifactId");
                writer.WriteString(dep.Artifact);
                writer.WriteEndElement();

                writer.WriteStartElement("version");
                writer.WriteString(dep.Version);
                writer.WriteEndElement();

                if (dep.PackageIds != null)
                {
                    writer.WriteStartElement("packageIds");
                    writer.WriteValue(dep.PackageIds);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();

            writer.Flush();
            writer.Close();
        }
    }
}

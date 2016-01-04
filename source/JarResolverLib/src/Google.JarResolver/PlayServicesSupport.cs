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
        /// The settings directory.  Used to store the dependencies.
        /// </summary>
        private string settingsDirectory;

        /// <summary>
        /// The client dependencies map.  This is a proper subset of dependencyMap.
        /// </summary>
        private Dictionary<string, Dependency> clientDependenciesMap =
            new Dictionary<string, Dependency>();

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
        public static PlayServicesSupport CreateInstance(
            string clientName,
            string sdkPath,
            string settingsDirectory)
        {
            PlayServicesSupport instance = new PlayServicesSupport();
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
            instance.clientDependenciesMap = instance.LoadDependencies(false);
            return instance;
        }

        /// <summary>
        /// Adds a dependency to the project.  This method should be called for
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
        /// </summary>
        /// <param name="group">Group - the Group Id of the artiface</param>
        /// <param name="artifact">Artifact - Artifact Id</param>
        /// <param name="version">Version - the version constraint</param>
        /// <exception cref="ResolutionException">thrown if the artifact is unknown.</exception>
        public void DependOn(string group, string artifact, string version)
        {
            Dependency dep = FindCandidate(new Dependency(group, artifact, version));
            if (dep == null)
            {
                throw new ResolutionException("Cannot find candidate artifact for " +
                    group + ":" + artifact + ":" + version);
            }

            clientDependenciesMap[dep.Key] = dep;

            PersistDependencies();
        }

        /// <summary>
        /// Clears the dependencies for this client.
        /// </summary>
        public void ClearDependencies()
        {
            if (File.Exists(DependencyFileName))
            {
                File.Delete(DependencyFileName);
            }

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

                    // the version is after the last -
                    int idx = existing.LastIndexOf("-");
                    string artifactName = existing.Substring(0, idx);
                    string artifactVersion = existing.Substring(idx + 1);

                    Dependency oldDep = new Dependency(dep.Group, artifactName, artifactVersion);

                    // add the artifact version so BestVersion == version.
                    oldDep.AddVersion(oldDep.Version);
                    doCleanup = doCleanup || confirmer == null || confirmer(oldDep, dep);

                    if (doCleanup)
                    {
                        if (File.Exists(s))
                        {
                            File.Delete(s);
                        }
                        else if (Directory.Exists(s))
                        {
                            Directory.Delete(s);
                        }
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
                        string fname = SDK + "/" + dep.BestVersionPath + "/" + dep.Artifact + "-" + dep.BestVersion + ext;
                        if (File.Exists(fname))
                        {
                            baseName = dep.Artifact + "-" + dep.BestVersion + ext;
                            aarFile = fname;
                        }
                    }

                    if (aarFile != null)
                    {
                        string destName = Path.Combine(destDirectory, baseName);
                        if (File.Exists(destName))
                        {
                            File.Delete(destName);
                        }
                        File.Copy(aarFile, destName);
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

        /// <summary>
        /// Finds an acceptable candidate for the given dependency.
        /// </summary>
        /// <returns>The dependency modified so BestVersion returns the best version.</returns>
        /// <param name="dep">The dependency to find a specific version for.</param>
        internal Dependency FindCandidate(Dependency dep)
        {
            // look in the repositories.
            string path = dep.Group + "/" + dep.Artifact;
            if (SDK == null)
            {
                throw new ResolutionException(
                    "Android SDK path not set." +
                    "  Set the SDK property or the ANDROID_HOME environment variable");
            }

            path = path.Replace(".", Path.DirectorySeparatorChar.ToString());

            string[] repos =
                {
                    "extras/android/m2repository",
                    "extras/google/m2repository"
                };

            foreach (string s in repos)
            {
                string fname = SDK + "/" + s + "/" + path + "/maven-metadata.xml";
                if (File.Exists(fname))
                {
                    ProcessMetadata(dep, fname);
                    dep.RepoPath = s;
                    break;
                }
            }

            while (dep.HasPossibleVersions)
            {
                // look for an existing artifact
                path = dep.Group + "/" + dep.Artifact;
                path = path.Replace(".", Path.DirectorySeparatorChar.ToString());
                path += "/" + dep.BestVersion;

                // TODO(wilkinsonclay): get the packaging from the pom.
                string[] packaging = { ".aar", ".jar" };

                // Check for the actual file existing, otherwise skip this version.
                foreach (string ext in packaging)
                {
                    string basename = dep.Artifact + "-" + dep.BestVersion + ext;
                    string fname = Path.Combine(
                                       SDK,
                                       Path.Combine(dep.BestVersionPath, basename));

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
            if (SDK == null)
            {
                throw new ResolutionException(
                    "Android SDK path not set." +
                    "  Set the SDK property or the ANDROID_HOME environment variable");
            }

            string basename = dep.Artifact + "-" + dep.BestVersion + ".pom";
            string pomFile = Path.Combine(
                                 SDK,
                                 Path.Combine(dep.BestVersionPath, basename));

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
                File.Delete(depFile);
            }

            ClearDependencies();
        }

        /// <summary>
        /// Loads the dependencies from the settings files.
        /// </summary>
        /// <param name="allClients">If true, all client dependencies are loaded and returned.</param>
        /// <returns>Dictionary of dependencies</returns>
        internal Dictionary<string, Dependency> LoadDependencies(bool allClients)
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
                            Dependency dep = FindCandidate(new Dependency(groupId, artifactId, versionId));
                            if (dep == null)
                            {
                                throw new ResolutionException("Cannot find candidate artifact for " +
                                    groupId + ":" + artifactId + ":" + versionId);
                            }

                            if (!dependencyMap.ContainsKey(dep.Key))
                            {
                                dependencyMap[dep.Key] = dep;
                            }
                        }
                    }

                    if (inDep && reader.Name == "groupId")
                    {
                        groupId = reader.ReadString();
                    }
                    else if (inDep && reader.Name == "artifactId")
                    {
                        artifactId = reader.ReadString();
                    }
                    else if (inDep && reader.Name == "version")
                    {
                        versionId = reader.ReadString();
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
            if (File.Exists(DependencyFileName))
            {
                File.Delete(DependencyFileName);
            }

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

                writer.WriteEndElement();
            }

            writer.WriteEndElement();

            writer.Flush();
            writer.Close();
        }
    }
}

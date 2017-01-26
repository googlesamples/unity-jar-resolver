// <copyright file="Dependency.cs" company="Google Inc.">
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
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;

    /// <summary>
    /// Represents a dependency.  A dependency is defined by a groupId,
    /// artifactId and version constraint.  This information is used to search
    /// the repositories of artifacts to find a version that meets the version
    /// contraints (as well as be compatible with other dependencies' constraints).
    /// <para>
    /// Once the version is identified, the BestVersion property is used to get
    /// the concrete version number that should be used.
    /// </para>
    /// </summary>
    public class Dependency
    {
        // TODO(wilkinsonclay): get the extension from the pom file.
        internal static string[] Packaging = {
            ".aar",
            ".jar",
            // This allows users to place an aar inside a Unity project and have Unity
            // ignore the file as part of the build process, but still allow the Jar
            // Resolver to process the AAR so it can be included in the build.
            ".srcaar"
        };

        /// <summary>
        /// The version comparator.  This comparator results in a descending sort
        /// order by version.
        /// </summary>
        private readonly VersionComparer versionComparison = new VersionComparer();

        /// <summary>
        /// The possible versions found in the repository.  This list is mutable
        /// and will change as the constraints are applied.
        /// </summary>
        private List<string> possibleVersions;

        /// <summary>
        /// Initializes a new instance of the
        ///  <see cref="Google.JarResolver.Dependency"/> class.
        /// </summary>
        /// <param name="group">Group ID</param>
        /// <param name="artifact">Artifact ID</param>
        /// <param name="version">Version constraint.</param>
        /// <param name="packageIds">Android SDK package identifiers required for this
        /// artifact.</param>
        /// <param name="repositories">List of additional repository directories to search for
        /// this artifact.</param>
        public Dependency(string group, string artifact, string version, string[] packageIds=null,
                          string[] repositories=null)
        {
            Group = group;
            Artifact = artifact;
            Version = version;
            PackageIds = packageIds;
            this.possibleVersions = new List<string>();
            Repositories = repositories;
            CreatedBy = System.Environment.StackTrace;
        }

        /// <summary>
        /// Stack trace of the point where this was created.
        /// </summary>
        internal string CreatedBy { get; private set; }

        /// <summary>
        /// Gets the group ID
        /// </summary>
        /// <value>The group.</value>
        public string Group { get; private set; }

        /// <summary>
        /// Gets the artifact ID.
        /// </summary>
        /// <value>The artifact.</value>
        public string Artifact { get; private set; }

        /// <summary>
        /// Gets the version constraint.
        /// </summary>
        /// <value>The version.</value>
        public string Version { get; private set; }

        /// <summary>
        /// Array of Android SDK identifiers for packages that are required for this
        /// artifact.
        /// </summary>
        /// <value>Package identifiers if set or null.</value>
        public string[] PackageIds { get; private set; }

        /// <summary>
        /// Array of repositories to search for this artifact.
        /// </summary>
        /// <value>List of repository directories if set or null.</value>
        public string[] Repositories { get; private set; }

        /// <summary>
        /// Gets the best version based on the version contraint, other
        /// dependencies (if resolve has been run), and the availability of the
        /// artifacts in the repository.  If this value is null or empty, either
        /// it has not been initialized by calling
        /// PlayServicesSupport.AddDependency()
        /// or there are no versions that meet all the constraints.
        /// </summary>
        /// <value>The best version.</value>
        public string BestVersion
        {
            get
            {
                if (possibleVersions.Count > 0)
                {
                    return possibleVersions[0];
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// Returns the available versions of this dependency.
        /// </summary>
        public ReadOnlyCollection<string> PossibleVersions
        {
            get
            {
                return possibleVersions.AsReadOnly();
            }
        }

        /// <summary>
        /// Gets the best version path relative to the SDK.
        /// </summary>
        /// <value>The best version path.</value>
        public string BestVersionPath
        {
            get
            {
                if (!string.IsNullOrEmpty(BestVersion))
                {
                    string path = Group + Path.DirectorySeparatorChar +
                                  Artifact;
                    path = path.Replace('.', Path.DirectorySeparatorChar);
                    return RepoPath + Path.DirectorySeparatorChar + path +
                    Path.DirectorySeparatorChar + BestVersion;
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Get the path to the artifact.
        /// </summary>
        public string BestVersionArtifact
        {
            get
            {
                // TODO(wilkinsonclay): get the extension from the pom file.
                string filenameWithoutExtension =
                    Path.Combine(BestVersionPath, Artifact + "-" + BestVersion);
                foreach (string extension in Packaging)
                {
                    string filename = filenameWithoutExtension + extension;
                    if (File.Exists(filename)) return filename;
                }
                return null;
            }
        }

        /// <summary>
        /// Gets or sets the repository path for this dependency.  This is
        /// relative to the SDK.
        /// </summary>
        /// <value>The repo path.</value>
        public string RepoPath { get; set; }

        /// <summary>
        /// Gets the versionless key.  This key is used to manage collections
        /// of dependencies, regardless of the version constraint.
        /// </summary>
        /// <value>The versionless key.</value>
        public string VersionlessKey
        {
            get
            {
                return Group + ":" + Artifact;
            }
        }

        /// <summary>
        /// Gets the key for this dependency.  The key is a tuple of the
        /// group, artifact and version constraint.
        /// </summary>
        /// <value>The key.</value>
        public string Key
        {
            get
            {
                return Group + ":" + Artifact + ":" + Version;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has possible versions.
        /// </summary>
        /// <value><c>true</c> if this instance has possible versions.</value>
        public bool HasPossibleVersions
        {
            get
            {
                return possibleVersions.Count > 0;
            }
        }

        /// <summary>
        /// Determines whether this instance is newer the specified candidate.
        /// </summary>
        /// <returns><c>true</c>
        /// if this instance is newer the specified candidate.</returns>
        /// <param name="candidate">Candidate to test</param>
        public bool IsNewer(Dependency candidate)
        {
            if (candidate.Group == Group && candidate.Artifact == Artifact)
            {
                return IsGreater(Version, candidate.Version);
            }

            return false;
        }

        /// <summary>
        /// Determines whether this instance is acceptable based
        ///  on the version constraint.
        /// </summary>
        /// <returns><c>true</c> if this instance is acceptable
        ///  version the specified ver.</returns>
        /// <param name="ver">Version to check.</param>
        public bool IsAcceptableVersion(string ver)
        {
            bool hasPlus = Version.Contains("+");
            bool latest = Version.ToUpper().Equals("LATEST");
            if (latest)
            {
                return string.IsNullOrEmpty(BestVersion) ||
                IsGreater(ver, BestVersion);
            }

            if (!hasPlus)
            {
                if (ver.Equals(Version))
                {
                    return true;
                }
                else
                {
                    string[] myParts = Version.Split('.');
                    string[] parts = ver.Split('.');
                    return AreEquivalent(myParts, parts);
                }
            }
            else
            {
                string[] myParts = Version.Split('.');
                string[] parts = ver.Split('.');
                return IsAcceptable(myParts, parts);
            }
        }

        /// <summary>
        /// Refines the possible version range  based on the given candidate.
        /// This is done by removing possible versions that are not acceptable
        /// to the candidate.
        /// </summary>
        /// <returns><c>true</c>, if there are still possible versions.</returns>
        /// <param name="candidate">Candidate to test versions with.</param>
        public bool RefineVersionRange(Dependency candidate)
        {
            // remove all possible versions that are not acceptable to the
            // candidate
            List<string> removals = new List<string>();

            // add the possible versions to both, so the sets are the same.
            foreach (string v in possibleVersions)
            {
                if (!candidate.IsAcceptableVersion(v))
                {
                    removals.Add(v);
                }
            }

            foreach (string v in removals)
            {
                possibleVersions.Remove(v);
            }

            return HasPossibleVersions;
        }

        /// <summary>
        /// Removes the possible version.
        /// </summary>
        /// <param name="ver">Ver to remove.</param>
        public void RemovePossibleVersion(string ver)
        {
            possibleVersions.Remove(ver);
        }

        /// <summary>
        /// Adds the version as possible version if acceptable.
        /// Acceptable versions meet the version constraint and are not already in
        /// the list.
        /// </summary>
        /// <param name="ver">Version to add</param>
        public void AddVersion(string ver)
        {
            if (possibleVersions.Contains(ver))
            {
                return;
            }

            if (IsAcceptableVersion(ver))
            {
                possibleVersions.Add(ver);
            }

            possibleVersions.Sort(versionComparison);
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents
        /// the current <see cref="Google.JarResolver.Dependency"/>.
        /// </summary>
        /// <returns>A <see cref="System.String"/> that represents the
        /// current <see cref="Google.JarResolver.Dependency"/>.</returns>
        public override string ToString()
        {
            return Key + "(" + BestVersion + ")";
        }

        /// <summary>
        /// Determines if version1 is greater than version2
        /// </summary>
        /// <returns><c>true</c> if version1  is greater than version2.</returns>
        /// <param name="version1">Version1 to test.</param>
        /// <param name="version2">Version2 to test.</param>
        internal static bool IsGreater(string version1, string version2)
        {
            // only works for concrete versions so remove "+"
            string[] parts1;
            string[] parts2;
            if (version1.EndsWith("+"))
            {
                parts1 = version1.Substring(0, version1.Length - 1).Split('.');
            }
            else
            {
                parts1 = version1.Split('.');
            }

            if (version2.EndsWith("+"))
            {
                parts2 = version2.Substring(0, version2.Length - 1).Split('.');
            }
            else
            {
                parts2 = version2.Split('.');
            }

            int i1 = 0;
            int i2 = 0;

            while (i1 < parts1.Length || i2 < parts2.Length)
            {
                int val1 = -1;
                int val2 = -1;
                if (i1 < parts1.Length)
                {
                    if (!int.TryParse(parts1[i1], out val1))
                    {
                        // -1 means use string compare
                        val1 = -1;
                    }
                }
                else
                {
                    val1 = -2;
                }

                if (i2 < parts2.Length)
                {
                    if (!int.TryParse(parts2[i2], out val2))
                    {
                        // -1 means use string compare
                        val2 = -1;
                    }
                }
                else
                {
                    val2 = -3;
                }

                if (val1 != val2 || val1 < 0)
                {
                    if (val1 == -1)
                    {
                        if (val2 >= -1)
                        {
                            return parts1[i1].CompareTo(parts2[i2]) > 0;
                        }
                        else
                        {
                            // parts2 is shorter, so parts1 wins.
                            return true;
                        }
                    }
                    else if (val2 == -1)
                    {
                        if (val1 >= -1)
                        {
                            return parts1[i1].CompareTo(parts2[i2]) > 0;
                        }
                        else
                        {
                            // parts1 is shorter, so parts2 wins.
                            return false;
                        }
                    }
                    else
                    {
                        return val1 > val2;
                    }
                }

                i1++;
                i2++;
            }

            return false;
        }

        /// <summary>
        /// Determines whether version 2 meets the constraints of version 1
        /// </summary>
        /// <returns><c>true</c> if ver2 is acceptable to ver1.</returns>
        /// <param name="ver1">Version 1</param>
        /// <param name="ver2">Version 2</param>
        internal bool IsAcceptable(string[] ver1, string[] ver2)
        {
            int i1 = 0;
            int i2 = 0;
            bool sawPlus = false;
            while (i1 < ver1.Length || i2 < ver2.Length)
            {
                int val1 = -1;
                int val2 = -1;

                // check if v1 has the + at this index.
                if (i1 >= ver1.Length)
                {
                    // use the wildcard to extend?
                    throw new System.NotImplementedException();
                }
                else if (ver1[i1].Contains("+"))
                {
                    sawPlus = true;
                    string v = ver1[i1].Substring(0, ver1[i1].IndexOf('+'));
                    if (!int.TryParse(v, out val1))
                    {
                        if (string.IsNullOrEmpty(v))
                        {
                            val1 = 0;
                        }
                        else
                        {
                            // -1 means use string compare
                            val1 = -1;
                        }
                    }
                }
                else
                {
                    // straight comparison
                    if (!int.TryParse(ver1[i1], out val1))
                    {
                        // -1 means use string compare
                        val1 = -1;
                    }
                }

                if (i2 >= ver2.Length)
                {
                    return false;
                }
                else
                {
                    // straight comparison
                    if (!int.TryParse(ver2[i2], out val2))
                    {
                        // -1 means use string compare
                        val2 = -1;
                    }
                }

                if ((val1 == -1 || val2 == -1) &&
                    !ver1[i1].ToLower().Equals(ver2[i2].ToLower()))
                {
                    return false;
                }
                else if (val1 != val2 && !sawPlus)
                {
                    return false;
                }
                else if (sawPlus)
                {
                    return val1 <= val2;
                }

                i1++;
                i2++;
            }

            return true;
        }

        /// <summary>
        /// Checks if ours version (stored as an array) is equivalent to theirs.
        /// Equivalency handles identifying version constraints (ours) that
        /// do not have the same number of trailing zeros as a version (theirs).
        /// </summary>
        /// <returns><c>true</c>, if equivalent <c>false</c> otherwise.</returns>
        /// <param name="ours">our version parsed into an array.</param>
        /// <param name="theirs">Theirs parsed into an array.</param>
        internal bool AreEquivalent(string[] ours, string[] theirs)
        {
            int ourIndex = 0;
            int theirIndex = 0;
            while (ourIndex < ours.Length || theirIndex < theirs.Length)
            {
                int us = -1;
                int them = -1;
                if (ourIndex < ours.Length)
                {
                    if (!int.TryParse(ours[ourIndex], out us))
                    {
                        // -1 means use string compare
                        us = -1;
                    }
                }
                else
                {
                    us = 0;
                }

                if (theirIndex < theirs.Length)
                {
                    if (!int.TryParse(theirs[theirIndex], out them))
                    {
                        // -1 means use string compare
                        them = -1;
                    }
                }
                else
                {
                    them = 0;
                }

                if (us >= 0 && them >= 0 && us != them)
                {
                    return false;
                }
                else if ((us < 0 || them < 0) &&
                    !ours[ourIndex].ToLower().Equals(theirs[theirIndex].ToLower()))
                {
                    return false;
                }

                ourIndex++;
                theirIndex++;
            }

            return true;
        }

        /// <summary>
        /// Version comparer. Resulting in a descending list of versions.
        /// </summary>
        public class VersionComparer : IComparer<string>
        {
            #region IComparer implementation

            /// <summary>
            /// Compare the specified x and y.
            /// </summary>
            /// <param name="x">The x coordinate.</param>
            /// <param name="y">The y coordinate.</param>
            /// <returns>negative if x is greater than y,
            /// positive if y is greater than x, 0 if equal.</returns>
            public int Compare(string x, string y)
            {
                if (IsGreater(x, y))
                {
                    return -1;
                }
                else if (IsGreater(y, x))
                {
                    return 1;
                }
                else
                {
                    return 0;
                }
            }

            #endregion
        }
    }
}

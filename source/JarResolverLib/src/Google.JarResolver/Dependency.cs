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

namespace Google.JarResolver {
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System;

    /// <summary>
    /// Represents a dependency.  A dependency is defined by a groupId,
    /// artifactId and version constraint.  This information is used to search
    /// the repositories of artifacts to find a version that meets the version
    /// constraints (as well as be compatible with other dependencies' constraints).
    /// </summary>
    public class Dependency {
        // Extensions of files managed by the resolver.
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
        internal static readonly VersionComparer versionComparer = new VersionComparer();

        /// <summary>
        /// Initializes a new instance of the
        ///  <see cref="Google.JarResolver.Dependency"/> class.
        /// </summary>
        /// <param name="group">Group ID</param>
        /// <param name="artifact">Artifact ID</param>
        /// <param name="version">Version constraint.</param>
        /// <param name="classifier">Artifact classifier.</param>
        /// <param name="packageIds">Android SDK package identifiers required for this
        /// artifact.</param>
        /// <param name="repositories">List of additional repository directories to search for
        /// this artifact.</param>
        /// <param name="createdBy">Human readable string that describes where this dependency
        /// originated.</param>
        public Dependency(string group, string artifact, string version,
                          string classifier = null, string[] packageIds=null,
                          string[] repositories=null, string createdBy=null) {
            // If the dependency was added programmatically, strip out stack frames from inside the
            // library since the developer is likely interested in where in their code the
            // dependency was injected.
            if (createdBy == null) {
                var usefulFrames = new List<string>();
                bool filterFrames = true;
                // Filter all of the initial stack frames from system libraries and this plugin.
                foreach (var frame in System.Environment.StackTrace.Split(new char[] { '\n' })) {
                    var frameString = frame.Trim();
                    if (frameString.StartsWith("at ")) frameString = frameString.Split()[1];
                    if (filterFrames && (
                            frameString.StartsWith("System.Environment.") ||
                            frameString.StartsWith("Google.JarResolver.") ||
                            frameString.StartsWith("System.Reflection.") ||
                            frameString.StartsWith("Google.VersionHandler"))) {
                        continue;
                    }
                    filterFrames = false;

                    // From Unity 2019, System.Environment.StackTrace stops returning parentheses.
                    // Remove the parentheses here to keep result consistent.
                    if (frameString.EndsWith("()")) {
                        frameString = frameString.Substring(0, frameString.Length - 2);
                    }
                    usefulFrames.Add(frameString);
                }
                createdBy = String.Join("\n", usefulFrames.ToArray());
            }
            Group = group;
            Artifact = artifact;
            Version = version;
            Classifier = classifier;
            PackageIds = packageIds;
            Repositories = repositories;
            CreatedBy = createdBy;
        }

        /// <summary>
        /// Copy Dependency.
        /// </summary>
        /// <param name="dependency">Dependency to copy.</param>
        public Dependency(Dependency dependency) {
            Group = dependency.Group;
            Artifact = dependency.Artifact;
            Version = dependency.Version;
            Classifier = dependency.Classifier;
            if (dependency.PackageIds != null) {
                PackageIds = (string[])dependency.PackageIds.Clone();
            }
            if (dependency.Repositories != null) {
                Repositories = (string[])dependency.Repositories.Clone();
            }
            CreatedBy = dependency.CreatedBy;
        }

        /// <summary>
        /// Tag that indicates where this was created.
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
        public string Version { get; set; }

        /// <summary>
        /// Gets the classifier.
        /// </summary>
        /// <value>The classifier.</value>
        public string Classifier { get; set; }

        /// <summary>

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
        /// Gets the versionless key.  This key is used to manage collections
        /// of dependencies, regardless of the version constraint.
        /// </summary>
        /// <value>The versionless key.</value>
        public string VersionlessKey { get { return Group + ":" + Artifact; } }

        /// <summary>
        /// Gets the key for this dependency.  The key is a tuple of the
        /// group, artifact and version constraint.
        /// </summary>
        /// <value>The key.</value>
        public string Key { get {
            string key = Group + ":" + Artifact + ":" + Version;
            if (!String.IsNullOrEmpty(Classifier))
                key += ":" + Classifier;
            return key; } }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents
        /// the current <see cref="Google.JarResolver.Dependency"/>.
        /// </summary>
        /// <returns>A <see cref="System.String"/> that represents the
        /// current <see cref="Google.JarResolver.Dependency"/>.</returns>
        public override string ToString() { return Key; }

        /// <summary>
        /// Determines if version1 is greater than version2
        /// </summary>
        /// <returns><c>true</c> if version1  is greater than version2.</returns>
        /// <param name="version1">Version1 to test.</param>
        /// <param name="version2">Version2 to test.</param>
        private static bool IsGreater(string version1, string version2) {
            version1 = version1.EndsWith("+") ?
                version1.Substring(0, version1.Length - 1) : version1;
            version2 = version2.EndsWith("+") ?
                version2.Substring(0, version2.Length - 1) : version2;
            string[] version1Components = version1.Split('.');
            string[] version2Components = version2.Split('.');
            int componentsToCompare = Math.Min(version1Components.Length,
                                               version2Components.Length);
            for (int i = 0; i < componentsToCompare; ++i) {
                string version1Component = version1Components[i];
                int version1ComponentInt;
                string version2Component = version2Components[i];
                int version2ComponentInt;
                if (Int32.TryParse(version1Component, out version1ComponentInt) &&
                    Int32.TryParse(version2Component, out version2ComponentInt)) {
                    if (version1ComponentInt > version2ComponentInt) {
                        return true;
                    } else if (version1ComponentInt < version2ComponentInt) {
                        return false;
                    }
                } else {
                    int stringCompareResult = version1Component.CompareTo(version2Component);
                    if (stringCompareResult > 0) {
                        return true;
                    } else if (stringCompareResult < 0) {
                        return false;
                    }
                }
            }
            return version1Components.Length > version2Components.Length;
        }

        /// <summary>
        /// Version comparer. Resulting in a descending list of versions.
        /// </summary>
        public class VersionComparer : IComparer<string> {
            /// <summary>
            /// Compare the specified x and y.
            /// </summary>
            /// <param name="x">The x coordinate.</param>
            /// <param name="y">The y coordinate.</param>
            /// <returns>negative if x is greater than y,
            /// positive if y is greater than x, 0 if equal.</returns>
            public int Compare(string x, string y) {
                if (IsGreater(x, y)) {
                    return -1;
                } else if (IsGreater(y, x)) {
                    return 1;
                }
                return 0;
            }
        }
    }
}

// <copyright file="LocalMavenRepository.cs" company="Google Inc.">
// Copyright (C) 2019 Google Inc. All Rights Reserved.
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

namespace GooglePlayServices {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Xml;

    using Google;
    using Google.JarResolver;

    /// <summary>
    /// Finds and operates on Maven repositories embedded in the Unity project.
    /// </summary>
    internal class LocalMavenRepository {

        /// <summary>
        /// Find paths to repositories that are included in the project.
        /// </summary>
        /// <param name="dependencies">Dependencies to search for local repositories.</param>
        /// <returns>Set of repository paths in the project.</returns>
        public static HashSet<string> FindLocalRepos(ICollection<Dependency> dependencies) {
            // Find all repositories embedded in the project.
            var repos = new HashSet<string>();
            var projectUri = GradleResolver.RepoPathToUri(Path.GetFullPath("."));
            foreach (var reposAndSources in
                     PlayServicesResolver.GetRepos(dependencies: dependencies)) {
                var repoUri = reposAndSources.Key;
                if (repoUri.StartsWith(projectUri)) {
                    repos.Add(Uri.UnescapeDataString(repoUri.Substring(projectUri.Length + 1)));
                }
            }
            return repos;
        }

        /// <summary>
        /// Find all .aar and .srcaar files under a directory.
        /// </summary>
        /// <param name="directory">Directory to recursively search.</param>
        /// <returns>A list of found aar and srcaar files.</returns>
        public static List<string> FindAars(string directory) {
            var foundFiles = new List<string>();
            if (Directory.Exists(directory)) {
                foreach (string filename in Directory.GetFiles(directory)) {
                    var packaging = Path.GetExtension(filename).ToLower();
                    if (packaging == ".aar" || packaging == ".srcaar") foundFiles.Add(filename);
                }
                foreach (string currentDirectory in Directory.GetDirectories(directory)) {
                    foundFiles.AddRange(FindAars(currentDirectory));
                }
            }
            return foundFiles;
        }

        /// <summary>
        /// Find all .aar and .srcaar files in the project's repositories.
        /// </summary>
        /// <param name="dependencies">Dependencies to search for local repositories.</param>
        /// <returns>A list of found aar and srcaar files.</returns>
        public static List<string> FindAarsInLocalRepos(ICollection<Dependency> dependencies) {
            var libraries = new List<string>();
            foreach (var repo in FindLocalRepos(dependencies)) {
                libraries.AddRange(FindAars(repo));
            }
            return libraries;
        }

        /// <summary>
        /// Get a path without a filename extension.
        /// </summary>
        /// <param name="path">Path to a file.</param>
        /// <returns>Path (including directory) without a filename extension.</returns>
        internal static string PathWithoutExtension(string path) {
            return Path.Combine(Path.GetDirectoryName(path),
                                Path.GetFileNameWithoutExtension(path));
        }

        /// <summary>
        /// Search for the POM file associated with the specified maven artifact and patch the
        /// packaging reference if the POM doesn't reference the artifact.
        /// file.
        /// </summary>
        /// <param name="artifactFilename">artifactFilename</param>
        /// <param name="sourceFilename">If artifactFilename is copied from a different location,
        /// pass the original location where POM file lives.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool PatchPomFile(string artifactFilename, string sourceFilename) {
            if (sourceFilename == null) {
                sourceFilename = artifactFilename;
            }
            if (FileUtils.IsUnderPackageDirectory(artifactFilename)) {
                // File under Packages folder is immutable.
                PlayServicesResolver.Log(
                    String.Format("Cannot patch POM from Packages directory since it is immutable" +
                        " ({0})", artifactFilename), level: LogLevel.Error);
                return false;
            }

            var failureImpact = String.Format("{0} may not be included in your project",
                                              Path.GetFileName(artifactFilename));
            var pomFilename = PathWithoutExtension(artifactFilename) + ".pom";
            // Copy POM file if artifact has been copied from a different location as well.
            if (String.Compare(sourceFilename, artifactFilename) != 0 &&
                !File.Exists(pomFilename)) {
                var sourcePomFilename = PathWithoutExtension(sourceFilename) + ".pom";
                var error = PlayServicesResolver.CopyAssetAndLabel(
                        sourcePomFilename, pomFilename);
                if (!String.IsNullOrEmpty(error)) {
                    PlayServicesResolver.Log(
                            String.Format("Failed to copy POM from {0} to {1} due to:\n{2}",
                                    sourcePomFilename, pomFilename, error),
                            level: LogLevel.Error);
                    return false;
                }
            }
            var artifactPackaging = Path.GetExtension(artifactFilename).ToLower().Substring(1);
            var pom = new XmlDocument();
            try {
                using (var stream = new StreamReader(pomFilename)) {
                    pom.Load(stream);
                }
            } catch (Exception ex) {
                PlayServicesResolver.Log(
                    String.Format("Unable to read maven POM {0} for {1} ({2}). " + failureImpact,
                                  pom, artifactFilename, ex), level: LogLevel.Error);
                return false;
            }
            bool updatedPackaging = false;
            XmlNodeList packagingNode = pom.GetElementsByTagName("packaging");
            foreach (XmlNode node in packagingNode) {
                if (node.InnerText != artifactPackaging) {
                    PlayServicesResolver.Log(String.Format(
                        "Replacing packaging of maven POM {0} {1} --> {2}",
                        pomFilename, node.InnerText, artifactPackaging), level: LogLevel.Verbose);
                    node.InnerText = artifactPackaging;
                    updatedPackaging = true;
                }
            }
            if (!FileUtils.CheckoutFile(pomFilename, PlayServicesResolver.logger)) {
                PlayServicesResolver.Log(
                    String.Format("Unable to checkout '{0}' to patch the file for inclusion in a " +
                                  "Gradle project.", pomFilename), LogLevel.Error);
                return false;
            }
            if (updatedPackaging) {
                try {
                    using (var xmlWriter =
                           XmlWriter.Create(pomFilename,
                                            new XmlWriterSettings {
                                                Indent = true,
                                                IndentChars = "  ",
                                                NewLineChars = "\n",
                                                NewLineHandling = NewLineHandling.Replace
                                            })) {
                        pom.Save(xmlWriter);
                    }
                } catch (Exception ex) {
                    PlayServicesResolver.Log(
                        String.Format("Unable to write patch maven POM {0} for {1} with " +
                                      "packaging {2} ({3}). " + failureImpact,
                                      pom, artifactFilename, artifactPackaging, ex));
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Patch all POM files in the local repository with PatchPomFile().
        /// If a srcaar and an aar are present in the same directory the POM is patched with a
        /// reference to the aar.
        /// </summary>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool PatchPomFilesInLocalRepos(ICollection<Dependency> dependencies) {
            // Filename extensions by the basename of each file path.
            var extensionsByBasenames = new Dictionary<string, HashSet<string>>();
            foreach (var filename in FindAarsInLocalRepos(dependencies)) {
                // No need to patch POM under package folder.
                if (FileUtils.IsUnderPackageDirectory(filename)) {
                    continue;
                }
                var pathWithoutExtension = PathWithoutExtension(filename);
                HashSet<string> extensions;
                if (!extensionsByBasenames.TryGetValue(pathWithoutExtension, out extensions)) {
                    extensions = new HashSet<string>();
                    extensionsByBasenames[pathWithoutExtension] = extensions;
                }
                extensions.Add(Path.GetExtension(filename));
            }
            bool successful = true;
            var packagingPriority = new [] { ".aar", ".srcaar" };
            foreach (var kv in extensionsByBasenames) {
                string filePackagingToUse = "";
                foreach (var packaging in packagingPriority) {
                    bool foundFile = false;
                    foreach (var filenamePackaging in kv.Value) {
                        filePackagingToUse = filenamePackaging;
                        if (filenamePackaging.ToLower() == packaging) {
                            foundFile = true;
                            break;
                        }
                    }
                    if (foundFile) break;
                }
                var artifect = kv.Key + filePackagingToUse;
                successful &= PatchPomFile(artifect, artifect);
            }
            return successful;
        }
    }

}

// <copyright file="EmbeddedResource.cs" company="Google Inc.">
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

namespace Google {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// Methods to manage assembly embedded resources.
    /// </summary>
    internal static class EmbeddedResource {

        /// <summary>
        /// Extracted resource file information.
        /// </summary>
        private class ExtractedResource {

            /// <summary>
            /// Assembly that contains the resource.
            /// </summary>
            private Assembly assembly;

            /// <summary>
            /// Assembly modification time.
            /// </summary>
            private DateTime assemblyModificationTime;

            /// <summary>
            /// Name of the extracted resource.
            /// </summary>
            private string resourceName;

            /// <summary>
            /// Path to the extracted resource.
            /// </summary>
            private string path;

            /// <summary>
            /// Construct an object to track an extracted resource.
            /// </summary>
            /// <param name="assembly">Assembly the resource was extracted from.</param>
            /// <param name="assemblyModificationTime">Last time the source assembly file was
            /// modified.</param>
            /// <param name="resourceName">Name of the resource in the assembly.</param>
            /// <param name="path">Path of the extracted resource.</param>
            public ExtractedResource(Assembly assembly, DateTime assemblyModificationTime,
                                     string resourceName, string path) {
                this.assembly = assembly;
                this.assemblyModificationTime = assemblyModificationTime;
                this.resourceName = resourceName;
                this.path = path;
            }

            /// <summary>
            /// Name of the extracted resource.
            /// </summary>
            public string ResourceName { get { return resourceName; } }

            /// <summary>
            /// Path of the extracted file.
            /// </summary>
            public string Path { get { return path; } }

            /// <summary>
            /// Whether the extracted file is out of date.
            /// </summary>
            public bool OutOfDate {
                get {
                    // If the source assembly is newer than the extracted file, the extracted file
                    // is out of date.
                    return !File.Exists(path) ||
                        assemblyModificationTime.CompareTo(File.GetLastWriteTime(path)) > 0;
                }
            }

            /// <summary>
            /// Extract the embedded resource to the Path creating intermediate directories
            /// if they're required.
            /// </summary>
            /// <param name="logger">Logger to log messages to.</param>
            /// <returns>true if successful, false otherwise.</returns>
            public bool Extract(Logger logger) {
                if (OutOfDate) {
                    var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null) {
                        logger.Log(
                            String.Format("Failed to find resource {0} in assembly {1}",
                                          ResourceName, assembly.FullName),
                            level: LogLevel.Error);
                        return false;
                    }
                    var data = new byte[stream.Length];
                    stream.Read(data, 0, (int)stream.Length);
                    try {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(
                            FileUtils.NormalizePathSeparators(Path)));
                        File.WriteAllBytes(Path, data);
                    } catch (IOException error) {
                        logger.Log(
                            String.Format("Failed to write resource {0} from assembly {1} to {2} " +
                                          "({3})", ResourceName, assembly.FullName, Path, error),
                            level: LogLevel.Error);
                        return false;
                    }
                }
                return true;
            }
        }

        // Cache of extracted resources by path. This is used to avoid file operations when
        // checking to see whether resources have already been extracted or are out of date.
        private static Dictionary<string, ExtractedResource> extractedResourceByPath =
            new Dictionary<string, ExtractedResource>();

        /// <summary>
        /// Extract a list of embedded resources to the specified path creating intermediate
        /// directories if they're required.
        /// </summary>
        /// <param name="assembly">Assembly to extract resources from.</param>
        /// <param name="resourceNameToTargetPaths">Each Key is the resource to extract and each
        /// Value is the path to extract to. If the resource name (Key) is null or empty, this
        /// method will attempt to extract a resource matching the filename component of the path.
        /// </param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool ExtractResources(
                Assembly assembly,
                IEnumerable<KeyValuePair<string, string>> resourceNameToTargetPaths,
                Logger logger) {
            bool allResourcesExtracted = true;
            var assemblyModificationTime = File.GetLastWriteTime(assembly.Location);
            foreach (var resourceNameToTargetPath in resourceNameToTargetPaths) {
                var targetPath = FileUtils.PosixPathSeparators(
                    Path.GetFullPath(resourceNameToTargetPath.Value));
                var resourceName = resourceNameToTargetPath.Key;
                if (String.IsNullOrEmpty(resourceName)) {
                    resourceName = Path.GetFileName(FileUtils.NormalizePathSeparators(targetPath));
                }
                ExtractedResource resourceToExtract;
                if (!extractedResourceByPath.TryGetValue(targetPath, out resourceToExtract)) {
                    resourceToExtract = new ExtractedResource(assembly, assemblyModificationTime,
                                                              resourceName, targetPath);
                }
                if (resourceToExtract.Extract(logger)) {
                    extractedResourceByPath[targetPath] = resourceToExtract;
                } else {
                    allResourcesExtracted = false;
                }
            }
            return allResourcesExtracted;
        }
    }
}

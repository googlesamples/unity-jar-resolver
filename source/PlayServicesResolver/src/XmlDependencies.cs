// <copyright file="XmlDependencies.cs" company="Google Inc.">
// Copyright (C) 2017 Google Inc. All Rights Reserved.
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
    using System.Text.RegularExpressions;
    using Google.JarResolver;
    using UnityEditor;

    /// <summary>
    /// Parses XML declared dependencies required by a Unity plugin.
    /// </summary>
    internal class XmlDependencies {

        /// <summary>
        /// Set of regular expressions that match files which contain dependency
        /// specifications.
        /// </summary>
        internal HashSet<Regex> fileRegularExpressions = new HashSet<Regex> {
            new Regex(@".*[/\\]Editor[/\\].*Dependencies\.xml$")
        };

        /// <summary>
        /// Human readable name for dependency files managed by this class.
        /// </summary>
        protected string dependencyType = "dependencies";

        /// <summary>
        /// Find all XML declared dependency files.
        /// </summary>
        /// <returns>List of XML dependency filenames in the project.</returns>
        private List<string> FindFiles() {
            var matchingFiles = new List<string>();
            foreach (var assetGuid in AssetDatabase.FindAssets("t:Object")) {
                var dependencyFile = AssetDatabase.GUIDToAssetPath(assetGuid);
                foreach (var regex in fileRegularExpressions) {
                    if (regex.Match(dependencyFile).Success) {
                        matchingFiles.Add(dependencyFile);
                    }
                }
            }
            return matchingFiles;
        }

        /// <summary>
        /// Read XML declared dependencies.
        /// </summary>
        /// <param name="filename">File to read.</param>
        /// <param name="logger">Logging delegate.</param>
        /// <returns>true if the file was read successfully, false otherwise.</returns>
        protected virtual bool Read(string filename,
                                    PlayServicesSupport.LogMessageWithLevel logger) {
            return false;
        }

        /// <summary>
        /// Find and read all XML declared dependencies.
        /// </summary>
        /// <param name="logger">Logging delegate.</param>
        /// <returns>true if all files were read successfully, false otherwise.</returns>
        public virtual bool ReadAll(PlayServicesSupport.LogMessageWithLevel logger) {
            bool success = true;
            foreach (var filename in FindFiles()) {
                if (!Read(filename, logger)) {
                    logger(String.Format("Unable to read {0} from {1}.\n" +
                                         "{0} in this file will be ignored.", dependencyType,
                                         filename),
                           level: PlayServicesSupport.LogLevel.Error);
                    success = false;
                }
            }
            return success;
        }
    }
}

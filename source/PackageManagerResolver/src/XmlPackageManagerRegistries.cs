// <copyright file="XmlRepositories.cs" company="Google Inc.">
// Copyright (C) 2020 Google Inc. All Rights Reserved.
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
    using System.Text.RegularExpressions;
    using System.Xml;
    using Google;
    using UnityEditor;

    /// <summary>
    /// Parses XML declared Unity Package Manager (UPM) registries required by a Unity plugin into
    /// the XmlRegistries.Registry class.
    /// </summary>
    internal class XmlPackageManagerRegistries {

        /// <summary>
        /// Set of regular expressions that match files which contain dependency
        /// specifications.
        /// </summary>
        internal static readonly HashSet<Regex> fileRegularExpressions = new HashSet<Regex> {
            new Regex(@".*[/\\]Editor[/\\].*Registries\.xml$")
        };

        /// <summary>
        /// Paths to search for files.
        /// </summary>
        internal static readonly string[] fileSearchPaths = new string[] { "Assets", "Packages"};

        /// <summary>
        /// Label applied to registries.
        /// </summary>
        internal static readonly string REGISTRIES_LABEL = "gumpr_registries";

        /// <summary>
        /// Asset managed by this module.
        /// </summary>
        private static readonly string UPM_REGISTRIES = "Package Manager Registries";

        /// <summary>
        /// Registries read from files indexed by URL.
        /// </summary>
        internal Dictionary<string, PackageManagerRegistry> Registries;

        /// <summary>
        /// Construct an empty XML UPM registries reader.
        /// </summary>
        public XmlPackageManagerRegistries() { Clear(); }

        /// <summary>
        /// Clear the cached registries.
        /// </summary>
        internal void Clear() {
            Registries = new Dictionary<string, PackageManagerRegistry>();
        }

        /// <summary>
        /// Determines whether a filename matches an XML registries file.
        /// </summary>
        /// <param name="filename">Filename to check.</param>
        /// <returns>true if it is a match, false otherwise.</returns>
        internal static bool IsRegistriesFile(string filename) {
            foreach (var regex in fileRegularExpressions) {
                if (regex.Match(filename).Success) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Find all XML declared registries files.
        /// </summary>
        /// <returns>Set of XML registries filenames in the project.</returns>
        private IEnumerable<string> FindFiles() {
            var foundFiles = new HashSet<string>(
                VersionHandlerImpl.SearchAssetDatabase(
                   assetsFilter: "Registries t:TextAsset",
                   filter: IsRegistriesFile,
                   directories: fileSearchPaths));
            foundFiles.UnionWith(VersionHandlerImpl.SearchAssetDatabase(
                   assetsFilter: "l:" + REGISTRIES_LABEL,
                   directories: fileSearchPaths));
            return foundFiles;
        }

        /// <summary>
        /// Read XML declared registries.
        /// </summary>
        /// <param name="filename">File to read.</param>
        /// <param name="logger">Logger class.</param>
        /// <returns>true if the file was read successfully, false otherwise.</returns>
        internal bool Read(string filename, Logger logger) {
            PackageManagerRegistry upmRegistry = null;
            logger.Log(String.Format("Reading {0} XML file {1}", UPM_REGISTRIES, filename),
                       level: LogLevel.Verbose);
            if (!XmlUtilities.ParseXmlTextFileElements(
                filename, logger,
                (reader, elementName, isStart, parentElementName, elementNameStack) => {
                    if (elementName == "registries" && parentElementName == "") {
                        return true;
                    } else if (elementName == "registry" &&
                               parentElementName == "registries" &&
                               isStart) {
                        upmRegistry = new PackageManagerRegistry() {
                            Name = reader.GetAttribute("name") ?? "",
                            Url = reader.GetAttribute("url") ?? "",
                            TermsOfService = reader.GetAttribute("termsOfService") ?? "",
                            PrivacyPolicy = reader.GetAttribute("privacyPolicy") ?? "",
                            CreatedBy = String.Format("{0}:{1}", filename,
                                                      reader.LineNumber)
                        };
                        return true;
                    } else if (elementName == "scopes" &&
                               parentElementName == "registry") {
                        if (isStart) upmRegistry.Scopes = new List<string>();
                        return true;
                    } else if (elementName == "scope" &&
                               parentElementName == "scopes" &&
                               !(String.IsNullOrEmpty(upmRegistry.Name) ||
                                 String.IsNullOrEmpty(upmRegistry.Url))) {
                        if (isStart && reader.Read() && reader.NodeType == XmlNodeType.Text) {
                            upmRegistry.Scopes.Add(reader.ReadContentAsString());
                        }
                        return true;
                    } else if (elementName == "registry" &&
                               parentElementName == "registries" &&
                               !isStart) {
                        if (!(String.IsNullOrEmpty(upmRegistry.Name) ||
                              String.IsNullOrEmpty(upmRegistry.Url) ||
                              upmRegistry.Scopes.Count == 0)) {
                            PackageManagerRegistry existingRegistry;
                            if (!Registries.TryGetValue(upmRegistry.Url, out existingRegistry)) {
                                Registries[upmRegistry.Url] = upmRegistry;
                            } else if (!existingRegistry.Equals(upmRegistry)) {
                                logger.Log(
                                    String.Format(
                                        "{0} for URL '{1}' called '{2}' was already read " +
                                        "from '{3}'.\n" +
                                        "{0} from '{4}' will be ignored.",
                                        UPM_REGISTRIES, upmRegistry.Url, upmRegistry.Name,
                                        existingRegistry.CreatedBy, filename),
                                    level: LogLevel.Warning);
                            }
                        } else {
                            logger.Log(
                                String.Format(
                                    "Malformed {0} for registry {1} " +
                                    "found in {2}.\n" +
                                    "All {0} will be ignored in {2}.",
                                    UPM_REGISTRIES, upmRegistry.ToString(), filename),
                                level: LogLevel.Error);
                            return false;
                        }
                        return true;
                    }
                    return false;
                })) {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Find and read all XML declared registries.
        /// </summary>
        /// <param name="logger">Logger class.</param>
        /// <returns>true if all files were read successfully, false otherwise.</returns>
        public bool ReadAll(Logger logger) {
            bool success = true;
            Clear();
            foreach (var filename in FindFiles()) {
                if (!Read(filename, logger)) {
                    logger.Log(String.Format("Unable to read {0} from {1}.\n" +
                                             "{0} in this file will be ignored.",
                                             UPM_REGISTRIES, filename),
                               level: LogLevel.Error);
                    success = false;
                }
            }
            return success;
        }
    }
}



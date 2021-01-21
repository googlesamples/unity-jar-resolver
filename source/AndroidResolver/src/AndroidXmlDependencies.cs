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
    using System.Xml;
    using Google;
    using Google.JarResolver;
    using UnityEditor;

    /// <summary>
    /// Parses XML declared dependencies required by the Android Resolver Unity plugin.
    /// </summary>
    internal class AndroidXmlDependencies : XmlDependencies {
        internal PlayServicesSupport svcSupport = null;

        public AndroidXmlDependencies() {
            dependencyType = "Android dependencies";
        }

        /// <summary>
        /// Read XML declared dependencies.
        /// </summary>
        /// <param name="filename">File to read.</param>
        /// <param name="logger">Logger instance to log with.</param>
        ///
        /// Parses dependencies in the form:
        ///
        /// <dependencies>
        ///   <androidPackages>
        ///     <androidPackage spec="some:package:1.2.3">
        ///       <androidSdkPackageIds>
        ///         <androidSdkPackageId>androidPackageManagerPackageId</androidSdkPackageId>
        ///       </androidSdkPackageIds>
        ///       <repositories>
        ///         <repository>uriToRepositoryToSearchForPackage</repository>
        ///       </repositories>
        ///     </androidPackage>
        ///   </androidPackages>
        /// </dependencies>
        protected override bool Read(string filename, Logger logger) {
            List<string> androidSdkPackageIds = null;
            string group = null;
            string artifact = null;
            string versionSpec = null;
            string classifier = null;
            List<string> repositories = null;
            logger.Log(
                String.Format("Reading Android dependency XML file {0}", filename),
                level: LogLevel.Verbose);

            if (!XmlUtilities.ParseXmlTextFileElements(
                filename, logger,
                (reader, elementName, isStart, parentElementName, elementNameStack) => {
                    if (elementName == "dependencies" && parentElementName == "") {
                        return true;
                    } else if (elementName == "androidPackages" &&
                               (parentElementName == "dependencies" ||
                                parentElementName == "")) {
                        return true;
                    } else if (elementName == "androidPackage" &&
                               parentElementName == "androidPackages") {
                        if (isStart) {
                            androidSdkPackageIds = new List<string>();
                            group = null;
                            artifact = null;
                            versionSpec = null;
                            classifier = null;
                            repositories = new List<string>();
                            // Parse a package specification in the form:
                            // group:artifact:version_spec
                            // (or)
                            // group:artifact:version_spec:classifier
                            var spec = reader.GetAttribute("spec") ?? "";
                            var specComponents = spec.Split(new [] { ':' });
                            if (specComponents.Length != 3 && specComponents.Length != 4) {
                                logger.Log(
                                    String.Format(
                                        "Ignoring invalid package specification '{0}' " +
                                        "while reading {1}:{2}\n",
                                        spec, filename, reader.LineNumber),
                                    level: LogLevel.Warning);
                                return false;
                            }
                            group = specComponents[0];
                            artifact = specComponents[1];
                            versionSpec = specComponents[2];
                            if (specComponents.Length == 4)
                                classifier = specComponents[3];

                            return true;
                        } else if (!(String.IsNullOrEmpty(group) ||
                                     String.IsNullOrEmpty(artifact) ||
                                     String.IsNullOrEmpty(versionSpec)
                                     )) {
                            svcSupport.DependOn(group, artifact, versionSpec, classifier: classifier,
                                                packageIds: androidSdkPackageIds.ToArray(),
                                                repositories: repositories.ToArray(),
                                                createdBy: String.Format("{0}:{1}", filename,
                                                                         reader.LineNumber));
                        }
                    } else if (elementName == "androidSdkPackageIds" &&
                               parentElementName == "androidPackage") {
                        return true;
                    } else if (elementName == "androidSdkPackageId" &&
                               parentElementName == "androidSdkPackageIds") {
                        // Parse package manager ID associated with this package.
                        if (isStart && reader.Read() && reader.NodeType == XmlNodeType.Text) {
                            androidSdkPackageIds.Add(reader.ReadContentAsString());
                        }
                        return true;
                    } else if (elementName == "repositories" &&
                               parentElementName == "androidPackage") {
                        return true;
                    } else if (elementName == "repositories" &&
                               parentElementName == "androidPackages") {
                        if (isStart) {
                            repositories = new List<string>();
                        } else {
                            foreach (var repo in repositories) {
                                PlayServicesSupport.AdditionalRepositoryPaths.Add(
                                    new KeyValuePair<string, string>(
                                        repo, String.Format("{0}:{1}", filename,
                                                            reader.LineNumber)));
                            }
                        }
                        return true;
                    } else if (elementName == "repository" &&
                               parentElementName == "repositories") {
                        if (isStart && reader.Read() && reader.NodeType == XmlNodeType.Text) {
                            repositories.Add(reader.ReadContentAsString());
                        }
                        return true;
                    }
                    // Ignore unknown tags so that different configurations can be stored in the
                    // same file.
                    return true;
                })) {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Find and read all XML declared dependencies.
        /// </summary>
        /// <param name="logger">Logger to log with.</param>
        public override bool ReadAll(Logger logger) {
            const string XML_DEPENDENCIES_INSTANCE = "InternalXmlDependencies";
            if (PlayServicesSupport.instances.TryGetValue(XML_DEPENDENCIES_INSTANCE,
                                                          out svcSupport)) {
                svcSupport.ClearDependencies();
            } else {
                svcSupport = PlayServicesSupport.CreateInstance(
                    XML_DEPENDENCIES_INSTANCE, PlayServicesResolver.AndroidSdkRoot,
                    "ProjectSettings", logMessageWithLevel: PlayServicesResolver.LogDelegate);
            }
            return base.ReadAll(logger);
        }
    }
}

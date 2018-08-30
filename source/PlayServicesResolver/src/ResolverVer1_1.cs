// <copyright file="ResolverVer1_1.cs" company="Google Inc.">
// Copyright (C) 2015 Google Inc. All Rights Reserved.
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

namespace GooglePlayServices
{
    using UnityEngine;
    using UnityEditor;
    using System.Collections.Generic;
    using Google;
    using Google.JarResolver;
    using System;
    using System.Collections;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Xml;

    public class ResolverVer1_1 : DefaultResolver
    {
        // Caches data associated with an aar so that it doesn't need to be queried to determine
        // whether it should be expanded / exploded if it hasn't changed.
        private class AarExplodeData
        {
            // Identifier for an ABI independent AAR.
            public const string ABI_UNIVERSAL = "universal";
            // Time the file was modified the last time it was inspected.
            public System.DateTime modificationTime;
            // Whether the AAR file should be expanded / exploded.
            public bool explode = false;
            // Project's bundle ID when this was expanded.
            public string bundleId = "";
            // Path of the target AAR package.
            public string path = "";
            // Comma separated string that lists the set of *available* ABIs in the source archive.
            // This is ABI_UNIVERSAL if the archive does not contain any native libraries.
            public string availableAbis = ABI_UNIVERSAL;
            // Comma separated string that lists the set of ABIs in the archive.
            // This is ABI_UNIVERSAL if the archive does not contain any native libraries.
            public string targetAbis = ABI_UNIVERSAL;
            // Whether gradle is selected as the build system.
            public bool gradleBuildSystem = PlayServicesResolver.GradleBuildEnabled;
            // Whether gradle export is enabeld.
            public bool gradleExport = PlayServicesResolver.GradleProjectExportEnabled;
            // AAR version that should be ignored when attempting to overwrite an existing
            // dependency.  This is reset when the dependency is updated to a version different
            // to this.
            // NOTE: This is not considered in AarExplodeDataIsDirty() as we do not want to
            // re-explode an AAR if this changes.
            public string ignoredVersion = "";

            /// <summary>
            /// Convert a comma separated list of ABIs to an AndroidAbis instance.
            /// </summary>
            /// <param name="abiString">String to convert.</param>
            /// <returns>AndroidAbis instance if native components are present,
            /// null otherwise.</returns>
            private static AndroidAbis AndroidAbisFromString(string abiString) {
                return abiString == ABI_UNIVERSAL ? null : new AndroidAbis(abiString);
            }

            /// <summary>
            /// Convert an AndroidAbis instance to a comma separated string.
            /// </summary>
            /// <param name="abis">Instance to convert.</param>
            /// <returns>Comma separated string.</returns>
            private static string AndroidAbisToString(AndroidAbis abis) {
                return abis != null ? abis.ToString() : ABI_UNIVERSAL;
            }

            /// <summary>
            /// Get the available native component ABIs in the archive.
            /// If this is a universal archive it returns null.
            /// </summary>
            public AndroidAbis AvailableAbis {
                get { return AndroidAbisFromString(availableAbis); }
                set { availableAbis = AndroidAbisToString(value); }
            }

            /// <summary>
            /// Get the current native component ABIs in the archive.
            /// If this is a universal archive it returns null.
            /// </summary>
            public AndroidAbis TargetAbis {
                get { return AndroidAbisFromString(targetAbis); }
                set { targetAbis = AndroidAbisToString(value); }
            }

            /// <summary>
            /// Default constructor.
            /// </summary>
            public AarExplodeData() {}

            /// <summary>
            /// Copy an instance of this object.
            /// </summary>
            public AarExplodeData(AarExplodeData dataToCopy) {
                modificationTime = dataToCopy.modificationTime;
                explode = dataToCopy.explode;
                bundleId = dataToCopy.bundleId;
                path = dataToCopy.path;
                targetAbis = dataToCopy.targetAbis;
                gradleBuildSystem = dataToCopy.gradleBuildSystem;
                gradleExport = dataToCopy.gradleExport;
                ignoredVersion = dataToCopy.ignoredVersion;
            }

            /// <summary>
            /// Compare with this object.
            /// </summary>
            /// <param name="obj">Object to compare with.</param>
            /// <returns>true if both objects have the same contents, false otherwise.</returns>
            public override bool Equals(System.Object obj)  {
                var data = obj as AarExplodeData;
                return data != null &&
                    modificationTime == data.modificationTime &&
                    explode == data.explode &&
                    bundleId == data.bundleId &&
                    path == data.path &&
                    availableAbis == data.availableAbis &&
                    targetAbis == data.targetAbis &&
                    gradleBuildSystem == data.gradleBuildSystem &&
                    gradleExport == data.gradleExport &&
                    ignoredVersion == data.ignoredVersion;
            }

            /// <summary>
            /// Generate a hash of this object.
            /// </summary>
            /// <returns></returns>
            public override int GetHashCode() {
                return modificationTime.GetHashCode() ^
                    explode.GetHashCode() ^
                    bundleId.GetHashCode() ^
                    path.GetHashCode() ^
                    availableAbis.GetHashCode() ^
                    targetAbis.GetHashCode() ^
                    gradleBuildSystem.GetHashCode() ^
                    gradleExport.GetHashCode() ^
                    ignoredVersion.GetHashCode();
            }

            /// <summary>
            /// Copy AAR explode data.
            /// </summary>
            public static Dictionary<string, AarExplodeData> CopyDictionary(
                    Dictionary<string, AarExplodeData> dataToCopy) {
                var copy = new Dictionary<string, AarExplodeData>();
                foreach (var item in dataToCopy) {
                    copy[item.Key] = new AarExplodeData(item.Value);
                }
                return copy;
            }

            /// <summary>
            /// Convert AAR data to a string.
            /// </summary>
            /// <returns>String description of the instance.</returns>
            public override string ToString() {
                return String.Format("modificationTime={0} " +
                                     "explode={1} " +
                                     "bundleId={2} " +
                                     "path={3} " +
                                     "availableAbis={4} " +
                                     "targetAbis={5} " +
                                     "gradleBuildSystem={6} " +
                                     "gradleExport={7}",
                                     modificationTime, explode, bundleId, path, availableAbis,
                                     targetAbis, gradleBuildSystem, gradleExport);
            }
        }

        // Data that should be stored in the explode cache.
        private Dictionary<string, AarExplodeData> aarExplodeData =
            new Dictionary<string, AarExplodeData>();
        // Data currently stored in the explode cache.
        private Dictionary<string, AarExplodeData> aarExplodeDataSaved =
            new Dictionary<string, AarExplodeData>();
        // File used to to serialize aarExplodeData.  This is required as Unity will reload classes
        // in the editor when C# files are modified.
        private string aarExplodeDataFile = Path.Combine("Temp", "GoogleAarExplodeCache.xml");

        // Directory used to execute Gradle.
        private string gradleBuildDirectory = Path.Combine("Temp", "PlayServicesResolverGradle");

        private const int MajorVersion = 1;
        private const int MinorVersion = 1;
        private const int PointVersion = 0;

        // Characters that are parsed by Gradle / Java in property values.
        // These characters need to be escaped to be correctly interpreted in a property value.
        private static string[] GradlePropertySpecialCharacters = new string[] {
            " ", "\\", "#", "!", "=", ":"
        };

        // Special characters that should not be escaped in URIs for Gradle property values.
        private static HashSet<string> GradleUriExcludeEscapeCharacters = new HashSet<string> {
            ":"
        };

        // Queue of System.Action objects for resolve actions to execute on the main thread.
        private static System.Collections.Queue resolveUpdateQueue = new System.Collections.Queue();
        // Currently active resolution operation.
        private static System.Action resolveActionActive = null;
        // Lock for resolveUpdateQueue and resolveActionActive.
        private static object resolveLock = new object();

        public ResolverVer1_1() {
            LoadAarExplodeCache();
        }

        /// <summary>
        /// Compare two dictionaries of AarExplodeData.
        /// </summary>
        private bool CompareExplodeData(Dictionary<string, AarExplodeData> explodeData1,
                                        Dictionary<string, AarExplodeData> explodeData2) {
            if (explodeData1 == explodeData2) return true;
            if (explodeData1 == null || explodeData2 == null) return false;
            if (explodeData1.Count != explodeData2.Count) return false;
            var keys = new HashSet<string>(explodeData1.Keys);
            keys.UnionWith(new HashSet<string>(explodeData2.Keys));
            foreach (var key in keys) {
                AarExplodeData data1;
                AarExplodeData data2;
                if (!(explodeData1.TryGetValue(key, out data1) &&
                      explodeData2.TryGetValue(key, out data2))) {
                    return false;
                }
                if (!data1.Equals(data2)) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Load data cached in aarExplodeDataFile into aarExplodeData.
        /// </summary>
        private void LoadAarExplodeCache() {
            if (!File.Exists(aarExplodeDataFile)) {
                // Build aarExplodeData from the current set of AARs in the project.
                foreach (string path in PlayServicesResolver.FindLabeledAssets()) {
                    PlayServicesResolver.Log(String.Format("Caching AAR {0} state",
                                                           path), LogLevel.Verbose);
                    ShouldExplode(path);
                }
                return;
            }

            try {
                using (XmlTextReader reader =
                           new XmlTextReader(new StreamReader(aarExplodeDataFile))) {
                    aarExplodeData.Clear();
                    while (reader.Read()) {
                        if (reader.NodeType == XmlNodeType.Element && reader.Name == "aars") {
                            while (reader.Read()) {
                                if (reader.NodeType == XmlNodeType.Element &&
                                    reader.Name == "explodeData") {
                                    string aar = "";
                                    AarExplodeData aarData = new AarExplodeData();
                                    do {
                                        if (!reader.Read()) break;

                                        if (reader.NodeType == XmlNodeType.Element) {
                                            string elementName = reader.Name;
                                            if (reader.Read() &&
                                                reader.NodeType == XmlNodeType.Text) {
                                                if (elementName == "aar") {
                                                    aar = reader.ReadContentAsString();
                                                } else if (elementName == "modificationTime") {
                                                    aarData.modificationTime =
                                                        reader.ReadContentAsDateTime();
                                                } else if (elementName == "explode") {
                                                    aarData.explode = reader.ReadContentAsBoolean();
                                                } else if (elementName == "bundleId") {
                                                    aarData.bundleId = reader.ReadContentAsString();
                                                } else if (elementName == "path") {
                                                    aarData.path = reader.ReadContentAsString();
                                                } else if (elementName == "availableAbis") {
                                                    aarData.availableAbis =
                                                        reader.ReadContentAsString();
                                                } else if (elementName == "targetAbi") {
                                                    aarData.targetAbis =
                                                        reader.ReadContentAsString();
                                                } else if (elementName == "gradleBuildSystem") {
                                                    aarData.gradleBuildSystem =
                                                        reader.ReadContentAsBoolean();
                                                } else if (elementName == "gradleExport") {
                                                    aarData.gradleExport =
                                                        reader.ReadContentAsBoolean();
                                                } else if (elementName == "ignoredVersion") {
                                                    aarData.ignoredVersion =
                                                        reader.ReadContentAsString();
                                                }
                                            }
                                        }
                                    } while (!(reader.Name == "explodeData" &&
                                               reader.NodeType == XmlNodeType.EndElement));
                                    if (aar != "" && aarData.path != "") {
                                        aarExplodeData[aar] = aarData;
                                    }
                                }
                            }
                        }
                    }
                    reader.Close();
                }
            } catch (Exception e) {
                PlayServicesResolver.Log(String.Format(
                    "Failed to read AAR cache {0} ({1})\n" +
                    "Auto-resolution will be slower.\n", aarExplodeDataFile, e.ToString()),
                    level: LogLevel.Warning);
            }
            aarExplodeDataSaved = AarExplodeData.CopyDictionary(aarExplodeData);
        }

        /// <summary>
        /// Save data from aarExplodeData into aarExplodeDataFile.
        /// </summary>
        private void SaveAarExplodeCache()
        {
            try {
                if (File.Exists(aarExplodeDataFile))
                {
                    // If the explode data hasn't been modified, don't save.
                    if (CompareExplodeData(aarExplodeData, aarExplodeDataSaved)) return;
                    File.Delete(aarExplodeDataFile);
                }
                using (XmlTextWriter writer =
                       new XmlTextWriter(new StreamWriter(aarExplodeDataFile)) {
                           Formatting = Formatting.Indented
                       }) {
                    writer.WriteStartElement("aars");
                    foreach (KeyValuePair<string, AarExplodeData> kv in aarExplodeData)
                    {
                        writer.WriteStartElement("explodeData");
                        writer.WriteStartElement("aar");
                        writer.WriteValue(kv.Key);
                        writer.WriteEndElement();
                        writer.WriteStartElement("modificationTime");
                        writer.WriteValue(kv.Value.modificationTime);
                        writer.WriteEndElement();
                        writer.WriteStartElement("explode");
                        writer.WriteValue(kv.Value.explode);
                        writer.WriteEndElement();
                        writer.WriteStartElement("bundleId");
                        writer.WriteValue(UnityCompat.ApplicationId);
                        writer.WriteEndElement();
                        writer.WriteStartElement("path");
                        writer.WriteValue(kv.Value.path);
                        writer.WriteEndElement();
                        writer.WriteStartElement("availableAbis");
                        writer.WriteValue(kv.Value.availableAbis);
                        writer.WriteEndElement();
                        writer.WriteStartElement("targetAbi");
                        writer.WriteValue(kv.Value.targetAbis);
                        writer.WriteEndElement();
                        writer.WriteStartElement("gradleBuildEnabled");
                        writer.WriteValue(kv.Value.gradleBuildSystem);
                        writer.WriteEndElement();
                        writer.WriteStartElement("gradleExport");
                        writer.WriteValue(kv.Value.gradleExport);
                        writer.WriteEndElement();
                        writer.WriteStartElement("ignoredVersion");
                        writer.WriteValue(kv.Value.ignoredVersion);
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.Flush();
                    writer.Close();
                    aarExplodeDataSaved = AarExplodeData.CopyDictionary(aarExplodeData);
                }
            } catch (Exception e) {
                PlayServicesResolver.Log(String.Format(
                    "Failed to write AAR cache {0} ({1})\n" +
                    "Auto-resolution will be slower after recompilation.\n", aarExplodeDataFile,
                    e.ToString()), level: LogLevel.Warning);
            }
        }

        #region IResolver implementation

        /// <summary>
        /// Version of the resolver. - 1.1.0
        /// </summary>
        /// <remarks>The resolver with the greatest version is used when resolving.
        /// The value of the verison is calcuated using MakeVersion in DefaultResolver</remarks>
        /// <seealso cref="DefaultResolver.MakeVersionNumber"></seealso>
        public override int Version()
        {
            return MakeVersionNumber(MajorVersion, MinorVersion, PointVersion);
        }

        /// <summary>
        /// Parse output of download_artifacts.gradle into lists of copied and missing artifacts.
        /// </summary>
        /// <param name="output">Standard output of the download_artifacts.gradle.</param>
        /// <param name="destinationDirectory">Directory to artifacts were copied into.</param>
        /// <param name="copiedArtifacts">Returns a list of copied artifact files.</param>
        /// <param name="missingArtifacts">Returns a list of missing artifact
        /// specifications.</param>
        /// <param name="modifiedArtifacts">Returns a list of artifact specifications that were
        /// modified.</param>
        private void ParseDownloadGradleArtifactsGradleOutput(
                string output, string destinationDirectory,
                out List<string> copiedArtifacts, out List<string> missingArtifacts,
                out List<string> modifiedArtifacts) {
            // Parse stdout for copied and missing artifacts.
            copiedArtifacts = new List<string>();
            missingArtifacts = new List<string>();
            modifiedArtifacts = new List<string>();
            string currentHeader = null;
            const string COPIED_ARTIFACTS_HEADER = "Copied artifacts:";
            const string MISSING_ARTIFACTS_HEADER = "Missing artifacts:";
            const string MODIFIED_ARTIFACTS_HEADER = "Modified artifacts:";
            foreach (var line in output.Split(new string[] { "\r\n", "\n" },
                                              StringSplitOptions.None)) {
                if (line.StartsWith(COPIED_ARTIFACTS_HEADER) ||
                    line.StartsWith(MISSING_ARTIFACTS_HEADER) ||
                    line.StartsWith(MODIFIED_ARTIFACTS_HEADER)) {
                    currentHeader = line;
                    continue;
                } else if (String.IsNullOrEmpty(line.Trim())) {
                    currentHeader = null;
                    continue;
                }
                if (!String.IsNullOrEmpty(currentHeader)) {
                    if (currentHeader == COPIED_ARTIFACTS_HEADER) {
                        // Store the POSIX path of the copied package to handle Windows
                        // path variants.
                        copiedArtifacts.Add(Path.Combine(destinationDirectory,
                                                         line.Trim()).Replace("\\", "/"));
                    } else if (currentHeader == MISSING_ARTIFACTS_HEADER) {
                        missingArtifacts.Add(line.Trim());
                    } else if (currentHeader == MODIFIED_ARTIFACTS_HEADER) {
                        modifiedArtifacts.Add(line.Trim());
                    }
                }
            }
        }

        /// <summary>
        /// Log an error with the set of dependencies that were not fetched.
        /// </summary>
        /// <param name="missingArtifacts">List of missing dependencies.</param>
        private void LogMissingDependenciesError(List<string> missingArtifacts) {
            // Log error for missing packages.
            if (missingArtifacts.Count > 0) {
                PlayServicesResolver.Log(
                   String.Format("Resolution failed\n\n" +
                                 "Failed to fetch the following dependencies:\n{0}\n\n",
                                 String.Join("\n", missingArtifacts.ToArray())),
                   level: LogLevel.Error);
            }
        }

        /// <summary>
        /// Escape all special characters in a gradle property value.
        /// </summary>
        /// <param name="value">Value to escape.</param>
        /// <param name="escapeFunc">Function which generates an escaped character.  By default
        /// this adds "\\" to each escaped character.</param>
        /// <param name="charactersToExclude">Characters to exclude from the escaping set.</param>
        /// <returns>Escaped value.</returns>
        private static string EscapeGradlePropertyValue(
                string value, Func<string, string> escapeFunc = null,
                HashSet<string> charactersToExclude = null) {
            if (escapeFunc == null) {
                escapeFunc = (characterToEscape) => { return "\\" + characterToEscape; };
            }
            foreach (var characterToEscape in GradlePropertySpecialCharacters) {
                if (charactersToExclude == null ||
                    !(charactersToExclude.Contains(characterToEscape))) {
                    value = value.Replace(characterToEscape, escapeFunc(characterToEscape));
                }
            }
            return value;
        }

        /// <summary>
        /// Generates a Gradle (Java) properties string from a dictionary of key value pairs.
        /// Details of the format is documented in
        /// http://docs.oracle.com/javase/7/docs/api/java/util/Properties.html#\
        /// store%28java.io.Writer,%20java.lang.String%29
        /// </summary>
        /// <param name="properties">Properties to generate a string from.  Each value must not
        /// contain a newline.</param>
        /// <returns>String with Gradle (Java) properties</returns>
        private string GenerateGradleProperties(Dictionary<string, string> properties) {
            var lines = new List<string>();
            foreach (var kv in properties) {
                var escapedKey = kv.Key.Replace(" ", "\\ ");
                var elementAfterLeadingWhitespace = kv.Value.TrimStart(new [] { ' ' });
                var escapedElement =
                    kv.Value.Substring(elementAfterLeadingWhitespace.Length).Replace(" ", "\\ ") +
                    EscapeGradlePropertyValue(elementAfterLeadingWhitespace);
                lines.Add(String.Format("{0}={1}", escapedKey, escapedElement));
            }
            return String.Join("\n", lines.ToArray());
        }

        /// <summary>
        /// Perform resolution using Gradle.
        /// </summary>
        /// <param name="destinationDirectory">Directory to copy packages into.</param>
        /// <param name="androidSdkPath">Path to the Android SDK.  This is required as
        /// PlayServicesSupport.SDK can only be called from the main thread.</param>
        /// <param name="logErrorOnMissingArtifacts">Log errors when artifacts are missing.</param>
        /// <param name="resolutionComplete">Called when resolution is complete with a list of
        /// packages that were not found.</param>
        private void GradleResolution(
                string destinationDirectory, string androidSdkPath,
                bool logErrorOnMissingArtifacts,
                System.Action<List<Dependency>> resolutionComplete) {
            var gradleWrapper = Path.GetFullPath(Path.Combine(
                gradleBuildDirectory,
                UnityEngine.RuntimePlatform.WindowsEditor == UnityEngine.Application.platform ?
                    "gradlew.bat" : "gradlew"));
            var buildScript = Path.GetFullPath(Path.Combine(
                gradleBuildDirectory, EMBEDDED_RESOURCES_NAMESPACE + "download_artifacts.gradle"));
            // Get all dependencies.
            var allDependencies = PlayServicesSupport.GetAllDependencies();
            var allDependenciesList = new List<Dependency>(allDependencies.Values);

            // Extract Gradle wrapper and the build script to the build directory.
            if (!(Directory.Exists(gradleBuildDirectory) && File.Exists(gradleWrapper) &&
                  File.Exists(buildScript))) {
                var gradleTemplateZip = Path.Combine(
                    gradleBuildDirectory, EMBEDDED_RESOURCES_NAMESPACE + "gradle-template.zip");
                foreach (var resource in new [] { gradleTemplateZip, buildScript }) {
                    ExtractResource(Path.GetFileName(resource), resource);
                }
                if (!PlayServicesResolver.ExtractZip(gradleTemplateZip, new [] {
                            "gradle/wrapper/gradle-wrapper.jar",
                            "gradle/wrapper/gradle-wrapper.properties",
                            "gradlew",
                            "gradlew.bat"}, gradleBuildDirectory)) {
                    PlayServicesResolver.Log(
                       String.Format("Unable to extract Gradle build component {0}\n\n" +
                                     "Resolution failed.", gradleTemplateZip),
                       level: LogLevel.Error);
                    resolutionComplete(allDependenciesList);
                    return;
                }
                // Files extracted from the zip file don't have the executable bit set on some
                // platforms, so set it here.
                // Unfortunately, File.GetAccessControl() isn't implemented, so we'll use
                // chmod (OSX / Linux) and on Windows extracted files are executable by default
                // so we do nothing.
                if (UnityEngine.RuntimePlatform.WindowsEditor !=
                    UnityEngine.Application.platform) {
                    var result = CommandLine.Run("chmod",
                                                 String.Format("ug+x \"{0}\"", gradleWrapper));
                    if (result.exitCode != 0) {
                        PlayServicesResolver.Log(
                            String.Format("Failed to make \"{0}\" executable.\n\n" +
                                          "Resolution failed.\n\n{1}", gradleWrapper,
                                          result.message),
                            level: LogLevel.Error);
                        resolutionComplete(allDependenciesList);
                        return;
                    }
                }
            }

            // Build array of repos to search, they're interleaved across all dependencies as the
            // order matters.
            int maxNumberOfRepos = 0;
            foreach (var dependency in allDependencies.Values) {
                maxNumberOfRepos = Math.Max(maxNumberOfRepos, dependency.Repositories.Length);
            }
            var repoSet = new HashSet<string>();
            var repoList = new List<string>();
            for (int i = 0; i < maxNumberOfRepos; i++) {
                foreach (var dependency in allDependencies.Values) {
                    var repos = dependency.Repositories;
                    if (i >= repos.Length) continue;
                    var repo = repos[i];
                    // Filter Android SDK repos as they're supplied in the build script.
                    if (repo.StartsWith(PlayServicesSupport.SdkVariable)) continue;
                    // Since we need a URL, determine whether the repo has a scheme.  If not,
                    // assume it's a local file.
                    bool validScheme = false;
                    foreach (var scheme in new [] { "file:", "http:", "https:" }) {
                        validScheme |= repo.StartsWith(scheme);
                    }
                    if (!validScheme) {
                        repo = "file:///" + Path.GetFullPath(repo).Replace("\\", "/");
                    }
                    // Escape the URI to handle special characters like spaces and percent escape
                    // all characters that are interpreted by gradle.
                    repo = EscapeGradlePropertyValue(
                        Uri.EscapeUriString(repo),
                        escapeFunc: Uri.EscapeDataString,
                        charactersToExclude: GradleUriExcludeEscapeCharacters);
                    if (repoSet.Contains(repo)) continue;
                    repoSet.Add(repo);
                    repoList.Add(repo);
                }
            }

            // Executed when Gradle finishes execution.
            CommandLine.CompletionHandler gradleComplete = (result) => {
                if (result.exitCode != 0) {
                    PlayServicesResolver.Log(
                        String.Format("Gradle failed to fetch dependencies.\n\n" +
                                      "{0}", result.message),
                        level: LogLevel.Error);
                    resolutionComplete(allDependenciesList);
                    return;
                }
                // Parse stdout for copied and missing artifacts.
                List<string> copiedArtifacts;
                List<string> missingArtifacts;
                List<string> modifiedArtifacts;
                ParseDownloadGradleArtifactsGradleOutput(result.stdout, destinationDirectory,
                                                         out copiedArtifacts,
                                                         out missingArtifacts,
                                                         out modifiedArtifacts);
                // Label all copied files.
                PlayServicesResolver.LabelAssets(copiedArtifacts);
                // Display a warning about modified artifact versions.
                if (modifiedArtifacts.Count > 0) {
                    PlayServicesResolver.Log(
                        String.Format("Some conflicting dependencies were found.\n" +
                                      "The following dependency versions were modified:\n" +
                                      "{0}\n", String.Join("\n", modifiedArtifacts.ToArray())),
                        level: LogLevel.Warning);
                }
                // Poke the explode cache for each copied file and add the exploded paths to the
                // output list set.
                var copiedArtifactsSet = new HashSet<string>(copiedArtifacts);
                foreach (var artifact in copiedArtifacts) {
                    if (ShouldExplode(artifact)) {
                        copiedArtifactsSet.Add(DetermineExplodedAarPath(artifact));
                    }
                }

                // Find all labelled files that were not copied and delete them.
                var staleArtifacts = new HashSet<string>();
                foreach (var assetPath in PlayServicesResolver.FindLabeledAssets()) {
                    if (!copiedArtifactsSet.Contains(assetPath.Replace("\\", "/"))) {
                        staleArtifacts.Add(assetPath);
                    }
                }
                if (staleArtifacts.Count > 0) {
                    PlayServicesResolver.Log(
                        String.Format("Deleting stale dependencies:\n{0}",
                                      String.Join("\n",
                                                  (new List<string>(staleArtifacts)).ToArray())),
                        level: LogLevel.Verbose);
                    foreach (var assetPath in staleArtifacts) {
                        FileUtils.DeleteExistingFileOrDirectory(assetPath);
                    }

                }
                // Process / explode copied AARs.
                ProcessAars(
                    destinationDirectory, new HashSet<string>(copiedArtifacts),
                    () => {
                        // Look up the original Dependency structure for each missing artifact.
                        var missingArtifactsAsDependencies = new List<Dependency>();
                        foreach (var artifact in missingArtifacts) {
                            Dependency dep;
                            if (!allDependencies.TryGetValue(artifact, out dep)) {
                                // If this fails, something may have gone wrong with the Gradle
                                // script.  Rather than failing hard, fallback to recreating the
                                // Dependency class with the partial data we have now.
                                var components = new List<string>(
                                    artifact.Split(new char[] { ':' }));
                                if (components.Count < 2) {
                                    PlayServicesResolver.Log(
                                        String.Format(
                                            "Found invalid missing artifact {0}\n" +
                                            "Something went wrong with the gradle artifact " +
                                            "download script\n." +
                                            "Please report a bug", artifact),
                                        level: LogLevel.Warning);
                                    continue;
                                } else if (components.Count < 3 || components[2] == "+") {
                                    components.Add("LATEST");
                                }
                                dep = new Dependency(components[0], components[1], components[2]);
                            }
                            missingArtifactsAsDependencies.Add(dep);
                        }
                        if (logErrorOnMissingArtifacts) {
                            LogMissingDependenciesError(missingArtifacts);
                        }
                        resolutionComplete(missingArtifactsAsDependencies);
                    });
            };

            // Executes gradleComplete on the main thread.
            CommandLine.CompletionHandler scheduleOnMainThread = (result) => {
                System.Action processResult = () => { gradleComplete(result); };
                RunOnMainThread.Run(processResult);
            };

            var filteredDependencies = new List<string>();
            foreach (var dependency in allDependencies.Values) {
                // Convert the legacy "LATEST" version spec to a Gradle version spec.
                filteredDependencies.Add(dependency.Version.ToUpper() == "LATEST" ?
                                         dependency.VersionlessKey + ":+" : dependency.Key);
            }

            var gradleProjectProperties = new Dictionary<string, string>() {
                { "ANDROID_HOME", androidSdkPath },
                { "TARGET_DIR", Path.GetFullPath(destinationDirectory) },
                { "MAVEN_REPOS", String.Join(";", repoList.ToArray()) },
                { "PACKAGES_TO_COPY", String.Join(";", filteredDependencies.ToArray()) }
            };
            var gradleArguments = new List<string> {
                String.Format("-b \"{0}\"", buildScript),
                SettingsDialog.UseGradleDaemon ? "--daemon" : "--no-daemon",
            };
            foreach (var kv in gradleProjectProperties) {
                gradleArguments.Add(String.Format("\"-P{0}={1}\"", kv.Key, kv.Value));
            }
            var gradleArgumentsString = String.Join(" ", gradleArguments.ToArray());

            // Generate gradle.properties to set properties in the script rather than using
            // the command line.
            // Some users of Windows 10 systems have had issues running the Gradle resolver
            // which is suspected to be caused by command line argument parsing.
            // Using both gradle.properties and properties specified via command line arguments
            // works fine.
            File.WriteAllText(Path.Combine(gradleBuildDirectory, "gradle.properties"),
                              GenerateGradleProperties(gradleProjectProperties));

            PlayServicesResolver.Log(String.Format("Running dependency fetching script\n" +
                                                   "\n" +
                                                   "{0} {1}\n",
                                                   gradleWrapper, gradleArgumentsString),
                                     level: LogLevel.Verbose);

            // Run the build script to perform the resolution popping up a window in the editor.
            var window = CommandLineDialog.CreateCommandLineDialog(
                "Resolving Android Dependencies");
            window.summaryText = "Resolving Android Dependencies....";
            window.modal = false;
            window.progressTitle = window.summaryText;
            window.autoScrollToBottom = true;
            window.logger = PlayServicesResolver.logger;
            window.RunAsync(gradleWrapper, gradleArgumentsString,
                            (result) => {
                                window.Close();
                                scheduleOnMainThread(result);
                            },
                            workingDirectory: gradleBuildDirectory,
                            maxProgressLines: 50);
            window.Show();
        }

        /// <summary>
        /// Search the project for AARs & JARs that could conflict with each other and resolve
        /// the conflicts if possible.
        /// </summary>
        ///
        /// This handles the following cases:
        /// 1. If any libraries present match the name play-services-* and google-play-services.jar
        ///    is included in the project the user will be warned of incompatibility between
        ///    the legacy JAR and the newer AAR libraries.
        /// 2. If a managed (labeled) library conflicting with one or more versions of unmanaged
        ///    (e.g play-services-base-10.2.3.aar (managed) vs. play-services-10.2.2.aar (unmanaged)
        ///     and play-services-base-9.2.4.aar (unmanaged))
        ///    The user is warned about the unmanaged conflicting libraries and, if they're
        ///    older than the managed library, prompted to delete the unmanaged libraries.
        private void FindAndResolveConflicts() {
            Func<string, string> getVersionlessArtifactFilename = (filename) => {
                var basename = Path.GetFileName(filename);
                int split = basename.LastIndexOf("-");
                return split >= 0 ? basename.Substring(0, split) : basename;
            };
            var managedPlayServicesArtifacts = new List<string>();
            // Gather artifacts managed by the resolver indexed by versionless name.
            var managedArtifacts = new Dictionary<string, string>();
            var managedArtifactFilenames = new HashSet<string>();
            foreach (var filename in PlayServicesResolver.FindLabeledAssets()) {
                var artifact = getVersionlessArtifactFilename(filename);
                // Ignore non-existent files as it's possible for the asset database to reference
                // missing files if it hasn't been refreshed or completed a refresh.
                if (File.Exists(filename) || Directory.Exists(filename)) {
                    managedArtifacts[artifact] = filename;
                    if (artifact.StartsWith("play-services-") ||
                        artifact.StartsWith("com.google.android.gms.play-services-")) {
                        managedPlayServicesArtifacts.Add(filename);
                    }
                }
            }
            managedArtifactFilenames.UnionWith(managedArtifacts.Values);

            // Gather all artifacts (AARs, JARs) that are not managed by the resolver.
            var unmanagedArtifacts = new Dictionary<string, List<string>>();
            var packagingExtensions = new HashSet<string>(Dependency.Packaging);
            // srcaar files are ignored by Unity so are not included in the build.
            packagingExtensions.Remove(".srcaar");
            // List of paths to the legacy google-play-services.jar
            var playServicesJars = new List<string>();
            const string playServicesJar = "google-play-services.jar";
            foreach (var assetGuid in AssetDatabase.FindAssets("t:Object")) {
                var filename = AssetDatabase.GUIDToAssetPath(assetGuid);
                // Ignore all assets that are managed by the plugin and, since the asset database
                // could be stale at this point, check the file exists.
                if (!managedArtifactFilenames.Contains(filename) &&
                    (File.Exists(filename) || Directory.Exists(filename))) {
                    if (Path.GetFileName(filename).ToLower() == playServicesJar) {
                        playServicesJars.Add(filename);
                    } else if (packagingExtensions.Contains(
                                   Path.GetExtension(filename).ToLower())) {
                        var versionlessFilename = getVersionlessArtifactFilename(filename);
                        List<string> existing;
                        var unmanaged = unmanagedArtifacts.TryGetValue(
                            versionlessFilename, out existing) ? existing : new List<string>();
                        unmanaged.Add(filename);
                        unmanagedArtifacts[versionlessFilename] = unmanaged;
                    }
                }
            }

            // Check for conflicting Play Services versions.
            // It's not possible to resolve this automatically as google-play-services.jar used to
            // include all libraries so we don't know the set of components the developer requires.
            if (managedPlayServicesArtifacts.Count > 0 && playServicesJars.Count > 0) {
                PlayServicesResolver.Log(
                    String.Format(
                        "Legacy {0} found!\n\n" +
                        "Your application will not build in the current state.\n" +
                        "{0} library (found in the following locations):\n" +
                        "{1}\n" +
                        "\n" +
                        "{0} is incompatible with plugins that use newer versions of Google\n" +
                        "Play services (conflicting libraries in the following locations):\n" +
                        "{2}\n" +
                        "\n" +
                        "To resolve this issue find the plugin(s) that use\n" +
                        "{0} and either add newer versions of the required libraries or\n" +
                        "contact the plugin vendor to do so.\n\n",
                        playServicesJar, String.Join("\n", playServicesJars.ToArray()),
                        String.Join("\n", managedPlayServicesArtifacts.ToArray())),
                    level: LogLevel.Warning);
            }

            // For each managed artifact aggregate the set of conflicting unmanaged artifacts.
            var conflicts = new Dictionary<string, List<string>>();
            foreach (var managed in managedArtifacts) {
                List<string> unmanagedFilenames;
                if (unmanagedArtifacts.TryGetValue(managed.Key, out unmanagedFilenames)) {
                    // Found a conflict
                    List<string> existingConflicts;
                    var unmanagedConflicts = conflicts.TryGetValue(
                            managed.Value, out existingConflicts) ?
                        existingConflicts : new List<string>();
                    unmanagedConflicts.AddRange(unmanagedFilenames);
                    conflicts[managed.Value] = unmanagedConflicts;
                }
            }

            // Warn about each conflicting version and attempt to resolve each conflict by removing
            // older unmanaged versions.
            Func<string, string> getVersionFromFilename = (filename) => {
                string basename = Path.GetFileNameWithoutExtension(Path.GetFileName(filename));
                return basename.Substring(getVersionlessArtifactFilename(basename).Length + 1);
            };
            foreach (var conflict in conflicts) {
                var currentVersion = getVersionFromFilename(conflict.Key);
                var conflictingVersionsSet = new HashSet<string>();
                foreach (var conflictFilename in conflict.Value) {
                    conflictingVersionsSet.Add(getVersionFromFilename(conflictFilename));
                }
                var conflictingVersions = new List<string>(conflictingVersionsSet);
                conflictingVersions.Sort(Dependency.versionComparer);

                var warningMessage = String.Format(
                    "Found conflicting Android library {0}\n" +
                    "\n" +
                    "{1} (managed by the Android Resolver) conflicts with:\n" +
                    "{2}\n",
                    getVersionlessArtifactFilename(conflict.Key),
                    conflict.Key, String.Join("\n", conflict.Value.ToArray()));

                // If the conflicting versions are older than the current version we can
                // possibly clean up the old versions automatically.
                if (Dependency.versionComparer.Compare(conflictingVersions[0],
                                                       currentVersion) >= 0) {
                    if (EditorUtility.DisplayDialog(
                            "Resolve Conflict?",
                            warningMessage +
                            "\n" +
                            "The conflicting libraries are older than the library managed by " +
                            "the Android Resolver.  Would you like to remove the old libraries " +
                            "to resolve the conflict?",
                            "Yes", "No")) {
                        foreach (var filename in conflict.Value) {
                            FileUtils.DeleteExistingFileOrDirectory(filename);
                        }
                        warningMessage = null;
                    }
                }

                if (!String.IsNullOrEmpty(warningMessage)) {
                    PlayServicesResolver.Log(
                        warningMessage +
                        "\n" +
                        "Your application is unlikely to build in the current state.\n" +
                        "\n" +
                        "To resolve this problem you can try one of the following:\n" +
                        "* Updating the dependencies managed by the Android Resolver\n" +
                        "  to remove references to old libraries.  Be careful to not\n" +
                        "  include conflicting versions of Google Play services.\n" +
                        "* Contacting the plugin vendor(s) with conflicting\n" +
                        "  dependencies and asking them to update their plugin.\n",
                        level: LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// Perform the resolution and the exploding/cleanup as needed.
        /// </summary>
        public override void DoResolution(
            PlayServicesSupport svcSupport, string destinationDirectory,
            System.Action resolutionComplete) {
            // Run resolution on the main thread to serialize the operation as DoResolutionUnsafe
            // is not thread safe.
            System.Action resolve = () => {
                System.Action unlockResolveAndSignalResolutionComplete = () => {
                    FindAndResolveConflicts();
                    lock (resolveLock) {
                        resolveActionActive = null;
                    }
                    resolutionComplete();
                };
                DoResolutionUnsafe(svcSupport, destinationDirectory,
                                   unlockResolveAndSignalResolutionComplete);
            };
            lock (resolveLock) {
                resolveUpdateQueue.Enqueue(resolve);
                RunOnMainThread.Run(UpdateTryResolution);
            }
        }

        // Try executing a resolution.
        private static void UpdateTryResolution() {
            lock (resolveLock) {
                if (resolveActionActive == null) {
                    if (resolveUpdateQueue.Count > 0) {
                        resolveActionActive = (System.Action)resolveUpdateQueue.Dequeue();
                        resolveActionActive();
                    }
                }
            }
        }

        /// <summary>
        /// Perform the resolution and the exploding/cleanup as needed.
        /// This is *not* thread safe.
        /// </summary>
         private void DoResolutionUnsafe(
            PlayServicesSupport svcSupport, string destinationDirectory,
            System.Action resolutionComplete)
        {
            // Cache the setting as it can only be queried from the main thread.
            var sdkPath = PlayServicesResolver.AndroidSdkRoot;
            // If the Android SDK path isn't set or doesn't exist report an error.
            if (String.IsNullOrEmpty(sdkPath) || !Directory.Exists(sdkPath)) {
                PlayServicesResolver.Log(String.Format(
                    "Android dependency resolution failed, your application will probably " +
                    "not run.\n\n" +
                    "Android SDK path must be set to a valid directory ({0})\n" +
                    "This must be configured in the 'Preference > External Tools > Android SDK'\n" +
                    "menu option.\n", String.IsNullOrEmpty(sdkPath) ? "{none}" : sdkPath),
                    level: LogLevel.Error);
                resolutionComplete();
                return;
            }

            System.Action resolve = () => {
                PlayServicesResolver.Log("Performing Android Dependency Resolution",
                                         LogLevel.Verbose);
                GradleResolution(destinationDirectory, sdkPath, true,
                                 (missingArtifacts) => { resolutionComplete(); });
            };

            System.Action<List<Dependency>> reportOrInstallMissingArtifacts =
                    (List<Dependency> requiredDependencies) => {
                // Set of packages that need to be installed.
                var installPackages = new HashSet<AndroidSdkPackageNameVersion>();
                // Retrieve the set of required packages and whether they're installed.
                var requiredPackages = new Dictionary<string, HashSet<string>>();

                if (requiredDependencies.Count == 0) {
                    resolutionComplete();
                    return;
                }
                foreach (Dependency dependency in requiredDependencies) {
                    PlayServicesResolver.Log(
                        String.Format("Missing Android component {0} (Android SDK Packages: {1})",
                                      dependency.Key, dependency.PackageIds != null ?
                                      String.Join(",", dependency.PackageIds) : "(none)"),
                        level: LogLevel.Verbose);
                    if (dependency.PackageIds != null) {
                        foreach (string packageId in dependency.PackageIds) {
                            HashSet<string> dependencySet;
                            if (!requiredPackages.TryGetValue(packageId, out dependencySet)) {
                                dependencySet = new HashSet<string>();
                            }
                            dependencySet.Add(dependency.Key);
                            requiredPackages[packageId] = dependencySet;
                            // Install / update the Android SDK package that hosts this dependency.
                            installPackages.Add(new AndroidSdkPackageNameVersion {
                                    LegacyName = packageId
                                });
                        }
                    }
                }

                // If no packages need to be installed or Android SDK package installation is
                // disabled.
                if (installPackages.Count == 0 || !AndroidPackageInstallationEnabled()) {
                    // Report missing packages as warnings and try to resolve anyway.
                    foreach (var pkg in requiredPackages.Keys) {
                        var packageNameVersion = new AndroidSdkPackageNameVersion {
                            LegacyName = pkg };
                        var depString = System.String.Join(
                            "\n", (new List<string>(requiredPackages[pkg])).ToArray());
                        if (installPackages.Contains(packageNameVersion)) {
                            PlayServicesResolver.Log(
                                String.Format(
                                    "Android SDK package {0} is not installed or out of " +
                                    "date.\n\n" +
                                    "This is required by the following dependencies:\n" +
                                    "{1}", pkg, depString),
                                level: LogLevel.Warning);
                        }
                    }
                    // At this point we've already tried resolving with Gradle.  Therefore,
                    // Android SDK package installation is disabled or not required trying
                    // to resolve again only repeats the same operation we've already
                    // performed.  So we just report report the missing artifacts as an error
                    // and abort.
                    var missingArtifacts = new List<string>();
                    foreach (var dep in requiredDependencies) missingArtifacts.Add(dep.Key);
                    LogMissingDependenciesError(missingArtifacts);
                    resolutionComplete();
                    return;
                }
                InstallAndroidSdkPackagesAndResolve(sdkPath, installPackages,
                                                    requiredPackages, resolve);
            };

            GradleResolution(destinationDirectory, sdkPath,
                             !AndroidPackageInstallationEnabled(),
                             reportOrInstallMissingArtifacts);
        }

        /// <summary>
        /// Run the SDK manager to install the specified set of packages then attempt resolution
        /// again.
        /// </summary>
        /// <param name="sdkPath">Path to the Android SDK.</param>
        /// <param name="installPackages">Set of Android SDK packages to install.</param>
        /// <param name="requiredPackages">The set dependencies for each Android SDK package.
        /// This is used to report which dependencies can't be installed if Android SDK package
        /// installation fails.</param>
        /// <param name="resolve">Action that performs resolution.</param>
        private void InstallAndroidSdkPackagesAndResolve(
                string sdkPath, HashSet<AndroidSdkPackageNameVersion> installPackages,
                Dictionary<string, HashSet<string>> requiredPackages, System.Action resolve) {
            // Find / upgrade the Android SDK manager.
            AndroidSdkManager.Create(
                sdkPath,
                (IAndroidSdkManager sdkManager) => {
                    if (sdkManager == null) {
                        PlayServicesResolver.Log(
                            String.Format(
                                "Unable to find the Android SDK manager tool.\n\n" +
                                "The following Required Android packages cannot be installed:\n" +
                                "{0}\n" +
                                "\n" +
                                "{1}\n",
                                AndroidSdkPackageNameVersion.ListToString(installPackages),
                                String.IsNullOrEmpty(sdkPath) ?
                                    PlayServicesSupport.AndroidSdkConfigurationError : ""),
                            level: LogLevel.Error);
                        return;
                    }
                    // Get the set of available and installed packages.
                    sdkManager.QueryPackages(
                        (AndroidSdkPackageCollection packages) => {
                            if (packages == null) return;

                            // Filter the set of packages to install by what is available.
                            foreach (var packageName in requiredPackages.Keys) {
                                var pkg = new AndroidSdkPackageNameVersion {
                                    LegacyName = packageName
                                };
                                var depString = System.String.Join(
                                    "\n",
                                    (new List<string>(requiredPackages[packageName])).ToArray());
                                var availablePackage =
                                    packages.GetMostRecentAvailablePackage(pkg.Name);
                                if (availablePackage == null || !availablePackage.Installed) {
                                    PlayServicesResolver.Log(
                                        String.Format(
                                            "Android SDK package {0} ({1}) {2}\n\n" +
                                            "This is required by the following dependencies:\n" +
                                            "{3}\n", pkg.Name, pkg.LegacyName,
                                            availablePackage != null ?
                                                "not installed or out of date." :
                                                "not available for installation.",
                                            depString),
                                        level: LogLevel.Warning);
                                    if (availablePackage == null) {
                                        installPackages.Remove(pkg);
                                    } else if (!availablePackage.Installed) {
                                        installPackages.Add(availablePackage);
                                    }
                                }
                            }
                            if (installPackages.Count == 0) {
                                resolve();
                                return;
                            }
                            // Start installation.
                            sdkManager.InstallPackages(
                                installPackages, (bool success) => { resolve(); });
                        });
                    });
        }


        /// <summary>
        /// Called during Update to allow the resolver to check any build settings of managed
        /// packages to see whether resolution should be triggered again.
        /// </summary>
        /// <returns>Array of packages that should be re-resolved if resolution should occur,
        /// null otherwise.</returns>
        public override string[] OnBuildSettings() {
            // Determine which packages need to be updated.
            List<string> packagesToUpdate = new List<string>();
            List<string> aarsToResolve = new List<string>();
            var aarExplodeDataCopy = new Dictionary<string, AarExplodeData>(aarExplodeData);
            foreach (var kv in aarExplodeDataCopy) {
                var aar = kv.Key;
                var aarData = kv.Value;
                // If the cached file has been removed, ditch it from the cache.
                if (!(File.Exists(aarData.path) || Directory.Exists(aarData.path))) {
                    PlayServicesResolver.Log(String.Format("Found missing AAR {0}", aarData.path),
                                             level: LogLevel.Verbose);
                    aarsToResolve.Add(aar);
                } else if (AarExplodeDataIsDirty(aarData)) {
                    PlayServicesResolver.Log(String.Format("{0} needs to be refreshed ({1})",
                                                           aarData.path, aarData.ToString()),
                                             level: LogLevel.Verbose);
                    packagesToUpdate.Add(aarData.path);
                    aarsToResolve.Add(aar);
                }
            }
            // Remove AARs that will be resolved from the dictionary so the next call to
            // OnBundleId triggers another resolution process.
            foreach (string aar in aarsToResolve) aarExplodeData.Remove(aar);
            SaveAarExplodeCache();
            if (packagesToUpdate.Count == 0) return null;
            string[] packagesToUpdateArray = packagesToUpdate.ToArray();
            PlayServicesResolver.Log(
                String.Format("OnBuildSettings, Packages to update ({0})",
                              String.Join(", ", packagesToUpdateArray)),
                level: LogLevel.Verbose);
            return packagesToUpdateArray;
        }

        #endregion

        /// <summary>
        /// Convert an AAR filename to package name.
        /// </summary>
        /// <param name="aarPath">Path of the AAR to convert.</param>
        /// <returns>AAR package name.</returns>
        private string AarPathToPackageName(string aarPath) {
            var aarFilename = Path.GetFileName(aarPath);
            foreach (var extension in Dependency.Packaging) {
                if (aarPath.EndsWith(extension)) {
                    return aarFilename.Substring(0, aarFilename.Length - extension.Length);
                }
            }
            return aarFilename;
        }

        /// <summary>
        /// Search the cache for an entry associated with the specified AAR path.
        /// </summary>
        /// <param name="aarPath">Path of the AAR to query.</param>
        /// <returns>AarExplodeData if the entry is found in the cache, null otherwise.</returns>
        private AarExplodeData FindAarExplodeDataEntry(string aarPath) {
            var aarFilename = AarPathToPackageName(aarPath);
            AarExplodeData aarData = null;
            // The argument to this method could be the exploded folder rather than the source
            // package (e.g some-package rather than some-package.aar).  Therefore search the
            // cache for the original / unexploded package if the specified file isn't found.
            var packageExtensions = new List<string>();
            packageExtensions.Add("");  // Search for aarFilename first.
            packageExtensions.AddRange(Dependency.Packaging);
            foreach (var extension in packageExtensions) {
                AarExplodeData data;
                if (aarExplodeData.TryGetValue(aarFilename + extension, out data)) {
                    aarData = data;
                    break;
                }
            }
            return aarData;
        }

        /// <summary>
        /// Whether an Ant project should be generated for an artifact.
        /// </summary>
        /// <param name="explode">Whether the artifact needs to be exploded so that it can be
        /// modified.</param>
        private static bool GenerateAntProject(bool explode) {
            return explode && !PlayServicesResolver.GradleBuildEnabled;
        }

        /// <summary>
        /// Determine whether a package is dirty in the AAR cache.
        /// </summary>
        /// <param name="aarData">Path of the AAR to query.</param>
        /// <returns>true if the cache entry is dirty, false otherwise.</returns>
        private bool AarExplodeDataIsDirty(AarExplodeData aarData) {
            if (aarData.bundleId != UnityCompat.ApplicationId) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Bundle ID changed {1} --> {2}", aarData.path,
                                  aarData.bundleId, UnityCompat.ApplicationId),
                    level: LogLevel.Verbose);
                return true;
            }
            var availableAbis = aarData.AvailableAbis;
            var targetAbis = aarData.TargetAbis;
            var currentAbis = AndroidAbis.Current;
            if (targetAbis != null && availableAbis != null &&
                !targetAbis.Equals(AndroidAbis.Current)) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Target ABIs changed {1} --> {2}", aarData.path,
                                  targetAbis.ToString(), currentAbis.ToString()),
                    level: LogLevel.Verbose);
                return true;
            }
            if (aarData.gradleBuildSystem != PlayServicesResolver.GradleBuildEnabled) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Gradle build system enabled changed {0} --> {1}",
                                  aarData.path, aarData.gradleBuildSystem,
                                  PlayServicesResolver.GradleBuildEnabled),
                    level: LogLevel.Verbose);
                return true;
            }
            if (aarData.gradleExport != PlayServicesResolver.GradleProjectExportEnabled) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Gradle export settings changed {0} --> {1}",
                                  aarData.path, aarData.gradleExport,
                                  PlayServicesResolver.GradleProjectExportEnabled),
                    level: LogLevel.Verbose);
                return true;
            }
            bool generateAntProject = GenerateAntProject(aarData.explode);
            if (generateAntProject && !Directory.Exists(aarData.path)) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Should be exploded but artifact directory missing.",
                                  aarData.path),
                    level: LogLevel.Verbose);
                return true;
            } else if (!generateAntProject && !File.Exists(aarData.path)) {
                PlayServicesResolver.Log(
                    String.Format("{0}: Should *not* be exploded but aritfact file missing.",
                                  aarData.path),
                    level: LogLevel.Verbose);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get the target path for an exploded AAR.
        /// </summary>
        /// <param name="aarPath">AAR file to explode.</param>
        /// <returns>Exploded AAR path.</returns>
        private string DetermineExplodedAarPath(string aarPath) {
            return Path.Combine(GooglePlayServices.SettingsDialog.PackageDir,
                                AarPathToPackageName(aarPath));
        }

        /// <summary>
        /// Get the path of an existing AAR or exploded directory within the target directory.
        /// </summary>
        /// <param name="artifactName">Name of the artifact to search for.</param>
        /// <returns>Path to the artifact if found, null otherwise.</returns>
        private string FindAarInTargetPath(string aarPath) {
            var basePath = DetermineExplodedAarPath(aarPath);
            if (Directory.Exists(basePath)) return basePath;
            foreach (var extension in Dependency.Packaging) {
                var packagePath = basePath + extension;
                if (File.Exists(packagePath)) return packagePath;
            }
            return null;
        }

        /// <summary>
        /// Processes the aars.
        /// </summary>
        /// <remarks>Each aar copied is inspected and determined if it should be
        /// exploded into a directory or not. Unneeded exploded directories are
        /// removed.
        /// <para>
        /// Exploding is needed if the version of Unity is old, or if the artifact
        /// has been explicitly flagged for exploding.  This allows the subsequent
        /// processing of variables in the AndroidManifest.xml file which is not
        /// supported by the current versions of the manifest merging process that
        /// Unity uses.
        /// </para>
        /// <param name="dir">The directory to process.</param>
        /// <param name="updatedFiles">Set of files that were recently updated and should be
        /// processed.</param>
        /// <param name="complete">Executed when this process is complete.</param>
        private void ProcessAars(string dir, HashSet<string> updatedFiles, Action complete) {
            // Get set of AAR files and directories we're managing.
            var uniqueAars = new HashSet<string>(PlayServicesResolver.FindLabeledAssets());
            foreach (var aarData in aarExplodeData.Values) uniqueAars.Add(aarData.path);
            var aars = new Queue<string>(uniqueAars);

            const string progressBarTitle = "Processing AARs...";
            int numberOfAars = aars.Count;
            bool displayProgress = (numberOfAars > 0 && !ExecutionEnvironment.InBatchMode);

            if (numberOfAars == 0) {
                complete();
                return;
            }
            // EditorUtility.DisplayProgressBar can't be called multiple times per UI thread tick
            // in some versions of Unity so perform increment processing on each UI thread tick.
            RunOnMainThread.PollOnUpdateUntilComplete(() => {
                int remainingAars = aars.Count;
                bool allAarsProcessed = remainingAars == 0;
                // Since the completion callback can trigger an update, remove this closure from
                // the polling job list if complete.
                if (allAarsProcessed) return true;
                int processedAars = numberOfAars - remainingAars;
                string aarPath = aars.Dequeue();
                remainingAars--;
                allAarsProcessed = remainingAars == 0;
                float progress = (float)processedAars / (float)numberOfAars;
                try {
                    if (displayProgress) {
                        EditorUtility.DisplayProgressBar(progressBarTitle, aarPath, progress);
                    }
                    bool explode = ShouldExplode(aarPath);
                    var aarData = FindAarExplodeDataEntry(aarPath);
                    PlayServicesResolver.Log(
                        String.Format("Processing {0} ({1})", aarPath, aarData.ToString()),
                        level: LogLevel.Verbose);
                    if (AarExplodeDataIsDirty(aarData) || updatedFiles.Contains(aarPath)) {
                        if (explode && File.Exists(aarPath)) {
                            AndroidAbis abis = null;
                            if (!ProcessAar(Path.GetFullPath(dir), aarPath,
                                            GenerateAntProject(explode), out abis)) {
                                PlayServicesResolver.Log(String.Format(
                                    "Failed to process {0}, your Android build will fail.\n" +
                                    "See previous error messages for failure details.\n",
                                    aarPath));
                            }
                            aarData.AvailableAbis = abis;
                        } else if (aarPath != aarData.path) {
                            // Clean up previously expanded / exploded versions of the AAR.
                            PlayServicesResolver.Log(
                                String.Format("Cleaning up previously exploded AAR {0}",
                                              aarPath),
                                level: LogLevel.Verbose);
                            FileUtils.DeleteExistingFileOrDirectory(
                                DetermineExplodedAarPath(aarPath));
                        }
                        aarData.gradleBuildSystem = PlayServicesResolver.GradleBuildEnabled;
                        aarData.gradleExport = PlayServicesResolver.GradleProjectExportEnabled;
                        aarData.TargetAbis = AndroidAbis.Current;
                    }
                    SaveAarExplodeCache();
                } finally {
                    if (allAarsProcessed) {
                        if (displayProgress) EditorUtility.ClearProgressBar();
                        complete();
                    }
                }
                return allAarsProcessed;
            });
        }

        /// <summary>
        /// Determines whether an aar file should be exploded (extracted).
        ///
        /// This is required for some aars so that the Unity Jar Resolver can perform variable
        /// expansion on manifests in the package before they're merged by aapt.
        /// </summary>
        /// <returns><c>true</c>, if the aar should be exploded, <c>false</c> otherwise.</returns>
        /// <param name="aarPath">Path of the AAR file to query whether it should be exploded or
        /// the path to the exploded AAR directory to determine whether the AAR should still be
        /// exploded.</param>
        internal virtual bool ShouldExplode(string aarPath) {
            bool newAarData = false;
            AarExplodeData aarData = FindAarExplodeDataEntry(aarPath);
            if (aarData == null) {
                newAarData = true;
                aarData = new AarExplodeData();
                aarData.path = aarPath;
            }
            string explodeDirectory = DetermineExplodedAarPath(aarPath);
            bool explosionEnabled = true;
            // Unfortunately, as of Unity 5.5.0f3, Unity does not set the applicationId variable
            // in the build.gradle it generates.  This results in non-functional libraries that
            // require the ${applicationId} variable to be expanded in their AndroidManifest.xml.
            // To work around this when Gradle builds are enabled, explosion is enabled for all
            // AARs that require variable expansion unless this behavior is explicitly disabled
            // in the settings dialog.
            if (PlayServicesResolver.GradleProjectExportEnabled && !SettingsDialog.ExplodeAars) {
                explosionEnabled = false;
            }
            AndroidAbis availableAbis = null;
            bool explode = false;
            if (explosionEnabled) {
                explode = !SupportsAarFiles;
                bool useCachedExplodeData = false;
                bool aarFile = File.Exists(aarPath);
                if (!explode) {
                    System.DateTime modificationTime =
                        aarFile ? File.GetLastWriteTime(aarPath) : System.DateTime.MinValue;
                    if (modificationTime.CompareTo(aarData.modificationTime) <= 0 &&
                        !AarExplodeDataIsDirty(aarData)) {
                        explode = aarData.explode;
                        useCachedExplodeData = true;
                    }
                }
                if (!explode) {
                    // If the path is a directory then the caller is referencing an AAR that has
                    // already been exploded in which case we keep explosion enabled.
                    string aarDirectory = Directory.Exists(explodeDirectory) ? explodeDirectory :
                        Directory.Exists(aarData.path) ? aarData.path : null;
                    if (!String.IsNullOrEmpty(aarDirectory)) {
                        // If the directory contains native libraries update the target ABI.
                        if (!useCachedExplodeData) {
                            newAarData = true;
                            availableAbis = AarDirectoryFindAbis(aarDirectory);
                        }
                        explode = true;
                    }
                }
                if (!useCachedExplodeData && !explode) {
                    // If the file doesn't exist we can't interrogate it so we can assume it
                    // doesn't need to be exploded.
                    if (!aarFile) return false;

                    string temporaryDirectory = CreateTemporaryDirectory();
                    if (temporaryDirectory == null) return false;
                    try {
                        string manifestFilename = "AndroidManifest.xml";
                        string classesFilename = "classes.jar";
                        if (aarPath.EndsWith(".aar") &&
                            PlayServicesResolver.ExtractZip(
                                aarPath, new string[] {manifestFilename, "jni", classesFilename},
                                temporaryDirectory)) {
                            string manifestPath = Path.Combine(temporaryDirectory,
                                                               manifestFilename);
                            if (File.Exists(manifestPath)) {
                                string manifest = File.ReadAllText(manifestPath);
                                explode = manifest.IndexOf("${applicationId}") >= 0;
                            }
                            // If the AAR is badly formatted (e.g does not contain classes.jar)
                            // explode it so that we can create classes.jar.
                            explode |= !File.Exists(Path.Combine(temporaryDirectory,
                                                                 classesFilename));
                            // If the AAR contains more than one ABI and Unity's build is
                            // targeting a single ABI, explode it so that unused ABIs can be
                            // removed.
                            newAarData = true;
                            availableAbis = AarDirectoryFindAbis(temporaryDirectory);
                            // Unity 2017's internal build system does not support AARs that contain
                            // native libraries so force explosion to pick up native libraries using
                            // Eclipse / Ant style projects.
                            explode |= availableAbis != null &&
                                Google.VersionHandler.GetUnityVersionMajorMinor() >= 2017.0f;
                            // NOTE: Unfortunately as of Unity 5.5 the internal Gradle build
                            // also blindly includes all ABIs from AARs included in the project
                            // so we need to explode the AARs and remove unused ABIs.
                            if (availableAbis != null) {
                                var abisToRemove = availableAbis.ToSet();
                                abisToRemove.ExceptWith(AndroidAbis.Current.ToSet());
                                explode |= abisToRemove.Count > 0;
                            }
                            aarData.modificationTime = File.GetLastWriteTime(aarPath);
                        }
                    }
                    catch (System.Exception e) {
                        PlayServicesResolver.Log(
                            String.Format("Unable to examine AAR file {0}\n\n{1}", aarPath, e),
                            level: LogLevel.Error);
                        throw e;
                    }
                    finally {
                        FileUtils.DeleteExistingFileOrDirectory(temporaryDirectory);
                    }
                }
            }
            // If this is a new cache entry populate the target ABI and bundle ID fields.
            if (newAarData) {
                aarData.AvailableAbis = availableAbis;
                aarData.TargetAbis = AndroidAbis.Current;
                aarData.bundleId = UnityCompat.ApplicationId;
            }
            aarData.path = GenerateAntProject(explode) ? explodeDirectory : aarPath;
            aarData.explode = explode;
            aarExplodeData[AarPathToPackageName(aarPath)] = aarData;
            SaveAarExplodeCache();
            return explode;
        }
    }
}

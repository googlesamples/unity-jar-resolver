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
            // Target ABI when this was expanded.
            public string targetAbi = ABI_UNIVERSAL;
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
                targetAbi = dataToCopy.targetAbi;
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
                    targetAbi == data.targetAbi &&
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
                    targetAbi.GetHashCode() ^
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
                    PlayServicesSupport.Log(String.Format("Caching AAR {0} state",
                                                          path), verbose: true);
                    ShouldExplode(path);
                }
                return;
            }

            XmlTextReader reader = new XmlTextReader(new StreamReader(aarExplodeDataFile));
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
                                    if (reader.Read() && reader.NodeType == XmlNodeType.Text) {
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
                                        } else if (elementName == "targetAbi") {
                                            aarData.targetAbi = reader.ReadContentAsString();
                                        } else if (elementName == "gradleBuildSystem") {
                                            aarData.gradleBuildSystem =
                                                reader.ReadContentAsBoolean();
                                        } else if (elementName == "gradleExport") {
                                            aarData.gradleExport = reader.ReadContentAsBoolean();
                                        } else if (elementName == "ignoredVersion") {
                                            aarData.ignoredVersion = reader.ReadContentAsString();
                                        }
                                    }
                                }
                            } while (!(reader.Name == "explodeData" &&
                                       reader.NodeType == XmlNodeType.EndElement));
                            if (aar != "" && aarData.path != "") aarExplodeData[aar] = aarData;
                        }
                    }
                }
            }
            aarExplodeDataSaved = AarExplodeData.CopyDictionary(aarExplodeData);
        }

        /// <summary>
        /// Save data from aarExplodeData into aarExplodeDataFile.
        /// </summary>
        private void SaveAarExplodeCache()
        {
            if (File.Exists(aarExplodeDataFile))
            {
                // If the explode data hasn't been modified, don't save.
                if (CompareExplodeData(aarExplodeData, aarExplodeDataSaved)) return;
                File.Delete(aarExplodeDataFile);
            }
            XmlTextWriter writer = new XmlTextWriter(new StreamWriter(aarExplodeDataFile)) {
                Formatting = Formatting.Indented,
            };
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
                writer.WriteStartElement("targetAbi");
                writer.WriteValue(kv.Value.targetAbi);
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
        private void ParseDownloadGradleArtifactsGradleOutput(
                string output, string destinationDirectory,
                out List<string> copiedArtifacts, out List<string> missingArtifacts) {
            // Parse stdout for copied and missing artifacts.
            copiedArtifacts = new List<string>();
            missingArtifacts = new List<string>();
            string currentHeader = null;
            const string COPIED_ARTIFACTS_HEADER = "Copied artifacts:";
            const string MISSING_ARTIFACTS_HEADER = "Missing artifacts:";
            foreach (var line in output.Split(new string[] { "\r\n", "\n" },
                                              StringSplitOptions.None)) {
                if (line.StartsWith(COPIED_ARTIFACTS_HEADER)) {
                    currentHeader = line;
                    continue;
                } else if (line.StartsWith(MISSING_ARTIFACTS_HEADER)) {
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
                PlayServicesSupport.Log(
                   String.Format("Resolution failed\n\n" +
                                 "Failed to fetch the following dependencies:\n{0}\n\n",
                                 String.Join("\n", missingArtifacts.ToArray())),
                   level: PlayServicesSupport.LogLevel.Error);
            }
        }

        /// <summary>
        /// Perform resolution using Gradle.
        /// </summary>
        /// <param name="svcSupport">PlayServicesSupport instance.</param>
        /// <param name="destinationDirectory">Directory to copy packages into.</param>
        /// <param name="androidSdkPath">Path to the Android SDK.  This is required as
        /// PlayServicesSupport.SDK can only be called from the main thread.</param>
        /// <param name="logErrorOnMissingArtifacts">Log errors when artifacts are missing.</param>
        /// <param name="resolutionComplete">Called when resolution is complete with a list of
        /// packages that were not found.</param>
        private void GradleResolution(
                PlayServicesSupport svcSupport, string destinationDirectory,
                string androidSdkPath, bool logErrorOnMissingArtifacts,
                System.Action<List<Dependency>> resolutionComplete) {
            var gradleWrapper = Path.Combine(
                gradleBuildDirectory,
                UnityEngine.RuntimePlatform.WindowsEditor == UnityEngine.Application.platform ?
                    "gradlew.bat" : "gradlew");
            var buildScript = Path.Combine(
                gradleBuildDirectory, EMBEDDED_RESOURCES_NAMESPACE + "download_artifacts.gradle");
            // Get all dependencies.
            var allDependencies = svcSupport.LoadDependencies(true, keepMissing: true,
                                                              findCandidates: true);
            var allDependenciesList = new List<Dependency>(allDependencies.Values);

            // Extract Gradle wrapper and the build script to the build directory.
            if (!(Directory.Exists(gradleBuildDirectory) && File.Exists(gradleWrapper) &&
                  File.Exists(buildScript))) {
                var gradleTemplateZip = Path.Combine(
                    gradleBuildDirectory, EMBEDDED_RESOURCES_NAMESPACE + "gradle-template.zip");
                foreach (var resource in new [] { gradleTemplateZip, buildScript }) {
                    ExtractResource(Path.GetFileName(resource), resource);
                }
                if (!ExtractAar(gradleTemplateZip, new [] {
                            "gradle/wrapper/gradle-wrapper.jar",
                            "gradle/wrapper/gradle-wrapper.properties",
                            "gradlew",
                            "gradlew.bat"}, gradleBuildDirectory)) {
                    PlayServicesSupport.Log(
                       String.Format("Unable to extract Gradle build component {0}\n\n" +
                                     "Resolution failed.", gradleTemplateZip),
                       level: PlayServicesSupport.LogLevel.Error);
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
                        PlayServicesSupport.Log(
                            String.Format("Failed to make \"{0}\" executable.\n\n" +
                                          "Resolution failed.\n\n{1}", gradleWrapper,
                                          result.message),
                            level: PlayServicesSupport.LogLevel.Error);
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
                    if (repoSet.Contains(repo)) continue;
                    repoSet.Add(repo);
                    repoList.Add(repo);
                }
            }

            // Executed when Gradle finishes execution.
            CommandLine.CompletionHandler gradleComplete = (result) => {
                if (result.exitCode != 0) {
                    PlayServicesSupport.Log(
                        String.Format("Gradle failed to fetch dependencies.\n\n" +
                                      "{0}", result.message),
                        level: PlayServicesSupport.LogLevel.Error);
                    resolutionComplete(allDependenciesList);
                    return;
                }
                // Parse stdout for copied and missing artifacts.
                List<string> copiedArtifacts;
                List<string> missingArtifacts;
                ParseDownloadGradleArtifactsGradleOutput(result.stdout, destinationDirectory,
                                                         out copiedArtifacts,
                                                         out missingArtifacts);
                // Label all copied files.
                PlayServicesResolver.LabelAssets(copiedArtifacts);
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
                    PlayServicesSupport.Log(
                        String.Format("Deleting stale dependencies:\n{0}",
                                      String.Join("\n",
                                                  (new List<string>(staleArtifacts)).ToArray())),
                        verbose: true);
                    foreach (var assetPath in staleArtifacts) {
                        PlayServicesSupport.DeleteExistingFileOrDirectory(assetPath,
                                                                          includeMetaFiles: true);
                    }

                }
                // Process / explode copied AARs.
                ProcessAars(destinationDirectory, new HashSet<string>(copiedArtifacts));

                // Look up the original Dependency structure for each missing artifact.
                var missingArtifactsAsDependencies = new List<Dependency>();
                foreach (var artifact in missingArtifacts) {
                    Dependency dep;
                    if (!allDependencies.TryGetValue(artifact, out dep)) {
                        // If this fails, something may have gone wrong with the Gradle script.
                        // Rather than failing hard, fallback to recreating the Dependency
                        // class with the partial data we have now.
                        var components = new List<string>(artifact.Split(new char[] { ':' }));
                        if (components.Count < 2) {
                            PlayServicesSupport.Log(
                                String.Format(
                                    "Found invalid missing artifact {0}\n" +
                                    "Something went wrong with the gradle artifact download " +
                                    "script\n." +
                                    "Please report a bug", artifact),
                                level: PlayServicesSupport.LogLevel.Warning);
                            continue;
                        } else if (components.Count < 3 || components[2] == "+") {
                            components.Add("LATEST");
                        }
                        dep = new Dependency(components[0], components[1], components[2]);
                    }
                    missingArtifactsAsDependencies.Add(dep);
                }
                if (logErrorOnMissingArtifacts) LogMissingDependenciesError(missingArtifacts);
                resolutionComplete(missingArtifactsAsDependencies);
            };

            // Executes gradleComplete on the main thread.
            CommandLine.CompletionHandler scheduleOnMainThread = (result) => {
                System.Action processResult = () => { gradleComplete(result); };
                PlayServicesResolver.updateQueue.Enqueue(processResult);
            };

            var filteredDependencies = new List<string>();
            foreach (var dependency in allDependencies.Values) {
                // Convert the legacy "LATEST" version spec to a Gradle version spec.
                filteredDependencies.Add(dependency.Version.ToUpper() == "LATEST" ?
                                         dependency.VersionlessKey + ":+" : dependency.Key);
            }

            var gradleArguments =
                String.Join(
                    " ",
                    new [] {
                        "-b", buildScript,
                        String.Format("\"-PANDROID_HOME={0}\"", androidSdkPath),
                        String.Format("\"-PTARGET_DIR={0}\"",
                                      Path.GetFullPath(destinationDirectory)),
                        String.Format("\"-PMAVEN_REPOS={0}\"",
                                      String.Join(";", repoList.ToArray())),
                        String.Format("\"-PPACKAGES_TO_COPY={0}\"",
                                      String.Join(";", filteredDependencies.ToArray()))
                    });

            PlayServicesSupport.Log(String.Format("Running dependency fetching script\n" +
                                                  "\n" +
                                                  "{0} {1}\n",
                                                  gradleWrapper, gradleArguments),
                                    verbose: true);

            // Run the build script to perform the resolution popping up a window in the editor.
            var window = CommandLineDialog.CreateCommandLineDialog(
                "Resolving Android Dependencies");
            window.summaryText = "Resolving Android Dependencies....";
            window.modal = false;
            window.progressTitle = window.summaryText;
            window.autoScrollToBottom = true;
            window.RunAsync(gradleWrapper, gradleArguments,
                            (result) => {
                                window.Close();
                                scheduleOnMainThread(result);
                            },
                            maxProgressLines: 50);
            window.Show();
        }

        /// <summary>
        /// Perform the resolution and the exploding/cleanup as needed.
        /// </summary>
        public override void DoResolution(
            PlayServicesSupport svcSupport, string destinationDirectory,
            PlayServicesSupport.OverwriteConfirmation handleOverwriteConfirmation,
            System.Action resolutionComplete) {
            // Run resolution on the main thread to serialize the operation as DoResolutionUnsafe
            // is not thread safe.
            System.Action resolve = () => {
                System.Action unlockResolveAndSignalResolutionComplete = () => {
                    lock (resolveLock) {
                        resolveActionActive = null;
                    }
                    resolutionComplete();
                };
                DoResolutionUnsafe(svcSupport, destinationDirectory, handleOverwriteConfirmation,
                                   unlockResolveAndSignalResolutionComplete);
            };
            lock (resolveLock) {
                resolveUpdateQueue.Enqueue(resolve);
                EditorApplication.update += UpdateTryResolution;
            }
        }

        // Try executing a resolution.
        private static void UpdateTryResolution() {
            lock (resolveLock) {
                if (resolveActionActive == null) {
                    if (resolveUpdateQueue.Count > 0) {
                        resolveActionActive = (System.Action)resolveUpdateQueue.Dequeue();
                        resolveActionActive();
                    } else {
                        EditorApplication.update -= UpdateTryResolution;
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
            PlayServicesSupport.OverwriteConfirmation handleOverwriteConfirmation,
            System.Action resolutionComplete)
        {
            // Cache the setting as it can only be queried from the main thread.
            bool fetchDependenciesWithGradle =
                GooglePlayServices.SettingsDialog.FetchDependenciesWithGradle;
            var sdkPath = svcSupport.SDK;
            System.Action resolve = () => {
                PlayServicesSupport.Log("Performing Android Dependency Resolution", verbose: true);
                if (fetchDependenciesWithGradle) {
                    GradleResolution(svcSupport, destinationDirectory, sdkPath, true,
                                     (missingArtifacts) => { resolutionComplete(); });
                } else {
                    DoResolutionNoAndroidPackageChecks(svcSupport, destinationDirectory,
                                                       handleOverwriteConfirmation);
                    resolutionComplete();
                }
            };

            Dictionary<string, string> pathsByDependencyKey;
            var dependencies = svcSupport.FindMissingDependencyPaths(destinationDirectory,
                                                                     out pathsByDependencyKey);
            // When fetching assets with Gradle we don't know the transitive set of artifacts
            // unless we run Gradle.  Therefore, it's not possible to quickly determine which
            // set of assets is stale.  In this case, we simply don't clean stale assets unless
            // resolution is required.
            if (!fetchDependenciesWithGradle) {
                // If any dependencies are no longer present we'll assume dependencies have been
                // added or removed so clean all stale tracked dependencies.
                var currentDependencyPaths = new HashSet<string>();
                // Normalize paths Windows paths to compare with POSIX file systems
                // (used by Maven).
                foreach (var assetPath in pathsByDependencyKey.Values) {
                    currentDependencyPaths.Add(assetPath.Replace("\\", "/"));
                }
                foreach (var assetPath in PlayServicesResolver.FindLabeledAssets()) {
                    var assetBasename = Directory.Exists(assetPath) ?
                        assetPath : Path.Combine(Path.GetDirectoryName(assetPath),
                                                 Path.GetFileNameWithoutExtension(assetPath));
                    assetBasename = assetBasename.Replace("\\", "/");
                    var assetTargetPaths = new List<string> { assetBasename };
                    foreach (var extension in Dependency.Packaging) {
                        assetTargetPaths.Add(assetBasename + extension);
                    }
                    if (!assetTargetPaths.Exists(
                             targetPath => currentDependencyPaths.Contains(targetPath))) {
                        PlayServicesSupport.Log(
                            String.Format(
                                "Deleting stale dependency {0} not in required paths:\n{1}",
                                assetPath,
                                String.Join("\n",
                                            (new List<string>(currentDependencyPaths)).ToArray())),
                            verbose: true);
                        PlayServicesSupport.DeleteExistingFileOrDirectory(assetPath,
                                                                          includeMetaFiles: true);
                    }
                }
            }

            // If dependencies are not missing, don't perform resolution.
            if (dependencies == null) {
                PlayServicesSupport.Log("No missing or stale Android dependencies found.",
                                        verbose: true);
                resolutionComplete();
                return;
            }
            PlayServicesSupport.Log("Found missing Android dependencies, resolving.",
                                    verbose: true);

            System.Action<List<Dependency>> reportOrInstallMissingArtifacts =
                    (List<Dependency> requiredDependencies) => {
                // Set of packages that need to be installed.
                var installPackages = new HashSet<AndroidSdkPackageNameVersion>();
                // Retrieve the set of required packages and whether they're installed.
                var requiredPackages = new Dictionary<string, HashSet<string>>();

                if (fetchDependenciesWithGradle) {
                    if (requiredDependencies.Count == 0) {
                        resolutionComplete();
                        return;
                    }
                } else {
                    requiredDependencies = new List<Dependency>(
                        PlayServicesSupport.GetTransitiveDependencies(
                            svcSupport.LoadDependencies(true, keepMissing: true),
                            repoPaths: svcSupport.RepositoryPaths).Values);
                }
                foreach (Dependency dependency in requiredDependencies) {
                    PlayServicesSupport.Log(
                        String.Format("Missing Android component {0} (Android SDK Packages: {1})",
                                      dependency.Key, dependency.PackageIds != null ?
                                      String.Join(",", dependency.PackageIds) : "(none)"),
                        verbose: true);
                    if (dependency.PackageIds != null) {
                        foreach (string packageId in dependency.PackageIds) {
                            HashSet<string> dependencySet;
                            if (!requiredPackages.TryGetValue(packageId, out dependencySet)) {
                                dependencySet = new HashSet<string>();
                            }
                            dependencySet.Add(dependency.Key);
                            requiredPackages[packageId] = dependencySet;
                            // If the dependency is missing, add it to the set that needs to be
                            // installed.
                            if (System.String.IsNullOrEmpty(dependency.BestVersionPath)) {
                                installPackages.Add(new AndroidSdkPackageNameVersion {
                                        LegacyName = packageId
                                    });
                            }
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
                            PlayServicesSupport.Log(
                                String.Format(
                                    "Android SDK package {0} is not installed or out of " +
                                    "date.\n\n" +
                                    "This is required by the following dependencies:\n" +
                                    "{1}", pkg, depString),
                                level: PlayServicesSupport.LogLevel.Warning);
                        }
                    }
                    if (fetchDependenciesWithGradle) {
                        // At this point we've already tried resolving with Gradle.  Therefore,
                        // Android SDK package installation is disabled or not required trying
                        // to resolve again only repeats the same operation we've already
                        // performed.  So we just report report the missing artifacts as an error
                        // and abort.
                        var missingArtifacts = new List<string>();
                        foreach (var dep in requiredDependencies) missingArtifacts.Add(dep.Key);
                        LogMissingDependenciesError(missingArtifacts);
                        return;
                    }
                    // Attempt resolution.
                    resolve();
                    return;
                }
                InstallAndroidSdkPackagesAndResolve(sdkPath, installPackages,
                                                    requiredPackages, resolve);
            };

            if (fetchDependenciesWithGradle) {
                GradleResolution(svcSupport, destinationDirectory, sdkPath,
                                 !AndroidPackageInstallationEnabled(),
                                 reportOrInstallMissingArtifacts);
            } else {
                reportOrInstallMissingArtifacts(null);
            }
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
                        PlayServicesSupport.Log(
                            String.Format(
                                "Unable to find the Android SDK manager tool.\n\n" +
                                "The following Required Android packages cannot be installed:\n" +
                                "{0}\n" +
                                "\n" +
                                "{1}\n",
                                AndroidSdkPackageNameVersion.ListToString(installPackages),
                                String.IsNullOrEmpty(sdkPath) ?
                                    PlayServicesSupport.AndroidSdkConfigurationError : ""),
                            level: PlayServicesSupport.LogLevel.Error);
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
                                    PlayServicesSupport.Log(
                                        String.Format(
                                            "Android SDK package {0} ({1}) {2}\n\n" +
                                            "This is required by the following dependencies:\n" +
                                            "{3}\n", pkg.Name, pkg.LegacyName,
                                            availablePackage != null ?
                                                "not installed or out of date." :
                                                "not available for installation.",
                                            depString),
                                        level: PlayServicesSupport.LogLevel.Warning);
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
                                installPackages,
                                (bool success) => { if (success) resolve(); });
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
                    PlayServicesSupport.Log(String.Format("Found missing AAR {0}", aarData.path),
                                            verbose: true);
                    aarsToResolve.Add(aar);
                } else if (AarExplodeDataIsDirty(aarData)) {
                    PlayServicesSupport.Log(String.Format("AAR {0} needs to be refreshed",
                                                          aarData.path), verbose: true);
                    packagesToUpdate.Add(aarData.path);
                    aarsToResolve.Add(aar);
                }
            }
            // Remove AARs that will be resolved from the dictionary so the next call to
            // OnBundleId triggers another resolution process.
            foreach (string aar in aarsToResolve) aarExplodeData.Remove(aar);
            SaveAarExplodeCache();
            return packagesToUpdate.Count > 0 ? packagesToUpdate.ToArray() : null;
        }

        /// <summary>
        /// Determine whether to replace a dependency with a new version.
        /// </summary>
        /// <param name="oldDependency">Previous version of the dependency.</param>
        /// <param name="newDependency">New version of the dependency.</param>
        /// <returns>true if the dependency should be replaced, false otherwise.</returns>
        public override bool ShouldReplaceDependency(Dependency oldDependency,
                                                     Dependency newDependency) {
            var artifactPath = FindAarInTargetPath(oldDependency.BestVersionArtifactPath);
            // If we're unable to find the specified artifact then it's possible the oldDependency
            // doesn't have a valid version string.
            if (String.IsNullOrEmpty(artifactPath)) return true;

            // ShouldExplode() creates an AarExplodeData entry if it isn't present.
            ShouldExplode(artifactPath);
            // NOTE: explodeData will only be null here if the artifact was deleted prior to
            // the call to ShouldExplode().
            AarExplodeData explodeData = FindAarExplodeDataEntry(artifactPath);
            if (explodeData != null && explodeData.ignoredVersion == newDependency.BestVersion) {
                return false;
            }
            var overwrite = PlayServicesResolver.HandleOverwriteConfirmation(oldDependency,
                                                                             newDependency);
            if (explodeData != null) {
                explodeData.ignoredVersion = overwrite ? "" : newDependency.BestVersion;
            }
            SaveAarExplodeCache();
            return overwrite;
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
        /// Determine whether a package is dirty in the AAR cache.
        /// </summary>
        /// <param name="aarData">Path of the AAR to query.</param>
        /// <returns>true if the cache entry is dirty, false otherwise.</returns>
        private bool AarExplodeDataIsDirty(AarExplodeData aarData) {
            return aarData.bundleId != UnityCompat.ApplicationId ||
                (aarData.targetAbi != AarExplodeData.ABI_UNIVERSAL &&
                 aarData.targetAbi != PlayServicesResolver.AndroidTargetDeviceAbi) ||
                aarData.gradleBuildSystem != PlayServicesResolver.GradleBuildEnabled ||
                aarData.gradleExport != PlayServicesResolver.GradleProjectExportEnabled ||
                (aarData.explode && !PlayServicesResolver.GradleBuildEnabled ?
                 !Directory.Exists(aarData.path) : !File.Exists(aarData.path));
        }

        /// <summary>
        /// Perform resolution with no Android package dependency checks.
        /// </summary>
        private void DoResolutionNoAndroidPackageChecks(
            PlayServicesSupport svcSupport, string destinationDirectory,
            PlayServicesSupport.OverwriteConfirmation handleOverwriteConfirmation)
        {
            var updatedAars = new HashSet<string>();
            try
            {
                // Get the collection of dependencies that need to be copied.
                Dictionary<string, Dependency> deps =
                    svcSupport.ResolveDependencies(true, destDirectory: destinationDirectory,
                                                   explodeAar: (aarPath) => {
                                                       return ShouldExplode(aarPath);
                                                   });
                // Copy the list
                updatedAars.UnionWith(svcSupport.CopyDependencies(
                    deps, destinationDirectory, handleOverwriteConfirmation).Values);
                // Label all copied files so they can be cleaned up if resolution needs to be
                // triggered again.
                PlayServicesResolver.LabelAssets(updatedAars);
            }
            catch (Google.JarResolver.ResolutionException e)
            {
                PlayServicesSupport.Log(e.ToString(), level: PlayServicesSupport.LogLevel.Error);
                return;
            }

            // we want to look at all the .aars to decide to explode or not.
            // Some aars have variables in their AndroidManifest.xml file,
            // e.g. ${applicationId}.  Unity does not understand how to process
            // these, so we handle it here.
            ProcessAars(destinationDirectory, updatedAars);
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
        private void ProcessAars(string dir, HashSet<string> updatedFiles) {
            // Get set of AAR files and directories we're managing.
            var aars = new HashSet<string>(PlayServicesResolver.FindLabeledAssets());
            foreach (var aarData in aarExplodeData.Values) aars.Add(aarData.path);
            foreach (string aarPath in aars) {
                bool explode = ShouldExplode(aarPath);
                var aarData = FindAarExplodeDataEntry(aarPath);
                if (AarExplodeDataIsDirty(aarData) || updatedFiles.Contains(aarPath)) {
                    if (explode && File.Exists(aarPath)) {
                        ProcessAar(Path.GetFullPath(dir), aarPath,
                                   !PlayServicesResolver.GradleBuildEnabled,
                                   out aarData.targetAbi);
                        aarData.targetAbi = aarData.targetAbi ?? AarExplodeData.ABI_UNIVERSAL;
                    } else {
                        // Clean up previously expanded / exploded versions of the AAR.
                        PlayServicesSupport.DeleteExistingFileOrDirectory(
                            DetermineExplodedAarPath(aarPath), includeMetaFiles: true);
                    }
                    aarData.gradleBuildSystem = PlayServicesResolver.GradleBuildEnabled;
                    aarData.gradleExport = PlayServicesResolver.GradleProjectExportEnabled;
                }
            }
            SaveAarExplodeCache();
        }

        /// <summary>
        /// Get the set of native library ABIs in an exploded AAR.
        /// </summary>
        /// <param name="aarDirectory">Directory to search for ABIs.</param>
        /// <returns>Set of ABI directory names in the exploded AAR.</returns>
        private HashSet<string> AarDirectoryFindAbis(string aarDirectory) {
            var foundAbis = new HashSet<string>();
            foreach (var libDirectory in NATIVE_LIBRARY_DIRECTORIES) {
                foreach (var kv in UNITY_ABI_TO_NATIVE_LIBRARY_ABI_DIRECTORY) {
                    var path = Path.Combine(libDirectory, kv.Value);
                    if (Directory.Exists(Path.Combine(aarDirectory, path))) {
                        foundAbis.Add(kv.Key);
                    }
                }
            }
            return foundAbis;
        }

        /// <summary>
        /// Determine whether a Unity library project (extract AAR) contains native libraries.
        /// </summary>
        /// <param name="foundAbis">Set of ABI directories in the AAR.</param>
        /// <return>ABI associated with the directory.</return>
        private string DetermineAbiFromAarAbiDirectories(HashSet<string> foundAbis) {
            var numberOfAbis = foundAbis.Count;
            if (numberOfAbis > 0) {
                if (numberOfAbis == 1) foreach (var abi in foundAbis) return abi;
                return PlayServicesResolver.DEFAULT_ANDROID_TARGET_DEVICE_ABI;
            }
            return AarExplodeData.ABI_UNIVERSAL;
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
            string targetAbi = AarExplodeData.ABI_UNIVERSAL;
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
                            targetAbi = DetermineAbiFromAarAbiDirectories(
                                AarDirectoryFindAbis(aarDirectory));
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
                            ExtractAar(aarPath, new string[] {manifestFilename, "jni",
                                                              classesFilename},
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
                            var abiDirs = AarDirectoryFindAbis(temporaryDirectory);
                            // Unity 2017's native build system does not support AARs that contain
                            // native libraries so force explosion to pick up native libraries using
                            // Eclipse / Ant style projects.
                            explode |= Google.VersionHandler.GetUnityVersionMajorMinor() >=
                                2017.0f && abiDirs.Count > 0;
                            targetAbi = DetermineAbiFromAarAbiDirectories(abiDirs);
                            // NOTE: Unfortunately as of Unity 5.5 the internal Gradle build
                            // also blindly includes all ABIs from AARs included in the project
                            // so we need to explode the AARs and remove unused ABIs.
                            explode |= targetAbi != AarExplodeData.ABI_UNIVERSAL &&
                                targetAbi != PlayServicesResolver.AndroidTargetDeviceAbi;
                            aarData.modificationTime = File.GetLastWriteTime(aarPath);
                        }
                    }
                    catch (System.Exception e) {
                        PlayServicesSupport.Log(
                            String.Format("Unable to examine AAR file {0}\n\n{1}", aarPath, e),
                            level: PlayServicesSupport.LogLevel.Error);
                        throw e;
                    }
                    finally {
                        PlayServicesSupport.DeleteExistingFileOrDirectory(temporaryDirectory);
                    }
                }
            }
            // If this is a new cache entry populate the target ABI and bundle ID fields.
            if (newAarData) {
                aarData.targetAbi = targetAbi;
                aarData.bundleId = UnityCompat.ApplicationId;
            }
            aarData.path = explode && !PlayServicesResolver.GradleBuildEnabled ?
                explodeDirectory : aarPath;
            aarData.explode = explode;
            aarExplodeData[AarPathToPackageName(aarPath)] = aarData;
            SaveAarExplodeCache();
            return explode;
        }
    }
}

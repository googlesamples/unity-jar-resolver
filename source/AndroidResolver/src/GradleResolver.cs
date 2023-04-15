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
    using System.Collections.Specialized;
    using Google;
    using Google.JarResolver;
    using System;
    using System.Collections;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Xml;

    public class GradleResolver
    {
        public GradleResolver() {}

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
                        copiedArtifacts.Add(
                            FileUtils.PosixPathSeparators(
                                Path.Combine(destinationDirectory, line.Trim())));
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
                PlayServicesResolver.analytics.Report(
                    "/resolve/gradle/failed",
                    PlayServicesResolver.GetResolutionMeasurementParameters(missingArtifacts),
                    "Gradle Resolve Failed");
                PlayServicesResolver.Log(
                   String.Format("Resolution failed\n\n" +
                                 "Failed to fetch the following dependencies:\n{0}\n\n",
                                 String.Join("\n", missingArtifacts.ToArray())),
                   level: LogLevel.Error);
            } else {
                PlayServicesResolver.analytics.Report(
                    "/resolve/gradle/failed",
                    PlayServicesResolver.GetResolutionMeasurementParameters(null),
                    "Gradle Resolve Failed");
            }
        }

        /// <summary>
        /// Get package spec from a dependency.
        /// </summary>
        /// <param name="dependency">Dependency instance to query for package spec.</param>
        internal static string DependencyToPackageSpec(Dependency dependency) {
            return dependency.Version.ToUpper() == "LATEST" ?
                dependency.VersionlessKey + ":+" : dependency.Key;
        }

        /// <summary>
        /// From a list of dependencies generate a list of Maven / Gradle / Ivy package spec
        /// strings.
        /// </summary>
        /// <param name="dependencies">Dependency instances to query for package specs.</param>
        /// <returns>Dictionary of Dependency instances indexed by package spec strings.</returns>
        internal static Dictionary<string, string> DependenciesToPackageSpecs(
                IEnumerable<Dependency> dependencies) {
            var sourcesByPackageSpec = new Dictionary<string, string>();
            foreach (var dependency in dependencies) {
                // Convert the legacy "LATEST" version spec to a Gradle version spec.
                var packageSpec = DependencyToPackageSpec(dependency);
                var source = CommandLine.SplitLines(dependency.CreatedBy)[0];
                string sources;
                if (sourcesByPackageSpec.TryGetValue(packageSpec, out sources)) {
                    sources = sources + ", " + source;
                } else {
                    sources = source;
                }
                sourcesByPackageSpec[packageSpec] = sources;
            }
            return sourcesByPackageSpec;
        }

        /// <summary>
        /// Convert a repo path to a valid URI.
        /// If the specified repo is a local directory and it doesn't exist, search the project
        /// for a match.
        /// Valid paths are:
        /// * Path relative to Assets or Packages folder, ex. "Firebase/m2repository"
        /// * Path relative to project folder, ex."Assets/Firebase/m2repository"
        /// </summary>
        /// <param name="repoPath">Repo path to convert.</param>
        /// <param name="sourceLocation">XML or source file this path is referenced from. If this is
        /// null the calling method's source location is used when logging the source of this
        /// repo declaration.</param>
        /// <returns>URI to the repo.</returns>
        internal static string RepoPathToUri(string repoPath, string sourceLocation=null) {
            if (sourceLocation == null) {
                // Get the caller's stack frame.
                sourceLocation = System.Environment.StackTrace.Split(new char[] { '\n' })[1];
            }
            // Filter Android SDK repos as they're supplied in the build script.
            if (repoPath.StartsWith(PlayServicesSupport.SdkVariable)) return null;
            // Since we need a URL, determine whether the repo has a scheme.  If not,
            // assume it's a local file.
            foreach (var scheme in new [] { "file:", "http:", "https:" }) {
                if (repoPath.StartsWith(scheme)) return GradleWrapper.EscapeUri(repoPath);
            }

            if (!Directory.Exists(repoPath)) {
                string trimmedRepoPath = repoPath;
                string foundPath = "";
                bool shouldLog = false;

                if (FileUtils.IsUnderDirectory(repoPath, FileUtils.ASSETS_FOLDER)) {
                    trimmedRepoPath = repoPath.Substring(FileUtils.ASSETS_FOLDER.Length + 1);
                } else if (FileUtils.IsUnderPackageDirectory(repoPath)) {
                    // Trim the Packages/package-id/ part
                    string packageFolder = FileUtils.GetPackageDirectory(repoPath);
                    if (!String.IsNullOrEmpty(packageFolder)) {
                        trimmedRepoPath = repoPath.Substring(packageFolder.Length + 1);
                    }
                }

                // Search under Packages/package-id first if Dependencies.xml is from a UPM package.
                if (FileUtils.IsUnderPackageDirectory(sourceLocation)) {
                    // Get the physical package directory.
                    // Ex. Library/PackageCache/com.google.unity-jar-resolver@1.2.120/
                    string packageFolder = FileUtils.GetPackageDirectory(
                            sourceLocation, FileUtils.PackageDirectoryType.PhysicalPath);
                    if (!String.IsNullOrEmpty(packageFolder)) {
                        string repoPathUnderPackages =
                            packageFolder + Path.DirectorySeparatorChar + trimmedRepoPath;
                        if (Directory.Exists(repoPathUnderPackages)) {
                            foundPath = repoPathUnderPackages;
                        } else {
                            // It is unlikely but possible the user has moved the repository in the
                            // project under Packages directory, so try searching for it.
                            foundPath = FileUtils.FindPathUnderDirectory(
                                packageFolder, trimmedRepoPath);
                            if (!String.IsNullOrEmpty(foundPath)) {
                                foundPath = packageFolder + Path.DirectorySeparatorChar + foundPath;
                                shouldLog = true;
                            }
                        }
                    }
                }

                // Search under Assets/
                if (String.IsNullOrEmpty(foundPath)) {
                    // Try to find under "Assets" folder.  It is possible that "Assets/" was not
                    // added to the repoPath.
                    string repoPathUnderAssets =
                        FileUtils.ASSETS_FOLDER + Path.DirectorySeparatorChar + trimmedRepoPath;
                    if (Directory.Exists(repoPathUnderAssets)) {
                        foundPath = repoPathUnderAssets;
                    } else {
                        // If the directory isn't found, it is possible the user has moved the
                        // repository in the project under Assets directory, so try searching for
                        // it.
                        foundPath = FileUtils.FindPathUnderDirectory(
                            FileUtils.ASSETS_FOLDER, trimmedRepoPath);
                        if (!String.IsNullOrEmpty(foundPath)) {
                            foundPath = FileUtils.ASSETS_FOLDER +
                                        Path.DirectorySeparatorChar + foundPath;
                            shouldLog = true;
                        }
                    }
                }

                if (!String.IsNullOrEmpty(foundPath)) {
                    if (shouldLog) {
                        PlayServicesResolver.Log(String.Format(
                            "{0}: Repo path '{1}' does not exist, will try using '{2}' instead.",
                            sourceLocation, repoPath, foundPath), level: LogLevel.Warning);
                    }
                    repoPath = foundPath;
                } else {
                    PlayServicesResolver.Log(String.Format(
                        "{0}: Repo path '{1}' does not exist.", sourceLocation, repoPath),
                        level: LogLevel.Warning);
                }
            }
            return GradleWrapper.EscapeUri(GradleWrapper.PathToFileUri(repoPath));
        }

        /// <summary>
        /// Extract the ordered set of repository URIs from the specified dependencies.
        /// </summary>
        /// <param name="dependencies">Dependency instances to query for repos.</param>
        /// <returns>Dictionary of source filenames by repo names.</returns>
        internal static List<KeyValuePair<string, string>> DependenciesToRepoUris(
                IEnumerable<Dependency> dependencies) {
            var sourcesByRepo = new OrderedDictionary();
            Action<string, string> addToSourcesByRepo = (repo, source) => {
                if (!String.IsNullOrEmpty(repo)) {
                    if (sourcesByRepo.Contains(repo)) {
                        var sources = (List<string>)sourcesByRepo[repo];
                        if (!sources.Contains(source)) {
                            sources.Add(source);
                        }
                    } else {
                        sourcesByRepo[repo] = new List<string>() { source };
                    }
                }
            };
            // Add global repos first.
            foreach (var kv in PlayServicesSupport.AdditionalRepositoryPaths) {
                addToSourcesByRepo(RepoPathToUri(kv.Key, sourceLocation: kv.Value), kv.Value);
            }
            // Build array of repos to search, they're interleaved across all dependencies as the
            // order matters.
            int maxNumberOfRepos = 0;
            foreach (var dependency in dependencies) {
                maxNumberOfRepos = Math.Max(maxNumberOfRepos, dependency.Repositories.Length);
            }
            for (int i = 0; i < maxNumberOfRepos; i++) {
                foreach (var dependency in dependencies) {
                    var repos = dependency.Repositories;
                    if (i >= repos.Length) continue;
                    var createdBy = CommandLine.SplitLines(dependency.CreatedBy)[0];
                    addToSourcesByRepo(RepoPathToUri(repos[i], sourceLocation: createdBy),
                                       createdBy);
                }
            }
            var sourcesByRepoList = new List<KeyValuePair<string, string>>();
            var enumerator = sourcesByRepo.GetEnumerator();
            while (enumerator.MoveNext()) {
                sourcesByRepoList.Add(
                    new KeyValuePair<string, string>(
                        (string)enumerator.Key,
                        String.Join(", ", ((List<string>)enumerator.Value).ToArray())));
            }
            return sourcesByRepoList;
        }

        // Holds Gradle resolution state.
        private class ResolutionState {
            public CommandLine.Result commandLineResult = new CommandLine.Result();
            public List<string> copiedArtifacts = new List<string>();
            public List<string> missingArtifacts = new List<string>();
            public List<Dependency> missingArtifactsAsDependencies = new List<Dependency>();
            public List<string> modifiedArtifacts = new List<string>();
        }

        /// <summary>
        /// Perform resolution using Gradle.
        /// </summary>
        /// <param name="destinationDirectory">Directory to copy packages into.</param>
        /// <param name="androidSdkPath">Path to the Android SDK.  This is required as
        /// PlayServicesSupport.SDK can only be called from the main thread.</param>
        /// <param name="logErrorOnMissingArtifacts">Log errors when artifacts are missing.</param>
        /// <param name="closeWindowOnCompletion">Whether to unconditionally close the resolution
        /// window when complete.</param>
        /// <param name="resolutionComplete">Called when resolution is complete with a list of
        /// packages that were not found.</param>
        private void GradleResolution(
                string destinationDirectory, string androidSdkPath,
                bool logErrorOnMissingArtifacts, bool closeWindowOnCompletion,
                System.Action<List<Dependency>> resolutionComplete) {
            // Get all dependencies.
            var allDependencies = PlayServicesSupport.GetAllDependencies();
            var allDependenciesList = new List<Dependency>(allDependencies.Values);

            PlayServicesResolver.analytics.Report(
                "/resolve/gradle", PlayServicesResolver.GetResolutionMeasurementParameters(null),
                "Gradle Resolve");

            var gradleWrapper = PlayServicesResolver.Gradle;
            var buildScript = Path.GetFullPath(Path.Combine(
                gradleWrapper.BuildDirectory,
                PlayServicesResolver.EMBEDDED_RESOURCES_NAMESPACE + "download_artifacts.gradle"));

            // Extract the gradle wrapper and build script.
            if (!(gradleWrapper.Extract(PlayServicesResolver.logger) &&
                  EmbeddedResource.ExtractResources(
                      typeof(GradleResolver).Assembly,
                      new KeyValuePair<string, string>[] {
                          new KeyValuePair<string, string>(null, buildScript),
                          // Copies the settings.gradle file into this folder to mark it as a Gradle
                          // project. Without the settings.gradle file, Gradle will search up all
                          // parent directories for a settings.gradle and prevent execution of the
                          // download_artifacts.gradle script if a settings.gradle is found.
                          new KeyValuePair<string, string>(
                              PlayServicesResolver.EMBEDDED_RESOURCES_NAMESPACE + "settings.gradle",
                              Path.GetFullPath(Path.Combine(gradleWrapper.BuildDirectory,
                                                            "settings.gradle"))),
                      }, PlayServicesResolver.logger))) {
                PlayServicesResolver.analytics.Report("/resolve/gradle/failed/extracttools",
                                                      "Gradle Resolve: Tool Extraction Failed");
                PlayServicesResolver.Log(String.Format(
                        "Failed to extract {0} and {1} from assembly {2}",
                        gradleWrapper.Executable, buildScript,
                        typeof(GradleResolver).Assembly.FullName),
                    level: LogLevel.Error);
                resolutionComplete(allDependenciesList);
                return;
            }
            // Build array of repos to search, they're interleaved across all dependencies as the
            // order matters.
            var repoList = new List<string>();
            foreach (var kv in DependenciesToRepoUris(allDependencies.Values)) repoList.Add(kv.Key);

            // Create an instance of ResolutionState to aggregate the results.
            var resolutionState = new ResolutionState();

            // Window used to display resolution progress.
            var window = CommandLineDialog.CreateCommandLineDialog(
                "Resolving Android Dependencies");

            // Register an event to redirect log messages to the resolution window.
            var logRedirector = window.Redirector;
            PlayServicesResolver.logger.LogMessage += logRedirector.LogToWindow;

            // When resolution is complete unregister the log redirection event.
            Action resolutionCompleteRestoreLogger = () => {
                PlayServicesResolver.logger.LogMessage -= logRedirector.LogToWindow;
                // If the command completed successfully or the log level is info or above close
                // the window, otherwise leave it open for inspection.
                if ((resolutionState.commandLineResult.exitCode == 0 &&
                     PlayServicesResolver.logger.Level >= LogLevel.Info &&
                     !(logRedirector.WarningLogged || logRedirector.ErrorLogged)) ||
                    closeWindowOnCompletion) {
                    window.Close();
                }
                resolutionComplete(resolutionState.missingArtifactsAsDependencies);
            };

            // Executed after refreshing the explode cache.
            Action processAars = () => {
                // Find all labeled files that were not copied and delete them.
                var staleArtifacts = new HashSet<string>();
                var copiedArtifactsSet = new HashSet<string>(resolutionState.copiedArtifacts);
                foreach (var assetPath in PlayServicesResolver.FindLabeledAssets()) {
                    if (!copiedArtifactsSet.Contains(FileUtils.PosixPathSeparators(assetPath))) {
                        staleArtifacts.Add(assetPath);
                    }
                }
                if (staleArtifacts.Count > 0) {
                    PlayServicesResolver.Log(
                        String.Format("Deleting stale dependencies:\n{0}",
                                      String.Join("\n",
                                                  (new List<string>(staleArtifacts)).ToArray())),
                        level: LogLevel.Verbose);
                    var deleteFailures = new List<string>();
                    foreach (var assetPath in staleArtifacts) {
                        deleteFailures.AddRange(FileUtils.DeleteExistingFileOrDirectory(assetPath));
                    }
                    var deleteError = FileUtils.FormatError("Failed to delete stale artifacts",
                                                            deleteFailures);
                    if (!String.IsNullOrEmpty(deleteError)) {
                        PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                    }
                }
                // Process / explode copied AARs.
                ProcessAars(
                    destinationDirectory, new HashSet<string>(resolutionState.copiedArtifacts),
                    (progress, message) => {
                        window.SetProgress("Processing libraries...", progress, message);
                    },
                    () => {
                        // Look up the original Dependency structure for each missing artifact.
                        resolutionState.missingArtifactsAsDependencies = new List<Dependency>();
                        foreach (var artifact in resolutionState.missingArtifacts) {
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
                            resolutionState.missingArtifactsAsDependencies.Add(dep);
                        }
                        if (logErrorOnMissingArtifacts) {
                            LogMissingDependenciesError(resolutionState.missingArtifacts);
                        }
                        resolutionCompleteRestoreLogger();
                    });
            };

            // Executed when Gradle finishes execution.
            CommandLine.CompletionHandler gradleComplete = (commandLineResult) => {
                resolutionState.commandLineResult = commandLineResult;
                if (commandLineResult.exitCode != 0) {
                    PlayServicesResolver.analytics.Report("/resolve/gradle/failed/fetch",
                                                          "Gradle Resolve: Tool Extraction Failed");
                    resolutionState.missingArtifactsAsDependencies = allDependenciesList;
                    PlayServicesResolver.Log(
                        String.Format("Gradle failed to fetch dependencies.\n\n{0}",
                                      commandLineResult.message), level: LogLevel.Error);
                    resolutionCompleteRestoreLogger();
                    return;
                }
                // Parse stdout for copied and missing artifacts.
                ParseDownloadGradleArtifactsGradleOutput(commandLineResult.stdout,
                                                         destinationDirectory,
                                                         out resolutionState.copiedArtifacts,
                                                         out resolutionState.missingArtifacts,
                                                         out resolutionState.modifiedArtifacts);
                // Display a warning about modified artifact versions.
                if (resolutionState.modifiedArtifacts.Count > 0) {
                    PlayServicesResolver.Log(
                        String.Format(
                            "Some conflicting dependencies were found.\n" +
                            "The following dependency versions were modified:\n" +
                            "{0}\n",
                            String.Join("\n", resolutionState.modifiedArtifacts.ToArray())),
                        level: LogLevel.Warning);
                }
                // Label all copied files.
                PlayServicesResolver.LabelAssets(
                    resolutionState.copiedArtifacts,
                    complete: (unusedUnlabeled) => {
                        // Check copied files for Jetpack (AndroidX) libraries.
                        if (PlayServicesResolver.FilesContainJetpackLibraries(
                            resolutionState.copiedArtifacts)) {
                            PlayServicesResolver.analytics.Report(
                                "/resolve/gradle/androidxdetected",
                                "Gradle Resolve: AndroidX detected");
                            bool jetifierEnabled = SettingsDialog.UseJetifier;
                            SettingsDialog.UseJetifier = true;
                            // Make sure Jetpack is supported, prompting the user to configure Unity
                            // in a supported configuration.
                            PlayServicesResolver.CanEnableJetifierOrPromptUser(
                                "Jetpack (AndroidX) libraries detected, ", (useJetifier) => {
                                    if (useJetifier) {
                                        PlayServicesResolver.analytics.Report(
                                            "/resolve/gradle/enablejetifier/enable",
                                            "Gradle Resolve: Enable Jetifier");
                                        if (jetifierEnabled != SettingsDialog.UseJetifier) {
                                            PlayServicesResolver.Log(
                                                "Detected Jetpack (AndroidX) libraries, enabled " +
                                                "the jetifier and resolving again.");
                                            // Run resolution again with the Jetifier enabled.
                                            PlayServicesResolver.DeleteLabeledAssets();
                                            GradleResolution(destinationDirectory,
                                                             androidSdkPath,
                                                             logErrorOnMissingArtifacts,
                                                             closeWindowOnCompletion,
                                                             resolutionComplete);
                                            return;
                                        }
                                        processAars();
                                    } else {
                                        PlayServicesResolver.analytics.Report(
                                            "/resolve/gradle/enablejetifier/abort",
                                            "Gradle Resolve: Enable Jetifier Aborted");
                                        // If the user didn't change their configuration, delete all
                                        // resolved libraries and abort resolution as the build will
                                        // fail.
                                        PlayServicesResolver.DeleteLabeledAssets();
                                        resolutionState.missingArtifactsAsDependencies =
                                            allDependenciesList;
                                        resolutionCompleteRestoreLogger();
                                        return;
                                    }
                                });
                        } else {
                            // Successful, proceed with processing libraries.
                            processAars();
                        }
                    },
                    synchronous: false,
                    progressUpdate: (progress, message) => {
                        window.SetProgress("Labeling libraries...", progress, message);
                    });
            };

            var packageSpecs =
                new List<string>(DependenciesToPackageSpecs(allDependencies.Values).Keys);

            var androidGradlePluginVersion = PlayServicesResolver.AndroidGradlePluginVersion;
            // If this version of Unity doesn't support Gradle builds use a relatively
            // recent (June 2019) version of the data binding library.
            if (String.IsNullOrEmpty(androidGradlePluginVersion)) {
                androidGradlePluginVersion = "2.3.0";
            }
            var gradleProjectProperties = new Dictionary<string, string>() {
                { "ANDROID_HOME", androidSdkPath },
                { "TARGET_DIR", Path.GetFullPath(destinationDirectory) },
                { "MAVEN_REPOS", String.Join(";", repoList.ToArray()) },
                { "PACKAGES_TO_COPY", String.Join(";", packageSpecs.ToArray()) },
                { "USE_JETIFIER", SettingsDialog.UseJetifier ? "1" : "0" },
                { "DATA_BINDING_VERSION", androidGradlePluginVersion }
            };

            // Run the build script to perform the resolution popping up a window in the editor.
            window.summaryText = "Resolving Android Dependencies...";
            window.modal = false;
            window.progressTitle = window.summaryText;
            window.autoScrollToBottom = true;
            window.logger = PlayServicesResolver.logger;
            var maxProgressLines = (allDependenciesList.Count * 10) + 50;

            if (gradleWrapper.Run(
                    SettingsDialog.UseGradleDaemon, buildScript, gradleProjectProperties,
                    null, PlayServicesResolver.logger,
                    (string toolPath, string arguments) => {
                        window.RunAsync(
                            toolPath, arguments,
                            (result) => { RunOnMainThread.Run(() => { gradleComplete(result); }); },
                            workingDirectory: gradleWrapper.BuildDirectory,
                            maxProgressLines: maxProgressLines);
                        return true;
                    })) {
                window.Show();
            } else {
                resolutionComplete(allDependenciesList);
            }
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
        ///
        /// <param name="complete">Called when the operation is complete.</param>
        private void FindAndResolveConflicts(Action complete) {
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
                managedArtifacts[artifact] = filename;
                if (artifact.StartsWith("play-services-") ||
                    artifact.StartsWith("com.google.android.gms.play-services-")) {
                    managedPlayServicesArtifacts.Add(filename);
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

            foreach (var packaging in packagingExtensions) {
                foreach (var filename in
                         VersionHandlerImpl.SearchAssetDatabase(
                             String.Format("{0} t:Object", packaging), (string filtername) => {
                                 // Ignore all assets that are managed by the plugin and anything
                                 // that doesn't end with the packaging extension.
                                 return !managedArtifactFilenames.Contains(filtername) &&
                                   Path.GetExtension(filtername).ToLower() == packaging;
                             })) {
                    if (Path.GetFileName(filename).ToLower() == playServicesJar) {
                        playServicesJars.Add(filename);
                    } else {
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
                PlayServicesResolver.analytics.Report(
                    "/androidresolver/resolve/conflicts/duplicategoogleplayservices",
                    "Gradle Resolve: Duplicate Google Play Services Found");
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

            // List of conflicts that haven't been removed.
            var leftConflicts = new List<string>();
            // Reports conflict count.
            Action reportConflictCount = () => {
                int numberOfConflicts = conflicts.Count;
                if (numberOfConflicts > 0) {
                    PlayServicesResolver.analytics.Report(
                        "/androidresolver/resolve/conflicts/cleanup",
                        new KeyValuePair<string, string>[] {
                            new KeyValuePair<string, string>(
                                "numFound", numberOfConflicts.ToString()),
                            new KeyValuePair<string, string>(
                                "numRemoved",
                                (numberOfConflicts - leftConflicts.Count).ToString()),
                        },
                        "Gradle Resolve: Cleaned Up Conflicting Libraries");
                }
            };

            var conflictsEnumerator = conflicts.GetEnumerator();

            // Move to the next conflicting package and prompt the user to delete a package.
            Action promptToDeleteNextConflict = null;

            promptToDeleteNextConflict = () => {
                bool conflictEnumerationComplete = false;
                while (true) {
                    if (!conflictsEnumerator.MoveNext()) {
                        conflictEnumerationComplete = true;
                        break;
                    }

                    var conflict = conflictsEnumerator.Current;
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
                        DialogWindow.Display(
                            "Resolve Conflict?",
                            warningMessage +
                            "\n" +
                            "The conflicting libraries are older than the library managed by " +
                            "the Android Resolver.  Would you like to remove the old libraries " +
                            "to resolve the conflict?",
                            DialogWindow.Option.Selected0, "Yes", "No",
                            complete: (selectedOption) => {
                                bool deleted = false;
                                if (selectedOption == DialogWindow.Option.Selected0) {
                                    var deleteFailures = new List<string>();
                                    foreach (var filename in conflict.Value) {
                                        deleteFailures.AddRange(
                                            FileUtils.DeleteExistingFileOrDirectory(filename));
                                    }
                                    var deleteError = FileUtils.FormatError(
                                        "Unable to delete old libraries", deleteFailures);
                                    if (!String.IsNullOrEmpty(deleteError)) {
                                        PlayServicesResolver.Log(deleteError,
                                                                 level: LogLevel.Error);
                                    } else {
                                        deleted = true;
                                    }
                                }
                                if (!deleted) leftConflicts.Add(warningMessage);
                                promptToDeleteNextConflict();
                            });
                        // Continue iteration when the dialog is complete.
                        break;
                    }
                }

                if (conflictEnumerationComplete) {
                    reportConflictCount();
                    foreach (var warningMessage in leftConflicts) {
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
                    complete();
                }
            };
            // Start prompting the user to delete conflicts.
            promptToDeleteNextConflict();
        }

        /// <summary>
        /// Perform the resolution and the exploding/cleanup as needed.
        /// </summary>
        /// <param name="destinationDirectory">Destination directory.</param>
        /// <param name="closeWindowOnCompletion">Whether to unconditionally close the resolution
        /// window when complete.</param>
        /// <param name="resolutionComplete">Delegate called when resolution is complete.</param>
        public void DoResolution(string destinationDirectory, bool closeWindowOnCompletion,
                                 System.Action resolutionComplete) {
            // Run resolution on the main thread to serialize the operation as DoResolutionUnsafe
            // is not thread safe.
            RunOnMainThread.Run(() => {
                DoResolutionUnsafe(destinationDirectory, closeWindowOnCompletion,
                                   () => {
                                       FindAndResolveConflicts(resolutionComplete);
                                   });
            });
        }

        /// <summary>
        /// Perform the resolution and the exploding/cleanup as needed.
        /// This is *not* thread safe.
        /// </summary>
        /// <param name="destinationDirectory">Directory to store results of resolution.</param>
        /// <param name="closeWindowOnCompletion">Whether to unconditionally close the resolution
        /// window when complete.</param>
        /// <param name="resolutionComplete">Action called when resolution completes.</param>
        private void DoResolutionUnsafe(string destinationDirectory, bool closeWindowOnCompletion,
                                        System.Action resolutionComplete)
        {
            // Cache the setting as it can only be queried from the main thread.
            var sdkPath = PlayServicesResolver.AndroidSdkRoot;
            // If the Android SDK path isn't set or doesn't exist report an error.
            if (String.IsNullOrEmpty(sdkPath) || !Directory.Exists(sdkPath)) {
                PlayServicesResolver.analytics.Report("/resolve/gradle/failed/missingandroidsdk",
                                                      "Gradle Resolve: Failed Missing Android SDK");
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
                GradleResolution(destinationDirectory, sdkPath, true, closeWindowOnCompletion,
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
                if (installPackages.Count == 0 || !SettingsDialog.InstallAndroidPackages) {
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
                             !SettingsDialog.InstallAndroidPackages, closeWindowOnCompletion,
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
        /// Get the target path for an exploded AAR.
        /// </summary>
        /// <param name="aarPath">AAR file to explode.</param>
        /// <returns>Exploded AAR path.</returns>
        private string DetermineExplodedAarPath(string aarPath) {
            return Path.Combine(GooglePlayServices.SettingsDialog.PackageDir,
                                AarPathToPackageName(aarPath));
        }

        /// <summary>
        /// Processes the aars.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each aar copied is inspected and determined if it should be
        /// exploded into a directory or not. Unneeded exploded directories are
        /// removed.
        /// </para>
        /// <para>
        /// Exploding is needed if the version of Unity is old, or if the artifact
        /// has been explicitly flagged for exploding.  This allows the subsequent
        /// processing of variables in the AndroidManifest.xml file which is not
        /// supported by the current versions of the manifest merging process that
        /// Unity uses.
        /// </para>
        /// </remarks>
        /// <param name="dir">The directory to process.</param>
        /// <param name="updatedFiles">Set of files that were recently updated and should be
        /// processed.</param>
        /// <param name="progressUpdate">Called with the progress (0..1) and message that indicates
        /// processing progress.</param>
        /// <param name="complete">Executed when this process is complete.</param>
        private void ProcessAars(string dir, HashSet<string> updatedFiles,
                                 Action<float, string> progressUpdate, Action complete) {
            // Get set of AAR files and directories we're managing.
            var uniqueAars = new HashSet<string>(PlayServicesResolver.FindLabeledAssets());
            var aars = new Queue<string>(uniqueAars);

            int numberOfAars = aars.Count;
            if (numberOfAars == 0) {
                complete();
                return;
            }

            PlayServicesResolver.analytics.Report(
                "/resolve/gradle/processaars",
                new KeyValuePair<string, string>[] {
                    new KeyValuePair<string, string>("numPackages", numberOfAars.ToString())
                },
                "Gradle Resolve: Process AARs");
            var failures = new List<string>();

            // Processing can be slow so execute incrementally so we don't block the update thread.
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
                    progressUpdate(progress, aarPath);
                    PlayServicesResolver.Log(String.Format("Processing {0}", aarPath),
                                             level: LogLevel.Verbose);
                    if (updatedFiles.Contains(aarPath)) {
                        if (!ProcessAar(aarPath)) {
                            PlayServicesResolver.Log(String.Format(
                                "Failed to process {0}, your Android build will fail.\n" +
                                "See previous error messages for failure details.\n",
                                aarPath));
                            failures.Add(aarPath);
                        }
                    }
                } finally {
                    if (allAarsProcessed) {
                        progressUpdate(1.0f, "Library processing complete");
                        if (failures.Count == 0) {
                            PlayServicesResolver.analytics.Report(
                                "/resolve/gradle/processaars/success",
                                new KeyValuePair<string, string>[] {
                                    new KeyValuePair<string, string>("numPackages",
                                                                     numberOfAars.ToString())
                                },
                                "Gradle Resolve: Process AARs Succeeded");
                        } else {
                            PlayServicesResolver.analytics.Report(
                                "/resolve/gradle/processaars/failed",
                                new KeyValuePair<string, string>[] {
                                    new KeyValuePair<string, string>("numPackages",
                                                                     numberOfAars.ToString()),
                                    new KeyValuePair<string, string>("numPackagesFailed",
                                                                     failures.Count.ToString())
                                },
                                "Gradle Resolve: Process AARs Failed");
                        }
                        complete();
                    }
                }
                return allAarsProcessed;
            });
        }

        /// <summary>
        /// Gets a value indicating whether this version of Unity supports aar files.
        /// </summary>
        /// <value><c>true</c> if supports aar files; otherwise, <c>false</c>.</value>
        internal static bool SupportsAarFiles
        {
            get
            {
                // Get the version number.
                string majorVersion = Application.unityVersion.Split('.')[0];
                int ver;
                if (!int.TryParse(majorVersion, out ver))
                {
                    ver = 4;
                }
                return ver >= 5;
            }
        }

        /// <summary>
        /// Determines whether an AAR file should be processed.
        ///
        /// This is required for some AAR so that the plugin can perform variable expansion on
        /// manifests and ABI stripping.
        /// </summary>
        /// <param name="aarDirectory">Path of the unpacked AAR file to query.</param>
        /// <returns>true, if the AAR should be processed, false otherwise.</returns>
        internal static bool ShouldProcess(string aarDirectory) {
            // Unfortunately, as of Unity 5.5.0f3, Unity does not set the applicationId variable
            // in the build.gradle it generates.  This results in non-functional libraries that
            // require the ${applicationId} variable to be expanded in their AndroidManifest.xml.
            // To work around this when Gradle builds are enabled, explosion is enabled for all
            // AARs that require variable expansion unless this behavior is explicitly disabled
            // in the settings dialog.
            if (!SettingsDialog.ExplodeAars) {
                return false;
            }
            // If this version of Unity doesn't support AAR files, always explode.
            if (!SupportsAarFiles) return true;

            const string manifestFilename = "AndroidManifest.xml";
            const string classesFilename = "classes.jar";
            string manifestPath = Path.Combine(aarDirectory, manifestFilename);
            if (File.Exists(manifestPath)) {
                string manifest = File.ReadAllText(manifestPath);
                if (manifest.IndexOf("${applicationId}") >= 0) return true;
            }

            // If the AAR is badly formatted (e.g does not contain classes.jar)
            // explode it so that we can create classes.jar.
            if (!File.Exists(Path.Combine(aarDirectory, classesFilename))) return true;

            // If the AAR contains more than one ABI and Unity's build is
            // targeting a single ABI, explode it so that unused ABIs can be
            // removed.
            var availableAbis = AarDirectoryFindAbis(aarDirectory);
            // Unity 2017's internal build system does not support AARs that contain
            // native libraries so force explosion to pick up native libraries using
            // Eclipse / Ant style projects.
            if (availableAbis != null &&
                Google.VersionHandler.GetUnityVersionMajorMinor() >= 2017.0f) {
                return true;
            }
            // NOTE: Unfortunately as of Unity 5.5 the internal Gradle build also blindly
            // includes all ABIs from AARs included in the project so we need to explode the
            // AARs and remove unused ABIs.
            if (availableAbis != null) {
                var abisToRemove = availableAbis.ToSet();
                abisToRemove.ExceptWith(AndroidAbis.Current.ToSet());
                if (abisToRemove.Count > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Create an AAR from the specified directory.
        /// </summary>
        /// <param name="aarFile">AAR file to create.</param>
        /// <param name="inputDirectory">Directory which contains the set of files to store
        /// in the AAR.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal static bool ArchiveAar(string aarFile, string inputDirectory) {
            try {
                string aarPath = Path.GetFullPath(aarFile);
                CommandLine.Result result = CommandLine.Run(
                    JavaUtilities.JarBinaryPath,
                    String.Format("cvf{0} \"{1}\" -C \"{2}\" .",
                                  aarFile.ToLower().EndsWith(".jar") ? "" : "M", aarPath,
                                  inputDirectory));
                if (result.exitCode != 0) {
                    Debug.LogError(String.Format("Error archiving {0}\n" +
                                                 "Exit code: {1}\n" +
                                                 "{2}\n" +
                                                 "{3}\n",
                                                 aarPath, result.exitCode, result.stdout,
                                                 result.stderr));
                    return false;
                }
            } catch (Exception e) {
                Debug.LogError(e);
                throw e;
            }
            return true;
        }

        // Native library ABI subdirectories supported by Unity.
        // Directories that contain native libraries within a Unity Android library project.
        private static string[] NATIVE_LIBRARY_DIRECTORIES = new string[] { "libs", "jni" };

        /// <summary>
        /// Get the set of native library ABIs in an exploded AAR.
        /// </summary>
        /// <param name="aarDirectory">Directory to search for ABIs.</param>
        /// <returns>Set of ABI directory names in the exploded AAR or null if none are
        /// found.</returns>
        internal static AndroidAbis AarDirectoryFindAbis(string aarDirectory) {
            var foundAbis = new HashSet<string>();
            foreach (var libDirectory in NATIVE_LIBRARY_DIRECTORIES) {
                foreach (var abiDir in AndroidAbis.AllSupported) {
                    if (Directory.Exists(Path.Combine(aarDirectory,
                                                      Path.Combine(libDirectory, abiDir)))) {
                        foundAbis.Add(abiDir);
                    }
                }
            }
            return foundAbis.Count > 0 ? new AndroidAbis(foundAbis) : null;
        }

        /// <summary>
        /// Process an AAR so that it can be included in a Unity build.
        /// </summary>
        /// <param name="aarFile">Aar file to process.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal static bool ProcessAar(string aarFile) {
            PlayServicesResolver.Log(String.Format("ProcessAar {0}", aarFile),
                                     level: LogLevel.Verbose);

            // If this isn't an aar ignore it.
            if (!aarFile.ToLower().EndsWith(".aar")) return true;

            string aarDirName = Path.GetFileNameWithoutExtension(aarFile);
            // Output directory for the contents of the AAR / JAR.
            string outputDir = Path.Combine(Path.GetDirectoryName(aarFile), aarDirName);
            string stagingDir = FileUtils.CreateTemporaryDirectory();
            if (stagingDir == null) {
                PlayServicesResolver.Log(String.Format(
                        "Unable to create temporary directory to process AAR {0}", aarFile),
                    level: LogLevel.Error);
                return false;
            }
            try {
                string workingDir = Path.Combine(stagingDir, aarDirName);
                var deleteError = FileUtils.FormatError(
                    String.Format("Failed to create working directory to process AAR {0}",
                                  aarFile), FileUtils.DeleteExistingFileOrDirectory(workingDir));
                if (!String.IsNullOrEmpty(deleteError)) {
                    PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                    return false;
                }
                Directory.CreateDirectory(workingDir);

                if (!PlayServicesResolver.ExtractZip(aarFile, null, workingDir, false)) {
                    return false;
                }

                bool process = ShouldProcess(workingDir);
                // Determine whether an Ant style project should be generated for this artifact.
                bool antProject = process && !PlayServicesResolver.GradleBuildEnabled;

                // If the AAR doesn't need to be processed or converted into an Ant project,
                // we're done.
                if (!(process || antProject)) return true;

                PlayServicesResolver.ReplaceVariablesInAndroidManifest(
                    Path.Combine(workingDir, "AndroidManifest.xml"),
                    PlayServicesResolver.GetAndroidApplicationId(),
                    new Dictionary<string, string>());

                string nativeLibsDir = null;
                if (antProject) {
                    // Create the libs directory to store the classes.jar and non-Java shared
                    // libraries.
                    string libDir = Path.Combine(workingDir, "libs");
                    nativeLibsDir = libDir;
                    Directory.CreateDirectory(libDir);

                    // Move the classes.jar file to libs.
                    string classesFile = Path.Combine(workingDir, "classes.jar");
                    string targetClassesFile = Path.Combine(libDir, Path.GetFileName(classesFile));
                    if (File.Exists(targetClassesFile)) File.Delete(targetClassesFile);
                    if (File.Exists(classesFile)) {
                        FileUtils.MoveFile(classesFile, targetClassesFile);
                    } else {
                        // Some libraries publish AARs that are poorly formatted (e.g missing
                        // a classes.jar file).  Firebase's license AARs at certain versions are
                        // examples of this.  When Unity's internal build system detects an Ant
                        // project or AAR without a classes.jar, the build is aborted.  This
                        // generates an empty classes.jar file to workaround the issue.
                        string emptyClassesDir = Path.Combine(stagingDir, "empty_classes_jar");
                        Directory.CreateDirectory(emptyClassesDir);
                        if (!ArchiveAar(targetClassesFile, emptyClassesDir)) return false;
                    }
                }

                // Copy non-Java shared libraries (.so) files from the "jni" directory into the
                // lib directory so that Unity's legacy (Ant-like) build system includes them in the
                // built APK.
                string jniLibDir = Path.Combine(workingDir, "jni");
                nativeLibsDir = nativeLibsDir ?? jniLibDir;
                if (Directory.Exists(jniLibDir)) {
                    var abisInArchive = AarDirectoryFindAbis(workingDir);
                    if (jniLibDir != nativeLibsDir) {
                        FileUtils.CopyDirectory(jniLibDir, nativeLibsDir);
                        deleteError = FileUtils.FormatError(
                            String.Format("Unable to delete JNI directory from AAR {0}", aarFile),
                            FileUtils.DeleteExistingFileOrDirectory(jniLibDir));
                        if (!String.IsNullOrEmpty(deleteError)) {
                            PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                            return false;
                        }
                    }
                    if (abisInArchive != null) {
                        // Remove shared libraries for all ABIs that are not required for the
                        // selected ABIs.
                        var activeAbisSet = AndroidAbis.Current.ToSet();
                        var abisInArchiveSet = abisInArchive.ToSet();
                        var abisInArchiveToRemoveSet = new HashSet<string>(abisInArchiveSet);
                        abisInArchiveToRemoveSet.ExceptWith(activeAbisSet);

                        Func<IEnumerable<string>, string> setToString = (setToConvert) => {
                            return String.Join(", ", (new List<string>(setToConvert)).ToArray());
                        };
                        PlayServicesResolver.Log(
                            String.Format(
                                "Target ABIs [{0}], ABIs [{1}] in {2}, will remove [{3}] ABIs",
                                setToString(activeAbisSet),
                                setToString(abisInArchiveSet),
                                aarFile,
                                setToString(abisInArchiveToRemoveSet)),
                            level: LogLevel.Verbose);

                        foreach (var abiToRemove in abisInArchiveToRemoveSet) {
                            abisInArchiveSet.Remove(abiToRemove);
                            deleteError = FileUtils.FormatError(
                                String.Format("Unable to remove unused ABIs from {0}", aarFile),
                                FileUtils.DeleteExistingFileOrDirectory(
                                    Path.Combine(nativeLibsDir, abiToRemove)));
                            if (!String.IsNullOrEmpty(deleteError)) {
                                PlayServicesResolver.Log(deleteError, LogLevel.Warning);
                            }
                        }
                    }
                }

                if (antProject) {
                    // Create the project.properties file which indicates to Unity that this
                    // directory is a plugin.
                    string projectProperties = Path.Combine(workingDir, "project.properties");
                    if (!File.Exists(projectProperties)) {
                        File.WriteAllLines(projectProperties, new [] {
                            "# Project target.",
                            "target=android-9",
                            "android.library=true"
                        });
                    }
                    PlayServicesResolver.Log(
                        String.Format("Creating Ant project: Replacing {0} with {1}", aarFile,
                                      outputDir), level: LogLevel.Verbose);
                    // Clean up the aar file.
                    deleteError = FileUtils.FormatError(
                        String.Format("Failed to clean up AAR file {0} after generating " +
                                      "Ant project {1}", aarFile, outputDir),
                        FileUtils.DeleteExistingFileOrDirectory(Path.GetFullPath(aarFile)));
                    if (!String.IsNullOrEmpty(deleteError)) {
                        PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                        return false;
                    }
                    // Create the output directory.
                    FileUtils.MoveDirectory(workingDir, outputDir);
                    // Add a tracking label to the exploded files.
                    PlayServicesResolver.LabelAssets(new [] { outputDir });
                } else {
                    // Add a tracking label to the exploded files just in-case packaging fails.
                    PlayServicesResolver.Log(String.Format("Repacking {0} from {1}",
                                                           aarFile, workingDir),
                                             level: LogLevel.Verbose);
                    // Create a new AAR file.
                    deleteError = FileUtils.FormatError(
                        String.Format("Failed to replace AAR file {0}", aarFile),
                        FileUtils.DeleteExistingFileOrDirectory(Path.GetFullPath(aarFile)));
                    if (!String.IsNullOrEmpty(deleteError)) {
                        PlayServicesResolver.Log(deleteError, level: LogLevel.Error);
                        return false;
                    }
                    if (!ArchiveAar(aarFile, workingDir)) return false;
                    PlayServicesResolver.LabelAssets(new [] { aarFile });
                }
            } catch (Exception e) {
                PlayServicesResolver.Log(String.Format("Failed to process AAR {0} ({1}",
                                                       aarFile, e),
                                         level: LogLevel.Error);
            } finally {
                // Clean up the temporary directory.
                var deleteError = FileUtils.FormatError(
                    String.Format("Failed to clean up temporary folder while processing {0}",
                                  aarFile), FileUtils.DeleteExistingFileOrDirectory(stagingDir));
                if (!String.IsNullOrEmpty(deleteError)) {
                    PlayServicesResolver.Log(deleteError, level: LogLevel.Warning);
                }
            }
            return true;
        }
    }
}

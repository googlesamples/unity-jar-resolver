// <copyright file="GradleTemplateResolver.cs" company="Google Inc.">
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
    using System.Text.RegularExpressions;

    using Google;
    using Google.JarResolver;

    using UnityEditor;

    /// <summary>
    /// Resolver which simply injects dependencies into a gradle template file.
    /// </summary>
    internal class GradleTemplateResolver {

        /// <summary>
        /// Path of the Gradle template file.
        /// </summary>
        public static string GradleTemplatePath =
            Path.Combine(SettingsDialog.AndroidPluginsDir, "mainTemplate.gradle");

        /// <summary>
        /// Line that indicates the start of the injected repos block in the template.
        /// </summary>
        private const string ReposStartLine = "// Android Resolver Repos Start";

        /// <summary>
        /// Line that indicates the end of the injected repos block in the template.
        /// </summary>
        private const string ReposEndLine = "// Android Resolver Repos End";

        /// <summary>
        /// Line that indicates where to initially inject repos in the default template.
        /// </summary>
        private const string ReposInjectionLine =
            @".*apply plugin: 'com\.android\.(application|library)'.*";

        /// <summary>
        /// Line that indicates the start of the injected dependencies block in the template.
        /// </summary>
        private const string DependenciesStartLine = "// Android Resolver Dependencies Start";

        /// <summary>
        /// Line that indicates the end of the injected dependencies block in the template.
        /// </summary>
        private const string DependenciesEndLine = "// Android Resolver Dependencies End";

        /// <summary>
        /// Token that indicates where dependencies should initially be injected.
        /// If this isn't present in the template dependencies will not be injected or they'll
        /// be removed.
        /// </summary>
        private const string DependenciesToken = @".*\*\*DEPS\*\*.*";

        /// <summary>
        /// Line that indicates the start of the injected exclusions block in the template.
        /// </summary>
        private const string PackagingOptionsStartLine = "// Android Resolver Exclusions Start";

        /// <summary>
        /// Line that indicates the end of the injected exclusions block in the template.
        /// </summary>
        private const string PackagingOptionsEndLine = "// Android Resolver Exclusions End";

        /// <summary>
        /// Token that indicates where exclusions should be injected.
        /// </summary>
        private const string PackagingOptionsToken = @"android +{";

        /// <summary>
        /// Copy srcaar files to aar files that are excluded from Unity's build process.
        /// </summary>
        /// <param name="dependencies">Dependencies to inject.</param>
        /// <returns>true if successful, false otherwise.</returns>
        private static bool CopySrcAars(ICollection<Dependency> dependencies) {
            bool succeeded = true;
            var aarFiles = new List<KeyValuePair<string, string>>();
            // Copy each .srcaar file to .aar while configuring the plugin importer to ignore the
            // file.
            foreach (var aar in LocalMavenRepository.FindAarsInLocalRepos(dependencies)) {
                var aarPath = aar;
                if (FileUtils.IsUnderPackageDirectory(aar)) {
                    var logicalPackagePath = FileUtils.GetPackageDirectory(aar,
                            FileUtils.PackageDirectoryType.AssetDatabasePath);
                    aarPath = FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                        aar, logicalPackagePath);
                }
                var dir = FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                        Path.GetDirectoryName(aar),
                        GooglePlayServices.SettingsDialog.LocalMavenRepoDir);
                var filename = Path.GetFileNameWithoutExtension(aarPath);
                var targetFilename = Path.Combine(dir, filename + ".aar");
                bool configuredAar = File.Exists(targetFilename);
                if (!configuredAar) {
                    var error = PlayServicesResolver.CopyAssetAndLabel(
                            aarPath, targetFilename);
                    if (String.IsNullOrEmpty(error)) {
                        try {
                            PluginImporter importer = (PluginImporter)AssetImporter.GetAtPath(
                                targetFilename);
                            importer.SetCompatibleWithAnyPlatform(false);
                            importer.SetCompatibleWithPlatform(BuildTarget.Android, false);
                            configuredAar = true;
                        } catch (Exception ex) {
                            PlayServicesResolver.Log(String.Format(
                                "Failed to disable {0} from being included by Unity's " +
                                "internal build.  {0} has been deleted and will not be " +
                                "included in Gradle builds. ({1})", aar, ex),
                                level: LogLevel.Error);
                        }
                    } else {
                        PlayServicesResolver.Log(String.Format(
                            "Unable to copy {0} to {1}.  {1} will not be included in Gradle " +
                            "builds. Reason: {2}", aarPath, targetFilename, error),
                            level: LogLevel.Error);
                    }
                }
                if (configuredAar) {
                    aarFiles.Add(new KeyValuePair<string, string>(aarPath, targetFilename));
                    // Some versions of Unity do not mark the asset database as dirty when
                    // plugin importer settings change so reimport the asset to synchronize
                    // the state.
                    AssetDatabase.ImportAsset(targetFilename, ImportAssetOptions.ForceUpdate);
                } else {
                    if (File.Exists(targetFilename)) {
                        AssetDatabase.DeleteAsset(targetFilename);
                    }
                    succeeded = false;
                }
            }
            foreach (var keyValue in aarFiles) {
                succeeded &= LocalMavenRepository.PatchPomFile(keyValue.Value, keyValue.Key);
            }
            return succeeded;
        }

        /// <summary>
        /// Finds an area in a set of lines to inject a block of text.
        /// </summary>
        private class TextFileLineInjector {
            // Token which, if found within a line, indicates where to inject the block of text if
            // the start / end block isn't found
            private Regex injectionToken;
            // Line that indicates the start of the block to replace.
            private string startBlockLine;
            // Line that indicates the end of the block to replace.
            private string endBlockLine;
            // Lines to inject.
            private List<string> replacementLines;
            // Shorthand name of the replacement block.
            private string replacementName;
            // Description of the file being modified.
            private string fileDescription;
            // Whether replacementLines has been injected.
            private bool injected = false;
            // Whether the injector is tracking a line between startBlockLine and endBlockLine.
            private bool inBlock = false;

            /// <summary>
            /// Construct the injector.
            /// </summary>
            /// <param name="injectionToken">Regular expression, if found within a line,
            /// indicates where to inject the block of text if the start / end block isn't
            /// found.</param>
            /// <param name="startBlockLine">Line which indicates the start of the block to
            /// replace.</param>
            /// <param name="endBlockLine">Line which indicates the end of the block to replace.
            /// </param>
            /// <param name="replacementLines">Lines to inject.</param>
            /// <param name="replacementName">Shorthand name of the replacement block.</param>
            /// <param name="fileDescription">Description of the file being modified.</param>
            public TextFileLineInjector(string injectionToken,
                                        string startBlockLine,
                                        string endBlockLine,
                                        ICollection<string> replacementLines,
                                        string replacementName,
                                        string fileDescription) {
                this.injectionToken = new Regex(injectionToken);
                this.startBlockLine = startBlockLine;
                this.endBlockLine = endBlockLine;
                this.replacementLines = new List<string>();
                if (replacementLines.Count > 0) {
                    this.replacementLines.Add(startBlockLine);
                    this.replacementLines.AddRange(replacementLines);
                    this.replacementLines.Add(endBlockLine);
                }
                this.replacementName = replacementName;
                this.fileDescription = fileDescription;
            }

            /// <summary>
            /// Process a line returning the set of lines to emit for this line.
            /// </summary>
            /// <param name="line">Line to process.</param>
            /// <param name="injectionApplied">Whether lines were injected.</param>
            /// <returns>List of lines to emit for the specified line.</returns>
            public List<string> ProcessLine(string line, out bool injectionApplied) {
                var trimmedLine = line.Trim();
                var outputLines = new List<string> { line };
                bool injectBlock = false;
                injectionApplied = false;
                if (injected) {
                    return outputLines;
                }
                if (!inBlock) {
                    if (trimmedLine.StartsWith(startBlockLine)) {
                        inBlock = true;
                        outputLines.Clear();
                    } else if (injectionToken.IsMatch(trimmedLine)) {
                        injectBlock = true;
                    }
                } else {
                    outputLines.Clear();
                    if (trimmedLine.StartsWith(endBlockLine)) {
                        inBlock = false;
                        injectBlock = true;
                    }
                }
                if (injectBlock) {
                    injected = true;
                    injectionApplied = true;
                    if (replacementLines.Count > 0) {
                        PlayServicesResolver.Log(String.Format("Adding {0} to {1}",
                                                               replacementName, fileDescription),
                                                 level: LogLevel.Verbose);
                        outputLines.InsertRange(0, replacementLines);
                    }
                }
                return outputLines;
            }
        }

        /// <summary>
        /// Inject / update dependencies in the gradle template file.
        /// </summary>
        /// <param name="dependencies">Dependencies to inject.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool InjectDependencies(ICollection<Dependency> dependencies) {
            var resolutionMeasurementParameters =
                PlayServicesResolver.GetResolutionMeasurementParameters(null);
            if (dependencies.Count > 0) {
                PlayServicesResolver.analytics.Report(
                       "/resolve/gradletemplate", resolutionMeasurementParameters,
                       "Gradle Template Resolve");
            }

            var fileDescription = String.Format("gradle template {0}", GradleTemplatePath);
            PlayServicesResolver.Log(String.Format("Reading {0}", fileDescription),
                                     level: LogLevel.Verbose);
            IEnumerable<string> lines;
            try {
                lines = File.ReadAllLines(GradleTemplatePath);
            } catch (Exception ex) {
                PlayServicesResolver.analytics.Report(
                       "/resolve/gradletemplate/failed/templateunreadable",
                       "Gradle Template Resolve: Failed Template Unreadable");
                PlayServicesResolver.Log(
                    String.Format("Unable to patch {0} ({1})", fileDescription, ex.ToString()),
                    level: LogLevel.Error);
                return false;
            }

            PlayServicesResolver.Log(String.Format("Searching for {0} in {1}", DependenciesToken,
                                                   fileDescription),
                                     level: LogLevel.Verbose);
            // Determine whether dependencies should be injected.
            var dependenciesToken = new Regex(DependenciesToken);
            bool containsDeps = false;
            foreach (var line in lines) {
                if (dependenciesToken.IsMatch(line)) {
                    containsDeps = true;
                    break;
                }
            }

            // If a dependencies token isn't present report a warning and abort.
            if (!containsDeps) {
                PlayServicesResolver.analytics.Report(
                       "/resolve/gradletemplate/failed/noinjectionpoint",
                       "Gradle Template Resolve: Failed No Injection Point");
                PlayServicesResolver.Log(
                    String.Format("No {0} token found in {1}, Android Resolver libraries will " +
                                  "not be added to the file.", DependenciesToken, fileDescription),
                    level: LogLevel.Warning);
                return true;
            }

            // Copy all srcaar files in the project to aar filenames so that they'll be included in
            // the Gradle build.
            if (!CopySrcAars(dependencies)) {
                PlayServicesResolver.analytics.Report(
                       "/resolve/gradletemplate/failed/srcaarcopy",
                       "Gradle Template Resolve: Failed srcaar I/O");
                return false;
            }

            var repoLines = new List<string>();
            // Optionally enable the jetifier.
            if (SettingsDialog.UseJetifier && dependencies.Count > 0) {
                repoLines.AddRange(new [] {
                        "([rootProject] + (rootProject.subprojects as List)).each {",
                        "    ext {",
                        "        it.setProperty(\"android.useAndroidX\", true)",
                        "        it.setProperty(\"android.enableJetifier\", true)",
                        "    }",
                        "}"
                    });
            }
            repoLines.AddRange(PlayServicesResolver.GradleMavenReposLines(dependencies));

            TextFileLineInjector[] injectors = new [] {
                new TextFileLineInjector(ReposInjectionLine, ReposStartLine, ReposEndLine,
                                         repoLines, "Repos", fileDescription),
                new TextFileLineInjector(DependenciesToken, DependenciesStartLine,
                                         DependenciesEndLine,
                                         PlayServicesResolver.GradleDependenciesLines(
                                             dependencies, includeDependenciesBlock: false),
                                         "Dependencies", fileDescription),
                new TextFileLineInjector(PackagingOptionsToken, PackagingOptionsStartLine,
                                         PackagingOptionsEndLine,
                                         PlayServicesResolver.PackagingOptionsLines(dependencies),
                                         "Packaging Options", fileDescription),
            };
            // Lines that will be written to the output file.
            var outputLines = new List<string>();
            foreach (var line in lines) {
                var currentOutputLines = new List<string>();
                foreach (var injector in injectors) {
                    bool injectionApplied = false;
                    currentOutputLines = injector.ProcessLine(line, out injectionApplied);
                    if (injectionApplied || currentOutputLines.Count == 0) break;
                }
                outputLines.AddRange(currentOutputLines);
            }
            var inputText = String.Join("\n", (new List<string>(lines)).ToArray()) + "\n";
            var outputText = String.Join("\n", outputLines.ToArray()) + "\n";
            if (inputText == outputText) {
                PlayServicesResolver.Log(String.Format("No changes to {0}", fileDescription),
                                         level: LogLevel.Verbose);
                return true;
            }
            if (!FileUtils.CheckoutFile(GradleTemplatePath, PlayServicesResolver.logger)) {
                PlayServicesResolver.Log(
                    String.Format("Failed to checkout '{0}', unable to patch the file.",
                                  GradleTemplatePath), level: LogLevel.Error);
                PlayServicesResolver.analytics.Report(
                       "/resolve/gradletemplate/failed/checkout",
                       "Gradle Template Resolve: Failed to checkout");
                return false;
            }
            PlayServicesResolver.Log(
                String.Format("Writing updated {0}", fileDescription),
                level: LogLevel.Verbose);
            try {
                File.WriteAllText(GradleTemplatePath, outputText);
                PlayServicesResolver.analytics.Report(
                       "/resolve/gradletemplate/success", resolutionMeasurementParameters,
                       "Gradle Template Resolve Success");
            } catch (Exception ex) {
                PlayServicesResolver.analytics.Report(
                       "/resolve/gradletemplate/failed/write",
                       "Gradle Template Resolve: Failed to write");
                PlayServicesResolver.Log(
                    String.Format("Unable to patch {0} ({1})", fileDescription,
                                  ex.ToString()), level: LogLevel.Error);
                return false;
            }
            return true;
        }
    }
}

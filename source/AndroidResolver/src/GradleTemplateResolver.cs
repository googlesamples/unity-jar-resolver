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
        /// Path of the Gradle properties file.
        /// Available only from Unity version 2019.3 onwards
        /// </summary>
        public static string GradlePropertiesTemplatePath =
            Path.Combine(SettingsDialog.AndroidPluginsDir, "gradleTemplate.properties");

        /// <summary>
        /// Line that indicates the start of the injected properties block in properties template.
        /// </summary>
        private const string PropertiesStartLine = "# Android Resolver Properties Start";

        /// <summary>
        /// Line that indicates the end of the injected properties block in properties template.
        /// </summary>
        private const string PropertiesEndLine = "# Android Resolver Properties End";

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
        /// Token that indicates where gradle properties should initially be injected.
        /// If this isn't present in the properties template, properties will not be
        /// injected or they'll be removed.
        /// </summary>
        private const string PropertiesInjectionLine = @"ADDITIONAL_PROPERTIES";

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
                // Only need to copy for .srcaar
                if (Path.GetExtension(aar).CompareTo(".srcaar") != 0) {
                    continue;
                }

                var aarPath = aar;
                if (FileUtils.IsUnderPackageDirectory(aar)) {
                    // Physical paths work better for copy operations than
                    // logical Unity paths.
                    var physicalPackagePath = FileUtils.GetPackageDirectory(aar,
                            FileUtils.PackageDirectoryType.PhysicalPath);
                    aarPath = FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                        aar, physicalPackagePath);
                }
                var dir = FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                        Path.GetDirectoryName(aar),
                        GooglePlayServices.SettingsDialog.LocalMavenRepoDir);
                var filename = Path.GetFileNameWithoutExtension(aarPath);
                var targetFilename = Path.Combine(dir, filename + ".aar");

                // Avoid situations where we can have a mix of file path
                // separators based on platform.
                aarPath = FileUtils.NormalizePathSeparators(aarPath);
                targetFilename = FileUtils.NormalizePathSeparators(
                    targetFilename);

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
        /// Patch file contents by injecting custom data.
        /// </summary>
        /// <param name="filePath">Path to file to modify</param>
        /// <param name="fileDescription">Used in logs for describing the file</param>
        /// <param name="analyticsReportName">Name used in analytics logs</param>
        /// <param name="analyticsReportUrlToken">Token used in forming analytics path</param>
        /// <param name="injectors">Array of text injectors</param>
        /// <param name="resolutionMeasurementProperties">used in analytics reporting</param>
        /// <returns>true if successful, false otherwise.</returns>
        private static bool PatchFile(string filePath,
                                      string fileDescription,
                                      string analyticsReportName,
                                      string analyticsReportUrlToken,
                                      TextFileLineInjector[] injectors,
                                      ICollection<KeyValuePair<string, string>>
                                        resolutionMeasurementParameters) {
            IEnumerable<string> lines;
            try {
                lines = File.ReadAllLines(filePath);
            } catch (Exception ex) {
                PlayServicesResolver.analytics.Report(
                       "/resolve/" + analyticsReportUrlToken + "/failed/templateunreadable",
                       analyticsReportName + " Resolve: Failed Template Unreadable");
                PlayServicesResolver.Log(
                    String.Format("Unable to patch {0} ({1})", fileDescription, ex.ToString()),
                    level: LogLevel.Error);
                return false;
            }

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
            return WriteToFile(filePath, fileDescription, outputText,
                               analyticsReportName, analyticsReportUrlToken,
                               resolutionMeasurementParameters);
        }

        /// <summary>
        /// Write new contents to a file on disk
        /// </summary>
        /// <param name="filePath">Path to file to modify</param>
        /// <param name="fileDescription">Used in logs for describing the file</param>
        /// <param name="outputText">Updated contents to write to the file</param>
        /// <param name="analyticsReportName">Name used in analytics logs</param>
        /// <param name="analyticsReportUrlToken">Token used in forming analytics path</param>
        /// <param name="resolutionMeasurementProperties">used in analytics reporting</param>
        /// <returns>true if successful, false otherwise.</returns>
        private static bool WriteToFile(string filePath,
                                       string fileDescription,
                                       string outputText,
                                       string analyticsReportName,
                                       string analyticsReportUrlToken,
                                       ICollection<KeyValuePair<string, string>>
                                        resolutionMeasurementParameters) {
            if (!FileUtils.CheckoutFile(filePath, PlayServicesResolver.logger)) {
                PlayServicesResolver.Log(
                    String.Format("Failed to checkout '{0}', unable to patch the file.",
                                  filePath), level: LogLevel.Error);
                PlayServicesResolver.analytics.Report(
                       "/resolve/" + analyticsReportUrlToken + "/failed/checkout",
                        analyticsReportName + " Resolve: Failed to checkout");
                return false;
            }
            PlayServicesResolver.Log(
                String.Format("Writing updated {0}", fileDescription),
                level: LogLevel.Verbose);
            try {
                File.WriteAllText(filePath, outputText);
                PlayServicesResolver.analytics.Report(
                       "/resolve/"+analyticsReportUrlToken+"/success",
                        resolutionMeasurementParameters,
                       analyticsReportName + " Resolve Success");
            } catch (Exception ex) {
                PlayServicesResolver.analytics.Report(
                       "/resolve/"+analyticsReportUrlToken+"/failed/write",
                       analyticsReportName + " Resolve: Failed to write");
                PlayServicesResolver.Log(
                    String.Format("Unable to patch {0} ({1})", fileDescription,
                                  ex.ToString()), level: LogLevel.Error);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Inject properties in the gradle properties template file.
        /// Because of a change in structure of android projects built with
        /// Unity 2019.3 and above, the correct way to enable jetifier and
        /// Android X is by updating the gradle properties template.
        /// </summary>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool InjectProperties(){
            var resolutionMeasurementParameters =
                PlayServicesResolver.GetResolutionMeasurementParameters(null);
            PlayServicesResolver.analytics.Report(
                    "/resolve/gradleproperties", resolutionMeasurementParameters,
                    "Gradle Properties Resolve");
            var propertiesLines = new List<string>();
            // Lines to add Custom Gradle properties template to enable
            // jetifier and androidx
            propertiesLines.AddRange(new [] {
                    "android.useAndroidX=true",
                    "android.enableJetifier=true",
            });
            var propertiesFileDescription = String.Format(
                "gradle properties template" + GradlePropertiesTemplatePath);
            TextFileLineInjector[] propertiesInjectors = new [] {
                new TextFileLineInjector(PropertiesInjectionLine,
                                        PropertiesStartLine, PropertiesEndLine,
                                        propertiesLines,
                                        "Properties",
                                        propertiesFileDescription)
            };
            if (!PatchFile(GradlePropertiesTemplatePath, propertiesFileDescription,
                        "Gradle Properties", "gradleproperties",
                        propertiesInjectors,
                        resolutionMeasurementParameters)) {
                PlayServicesResolver.Log(
                    String.Format("Unable to patch " + propertiesFileDescription),
                    level: LogLevel.Error);
                return false;
            }
            return true;
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
                // For Unity versions lower than 2019.3 add jetifier and AndroidX
                // properties to custom main gradle template
                if (VersionHandler.GetUnityVersionMajorMinor() < 2019.3f) {
                    repoLines.AddRange(new [] {
                            "([rootProject] + (rootProject.subprojects as List)).each {",
                            "    ext {",
                            "        it.setProperty(\"android.useAndroidX\", true)",
                            "        it.setProperty(\"android.enableJetifier\", true)",
                            "    }",
                            "}"
                        });
                }
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
            return PatchFile(GradleTemplatePath, fileDescription,
                             "Gradle Template", "gradletemplate",
                             injectors, resolutionMeasurementParameters);
        }
    }
}

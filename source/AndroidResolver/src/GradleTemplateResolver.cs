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
        /// Filename of the Custom Gradle Properties Template file.
        /// </summary>
        public static string GradlePropertiesTemplateFilename = "gradleTemplate.properties";

        /// <summary>
        /// Path of the Custom Gradle Properties Template file in the Unity project.
        /// Available only from Unity version 2019.3 onwards
        /// </summary>
        public static string GradlePropertiesTemplatePath =
            Path.Combine(SettingsDialog.AndroidPluginsDir, GradlePropertiesTemplateFilename);

        /// <summary>
        /// Filename of the Custom Gradle Settings Template file.
        /// </summary>
        public static string GradleSettingsTemplatePathFilename = "settingsTemplate.gradle";

        /// <summary>
        /// Path of the Custom Gradle Settings Template file.
        /// </summary>
        public static string GradleSettingsTemplatePath =
            Path.Combine(SettingsDialog.AndroidPluginsDir, GradleSettingsTemplatePathFilename);

        public static string UnityGradleTemplatesDir {
            get {
                if (unityGradleTemplatesDir == null) {
                    var engineDir = PlayServicesResolver.AndroidPlaybackEngineDirectory;
                    if (String.IsNullOrEmpty(engineDir)) return null;
                    var gradleTemplateDir =
                        Path.Combine(Path.Combine(engineDir, "Tools"), "GradleTemplates");
                    unityGradleTemplatesDir = gradleTemplateDir;
                }
                return unityGradleTemplatesDir;
            }
        }
        private static string unityGradleTemplatesDir = null;

        public static string UnityGradleSettingsTemplatePath {
            get {
                return Path.Combine(
                        UnityGradleTemplatesDir,
                        GradleSettingsTemplatePathFilename);
            }
        }

        /// <summary>
        /// Line that indicates the start of the injected properties block in properties template.
        /// </summary>
        private const string PropertiesStartLine = "# Android Resolver Properties Start";

        /// <summary>
        /// Line that indicates the end of the injected properties block in properties template.
        /// </summary>
        private const string PropertiesEndLine = "# Android Resolver Properties End";

        /// <summary>
        /// Filename of the Custom Main Gradle Template file.
        /// </summary>
        public static string GradleTemplateFilename = "mainTemplate.gradle";

        /// <summary>
        /// Path of the Gradle template file.
        /// </summary>
        public static string GradleTemplatePath =
            Path.Combine(SettingsDialog.AndroidPluginsDir, GradleTemplateFilename);

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
        /// Line that indicates where to initially inject repos in the settings template.
        /// </summary>
        private const string ReposInjectionLineInGradleSettings =
            @".*flatDir {";

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
        /// Whether current of Unity has changed the place to specify Maven repos from
        /// `mainTemplate.gradle` to `settingsTemplate.gradle`.
        /// </summary>
        public static bool UnityChangeMavenRepoInSettingsTemplate {
            get {
                return Google.VersionHandler.GetUnityVersionMajorMinor() >= 2022.2f;
            }
        }

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
        /// Inject / update additional Maven repository urls specified from `Dependencies.xml` in
        /// the Gradle settings template file.
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
            if (!UnityChangeMavenRepoInSettingsTemplate) {
                repoLines.AddRange(PlayServicesResolver.GradleMavenReposLines(dependencies));
            }

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

        /// <summary>
        /// Inject / update dependencies in the gradle template file.
        /// </summary>
        /// <returns>true if successful, false otherwise.</returns>
        public static bool InjectSettings(ICollection<Dependency> dependencies, out string lastError) {
            if (!UnityChangeMavenRepoInSettingsTemplate ||
                !PlayServicesResolver.GradleTemplateEnabled) {
                // Early out since there is no need to patch settings template.
                lastError = "";
                return true;
            }

            if (!EnsureGradleTemplateEnabled(GradleSettingsTemplatePathFilename)) {
                lastError = String.Format(
                    "Failed to auto-generate '{0}'. This is required to specify " +
                    "additional Maven repos from Unity 2022.2. " +
                    "Please manually generate '{2}' through one " +
                    "of the following methods:\n" +
                    "* For Unity 2022.2.10+, enable 'Custom Gradle Settings Template' " +
                    "found under 'Player Settings > Settings for Android -> Publishing " +
                    "Settings' menu. \n" +
                    "* Manually copy '{1}' to '{2}'\n" +
                    "If you like to patch this yourself, simply disable 'Copy and patch " +
                    "settingsTemplate.gradle' in Android Resolver settings.",
                    GradleSettingsTemplatePathFilename,
                    UnityGradleSettingsTemplatePath,
                    GradleSettingsTemplatePath);
                return false;
            }

            // ReposInjectionLineInGradleSettings

            var resolutionMeasurementParameters =
                PlayServicesResolver.GetResolutionMeasurementParameters(null);
            PlayServicesResolver.analytics.Report(
                    "/resolve/gradlesettings", resolutionMeasurementParameters,
                    "Gradle Settings Template Resolve");

            var settingsFileDescription = String.Format(
                "gradle settings template " + GradleSettingsTemplatePath);

            TextFileLineInjector[] settingsInjectors = new [] {
                new TextFileLineInjector(ReposInjectionLineInGradleSettings,
                                        ReposStartLine, ReposEndLine,
                                        GradleMavenReposLinesFromDependencies(
                                                dependencies: dependencies,
                                                addMavenGoogle: false,
                                                addMavenCentral: false,
                                                addMavenLocal: true),
                                        "Repo",
                                        settingsFileDescription)
            };
            if (!PatchFile(GradleSettingsTemplatePath, settingsFileDescription,
                        "Gradle Settings", "gradlesettings",
                        settingsInjectors,
                        resolutionMeasurementParameters)) {
                lastError = String.Format("Unable to patch " + settingsFileDescription);
                return false;
            }

            lastError = "";
            return true;
        }

        /// <summary>
        /// Get the included dependency repos as lines that can be included in a Gradle file.
        /// </summary>
        /// <returns>Lines that can be included in a gradle file.</returns>
        internal static IList<string> GradleMavenReposLinesFromDependencies(
                ICollection<Dependency> dependencies,
                bool addMavenGoogle,
                bool addMavenCentral,
                bool addMavenLocal) {
            var lines = new List<string>();
            if (dependencies.Count > 0) {
                var exportEnabled = PlayServicesResolver.GradleProjectExportEnabled;
                var useFullPath = (
                        exportEnabled &&
                        SettingsDialog.UseFullCustomMavenRepoPathWhenExport ) || (
                        !exportEnabled &&
                        SettingsDialog.UseFullCustomMavenRepoPathWhenNotExport);

                var projectPath = FileUtils.PosixPathSeparators(Path.GetFullPath("."));
                var projectFileUri = GradleResolver.RepoPathToUri(projectPath);
                // projectPath will point to the Unity project root directory as Unity will
                // generate the root Gradle project in "Temp/gradleOut" when *not* exporting a
                // gradle project.
                if (!useFullPath) {
                    lines.Add(String.Format(
                            "        def unityProjectPath = $/{0}**DIR_UNITYPROJECT**/$" +
                            ".replace(\"\\\\\", \"/\")", GradleWrapper.FILE_SCHEME));
                }
                if(addMavenGoogle) {
                    lines.Add("        maven {");
                    lines.Add("            url \"https://maven.google.com\"");
                    lines.Add("        }");
                }
                // Consolidate repos url from Packages folders like
                //   "Packages/com.company.pkg1/Path/To/m2repository" and
                //   "Packages/com.company.pkg2/Path/To/m2repository"
                Dictionary< string, List<string> > repoUriToSources =
                        new Dictionary<string, List<string>>();
                foreach (var repoAndSources in PlayServicesResolver.GetRepos(dependencies: dependencies)) {
                    string repoUri;
                    if (repoAndSources.Key.StartsWith(projectFileUri)) {
                        var relativePath = repoAndSources.Key.Substring(projectFileUri.Length + 1);
                        // Convert "Assets", "Packages/packageid", or
                        // "Library/PackageCache/packageid@version" prefix to local maven repo
                        // path.  Note that local maven repo path only exists if the original repo
                        // path contains .srcaar.
                        var repoPath = FileUtils.PosixPathSeparators(
                            FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                                relativePath, GooglePlayServices.SettingsDialog.LocalMavenRepoDir));
                        if (!Directory.Exists(repoPath)) {
                            repoPath = relativePath;
                        }

                        if (useFullPath) {
                            // build.gradle expects file:/// URI so file separator will be "/" in anycase
                            // and we must NOT use Path.Combine here because it will use "\" for win platforms
                            repoUri = String.Format("\"{0}/{1}\"", projectFileUri, repoPath);
                        } else {
                            repoUri = String.Format("(unityProjectPath + \"/{0}\")", repoPath);
                        }
                    } else {
                        repoUri = String.Format("\"{0}\"", repoAndSources.Key);
                    }
                    List<string> sources;
                    if (!repoUriToSources.TryGetValue(repoUri, out sources)) {
                        sources = new List<string>();
                        repoUriToSources[repoUri] = sources;
                    }
                    sources.Add(repoAndSources.Value);
                }
                foreach(var kv in repoUriToSources) {
                    lines.Add("        maven {");
                    lines.Add(String.Format("            url {0} // {1}", kv.Key,
                                            String.Join(", ", kv.Value.ToArray())));
                    lines.Add("        }");
                }
                if (addMavenLocal) {
                    lines.Add("        mavenLocal()");
                }
                if (addMavenCentral) {
                    lines.Add("        mavenCentral()");
                }
            }
            return lines;
        }

        public static bool EnsureGradleTemplateEnabled(string templateName) {
            string templatePath = Path.Combine(SettingsDialog.AndroidPluginsDir, templateName);
            if (File.Exists(templatePath)) {
                return true;
            }

            string templateSourcePath = Path.Combine(UnityGradleTemplatesDir, templateName);

            try {
                File.Copy(templateSourcePath, templatePath);
            } catch (Exception e) {
                PlayServicesResolver.Log(String.Format(
                        "Unable to copy '{0}' from Unity engine folder '{1}' to this project " +
                        "folder '{2}'. \n {3}",
                        templateName,
                        UnityGradleTemplatesDir,
                        SettingsDialog.AndroidPluginsDir,
                        e.ToString()), LogLevel.Error);
                return false;
            }
            PlayServicesResolver.Log(String.Format(
                    "Copied '{0}' from Unity engine folder to this project '{1}'",
                    templateName, SettingsDialog.AndroidPluginsDir));
            return true;
        }
    }
}

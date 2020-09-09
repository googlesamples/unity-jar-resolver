// <copyright file="GradleWrapper.cs" company="Google Inc.">
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
    using GooglePlayServices;

    /// <summary>
    /// Provides methods to unpack and execute the embedded Gradle wrapper.
    /// </summary>
    internal class GradleWrapper {

        /// <summary>
        /// Embedded resource zip file that contains the Gradle wrapper.
        /// </summary>
        private string archiveResource;

        /// <summary>
        /// Assembly that contains the archiveResource.
        /// </summary>
        private Assembly resourceAssembly;

        /// <summary>
        /// Directory containing the Gradle wrapper.
        /// </summary>
        private string buildDirectory;

        /// <summary>
        /// Create an instance to manage the Gradle wrapper.
        /// </summary>
        /// <param name="resourceAssembly">Assembly that contains archiveResource.</param>
        /// <param name="archiveResource">Embedded zip archive resource that contains the Gradle
        /// wrapper.</param>
        /// <param name="buildDirectory">Directory to extract the Gradle wrapper to and executed it
        /// from.</param>
        public GradleWrapper(Assembly resourceAssembly, string archiveResource,
                             string buildDirectory) {
            this.resourceAssembly = resourceAssembly;
            this.archiveResource = archiveResource;
            this.buildDirectory = buildDirectory;
        }

        /// <summary>
        /// Get the location of the archive on the local filesystem containing the Gradle wrapper.
        /// </summary>
        private string Archive {
            get {
                return Path.Combine(BuildDirectory, archiveResource);
            }
        }

        /// <summary>
        /// Get the directory containing the Gradle wrapper.
        /// </summary>
        public string BuildDirectory { get { return buildDirectory; } }

        /// <summary>
        /// Returns the Gradle wrapper executable path for the current platform.
        /// </summary>
        public string Executable {
            get {
                return Path.GetFullPath(
                    Path.Combine(BuildDirectory,
                                 UnityEngine.RuntimePlatform.WindowsEditor ==
                                 UnityEngine.Application.platform ? "gradlew.bat" : "gradlew"));
            }
        }

        /// <summary>
        /// Gradle wrapper files to extract from the ARCHIVE_RESOURCE.
        /// </summary>
        private static string[] archivedFiles = new [] {
            "gradle/wrapper/gradle-wrapper.jar",
            "gradle/wrapper/gradle-wrapper.properties",
            "gradlew",
            "gradlew.bat"
        };

        /// <summary>
        /// Extract the gradle wrapper and prepare it for use.
        /// </summary>
        /// <param name="logger">Logger to report errors to.</param>
        public bool Extract(Logger logger) {
            if (!(EmbeddedResource.ExtractResources(
                    resourceAssembly,
                    new KeyValuePair<string, string>[] {
                        new KeyValuePair<string, string>(archiveResource, Archive)
                    }, logger) &&
                  PlayServicesResolver.ExtractZip(Archive, archivedFiles, BuildDirectory, true))) {
                logger.Log(String.Format("Failed to extract Gradle wrapper resource {0}",
                                         Archive), level: LogLevel.Error);
                return false;
            }
            var executable = Executable;
            // Files extracted from the zip file don't have the executable bit set on some
            // platforms, so set it here.
            // Unfortunately, File.GetAccessControl() isn't implemented, so we'll use
            // chmod (OSX / Linux) and on Windows extracted files are executable by default
            // so we do nothing.
            if (UnityEngine.RuntimePlatform.WindowsEditor != UnityEngine.Application.platform) {
                var result = CommandLine.Run("chmod", String.Format("ug+x \"{0}\"", executable));
                if (result.exitCode != 0) {
                    logger.Log(String.Format("Failed to make \"{0}\" executable.\n\n{1}",
                                             executable, result.message),
                               level: LogLevel.Error);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Prepare Gradle for execution and call a closure with the command line parameters to
        /// execute the wrapper.
        /// </summary>
        /// <param name="useGradleDaemon">Whether to use the Gradle daemon.</param>
        /// <param name="buildScript">Path to the Gradle build script to use.</param>
        /// <param name="projectProperties">Project properties to use when running the script.
        /// </param>
        /// <param name="arguments">Other arguments to pass to Gradle.</param>
        /// <param name="logger">Logger to report errors to.</param>
        /// <param name="executeCommand">Closure which takes the tool path to execute
        /// (gradle wrapper) and a string of arguments returning true if successful,
        /// false otherwise.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public bool Run(bool useGradleDaemon, string buildScript,
                        Dictionary<string, string> projectProperties,
                        string arguments, Logger logger,
                        Func<string, string, bool> executeCommand) {
            var allArguments = new List<string>() {
                useGradleDaemon ? "--daemon" : "--no-daemon",
                String.Format("-b \"{0}\"", Path.GetFullPath(buildScript)),
            };
            if (!String.IsNullOrEmpty(arguments)) allArguments.Add(arguments);
            foreach (var kv in projectProperties) {
                allArguments.Add(String.Format("\"-P{0}={1}\"", kv.Key, kv.Value));
            }
            var argumentsString = String.Join(" ", allArguments.ToArray());

            // Generate gradle.properties to set properties in the script rather than using
            // the command line.
            // Some users of Windows 10 systems have had issues running the Gradle resolver
            // which is suspected to be caused by command line argument parsing or truncation.
            // Using both gradle.properties and properties specified via command line arguments
            // works fine.
            try {
                File.WriteAllText(Path.Combine(BuildDirectory, "gradle.properties"),
                                  GradleWrapper.GenerateGradleProperties(projectProperties));
            } catch (IOException error) {
                logger.Log(String.Format("Unable to configure Gradle for execution " +
                                         "({0} {1})\n\n{2}",
                                         Executable, argumentsString, error),
                           level: LogLevel.Error);
                return false;
            }
            logger.Log(String.Format("Running Gradle...\n\n{0} {1}", Executable, argumentsString),
                       level: LogLevel.Verbose);
            return executeCommand(Executable, argumentsString);
        }

        // Characters that are parsed by Gradle / Java in property values.
        // These characters need to be escaped to be correctly interpreted in a property value.
        private static string[] GradlePropertySpecialCharacters = new string[] {
            " ", "\\", "#", "!", "=", ":"
        };

        /// <summary>
        /// Escape all special characters in a gradle property value.
        /// </summary>
        /// <param name="value">Value to escape.</param>
        /// <param name="escapeFunc">Function which generates an escaped character.  By default
        /// this adds "\\" to each escaped character.</param>
        /// <param name="charactersToExclude">Characters to exclude from the escaping set.</param>
        /// <returns>Escaped value.</returns>
        public static string EscapeGradlePropertyValue(
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
        /// http://docs.oracle.com/javase/7/docs/api/java/util/Properties.html#store%28java.io.Writer,%20java.lang.String%29
        /// </summary>
        /// <param name="properties">Properties to generate a string from.  Each value must not
        /// contain a newline.</param>
        /// <returns>String with Gradle (Java) properties</returns>
        public static string GenerateGradleProperties(Dictionary<string, string> properties) {
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
        /// File scheme that can be concatenated with an absolute path on the local filesystem.
        /// </summary>
        public const string FILE_SCHEME = "file:///";

        /// <summary>
        /// Convert a local filesystem path to a URI.
        /// </summary>
        /// <param name="localPath">Path to convert.</param>
        /// <returns>File URI.</returns>
        public static string PathToFileUri(string localPath) {
            return FILE_SCHEME + FileUtils.PosixPathSeparators(Path.GetFullPath(localPath));
        }

        // Special characters that should not be escaped in URIs for Gradle property values.
        private static HashSet<string> GradleUriExcludeEscapeCharacters = new HashSet<string> {
            ":"
        };

        /// <summary>
        /// Escape a URI so that it can be passed to Gradle.
        /// </summary>
        /// <param name="uri">URI to escape.</param>
        /// <returns>Escaped URI.</returns>
        public static string EscapeUri(string uri) {
            // Escape the URI to handle special characters like spaces and percent escape
            // all characters that are interpreted by gradle.
            return GradleWrapper.EscapeGradlePropertyValue(
                Uri.EscapeUriString(uri), escapeFunc: Uri.EscapeDataString,
                charactersToExclude: GradleUriExcludeEscapeCharacters);
        }
    }
}

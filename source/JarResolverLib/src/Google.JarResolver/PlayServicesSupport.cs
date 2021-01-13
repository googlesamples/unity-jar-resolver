// <copyright file="PlayServicesSupport.cs" company="Google Inc.">
// Copyright (C) 2014 Google Inc. All Rights Reserved.
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

namespace Google.JarResolver
{
    using System;
    using System.Diagnostics;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Xml;

    using Google;

    /// <summary>
    /// Play services support is a helper class for managing the Google play services
    /// and Android support libraries in a Unity project.  This is done by using
    /// the Maven repositories that are part of the Android SDK.  This class
    /// implements the logic of version checking, transitive dependencies, and
    /// updating a a directory to make sure that there is only one version of
    /// a dependency present.
    /// </summary>
    public class PlayServicesSupport
    {
        /// <summary>
        /// The name of the client.
        /// </summary>
        private string clientName;

        /// <summary>
        /// Log severity.
        /// </summary>
        public enum LogLevel {
            Info,
            Warning,
            Error,
        };

        /// <summary>
        /// Delegate used to specify a log method for this class.  If provided this class
        /// will log messages via this delegate.
        /// </summary>
        public delegate void LogMessage(string message);

        /// <summary>
        /// Delegate used to specify a log method for this class.  If provided this class
        /// will log messages via this delegate.
        /// </summary>
        /// <param name="message">Message to log.</param>
        /// <param name="level">Severity of the log message.</param>
        public delegate void LogMessageWithLevel(string message, LogLevel level);

        /// <summary>
        /// Log function delegate.  If set, this class will write log messages via this method.
        /// </summary>
        internal static LogMessageWithLevel logger;

        /// <summary>
        /// The repository paths.
        /// </summary>
        private List<string> repositoryPaths = new List<string>();


        /// <summary>
        /// List of additional global repository paths.
        /// These are added to the set of repositories used to construct this class.
        /// The key in the pair is the repo and the value is the source (file & line) it was
        /// parsed from.
        /// </summary>
        internal static List<KeyValuePair<string, string>> AdditionalRepositoryPaths =
            new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Get the set of repository paths.
        /// This includes the repo paths specified at construction and AdditionalRepositoryPaths.
        /// </summary>
        internal List<string> RepositoryPaths {
            get {
                var allPaths = new List<string>(repositoryPaths);
                foreach (var kv in AdditionalRepositoryPaths) {
                    allPaths.Add(kv.Value);
                }
                return allPaths;
            }
        }

        /// <summary>
        /// The client dependencies map.  This is a proper subset of dependencyMap.
        /// </summary>
        private Dictionary<string, Dependency> clientDependenciesMap =
            new Dictionary<string, Dependency>();

        /// <summary>
        /// String that is expanded with the path of the Android SDK.
        /// </summary>
        internal const string SdkVariable = "$SDK";

        /// <summary>
        /// Error message displayed / logged when the Android SDK path isn't configured.
        /// </summary>
        public const string AndroidSdkConfigurationError =
            ("Android SDK path not set.  " +
             "Set the Android SDK property using the Unity " +
             "\"Edit > Preferences > External Tools\" menu option on Windows " +
             "or the \"Unity > Preferences > External Tools\" menu option on OSX. " +
             "Alternatively, set the ANDROID_HOME environment variable");

        // Map of common dependencies to Android SDK packages.
        private static List<KeyValuePair<Regex, string>> CommonPackages =
            new List<KeyValuePair<Regex, string>> {
                new KeyValuePair<Regex, string>(
                    new Regex("^com\\.android\\.support:support-.*"),
                    "extra-android-m2repository"),
                new KeyValuePair<Regex, string>(
                    new Regex("^com\\.google\\.android\\.gms:.*"),
                    "extra-google-m2repository"),
                new KeyValuePair<Regex, string>(
                    new Regex("^com\\.google\\.firebase:firebase-.*"),
                    "extra-google-m2repository")
        };

        /// <summary>
        /// Whether verbose logging is enabled.
        /// </summary>
        internal static bool verboseLogging = false;

        // Set of currently created instances per client.
        public static Dictionary<string, PlayServicesSupport> instances =
            new Dictionary<string, PlayServicesSupport>();

        /// <summary>
        /// Creates an instance of PlayServicesSupport.  This instance is
        /// used to add dependencies for the calling client and invoke the resolution.
        /// </summary>
        /// <returns>The instance.</returns>
        /// <param name="clientName">Client name.  Must be a valid filename.
        /// This is used to uniquely identify
        /// the calling client so that dependencies can be associated with a specific
        /// client to help in resetting dependencies.</param>
        /// <param name="sdkPath">Sdk path for Android SDK (unused).</param>
        /// <param name="settingsDirectory">This parameter is obsolete.</param>
        /// <param name="logger">Delegate used to write messages to the log.</param>
        /// <param name="logMessageWithLevel">Delegate used to write messages to the log.  If
        /// this is specified "logger" is ignored.</param>
        public static PlayServicesSupport CreateInstance(
                string clientName, string sdkPath, string settingsDirectory,
                LogMessage logger = null, LogMessageWithLevel logMessageWithLevel = null) {
            return CreateInstance(clientName, sdkPath, null, settingsDirectory, logger: logger,
                                  logMessageWithLevel: logMessageWithLevel);
        }

        /// <summary>
        /// Creates an instance of PlayServicesSupport.  This instance is
        /// used to add dependencies for the calling client and invoke the resolution.
        /// </summary>
        /// <returns>The instance.</returns>
        /// <param name="clientName">Client name.  Must be a valid filename.
        /// This is used to uniquely identify
        /// the calling client so that dependencies can be associated with a specific
        /// client to help in resetting dependencies.</param>
        /// <param name="sdkPath">Sdk path for Android SDK (unused).</param>
        /// <param name="additionalRepositories">Array of additional repository paths. can be
        /// null</param>
        /// <param name="settingsDirectory">This parameter is obsolete.</param>
        /// <param name="logger">Delegate used to write messages to the log.</param>
        /// <param name="logMessageWithLevel">Delegate used to write messages to the log.  If
        /// this is specified "logger" is ignored.</param>
        internal static PlayServicesSupport CreateInstance(
                string clientName, string sdkPath, string[] additionalRepositories,
                string settingsDirectory, LogMessage logger = null,
                LogMessageWithLevel logMessageWithLevel = null)
        {
            PlayServicesSupport instance = new PlayServicesSupport();
            LogMessageWithLevel legacyLogger = (string message, LogLevel level) => {
                if (logger != null) logger(message);
            };
            PlayServicesSupport.logger =
                PlayServicesSupport.logger ?? (logMessageWithLevel ??
                                               (logger != null ? legacyLogger : null));
            string badchars = new string(Path.GetInvalidFileNameChars());

            foreach (char ch in clientName)
            {
                if (badchars.IndexOf(ch) >= 0)
                {
                    throw new Exception("Invalid clientName: " + clientName);
                }
            }

            instance.clientName = clientName;

            var repoPaths = new List<string>();
            repoPaths.AddRange(additionalRepositories ?? new string[] {});
            // Add the standard repo paths from the Android SDK
            string sdkExtrasDir = Path.Combine(SdkVariable, "extras");
            repoPaths.AddRange(
                new string [] {
                    Path.Combine(sdkExtrasDir, Path.Combine("android","m2repository")),
                    Path.Combine(sdkExtrasDir, Path.Combine("google","m2repository"))
                });
            instance.repositoryPaths = UniqueList(repoPaths);
            instances[instance.clientName] = instance;
            return instance;
        }

        /// <summary>
        /// Log a message to the currently set logger.
        /// </summary>
        /// <param name="message">Message to log.</param>
        /// <param name="level">Severity of the log message.</param>
        /// <param name="verbose">Whether the message should only be displayed with verbose
        /// logging enabled.</param>
        internal static void Log(string message, LogLevel level = LogLevel.Info,
                                 bool verbose = false) {
            if (logger != null && (!verbose || verboseLogging)) {
                logger(message, level);
            }
        }

        /// <summary>
        /// Lookup common package IDs for a dependency.
        /// </summary>
        private static Dependency AddCommonPackageIds(Dependency dep) {
            if (dep.PackageIds != null) return dep;

            var packageNames = new List<string>();
            string[] packageIds = null;
            foreach (var kv in CommonPackages) {
                var match = kv.Key.Match(dep.Key);
                if (match.Success) {
                    packageNames.Add(kv.Value);
                    break;
                }
            }
            if (packageNames.Count > 0) packageIds = packageNames.ToArray();
            return new Dependency(dep.Group, dep.Artifact, dep.Version,
                                  classifier: dep.Classifier, packageIds: packageIds,
                                  repositories: dep.Repositories);
        }

        /// <summary>
        /// Adds a dependency to the project.
        /// </summary>
        /// <remarks>This method should be called for
        /// each library that is required.  Transitive dependencies are processed
        /// so only directly referenced libraries need to be added.
        /// <para>
        /// The version string can be contain a trailing + to indicate " or greater".
        /// Trailing 0s are implied.  For example:
        /// </para>
        /// <para>  1.0 means only version 1.0, but
        /// also matches 1.0.0.
        /// </para>
        /// <para>1.2.3+ means version 1.2.3 or 1.2.4, etc. but not 1.3.
        /// </para>
        /// <para>
        /// 0+ means any version.
        /// </para>
        /// <para>
        /// LATEST means the only the latest version.
        /// </para>
        /// </remarks>
        /// <param name="group">Group - the Group Id of the artifact</param>
        /// <param name="artifact">Artifact - Artifact Id</param>
        /// <param name="version">Version - the version constraint</param>
        /// <param name="classifier">Classifier - the artifact classifer.</param>
        /// <param name="packageIds">Optional list of Android SDK package identifiers.</param>
        /// <param name="repositories">List of additional repository directories to search for
        /// this artifact.</param>
        /// <param name="createdBy">Human readable string that describes where this dependency
        /// originated.</param>
        public void DependOn(string group, string artifact, string version,
                             string classifier = null, string[] packageIds = null,
                             string[] repositories = null, string createdBy = null) {
            Log("DependOn - group: " + group +
                " artifact: " + artifact +
                " version: " + version +
                " classifier: " +
                (classifier!= null ? classifier : "null") +
                " packageIds: " +
                (packageIds != null ? String.Join(", ", packageIds) : "null") +
                " repositories: " +
                (repositories != null ? String.Join(", ", repositories) :
                 "null"),
                verbose: true);
            repositories = repositories ?? new string[] {};
            var depRepoList = new List<string>(repositories);
            depRepoList.AddRange(repositoryPaths);
            var dep = AddCommonPackageIds(new Dependency(
                group, artifact, version, classifier: classifier,
                packageIds: packageIds,
                repositories: UniqueList(depRepoList).ToArray(),
                createdBy: createdBy));
            clientDependenciesMap[dep.Key] = dep;
        }

        /// <summary>
        /// Get the current list of dependencies for all clients.
        /// </summary>
        /// <returns>Dictionary of Dependency instances indexed by Dependency.Key.</returns>
        public static Dictionary<string, Dependency> GetAllDependencies() {
            var allDependencies = new Dictionary<string, Dependency>();
            foreach (var instance in instances.Values) {
                foreach (var dependencyByKey in instance.clientDependenciesMap) {
                    allDependencies[dependencyByKey.Key] = new Dependency(dependencyByKey.Value);
                }
            }
            return allDependencies;
        }

        /// <summary>
        /// Clears the dependencies for this client.
        /// </summary>
        public void ClearDependencies()
        {
            clientDependenciesMap = new Dictionary<string, Dependency>();
            AdditionalRepositoryPaths = new List<KeyValuePair<string, string>>();
        }

        /// <summary>
        /// Create a unique ordered list of items from a list with duplicate items.
        /// Only the first occurrence of items in the original list are kept.
        /// </summary>
        private static List<T> UniqueList<T>(List<T> list)
        {
            // We can't just copy hash to list since we are preserving order.
            HashSet<T> hashSet = new HashSet<T>();
            List<T> outputList = new List<T>();
            foreach (var item in list)
            {
                if (hashSet.Contains(item)) continue;
                hashSet.Add(item);
                outputList.Add(item);
            }
            return outputList;
        }

        /// <summary>
        /// Resets the dependencies. FOR TESTING ONLY!!!
        /// </summary>
        internal static void ResetDependencies()
        {
            if (instances != null) instances.Clear();
        }
    }
}

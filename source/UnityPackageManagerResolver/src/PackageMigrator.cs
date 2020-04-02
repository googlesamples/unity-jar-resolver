// <copyright file="PackageMigrator.cs" company="Google LLC">
// Copyright (C) 2020 Google LLC All Rights Reserved.
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

using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace Google {

/// <summary>
/// Searches for packages installed in the project that are available in UPM and prompts the
/// developer to migrate them to UPM.
/// </summary>
[InitializeOnLoad]
internal class PackageMigrator {

    /// <summary>
    /// Map of Version Handler determined package to Unity Package Manager Package ID and version.
    /// </summary>
    internal class PackageMap {

        /// <summary>
        /// Available Unity Package Manager package information indexed by package ID.
        /// This should be set via CacheAvailablePackageInfo() before creating new (i.e
        /// not read from a file) PackageMap instances. </summary>
        private static readonly Dictionary<string, UnityPackageManagerClient.PackageInfo>
            availablePackageInfoCache =
                new Dictionary<string, UnityPackageManagerClient.PackageInfo>();

        /// <summary>
        /// Installed Unity Package Manager package information indexed by package ID.
        /// This should be set via CacheInstalledPackageInfo() before creating PackageMap
        /// instances. </summary>
        private static readonly Dictionary<string, UnityPackageManagerClient.PackageInfo>
            installedPackageInfoCache =
                new Dictionary<string, UnityPackageManagerClient.PackageInfo>();

        /// <summary>
        /// Version Handler package name.
        /// </summary>
        public string VersionHandlerPackageName { get; set; }

        /// <summary>
        /// Version Handler package name and version string.
        /// </summary>
        public string VersionHandlerPackageId {
            get {
                return String.Format("'{0}' v{1}", VersionHandlerPackageName,
                                     VersionHandlerPackageVersion);
            }
        }

        /// <summary>
        /// Get the files associated with the version handler package.
        /// </summary>
        public VersionHandlerImpl.ManifestReferences VersionHandlerManifest {
            get {
                if (!String.IsNullOrEmpty(VersionHandlerPackageName)) {
                    var manifestsByPackageName =
                        VersionHandlerImpl.ManifestReferences.
                            FindAndReadManifestsInAssetsFolderByPackageName();
                    VersionHandlerImpl.ManifestReferences manifestReferences;
                    if (manifestsByPackageName.TryGetValue(VersionHandlerPackageName,
                                                           out manifestReferences)) {
                        return manifestReferences;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Get the version string from the Version Handler package.
        /// </summary>
        public string VersionHandlerPackageVersion {
            get {
                var manifest = VersionHandlerManifest;
                return manifest != null ? manifest.currentMetadata.versionString : null;
            }
        }

        /// <summary>
        /// Get the numeric version from the Version Handler package.
        /// </summary>
        public long VersionHandlerPackageCalculatedVersion {
            get {
                var manifest = VersionHandlerManifest;
                return manifest != null ? manifest.currentMetadata.CalculateVersion() : 0;
            }
        }

        /// <summary>
        /// Get the Unity Package Manager Package ID in the form "name@version".
        /// </summary>
        public string UnityPackageManagerPackageId { get; set; }

        /// <summary>
        /// Get the Unity Package Manager package info associated with the specific package ID.
        /// </summary>
        public UnityPackageManagerClient.PackageInfo AvailableUnityPackageManagerPackageInfo {
            get {
                return FindPackageInfoById(availablePackageInfoCache, UnityPackageManagerPackageId);
            }
        }

        /// <summary>
        /// Get the Unity Package Manager package info associated with the specific package ID.
        /// </summary>
        public UnityPackageManagerClient.PackageInfo InstalledUnityPackageManagerPackageInfo {
            get {
                return FindPackageInfoById(installedPackageInfoCache, UnityPackageManagerPackageId);
            }
        }

        /// <summary>
        /// Determine whether a package has been migrated.
        /// </summary>
        public bool Migrated {
            get {
                return InstalledUnityPackageManagerPackageInfo != null &&
                    VersionHandlerManifest == null;
            }
        }

        /// <summary>
        /// Construct an empty package map.
        /// </summary>
        public PackageMap() {
            VersionHandlerPackageName = "";
            UnityPackageManagerPackageId = "";
        }

        /// <summary>
        /// Compare with this object.
        /// </summary>
        /// <param name="obj">Object to compare with.</param>
        /// <returns>true if both objects have the same contents excluding CreatedBy,
        /// false otherwise.</returns>
        public override bool Equals(System.Object obj) {
            var other = obj as PackageMap;
            return other != null &&
                VersionHandlerPackageName == other.VersionHandlerPackageName &&
                UnityPackageManagerPackageId == other.UnityPackageManagerPackageId;
        }

        /// <summary>
        /// Geneerate a hash of this object.
        /// </summary>
        /// <returns>Hash of this object.</returns>
        public override int GetHashCode() {
            return VersionHandlerPackageName.GetHashCode() ^
                UnityPackageManagerPackageId.GetHashCode();
        }

        /// <summary>
        /// Convert to a human readable string.
        /// </summary>
        public override string ToString() {
            return String.Format("Migrated: {0}, {1} -> '{2}'", Migrated, VersionHandlerPackageId,
                                 UnityPackageManagerPackageId);
        }

        /// <summary>
        /// Stores a set of PackageMap instances.
        /// </summary>
        internal static readonly string PackageMapFile =
            Path.Combine("Temp", "UnityPackageManagerResolverPackageMap.xml");

        /// <summary>
        /// Read a set of PackageMap instances from a file.
        /// </summary>
        /// <returns>List of package maps.</returns>
        /// <exception cref="IOException>Thrown if an error occurs while reading the
        /// file.</exception>
        public static ICollection<PackageMap> ReadFromFile() {
            var packageMaps = new List<PackageMap>();
            if (!File.Exists(PackageMapFile)) return packageMaps;

            PackageMap currentPackageMap = null;
            if (!XmlUtilities.ParseXmlTextFileElements(
                PackageMapFile, PackageMigrator.Logger,
                (reader, elementName, isStart, parentElementName, elementNameStack) => {
                    if (elementName == "packageMaps" && parentElementName == "") {
                        if (isStart) {
                            currentPackageMap = new PackageMap();
                        } else {
                            if (currentPackageMap != null &&
                                !String.IsNullOrEmpty(
                                    currentPackageMap.VersionHandlerPackageName) &&
                                !String.IsNullOrEmpty(
                                    currentPackageMap.UnityPackageManagerPackageId)) {
                                packageMaps.Add(currentPackageMap);
                            }
                        }
                        return true;
                    } else if (elementName == "packageMap" && parentElementName == "packageMaps") {
                        return true;
                    } else if (elementName == "versionHandlerPackageName" &&
                               parentElementName == "packageMap") {
                        if (isStart && reader.Read() && reader.NodeType == XmlNodeType.Text) {
                            currentPackageMap.VersionHandlerPackageName =
                                reader.ReadContentAsString();
                        }
                        return true;
                    } else if (elementName == "unityPackageManagerPackageId" &&
                               parentElementName == "packageMap") {
                        if (isStart && reader.Read() && reader.NodeType == XmlNodeType.Text) {
                            currentPackageMap.UnityPackageManagerPackageId =
                                reader.ReadContentAsString();
                        }
                        return true;
                    }
                    PackageMigrator.Logger.Log(
                        String.Format("{0}:{1}: Unknown XML element '{2}', parent '{3}'",
                                      PackageMapFile, reader.LineNumber, elementName,
                                      parentElementName, reader.LineNumber),
                        level: LogLevel.Error);
                    return false;
                })) {
                throw new IOException(String.Format("Failed to read package map file {0}. " +
                                                    "Package migration will likely fail.",
                                                    PackageMapFile));
            }
            return packageMaps;
        }

        /// <summary>
        /// Generate a collection sorted by Version Handler package name.
        /// </summary>
        /// <returns>Sorted list of package maps.</returns.
        private static ICollection<PackageMap> SortedPackageMap(
                IEnumerable<PackageMap> packages) {
            var sorted = new SortedDictionary<string, PackageMap>();
            foreach (var pkg in packages) {
                if (!String.IsNullOrEmpty(pkg.VersionHandlerPackageName)) {
                    sorted[pkg.VersionHandlerPackageName] = pkg;
                }
            }
            return sorted.Values;
        }

        /// <summary>
        /// Write a set of PackageMap instances to a file.
        /// </summary>
        /// <param name="packageMaps">Package maps to write to a file.<param>
        /// <exception cref="IOException>Thrown if an error occurs while writing the
        /// file.</exception>
        public static void WriteToFile(ICollection<PackageMap> packageMaps) {
            try {
                if (packageMaps.Count == 0) {
                    var failed = FileUtils.DeleteExistingFileOrDirectory(PackageMapFile);
                    if (failed.Count == 0) {
                        return;
                    }
                    throw new IOException(String.Format(
                        "Failed to delete {0}, package migration may attempt to resume from the " +
                        "cached state.", PackageMapFile));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(PackageMapFile));
                using (var writer = new XmlTextWriter(new StreamWriter(PackageMapFile)) {
                        Formatting = Formatting.Indented,
                    }) {
                    writer.WriteStartElement("packageMaps");
                    foreach (var pkg in SortedPackageMap(packageMaps)) {
                        writer.WriteStartElement("packageMap");
                        writer.WriteStartElement("versionHandlerPackageName");
                        writer.WriteValue(pkg.VersionHandlerPackageName);
                        writer.WriteEndElement();
                        writer.WriteStartElement("unityPackageManagerPackageId");
                        writer.WriteValue(pkg.UnityPackageManagerPackageId);
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
            } catch (Exception e) {
                throw new IOException(
                    String.Format("Failed to write package map file {0} ({1})\n" +
                                  "Package migration will likely fail.", PackageMapFile, e), e);
            }
        }

        /// <summary>
        /// Lookup a package in the info cache.
        /// </summary>
        /// <param name="cache">Cache to search.</param>
        /// <param name="packageId">Package ID to search with.</param>
        /// <returns>PackageInfo if found, null otherwise.</returns>
        private static UnityPackageManagerClient.PackageInfo FindPackageInfoById(
                Dictionary<string, UnityPackageManagerClient.PackageInfo> cache, string packageId) {
            UnityPackageManagerClient.PackageInfo packageInfo;
            if (cache.TryGetValue(packageId, out packageInfo)) {
                return packageInfo;
            }
            return null;
        }

        /// <summary>
        /// Delegate used to list / search for Unity Package Manager packages.
        /// </summary>
        /// <param name="result">Called with the result of the list / search operation.</param>
        private delegate void SearchUnityPackageManagerDelegate(
            Action<UnityPackageManagerClient.SearchResult> result);

        /// <summary>
        /// Reports progress of the package search process.
        /// </summary>
        /// <param name="progress">Progress (0..1).</param>
        /// <param name="description">Description of the operation being performed.</param>
        public delegate void FindPackagesProgressDelegate(float progress, string description);

        /// <summary>
        /// Cache Unity Package Manager packages.
        /// </summary>
        /// <param name="refresh">Whether to refresh the cache or data in memory.</param>
        /// <param name="search">Search operation that returns a result to cache.</param>
        /// <param name="searchDescription">Type of search operation being performed for error
        /// reporting.</param>
        /// <param name="cache">Cache of PackageInfo objects to update.</param>
        /// <param name="complete">Called when packages have been cached by this class.</param>
        /// <param name="progressDelegate">Called as the operation progresses.</param>
        /// <param name="simulateProgressTimeInMilliseconds">Length of time to use to simulate
        /// progress of the search operation. The search will be reported as complete in this
        /// length of time.</param>
        private static void CachePackageInfo(
                bool refresh, SearchUnityPackageManagerDelegate search, string searchDescription,
                Dictionary<string, UnityPackageManagerClient.PackageInfo> cache,
                Action<UnityPackageManagerClient.Error> complete,
                FindPackagesProgressDelegate progressDelegate,
                float simulateProgressTimeInMilliseconds) {
            if (!refresh && cache.Count > 0) {
                progressDelegate(1.0f, searchDescription);
                complete(new UnityPackageManagerClient.Error(null));
                return;
            }
            progressDelegate(0.0f, searchDescription);

            // Start a job to report progress during the search operation.
            const float ProgressReportIntervalInMilliseconds = 1000.0f;
            var progressPerUpdate = ProgressReportIntervalInMilliseconds /
                simulateProgressTimeInMilliseconds;
            object progressState = 0.0f;
            var progressJob = new RunOnMainThread.PeriodicJob(() => {
                    float progress = (float)progressState;
                    progressDelegate(progress, searchDescription);
                    progressState = Math.Min(progress + progressPerUpdate, 1.0f);
                    return false;
                }) {
                IntervalInMilliseconds = ProgressReportIntervalInMilliseconds
            };
            progressJob.Execute();

            search((result) => {
                    var finishedDescription = searchDescription;
                    if (!String.IsNullOrEmpty(result.Error.Message)) {
                        finishedDescription = String.Format("{0} failed ({1})", searchDescription,
                                                            result.Error);
                        Logger.Log(finishedDescription, level: LogLevel.Error);
                    } else {
                        cache.Clear();
                        foreach (var pkg in result.Packages) cache[pkg.PackageId] = pkg;
                    }
                    progressJob.Stop();
                    progressDelegate(1.0f, finishedDescription);
                    complete(result.Error);
                });
        }

        /// <summary>
        /// Cache installed Unity Package Manager packages.
        /// </summary>
        /// <param name="refresh">Whether to refresh the cache or data in memory.</param>
        /// <param name="complete">Called when packages have been cached by this class.</param>
        /// <param name="progressDelegate">Called as the operation progresses.</param>
        public static void CacheInstalledPackageInfo(
                bool refresh, Action<UnityPackageManagerClient.Error> complete,
                FindPackagesProgressDelegate progressDelegate) {
            CachePackageInfo(refresh, UnityPackageManagerClient.ListInstalledPackages,
                             "Listing installed UPM packages",
                             installedPackageInfoCache, complete, progressDelegate,
                             2000.0f /* 2 seconds */);
        }

        /// <summary>
        /// Cache available Unity Package Manager packages.
        /// </summary>
        /// <param name="refresh">Whether to refresh the cache or data in memory.</param>
        /// <param name="complete">Called when packages have been cached by this class.</param>
        public static void CacheAvailablePackageInfo(
                bool refresh, Action<UnityPackageManagerClient.Error> complete,
                FindPackagesProgressDelegate progressDelegate) {
            CachePackageInfo(refresh, UnityPackageManagerClient.SearchAvailablePackages,
                             "Searching for available UPM packages",
                             availablePackageInfoCache, complete, progressDelegate,
                             20000.0f /* 20 seconds */);
        }

        /// <summary>
        /// Cache available and installed Unity Package Manager packages.
        /// </summary>
        /// <param name="refresh">Whether to refresh the cache or data in memory.</param>
        /// <param name="complete">Called when packages have been cached by this class.</param>
        /// <param name="progressDelegate">Called as the operation progresses.</param>
        public static void CachePackageInfo(bool refresh,
                                            Action<UnityPackageManagerClient.Error> complete,
                                            FindPackagesProgressDelegate progressDelegate) {
            // Fraction of caching progress for each of the following operations.
            const float CacheInstallPackageProgress = 0.1f;
            const float CacheAvailablePackageProgress = 0.9f;

            CacheInstalledPackageInfo(refresh, (installedError) => {
                    if (!String.IsNullOrEmpty(installedError.Message)) {
                        complete(installedError);
                        return;
                    }
                    CacheAvailablePackageInfo(refresh, (availableError) => {
                            complete(availableError);
                        },
                        (progress, description) => {
                            progressDelegate(CacheInstallPackageProgress +
                                             (progress * CacheAvailablePackageProgress),
                                             description);
                        });
                },
                (progress, description) => {
                    progressDelegate(progress * CacheInstallPackageProgress,
                                     description);
                });
        }

        /// <summary>
        /// Find packages in the project managed by the Version Handler that can be migrated to
        /// the Unity Package Manager.
        /// </summary>
        /// <param name="complete">Called with an error string (empty if no error occured) and the
        /// list of packages that should be migrated.</param>
        /// <param name="progressDelegate">Called as the operation progresses.</param>
        /// <param name="includeOutOfDatePackages">Include UPM packages that are older than the
        /// packages installed in the project.</param>
        public static void FindPackagesToMigrate(Action<string, ICollection<PackageMap>> complete,
                                                 FindPackagesProgressDelegate progressDelegate,
                                                 bool includeOutOfDatePackages = false) {
            if (!PackageMigrator.Available) {
                complete(PackageMigrator.NotAvailableMessage, new PackageMap[] {});
                return;
            }
            var packageMaps = new HashSet<PackageMap>();

            progressDelegate(0.0f, "Searching for managed Version Handler packages");
            var installedManifestsByPackageName = VersionHandlerImpl.ManifestReferences.
                FindAndReadManifestsInAssetsFolderByPackageName();
            var installedManifestsByNamesAndAliases =
                new Dictionary<string, VersionHandlerImpl.ManifestReferences>(
                    installedManifestsByPackageName);
            foreach (var manifest in installedManifestsByPackageName.Values) {
                foreach (var manifestAlias in manifest.Aliases) {
                    installedManifestsByNamesAndAliases[manifestAlias] = manifest;
                }
            }
            var installedManifestPackageNames =
                new HashSet<string>(installedManifestsByNamesAndAliases.Keys);

            // If no Version Handler packages are installed, return an empty set.
            if (installedManifestsByNamesAndAliases.Count == 0) {
                progressDelegate(1.0f, "No Version Handler packages found");
                complete("", packageMaps);
                return;
            }

            Logger.Log(String.Format(
                "Detected version handler packages:\n{0}",
                String.Join("\n", (new List<string>(
                    installedManifestsByPackageName.Keys)).ToArray())),
                level: LogLevel.Verbose);

            CachePackageInfo(true, (error) => {
                    if (!String.IsNullOrEmpty(error.Message)) {
                        progressDelegate(1.0f, String.Format("Failed: {0}", error.Message));
                        complete(error.Message, packageMaps);
                        return;
                    }
                    var packageInfos = new List<UnityPackageManagerClient.PackageInfo>(
                        installedPackageInfoCache.Values);
                    packageInfos.AddRange(availablePackageInfoCache.Values);

                    foreach (var pkg in packageInfos) {
                        var foundVersionHandlerPackageNames = pkg.GetVersionHandlerPackageNames();
                        var versionHandlerPackageNames =
                            new HashSet<string>(foundVersionHandlerPackageNames);
                        versionHandlerPackageNames.IntersectWith(installedManifestPackageNames);

                        if (versionHandlerPackageNames.Count == 0) {
                            Logger.Log(String.Format(
                                "{0} does not map to an installed Version Handler package [{1}]",
                                pkg.PackageId,
                                String.Join(", ", (new List<string>(
                                    foundVersionHandlerPackageNames)).ToArray())),
                                level: LogLevel.Debug);
                            continue;
                        }

                        // Map all aliases to the canonical names of version handler packages to
                        // migrate.
                        var canonicalVersionHandlerPackageNames = new HashSet<string>();
                        var appliedMappings = new List<string>();
                        foreach (var versionHandlerPackageName in versionHandlerPackageNames) {
                            var canonicalName = installedManifestsByNamesAndAliases[
                                versionHandlerPackageName].filenameCanonical;
                            canonicalVersionHandlerPackageNames.Add(canonicalName);
                            if (canonicalName == versionHandlerPackageName ||
                                versionHandlerPackageNames.Contains(canonicalName)) {
                                continue;
                            }
                            appliedMappings.Add(String.Format(
                                "'{0}' UPM package references VH package '{1}' with canonical " +
                                "name '{2}'", pkg.PackageId, versionHandlerPackageName,
                                canonicalName));
                        }
                        if (appliedMappings.Count > 0) {
                            Logger.Log(String.Format("Mapped VH package aliases:\n{0}",
                                                     String.Join("\n", appliedMappings.ToArray())),
                                       level: LogLevel.Verbose);
                        }

                        foreach (var versionHandlerPackageName in
                                 canonicalVersionHandlerPackageNames) {
                            var packageMap = new PackageMap() {
                                VersionHandlerPackageName = versionHandlerPackageName,
                                UnityPackageManagerPackageId = pkg.PackageId
                            };
                            Logger.Log(
                                String.Format("Found Version Handler package to migrate to UPM " +
                                              "package {0} --> '{1}'.",
                                              packageMap.VersionHandlerPackageId,
                                              packageMap.UnityPackageManagerPackageId),
                                level: LogLevel.Verbose);
                            if (!includeOutOfDatePackages &&
                                packageMap.AvailableUnityPackageManagerPackageInfo.
                                    CalculateVersion() <
                                packageMap.VersionHandlerPackageCalculatedVersion) {
                                Logger.Log(
                                    String.Format(
                                        "Ignoring UPM package '{0}' as it's older than " +
                                        "installed package '{1}' at version {2}.",
                                        packageMap.UnityPackageManagerPackageId,
                                        packageMap.VersionHandlerPackageName,
                                        packageMap.VersionHandlerPackageVersion),
                                    level: LogLevel.Verbose);
                                continue;
                            }
                            packageMaps.Add(packageMap);
                        }
                    }

                    complete("", packageMaps);
                }, progressDelegate);
        }

        /// <summary>
        /// Calculate migration progress through the specified list.
        /// </summary>
        /// <param name="packageMaps">List of packages to migrated.</param>
        /// <returns>A value between 0 (no progress) and 1 (complete).</returns>
        public static float CalculateProgress(ICollection<PackageMap> packages) {
            int total = packages.Count;
            int migrated = 0;
            foreach (var pkg in packages) {
                if (pkg.Migrated) migrated ++;
            }
            return total > 0 ? (float)migrated / (float)total : 1.0f;
        }
    }

    /// <summary>
    /// Logger for this module.
    /// </summary>
    public static Logger Logger = UnityPackageManagerResolver.logger;

    /// <summary>
    /// Job queue to execute package migration.
    /// </summary>
    private static RunOnMainThread.JobQueue migrationJobQueue = new RunOnMainThread.JobQueue();

    /// <summary>
    /// Packages being migrated.
    /// </summary>
    private static ICollection<PackageMap> inProgressPackageMaps = null;

    /// <summary>
    /// Enumerates through set of packages being migrated.
    /// </summary>
    private static IEnumerator<PackageMap> inProgressPackageMapsEnumerator = null;

    /// <summary>
    /// Reports progress of the package migration process.
    /// </summary>
    /// <param name="progress">Progress (0..1).</param>
    /// <param name="packageMap">Package being migrated or null if complete.</param>
    private delegate void ProgressDelegate(float progress, PackageMap packageMap);

    /// <summary>
    /// Determine whether the package manager with scoped registries is available.
    /// </summary>
    public static bool Available {
        get {
            return UnityPackageManagerClient.Available &&
                UnityPackageManagerResolver.ScopedRegistriesSupported;
        }
    }

    /// <summary>
    /// Message displayed if package migration doesn't work.
    /// </summary>
    private const string NotAvailableMessage =
        "Unity Package Manager with support for scoped registries is not " +
        "available in this version of Unity.";

    /// <summary>
    /// Read the package migration state.
    /// </summary>
    /// <returns>true if the migration state is already in memory or read successfully and contains
    /// a non-zero number of PackageMap instances, false otherwise.</returns>
    /// <exception>Throws IOException if the read fails.</exception>
    private static bool ReadMigrationState() {
        if (inProgressPackageMaps == null || inProgressPackageMapsEnumerator == null) {
            // Read in-progress migrations including progress through the queue.
            try {
                inProgressPackageMaps = PackageMap.ReadFromFile();
                inProgressPackageMapsEnumerator = inProgressPackageMaps.GetEnumerator();
            } catch (IOException error) {
                ClearMigrationState();
                throw error;
            }
        } else {
            inProgressPackageMapsEnumerator.Reset();
        }
        return inProgressPackageMapsEnumerator.MoveNext();
    }


    /// <summary>
    /// Clear the package migration state.
    /// </summary>
    private static void ClearMigrationState() {
        inProgressPackageMaps = null;
        inProgressPackageMapsEnumerator = null;
        PackageMap.WriteToFile(new PackageMap[] {});
    }

    /// <summary>
    /// Migrate the next package in the queue.
    /// </summary>
    /// <remarks>
    /// This method assumes it's being executed as a job on migrationJobQueue so
    /// migrationJobQueue.Complete() must be called before the complete() method in all code paths.
    /// </remarks>
    /// <param name="complete">When complete, called with a null string if successful,
    /// called with an error message otherwise.</param>
    /// <param name="progress">Called to report progress.</param>
    private static void MigrateNext(Action<string> complete, ProgressDelegate progress) {
        do {
            var packageMap = inProgressPackageMapsEnumerator.Current;
            Logger.Log(String.Format("Examining package to migrate: {0}", packageMap.ToString()),
                       level: LogLevel.Verbose);
            if (!packageMap.Migrated) {
                progress(PackageMap.CalculateProgress(inProgressPackageMaps), packageMap);

                Logger.Log(String.Format("Removing {0} to replace with {1}",
                                         packageMap.VersionHandlerPackageName,
                                         packageMap.UnityPackageManagerPackageId),
                           level: LogLevel.Verbose);
                // Uninstall the .unitypackage.
                var deleteResult = VersionHandlerImpl.ManifestReferences.DeletePackages(
                    new HashSet<string>() { packageMap.VersionHandlerPackageName },
                    force: true);
                if (deleteResult.Count > 0) {
                    var error = String.Format("Uninstallation of .unitypackage '{0}' failed, " +
                                              "halted package migration to '{1}'. You will need " +
                                              "to reinstall '{0}' to restore your project",
                                              packageMap.VersionHandlerPackageName,
                                              packageMap.UnityPackageManagerPackageId);
                    migrationJobQueue.Complete();
                    complete(error);
                    return;
                }

                Logger.Log(String.Format("Installing {0} to replace {1}",
                                         packageMap.UnityPackageManagerPackageId,
                                         packageMap.VersionHandlerPackageName),
                           level: LogLevel.Verbose);
                UnityPackageManagerClient.AddPackage(
                    packageMap.UnityPackageManagerPackageId,
                    (result) => {
                        if (!String.IsNullOrEmpty(result.Error.Message)) {
                            var error = String.Format("Installation of package '{0}' failed, " +
                                                      "halted package migration ({1}). You will " +
                                                      "need to reinstall {2} to restore your " +
                                                      "project.",
                                                      packageMap.UnityPackageManagerPackageId,
                                                      result.Error.Message,
                                                      packageMap.VersionHandlerPackageName);
                            migrationJobQueue.Complete();
                            complete(error);
                        } else {
                            MigrateNext(complete, progress);
                        }
                    });
                return;
            }
        } while (inProgressPackageMapsEnumerator.MoveNext());

        Logger.Log("Package migration complete", level: LogLevel.Verbose);
        progress(1.0f, null);
        ClearMigrationState();
        migrationJobQueue.Complete();
        complete(null);
    }

    /// <summary>
    /// Start / resume package migration.
    /// </summary>
    /// <param name="complete">When complete, called with a null string if successful,
    /// called with an error message otherwise.</param>
    /// <param name="migratePackagesProgressDelegate">Called to report progress of package
    /// migration.</param>
    /// <param name="findPackagesProgressDelegate">Called to report progress of package
    /// migration initialization.</param>
    private static void StartOrResumeMigration(
            Action<string> complete, ProgressDelegate migratePackagesProgressDelegate,
            PackageMap.FindPackagesProgressDelegate findPackagesProgressDelegate) {
        migrationJobQueue.Schedule(() => {
                // Read in-progress migrations including progress through the queue.
                try {
                    if (!ReadMigrationState()) {
                        migrationJobQueue.Complete();
                        complete(null);
                        return;
                    }
                } catch (IOException ioError) {
                    UnityPackageManagerResolver.analytics.Report(
                        "package_migrator/migration/failed/read_snapshot",
                        "Migrate Packages: Read Snapshot Failed");
                    migrationJobQueue.Complete();
                    complete(ioError.Message);
                    return;
                }
                // Fetch the list of installed packages before starting migration.
                PackageMap.CacheInstalledPackageInfo(
                    false, (error) => {
                        if (!String.IsNullOrEmpty(error.Message)) {
                            UnityPackageManagerResolver.analytics.Report(
                                "package_migrator/migration/failed/find_packages",
                                "Migrate Packages: Find Packages Failed");
                            migrationJobQueue.Complete();
                            complete(error.Message);
                            return;
                        }
                        // Migrate packages.
                        MigrateNext(complete, migratePackagesProgressDelegate);
                    }, findPackagesProgressDelegate);
            });
    }

    /// <summary>
    /// Called when the assembly is initialized by Unity.
    /// </summary>
    static PackageMigrator() {
        ResumeMigration();
    }

    /// <summary>
    /// Report that package migration failed.
    /// </summary>
    private static void ReportPackageMigrationFailed() {
        int numberOfSelectedPackages = -1;
        int numberOfMigratedPackages = -1;
        try {
            ReadMigrationState();
            numberOfSelectedPackages = inProgressPackageMaps.Count;
            numberOfMigratedPackages = 0;
            foreach (var packageMap in inProgressPackageMaps) {
                if (packageMap.Migrated) numberOfMigratedPackages ++;
            }
        } catch (IOException) {
            // Ignore the exception.
        }
        UnityPackageManagerResolver.analytics.Report(
            "package_migrator/migration/failed",
            new KeyValuePair<string, string>[] {
                new KeyValuePair<string, string>("selected", numberOfSelectedPackages.ToString()),
                new KeyValuePair<string, string>("migrated", numberOfMigratedPackages.ToString()),
            },
            "Migrate Packages: Failed");
    }


    /// <summary>
    /// Resume migration after an app domain reload.
    /// </summary>
    public static void ResumeMigration() {
        RunOnMainThread.Run(() => {
                if (!Available) return;
                try {
                    if (ReadMigrationState()) {
                        Logger.Log(String.Format("Resuming migration from {0} of {1} packages",
                                                 PackageMap.PackageMapFile,
                                                 inProgressPackageMaps.Count),
                                   level: LogLevel.Verbose);
                        TryMigration((error) => {
                                Logger.Log(String.Format("Migration failed: {0}", error),
                                           level: LogLevel.Error);
                            });
                    }
                } catch (IOException ioError) {
                    Logger.Log(String.Format("Failed to resume package migration: {0}", ioError),
                               level: LogLevel.Error);
                    ReportPackageMigrationFailed();
                }
            }, runNow: false);
    }

    /// <summary>
    /// Displayed when searching for / migrating packages.
    /// </summary>
    const string WindowTitle = "Migrating Packages";

    /// <summary>
    /// Displays progress of a find packages operation.
    /// </summary>
    /// <param name="progress">Progress (0..1).</param>
    /// <param name="description">Description of the operation being performed.</param>
    private static void UpdateFindProgressBar(float progress, string description) {
        var message = String.Format("Finding package(s) to migrate {0}%: {1}",
                                    (int)(progress * 100.0f), description);
        Logger.Log(message, level: LogLevel.Verbose);
        EditorUtility.DisplayProgressBar(WindowTitle, description, progress);
    }

    /// <summary>
    /// Display progress of a package migration operation.
    /// </summary>
    /// <param name="progress">Progress (0..1).</param>
    /// <param name="packageMap">Package being migrated.</param>
    private static void UpdatePackageMigrationProgressBar(float progress, PackageMap packageMap) {
        var description = String.Format("{0} --> {1}",
                                        packageMap.VersionHandlerPackageId,
                                        packageMap.UnityPackageManagerPackageId);
        var message = String.Format("Migrating package(s) {0}%: {1}",
                                    (int)(progress * 100.0f), description);
        Logger.Log(message, level: LogLevel.Verbose);
        EditorUtility.DisplayProgressBar(WindowTitle, description, progress);
    }

    /// <summary>
    /// Run through the whole process.
    /// </summary>
    /// <param name="complete">Called when migration is complete with an error message if
    /// it fails.</param>
    /// <param name="packageMaps">Set of packages to migrate.</param>
    public static void TryMigration(Action<string> complete) {
        if (!Available) {
            complete(NotAvailableMessage);
            return;
        }

        Action<string> clearProgressAndComplete = (error) => {
            EditorUtility.ClearProgressBar();
            if (String.IsNullOrEmpty(error)) {
                UnityPackageManagerResolver.analytics.Report(
                    "package_migrator/migration/success",
                    new KeyValuePair<string, string>[] {
                        new KeyValuePair<string, string>(
                            "migrated", inProgressPackageMaps.Count.ToString()),
                    },
                    "Migrate Packages: Success");
            }
            ClearMigrationState();
            complete(error);
        };

        StartOrResumeMigration((migrationError) => {
                if (!String.IsNullOrEmpty(migrationError)) {
                    clearProgressAndComplete(migrationError);
                    return;
                }

                PackageMap.FindPackagesToMigrate((error, packageMaps) => {
                        if (!String.IsNullOrEmpty(error)) {
                            UnityPackageManagerResolver.analytics.Report(
                                "package_migrator/migration/failed/find_packages",
                                "Migrate Packages: Find Packages Failed");
                           clearProgressAndComplete(error);
                           return;
                        }
                        try {
                            PackageMap.WriteToFile(packageMaps);
                        } catch (IOException e) {
                            EditorUtility.ClearProgressBar();
                            clearProgressAndComplete(e.Message);
                            return;
                        }
                        StartOrResumeMigration(clearProgressAndComplete,
                                               UpdatePackageMigrationProgressBar,
                                               UpdateFindProgressBar);
                    }, UpdateFindProgressBar);
            }, UpdatePackageMigrationProgressBar, UpdateFindProgressBar);
    }

    /// <summary>
    /// Display a window to select which packages to install.
    /// </summary>
    /// <param name="packageMaps">Set of package maps available for selection.</param>
    /// <param name="selectedPackageMaps">Called with a set of items selected from the specified
    /// list.</param>
    public static void DisplaySelectionWindow(ICollection<PackageMap> packageMaps,
                                              Action<ICollection<PackageMap>> selectedPackageMaps) {
        var packageMapsByPackageId = new Dictionary<string, PackageMap>();
        var items = new List<KeyValuePair<string, string>>();
        foreach (var pkg in packageMaps) {
            packageMapsByPackageId[pkg.UnityPackageManagerPackageId] = pkg;
            items.Add(new KeyValuePair<string, string>(
                pkg.UnityPackageManagerPackageId,
                String.Format("{0} --> {1}", pkg.VersionHandlerPackageId,
                              pkg.UnityPackageManagerPackageId)));
        }

        var window = MultiSelectWindow.CreateMultiSelectWindow(WindowTitle);
        window.minSize = new UnityEngine.Vector2(800, 400);
        window.SelectedItems = new HashSet<string>(packageMapsByPackageId.Keys);
        window.AvailableItems = items;
        window.Sort(1);
        window.Caption =
            "Select the set of packages to migrate from being installed under " +
            "the Assets folder to being installed using the Unity Package Manager\n\n" +
            "As each package is migrated, it will be removed from the Assets folder and " +
            "installed via the Unity Package Manager. If this package (EDM4U) is being " +
            "migrated the progress bar will disappear as it is migrated and migration will " +
            "resume when this package is reloaded after being installed by the Unity " +
            "Package Manager";
        window.OnApply = () => {
            var selected = new List<PackageMap>();
            foreach (var item in window.SelectedItems) {
                selected.Add(packageMapsByPackageId[item]);
            }
            selectedPackageMaps(selected);
        };
        window.OnCancel = () => {
            selectedPackageMaps(new PackageMap[] {});
        };
    }

    /// <summary>
    /// Display an error in a dialog.
    /// </summary>
    /// <param name="error">Error message to display.</param>
    private static void DisplayError(string error) {
        Logger.Log(error, level: LogLevel.Error);
        Dialog.Display(WindowTitle, error, Dialog.Option.Selected0, "OK");
    }

    /// <summary>
    /// Find packages to migrate, display a window to provide the user a way to select the packages
    /// to migrate and start migration if they're selected.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Unity Package Manager Resolver/Migrate Packages")]
    public static void MigratePackages() {
        PackageMap.FindPackagesToMigrate((findError, availablePackageMaps) => {
                EditorUtility.ClearProgressBar();

                // If an error occurs, display a dialog.
                if (!String.IsNullOrEmpty(findError)) {
                    UnityPackageManagerResolver.analytics.Report(
                        "package_migrator/migration/failed/find_packages",
                        "Migrate Packages: Find Packages Failed");
                    DisplayError(findError);
                    return;
                }

                // Show a package selection window and start migration if the user selects apply.
                DisplaySelectionWindow(availablePackageMaps, (selectedPackageMaps) => {
                        if (selectedPackageMaps.Count == 0) {
                            UnityPackageManagerResolver.analytics.Report(
                                "package_migrator/migration/canceled",
                                "Migrate Packages: Canceled");
                            ClearMigrationState();
                            return;
                        }
                        try {
                            ClearMigrationState();
                            PackageMap.WriteToFile(selectedPackageMaps);
                        } catch (IOException e) {
                            DisplayError(String.Format("Migration failed ({0})", e.Message));
                            UnityPackageManagerResolver.analytics.Report(
                                "package_migrator/migration/failed/write_snapshot",
                                "Migrate Packages: Write Snapshot Failed");
                            return;
                        }

                        TryMigration((migrationError) => {
                                if (!String.IsNullOrEmpty(migrationError)) {
                                    DisplayError(migrationError);
                                }
                            });
                    });
            }, UpdateFindProgressBar);
    }
}

/// <summary>
/// Extension class to retrieve Version Handler package information from PackageInfo.
/// </summary>
internal static class PackageInfoVersionHandlerExtensions {

    /// <summary>
    /// Regular expression that extracts the Version Handler package name from a label.
    /// </summary>
    private static Regex KEYWORD_VERSION_HANDLER_NAME_REGEX = new Regex("^vh[-_]name:(.*)");

    /// <summary>
    /// Get Version Handler package names associated with this Unity Package Manager package.
    /// </summary>
    /// <returns>Set of package names associataed with this package.</returns>
    public static HashSet<string> GetVersionHandlerPackageNames(
            this UnityPackageManagerClient.PackageInfo packageInfo) {
        var versionHandlerPackageNames = new HashSet<string>();
        foreach (var keyword in packageInfo.Keywords) {
            var match = KEYWORD_VERSION_HANDLER_NAME_REGEX.Match(keyword);
            if (match.Success) versionHandlerPackageNames.Add(match.Groups[1].Value);
        }
        return versionHandlerPackageNames;
    }

    /// <summary>
    /// Get a numeric version of the Unity Package Manager package.
    /// </summary>
    /// <returns>Numeric version of the package that can be compared with Version Handler package
    /// versions.</returns>
    public static long CalculateVersion(this UnityPackageManagerClient.PackageInfo packageInfo) {
        return VersionHandlerImpl.FileMetadata.CalculateVersion(packageInfo.Version);
    }
}

}

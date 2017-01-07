// <copyright file="VersionHandler.cs" company="Google Inc.">
// Copyright (C) 2016 Google Inc. All Rights Reserved.
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

using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System;

namespace Google {

[InitializeOnLoad]
public class VersionHandler : AssetPostprocessor {
    /// <summary>
    /// Derives metadata from an asset filename.
    /// </summary>
    public class FileMetadata {
        // Splits a filename into components.
        private class FilenameComponents
        {
            // Name of the file.
            public string filename;
            // Directory component.
            public string directory;
            // Basename (filename with no directory).
            public string basename;
            // Extension component.
            public string extension;
            // Basename without an extension.
            public string basenameNoExtension;

            // Parse a filename into components.
            public FilenameComponents(string filename) {
                this.filename = filename;
                directory = Path.GetDirectoryName(filename);
                basename = Path.GetFileName(filename);
                extension = Path.GetExtension(basename);
                basenameNoExtension =
                    basename.Substring(0, basename.Length - extension.Length);
            }
        }


        // Separator for metadata tokens in the supplied filename.
        private static char[] FILENAME_TOKEN_SEPARATOR = new char[] { '_' };
        // Separator for fields in each metadata token in the supplied
        // filename or label.
        private static char[] FIELD_SEPARATOR = new char[] { '-' };

        // Prefix which identifies the targets metadata in the filename or
        // asset label.
        private static string TOKEN_TARGETS = "t";
        // Prefix which identifies the version metadata in the filename or
        // asset label.
        private static string TOKEN_VERSION = "v";
        // Prefix which indicates this file is a package manifest.
        private static string FILENAME_TOKEN_MANIFEST = "manifest";

        // Delimiter for version numbers.
        private static char[] VERSION_DELIMITER = new char[] { '.' };
        // Maximum number of components parsed from a version number.
        private static int MAX_VERSION_COMPONENTS = 4;
        // Multiplier applied to each component of the version number,
        // see CalculateVersion().
        private static long VERSION_COMPONENT_MULTIPLIER = 1000;
        // Prefix for labels which encode metadata of an asset.
        private static string LABEL_PREFIX = "gvh_";
        // Initialized depending on the version of unity we are running against
        private static HashSet<BuildTarget> targetBlackList = null;
        // Initialized by parsing BuildTarget enumeration values from
        // BUILD_TARGET_NAME_TO_ENUM_NAME.
        private static Dictionary<string, BuildTarget>
            buildTargetNameToEnum = null;

        /// <summary>
        /// Label which flags whether an asset is should be managed by this
        /// module.
        /// </summary>
        public static string ASSET_LABEL = "gvh";

        // Map of build target names to BuildTarget enumeration names.
        // We don't use BuildTarget enumeration values here as Unity has a
        // habit of removing unsupported ones from the API.
        static public Dictionary<string, string>
            BUILD_TARGET_NAME_TO_ENUM_NAME = new Dictionary<string, string> {
            {"osx", "StandaloneOSXUniversal"},
            {"osxintel", "StandaloneOSXIntel"},
            {"windows", "StandaloneWindows"},
            {"ios", "iOS"},
            {"ps3", "PS3"},
            {"xbox360", "XBOX360"},
            {"android", "Android"},
            {"linux32", "StandaloneLinux"},
            {"windows64", "StandaloneWindows64"},
            {"webgl", "WebGL"},
            {"linux64", "StandaloneLinux64"},
            {"linux", "StandaloneLinuxUniversal"},
            {"osxintel64", "StandaloneOSXIntel64"},
            {"tizen", "Tizen"},
            {"psp2", "PSP2"},
            {"ps4", "PS4"},
            {"xboxone", "XboxOne"},
            {"samsungtv", "SamsungTV"},
            {"nintendo3ds", "Nintendo3DS"},
            {"wiiu", "WiiU"},
            {"tvos", "tvOS"},
        };

        /// <summary>
        /// Get a set of build target names mapped to supported BuildTarget
        /// enumeration values.
        /// </summary>
        internal static Dictionary<string, BuildTarget> GetBuildTargetNameToEnum() {
            if (buildTargetNameToEnum == null) {
                var targetBlackList = GetBlackList();
                buildTargetNameToEnum =
                    new Dictionary<string, BuildTarget>();
                foreach (var targetNameEnumName in
                         BUILD_TARGET_NAME_TO_ENUM_NAME) {
                    // Attempt to parse the build target name.
                    // ArgumentException, OverflowException or
                    // TypeInitializationException
                    // will be thrown if the build target is no longer
                    // supported.
                    BuildTarget target;
                    try {
                        target = (BuildTarget)Enum.Parse(
                            typeof(BuildTarget), targetNameEnumName.Value);
                    } catch (ArgumentException) {
                        continue;
                    } catch (OverflowException) {
                        continue;
                    } catch (TypeInitializationException) {
                        continue;
                    }
                    if (!targetBlackList.Contains(target)) {
                        buildTargetNameToEnum[targetNameEnumName.Key] =
                            target;
                    }
                }
            }
            return buildTargetNameToEnum;
        }

        // Returns the major/minor version of the unity environment we are running in
        // as a float so it can be compared numerically.
        static float GetUnityVersionMajorMinor() {
            float result = 5.4f;
            string version = Application.unityVersion;
            if (!string.IsNullOrEmpty(version)) {
                int dotIndex = version.IndexOf('.');
                if (dotIndex > 0 && version.Length > dotIndex + 1) {
                    if (!float.TryParse(version.Substring(0, dotIndex + 2), out result)) {
                        result = 5.4f;
                    }
                }
            }
            return result;
        }

        // Returns a hashset containing blacklisted build targets for the current
        // unity environment.
        // We need to maintain a seperate blacklist as Unity occasionally
        // removes BuildTarget display names but does not remove the enumeration
        // values associated with the names.  This causes a fatal error in
        // PluginImporter.GetCompatibleWithPlatform() when provided with a
        // BuildTarget that no longer has a display name.
        static HashSet<BuildTarget> GetBlackList() {
            if (targetBlackList == null) {
                targetBlackList = new HashSet<BuildTarget>();
                if (GetUnityVersionMajorMinor() >= 5.5) {
                    targetBlackList.Add(BuildTarget.PS3);
                    targetBlackList.Add(BuildTarget.XBOX360);
                }
            }
            return targetBlackList;
        }

        /// <summary>
        /// Name of the file use to construct this object.
        /// </summary>
        public string filename = "";

        /// <summary>
        /// Name of the file with metadata stripped.
        /// </summary>
        public string filenameCanonical = "";

        /// <summary>
        /// Version string parsed from the filename or AssetDatabase label if
        /// it's not present in the filename.
        /// </summary>
        public string versionString = "";

        /// <summary>
        /// List of target platforms parsed from the filename.
        /// </summary>
        public string[] targets = null;

        /// <summary>
        /// Set if this references an asset manifest.
        /// </summary>
        public bool isManifest = false;

        /// <summary>
        /// Parse metadata from filename and store in this class.
        /// </summary>
        /// <param name="filename">Name of the file to parse.</param>
        public FileMetadata(string filename) {
            this.filename = filename;
            filenameCanonical = filename;

            var filenameComponents = new FilenameComponents(filename);
            // Parse metadata from the filename.
            string[] tokens =
                filenameComponents.basenameNoExtension.Split(
                    FILENAME_TOKEN_SEPARATOR);
            if (tokens.Length > 1) {
                filenameComponents.basenameNoExtension = tokens[0];
                for (int i = 1; i < tokens.Length; ++i) {
                    string token = tokens[i];
                    if (token == FILENAME_TOKEN_MANIFEST) {
                        isManifest = true;
                    } else if (token.StartsWith(TOKEN_TARGETS)) {
                        targets = ParseTargets(token);
                    } else if (token.StartsWith(TOKEN_VERSION)) {
                        versionString = ParseVersion(token);
                    }
                }
            }
            // Parse metadata from asset labels if it hasn't been specified in
            // the filename.
            AssetImporter importer = GetAssetImporter();
            if (importer != null) {
                foreach (string label in AssetDatabase.GetLabels(importer)) {
                    // Labels are converted to title case in the asset database
                    // so convert to lower case before parsing.
                    string lowerLabel = label.ToLower();
                    if (lowerLabel.StartsWith(LABEL_PREFIX)) {
                        string token =
                            lowerLabel.Substring(LABEL_PREFIX.Length);
                        if (token.StartsWith(TOKEN_TARGETS)) {
                            if (targets == null) {
                                targets = ParseTargets(token);
                            }
                        } else if (token.StartsWith(TOKEN_VERSION)) {
                            if (String.IsNullOrEmpty(versionString)) {
                                versionString = ParseVersion(token);
                            }
                        } else if (token.Equals(FILENAME_TOKEN_MANIFEST)) {
                            isManifest = true;
                        }
                    }
                }
            }

            // On Windows the AssetDatabase converts native path separators
            // used by the .NET framework '\' to *nix style '/' such that
            // System.IO.Path generated paths will not match those looked up
            // in the asset database.  So we convert the output of Path.Combine
            // here to use *nix style paths so that it's possible to perform
            // simple string comparisons to check for path equality.
            filenameCanonical = Path.Combine(
                filenameComponents.directory,
                filenameComponents.basenameNoExtension +
                filenameComponents.extension).Replace('\\', '/');
            UpdateAssetLabels();
        }

        /// <summary>
        /// Parse version from a filename or label field.
        /// </summary>
        /// <param name="token">String to parse.  Should start with
        /// TOKEN_VERSION</param>
        /// <returns>Version string parsed from the token.</returns>
        private static string ParseVersion(string token) {
            return token.Substring(TOKEN_VERSION.Length);
        }

        /// <summary>
        /// Parse target names from a filename or label field.
        /// </summary>
        /// <param name="token">String to parse.  Should start with
        /// TOKEN_TARGETS</param>
        /// <returns>List of target names parsed from the token.</returns>
        private static string[] ParseTargets(string token) {
            string[] parsedTargets =
                token.Substring(TOKEN_TARGETS.Length).Split(FIELD_SEPARATOR);
            // Convert all target names to lower case.
            string[] targets = new string[parsedTargets.Length];
            for (int i = 0; i < parsedTargets.Length; ++i) {
                targets[i] = parsedTargets[i].ToLower();
            }
            return targets;
        }

        /// <summary>
        /// Determine whether this file is compatible with the editor.
        /// This is a special case as the editor isn't a "platform" covered
        /// by UnityEditor.BuildTarget.
        /// </summary>
        /// <returns>true if this file targets the editor, false
        /// otherwise.</returns>
        public bool GetEditorEnabled() {
            return targets != null && Array.IndexOf(targets, "editor") >= 0;
        }

        /// <summary>
        /// Get the list of build targets this file is compatible with.
        /// </summary>
        /// <returns>Set of BuildTarget (platforms) this is compatible with.
        /// </returns>
        public HashSet<BuildTarget> GetBuildTargets() {
            HashSet<BuildTarget> buildTargetSet = new HashSet<BuildTarget>();
            var buildTargetToEnum = GetBuildTargetNameToEnum();
            if (targets != null) {
                foreach (string target in targets) {
                    BuildTarget buildTarget;
                    if (buildTargetToEnum.TryGetValue(target, out buildTarget)) {
                        buildTargetSet.Add(buildTarget);
                    } else if (!target.Equals("editor")) {
                        UnityEngine.Debug.LogError(
                            filename + " reference to unknown target " +
                            target + " the version handler may out of date.");
                    }
                }

            }
            return buildTargetSet;
        }

        /// <summary>
        /// Save metadata from this class into the asset's labels.
        /// </summary>
        public void UpdateAssetLabels() {
            AssetImporter importer = AssetImporter.GetAtPath(filename);
            List<string> labels = new List<String>();
            // Strip labels we're currently managing.
            foreach (string label in AssetDatabase.GetLabels(importer)) {
                if (!(label.ToLower().StartsWith(LABEL_PREFIX) ||
                      label.ToLower().Equals(ASSET_LABEL))) {
                    labels.Add(label);
                }
            }
            // Add / preserve the label that indicates this asset is managed by
            // this module.
            labels.Add(ASSET_LABEL);
            // Add labels for the metadata in this class.
            if (!String.IsNullOrEmpty(versionString)) {
                labels.Add(LABEL_PREFIX + TOKEN_VERSION + versionString);
            }
            if (targets != null && targets.Length > 0) {
                labels.Add(LABEL_PREFIX + TOKEN_TARGETS +
                           String.Join(Char.ToString(FIELD_SEPARATOR[0]),
                                       targets));
            }
            if (isManifest) {
                labels.Add(LABEL_PREFIX + FILENAME_TOKEN_MANIFEST);
            }
            AssetDatabase.SetLabels(importer, labels.ToArray());
        }

        /// <summary>
        /// Get the AssetImporter associated with this file.
        /// </summary>
        /// <returns>AssetImporter instance if one is associated with this
        /// file, null otherwise.</returns>
        public AssetImporter GetAssetImporter() {
            return AssetImporter.GetAtPath(filename);
        }

        /// <summary>
        /// Rename the file associated with this data.
        /// </summary>
        /// <param name="newFilename">New name of the file.</param>
        /// <returns>true if successful, false otherwise.</returns>
        public bool RenameAsset(string newFilename) {
            var filenameComponents = new FilenameComponents(newFilename);
            Debug.Assert(filenameComponents.directory ==
                         Path.GetDirectoryName(filename));
            // If the target file exists, delete it.
            if (AssetImporter.GetAtPath(newFilename) != null) {
                if (!AssetDatabase.MoveAssetToTrash(newFilename)) {
                    UnityEngine.Debug.LogError(
                        "Failed to move asset to trash: " + filename);
                    return false;
                }
            }
            try {
              string error = AssetDatabase.RenameAsset(
                  filename, filenameComponents.basenameNoExtension);
              if (!String.IsNullOrEmpty(error)) {
                  UnityEngine.Debug.LogError(
                      "Failed to rename asset " + filename + " to " +
                      newFilename + " (" + error + ")");
                  return false;
              }
              AssetDatabase.ImportAsset(newFilename);
            } catch (Exception) {
                // Unity 5.3 and below can end up throw all sorts of
                // exceptions here when attempting to reload renamed
                // assemblies.  Since these are completely harmless as
                // everything will be reloaded and exceptions will be
                // reported upon AssetDatabase.Refresh(), ignore them.
            }
            filename = newFilename;
            UpdateAssetLabels();
            return true;
        }


        /// <summary>
        /// Get a numeric version number.  Each component is multiplied by
        /// VERSION_COMPONENT_MULTIPLIER^(MAX_VERSION_COMPONENTS -
        ///                               (component_index + 1))
        /// and accumulated in the returned value.
        /// If the version string contains more than MAX_VERSION_COMPONENTS the
        /// remaining components are ignored.
        /// </summary>
        /// <returns>64-bit version number.</returns>
        public long CalculateVersion() {
            return CalculateVersion(versionString);
        }

        /// <summary>
        /// Get a numeric version number.  Each component is multiplied by
        /// VERSION_COMPONENT_MULTIPLIER^(MAX_VERSION_COMPONENTS -
        ///                               (component_index + 1))
        /// and accumulated in the returned value.
        /// If the version string contains more than MAX_VERSION_COMPONENTS the
        /// remaining components are ignored.
        /// </summary>
        /// <param name="version">Version string to parse.</param>
        /// <returns>64-bit version number.</returns>
        public static long CalculateVersion(string versionString) {
            long versionNumber = 0;
            if (versionString.Length > 0) {
                string[] components = versionString.Split(VERSION_DELIMITER);
                int numberOfComponents =
                    components.Length < MAX_VERSION_COMPONENTS ?
                        components.Length : MAX_VERSION_COMPONENTS;
                for (int i = 0; i < numberOfComponents; ++i) {
                    versionNumber +=
                        Convert.ToInt64(components[i]) *
                        (long)Math.Pow(
                            (double)VERSION_COMPONENT_MULTIPLIER,
                            (double)(MAX_VERSION_COMPONENTS - (i + 1)));
                }
            }
            return versionNumber;
        }

        /// <summary>
        /// Convert a numeric version back to a version string.
        /// </summary>
        /// <param name="version">Numeric version number.</param>
        /// <returns>Version string.</returns>
        public static string VersionNumberToString(long versionNumber) {
            List<string> components = new List<string>();
            for (int i = 0; i < MAX_VERSION_COMPONENTS; ++i) {
                long componentDivisor =
                    (long)Math.Pow((double)VERSION_COMPONENT_MULTIPLIER,
                                   (double)(MAX_VERSION_COMPONENTS - (i + 1)));
                components.Add((versionNumber / componentDivisor).ToString());
                versionNumber %= componentDivisor;

            }
            return String.Join(Char.ToString(VERSION_DELIMITER[0]),
                               components.ToArray());
        }
    }

    /// <summary>
    /// Set of FileMetadata ordered by version.
    /// </summary>
    public class FileMetadataByVersion {
        /// <summary>
        /// Name of the file with metadata removed.
        /// </summary>
        public string filenameCanonical = null;

        /// <summary>
        /// Dictionary of FileMetadata ordered by version.
        /// </summary>
        private SortedDictionary<long, FileMetadata> metadataByVersion =
            new SortedDictionary<long, FileMetadata>();

        /// <summary>
        /// Get the FileMetadata from this object ordered by version number.
        /// </summary>
        public SortedDictionary<long, FileMetadata>.ValueCollection Values {
            get { return metadataByVersion.Values; }
        }

        /// <summary>
        /// Get FileMetadata from this object given a version number.
        /// </summary>
        /// <param name="version">Version to search for.</param>
        /// <returns>FileMetadata instance if the version is found, null
        /// otherwise.</returns>
        public FileMetadata this[long version] {
            get {
                FileMetadata metadata;
                if (metadataByVersion.TryGetValue(version, out metadata)) {
                    return metadata;
                }
                return null;
            }
        }

        /// <summary>
        /// Determine whether the PluginImporter class is available in
        /// UnityEditor. Unity 4 does not have the PluginImporter class so
        /// it's not possible to modify asset metadata without hacking the
        /// .meta yaml files directly to enable / disable plugin targeting.
        /// </summary>
        internal static bool PluginImporterAvailable {
            get {
                return FindClass("UnityEditor", "UnityEditor.PluginImporter") != null;
            }
        }

        /// <summary>
        /// Construct an instance.
        /// </summary>
        /// <param name="filenameCanonical">Filename with metadata stripped.
        /// </param>
        public FileMetadataByVersion(string filenameCanonical) {
            this.filenameCanonical = filenameCanonical;
        }

        /// <summary>
        /// Add metadata to the set.
        /// </summary>
        public void Add(FileMetadata metadata) {
            System.Diagnostics.Debug.Assert(
                filenameCanonical == null ||
                metadata.filenameCanonical.Equals(filenameCanonical));
            metadataByVersion[metadata.CalculateVersion()] = metadata;
        }

        /// <summary>
        /// If this instance references a set of plugins, enable the most
        /// recent versions.
        /// </summary>
        /// <returns>true if any plugin metadata was modified and requires an
        /// AssetDatabase.Refresh(), false otherwise.</return>
        public bool EnableMostRecentPlugins() {
            bool modified = false;
            int versionIndex = 0;
            int numberOfVersions = metadataByVersion.Count;
            var disabledVersions = new List<string>();
            string enabledVersion = null;

            // If the canonical file is out of date, update it.
            if (numberOfVersions > 0) {
                FileMetadata mostRecentVersion =
                    (FileMetadata)((new ArrayList(metadataByVersion.Values))
                                   [numberOfVersions - 1]);
                if (mostRecentVersion.filename != filenameCanonical) {
                    FileMetadata canonicalMetadata = null;
                    foreach (var metadata in metadataByVersion.Values) {
                        if (metadata.filename == filenameCanonical) {
                            canonicalMetadata = metadata;
                            break;
                        }
                    }
                    if (!mostRecentVersion.RenameAsset(filenameCanonical)) {
                        return false;
                    }
                    if (canonicalMetadata != null) {
                        // Overwrote the current version with the rename
                        // operation.
                        metadataByVersion.Remove(
                            canonicalMetadata.CalculateVersion());
                        numberOfVersions = metadataByVersion.Count;
                    }
                    modified = true;
                }
            }

            // Configure targeting for each revision of the plugin.
            foreach (FileMetadata metadata in metadataByVersion.Values) {
                versionIndex++;
                PluginImporter pluginImporter = null;
                try {
                    pluginImporter =
                        (PluginImporter)metadata.GetAssetImporter();
                } catch (InvalidCastException) {
                    continue;
                }
                bool editorEnabled = metadata.GetEditorEnabled();
                var selectedTargets = metadata.GetBuildTargets();
                bool modifiedThisVersion = false;
                // Only enable the most recent plugin - SortedDictionary
                // orders keys in ascending order.
                bool obsoleteVersion = (numberOfVersions > 1 &&
                                        versionIndex < numberOfVersions);
                // If this is an obsolete version.
                if (obsoleteVersion) {
                    // Disable for all platforms and the editor.
                    editorEnabled = false;
                    selectedTargets = new HashSet<BuildTarget>();
                } else {
                    // Track the current version.
                    enabledVersion = metadata.versionString;
                }
                // Enable / disable editor and platform settings.
                if (pluginImporter.GetCompatibleWithEditor() !=
                    editorEnabled) {
                    pluginImporter.SetCompatibleWithEditor(editorEnabled);
                    modifiedThisVersion = true;
                }
                foreach (BuildTarget target in
                         FileMetadata.GetBuildTargetNameToEnum().Values) {
                    bool enabled = selectedTargets != null &&
                        selectedTargets.Contains(target);
                    try {
                        if (pluginImporter.GetCompatibleWithPlatform(target) !=
                            enabled) {
                            pluginImporter.SetCompatibleWithPlatform(
                                target, enabled);
                            modifiedThisVersion = true;
                        }
                    }
                    catch(Exception e) {
                      UnityEngine.Debug.LogWarning(
                        "Unexpected error enumerating targets: " + e.Message);
                    }
                }
                if (modifiedThisVersion) {
                    pluginImporter.SaveAndReimport();
                }
                // If the version was modified and it's obsolete keep track of
                // it to log it later.
                if (obsoleteVersion && modifiedThisVersion) {
                    disabledVersions.Add(metadata.versionString);
                }
                modified |= modifiedThisVersion;
            }
            // Log the versions that have been disabled and the version that
            // has been enabled.
            if (modified && enabledVersion != null &&
                VersionHandler.VerboseLoggingEnabled) {
                string message = (filenameCanonical + ": enabled version " +
                                  enabledVersion);
                if (disabledVersions.Count > 0) {
                    message += ("  obsolete versions disabled (" +
                                String.Join(", ", disabledVersions.ToArray()) +
                                ")");
                }
                UnityEngine.Debug.Log(message);
            }
            return modified;
        }

        /// <summary>
        /// Get all versions older than the newest version of each file with
        /// multiple versions specified in its' metadata.
        /// </summary>
        /// <returns>Set of obsolete files.</returns>
        public HashSet<string> FindObsoleteVersions() {
            HashSet<string> obsoleteFiles = new HashSet<string>();
            int versionIndex = 0;
            int numberOfVersions = Values.Count;
            foreach (var metadata in Values) {
                versionIndex++;
                if (versionIndex < numberOfVersions) {
                    obsoleteFiles.Add(metadata.filename);
                }
            }
            return obsoleteFiles;
        }
    }

    /// <summary>
    /// Set of FileMetadata grouped by filename with metadata stripped.
    /// For example, "stuff_tEditor_v1.0.0.dll" and "stuff_tEditor_v1.0.1.dll"
    /// will be referenced by FileMetadataVersions using the key "stuff.dll".
    /// </summary>
    public class FileMetadataSet {
        /// <summary>
        /// Dictionary of FileMetadataVersions indexed by filename with
        /// metadata stripped.
        /// </summary>
        private Dictionary<string, FileMetadataByVersion>
            metadataByCanonicalFilename =
                new Dictionary<string, FileMetadataByVersion>();

        /// <summary>
        /// Get the FileMetadataByVersion for each filename bucket in this set.
        /// </summary>
        public Dictionary<string, FileMetadataByVersion>.ValueCollection
                Values {
            get { return metadataByCanonicalFilename.Values; }
        }

        /// <summary>
        /// Construct an instance.
        /// </summary>
        public FileMetadataSet() { }

        /// <summary>
        /// Add file metadata to the set.
        /// </summary>
        public void Add(FileMetadata metadata) {
            FileMetadataByVersion metadataByVersion;
            string filenameCanonical = metadata.filenameCanonical;
            if (!metadataByCanonicalFilename.TryGetValue(
                    filenameCanonical, out metadataByVersion)) {
                metadataByVersion =
                    new FileMetadataByVersion(filenameCanonical);
            }
            metadataByVersion.Add(metadata);
            metadataByCanonicalFilename[filenameCanonical] = metadataByVersion;
        }

        /// <summary>
        /// For each plugin (DLL) referenced by this set, disable targeting
        /// for all versions and re-enable platform targeting for the most
        /// recent version.
        /// </summary>
        /// <returns>true if any plugin metadata was modified and requires an
        /// AssetDatabase.Refresh(), false otherwise.</return>
        public bool EnableMostRecentPlugins() {
            bool modified = false;

            // If PluginImporter isn't available it's not possible
            // to enable / disable targeting.
            if (!FileMetadataByVersion.PluginImporterAvailable) return false;

            foreach (var metadataByVersion in
                     metadataByCanonicalFilename.Values) {
                modified |= metadataByVersion.EnableMostRecentPlugins();
            }
            return modified;
        }

        /// <summary>
        /// Parse metadata from a set of filenames.
        /// </summary>
        /// <param name="assetFiles">Filenames to parse.</param>
        /// <returns>FileMetadataSet referencing metadata parsed from filenames
        /// ordered by version and bucketed by canonical filename.
        /// </returns>
        public static FileMetadataSet ParseFromFilenames(string[] filenames) {
            FileMetadataSet metadataSet = new FileMetadataSet();
            // Parse metadata from filenames and bucket by version.
            foreach (string filename in filenames) {
                metadataSet.Add(new FileMetadata(filename));
            }
            return metadataSet;
        }

        /// <summary>
        /// Filter the a set for files which have multiple versions and those
        /// with metadata that selects the set of target platforms.
        /// </summary>
        /// <param name="metadataSet">Set to filter.</param>
        /// <returns>Filtered MetadataSet.
        public static FileMetadataSet FindWithPendingUpdates(
                FileMetadataSet metadataSet) {
            FileMetadataSet outMetadataSet = new FileMetadataSet();
            foreach (var filenameAndMetadata in
                     metadataSet.metadataByCanonicalFilename) {
                var metadataByVersion = filenameAndMetadata.Value.Values;
                bool needsUpdate = metadataByVersion.Count > 1;
                if (!needsUpdate) {
                    foreach (var metadata in metadataByVersion) {
                        if ((metadata.targets != null &&
                             metadata.targets.Length > 0) ||
                            metadata.isManifest) {
                            needsUpdate = true;
                            break;
                        }
                    }
                }
                if (needsUpdate) {
                    outMetadataSet.metadataByCanonicalFilename[
                        filenameAndMetadata.Key] = filenameAndMetadata.Value;
                }
            }
            return outMetadataSet;
        }

        /// <summary>
        /// Search for metadata for an existing file given a canonical filename
        /// and version.
        /// </summary>
        /// <param name="filenameCanonical">Name of the file set to search
        /// for.</param>
        /// <param name="version">Version number of the file in the set.</param>
        /// <returns>Reference to the metadata if successful, null otherwise.
        /// </returns>
        public FileMetadata FindMetadata(string filenameCanonical,
                                         long version) {
            FileMetadataByVersion metadataByVersion;
            if (!metadataByCanonicalFilename.TryGetValue(
                    filenameCanonical, out metadataByVersion)) {
                return null;
            }
            return metadataByVersion[version];
        }
    }

    /// <summary>
    /// Stores current and obsolete file references for a package.
    /// </summary>
    public class ManifestReferences {
        /// <summary>
        /// Name of this package.
        /// </summary>
        public string filenameCanonical = null;

        /// <summary>
        /// Metadata which references the most recent version metadata file.
        /// </summary>
        public FileMetadata currentMetadata = null;

        /// <summary>
        /// Set of current files in this package.
        /// </summary>
        public HashSet<string> currentFiles = new HashSet<string>();

        /// <summary>
        /// Set of obsolete files in this package.
        /// </summary>
        public HashSet<string> obsoleteFiles = new HashSet<string>();

        /// <summary>
        /// Create an instance.
        /// </summary>
        public ManifestReferences() { }

        /// <summary>
        /// Parse current and obsolete file references from a package's
        /// manifest files.
        /// </summary>
        /// <param name="metadataByVersion">Metadata for files ordered by
        /// version number.  If the metadata does not have the isManifest
        /// attribute it is ignored.</param>
        /// <param name="metadataSet">Set of all metadata files in the
        /// project.  This is used to handle file renaming in the parsed
        /// manifest.  If the manifest contains files that have been
        /// renamed it's updated with the new filenames.</param>
        /// <returns>true if data was parsed from the specified file metadata,
        /// false otherwise.</returns>
        public bool ParseManifests(FileMetadataByVersion metadataByVersion,
                                   FileMetadataSet metadataSet) {
            currentFiles = new HashSet<string>();
            obsoleteFiles = new HashSet<string>();

            int versionIndex = 0;
            int numberOfVersions = metadataByVersion.Values.Count;
            foreach (FileMetadata metadata in metadataByVersion.Values) {
                versionIndex++;
                if (!metadata.isManifest) return false;
                bool manifestNeedsUpdate = false;
                HashSet<string> filesInManifest =
                    versionIndex < numberOfVersions ?
                        obsoleteFiles : currentFiles;
                StreamReader manifestFile =
                    new StreamReader(metadata.filename);
                string line;
                while ((line = manifestFile.ReadLine()) != null) {
                    var manifestFileMetadata = new FileMetadata(line.Trim());
                    string filename = manifestFileMetadata.filename;
                    // Check for a renamed file.
                    var existingFileMetadata =
                        metadataSet.FindMetadata(
                            manifestFileMetadata.filenameCanonical,
                            manifestFileMetadata.CalculateVersion());
                    if (existingFileMetadata != null &&
                        !manifestFileMetadata.filename.Equals(
                            existingFileMetadata.filename)) {
                        filename = existingFileMetadata.filename;
                        manifestNeedsUpdate = true;
                    }
                    filesInManifest.Add(filename);
                }
                manifestFile.Close();

                // If this is the most recent manifest version, remove all
                // current files from the set to delete.
                if (versionIndex == numberOfVersions) {
                    currentMetadata = metadata;
                    foreach (var currentFile in filesInManifest) {
                        obsoleteFiles.Remove(currentFile);
                    }
                }

                // Rewrite the manifest to track renamed files.
                if (manifestNeedsUpdate) {
                    File.Delete(metadata.filename);
                    var writer = new StreamWriter(metadata.filename);
                    foreach (var filename in filesInManifest) {
                        writer.WriteLine(filename);
                    }
                    writer.Close();
                }
            }
            this.filenameCanonical = metadataByVersion.filenameCanonical;
            return true;
        }

        /// <summary>
        /// Find and read all package manifests.
        /// </summary>
        /// <param name="metadataSet">Set to query for manifest files.</param>
        /// <returns>List of ManifestReferences which contain current and
        /// obsolete files referenced in each manifest file.</returns>
        public static List<ManifestReferences> FindAndReadManifests(
                FileMetadataSet metadataSet) {
            var manifestReferencesList = new List<ManifestReferences>();
            foreach (var metadataByVersion in metadataSet.Values) {
                ManifestReferences manifestReferences =
                    new ManifestReferences();
                if (manifestReferences.ParseManifests(metadataByVersion,
                                                      metadataSet)) {
                    manifestReferencesList.Add(manifestReferences);
                }
            }
            return manifestReferencesList;
        }
    }

    /// <summary>
    /// Set of obsolete filenames.
    /// </summary>
    public class ObsoleteFiles {

        /// <summary>
        /// Obsolete files that are not referenced by any manifests.
        /// </summary>
        public HashSet<string> unreferenced;

        /// <summary>
        /// Obsolete files that are referenced by manifests.  Each item in
        /// the dictionary contains a list of manifests referencing the file.
        /// </summary>
        public Dictionary<string, List<string>> referenced;

        /// <summary>
        /// Build an ObsoleteFiles instance searching a set of
        /// ManifestReferences and a FileMetadataSet for old files.
        /// Old files are bundled into unreferenced (i.e not referenced by a
        /// manifest that is not pending deletion) and reference (referenced
        /// by an active manifest).
        /// </summary>
        /// <param name="manifestReferencesList">List of manifests to query
        /// for obsolete files.</param>
        /// <param name="metadataSet">Set of metadata to query for obsolete
        /// files.<param>
        /// <returns>ObsoleteFiles instance which references the discovered
        /// obsolete files.</returns>
        public ObsoleteFiles(
                List<ManifestReferences> manifestReferencesList,
                FileMetadataSet metadataSet) {
            // Combine all currently referenced and obsolete files into a
            // global sets.
            var currentFiles = new HashSet<string>();
            var obsoleteFiles = new HashSet<string>();
            foreach (var manifestReferences in manifestReferencesList) {
                currentFiles.UnionWith(manifestReferences.currentFiles);
                obsoleteFiles.UnionWith(manifestReferences.obsoleteFiles);
            }
            // Fold in obsolete files that are not referenced by manifests.
            foreach (var metadataByVersion in metadataSet.Values) {
                obsoleteFiles.UnionWith(
                    metadataByVersion.FindObsoleteVersions());
            }
            // Filter the obsoleteFiles set for all obsolete files currently
            // in use and add to a dictionary indexed by filename
            // which contains a list of manifest filenames which reference
            // each file.
            var referencedObsoleteFiles =
                new Dictionary<string, List<string>>();
            var obsoleteFilesToDelete = new HashSet<string>();
            foreach (var obsoleteFile in obsoleteFiles) {
                var manifestsReferencingFile = new List<string>();
                foreach (var manifestReferences in manifestReferencesList) {
                    if (manifestReferences.currentFiles.Contains(
                            obsoleteFile)) {
                        manifestsReferencingFile.Add(
                            manifestReferences.currentMetadata.filename);
                    }
                }
                // If the referenced file doesn't exist, ignore it.
                if (!File.Exists(obsoleteFile)) {
                    continue;
                }
                if (manifestsReferencingFile.Count > 0) {
                    referencedObsoleteFiles[obsoleteFile] =
                        manifestsReferencingFile;
                } else {
                    obsoleteFilesToDelete.Add(obsoleteFile);
                }
            }
            unreferenced = obsoleteFilesToDelete;
            referenced = referencedObsoleteFiles;
        }
    }

    // Keys in the editor preferences which control the behavior of this
    // module.
    private const string PREFERENCE_ENABLED =
        "Google.VersionHandler.VersionHandlingEnabled";
    private const string PREFERENCE_CLEANUP_PROMPT_ENABLED =
        "Google.VersionHandler.CleanUpPromptEnabled";
    private const string PREFERENCE_VERBOSE_LOGGING_ENABLED =
        "Google.VersionHandler.VerboseLoggingEnabled";

    // Name of this plugin.
    private const string PLUGIN_NAME = "Google Version Handler";

    /// <summary>
    /// Enables / disables assets imported at multiple revisions / versions.
    /// In addition, this module will read text files matching _manifest_
    /// and remove files from older manifest files.
    /// </summary>
    static VersionHandler() {
        UpdateVersionedAssets();
    }

    /// <summary>
    /// Enable / disable automated version handling.
    /// </summary>
    public static bool Enabled {
        get {
            return !System.Environment.CommandLine.Contains("-batchmode") &&
                EditorPrefs.GetBool(PREFERENCE_ENABLED, defaultValue: true);
        }
        set { EditorPrefs.SetBool(PREFERENCE_ENABLED, value); }
    }

    /// <summary>
    /// Enable / disable prompting the user on clean up.
    /// </summary>
    public static bool CleanUpPromptEnabled {
        get { return EditorPrefs.GetBool(PREFERENCE_CLEANUP_PROMPT_ENABLED,
                                         defaultValue: true); }
        set { EditorPrefs.SetBool(PREFERENCE_CLEANUP_PROMPT_ENABLED, value); }
    }

    /// <summary>
    /// Enable / disable verbose logging.
    /// </summary>
    public static bool VerboseLoggingEnabled {
        get { return System.Environment.CommandLine.Contains("-batchmode") ||
                EditorPrefs.GetBool(PREFERENCE_VERBOSE_LOGGING_ENABLED,
                                    defaultValue: false); }
        set { EditorPrefs.SetBool(PREFERENCE_VERBOSE_LOGGING_ENABLED, value); }
    }

    /// <summary>
    /// Add the settings dialog for this module to the menu and show the
    /// window when the menu item is selected.
    /// </summary>
    [MenuItem("Assets/Play Services Resolver/Version Handler/Settings")]
    public static void ShowSettings() {
        SettingsDialog window = (SettingsDialog)EditorWindow.GetWindow(
            typeof(SettingsDialog), true, PLUGIN_NAME + " Settings");
        window.Initialize();
        window.Show();
    }

    /// <summary>
    /// Menu item which forces version handler execution.
    /// </summary>
    [MenuItem("Assets/Play Services Resolver/Version Handler/Update")]
    public static void UpdateNow() {
        UpdateVersionedAssets(forceUpdate: true);
        EditorUtility.DisplayDialog(PLUGIN_NAME, "Update complete.", "OK");
    }

    /// <summary>
    /// Delegate used to filter a file and directory names.
    /// </summary>
    /// <returns>true if the filename should be returned by an enumerator,
    /// false otherwise.</returns>
    /// <param name="filename">Name of the file / directory to filter.</param>
    public delegate bool FilenameFilter(string filename);

    /// <summary>
    /// Search the asset database for all files matching the specified filter.
    /// </summary>
    /// <returns>Array of matching files.</returns>
    /// <param name="assetsFilter">Filter used to query the
    /// AssetDatabase.  If this isn't specified, all assets are searched.
    /// </param>
    /// <param name="filter">Optional delegate to filter the returned
    /// list.</param>
    public static string[] SearchAssetDatabase(string assetsFilter = null,
                                               FilenameFilter filter = null) {
        HashSet<string> matchingEntries = new HashSet<string>();
        assetsFilter = assetsFilter != null ? assetsFilter : "t:Object";
        foreach (string assetGuid in AssetDatabase.FindAssets(assetsFilter)) {
            string filename = AssetDatabase.GUIDToAssetPath(assetGuid);
            if (filter == null || filter(filename)) {
                matchingEntries.Add(filename);
            }
        }
        string[] entries = new string[matchingEntries.Count];
        matchingEntries.CopyTo(entries);
        return entries;
    }

    /// <summary>
    /// Get all assets managed by this module.
    /// </summary>
    public static string[] FindAllAssets() {
        return SearchAssetDatabase(
            assetsFilter: "l:" + FileMetadata.ASSET_LABEL);
    }

    /// <summary>
    /// Move an asset to trash, writing to the log if logging is enabled.
    /// </summary>
    private static void MoveAssetToTrash(string filename) {
        if (VerboseLoggingEnabled) {
            UnityEngine.Debug.Log("Moved obsolete file to trash: " + filename);
        }
        if (!AssetDatabase.MoveAssetToTrash(filename)) {
            UnityEngine.Debug.LogError(
                "Failed to move obsolete file to trash: " + filename);
        }
    }

    /// <summary>
    /// Find all files in the asset database with multiple version numbers
    /// encoded in their filename, select the most recent revisions and
    /// delete obsolete versions and files referenced by old manifests that
    /// are not present in the most recent manifests.
    /// </summary>
    public static void UpdateVersionedAssets(bool forceUpdate = false) {
        // If this module is disabled do nothing.
        if (!forceUpdate && !Enabled) return;

        var metadataSet = FileMetadataSet.FindWithPendingUpdates(
            FileMetadataSet.ParseFromFilenames(FindAllAssets()));

        if (metadataSet.EnableMostRecentPlugins()) {
            AssetDatabase.Refresh();
        }

        var obsoleteFiles = new ObsoleteFiles(
            ManifestReferences.FindAndReadManifests(metadataSet), metadataSet);

        // Obsolete files that are no longer reference can be safely
        // deleted, prompt the user for confirmation if they have the option
        // enabled.
        bool deleteFiles = true;
        if (obsoleteFiles.unreferenced.Count > 0) {
            if (CleanUpPromptEnabled && deleteFiles) {
                deleteFiles = EditorUtility.DisplayDialog(
                    PLUGIN_NAME,
                    "Would you like to delete the following obsolete files " +
                    "in your project?\n\n" +
                    String.Join("\n", new List<string>(
                                        obsoleteFiles.unreferenced).ToArray()),
                    "Yes", cancel: "No");
            }
            foreach (var filename in obsoleteFiles.unreferenced) {
                if (deleteFiles) {
                    MoveAssetToTrash(filename);
                } else if (VerboseLoggingEnabled) {
                    UnityEngine.Debug.Log("Leaving obsolete file: " +
                                          filename);
                }
            }
        }

        // If any obsolete referenced files are present, prompt the user for
        // confirmation of deletion.
        if (obsoleteFiles.referenced.Count > 0) {
            List<string> referencesString = new List<string>();
            foreach (var item in obsoleteFiles.referenced) {
                List<string> lines = new List<string>();
                lines.Add(item.Key);
                foreach (var reference in item.Value) {
                    lines.Add("    " + reference);
                }
                referencesString.Add(String.Join("\n", lines.ToArray()));
            }
            deleteFiles = EditorUtility.DisplayDialog(
               PLUGIN_NAME,
               "The following obsolete files are referenced by packages in " +
               "your project, would you like to delete them?\n\n" +
               String.Join("\n", referencesString.ToArray()),
               "Yes", cancel: "No");

            foreach (var item in obsoleteFiles.referenced) {
                if (deleteFiles) {
                    MoveAssetToTrash(item.Key);
                } else if (VerboseLoggingEnabled) {
                    UnityEngine.Debug.Log(
                        "Leaving obsolete file: " + item.Key + " | " +
                        "Referenced by (" +
                        String.Join(", ", item.Value.ToArray())  + ")");
                }
            }
        }
    }

    /// <summary>
    /// Scanned for versioned assets and apply modifications if required.
    /// </summary>
    private static void OnPostProcessAllAssets (
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromPath) {
        UpdateVersionedAssets();
    }


    /// <summary>
    /// Find a class from an assembly by name.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to search for.</param>
    /// <param name="className">Name of the class to find.</param>
    /// <returns>The Type of the class if found, null otherwise.</returns>
    public static Type FindClass(string assemblyName, string className) {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (assembly.GetName().Name == assemblyName) {
                return Type.GetType(className + ", " + assembly.FullName);
            }
        }
        return null;
    }

    /// <summary>
    /// Call a method on an object with named arguments.
    /// </summary>
    /// <param name="objectInstance">Object to call a method on.</param>
    /// <param name="methodName">Name of the method to call.</param>
    /// <param name="arg">Positional arguments of the method.</param>
    /// <param name="namedArgs">Named arguments of the method.</param>
    /// <returns>object returned by the method.</returns>
    public static object InvokeInstanceMethod(
            object objectInstance, string methodName, object[] args,
            Dictionary<string, object> namedArgs = null) {
        return InvokeMethod(objectInstance.GetType(),
                            objectInstance, methodName, args: args,
                            namedArgs: namedArgs);
    }

    /// <summary>
    /// Call a static method on an object.
    /// </summary>
    /// <param name="type">Class to call the method on.</param>
    /// <param name="methodName">Name of the method to call.</param>
    /// <param name="arg">Positional arguments of the method.</param>
    /// <param name="namedArgs">Named arguments of the method.</param>
    /// <returns>object returned by the method.</returns>
    public static object InvokeStaticMethod(
            Type type, string methodName, object[] args,
            Dictionary<string, object> namedArgs = null) {
        return InvokeMethod(type, null, methodName, args: args,
                            namedArgs: namedArgs);
    }

    /// <summary>
    /// Call a method on an object with named arguments.
    /// </summary>
    /// <param name="type">Class to call the method on.</param>
    /// <param name="objectInstance">Object to call a method on.</param>
    /// <param name="methodName">Name of the method to call.</param>
    /// <param name="arg">Positional arguments of the method.</param>
    /// <param name="namedArgs">Named arguments of the method.</param>
    /// <returns>object returned by the method.</returns>
    public static object InvokeMethod(
            Type type, object objectInstance, string methodName,
            object[] args, Dictionary<string, object> namedArgs = null) {
        MethodInfo method = type.GetMethod(methodName);
        ParameterInfo[] parameters = method.GetParameters();
        int numParameters = parameters.Length;
        object[] parameterValues = new object[numParameters];
        int numPositionalArgs = args != null ? args.Length : 0;
        foreach (var parameter in parameters) {
            int position = parameter.Position;
            if (position < numPositionalArgs) {
                parameterValues[position] = args[position];
                continue;
            }
            object namedValue = parameter.RawDefaultValue;
            if (namedArgs != null) {
                object overrideValue;
                if (namedArgs.TryGetValue(parameter.Name, out overrideValue)) {
                    namedValue = overrideValue;
                }
            }
            parameterValues[position] = namedValue;
        }
        return method.Invoke(objectInstance, parameterValues);
    }
}

} // namespace Google

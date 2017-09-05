// <copyright file="VersionHandlerImpl.cs" company="Google Inc.">
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
using System.Globalization;
using System.IO;
using System.Reflection;
using System;

namespace Google {

[InitializeOnLoad]
public class VersionHandlerImpl : AssetPostprocessor {
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
        private static string[] TOKEN_TARGETS = new [] { "targets-", "t" };
        // Prefix which identifies the version metadata in the filename or
        // asset label.
        private static string[] TOKEN_VERSION = new [] { "version-", "v" };
        // Prefix which identifies the .NET version metadata in the filename
        // or asset label.
        private static string[] TOKEN_DOTNET_TARGETS = new [] { "dotnet-" };
        // Prefix which indicates this file is a package manifest.
        private static string[] TOKEN_MANIFEST = new [] { "manifest" };
        // Prefix which identifies the canonical name of this Linux library.
        private static string[] TOKEN_LINUX_LIBRARY_BASENAME = new [] { "linuxlibname-" };

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

        // Special build target name which enables all platforms / build targets.
        public static string BUILD_TARGET_NAME_ANY = "any";

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
            {"n3rds", "N3DS"},
            {"nintendo3ds", "Nintendo3DS"},
            {"wiiu", "WiiU"},
            {"tvos", "tvOS"},
            {"switch", "Switch"},
        };

        // Available .NET framework versions.
        public const string DOTNET_RUNTIME_VERSION_LEGACY = "3.5";
        public const string DOTNET_RUNTIME_VERSION_LATEST = "4.5";
        // Matches the .NET framework 3.5 enum value
        // UnityEditor.PlayerSettings.scriptingRuntimeVersion in Unity 2017 and beyond.
        private const string SCRIPTING_RUNTIME_LEGACY = "Legacy";

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
                if (VersionHandlerImpl.GetUnityVersionMajorMinor() >= 5.5) {
                    targetBlackList.Add(BuildTarget.PS3);
                    targetBlackList.Add(BuildTarget.XBOX360);
                }
            }
            return targetBlackList;
        }

        /// <summary>
        /// Property that retrieves UnityEditor.PlayerSettings.scriptingRuntimeVersion.
        /// </summary>
        /// This retrieves UnityEditor.PlayerSettings.scriptingRuntimeVersion using reflection
        /// so that this module is compatible with versions of Unity earlier than 2017.
        /// <returns>String representation of
        /// UnityEditor.PlayerSettings.scriptingRuntimeVersion.  If the property isn't
        /// available this returns SCRIPTING_RUNTIME_LEGACY.</returns>
        private static string ScriptingRuntimeVersion {
            get {
                var scriptingVersionProperty =
                    typeof(UnityEditor.PlayerSettings).GetProperty("scriptingRuntimeVersion");
                var scriptingVersionValue = scriptingVersionProperty != null ?
                    scriptingVersionProperty.GetValue(null, null) : null;
                var scriptingVersionString = scriptingVersionValue != null ?
                    scriptingVersionValue.ToString() : SCRIPTING_RUNTIME_LEGACY;
                return scriptingVersionString;
            }
        }

        /// <summary>
        /// Retrieve the UnityEditor.PlayerSettings.scriptingRuntimeVersion as a version string.
        /// </summary>
        /// <returns>.NET version string.</returns>
        public static string ScriptingRuntimeDotNetVersion {
            get {
                if (ScriptingRuntimeVersion == SCRIPTING_RUNTIME_LEGACY) {
                    return FileMetadata.DOTNET_RUNTIME_VERSION_LEGACY;
                }
                return FileMetadata.DOTNET_RUNTIME_VERSION_LATEST;
            }
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
        /// List of compatible .NET versions parsed from this asset.
        /// </summary>
        public string[] dotNetTargets = null;

        /// <summary>
        /// Basename of a Linux library plugin.
        /// </summary>
        public string linuxLibraryBasename = null;

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
                    ParseToken(tokens[i]);
                }
            }
            // Parse metadata from asset labels if it hasn't been specified in
            // the filename.
            AssetImporter importer = GetAssetImporter();
            if (importer != null) {
                foreach (string label in AssetDatabase.GetLabels(importer)) {
                    ParseLabel(label);
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
        /// Determine whether a token matches a set of prefixes extracting the value(s) if it
        /// does.
        /// </summary>
        /// <param name="token">Token to parse.</param>
        /// <param name="prefixes">Set of prefixes to compare with the token.</param>
        /// <param name="prefix">Added to each item in prefixes before comparing with the token.
        /// </param>
        /// <returns>Array of values if the token matches the prefxies, null otherwise.</returns>
        private string[] MatchPrefixesGetValues(string token, string[] prefixes, string prefix) {
            var tokenLower = token.ToLower();
            foreach (var item in prefixes) {
                var itemWithPrefix = prefix + item;
                if (tokenLower.StartsWith(itemWithPrefix)) {
                    return token.Substring(itemWithPrefix.Length).Split(FIELD_SEPARATOR);
                }
            }
            return null;
        }

        /// <summary>
        /// Parse a metadata token and store the value this class.
        /// </summary>
        /// <param name="token">Token to parse.</param>
        /// <param name="prefix">Added as a prefix applied to each prefix compared with the
        /// token.</param>
        /// <returns>true if the token is parsed, false otherwise.</returns>
        private bool ParseToken(string token, string prefix = null) {
            prefix = prefix ?? "";
            var values = MatchPrefixesGetValues(token, TOKEN_MANIFEST, prefix);
            if (values != null) {
                isManifest = true;
                return true;
            }
            values = MatchPrefixesGetValues(token, TOKEN_DOTNET_TARGETS, prefix);
            if (values != null) {
                dotNetTargets = values;
                return true;
            }
            values = MatchPrefixesGetValues(token, TOKEN_TARGETS, prefix);
            if (values != null) {
                if (targets == null) {
                    // Convert all target names to lower case.
                    targets = new string[values.Length];
                    for (int i = 0; i < targets.Length; ++i) {
                        targets[i] = values[i].ToLower();
                    }
                    return true;
                }
            }
            values = MatchPrefixesGetValues(token, TOKEN_VERSION, prefix);
            if (values != null) {
                if (String.IsNullOrEmpty(versionString) && values.Length > 0) {
                    versionString = values[0];
                    return true;
                }
            }
            values = MatchPrefixesGetValues(token, TOKEN_LINUX_LIBRARY_BASENAME, prefix);
            if (values != null) {
                linuxLibraryBasename = values[0];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Parse a metadata token (from an asset label) and store the value this class.
        /// </summary>
        /// <param name="label">Asset label to parse.</param>
        /// <returns>true if the token is parsed, false otherwise.</returns>
        private bool ParseLabel(string label) {
            return ParseToken(label, prefix: LABEL_PREFIX);
        }

        /// <summary>
        /// Create a field from a prefix and set of values.
        /// </summary>
        /// <param name="fieldPrefixes">The first item of this list is used as the prefix.
        /// </param>
        /// <param name="values">Set of values to store with the field.</param>
        private string CreateToken(string[] fieldPrefixes, string[] values) {
            return String.Format("{0}{1}", fieldPrefixes[0],
                                 values == null ? "" :
                                     String.Join(Char.ToString(FIELD_SEPARATOR[0]), values));
        }

        /// <summary>
        /// Create an asset label from a prefix and set of values.
        /// </summary>
        /// <param name="fieldPrefixes">The first item of this list is used as the prefix.
        /// </param>
        /// <param name="values">Set of values to store with the field.</param>
        private string CreateLabel(string[] fieldPrefixes, string[] values) {
            return LABEL_PREFIX + CreateToken(fieldPrefixes, values);
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
        /// Determine whether any build targets have been specified.
        /// </summary>
        /// <returns>true if targets are specified, false otherwise.</returns>
        public bool GetBuildTargetsSpecified() {
            return targets != null && targets.Length > 0;
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
                var targetsSet = new HashSet<string>(targets);
                if (targetsSet.Contains(BUILD_TARGET_NAME_ANY)) {
                    buildTargetSet.UnionWith(buildTargetToEnum.Values);
                } else {
                    foreach (string target in targets) {
                        BuildTarget buildTarget;
                        if (buildTargetToEnum.TryGetValue(target, out buildTarget)) {
                            buildTargetSet.Add(buildTarget);
                        } else if (!target.Equals("editor")) {
                            Log(filename + " reference to unknown target " +
                                target + " the version handler may out of date.",
                                level: LogLevel.Error);
                        }
                    }
                }
            }
            return buildTargetSet;
        }

        /// <summary>
        /// Get the list of .NET versions this file is compatible with.
        /// </summary>
        /// <returns>Set of .NET versions this is compatible with.  This returns an empty
        /// set if the state of the file should not be modified when the .NET framework
        /// version changes.</returns>
        public HashSet<string> GetDotNetTargets() {
            var dotNetTargetSet = new HashSet<string>();
            if (dotNetTargets != null) dotNetTargetSet.UnionWith(dotNetTargets);
            return dotNetTargetSet;
        }

        /// <summary>
        /// Save metadata from this class into the asset's labels.
        /// </summary>
        public void UpdateAssetLabels() {
            if (String.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(filename))) return;
            AssetImporter importer = AssetImporter.GetAtPath(filename);
            var labels = new List<string>();
            var currentLabels = new List<string>();
            // Strip labels we're currently managing.
            foreach (string label in AssetDatabase.GetLabels(importer)) {
                currentLabels.Add(label);
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
                labels.Add(CreateLabel(TOKEN_VERSION, new [] { versionString }));
            }
            if (targets != null && targets.Length > 0) {
                labels.Add(CreateLabel(TOKEN_TARGETS, targets));
            }
            if (dotNetTargets != null && dotNetTargets.Length > 0) {
                labels.Add(CreateLabel(TOKEN_DOTNET_TARGETS, dotNetTargets));
            }
            if (!String.IsNullOrEmpty(linuxLibraryBasename)) {
                labels.Add(CreateLabel(TOKEN_LINUX_LIBRARY_BASENAME,
                                       new [] { linuxLibraryBasename }));
            }
            if (isManifest) {
                labels.Add(CreateLabel(TOKEN_MANIFEST, null));
            }
            if (!(new HashSet<string>(labels)).SetEquals(new HashSet<string>(currentLabels))) {
                Log(String.Format("Changing labels of {0}\n" +
                                  "from: {1}\n" +
                                  "to: {2}\n",
                                  filename,
                                  String.Join(", ", currentLabels.ToArray()),
                                  String.Join(", ", labels.ToArray())),
                    verbose: true);
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
            Log(String.Format("Renaming {0} -> {1}", filename, newFilename), verbose: true);
            // If the target file exists, delete it.
            if (AssetImporter.GetAtPath(newFilename) != null) {
                if (!AssetDatabase.MoveAssetToTrash(newFilename)) {
                    Log("Failed to move asset to trash: " + filename,
                        level: LogLevel.Error);
                    return false;
                }
            }
            try {
              // This is *really* slow.
              string error = AssetDatabase.RenameAsset(
                  filename, filenameComponents.basenameNoExtension);
              if (!String.IsNullOrEmpty(error)) {
                  Log("Failed to rename asset " + filename + " to " +
                      newFilename + " (" + error + ")",
                      level: LogLevel.Error);
                  return false;
              }
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
        /// Get the most referenced FileMetadata from this object.
        /// </summary>
        /// <returns>FileMetadata instance if this object contains at least one version, null
        /// otherwise.</returns>
        public FileMetadata MostRecentVersion {
            get {
                var numberOfVersions = metadataByVersion.Count;
                return numberOfVersions > 0 ?
                    (FileMetadata)(
                        (new ArrayList(metadataByVersion.Values))[numberOfVersions - 1]) : null;
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
                return VersionHandler.FindClass("UnityEditor",
                                                "UnityEditor.PluginImporter") != null;
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
                FileMetadata mostRecentVersion = MostRecentVersion;
                if (mostRecentVersion.filename != filenameCanonical &&
                    RenameToCanonicalFilenames) {
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
            var dotNetVersion = FileMetadata.ScriptingRuntimeDotNetVersion;
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
                var hasBuildTargets = metadata.GetBuildTargetsSpecified();
                var dotNetTargets = metadata.GetDotNetTargets();
                bool hasDotNetTargets = dotNetTargets.Count > 0;
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
                    if (hasDotNetTargets) {
                        // Determine whether this is supported by the selected .NET version.
                        bool dotNetSupported = dotNetTargets.Contains(dotNetVersion);
                        editorEnabled = dotNetSupported;
                        if (dotNetSupported) {
                            if (!hasBuildTargets) {
                                // If no build targets are specified, target everything except the
                                // editor.
                                selectedTargets = new HashSet<BuildTarget>(
                                    FileMetadata.GetBuildTargetNameToEnum().Values);
                                hasBuildTargets = true;
                            }
                        } else {
                            selectedTargets = new HashSet<BuildTarget>();
                        }
                    }
                    // Track the current version.
                    enabledVersion = metadata.versionString;
                }
                // If the metadata for this file specifies no build setting changes, ignore the
                // file.
                if (!(obsoleteVersion || hasDotNetTargets || hasBuildTargets)) {
                    continue;
                }
                var dotNetVersionMessage = hasDotNetTargets ? String.Format(
                        "\n.NET selected {0}, supported ({1})", dotNetVersion,
                        String.Join(", ", (new List<string>(dotNetTargets)).ToArray())) : "";
                // Enable / disable editor and platform settings.
                if (pluginImporter.GetCompatibleWithEditor() != editorEnabled) {
                    Log(String.Format("{0}: editor enabled {1} (current: {2}){3}",
                                      metadata.filename,
                                      editorEnabled,
                                      pluginImporter.GetCompatibleWithEditor(),
                                      dotNetVersionMessage),
                        verbose: true);
                    pluginImporter.SetCompatibleWithEditor(editorEnabled);
                    modifiedThisVersion = true;
                }
                bool compatibleWithAnyPlatform = pluginImporter.GetCompatibleWithAnyPlatform();
                foreach (BuildTarget target in
                         FileMetadata.GetBuildTargetNameToEnum().Values) {
                    bool enabled = selectedTargets != null &&
                        selectedTargets.Contains(target);
                    // If we need to explicitly target a platform disable the compatible with any
                    // platform flag.
                    if (!enabled && compatibleWithAnyPlatform) {
                        pluginImporter.SetCompatibleWithAnyPlatform(false);
                        compatibleWithAnyPlatform = false;
                    }
                    try {
                        bool compatibleWithTarget =
                            pluginImporter.GetCompatibleWithPlatform(target);
                        if (compatibleWithTarget != enabled) {
                            Log(String.Format("{0}: {1} enabled {2} (current: {3}){4}",
                                              metadata.filename, target, enabled,
                                              compatibleWithTarget, dotNetVersionMessage),
                                verbose: true);
                            pluginImporter.SetCompatibleWithPlatform(
                                target, enabled);
                            modifiedThisVersion = true;
                        }
                    }
                    catch(Exception e) {
                        Log("Unexpected error enumerating targets: " + e.Message,
                            level: LogLevel.Warning);
                    }
                }
                // When explicitly setting platform compatibility we need to disable Unity's
                // "any" platform match for it to take effect.
                if (modifiedThisVersion && hasBuildTargets) {
                    pluginImporter.SetCompatibleWithAnyPlatform(false);
                }
                // Some versions of Unity (e.g 5.6) do not mark the asset
                // database as dirty when plugin importer settings change.
                // Therefore, force a reimport of each file touched by the
                // plugin importer.
                if (modifiedThisVersion) {
                    AssetDatabase.ImportAsset(metadata.filename,
                                              ImportAssetOptions.ForceUpdate);
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
            if (modified && !String.IsNullOrEmpty(enabledVersion)) {
                string message = (filenameCanonical + ": enabled version " +
                                  enabledVersion);
                if (disabledVersions.Count > 0) {
                    message += ("  obsolete versions disabled (" +
                                String.Join(", ", disabledVersions.ToArray()) +
                                ")");
                }
                Log(message, verbose: true);
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
        /// <param name="forceUpdate">Whether the update was forced by the
        /// user.</param>
        /// <returns>true if any plugin metadata was modified and requires an
        /// AssetDatabase.Refresh(), false otherwise.</return>
        public bool EnableMostRecentPlugins(bool forceUpdate) {
            bool modified = false;

            // If PluginImporter isn't available it's not possible
            // to enable / disable targeting.
            if (!FileMetadataByVersion.PluginImporterAvailable) {
                // File that stores the state of the warning flag for this editor session.
                // We need to store this in a file as this DLL can be reloaded while the
                // editor is open resetting any state in memory.
                string warningFile = Path.Combine(
                    "Temp", "VersionHandlerEnableMostRecentPlugins.txt");
                string warning =
                    "UnityEditor.PluginImporter is not supported in this version of Unity.\n\n" +
                    "Plugins managed by VersionHandler will not be enabled.\n" +
                    "You need to manually enable / disable the most recent version of each \n" +
                    "file you wish to use.\n\n" +
                    "Found the following VersionHandler managed files:\n" +
                    "{0}\n" +
                    "\n" +
                    "To resolve this:\n" +
                    "* Remove the oldest version of each file.\n" +
                    "  Each file either has the version as part of the filename or the version\n" +
                    "  is asset tag in the form gvh_vVERSION that is visible via the inspector\n" +
                    "  when selecting the file.\n" +
                    "* Enable the remaining files for the specified build targets.\n" +
                    "  For example, if the file has \"Targets: [editor]\":\n" +
                    "  - Select the file.\n" +
                    "  - In Unity 5.x:\n" +
                    "    - Open the inspector.\n" +
                    "    - Check the \"editor\" box.\n" +
                    "  - In Unity 4.x:\n" +
                    "    - Select 'Assets > Reimport' from the menu.\n";
                var warningLines = new List<string>();
                foreach (var metadataByVersion in
                         metadataByCanonicalFilename.Values) {
                    bool hasRelevantVersions = false;
                    var fileInfoLines = new List<string>();
                    fileInfoLines.Add(String.Format("Target Filename: {0}",
                                                    metadataByVersion.filenameCanonical));
                    foreach (var metadata in metadataByVersion.Values) {
                        // Ignore manifests and files that don't target any build targets.
                        if (metadata.isManifest ||
                            metadata.targets == null || metadata.targets.Length == 0) {
                            continue;
                        }
                        // Ignore missing files.
                        string currentFilename = File.Exists(metadata.filename) ?
                            metadata.filename :
                            File.Exists(metadataByVersion.filenameCanonical) ?
                            metadataByVersion.filenameCanonical : null;
                        if (String.IsNullOrEmpty(currentFilename)) continue;

                        hasRelevantVersions = true;
                        fileInfoLines.Add(String.Format(
                                "    Version: {0}\n" +
                                "      Current Filename: {1}\n" +
                                "      Targets: [{2}]",
                                metadata.versionString,
                                currentFilename,
                                metadata.targets != null ?
                                    String.Join(", ", metadata.targets) : ""));
                    }
                    fileInfoLines.Add("");
                    if (hasRelevantVersions) warningLines.AddRange(fileInfoLines);
                }
                if (warningLines.Count > 0 && (forceUpdate || !File.Exists(warningFile))) {
                    // Touch the warning file to prevent this showing up when this method
                    // isn't run interactively.
                    using (var filestream = File.Open(warningFile, FileMode.OpenOrCreate)) {
                        filestream.Close();
                    }
                    Log(String.Format(warning, String.Join("\n", warningLines.ToArray())),
                        level: LogLevel.Warning);
                }
                return false;
            }

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
        /// Filter the a set for files which have multiple versions or those
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
                foreach (var metadata in metadataByVersion) {
                    if ((metadata.targets != null &&
                         metadata.targets.Length > 0) ||
                        metadata.isManifest) {
                        needsUpdate = true;
                        break;
                    }
                }
                if (needsUpdate) {
                    Log(filenameAndMetadata.Key + " metadata will be checked",
                        verbose: true);
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
        /// Metadata for each version of this manifest.
        /// </summary>
        public FileMetadataByVersion metadataByVersion = null;

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
                this.metadataByVersion = metadataByVersion;
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
        /// Same as the "unreferenced" member exluding manifest files.
        /// </summary>
        public HashSet<string> unreferencedExcludingManifests;

        /// <summary>
        /// Obsolete files that are referenced by manifests.  Each item in
        /// the dictionary contains a list of manifests referencing the file.
        /// </summary>
        public Dictionary<string, List<string>> referenced;

        /// <summary>
        /// Same as the "referenced" member exluding manifest files.
        /// </summary>
        public Dictionary<string, List<string>> referencedExcludingManifests;

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
            var manifestFilenames = new HashSet<string>();
            foreach (var manifestReferences in manifestReferencesList) {
                currentFiles.UnionWith(manifestReferences.currentFiles);
                obsoleteFiles.UnionWith(manifestReferences.obsoleteFiles);
                foreach (var manifestMetadata in manifestReferences.metadataByVersion.Values) {
                    manifestFilenames.Add(manifestMetadata.filename);
                }
            }
            // Fold in obsolete files that are not referenced by manifests.
            foreach (var metadataByVersion in metadataSet.Values) {
                var obsoleteVersions = metadataByVersion.FindObsoleteVersions();
                obsoleteFiles.UnionWith(obsoleteVersions);
                if (metadataByVersion.MostRecentVersion.isManifest) {
                    manifestFilenames.UnionWith(obsoleteVersions);
                }
            }
            // Filter the obsoleteFiles set for all obsolete files currently
            // in use and add to a dictionary indexed by filename
            // which contains a list of manifest filenames which reference
            // each file.
            var referencedObsoleteFiles =
                new Dictionary<string, List<string>>();
            var referencedObsoleteFilesExcludingManifests = new Dictionary<string, List<string>>();
            var obsoleteFilesToDelete = new HashSet<string>();
            var obsoleteFilesToDeleteExcludingManifests = new HashSet<string>();
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
                bool isManifest = manifestFilenames.Contains(obsoleteFile);
                if (manifestsReferencingFile.Count > 0) {
                    referencedObsoleteFiles[obsoleteFile] =
                        manifestsReferencingFile;
                    if (!isManifest) {
                        referencedObsoleteFilesExcludingManifests[obsoleteFile] =
                            manifestsReferencingFile;
                    }
                } else {
                    obsoleteFilesToDelete.Add(obsoleteFile);
                    if (!isManifest) {
                        obsoleteFilesToDeleteExcludingManifests.Add(obsoleteFile);
                    }
                }
            }
            unreferenced = obsoleteFilesToDelete;
            unreferencedExcludingManifests = obsoleteFilesToDeleteExcludingManifests;
            referenced = referencedObsoleteFiles;
            referencedExcludingManifests = referencedObsoleteFilesExcludingManifests;
        }
    }

    /// <summary>
    /// Finds and renames Linux libraries.
    /// </summary>
    public class LinuxLibraryRenamer {

        /// <summary>
        /// Retrieves the list of library files managed by this module.
        /// </summary>
        public List<FileMetadata> Libraries { get; private set; }

        // Directories relative to the Assets folder that contain native plugins.
        private string PLUGIN_DIR_X86 = Path.Combine("Assets", Path.Combine("Plugins", "x86"));
        private string PLUGIN_DIR_X86_64 =
            Path.Combine("Assets", Path.Combine("Plugins", "x86_64"));
        private string LIBRARY_EXTENSION = ".so";

        /// <summary>
        /// Construct this object searching the specified set of metadata for Linux libraries.
        /// </summary>
        public LinuxLibraryRenamer(FileMetadataSet metadataSet) {
            var foundLibraries = new List<FileMetadata>();
            foreach (var metadataByVersion in metadataSet.Values) {
                var mostRecent = metadataByVersion.MostRecentVersion;
                var dir = Path.GetDirectoryName(mostRecent.filename);
                if (!String.IsNullOrEmpty(mostRecent.linuxLibraryBasename) &&
                    mostRecent.filename.EndsWith(mostRecent.linuxLibraryBasename +
                                                 LIBRARY_EXTENSION) &&
                    (dir.StartsWith(PLUGIN_DIR_X86) ||
                     dir.StartsWith(PLUGIN_DIR_X86_64))) {
                    foundLibraries.Add(mostRecent);
                }
            }
            Libraries = foundLibraries;
        }

        /// <summary>
        /// Rename libraries for the loaded Unity version.
        /// </summary>
        public void RenameLibraries() {
            foreach (var library in Libraries) {
                var filename = library.filename;
                var newFilename = Path.Combine(Path.GetDirectoryName(filename),
                                               LibraryPrefix + library.linuxLibraryBasename +
                                               LIBRARY_EXTENSION);
                if (filename != newFilename) {
                    library.RenameAsset(newFilename);
                }
            }
        }

        /// <summary>
        /// Library prefix for the current version of Unity.
        /// </summary>
        private string LibraryPrefix {
            get {
                return GetUnityVersionMajorMinor() < 5.6f ? "" : "lib";
            }
        }
    }

    // Keys in the editor preferences which control the behavior of this
    // module.
    private const string PREFERENCE_ENABLED =
        "Google.VersionHandler.VersionHandlingEnabled";
    private const string PREFERENCE_CLEANUP_PROMPT_ENABLED =
        "Google.VersionHandler.CleanUpPromptEnabled";
    private const string PREFERENCE_RENAME_TO_CANONICAL_FILENAMES =
        "Google.VersionHandler.RenameToCanonicalFilenames";
    private const string PREFERENCE_VERBOSE_LOGGING_ENABLED =
        "Google.VersionHandler.VerboseLoggingEnabled";
    // List of preference keys, used to restore default settings.
    private static string[] PREFERENCE_KEYS = new [] {
        PREFERENCE_ENABLED,
        PREFERENCE_CLEANUP_PROMPT_ENABLED,
        PREFERENCE_RENAME_TO_CANONICAL_FILENAMES,
        PREFERENCE_VERBOSE_LOGGING_ENABLED
    };

    // Name of this plugin.
    private const string PLUGIN_NAME = "Google Version Handler";

    // Path of the file that contains methods to call when the version handler update process
    // is complete.
    private const string CALLBACKS_PATH = "Temp/VersionHandlerImplCallbacks";
    // Path of the file that indicates whether the asset database is currently being refreshed
    // due to this module.
    private const string REFRESH_PATH = "Temp/VersionHandlerImplRefresh";

    // Whether compilation is currently occuring.
    private static bool compiling = false;

    /// <summary>
    /// Enables / disables assets imported at multiple revisions / versions.
    /// In addition, this module will read text files matching _manifest_
    /// and remove files from older manifest files.
    /// </summary>
    static VersionHandlerImpl() {
        EditorApplication.update -= UpdateVersionedAssetsOnUpdate;
        EditorApplication.update += UpdateVersionedAssetsOnUpdate;
        Log("Loaded VersionHandlerImpl", verbose: true);
    }

    static void UpdateVersionedAssetsOnUpdate() {
        EditorApplication.update -= UpdateVersionedAssetsOnUpdate;
        UpdateVersionedAssets();
        EditorApplication.update += NotifyWhenCompliationComplete;
    }

    /// <summary>
    /// Indicates whether the asset database is being refreshed.
    /// </summary>
    private static bool Refreshing {
        get {
            return File.Exists(REFRESH_PATH);
        }

        set {
            bool refreshing = Refreshing;
            if (refreshing != value) {
                if (value) {
                    File.WriteAllText(REFRESH_PATH, "AssetDatabase Refreshing");
                } else {
                    File.Delete(REFRESH_PATH);
                }
            }
        }
    }

    /// <summary>
    /// Method polled from EditorApplication.update that waits until compilation is finished
    /// prior to calling NotifyUpdateCompleteMethods() if an asset database refresh was in
    /// progress.
    /// </summary>
    /// <remarks>
    /// The typical update flow when the asset database is refreshed looks like this:
    /// - Load VersionHandler DLL
    /// - UpdateVersionedAssets()
    /// - Asset database refreshed (Refreshing = true)
    ///   - If the database isn't refreshed NotifyUpdateCompleteMethods() is called immediately.
    /// - Compilation flag set on next editor update.
    /// - DLL is reloaded, compilation flag still set.
    /// - UpdateVersionedAssets(), no metadata changes or asset database refresh.
    /// - This method polls until compilation flag is false, if Refreshing is true
    ///   NotifyUpdateCompleteMethods() is called.
    /// </remarks>
    private static void NotifyWhenCompliationComplete() {
        if (EditorApplication.isCompiling) {
            if (!compiling) {
                Log("Compiling...", verbose: true);
            }
            compiling = true;
            return;
        }
        if (compiling) {
            Log("Compilation complete.", verbose: true);
            compiling = false;
        }
        EditorApplication.update -= NotifyWhenCompliationComplete;
        // If a refresh was initiated by this module, clear the refresh flag.
        var wasRefreshing = Refreshing;
        Refreshing = false;
        if (wasRefreshing) NotifyUpdateCompleteMethods();
    }

    /// <summary>
    /// Call all methods referenced by the UpdateCompleteMethods property.
    /// </summary>
    private static void NotifyUpdateCompleteMethods() {
        foreach (var method in UpdateCompleteMethods) {
            var tokens = method.Split(new [] {':'});
            if (tokens.Length == 3) {
                try {
                    VersionHandler.InvokeStaticMethod(
                        VersionHandler.FindClass(tokens[0], tokens[1]), tokens[2], null);
                } catch (Exception e) {
                    Log(String.Format(
                        "Failed to call VersionHandler complete method '{0}'.\n" +
                        "{1}\n", method, e.ToString()), level: LogLevel.Error);
                }
            } else {
                Log(String.Format("Unable to call VersionHandler complete method '{0}'.\n" +
                                  "This string should use the format\n" +
                                  "'assemblyname:classname:methodname'",
                                  method), level: LogLevel.Error);
            }
        }
    }

    /// <summary>
    /// Reset settings to default values.
    /// </summary>
    /// <param name="preferenceKeys">List of preferences that should be reset / deleted.</param>
    internal static void RestoreDefaultSettings(IEnumerable<string> preferenceKeys) {
        foreach (var key in preferenceKeys) {
            if (EditorPrefs.HasKey(key)) EditorPrefs.DeleteKey(key);
        }
    }

    /// <summary>
    /// Reset settings of this plugin to default values.
    /// </summary>
    internal static void RestoreDefaultSettings() {
        RestoreDefaultSettings(PREFERENCE_KEYS);
    }

    /// <summary>
    /// Enable / disable automated version handling.
    /// </summary>
    public static bool Enabled {
        get {
            return !System.Environment.CommandLine.Contains("-gvh_disable") &&
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
    /// Enable / disable renaming to canonical filenames.
    /// </summary>
    public static bool RenameToCanonicalFilenames {
        get { return EditorPrefs.GetBool(PREFERENCE_RENAME_TO_CANONICAL_FILENAMES,
                                         defaultValue: false); }
        set { EditorPrefs.SetBool(PREFERENCE_RENAME_TO_CANONICAL_FILENAMES, value); }
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
    /// Set the methods to call when VersionHandler completes an update.
    /// Each string in the specified list should have the format
    /// "assemblyname:classname:methodname".
    /// assemblyname can be empty to search all assemblies for classname.
    /// For example:
    /// ":MyClass:MyMethod"
    /// Would call MyClass.MyMethod() when the update process is complete.
    /// </summary>
    public static IEnumerable<string> UpdateCompleteMethods {
        get {
            var methods = new HashSet<string>();
            var callbacks_data =
                File.Exists(CALLBACKS_PATH) ? File.ReadAllText(CALLBACKS_PATH) : "";
            foreach (var callback in callbacks_data.Split(new [] { '\n' })) {
                var trimmedCallback = callback.Trim();
                if (!String.IsNullOrEmpty(trimmedCallback)) {
                    methods.Add(trimmedCallback);
                }
            }
            return methods;
        }

        set {
            File.WriteAllText(
                CALLBACKS_PATH,
                value == null ? "" : String.Join("\n", new List<string>(value).ToArray()));
        }
    }

    /// <summary>
    /// Log severity.
    /// </summary>
    internal enum LogLevel {
        Info,
        Warning,
        Error,
    };

    /// <summary>
    /// Whether to also log to a file in the project.
    /// </summary>
    internal static bool LogToFile { get; set; }

    /// <summary>
    /// Write a message to the log file.
    /// </summary>
    /// <param name="message">Message to log.</param>
    internal static void WriteToLogFile(string message) {
        if (LogToFile) {
            using (var file = new StreamWriter("VersionHandlerSave.log", true)) {
                file.WriteLine(message);
            }
        }
    }

    /// <summary>
    /// Log a message.
    /// </summary>
    /// <param name="message">Message to log.</param>
    /// <param name="verbose">Whether the message should only be displayed if verbose logging is
    /// enabled.</param>
    /// <param name="level">Severity of the message.</param>
    internal static void Log(string message, bool verbose = false,
                             LogLevel level = LogLevel.Info) {
        if (!verbose || VerboseLoggingEnabled) {
            switch (level) {
                case LogLevel.Info:
                    Debug.Log(message);
                    WriteToLogFile(message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    WriteToLogFile("WARNING: " + message);
                    break;
                case LogLevel.Error:
                    Debug.LogError(message);
                    WriteToLogFile("ERROR: " + message);
                    break;
            }
        }
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
                                               VersionHandler.FilenameFilter filter = null) {
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
        Log("Moved obsolete file to trash: " + filename, verbose: true);
        if (!AssetDatabase.MoveAssetToTrash(filename)) {
            Log("Failed to move obsolete file to trash: " + filename,
                level: LogLevel.Error);
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

        var metadataSet = FileMetadataSet.ParseFromFilenames(FindAllAssets());
        // Rename linux libraries, if any are being tracked.
        var linuxLibraries = new LinuxLibraryRenamer(metadataSet);
        linuxLibraries.RenameLibraries();

        if (!forceUpdate) {
            metadataSet = FileMetadataSet.FindWithPendingUpdates(metadataSet);
        }
        if (metadataSet.EnableMostRecentPlugins(forceUpdate)) {
            AssetDatabase.Refresh();
            Refreshing = true;
        }

        var obsoleteFiles = new ObsoleteFiles(
            ManifestReferences.FindAndReadManifests(metadataSet), metadataSet);

        // Obsolete files that are no longer reference can be safely
        // deleted, prompt the user for confirmation if they have the option
        // enabled.
        bool deleteFiles = true;
        if (obsoleteFiles.unreferenced.Count > 0) {
            if (CleanUpPromptEnabled && deleteFiles &&
                obsoleteFiles.unreferencedExcludingManifests.Count > 0) {
                deleteFiles = EditorUtility.DisplayDialog(
                    PLUGIN_NAME,
                    "Would you like to delete the following obsolete files " +
                    "in your project?\n\n" +
                    String.Join("\n", new List<string>(
                                        obsoleteFiles.unreferencedExcludingManifests).ToArray()),
                    "Yes", cancel: "No");
            }
            foreach (var filename in obsoleteFiles.unreferenced) {
                if (deleteFiles) {
                    MoveAssetToTrash(filename);
                } else {
                    Log("Leaving obsolete file: " + filename, verbose: true);
                }
            }
        }

        // If any obsolete referenced files are present, prompt the user for
        // confirmation of deletion.
        if (obsoleteFiles.referenced.Count > 0) {
            List<string> referencesString = new List<string>();
            foreach (var item in obsoleteFiles.referencedExcludingManifests) {
                List<string> lines = new List<string>();
                foreach (var reference in item.Value) {
                    lines.Add(String.Format("{0}: {1}", reference, item.Key));
                }
                referencesString.Add(String.Join("\n", lines.ToArray()));
            }
            deleteFiles = obsoleteFiles.referencedExcludingManifests.Values.Count == 0 ||
                EditorUtility.DisplayDialog(
                   PLUGIN_NAME,
                   "The following obsolete files are referenced by packages in " +
                   "your project, would you like to delete them?\n\n" +
                   String.Join("\n", referencesString.ToArray()),
                   "Yes", cancel: "No");
            foreach (var item in obsoleteFiles.referenced) {
                if (deleteFiles) {
                    MoveAssetToTrash(item.Key);
                } else {
                    Log("Leaving obsolete file: " + item.Key + " | " + "Referenced by (" +
                        String.Join(", ", item.Value.ToArray())  + ")", verbose: true);
                }
            }
        }

        if (!Refreshing) {
            // If for some reason, another module caused compilation to occur then we'll postpone
            // notification until it's complete.
            if (EditorApplication.isCompiling) {
                EditorApplication.update += NotifyWhenCompliationComplete;
            } else {
                // If we're in a quiescent state, notify the update complete methods.
                NotifyUpdateCompleteMethods();
            }
        }
    }

    // Returns the major/minor version of the unity environment we are running in
    // as a float so it can be compared numerically.
    public static float GetUnityVersionMajorMinor() {
        float result = 5.4f;
        string version = Application.unityVersion;
        if (!string.IsNullOrEmpty(version)) {
            int dotIndex = version.IndexOf('.');
            if (dotIndex > 0 && version.Length > dotIndex + 1) {
                if (!float.TryParse(version.Substring(0, dotIndex + 2), NumberStyles.Any,
                                    CultureInfo.InvariantCulture, out result)) {
                    result = 5.4f;
                }
            }
        }
        return result;
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
    /// Update versioned assets when assets are saved.
    /// </summary>
    [InitializeOnLoad]
    internal class DotNetVersionUpdater : UnityEditor.AssetModificationProcessor {
        // Currently selected .NET framework version.
        private static string currentDotNetVersion = FileMetadata.DOTNET_RUNTIME_VERSION_LEGACY;

        // Save the currently selected .NET framework version so that we can apply targeting
        // only when the .NET version is changed.
        static DotNetVersionUpdater() {
            currentDotNetVersion = FileMetadata.ScriptingRuntimeDotNetVersion;
            Log(String.Format("Detected .NET version {0}", currentDotNetVersion), verbose: true);
        }

        /// <summary>
        /// Update versioned assets when assets are saved.
        /// </summary>
        /// When the user changes the .NET framework version in Unity 2017 or beyond, the player
        /// settings are saved and the editor is restarted.  This method hooks the option change
        /// so that it's possible to select the correct set of assemblies (C# DLLs) that target
        /// the selected .NET version.
        internal static string[] OnWillSaveAssets(string[] paths) {
            var newDotNetVersion = FileMetadata.ScriptingRuntimeDotNetVersion;
            if (currentDotNetVersion != newDotNetVersion) {
                bool logToFilePrevious = LogToFile;
                // Enable logging to a file as all logs on shutdown do not end up in Unity's log
                // file.
                LogToFile = VerboseLoggingEnabled;
                Log(String.Format(".NET framework version changed from {0} to {1}\n",
                                  currentDotNetVersion, newDotNetVersion));
                currentDotNetVersion = FileMetadata.ScriptingRuntimeDotNetVersion;
                UpdateVersionedAssets(forceUpdate: true);
                LogToFile = logToFilePrevious;
            }
            return paths;
        }
    }
}

} // namespace Google

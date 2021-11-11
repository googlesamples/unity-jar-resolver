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
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System;

namespace Google {

[InitializeOnLoad]
public class VersionHandlerImpl : AssetPostprocessor {
    /// <summary>
    /// A unique class to create the multi-select window to obsolete files.
    /// </summary>
    private class ObsoleteFilesWindow : MultiSelectWindow {}

    /// <summary>
    /// Derives metadata from an asset filename.
    /// </summary>
    public class FileMetadata {
        // Splits a filename into components.
        private class FilenameComponents
        {
            // Known multi-component extensions to strip.
            private static readonly string[] MULTI_COMPONENT_EXTENSIONS = new [] { ".dll.mdb" };

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
                // Strip known multi-component extensions from the filename.
                extension = null;
                foreach (var knownExtension in MULTI_COMPONENT_EXTENSIONS) {
                    if (basename.ToLower().EndsWith(knownExtension)) extension = knownExtension;
                }
                if (String.IsNullOrEmpty(extension)) extension = Path.GetExtension(basename);
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

        public static string[] TOKEN_TARGETS = new [] { "targets-", "t" };
        // Prefix which identifies the version metadata in the filename or
        // asset label.
        public static string[] TOKEN_VERSION = new [] { "version-", "v" };
        // Prefix which identifies the .NET version metadata in the filename
        // or asset label.
        public static string[] TOKEN_DOTNET_TARGETS = new [] { "dotnet-" };
        // Prefix which indicates this file is a package manifest.
        public static string[] TOKEN_MANIFEST = new [] { "manifest" };
        // Prefix which allows a manifest to specify a human readable package name.
        // If the name begins with a digit [0-9] the numeric letters at the start of the string
        // are used to order the priority of the package alias.
        // For example, given the names:
        // - "0current name"
        // - "1old name"
        // - "another alias"
        // will create the list of names ["current name", "old name", "another alias"] where
        // "current name" is the current display name of the package.
        public static string[] TOKEN_MANIFEST_NAME = new[] { "manifestname-"};
        // Prefix which identifies the canonical name of this Linux library.
        public static string[] TOKEN_LINUX_LIBRARY_BASENAME = new [] { "linuxlibname-" };
        // Prefix which identifies the original path of a file when the package was exported.
        public static string[] TOKEN_EXPORT_PATH = new [] { "exportpath-" };

        // Delimiter for version numbers.
        private static char[] VERSION_DELIMITER = new char[] { '.' };
        // Maximum number of components parsed from a version number.
        private static int MAX_VERSION_COMPONENTS = 4;
        // Multiplier applied to each component of the version number,
        // see CalculateVersion().
        private static long VERSION_COMPONENT_MULTIPLIER = 1000;
        // Prefix for labels which encode metadata of an asset.
        private static string LABEL_PREFIX = "gvh_";
        // Prefix for labels which encode metadata of the asset for 1.2.138 and above.
        // These labels are never removed by the version handler.
        private static string LABEL_PREFIX_PRESERVE = "gvhp_";
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

        /// <summary>
        /// Label which flags whether an asset should be disabled by renaming the file
        /// (instead of using the PluginManager).
        /// </summary>
        public static string ASSET_LABEL_RENAME_TO_DISABLE = "gvh_rename_to_disable";

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

        // Regular expression which matches valid version strings.
        private static Regex VERSION_REGEX = new Regex("^[0-9][0-9.]+$");
        // Regular expression which matches valid .NET framework versions.
        private static Regex DOTNET_RUNTIME_REGEX =
            new Regex("^(" + String.Join("|", (new List<string> {
                        DOTNET_RUNTIME_VERSION_LEGACY,
                        DOTNET_RUNTIME_VERSION_LATEST,
                        SCRIPTING_RUNTIME_LEGACY }).ToArray()) + ")$",
                RegexOptions.IgnoreCase);
        // Regular expression which matches valid
        private static Regex BUILD_TARGET_REGEX =
            new Regex("^(editor|" + String.Join(
                        "|", (new List<string>(BUILD_TARGET_NAME_TO_ENUM_NAME.Keys)).ToArray()) +
                      ")$", RegexOptions.IgnoreCase);
        // Regular expression which matches an index in a manifest name field.
        private static Regex MANIFEST_NAME_REGEX = new Regex("^([0-9]+)(.*)");

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
        // We need to maintain a separate blacklist as Unity occasionally
        // removes BuildTarget display names but does not remove the enumeration
        // values associated with the names.  This causes a fatal error in
        // PluginImporter.GetCompatibleWithPlatform() when provided with a
        // BuildTarget that no longer has a display name.
        static HashSet<BuildTarget> GetBlackList() {
            if (targetBlackList == null) {
                targetBlackList = new HashSet<BuildTarget>();
                if (VersionHandlerImpl.GetUnityVersionMajorMinor() >= 5.5) {
#pragma warning disable 618
                    targetBlackList.Add(BuildTarget.PS3);
                    targetBlackList.Add(BuildTarget.XBOX360);
#pragma warning restore 618
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
        public HashSet<string> targets = null;

        /// <summary>
        /// Set if this references an asset manifest.
        /// </summary>
        public bool isManifest = false;

        /// <summary>
        /// Offset subtracted from entries inserted in customManifestNames.
        /// </summary>
        private const int CUSTOM_MANIFEST_NAMES_FIRST_INDEX_OFFSET = Int32.MaxValue / 2;

        /// <summary>
        /// Backing store for aliases of the manifest name, the current package name is always
        /// first in the set. This contains only values parsed from manifestname labels,
        /// the ManifestName property is used to retrieve the preferred manifest name.
        /// </summary>
        public SortedList<int, string> customManifestNames = new SortedList<int, string>();

        /// <summary>
        /// Set if this references an asset which is handled by PluginManager.
        /// </summary>
        public bool isHandledByPluginImporter = false;

        /// <summary>
        /// List of compatible .NET versions parsed from this asset.
        /// </summary>
        public HashSet<string> dotNetTargets = null;

        /// <summary>
        /// Basename of a Linux library plugin.
        /// </summary>
        public string linuxLibraryBasename = null;

        /// <summary>
        /// Path of the file when it was originally exported as a package.
        /// </summary>
        public string exportPath = "";

        /// <summary>
        /// Path of the file when it was originally exported as a package in the project.
        /// </summary>
        public string ExportPathInProject {
            get {
                var exportPathInProject = exportPath;
                if (!String.IsNullOrEmpty(exportPathInProject)) {
                    // Remove the assets folder, if specified.
                    if (FileUtils.IsUnderDirectory(exportPathInProject, FileUtils.ASSETS_FOLDER)) {
                        exportPathInProject =
                            exportPath.Substring(FileUtils.ASSETS_FOLDER.Length + 1);
                    }
                    // Determine whether this package is installed as a package or asset to
                    // determine the root directory.
                    var packageDirectory = FileUtils.GetPackageDirectory(filename);
                    var installRoot = !String.IsNullOrEmpty(packageDirectory) ?
                        packageDirectory : FileUtils.ASSETS_FOLDER;
                    // Translate exportPath into a package relative path.
                    exportPathInProject = FileUtils.PosixPathSeparators(
                        Path.Combine(installRoot, exportPathInProject));
                }
                return exportPathInProject;
            }
        }

        /// <summary>
        /// If this is a manifest, get the display name.
        /// </summary>
        /// <returns>If this file is a  manifest, returns the display name of the manifest,
        /// null otherwise.</returns>
        public string ManifestName {
            get {
                string name = null;
                bool hasManifestNames = customManifestNames != null &&
                    customManifestNames.Count > 0;
                if (isManifest || hasManifestNames) {
                    if (hasManifestNames) {
                        name = customManifestNames.Values[0];
                    } else {
                        name = (new FilenameComponents(filenameCanonical)).basenameNoExtension;
                    }
                }
                return name;
            }
        }

        /// <summary>
        /// Whether it's possible to change the asset metadata.
        /// </summary>
        /// <returns>true if the asset metadata for this file is read-only,
        /// false otherwise.</returns>
        public bool IsReadOnly {
            get {
                return FileUtils.IsUnderPackageDirectory(filename);
            }
        }

        /// <summary>
        /// Parse metadata from filename and store in this class.
        /// </summary>
        /// <param name="filename">Name of the file to parse.</param>
        public FileMetadata(string filename) {
            this.filename = FileUtils.NormalizePathSeparators(filename);
            filenameCanonical = ParseMetadataFromFilename(this.filename);
            ParseMetadataFromAssetLabels();

            // If the export path was specified, override the canonical filename.
            var exportPathInProject = ExportPathInProject;
            if (!String.IsNullOrEmpty(exportPathInProject)) {
                filenameCanonical = ParseMetadataFromFilename(exportPathInProject);
            }
            UpdateAssetLabels();
        }

        /// <summary>
        /// Parse metadata from the specified filename and store in this class.
        /// </summary>
        /// <param name="filenameToParse">Parse metadata from the specified filename.</param>
        /// <returns>Filename with metadata removed.</returns>
        private string ParseMetadataFromFilename(string filenameToParse) {
            var filenameComponents = new FilenameComponents(filenameToParse);
            // Parse metadata from the filename.
            string[] tokens =
                filenameComponents.basenameNoExtension.Split(
                    FILENAME_TOKEN_SEPARATOR);
            if (tokens.Length > 1) {
                var basenameNoExtension = tokens[0];
                for (int i = 1; i < tokens.Length; ++i) {
                    if (!ParseToken(tokens[i])) {
                        basenameNoExtension += FILENAME_TOKEN_SEPARATOR[0] + tokens[i];
                    }
                }
                filenameComponents.basenameNoExtension = basenameNoExtension;
            }
            // On Windows the AssetDatabase converts native path separators
            // used by the .NET framework '\' to *nix style '/' such that
            // System.IO.Path generated paths will not match those looked up
            // in the asset database.  So we convert the output of Path.Combine
            // here to use *nix style paths so that it's possible to perform
            // simple string comparisons to check for path equality.
            return FileUtils.NormalizePathSeparators(Path.Combine(
                filenameComponents.directory,
                filenameComponents.basenameNoExtension +
                filenameComponents.extension));
        }

        /// <summary>
        /// Parse metadata from asset labels.
        /// </summary>
        public void ParseMetadataFromAssetLabels() {
            // Parse metadata from asset labels if it hasn't been specified in
            // the filename.
            AssetImporter importer = GetAssetImporter();
            if (importer != null) {
                foreach (string label in AssetDatabase.GetLabels(importer)) {
                    ParseLabel(label);
                }
                isHandledByPluginImporter = typeof(PluginImporter).IsInstanceOfType(importer);
            }
        }

        /// <summary>
        /// Determine whether a token matches a set of prefixes extracting the value(s) if it
        /// does.
        /// </summary>
        /// <param name="token">Token to parse.</param>
        /// <param name="prefixes">Set of prefixes to compare with the token.</param>
        /// <param name="prefix">Added to each item in prefixes before comparing with the token.
        /// </param>
        /// <returns>Array of values if the token matches the prefixes, null otherwise.</returns>
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
        /// Determine whether a list of string match a regular expression.
        /// </summary>
        /// <param name="items">Items to compare against the specific regular expression.</param>
        /// <param name="regEx">Regular expression to compare against each item.</param>
        /// <returns>true if all items in the list match the regular expression, false otherwise.
        /// </returns>
        private bool StringListMatchesRegex(IEnumerable<string> items, Regex regEx) {
            foreach (var item in items) {
                if (!regEx.Match(item).Success) return false;
            }
            return true;
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
            var values = MatchPrefixesGetValues(token, TOKEN_MANIFEST_NAME, prefix);
            if (values != null) {
                var name = String.Join(FIELD_SEPARATOR[0].ToString(), values);
                var nameMatch = MANIFEST_NAME_REGEX.Match(name);
                int order = CUSTOM_MANIFEST_NAMES_FIRST_INDEX_OFFSET - customManifestNames.Count;
                if (nameMatch.Success) {
                    int parsedOrder;
                    if (Int32.TryParse(nameMatch.Groups[1].Value, out parsedOrder)) {
                        order = parsedOrder - CUSTOM_MANIFEST_NAMES_FIRST_INDEX_OFFSET;
                    }
                    name = nameMatch.Groups[2].Value;
                }
                customManifestNames.Remove(order);
                customManifestNames.Add(order, name);
                isManifest = true;
                return true;
            }
            values = MatchPrefixesGetValues(token, TOKEN_MANIFEST, prefix);
            if (values != null) {
                isManifest = true;
                return true;
            }
            values = MatchPrefixesGetValues(token, TOKEN_DOTNET_TARGETS, prefix);
            if (values != null && StringListMatchesRegex(values, DOTNET_RUNTIME_REGEX)) {
                if(dotNetTargets == null) {
                    dotNetTargets = new HashSet<string>();
                }
                dotNetTargets.UnionWith(values);
                return true;
            }
            values = MatchPrefixesGetValues(token, TOKEN_TARGETS, prefix);
            if (values != null && StringListMatchesRegex(values, BUILD_TARGET_REGEX)) {
                if (targets == null) {
                    targets = new HashSet<string>();
                }
                // Convert all target names to lower case.
                foreach (var value in values) {
                    targets.Add(value.ToLower());
                }
                return true;
            }
            values = MatchPrefixesGetValues(token, TOKEN_VERSION, prefix);
            if (values != null && StringListMatchesRegex(values, VERSION_REGEX)) {
                if (values.Length > 0) {
                    versionString = values[0];
                    return true;
                }
            }
            values = MatchPrefixesGetValues(token, TOKEN_LINUX_LIBRARY_BASENAME, prefix);
            if (values != null) {
                linuxLibraryBasename = String.Join(FIELD_SEPARATOR[0].ToString(), values);
                return true;
            }
            values = MatchPrefixesGetValues(token, TOKEN_EXPORT_PATH, prefix);
            if (values != null) {
                exportPath = FileUtils.PosixPathSeparators(
                                 String.Join(FIELD_SEPARATOR[0].ToString(), values));
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
            return ParseToken(label, prefix: LABEL_PREFIX_PRESERVE) ||
                ParseToken(label, prefix: LABEL_PREFIX);
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
        /// Create an array of asset labels from a prefix and set of values.
        /// </summary>
        /// <param name="fieldPrefixes">The first item of this list is used as the prefix.
        /// </param>
        /// <param name="values">Set of values to store with the field.</param>
        /// <param name="currentLabels">Labels to search for the suitable prefix.</param>
        /// <param name="preserve">Always preserve these labels.</param>
        private static string[] CreateLabels(string[] fieldPrefixes, IEnumerable<string> values,
                                             HashSet<string> currentLabels, bool preserve = false) {
            string prefix = fieldPrefixes[0];
            List<string> labels = new List<string>();
            foreach (var value in values) {
                labels.Add(CreateLabel(prefix, value, currentLabels, preserve: preserve));
            }

            return labels.ToArray();
        }

        /// <summary>
        /// Create an asset label from a prefix and a single value
        /// </summary>
        /// <param name="prefix"> The field prefix to be applied to the label.
        /// </param>
        /// <param name="value">The value to store in the field</param>
        /// <param name="preserve">Whether the label should be preserved.</param>
        public static string CreateLabel(string prefix, string value, bool preserve = false) {
            return (preserve ? LABEL_PREFIX_PRESERVE : LABEL_PREFIX) + prefix + value;
        }

        /// <summary>
        /// Create an asset label keeping the preservation in the supplied set if it already exists.
        /// </summary>
        /// <param name="prefix"> The field prefix to be applied to the label.
        /// </param>
        /// <param name="value">The value to store in the field</param>
        /// <param name="currentLabels">Labels to search for the suitable prefix.</param>
        /// <param name="preserve">Whether the label should be preserved.</param>
        public static string CreateLabel(string prefix, string value, HashSet<string> currentLabels,
                                         bool preserve = false) {
            var legacyLabel = CreateLabel(prefix, value, false);
            var preservedLabel = CreateLabel(prefix, value, true);
            if (currentLabels.Contains(legacyLabel)) return legacyLabel;
            if (currentLabels.Contains(preservedLabel)) return preservedLabel;
            return preserve ? preservedLabel : legacyLabel;
        }

        /// <summary>
        /// Determine whether this file is compatible with the editor.
        /// This is a special case as the editor isn't a "platform" covered
        /// by UnityEditor.BuildTarget.
        /// </summary>
        /// <returns>true if this file targets the editor, false
        /// otherwise.</returns>
        public bool GetEditorEnabled() {
            return targets != null && targets.Contains("editor");
        }

        /// <summary>
        /// Determine whether any build targets have been specified.
        /// </summary>
        /// <returns>true if targets are specified, false otherwise.</returns>
        public bool GetBuildTargetsSpecified() {
            return targets != null && targets.Count > 0;
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
        /// Get the set of build targets as strings.
        /// </summary>
        /// <returns>Set of build targets as strings.</returns>
        public HashSet<string> GetBuildTargetStrings() {
            var buildTargetStringsSet = new HashSet<string>();
            foreach (var buildTarget in GetBuildTargets()) {
                buildTargetStringsSet.Add(buildTarget.ToString());
            }
            return buildTargetStringsSet;
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
            if (String.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(filename)) || IsReadOnly) {
                return;
            }
            AssetImporter importer = AssetImporter.GetAtPath(filename);
            var labels = new HashSet<string>();
            var currentLabels = new HashSet<string>();
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
                labels.Add(CreateLabel(TOKEN_VERSION[0], versionString, currentLabels));
            }
            if (targets != null && targets.Count > 0) {
                labels.UnionWith(CreateLabels(TOKEN_TARGETS, targets, currentLabels));

                if (!isHandledByPluginImporter) {
                    labels.Add(ASSET_LABEL_RENAME_TO_DISABLE);
                }
            }
            if (dotNetTargets != null && dotNetTargets.Count > 0) {
                labels.UnionWith(CreateLabels(TOKEN_DOTNET_TARGETS, dotNetTargets, currentLabels));
            }
            if (!String.IsNullOrEmpty(linuxLibraryBasename)) {
                labels.Add(CreateLabel(TOKEN_LINUX_LIBRARY_BASENAME[0], linuxLibraryBasename,
                                       currentLabels));
            }
            if (!String.IsNullOrEmpty(exportPath)) {
                labels.Add(CreateLabel(TOKEN_EXPORT_PATH[0], exportPath, currentLabels,
                                       preserve: true));
            }
            if (isManifest) {
                labels.Add(CreateLabel(TOKEN_MANIFEST[0], null, currentLabels));
            }
            if (customManifestNames != null && customManifestNames.Count > 0) {
                foreach (var indexAndName in customManifestNames) {
                    int order = indexAndName.Key + CUSTOM_MANIFEST_NAMES_FIRST_INDEX_OFFSET;
                    var name = indexAndName.Value;
                    if (order < CUSTOM_MANIFEST_NAMES_FIRST_INDEX_OFFSET) {
                        labels.Add(CreateLabel(TOKEN_MANIFEST_NAME[0], order.ToString() + name,
                                               currentLabels, preserve: true));
                    } else {
                        labels.Add(CreateLabel(TOKEN_MANIFEST_NAME[0], name, currentLabels,
                                               preserve: true));
                    }
                }
            }
            if (!labels.SetEquals(currentLabels)) {
                var sortedLabels = new List<string>(labels);
                var sortedCurrentLabels = new List<string>(currentLabels);
                sortedCurrentLabels.Sort();
                sortedLabels.Sort();
                var labelsArray = sortedLabels.ToArray();
                Log(String.Format("Changing labels of {0}\n" +
                                  "from: {1}\n" +
                                  "to:   {2}\n",
                                  filename,
                                  String.Join(", ", sortedCurrentLabels.ToArray()),
                                  String.Join(", ", labelsArray)),
                    verbose: true);
                AssetDatabase.SetLabels(importer, labelsArray);
            }
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
            // If the source and target filenames are the same, there is nothing to do.
            newFilename = FileUtils.NormalizePathSeparators(newFilename);
            if (FileUtils.NormalizePathSeparators(filename) == newFilename) {
                return true;
            }
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
                var targetDir = Path.GetDirectoryName(filename);
                if (!String.IsNullOrEmpty(targetDir)) {
                    Directory.CreateDirectory(targetDir);
                }
                // This is *really* slow.
                string error = AssetDatabase.MoveAsset(filename, newFilename);
                if (!String.IsNullOrEmpty(error)) {
                    string renameError = AssetDatabase.RenameAsset(
                        filename, filenameComponents.basenameNoExtension);
                    error = String.IsNullOrEmpty(renameError) ?
                        renameError : String.Format("{0}, {1}", error, renameError);
                }
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
        /// <param name="versionString">Version string to parse.</param>
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
        /// <param name="versionNumber">Numeric version number.</param>
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
        /// Backing store for EnabledEditorDlls.
        /// </summary>
        private HashSet<string> enabledEditorDlls = new HashSet<string>();

        /// <summary>
        /// Full path of DLLs that should be loaded into the editor application domain.
        /// </summary>
        public ICollection<string> EnabledEditorDlls { get { return enabledEditorDlls; } }

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
            Debug.Assert(
                filenameCanonical == null ||
                metadata.filenameCanonical.Equals(filenameCanonical));
            metadataByVersion[metadata.CalculateVersion()] = metadata;
        }

        /// <summary>
        /// If this instance references a set of plugins, enable the most
        /// recent versions.
        /// </summary>
        /// <returns>true if any plugin metadata was modified and requires an
        /// AssetDatabase.Refresh(), false otherwise.</returns>
        public bool EnableMostRecentPlugins() {
            return EnableMostRecentPlugins(new HashSet<string>());
        }

        /// <summary>
        /// If this instance references a set of plugins, enable the most
        /// recent versions.
        /// </summary>
        /// <param name="disableFiles">Set of files in the project that should be disabled.</param>
        /// <returns>true if any plugin metadata was modified and requires an
        /// AssetDatabase.Refresh(), false otherwise.</returns>
        public bool EnableMostRecentPlugins(HashSet<string> disableFiles) {
            bool modified = false;
            int versionIndex = 0;
            int numberOfVersions = metadataByVersion.Count;
            var disabledVersions = new List<string>();
            string enabledVersion = null;
            enabledEditorDlls = new HashSet<string>();

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
                if (pluginImporter == null) {
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
                bool obsoleteVersion =
                    (numberOfVersions > 1 && versionIndex < numberOfVersions) ||
                    disableFiles.Contains(metadata.filename);
                // If this is an obsolete version.
                if (obsoleteVersion) {
                    // Disable for all platforms and the editor.
                    editorEnabled = false;
                    selectedTargets = new HashSet<BuildTarget>();
                    Log(String.Format("{0} is obsolete and will be disabled.", metadata.filename),
                        verbose: true);
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
                    if (metadata.filename.ToLower().EndsWith(".dll")) {
                        enabledEditorDlls.Add(Path.GetFullPath(metadata.filename));
                    }
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
                    Log(String.Format("Metadata changed: force import of {0}", metadata.filename),
                        verbose: true);
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
        /// Dictionary of FileMetadataByVersion indexed by filename with
        /// metadata stripped.
        /// </summary>
        /// <remarks>
        /// This shouldn't not be modified directly, use Add() and Clear() instead.
        /// </remarks>
        private Dictionary<string, FileMetadataByVersion>
            metadataByCanonicalFilename =
                new Dictionary<string, FileMetadataByVersion>();

        /// <summary>
        /// Dictionary of FileMetadata indexed by filename in the project and the export path if
        /// it exists. This is the inverse mapping of metadataByCanonicalFilename.
        /// </summary>
        /// <remarks>
        /// This shouldn't not be modified directly, use Add() and Clear() instead.
        /// </remarks>
        private Dictionary<string, FileMetadata> metadataByFilename =
            new Dictionary<string, FileMetadata>();

        /// <summary>
        /// Get the FileMetadataByVersion for each filename bucket in this set.
        /// </summary>
        public Dictionary<string, FileMetadataByVersion>.ValueCollection
                Values {
            get { return metadataByCanonicalFilename.Values; }
        }

        /// <summary>
        /// Retrieves a map of in-project filename to file metadata.
        /// </summary>
        public Dictionary<string, FileMetadata> MetadataByFilename {
            get { return metadataByFilename; }
        }

        /// <summary>
        /// Backing store for EnabledEditorDlls.
        /// </summary>
        private HashSet<string> enabledEditorDlls = new HashSet<string>();

        /// <summary>
        /// Full path of DLLs that should be loaded into the editor application domain.
        /// </summary>
        public ICollection<string> EnabledEditorDlls { get { return enabledEditorDlls; } }

        /// <summary>
        /// Construct an instance.
        /// </summary>
        public FileMetadataSet() { }

        /// <summary>
        /// Filter all files that can't be modified.
        /// </summary>
        /// <param name="metadataSet">Metadata set to filter.</param>
        /// <returns>New FileMetadata with files that can't be modified removed.</returns>
        public static FileMetadataSet FilterOutReadOnlyFiles(FileMetadataSet metadataSet) {
            var filteredMetadata = new FileMetadataSet();
            foreach (var metadataByVersion in metadataSet.Values) {
                foreach (var metadata in metadataByVersion.Values) {
                    if (!metadata.IsReadOnly) filteredMetadata.Add(metadata);
                }
            }
            return filteredMetadata;
        }

        /// <summary>
        /// Empty the set.
        /// </summary>
        public void Clear() {
            metadataByCanonicalFilename = new Dictionary<string, FileMetadataByVersion>();
            metadataByFilename = new Dictionary<string, FileMetadata>();
            enabledEditorDlls = new HashSet<string>();
        }

        /// <summary>
        /// Add file metadata to the set.
        /// </summary>
        /// <param name="metadata">File metadata to add to the set.</param>
        public void Add(FileMetadata metadata) {
            Add(metadata.filenameCanonical, metadata);
        }

        /// <summary>
        /// Add file metadata to the set.
        /// </summary>
        /// <param name="filenameCanonical">File to associate the metadata with.</param>
        /// <param name="metadata">File metadata to add to the set.</param>
        public void Add(string filenameCanonical, FileMetadata metadata) {
            FindMetadataByVersion(filenameCanonical, true).Add(metadata);
            UpdateMetadataByFilename(metadata);
        }

        /// <summary>
        /// Add a set of files to the specified set.
        /// </summary>
        /// <param name="filenameCanonical">File to associate the metadata with.</param>
        /// <param name="metadataByVersion">Set of files to add to the set.</param>
        public void Add(string filenameCanonical, FileMetadataByVersion metadataByVersion) {
            var existingMetadataByVersion = FindMetadataByVersion(filenameCanonical, true);
            foreach (var metadata in metadataByVersion.Values) {
                existingMetadataByVersion.Add(metadata);
                UpdateMetadataByFilename(metadata);
            }
        }

        /// <summary>
        /// Update the mapping of filename to metadata.
        /// </summary>
        /// <param name="metadata">File metadata to add to the set.</param>
        private void UpdateMetadataByFilename(FileMetadata metadata) {
            metadataByFilename[metadata.filename] = metadata;
            if (!String.IsNullOrEmpty(metadata.ExportPathInProject)) {
                metadataByFilename[metadata.ExportPathInProject] = metadata;
            }
        }

        /// <summary>
        /// Search for metadata for an existing file given a canonical filename
        /// and version.
        /// </summary>
        /// <param name="filenameCanonical">Name of the file set to search
        /// for.</param>
        /// <param name="version">Version number of the file in the set or 0 to find the
        /// most recent file.</param>
        /// <returns>Reference to the metadata if successful, null otherwise.</returns>
        public FileMetadata FindMetadata(string filenameCanonical,
                                         long version) {
            FileMetadata metadata;
            if (metadataByFilename.TryGetValue(filenameCanonical, out metadata)) {
                return metadata;
            }
            var metadataByVersion = FindMetadataByVersion(filenameCanonical, false);
            if (metadataByVersion != null) {
                metadata = version > 0 ? metadataByVersion[version] :
                    metadataByVersion.MostRecentVersion;
                if (metadata != null) return metadata;
            }
            return null;
        }

        /// <summary>
        /// Get metadata by version for a given canonical filename.
        /// </summary>
        /// <param name="filenameCanonical">Name of the file set to search for.</param>
        /// <param name="addEntry">Whether to add an entry if the metadata isn't found.</param>
        /// <returns>Reference to the metadata by version if successful or addEntry is true,
        /// null otherwise.</returns>
        public FileMetadataByVersion FindMetadataByVersion(string filenameCanonical,
                                                            bool addEntry) {
            FileMetadataByVersion metadataByVersion;
            if (!metadataByCanonicalFilename.TryGetValue(
                    filenameCanonical, out metadataByVersion)) {
                if (!addEntry) return null;
                metadataByVersion = new FileMetadataByVersion(filenameCanonical);
                metadataByCanonicalFilename[filenameCanonical] = metadataByVersion;
            }
            return metadataByVersion;
        }

        /// <summary>
        /// Find the highest priority name of a manifest.
        /// </summary>
        /// <param name="name">Name to lookup from the graph.</param>
        /// <param name="aliasesByName">Adjacency list (graph) of aliases to search.</param>
        /// <param name="maxDepth">Maximum depth to traverse the graph.</param>
        /// <param name="depth">Graph traversal depth.</param>
        /// <returns>Name and depth in the graph or the supplied name and a depth of -1
        /// if not found.</returns>
        private static KeyValuePair<string, int> FindHighestPriorityManifestName(
            string name, Dictionary<string, HashSet<string>> aliasesByName, int maxDepth,
            int depth = 0) {
            if (depth > maxDepth) {
                Log(String.Format(
                        "Detected manifest name alias loop for name {0}, to fix this change the " +
                        "list (see below) to not contain a loop:\n{1}",
                        name, String.Join("\n", (new List<string>(aliasesByName.Keys)).ToArray())),
                    level: LogLevel.Warning);
                return new KeyValuePair<string, int>(name, -1);
            }
            KeyValuePair<string, int> deepestNameAndDepth =
                new KeyValuePair<string, int>(name, depth);
            HashSet<string> aliases;
            if (!aliasesByName.TryGetValue(name, out aliases)) {
                return deepestNameAndDepth;
            }
            if (aliases.Count == 1 && (new List<string>(aliases))[0] == name) {
                return deepestNameAndDepth;
            }
            foreach (var alias in aliases) {
                var nameAndDepth = FindHighestPriorityManifestName(
                    alias, aliasesByName, maxDepth, depth: depth + 1);
                    if (nameAndDepth.Value > deepestNameAndDepth.Value) {
                        deepestNameAndDepth = nameAndDepth;
                    }
            }
            return deepestNameAndDepth;
        }

        /// <summary>
        /// Create an adjacency list of manifest names.
        /// </summary>
        /// <returns>Adjacency list of manifest names. For example, given the set of manifest
        /// names for a file "foo.txt" [manifestName-0A, manifestName-1B, manifestName-1C]
        /// this returns the set of aliaes for the name of manifest "A", i.e {"A": ["B, "C"]}.
        /// </returns>
        private Dictionary<string, HashSet<string>> GetManifestAliasesByName() {
            // Create an adjacency list of manifest alias to name which can be used to search the
            // highest priority name of a manifest "foo", which is the entry {"foo": ["foo"]}.
            var aliasesByName = new Dictionary<string, HashSet<string>>();
            foreach (var metadataByVersion in Values) {
                var metadata = metadataByVersion.MostRecentVersion;
                if (metadata.isManifest) {
                    foreach (var name in metadata.customManifestNames.Values) {
                        HashSet<string> aliases = null;
                        if (!aliasesByName.TryGetValue(name, out aliases)) {
                            aliases = new HashSet<string>();
                            aliasesByName[name] = aliases;
                        }
                        aliases.Add(metadata.customManifestNames.Values[0]);
                    }
                }
            }
            // If manifest isn't an alias and doesn't have any aliases store an empty set.
            foreach (var metadataByVersion in Values) {
                var metadata = metadataByVersion.MostRecentVersion;
                if (metadata.isManifest) {
                    var manifestName = metadata.ManifestName;
                    bool found = false;
                    foreach (var kv in aliasesByName) {
                        if (kv.Key == manifestName || kv.Value.Contains(manifestName)) {
                            found = true;
                            break;
                        }
                    }
                    // If there are no aliases, store an empty set.
                    if (!found) {
                        aliasesByName[metadata.ManifestName] = new HashSet<string>();
                    }
                }
            }

            // Display adjacency list for debugging.
            var logLines = new List<string>();
            foreach (var nameAndAliases in aliasesByName) {
                logLines.Add(String.Format(
                    "name: {0} --> aliases: [{1}]", nameAndAliases.Key,
                    String.Join(", ", (new List<string>(nameAndAliases.Value)).ToArray())));
            }
            if (logLines.Count > 0) {
                Log(String.Format("Manifest aliases:\n{0}",
                                  String.Join("\n", logLines.ToArray())), verbose: true);
            }
            return aliasesByName;
        }

        /// <summary>
        /// Use manifest aliases to consolidate manifest metadata.
        /// </summary>
        /// <returns>Flattened map of highest priority manifest name by each alias of the manifest
        /// name.</returns>
        public Dictionary<string, string> ConsolidateManifests() {
            var aliasesByName = GetManifestAliasesByName();
            // Flatten graph of manifest aliases so that each entry maps to the highest priority
            // name.
            var manifestAliases = new Dictionary<string, string>();
            int numberOfAliases = aliasesByName.Count;

            var logLines = new List<string>();
            foreach (var name in aliasesByName.Keys) {
                var foundName = FindHighestPriorityManifestName(name, aliasesByName,
                                                                numberOfAliases).Key;
                manifestAliases[name] = foundName;
                logLines.Add(String.Format("name: {0} --> alias: {1}", name, foundName));
            }
            if (logLines.Count > 0) {
                Log(String.Format("Flattened manifest aliases:\n{0}",
                                  String.Join("\n", logLines.ToArray())), verbose: true);
            }

            // Create a new metadata map consolidating manifests by their highest priority name.
            var oldMetadataByCanonicalFilename = metadataByCanonicalFilename;
            Clear();
            foreach (var canonicalFilenameAndMetadataByVersion in oldMetadataByCanonicalFilename) {
                var metadata = canonicalFilenameAndMetadataByVersion.Value.MostRecentVersion;
                if (metadata.isManifest) {
                    FileMetadataByVersion manifests = canonicalFilenameAndMetadataByVersion.Value;
                    // Merge multiple versions of the manifest.
                    string manifestName;
                    if (!manifestAliases.TryGetValue(metadata.ManifestName, out manifestName)) {
                        manifestName = metadata.ManifestName;
                    }
                    Add(manifestName, manifests);

                    logLines = new List<string>();
                    foreach (var manifest in manifests.Values) {
                        logLines.Add(String.Format("file: {0}, version: {1}",
                                                   manifest.filename, manifest.versionString));
                    }
                    Log(String.Format("Add manifests to package '{0}':\n{1}",
                                      manifestName, String.Join("\n", logLines.ToArray())),
                        verbose: true);
                } else {
                    Add(canonicalFilenameAndMetadataByVersion.Key,
                        canonicalFilenameAndMetadataByVersion.Value);
                }
            }
            return manifestAliases;
        }

        /// <summary>
        /// For each plugin (DLL) referenced by this set, disable targeting
        /// for all versions and re-enable platform targeting for the most
        /// recent version.
        /// </summary>
        /// <param name="forceUpdate">Whether the update was forced by the
        /// user.</param>
        /// <returns>true if any plugin metadata was modified and requires an
        /// AssetDatabase.Refresh(), false otherwise.</returns>
        public bool EnableMostRecentPlugins(bool forceUpdate) {
            return EnableMostRecentPlugins(forceUpdate, new HashSet<string>());
        }

        /// <summary>
        /// For each plugin (DLL) referenced by this set, disable targeting
        /// for all versions and re-enable platform targeting for the most
        /// recent version.
        /// </summary>
        /// <param name="forceUpdate">Whether the update was forced by the
        /// user.</param>
        /// <param name="disableFiles">Set of files that should be disabled.</param>
        /// <returns>true if any plugin metadata was modified and requires an
        /// AssetDatabase.Refresh(), false otherwise.</returns>
        public bool EnableMostRecentPlugins(bool forceUpdate,
                                            HashSet<string> disableFiles) {
            bool modified = false;

            // If PluginImporter isn't available it's not possible
            // to enable / disable targeting.
            if (!FileMetadataByVersion.PluginImporterAvailable) {
                // File that stores the state of the warning flag for this editor session.
                // We need to store this in a file as this DLL can be reloaded while the
                // editor is open resetting any state in memory.
                string warningFile = Path.Combine(
                    FileUtils.ProjectTemporaryDirectory,
                    "VersionHandlerEnableMostRecentPlugins.txt");
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
                foreach (var metadataByVersion in Values) {
                    bool hasRelevantVersions = false;
                    var fileInfoLines = new List<string>();
                    fileInfoLines.Add(String.Format("Target Filename: {0}",
                                                    metadataByVersion.filenameCanonical));
                    foreach (var metadata in metadataByVersion.Values) {
                        // Ignore manifests and files that don't target any build targets.
                        if (metadata.isManifest ||
                            metadata.targets == null || metadata.targets.Count == 0) {
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
                                    String.Join(", ", new List<string>(metadata.targets).ToArray())
                                                                                            : ""));
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

            foreach (var metadataByVersion in Values) {
                modified |= metadataByVersion.EnableMostRecentPlugins(disableFiles);
                enabledEditorDlls.UnionWith(metadataByVersion.EnabledEditorDlls);
            }
            return modified;
        }

        /// <summary>
        /// Parse metadata from a set of filenames.
        /// </summary>
        /// <param name="filenames">Filenames to parse.</param>
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
        /// <returns>Filtered MetadataSet.</returns>
        public static FileMetadataSet FindWithPendingUpdates(
                FileMetadataSet metadataSet) {
            FileMetadataSet outMetadataSet = new FileMetadataSet();
            foreach (var filenameAndMetadata in
                     metadataSet.metadataByCanonicalFilename) {
                var metadataByVersion = filenameAndMetadata.Value.Values;
                bool needsUpdate = metadataByVersion.Count > 1;
                foreach (var metadata in metadataByVersion) {
                    if ((metadata.targets != null &&
                         metadata.targets.Count > 0) ||
                        metadata.isManifest) {
                        needsUpdate = true;
                        break;
                    }
                }
                if (needsUpdate) {
                    outMetadataSet.Add(filenameAndMetadata.Key, filenameAndMetadata.Value);
                }
            }
            return outMetadataSet;
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
        /// Metadata of files in the package indexed by current filename.
        /// </summary>
        public Dictionary<string, FileMetadata> metadataByFilename =
            new Dictionary<string, FileMetadata>();

        /// <summary>
        /// Set of obsolete files in this package.
        /// </summary>
        public HashSet<string> obsoleteFiles = new HashSet<string>();

        /// <summary>
        /// Backing store of Aliases.
        /// </summary>
        private HashSet<string> aliases = new HashSet<string>();

        /// <summary>
        /// Alias names of this manifest.
        /// </summary>
        public ICollection<string> Aliases { get { return aliases; } }

        /// <summary>
        /// Cache of all manifest references indexed by package name.
        /// </summary>
        private static Dictionary<string, ManifestReferences> cacheAll =
            new Dictionary<string, ManifestReferences>();

        /// <summary>
        /// Cache of manifest references installed under Assets (i.e not in the Unity Package
        /// Manager) indexed by package name.
        /// </summary>
        private static Dictionary<string, ManifestReferences> cacheInAssets =
            new Dictionary<string, ManifestReferences>();

        /// <summary>
        /// Get all files managed by this package, including manifest and obsolete files.
        /// </summary>
        public IEnumerable<string> All {
            get {
                List<string> files = new List<string>();
                if (currentMetadata != null) {
                    files.Add(currentMetadata.filename);
                }
                if (metadataByVersion != null) {
                    foreach (var fileMetadata in metadataByVersion.Values) {
                        files.Add(fileMetadata.filename);
                    }
                }
                if (currentFiles != null) {
                    foreach (var file in currentFiles) {
                        files.Add(file);
                    }
                }
                if (obsoleteFiles != null) {
                    foreach (var file in obsoleteFiles) {
                        files.Add(file);
                    }
                }
                return files;
            }
        }

        /// <summary>
        /// Create an instance.
        /// </summary>
        public ManifestReferences() { }

        /// <summary>
        /// Parse a legacy manfiest file.
        /// </summary>
        /// <param name="metadata">Package manifest to parse.</param>
        /// <param name="metadataSet">Set of all metadata files in the
        /// project.  This is used to handle file renaming in the parsed
        /// manifest.  If the manifest contains files that have been
        /// renamed it's updated with the new filenames.</param>
        /// <returns>Metadata of files in the package indexed by current filename.
        /// The FileMetadataByVersion will be null for files that aren't present in the
        /// asset database.</returns>
        public Dictionary<string, KeyValuePair<FileMetadata, FileMetadataByVersion>>
                ParseLegacyManifest(FileMetadata metadata, FileMetadataSet metadataSet) {
            var filesInManifest =
                new Dictionary<string, KeyValuePair<FileMetadata, FileMetadataByVersion>>();
            StreamReader manifestFile =
                new StreamReader(metadata.filename);
            string line;
            while ((line = manifestFile.ReadLine()) != null) {
                var strippedLine = line.Trim();
                if (String.IsNullOrEmpty(strippedLine)) continue;
                var manifestFileMetadata = new FileMetadata(strippedLine);
                // Check for a renamed file.
                long version = manifestFileMetadata.CalculateVersion();
                var existingFileMetadata =
                    metadataSet.FindMetadata(manifestFileMetadata.filenameCanonical, version) ??
                    metadataSet.FindMetadata(manifestFileMetadata.filename, version);
                if (existingFileMetadata != null) manifestFileMetadata = existingFileMetadata;
                filesInManifest[manifestFileMetadata.filename] =
                    new KeyValuePair<FileMetadata, FileMetadataByVersion>(
                        manifestFileMetadata,
                        metadataSet.FindMetadataByVersion(manifestFileMetadata.filenameCanonical,
                                                          false));
            }
            manifestFile.Close();
            return filesInManifest;
        }

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
                Log(String.Format("Parsing manifest '{0}' of package '{1}'", metadata.filename,
                                  metadataByVersion.filenameCanonical), verbose: true);
                this.metadataByVersion = metadataByVersion;
                var filesInManifest = ParseLegacyManifest(metadata, metadataSet);
                // If this is the most recent manifest version, remove all
                // current files from the set to delete.
                if (versionIndex == numberOfVersions) {
                    var filenames = new HashSet<string>();
                    // Add references to the most recent file metadata for each referenced file.
                    metadataByFilename = new Dictionary<string, FileMetadata>();
                    foreach (var kv in filesInManifest.Values) {
                        var fileMetadataByVersion = kv.Value;
                        var mostRecentMetadata =
                            fileMetadataByVersion != null ?
                            fileMetadataByVersion.MostRecentVersion : kv.Key;
                        metadataByFilename[mostRecentMetadata.filename] = mostRecentMetadata;
                        filenames.Add(mostRecentMetadata.filename);
                        if (fileMetadataByVersion != null) {
                            var versions = fileMetadataByVersion.Values;
                            int numberOfFileVersions = versions.Count;
                            int fileVersionIndex = 0;
                            foreach (var version in versions) {
                                fileVersionIndex ++;
                                if (fileVersionIndex == numberOfFileVersions) break;
                                obsoleteFiles.Add(version.filename);
                            }
                        }
                    }

                    currentFiles.UnionWith(filenames);
                    obsoleteFiles.ExceptWith(filenames);
                    currentMetadata = metadata;
                } else {
                    var filenames = new HashSet<string>(filesInManifest.Keys);
                    obsoleteFiles.UnionWith(filenames);
                }
            }
            filenameCanonical = metadataByVersion.filenameCanonical;
            var currentFilesSorted = new List<string>(currentFiles);
            var obsoleteFilesSorted = new List<string>(obsoleteFiles);
            currentFilesSorted.Sort();
            obsoleteFilesSorted.Sort();
            var components = new List<string>();
            if (currentFilesSorted.Count > 0) {
                components.Add(String.Format("Current files:\n{0}",
                                             String.Join("\n", currentFilesSorted.ToArray())));
            }
            if (obsoleteFilesSorted.Count > 0) {
                components.Add(String.Format("Obsolete files:\n{0}",
                                             String.Join("\n", obsoleteFilesSorted.ToArray())));
            }
            Log(String.Format("'{0}' Manifest:\n{1}",
                              filenameCanonical, String.Join("\n", components.ToArray())),
                verbose: true);
            return true;
        }

        /// <summary>
        /// Find and read all package manifests.
        /// </summary>
        /// <param name="metadataSet">Set to query for manifest files.</param>
        /// <returns>List of ManifestReferences which contain current and
        /// obsolete files referenced in each manifest file.</returns>
        public static List<ManifestReferences> FindAndReadManifests(FileMetadataSet metadataSet) {
            return new List<ManifestReferences>(
                FindAndReadManifestsByPackageName(metadataSet).Values);
        }

        /// <summary>
        /// Find and read all package manifests and bucket by package name.
        /// </summary>
        /// <returns>Dictionary of ManifestReferences indexed by package name.</returns>
        public static Dictionary<string, ManifestReferences> FindAndReadManifestsByPackageName(
                 FileMetadataSet metadataSet) {
            var manifestAliases = metadataSet.ConsolidateManifests();
            // Invert the map of manifest aliases to create a dictionary that maps canonical name
            // to a set of aliases.
            var aliasesByName = new Dictionary<string, HashSet<string>>();
            foreach (var aliasAndName in manifestAliases) {
                var alias = aliasAndName.Key;
                var name = aliasAndName.Value;
                HashSet<string> aliases;
                if (!aliasesByName.TryGetValue(name, out aliases)) {
                    aliases = new HashSet<string>();
                    aliasesByName[name] = aliases;
                }
                aliases.Add(alias);
            }

            var allObsoleteFiles = new HashSet<string>();
            var manifestReferencesByPackageName = new Dictionary<string, ManifestReferences>();
            foreach (var metadataByVersion in metadataSet.Values) {
                ManifestReferences manifestReferences = new ManifestReferences();
                if (manifestReferences.ParseManifests(metadataByVersion,
                                                      metadataSet)) {
                    manifestReferences.aliases =
                        aliasesByName[manifestReferences.filenameCanonical];
                    manifestReferencesByPackageName[manifestReferences.filenameCanonical] =
                        manifestReferences;
                    allObsoleteFiles.UnionWith(manifestReferences.obsoleteFiles);
                }
            }

            // Move globally obsolete files to the obsolete files set across all manifests.
            foreach (var manifestReferences in manifestReferencesByPackageName.Values) {
                var newlyObsoleteFiles = new HashSet<string>(manifestReferences.currentFiles);
                newlyObsoleteFiles.IntersectWith(allObsoleteFiles);
                manifestReferences.currentFiles.ExceptWith(newlyObsoleteFiles);
                manifestReferences.obsoleteFiles.UnionWith(newlyObsoleteFiles);
            }

            return manifestReferencesByPackageName;
        }

        /// <summary>
        /// Find and read all package manifests.
        /// </summary>
        /// <returns>List of ManifestReferences which contain current and
        /// obsolete files referenced in each manifest file.</returns>
        public static List<ManifestReferences> FindAndReadManifests() {
            if (cacheAll.Count == 0) {
                cacheAll = FindAndReadManifestsByPackageName(
                    FileMetadataSet.ParseFromFilenames(FindAllAssets()));
            }
            return new List<ManifestReferences>(cacheAll.Values);
        }

        /// <summary>
        /// Find and read all package manifests from the Assets folder and index by name.
        /// </summary>
        /// <returns>Dictionary of ManifestReferences indexed by package name.</returns>
        public static Dictionary<string, ManifestReferences>
                FindAndReadManifestsInAssetsFolderByPackageName() {
            if (cacheInAssets.Count == 0) {
                cacheInAssets = FindAndReadManifestsByPackageName(
                    FileMetadataSet.FilterOutReadOnlyFiles(
                        FileMetadataSet.ParseFromFilenames(FindAllAssets())));

            }
            return cacheInAssets;
        }

        /// <summary>
        /// Find and read all package manifests from Assets folder.
        /// </summary>
        /// <returns>List of ManifestReferences from Assets folder</returns>
        public static List<ManifestReferences> FindAndReadManifestsInAssetsFolder() {
            return new List<ManifestReferences>(
                FindAndReadManifestsInAssetsFolderByPackageName().Values);
        }

        /// <summary>
        /// Delete a subset of packages managed by Version Handler.
        /// </summary>
        /// <param name="packages">A HashSet of canonical name of the packages to be
        /// uninstalled</param>
        /// <param name="force">Force to remove all file even it is referenced by other
        /// packages.</param>
        /// <returns>If successful returns an empty collection, a collection of files that could
        /// not be removed otherwise.</returns>
        public static ICollection<string> DeletePackages(HashSet<string> packages,
                                                         bool force = false) {
            // Create a map from canonical name to ManifestReferences.
            var manifestMap = new Dictionary<string, ManifestReferences>(
                FindAndReadManifestsInAssetsFolderByPackageName());

            HashSet<string> filesToRemove = new HashSet<string>();
            foreach (var pkgName in packages) {
                ManifestReferences pkg = null;
                if (manifestMap.TryGetValue(pkgName, out pkg)) {
                    filesToRemove.UnionWith(pkg.All);
                }
                manifestMap.Remove(pkgName);
            }

            HashSet<string> filesToExclude = new HashSet<string>();
            if (!force) {
                // Keep files which are referenced by other packages.
                foreach (var manifestEntry in manifestMap) {
                    filesToExclude.UnionWith(manifestEntry.Value.All);
                }
            }

            HashSet<string> filesToKeep = new HashSet<string>(filesToRemove);
            filesToKeep.IntersectWith(filesToExclude);
            filesToRemove.ExceptWith(filesToExclude);

            VersionHandlerImpl.Log(String.Format("Uninstalling the following packages:\n{0}\n{1}",
                    String.Join("\n", (new List<string>(packages)).ToArray()),
                    filesToKeep.Count == 0 ? "" : String.Format(
                            "Ignoring the following files referenced by other packages:\n{0}\n",
                            String.Join("\n", (new List<string>(filesToKeep)).ToArray()))));

            var result = FileUtils.RemoveAssets(filesToRemove, VersionHandlerImpl.Logger);
            if (result.Success) {
                VersionHandlerImpl.analytics.Report("uninstallpackage/delete/success",
                        "Successfully Deleted All Files in Packages");
            } else {
                VersionHandlerImpl.analytics.Report("uninstallpackage/delete/fail",
                        "Fail to Delete Some Files in Packages");
            }
            return result.RemoveFailed;
        }

        /// <summary>
        /// Flush the caches.
        /// </summary>
        public static void FlushCaches() {
            cacheAll = new Dictionary<string, ManifestReferences>();
            cacheInAssets = new Dictionary<string, ManifestReferences>();
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
        /// Same as the "unreferenced" member excluding manifest files.
        /// </summary>
        public HashSet<string> unreferencedExcludingManifests;

        /// <summary>
        /// Obsolete files that are referenced by manifests.  Each item in
        /// the dictionary contains a list of manifests referencing the file.
        /// </summary>
        public Dictionary<string, List<ManifestReferences>> referenced;

        /// <summary>
        /// Same as the "referenced" member excluding manifest files.
        /// </summary>
        public Dictionary<string, List<ManifestReferences>> referencedExcludingManifests;

        /// <summary>
        /// Get all referenced and unreferenced obsolete files.
        /// </summary>
        public HashSet<string> All {
            get {
                var all = new HashSet<string>();
                all.UnionWith(unreferenced);
                all.UnionWith(referenced.Keys);
                return all;
            }
        }

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
        /// files.</param>
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
                new Dictionary<string, List<ManifestReferences>>();
            var referencedObsoleteFilesExcludingManifests =
                new Dictionary<string, List<ManifestReferences>>();
            var obsoleteFilesToDelete = new HashSet<string>();
            var obsoleteFilesToDeleteExcludingManifests = new HashSet<string>();
            foreach (var obsoleteFile in obsoleteFiles) {
                var manifestsReferencingFile = new List<ManifestReferences>();
                foreach (var manifestReferences in manifestReferencesList) {
                    if (manifestReferences.currentFiles.Contains(obsoleteFile)) {
                        manifestsReferencingFile.Add(manifestReferences);
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
                var newFilename = FileUtils.NormalizePathSeparators(
                    Path.Combine(Path.GetDirectoryName(filename),
                                 LibraryPrefix + library.linuxLibraryBasename + LIBRARY_EXTENSION));
                library.RenameAsset(newFilename);
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
    private const string PREFERENCE_RENAME_TO_DISABLE_FILES_ENABLED =
        "Google.VersionHandler.RenameToDisableFilesEnabled";
    // List of preference keys, used to restore default settings.
    private static string[] PREFERENCE_KEYS = new [] {
        PREFERENCE_ENABLED,
        PREFERENCE_CLEANUP_PROMPT_ENABLED,
        PREFERENCE_RENAME_TO_CANONICAL_FILENAMES,
        PREFERENCE_VERBOSE_LOGGING_ENABLED,
        PREFERENCE_RENAME_TO_DISABLE_FILES_ENABLED
    };

    // Name of this plugin.
    private const string PLUGIN_NAME = "Google Version Handler";

    // Path of the file that contains methods to call when the version handler update process
    // is complete.
    private const string CALLBACKS_PATH = "Temp/VersionHandlerImplCallbacks";
    // Path of the file that indicates whether the asset database is currently being refreshed
    // due to this module.
    private const string REFRESH_PATH = "Temp/VersionHandlerImplRefresh";
    // Path of the file that indicates whether files need to be cleaned up after an asset database
    // refresh.
    private const string CLEANUP_FILES_PENDING_PATH = "Temp/VersionHandlerImplCleanupFilesPending";

    // Whether compilation is currently occuring.
    private static bool compiling = false;

    // Settings used by this module.
    internal static ProjectSettings settings = new ProjectSettings("Google.VersionHandler.");

    /// <summary>
    /// Logger for this module.
    /// </summary>
    private static Logger logger = new Logger();

    public static Logger Logger {
        get { return logger; }
    }

    // Google Analytics tracking ID.
    internal const string GA_TRACKING_ID = "UA-54627617-3";
    internal const string MEASUREMENT_ID = "com.google.external-dependency-manager";
    // Name of the plugin suite.
    internal const string PLUGIN_SUITE_NAME = "External Dependency Manager";
    // Privacy policy for analytics data usage.
    internal const string PRIVACY_POLICY = "https://policies.google.com/privacy";
    // Product Url
    internal const string DATA_USAGE_URL =
            "https://github.com/googlesamples/unity-jar-resolver#analytics";

    // Analytics reporter.
    internal static EditorMeasurement analytics = new EditorMeasurement(
            settings, logger, GA_TRACKING_ID, MEASUREMENT_ID, PLUGIN_SUITE_NAME, "",
            PRIVACY_POLICY) {
        BasePath = "/versionhandler/",
        BaseQuery = String.Format("version={0}", VersionHandlerVersionNumber.Value.ToString()),
        BaseReportName = "Version Handler: ",
        InstallSourceFilename = Assembly.GetAssembly(typeof(VersionHandlerImpl)).Location,
        DataUsageUrl = DATA_USAGE_URL
    };

    /// <summary>
    /// Load log preferences.
    /// </summary>
    private static void LoadLogPreferences() {
        VerboseLoggingEnabled = VerboseLoggingEnabled;
    }

    /// <summary>
    /// Enables / disables assets imported at multiple revisions / versions.
    /// In addition, this module will read text files matching _manifest_
    /// and remove files from older manifest files.
    /// </summary>
    static VersionHandlerImpl() {
        Log("Loaded VersionHandlerImpl", verbose: true);
        RunOnMainThread.Run(() => {
                LoadLogPreferences();
                UpdateVersionedAssetsOnUpdate();
            }, runNow: false);
    }

    static void UpdateVersionedAssetsOnUpdate() {
        // Skips update if this module is disabled.
        if (!Enabled) {
            return;
        }

        UpdateVersionedAssets();
        NotifyWhenCompliationComplete(false);
        UpdateAssetsWithBuildTargets(EditorUserBuildSettings.activeBuildTarget);
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
    /// Whether files need to be cleaned up after an asset database refresh.
    /// </summary>
    private static bool CleanupFilesPending {
        get {
            return File.Exists(CLEANUP_FILES_PENDING_PATH);
        }

        set {
            bool pending = CleanupFilesPending;
            if (pending != value) {
                if (value) {
                    File.WriteAllText(CLEANUP_FILES_PENDING_PATH, "Cleanup files after refresh");
                } else {
                    File.Delete(CLEANUP_FILES_PENDING_PATH);
                }
            }
        }
    }

    /// <summary>
    /// Whether all editor DLLs have been loaded into the app domain.
    /// </summary>
    private static bool EnabledEditorDllsLoaded {
        get {
            var loadedAssemblyPaths = new HashSet<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                string location;
                try {
                    location = assembly.Location;
                } catch (NotSupportedException) {
                    // Dynamic assemblies do not have a file location so ignore.
                    continue;
                }
                if (String.IsNullOrEmpty(location)) continue;
                var path = Path.GetFullPath(location);
                if (enabledEditorDlls.Contains(path)) {
                    loadedAssemblyPaths.Add(path);
                }
            }
            return loadedAssemblyPaths.IsSupersetOf(enabledEditorDlls);
        }
    }

    /// <summary>
    /// Polls on main thread until compilation is finished prior to calling
    /// NotifyUpdateCompleteMethods() if an asset database refresh was in progress or
    /// forceNotification is true.
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
    /// <param name="forceNotification">Force notification if the asset database was not
    /// refreshing.</param>
    private static void NotifyWhenCompliationComplete(bool forceNotification) {
        RunOnMainThread.PollOnUpdateUntilComplete(() => {
                if (EditorApplication.isCompiling || !EnabledEditorDllsLoaded) {
                    if (!compiling) {
                        Log("Compiling...", verbose: true);
                    }
                    compiling = true;
                    // When running a single method this can get stuck forever as
                    // PollOnUpdateUntilComplete() will block the main thread which prevents the
                    // EditorApplication.isCompiling flag from being updated by the editor.
                    return ExecutionEnvironment.ExecuteMethodEnabled;
                }
                if (compiling) {
                    Log("Compilation complete.", verbose: true);
                    compiling = false;
                }
                // If a refresh was initiated by this module, clear the refresh flag.
                var wasRefreshing = Refreshing;
                Refreshing = false;
                if (wasRefreshing || forceNotification) {
                    if (CleanupFilesPending) {
                        CleanupFilesPending = false;
                        RunOnMainThread.Run(() => {
                                UpdateVersionedAssetsOnMainThread(false, () => {},
                                                                  setCleanupFilesPending: false);
                            }, runNow: false);
                    }

                    analytics.Report("enablemostrecentplugins", "Enable Most Recent Plugins");
                    NotifyUpdateCompleteMethods();
                }
                return true;
            });
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
    /// Reset settings of this plugin to default values.
    /// </summary>
    internal static void RestoreDefaultSettings() {
        settings.DeleteKeys(PREFERENCE_KEYS);
        analytics.RestoreDefaultSettings();
    }

    /// <summary>
    /// Whether to use project level settings.
    /// </summary>
    public static bool UseProjectSettings {
        get { return settings.UseProjectSettings; }
        set { settings.UseProjectSettings = value; }
    }

    /// <summary>
    /// Enable / disable automated version handling.
    /// </summary>
    public static bool Enabled {
        get {
            return !System.Environment.CommandLine.ToLower().Contains("-gvh_disable") &&
                settings.GetBool(PREFERENCE_ENABLED, defaultValue: true);
        }
        set { settings.SetBool(PREFERENCE_ENABLED, value); }
    }

    /// <summary>
    /// Enable / disable prompting the user on clean up.
    /// </summary>
    public static bool CleanUpPromptEnabled {
        get { return settings.GetBool(PREFERENCE_CLEANUP_PROMPT_ENABLED,
                                      defaultValue: true); }
        set { settings.SetBool(PREFERENCE_CLEANUP_PROMPT_ENABLED, value); }
    }

    /// <summary>
    /// Enable / disable renaming to canonical filenames.
    /// </summary>
    public static bool RenameToCanonicalFilenames {
        get { return settings.GetBool(PREFERENCE_RENAME_TO_CANONICAL_FILENAMES,
                                      defaultValue: false); }
        set { settings.SetBool(PREFERENCE_RENAME_TO_CANONICAL_FILENAMES, value); }
    }

    /// <summary>
    /// Enable / disable verbose logging.
    /// </summary>
    public static bool VerboseLoggingEnabled {
        get { return settings.GetBool(PREFERENCE_VERBOSE_LOGGING_ENABLED,
                                      defaultValue: false); }
        set {
            settings.SetBool(PREFERENCE_VERBOSE_LOGGING_ENABLED, value);
            logger.Level = value ? LogLevel.Verbose : LogLevel.Info;
        }
    }

    /// <summary>
    /// Enable / disable verbose logging.
    /// </summary>
    public static bool RenameToDisableFilesEnabled {
        get {
            return settings.GetBool(PREFERENCE_RENAME_TO_DISABLE_FILES_ENABLED, defaultValue: true);
        }
        set { settings.SetBool(PREFERENCE_RENAME_TO_DISABLE_FILES_ENABLED, value); }
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
    /// Whether to also log to a file in the project.
    /// </summary>
    internal static bool LogToFile {
        get { return logger.LogFilename != null; }
        set { logger.LogFilename = value ? "VersionHandlerSave.log" : null; }
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
        logger.Log(message, level: verbose ? LogLevel.Verbose : level);
    }

    /// <summary>
    /// Generate a documentation URL.
    /// </summary>
    /// <param name="subsection">String to add to the URL.</param>
    /// <returns>URL</returns>
    internal static string DocumentationUrl(string subsection) {
        return String.Format("{0}{1}", "https://github.com/googlesamples/unity-jar-resolver",
                             subsection);
    }

    /// <summary>
    /// Link to the project documentation.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Documentation")]
    public static void OpenProjectDocumentation() {
        analytics.OpenUrl(VersionHandlerImpl.DocumentationUrl("#overview"), "Overview");
    }

    /// <summary>
    /// Link to the documentation.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Version Handler/Documentation")]
    public static void OpenDocumentation() {
        analytics.OpenUrl(VersionHandlerImpl.DocumentationUrl("#version-handler-usage"), "Usage");
    }

    /// <summary>
    /// Add the settings dialog for this module to the menu and show the
    /// window when the menu item is selected.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Version Handler/Settings")]
    public static void ShowSettings() {
        SettingsDialog window = (SettingsDialog)EditorWindow.GetWindow(
            typeof(SettingsDialog), true, PLUGIN_NAME + " Settings");
        window.Initialize();
        window.Show();
    }

    /// <summary>
    /// Menu item which forces version handler execution.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Version Handler/Update")]
    public static void UpdateNow() {
        LoadLogPreferences();
        UpdateVersionedAssets(true, () => {
                DialogWindow.Display(PLUGIN_NAME, "Update complete.",
                                     DialogWindow.Option.Selected0, "OK");
            });
    }

    /// <summary>
    /// Menu item which logs the set of installed packages and their contents to the console.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Version Handler/Display Managed Packages")]
    public static void DisplayInstalledPackages() {
        var manifests = ManifestReferences.FindAndReadManifestsInAssetsFolder();
        foreach (var pkg in manifests) {
            if (!String.IsNullOrEmpty(pkg.filenameCanonical) && pkg.metadataByVersion != null) {
                var versions = new List<string>();
                foreach (var fileMetadata in pkg.metadataByVersion.Values) {
                    versions.Add(fileMetadata.versionString);
                }
                var lines = new List<string>();
                lines.Add(String.Format("{0}: [{1}]", pkg.filenameCanonical,
                                        String.Join(", ", versions.ToArray())));

                var packageFiles = new List<string>(pkg.currentFiles);
                packageFiles.Sort();
                if (packageFiles.Count > 0) {
                    lines.Add(String.Format("Up-to-date files:\n{0}\n\n",
                                            String.Join("\n", packageFiles.ToArray())));
                }
                var obsoleteFiles = new List<string>(pkg.obsoleteFiles);
                obsoleteFiles.Sort();
                if (obsoleteFiles.Count > 0) {
                    lines.Add(String.Format("Obsolete files:\n{0}\n\n",
                                            String.Join("\n", obsoleteFiles.ToArray())));
                }
                Log(String.Join("\n", lines.ToArray()));
            }
        }
    }

    /// <summary>
    /// Menu item which moves all managed files to their initial install locations.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Version Handler/Move Files To Install Locations")]
    public static void MoveFilesToInstallLocations() {
        var manifests = ManifestReferences.FindAndReadManifestsInAssetsFolder();
        foreach (var pkg in manifests) {
            if (!String.IsNullOrEmpty(pkg.filenameCanonical) && pkg.metadataByVersion != null) {
                var logLines = new List<string>();
                foreach (var metadata in pkg.metadataByFilename.Values) {
                    var exportPathInProject = metadata.ExportPathInProject;
                    if (!String.IsNullOrEmpty(exportPathInProject)) {
                        var originalFilename = metadata.filename;
                        if (originalFilename != exportPathInProject &&
                            metadata.RenameAsset(exportPathInProject)) {
                            logLines.Add(String.Format("{0} --> {1}", originalFilename,
                                                       exportPathInProject));
                        }
                    }
                }
                if (logLines.Count > 0) {
                    Log(String.Format("'{0}' files moved to their install locations:\n{1}",
                                      pkg.filenameCanonical,
                                      String.Join("\n", logLines.ToArray())));
                }
            }
        }
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
    /// <param name="directories">Directories to search for the assets in the project. Directories
    /// that don't exist are ignored.</param>
    public static string[] SearchAssetDatabase(string assetsFilter = null,
                                               VersionHandler.FilenameFilter filter = null,
                                               IEnumerable<string> directories = null) {
        HashSet<string> matchingEntries = new HashSet<string>();
        assetsFilter = assetsFilter != null ? assetsFilter : "t:Object";
        string[] searchDirectories = null;
        if (directories != null) {
            var existingDirectories = new List<string>();
            foreach (string directory in directories) {
                if (Directory.Exists(directory)) existingDirectories.Add(directory);
            }
            if (existingDirectories.Count > 0) searchDirectories = existingDirectories.ToArray();
        }
        var assetGuids = searchDirectories == null ? AssetDatabase.FindAssets(assetsFilter) :
            AssetDatabase.FindAssets(assetsFilter, searchDirectories);
        foreach (string assetGuid in assetGuids) {
            string filename = AssetDatabase.GUIDToAssetPath(assetGuid);
            // Ignore non-existent files as it's possible for the asset database to reference
            // missing files if it hasn't been refreshed or completed a refresh.
            if ((File.Exists(filename) || Directory.Exists(filename)) &&
                (filter == null || filter(filename))) {
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
            Log("Failed to move obsolete file to trash: " + filename + "\n" +
                "You may need to restart Unity.",
                level: LogLevel.Error);
        }
    }

    /// <summary>
    /// Find all files in the asset database with multiple version numbers
    /// encoded in their filename, select the most recent revisions and
    /// delete obsolete versions and files referenced by old manifests that
    /// are not present in the most recent manifests.
    /// </summary>
    public static void UpdateVersionedAssets() {
        UpdateVersionedAssets(false);
    }

    /// <summary>
    /// Find all files in the asset database with multiple version numbers
    /// encoded in their filename, select the most recent revisions and
    /// delete obsolete versions and files referenced by old manifests that
    /// are not present in the most recent manifests.
    /// </summary>
    /// <param name="forceUpdate">Whether to force an update.</param>
    public static void UpdateVersionedAssets(bool forceUpdate) {
        UpdateVersionedAssets(forceUpdate, () => {});
    }

    /// <summary>
    /// All editor DLLs enabled by the last pass of UpdateVersionedAssets().
    /// </summary>
    private static HashSet<string> enabledEditorDlls = new HashSet<string>();

    /// <summary>
    /// Find all files in the asset database with multiple version numbers
    /// encoded in their filename, select the most recent revisions and
    /// delete obsolete versions and files referenced by old manifests that
    /// are not present in the most recent manifests.
    /// </summary>
    /// <param name="forceUpdate">Whether to force an update.</param>
    /// <param name="complete">Called when this is method is complete.</param>
    public static void UpdateVersionedAssets(bool forceUpdate, Action complete) {
        CancelUpdateVersionedAssets();
        RunOnMainThread.Run(() => {
                UpdateVersionedAssetsOnMainThread(forceUpdate, complete); },
            runNow: false);
    }

    /// <summary>
    /// Find all files in the asset database with multiple version numbers
    /// encoded in their filename, select the most recent revisions and
    /// delete obsolete versions and files referenced by old manifests that
    /// are not present in the most recent manifests.
    /// </summary>
    /// <param name="forceUpdate">Whether to force an update.</param>
    /// <param name="complete">Called when this is method is complete.</param>
    /// <param name="setCleanupFilesPending">Whether to set the CleanupFilesPending flag to run
    /// this method again after an asset database refresh is complete.</param>
    private static void UpdateVersionedAssetsOnMainThread(bool forceUpdate,
                                                          Action complete,
                                                          bool setCleanupFilesPending = true) {
        // If this module is disabled do nothing.
        if (!forceUpdate && !Enabled) {
            complete();
            return;
        }

        UpdateAssetsWithBuildTargets(EditorUserBuildSettings.activeBuildTarget);

        var allMetadataSet = FileMetadataSet.FilterOutReadOnlyFiles(
            FileMetadataSet.ParseFromFilenames(FindAllAssets()));
        // Rename linux libraries, if any are being tracked.
        var linuxLibraries = new LinuxLibraryRenamer(allMetadataSet);
        linuxLibraries.RenameLibraries();

        var metadataSet = allMetadataSet;
        if (!forceUpdate) metadataSet = FileMetadataSet.FindWithPendingUpdates(allMetadataSet);

        var obsoleteFiles = new ObsoleteFiles(
            ManifestReferences.FindAndReadManifests(allMetadataSet), allMetadataSet);
        if (metadataSet.EnableMostRecentPlugins(forceUpdate, obsoleteFiles.All)) {
            enabledEditorDlls.UnionWith(metadataSet.EnabledEditorDlls);
            AssetDatabase.Refresh();
            Refreshing = true;
        }

        // Obsolete files that are no longer referenced can be safely deleted, prompt the user for
        // confirmation if they have the option enabled.
        var cleanupFiles = new List<KeyValuePair<string, string>>();
        if (obsoleteFiles.unreferenced.Count > 0) {
            if (!ExecutionEnvironment.InBatchMode && CleanUpPromptEnabled &&
                obsoleteFiles.unreferencedExcludingManifests.Count > 0) {
                foreach (var filename in obsoleteFiles.unreferenced) {
                    cleanupFiles.Add(new KeyValuePair<string, string>(filename, filename));
                }
            } else {
                FileUtils.RemoveAssets(obsoleteFiles.unreferenced, VersionHandlerImpl.Logger);
            }
        }

        // If any obsolete referenced files are present, prompt the user for confirmation of
        // deletion.
        if (obsoleteFiles.referenced.Count > 0) {
            if (!ExecutionEnvironment.InBatchMode) {
                foreach (var item in obsoleteFiles.referenced) {
                    var filename = item.Key;
                    var manifestReferencesList = item.Value;
                    var references = new List<string>();
                    foreach (var manifestReferences in manifestReferencesList) {
                        references.Add(manifestReferences.filenameCanonical);
                    }
                    cleanupFiles.Add(
                        new KeyValuePair<string, string>(
                            filename,
                            String.Format("{0} ({1})", filename,
                                          String.Join(", ", references.ToArray()))));
                }
            } else {
                FileUtils.RemoveAssets(obsoleteFiles.referenced.Keys, VersionHandlerImpl.Logger);
            }
        }

        bool cleanupFilesPending = cleanupFiles.Count > 0;
        if (cleanupFilesPending && !Refreshing) {
            var window = MultiSelectWindow.CreateMultiSelectWindow<ObsoleteFilesWindow>(PLUGIN_NAME);
            Action<string> logObsoleteFile = (filename) => {
                Log("Leaving obsolete file: " + filename, verbose: true);
            };
            Action deleteFiles = () => {
                bool deletedAll = true;
                FileUtils.RemoveAssets(window.SelectedItems, VersionHandlerImpl.Logger);
                foreach (var filenameAndDisplay in window.AvailableItems) {
                    if (!window.SelectedItems.Contains(filenameAndDisplay.Key)) {
                        deletedAll = false;
                        logObsoleteFile(filenameAndDisplay.Value);
                    }
                }
                if (deletedAll) {
                    analytics.Report("deleteobsoletefiles/confirm/all",
                                     "Delete All Obsolete Files");
                } else {
                    analytics.Report("deleteobsoletefiles/confirm/subset",
                                     "Delete Subset of Obsolete Files");
                }
                complete();
            };
            Action leaveFiles = () => {
                foreach (var filenameAndDisplay in window.AvailableItems) {
                    logObsoleteFile(filenameAndDisplay.Value);
                }
                analytics.Report("deleteobsoletefiles/abort",
                                 "Leave Obsolete Files");
                complete();
            };
            window.AvailableItems = new List<KeyValuePair<string, string>>(cleanupFiles);
            window.Sort(1);
            window.SelectAll();
            window.Caption =
                "Would you like to delete the following obsolete files in your project?";
            window.OnApply = deleteFiles;
            window.OnCancel = leaveFiles;
            window.Show();
        } else {
            if (cleanupFilesPending && setCleanupFilesPending) CleanupFilesPending = true;
            complete();
        }

        if (!Refreshing) {
            // If for some reason, another module caused compilation to occur then we'll postpone
            // notification until it's complete.
            NotifyWhenCompliationComplete(true);
        }
    }

    /// <summary>
    /// Go through all files with build targets that need to be enabled/disabled by renaming
    /// (can't be handled through PluginHandler) and enable/disable them based on the whether they
    /// should build on currentBuildTarget.
    /// </summary>
    /// <param name="currentBuildTarget">
    /// The BuildTarget to use to determine which files should be enabled/disabled.
    /// </param>
    public static void UpdateAssetsWithBuildTargets(BuildTarget currentBuildTarget) {
        string[] assets = SearchAssetDatabase(
                            assetsFilter: "l:" + FileMetadata.ASSET_LABEL_RENAME_TO_DISABLE);

        var metadataSet = FileMetadataSet.ParseFromFilenames(assets);
        foreach (var versionPair in metadataSet.Values) {
            // Disable all but the most recent version of the file.
            // Enable / disable most recent version based on whether its targets contain
            // the currently selected build target.
            foreach (var versionData in versionPair.Values) {
                bool enabled = false;
                if (versionData == versionPair.MostRecentVersion) {
                    Log(String.Format(
                        "{0}: editor enabled {1}, build targets [{2}] (current target {3})",
                        versionData.filename, versionData.GetEditorEnabled(),
                        String.Join(
                            ", ",
                            (new List<string>(versionData.GetBuildTargetStrings())).ToArray()),
                        currentBuildTarget),
                        level: LogLevel.Verbose);
                    if (versionData.GetBuildTargetsSpecified() && !versionData.GetEditorEnabled()) {
                        var buildTargets = versionData.GetBuildTargets();
                        enabled = buildTargets.Contains(currentBuildTarget);
                    } else {
                        enabled = true;
                    }
                }

                SetFileEnabledByRename(versionData, enabled);
            }
        }

    }

    /// <summary>
    /// Enable or disable a file by renaming it (changing its extension to/from .xxx_DISABLED).
    /// </summary>
    /// <param name="metadata"> The metadata for the file which should be changed. </param>
    /// <param name="enabled"> The new state of the file. </param>
    private static void SetFileEnabledByRename(FileMetadata metadata, bool enabled) {
        if (!RenameToDisableFilesEnabled)
            return;

        string disableToken = "_DISABLED";

        var filename = metadata.filename;
        var newFilename = filename;
        if (enabled && filename.EndsWith(disableToken)) {
            int tokenIndex = filename.LastIndexOf(disableToken);
            if (tokenIndex > 0) {
                newFilename = filename.Substring(0, tokenIndex);
            }
        } else if (!enabled && !filename.EndsWith(disableToken)) {
            newFilename = filename + disableToken;
        }

        if (filename == newFilename || newFilename == "") {
            return;
        }

        Log("Renaming file " + filename + " to " + newFilename, verbose: true);
        bool movedAsset = true;
        if (File.Exists(newFilename)) {
            movedAsset = AssetDatabase.MoveAssetToTrash(newFilename);
        }
        string moveError = AssetDatabase.MoveAsset(filename, newFilename);
        movedAsset &= String.IsNullOrEmpty(moveError);
        if (!movedAsset) {
            Log(String.Format("Unable to disable {0} ({1})", filename, moveError),
                level: LogLevel.Error);
        }
    }

    // Returns the major/minor version of the unity environment we are running in
    // as a float so it can be compared numerically.
    public static float GetUnityVersionMajorMinor() {
        return ExecutionEnvironment.VersionMajorMinor;
    }

    // ID of the scheduled job which performs an update.
    private static int updateVersionedAssetsJob = 0;

    /// <summary>
    /// Cancel the update versioned assets job.
    /// </summary>
    private static void CancelUpdateVersionedAssets() {
        if (updateVersionedAssetsJob > 0) {
            RunOnMainThread.Cancel(updateVersionedAssetsJob);
            updateVersionedAssetsJob = 0;
        }
    }

    /// <summary>
    /// Scanned for versioned assets and apply modifications if required.
    /// </summary>
    private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromPath) {
        ManifestReferences.FlushCaches();
        if (Enabled) {
            const double UpdateDelayInMiliseconds = 2000;
            CancelUpdateVersionedAssets();
            updateVersionedAssetsJob =
                RunOnMainThread.Schedule(() => {
                        UpdateVersionedAssets();
                    },
                    UpdateDelayInMiliseconds);
        }
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
                LogToFile = logger.Level == LogLevel.Verbose;
                Log(String.Format(".NET framework version changed from {0} to {1}\n",
                                  currentDotNetVersion, newDotNetVersion));
                currentDotNetVersion = FileMetadata.ScriptingRuntimeDotNetVersion;
                UpdateVersionedAssets(forceUpdate: true);
                LogToFile = logToFilePrevious;
            }
            return paths;
        }
    }

    /// <summary>
    /// Update assets when BuildTarget changes.
    /// </summary>
    [InitializeOnLoad]
    internal class BuildTargetChecker {
        private const double POLL_INTERVAL_MILLISECONDS = 1000.0f;
        private static BuildTarget? lastKnownBuildTarget = null;

        static BuildTargetChecker() {
            RunOnMainThread.Run(HandleSettingsChanged, runNow: false);
        }

        // NOTE: This should only be called from the main thread.
        public static void HandleSettingsChanged() { CheckBuildTarget(); }

        private static void CheckBuildTarget() {
            var newBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            if (lastKnownBuildTarget == null || newBuildTarget != lastKnownBuildTarget) {
                lastKnownBuildTarget = newBuildTarget;
                HandleBuildTargetChanged(newBuildTarget);
            }

            // Disable callback queue in non-interactive (batch) mode and when
            // -executeMethod is specified on the command line.
            // In batch mode, everything is executed in a single thread. So there
            // is no way for VersionHandler to gain control after its initially called.
            // -executeMethod doesn't trigger a reliable EditorApplication.update
            // event which can cause the queue to grow unbounded, possibly freezing Unity.
            if (Enabled && RenameToDisableFilesEnabled &&
                !ExecutionEnvironment.InBatchMode &&
                !ExecutionEnvironment.ExecuteMethodEnabled) {
                RunOnMainThread.Schedule(CheckBuildTarget, POLL_INTERVAL_MILLISECONDS);
            }
        }

        private static void HandleBuildTargetChanged(BuildTarget newBuildTarget) {
            UpdateAssetsWithBuildTargets(newBuildTarget);
        }

    }
}

} // namespace Google

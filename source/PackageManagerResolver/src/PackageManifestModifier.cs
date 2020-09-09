// <copyright file="PackageManifestModifier.cs" company="Google LLC">
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

using EDMInternal.MiniJSON;
using System;
using System.IO;
using System.Collections.Generic;

namespace Google {

internal class PackageManifestModifier {

    /// <summary>
    /// Thrown if an error occurs while parsing the manifest.
    /// </summary>
    internal class ParseException : Exception {

        /// <summary>
        /// Construct an exception with a message.
        /// </summary>
        public ParseException(string message) : base(message) {}
    }

    /// <summary>
    /// Relative location of the manifest file from project root.
    /// </summary>
    internal const string MANIFEST_FILE_PATH = "Packages/manifest.json";

    /// <summary>
    /// JSON keys to be used in manifest.json
    /// manifest.json expects scoped registries to be specified in the following format:
    ///   {
    ///     "scopedRegistries" : [
    ///       {
    ///          "name": "Registry Name",
    ///          "url": "https://path/to/registry",
    ///          "scopes": [ "com.company.scope" ]
    ///       }
    ///     ]
    ///   }
    /// </summary>
    private const string MANIFEST_SCOPED_REGISTRIES_KEY = "scopedRegistries";
    private const string MANIFEST_REGISTRY_NAME_KEY = "name";
    private const string MANIFEST_REGISTRY_URL_KEY = "url";
    private const string MANIFEST_REGISTRY_SCOPES_KEY = "scopes";

    /// <summary>
    /// Logger for this object.
    /// </summary>
    public Google.Logger Logger;

    /// <summary>
    /// Parsed manifest data from the manifest JSON file.
    /// </summary>
    internal Dictionary<string, object> manifestDict = null;

    /// <summary>
    /// Construct an object to modify manifest file.
    /// </summary>
    public PackageManifestModifier() { Logger = new Google.Logger(); }

    /// <summary>
    /// Construct an object based on another modifier.
    /// </summary>
    public PackageManifestModifier(PackageManifestModifier other) {
        Logger = other.Logger;
        try {
            manifestDict = Json.Deserialize(other.GetManifestJson()) as Dictionary<string, object>;
        } catch (Exception e) {
            Logger.Log(String.Format("Failed to clone PackageManifestModifier. \nException:{1}",
                MANIFEST_FILE_PATH, e.ToString()), LogLevel.Error);
        }
    }

    /// <summary>
    /// Read manifest from the file and parse JSON string into a dictionary.
    /// </summary>
    /// <returns>True if read and parsed successfully.</returns>
    internal bool ReadManifest() {
        string manifestText;
        try {
            manifestText = File.ReadAllText(MANIFEST_FILE_PATH);
        } catch (Exception e) {
            Logger.Log(String.Format("Failed to read {0}. \nException:{1}",
                MANIFEST_FILE_PATH, e.ToString()), LogLevel.Error);
            return false;
        }
        try {
            manifestDict = Json.Deserialize(manifestText) as Dictionary<string,object>;
        } catch (Exception e) {
            Logger.Log(String.Format("Failed to parse {0}. \nException:{1}",
                MANIFEST_FILE_PATH, e.ToString()), LogLevel.Error);
            return false;
        }
        if (manifestDict == null) {
            Logger.Log(String.Format("Failed to read the {0} because it is empty or malformed.",
                MANIFEST_FILE_PATH), LogLevel.Error);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Try to get the scopedRegistries entry from the manifest.
    /// </summary>
    /// <returns>List of scoped registries if found, null otherwise.</returns>
    /// <exception>Throws ParseException if the manifest isn't loaded or there is an error parsing
    /// the manifest.</exception>
    private List<object> ScopedRegistries {
        get {
            object scopedRegistriesObj = null;
            if (manifestDict == null) {
                throw new ParseException(String.Format("'{0}' not loaded.", MANIFEST_FILE_PATH));
            } else if (manifestDict.TryGetValue(MANIFEST_SCOPED_REGISTRIES_KEY,
                                                out scopedRegistriesObj)) {
                var scopedRegistries = scopedRegistriesObj as List<object>;
                if (scopedRegistries == null) {
                    throw new ParseException(
                        String.Format("Found malformed '{0}' section in '{1}'.",
                                      MANIFEST_SCOPED_REGISTRIES_KEY, MANIFEST_FILE_PATH));
                }
                return scopedRegistries;
            }
            return null;
        }
    }


    /// <summary>
    /// Extract scoped registries from the parsed manifest.
    /// </summary>
    internal Dictionary<string, List<PackageManagerRegistry>> PackageManagerRegistries {
        get {
            var upmRegistries = new Dictionary<string, List<PackageManagerRegistry>>();
            List<object> scopedRegistries = null;
            try {
                scopedRegistries = ScopedRegistries;
            } catch (ParseException exception) {
                Logger.Log(exception.ToString(), level: LogLevel.Warning);
            }
            if (scopedRegistries == null) {
                Logger.Log(String.Format("No registries found in '{0}'", MANIFEST_FILE_PATH),
                           level: LogLevel.Verbose);
                return upmRegistries;
            }
            Logger.Log(String.Format("Reading '{0}' from '{1}'",
                                     MANIFEST_SCOPED_REGISTRIES_KEY, MANIFEST_FILE_PATH),
                       level: LogLevel.Verbose);
            foreach (var obj in scopedRegistries) {
                var registry = obj as Dictionary<string, object>;
                if (registry == null) continue;
                string name = null;
                string url = null;
                var scopes = new List<string>();
                object nameObj = null;
                object urlObj = null;
                object scopesObj;
                if (registry.TryGetValue(MANIFEST_REGISTRY_NAME_KEY, out nameObj)) {
                    name = nameObj as string;
                }
                if (registry.TryGetValue(MANIFEST_REGISTRY_URL_KEY, out urlObj)) {
                    url = urlObj as string;
                }
                if (registry.TryGetValue(MANIFEST_REGISTRY_SCOPES_KEY,
                                         out scopesObj)) {
                    List<object> scopesObjList = scopesObj as List<object>;
                    if (scopesObjList != null && scopesObjList.Count > 0) {
                        foreach (var scopeObj in scopesObjList) {
                            string scope = scopeObj as string;
                            if (!String.IsNullOrEmpty(scope)) scopes.Add(scope);
                        }
                    }
                }
                var upmRegistry = new PackageManagerRegistry() {
                    Name = name,
                    Url = url,
                    Scopes = scopes,
                    CustomData = registry,
                    CreatedBy = MANIFEST_FILE_PATH
                };
                if (!String.IsNullOrEmpty(name) &&
                    !String.IsNullOrEmpty(url) &&
                    scopes.Count > 0) {
                    List<PackageManagerRegistry> upmRegistryList;
                    if (!upmRegistries.TryGetValue(url, out upmRegistryList)) {
                        upmRegistryList = new List<PackageManagerRegistry>();
                        upmRegistries[url] = upmRegistryList;
                    }
                    upmRegistryList.Add(upmRegistry);
                    Logger.Log(String.Format("Read '{0}' from '{1}'",
                                             upmRegistry.ToString(), MANIFEST_FILE_PATH),
                               level: LogLevel.Verbose);
                } else {
                    Logger.Log(
                        String.Format("Ignoring malformed registry {0} in {1}",
                                      upmRegistry.ToString(), MANIFEST_FILE_PATH),
                        level: LogLevel.Warning);
                }

            }
            return upmRegistries;
        }
    }

    /// <summary>
    /// Add a scoped registries.
    /// </summary>
    /// <param name="registries">Registries to add to the manifest.</param>
    /// <returns>true if the registries are added to the manifest, false otherwise.</returns>
    internal bool AddRegistries(IEnumerable<PackageManagerRegistry> registries) {
        List<object> scopedRegistries;
        try {
            scopedRegistries = ScopedRegistries;
        } catch (ParseException exception) {
            Logger.Log(String.Format("{0}  Unable to add registries:\n",
                                     exception.ToString(),
                                     PackageManagerRegistry.ToString(registries)),
                       level: LogLevel.Error);
            return false;
        }
        if (scopedRegistries == null) {
            scopedRegistries = new List<object>();
            manifestDict[MANIFEST_SCOPED_REGISTRIES_KEY] = scopedRegistries;
        }
        RemoveRegistries(registries, displayWarning: false);
        foreach (var registry in registries) {
            scopedRegistries.Add(new Dictionary<string, object>() {
                                     { MANIFEST_REGISTRY_NAME_KEY, registry.Name },
                                     { MANIFEST_REGISTRY_URL_KEY, registry.Url },
                                     { MANIFEST_REGISTRY_SCOPES_KEY, registry.Scopes }
                                 });
        }
        return true;
    }

    /// <summary>
    /// Remove all scoped registries in the given list.
    /// </summary>
    /// <param name="registries">A list of scoped registry to be removed</param>
    /// <param name="displayWarning">Whether to display a warning if specified registries were not
    /// found.</param>
    /// <returns>true if the registries could be removed, false otherwise.</returns>
    internal bool RemoveRegistries(IEnumerable<PackageManagerRegistry> registries,
                                   bool displayWarning = true) {
        List<object> scopedRegistries = null;
        try {
            scopedRegistries = ScopedRegistries;
        } catch (ParseException exception) {
            Logger.Log(String.Format("{0}  Unable to remove registries:\n", exception.ToString(),
                                     PackageManagerRegistry.ToString(registries)),
                       level: LogLevel.Error);
            return false;
        }
        int removed = 0;
        int numberOfRegistries = 0;
        var scopedRegistriesByUrl = PackageManagerRegistries;
        foreach (var registry in registries) {
            numberOfRegistries ++;
            List<PackageManagerRegistry> existingRegistries;
            if (scopedRegistriesByUrl.TryGetValue(registry.Url, out existingRegistries)) {
                int remaining = existingRegistries.Count;
                foreach (var existingRegistry in existingRegistries) {
                    if (scopedRegistries.Remove(existingRegistry.CustomData)) {
                        remaining --;
                    } else {
                        Logger.Log(String.Format("Failed to remove registry '{0}' from '{1}'",
                                                 existingRegistry, MANIFEST_FILE_PATH),
                                   level: LogLevel.Error);
                    }
                }
                if (remaining == 0) removed ++;
            }
        }
        if (displayWarning) {
            Logger.Log(String.Format("Removed {0}/{1} registries from '{2}'",
                                     removed, numberOfRegistries, MANIFEST_FILE_PATH),
                       level: removed == numberOfRegistries ? LogLevel.Verbose : LogLevel.Warning);
        }
        return removed == numberOfRegistries;
    }

    /// <summary>
    /// Write the dictionary to manifest file.
    /// </summary>
    /// <returns>True if serialized and wrote successfully.</returns>
    internal bool WriteManifest() {
        if (manifestDict == null) {
            Logger.Log(String.Format("No manifest to write to '{0}'", MANIFEST_FILE_PATH),
                       level: LogLevel.Error);
            return false;
        }
        try {
            string manifestText = GetManifestJson();

            if (!String.IsNullOrEmpty(manifestText)) {
                File.WriteAllText(MANIFEST_FILE_PATH, manifestText);
            }
        } catch (Exception e) {
            Logger.Log(
                String.Format("Failed to write to {0}. \nException:{1}",
                              MANIFEST_FILE_PATH, e.ToString()),
                level: LogLevel.Error);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Get the manifest json string.
    /// </summary>
    /// <returns>Manifest json string.</returns>
    internal string GetManifestJson() {
        return manifestDict != null ?
                Json.Serialize(manifestDict, humanReadable: true, indentSpaces: 2) :
                "";
    }
}
} // namespace Google

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
using System.Text;

namespace Google {

internal class PackageManifestModifier {
    /// <summary>
    /// Scoped Registry Name for Game Package Registry
    /// </summary>
    internal const string GOOGLE_REGISTRY_NAME = "Game Package Registry by Google";

    /// <summary>
    /// Scoped Registry URL for Game Package Registry
    /// </summary>
    internal const string GOOGLE_REGISTRY_URL = "https://unityregistry-pa.googleapis.com";

    /// <summary>
    /// Scoped Registry scopes for Game Package Registry
    /// </summary>
    internal static readonly List<object> GOOGLE_REGISTRY_SCOPES = new List<object>(){
        "com.google"
    };

    /// <summary>
    /// Relative location of the manifest file from project root.
    /// </summary>
    internal const string MANIFEST_FILE_PATH = "Packages/manifest.json";

    /// <summary>
    /// Logger for this object.
    /// </summary>
    public Google.Logger Logger;

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
    /// Parsed manifest data from the manifest JSON file.
    /// </summary>
    internal Dictionary<string, object> manifestDict;

    /// <summary>
    /// Construct an object to modify manifest file.
    /// </summary>
    public PackageManifestModifier() {Logger = new Google.Logger();}

    /// <summary>
    /// Read manifest from the file and parse JSON string into a dictionary.
    /// </summary>
    /// <return>True if read and parsed successfully.</return>
    internal bool ReadManifest() {
        try {
            string manifestText = File.ReadAllText(MANIFEST_FILE_PATH);

            manifestDict = Json.Deserialize(manifestText) as Dictionary<string,object>;
        } catch (Exception e) {
            Logger.Log(String.Format("Failed to read the {0}. \nException:{1}",
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
    /// Search all scoped registries using the given url.
    /// </summary>
    /// <para name="searchUrl">Url for searching</para>
    /// <returns>A list of found scoped registries</returns>
    internal List<Dictionary<string, object>> SearchRegistries(string searchUrl) {
        List<Dictionary<string, object>> foundRegistries = new List<Dictionary<string, object>>();
        object scopedRegistriesObj;
        if (manifestDict.TryGetValue(MANIFEST_SCOPED_REGISTRIES_KEY, out scopedRegistriesObj)){
            var scopedRegistries = scopedRegistriesObj as List<object>;
            if (scopedRegistries != null) {
                foreach (var obj in scopedRegistries) {
                    var registry = obj as Dictionary<string, object>;
                    object urlObj;
                    if (registry.TryGetValue(MANIFEST_REGISTRY_URL_KEY, out urlObj)) {
                        var url = urlObj as string;
                        if (url != null && String.Compare(url, searchUrl) == 0) {
                            foundRegistries.Add(registry);
                        }
                    }
                }
            }
        }
        return foundRegistries;
    }

    /// <summary>
    /// Add a scoped registries.
    /// </summary>
    /// <para name="name">Name of the scoped registry</para>
    /// <para name="url">Url of the scoped registry</para>
    /// <para name="scopes">A list of scopes of the scoped registry</para>
    internal void AddRegistry(string name, string url, List<object> scopes) {
        Dictionary<string, object> registry = new Dictionary<string, object>() {
            { MANIFEST_REGISTRY_NAME_KEY, name },
            { MANIFEST_REGISTRY_URL_KEY, url },
            { MANIFEST_REGISTRY_SCOPES_KEY, scopes }
        };

        object scopedRegistriesObj;

        if (!manifestDict.TryGetValue(MANIFEST_SCOPED_REGISTRIES_KEY, out scopedRegistriesObj)) {
            scopedRegistriesObj = new List<object>();
        }
        var scopedRegistries = scopedRegistriesObj as List<object>;
        if (scopedRegistries != null) {
            scopedRegistries.Add(registry);
        } else {
            Logger.Log(String.Format(
                "Cannot add registry {0} (url: {1}) because \"scopedRegistries\" in manifest.json" +
                " is not a list.", name, url), LogLevel.Error);
        }
        manifestDict[MANIFEST_SCOPED_REGISTRIES_KEY] = scopedRegistries;
    }

    /// <summary>
    /// Remove all scoped registries in the given list.
    /// </summary>
    /// <para name="registries">A list of scoped registry to be removed</para>
    internal void RemoveRegistries(List<Dictionary<string, object>> registries) {
        object scopedRegistriesObj;
        if (!manifestDict.TryGetValue(MANIFEST_SCOPED_REGISTRIES_KEY, out scopedRegistriesObj)) {
            var scopedRegistries = scopedRegistriesObj as List<object>;
            if (scopedRegistries != null) {
                foreach (var registry in registries) {
                    scopedRegistries.Remove(registry);
                }
                if (scopedRegistries.Count == 0) {
                    manifestDict.Remove(MANIFEST_SCOPED_REGISTRIES_KEY);
                }
            } else {
                Logger.Log(
                    String.Format("Cannot remove registries because \"{0}\" in {1} is not a list.",
                        MANIFEST_SCOPED_REGISTRIES_KEY, MANIFEST_FILE_PATH),LogLevel.Error);
            }
        } else {
            Logger.Log(
                String.Format("Cannot remove registries because \"{0}\" is not in {1}.",
                    MANIFEST_SCOPED_REGISTRIES_KEY, MANIFEST_FILE_PATH),LogLevel.Error);
        }
    }

    /// <summary>
    /// Write the dictionary to manifest file.
    /// </summary>
    /// <return>True if serialized and wrote successfully.</return>
    internal bool WriteManifest() {
        try {
            string manifestText =
                Json.Serialize(manifestDict, humanReadable: true, indentSpaces: 2);

            if (!String.IsNullOrEmpty(manifestText)) {
                File.WriteAllText(MANIFEST_FILE_PATH, manifestText);
            }
        } catch (Exception e) {
            Logger.Log(
                String.Format("Failed to write to {0}. \nException:{1}",
                    MANIFEST_FILE_PATH, e.ToString()),
                LogLevel.Error);
            return false;
        }
        return true;
    }
}
} // namespace Google

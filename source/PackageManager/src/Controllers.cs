// <copyright file="Controllers.cs" company="Google Inc.">
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
namespace Google.PackageManager {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Xml.Serialization;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Unity editor prefs abstraction.
    /// </summary>
    public interface IEditorPrefs {
        void DeleteAll();
        void DeleteKey(string key);
        bool GetBool(string key, bool defaultValue = false);
        float GetFloat(string key, float defaultValue = 0.0F);
        int GetInt(string key, int defaultValue = 0);
        string GetString(string key, string defaultValue = "");
        bool HasKey(string key);
        void SetBool(string key, bool value);
        void SetFloat(string key, float value);
        void SetInt(string key, int value);
        void SetString(string key, string value);
    }

    /// <summary>
    /// Unity editor prefs implementation. The reason this exists is to allow for
    /// the separation of UnityEditor calls from the controllers. Used to support
    /// testing and enforce cleaner separations.
    /// </summary>
    public class UnityEditorPrefs : IEditorPrefs {
        public void DeleteAll() {
            EditorPrefs.DeleteAll();
        }

        public void DeleteKey(string key) {
            EditorPrefs.DeleteKey(key);
        }

        public bool GetBool(string key, bool defaultValue = false) {
            return EditorPrefs.GetBool(key, defaultValue);
        }

        public float GetFloat(string key, float defaultValue = 0) {
            return EditorPrefs.GetFloat(key, defaultValue);
        }

        public int GetInt(string key, int defaultValue = 0) {
            return EditorPrefs.GetInt(key, defaultValue);
        }

        public string GetString(string key, string defaultValue = "") {
            return EditorPrefs.GetString(key, defaultValue);
        }

        public bool HasKey(string key) {
            return EditorPrefs.HasKey(key);
        }

        public void SetBool(string key, bool value) {
            EditorPrefs.SetBool(key, value);
        }

        public void SetFloat(string key, float value) {
            EditorPrefs.SetFloat(key, value);
        }

        public void SetInt(string key, int value) {
            EditorPrefs.SetInt(key, value);
        }

        public void SetString(string key, string value) {
            EditorPrefs.SetString(key, value);
        }
    }

    /// <summary>
    /// UnityController acts as a wrapper around Unity APIs that other controllers use.
    /// This is useful during testing as it allows separation of concerns.
    /// </summary>
    public static class UnityController {
        public static IEditorPrefs editorPrefs { get; private set; }
        static UnityController() {
            editorPrefs = new UnityEditorPrefs();
        }
        /// <summary>
        /// Swaps the editor prefs. Exposed for testing.
        /// </summary>
        /// <param name="newEditorPrefs">New editor prefs.</param>
        public static void SwapEditorPrefs(IEditorPrefs newEditorPrefs) {
            editorPrefs = newEditorPrefs;
        }
    }

    /// <summary>
    /// Helper class for interfacing with package manager specific settings.
    /// </summary>
    public static class SettingsController {
        /// <summary>
        /// Location on filesystem where downloaded packages are stored.
        /// </summary>
        public static string DownloadCachePath {
            get {
                return UnityController.editorPrefs.GetString(Constants.KEY_DOWNLOAD_CACHE,
                                             GetDefaultDownloadPath());
            }
            set {
                if (Directory.Exists(value)) {
                    UnityController.editorPrefs.SetString(Constants.KEY_DOWNLOAD_CACHE, value);
                } else {
                    throw new Exception("Download Cache location does not exist: " +
                                        value);
                }
            }
        }
        /// <summary>
        /// The verbose logging. TODO(krispy): make visible in UI.
        /// </summary>
        public static bool VerboseLogging {
            get {
                return UnityController.editorPrefs.GetBool(
                    Constants.VERBOSE_PACKAGE_MANANGER_LOGGING_KEY, true);
            }
            set {
                UnityController.editorPrefs.SetBool(
                    Constants.VERBOSE_PACKAGE_MANANGER_LOGGING_KEY, value);
            }
        }
        /// <summary>
        /// Determines if the user should be able to see the plugin package files
        /// before installing a plugin. TODO(krispy): make visible in UI.
        /// </summary>
        public static bool ShowInstallFiles {
            get {
                return UnityController.editorPrefs.GetBool(Constants.SHOW_INSTALL_ASSETS_KEY,
                                           true);
            }
            set {
                UnityController.editorPrefs.SetBool(Constants.SHOW_INSTALL_ASSETS_KEY, value);
            }
        }

        /// <summary>
        /// Gets the default download cache path. This is where plugin packages
        /// are downloaded before installation.
        /// </summary>
        /// <returns>The default download path.</returns>
        static string GetDefaultDownloadPath() {
            return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
                                Constants.GPM_CACHE_NAME);
        }
    }

    public enum ResponseCode {
        REGISTRY_ALREADY_PRESENT,
        REGISTRY_ADDED,
        REGISTRY_REMOVED,
        REGISTRY_NOT_FOUND,
        PLUGIN_RESOLVED,
        PLUGIN_METADATA_FAILURE,
        FETCH_ERROR,
        FETCH_TIMEOUT,
        FETCH_COMPLETE,
        XML_INVALID,
        URI_INVALID,
    }

    public interface IUriDataFetcher {
        ResponseCode BlockingFetchAsString(Uri uri, out string result);
    }

    /// <summary>
    /// URI fetcher. TODO(krispy): investigate using external fetch through compiled python lib
    /// </summary>
    public class UriFetcher : IUriDataFetcher {
        public ResponseCode BlockingFetchAsString(Uri uri, out string result) {
            // fetch uri to test its validity inflate it to a registry object
            var www = new WWW(uri.AbsoluteUri);
            // TODO(krispy): add timout check
            while (www.error == null && !www.isDone) { }
            if (www.error != null) {
                result = www.error;
                return ResponseCode.FETCH_ERROR;
            }
            result = www.text;
            return ResponseCode.FETCH_COMPLETE;
        }
    }

    public static class UriDataFetchController {
        public static IUriDataFetcher uriFetcher = new UriFetcher();
        public static void SwapUriDataFetcher(IUriDataFetcher newFetcher) {
            uriFetcher = newFetcher;
        }
    }

    /// <summary>
    /// Some constants are inconvenent for testing purposes. This class wraps the constants that
    /// may be changed during test execution.
    /// </summary>
    public static class TestableConstants {
        public static bool testcase = false;
        static string debugDefaultRegistryLocation = Constants.DEFAULT_REGISTRY_LOCATION;
        public static string DefaultRegistryLocation {
            get {
                return debugDefaultRegistryLocation;
            }
            set {
                if (testcase) {
                    debugDefaultRegistryLocation = value;
                }
            }
        }
    }

    /// <summary>
    /// Registry wrapper for convenience.
    /// </summary>
    public class RegistryWrapper {
        /// <summary>
        /// Gets or sets the location of the Registry held in Model
        /// </summary>
        /// <value>The location.</value>
        public Uri Location { get; set; }
        /// <summary>
        /// Gets or sets the model which is the actual registry.
        /// </summary>
        /// <value>The model.</value>
        public Registry Model { get; set; }
    }

    /// <summary>
    /// Registry manager controller responsible for adding/removing known registry locations. The
    /// set of known registries is available across all Unity projects.
    /// </summary>
    public static class RegistryManagerController {
        /// <summary>
        /// Registry database used to hold known registries, serializeable to xml.
        /// </summary>
        [XmlRoot("regdb")]
        public class RegistryDatabase : PackageManagerModel<RegistryDatabase> {
            [XmlArray("registries")]
            [XmlArrayItem("reg-uri-string")]
            public List<string> registryLocation = new List<string>();
        }

        static Dictionary<Uri, RegistryWrapper> regCache;
        static RegistryDatabase regDb;

        /// <summary>
        /// Initializes the <see cref="T:Google.PackageManager.RegistryManagerController"/> class.
        /// </summary>
        static RegistryManagerController() {
            _init();
        }

        public static void _init() {
            regCache = new Dictionary<Uri, RegistryWrapper>();
            string regDbXml = UnityController.editorPrefs.GetString(Constants.KEY_REGISTRIES, null);
            if (regDbXml == null || regDb == null) {
                regDb = new RegistryDatabase();
                AddRegistry(new Uri(TestableConstants.DefaultRegistryLocation));
            } else {
                var regLocs = new List<string>(regDb.registryLocation);
                foreach (var regUri in regLocs) {
                    AddRegistry(new Uri(regUri));
                }
            }
        }

        /// <summary>
        /// Attempts to add a registry to the known set of registries using the
        /// provided Uri. If the Uri turns out to be a registry location that is
        /// not already part of the known set of registries then it is added to
        /// the set of known registries. Once a valid registry Uri has been provided
        /// it will persist across Unity projects.
        /// </summary>
        /// <returns>A <see cref="T:Google.PackageManager.ResponseCode"/></returns>
        /// <param name="uri">URI.</param>
        public static ResponseCode AddRegistry(Uri uri) {
            if (regCache.ContainsKey(uri)) {
                return ResponseCode.REGISTRY_ALREADY_PRESENT;
            }

            string xmlData;
            ResponseCode rc = UriDataFetchController
                .uriFetcher.BlockingFetchAsString(uri, out xmlData);
            if (rc != ResponseCode.FETCH_COMPLETE) {
                return rc;
            }

            try {
                var reg = Registry.LoadFromString(xmlData);
                regCache[uri] = new RegistryWrapper { Location = uri, Model = reg };
                regDb.registryLocation.Add(uri.AbsoluteUri);
                var v = regDb.SerializeToXMLString();
                UnityController.editorPrefs.SetString(
                    Constants.KEY_REGISTRIES, v);
            } catch (Exception e) {
                Console.WriteLine(e);
                return ResponseCode.XML_INVALID;
            }

            return ResponseCode.REGISTRY_ADDED;
        }

        /// <summary>
        /// Attempts to remove the registry identified by the uri.
        /// </summary>
        /// <returns>A ResponseCode</returns>
        /// <param name="uri">URI.</param>
        public static ResponseCode RemoveRegistry(Uri uri) {
            if (uri == null) {
                return ResponseCode.URI_INVALID;
            }
            if (!regCache.ContainsKey(uri)) {
                return ResponseCode.REGISTRY_NOT_FOUND;
            }
            regCache.Remove(uri);
            regDb.registryLocation.Remove(uri.AbsoluteUri);
            var v = regDb.SerializeToXMLString();
            UnityController.editorPrefs.SetString(
                Constants.KEY_REGISTRIES, v);
            return ResponseCode.REGISTRY_REMOVED;
        }

        /// <summary>
        /// Gets all registries. Modifying the resulting list does not alter the set of known
        /// registries.
        /// </summary>
        /// <value>All known registries.</value>
        public static List<Registry> AllRegistries {
            get {
                var result = new List<Registry>();
                foreach (var v in regCache) {
                    result.Add(v.Value.Model);
                }
                return result;
            }
        }

        /// <summary>
        /// Gets all wrapped registries.
        /// </summary>
        /// <value>All wrapped registries.</value>
        public static List<RegistryWrapper> AllWrappedRegistries {
            get {
                var result = new List<RegistryWrapper>();
                foreach (var v in regCache) {
                    result.Add(v.Value);
                }
                return result;
            }
        }

        /// <summary>
        /// Gets the URI for registry.
        /// </summary>
        /// <returns>The URI for registry or null if not found.</returns>
        /// <param name="reg">Registry object</param>
        public static Uri GetUriForRegistry(Registry reg) {
            if (reg == null) {
                return null;
            }
            Uri result = null;
            foreach (var wrapper in regCache.Values) {
                if (wrapper.Model.GenerateUniqueKey().Equals(reg.GenerateUniqueKey())) {
                    result = wrapper.Location;
                    break;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Packaged plugin wrapper class for convenience.
    /// </summary>
    public class PackagedPlugin {
        /// <summary>
        /// Gets or sets the parent registry which is the owner of this plugin.
        /// </summary>
        /// <value>The parent registry.</value>
        public Registry ParentRegistry { get; set; }
        /// <summary>
        /// Gets or sets the meta data which holds the details about the plugin.
        /// </summary>
        /// <value>The meta data.</value>
        public PluginMetaData MetaData { get; set; }
        /// <summary>
        /// Gets or sets the description for the release version of the plugin.
        /// </summary>
        /// <value>The description.</value>
        public PluginDescription Description { get; set; }
        /// <summary>
        /// Gets or sets the location of where the MetaData came from.
        /// </summary>
        /// <value>The location.</value>
        public Uri Location { get; set; }
    }

    /// <summary>
    /// Plugin manager controller.
    /// </summary>
    public static class PluginManagerController {
        /// <summary>
        /// The plugin cache assiciates RegistryWrapper keys to lists of PackagedPlugins that are
        /// part of the set of plugins defined by the Registry.
        /// </summary>
        static Dictionary<RegistryWrapper, List<PackagedPlugin>> pluginCache;
        /// <summary>
        /// The plugin map is used to quickly lookup a specific plugin. It's key is a combination
        /// of the parent registry unique key and the plugin's unique key.
        /// eg. "com.google.unity.example:jarresolver-google-registry:0.0.1.1:com.google.unity
        ///     .example:gpm-example-plugin:1.0.0.0"
        /// </summary>
        static Dictionary<string, PackagedPlugin> pluginMap;

        static PluginManagerController() {
            pluginCache = new Dictionary<RegistryWrapper, List<PackagedPlugin>>();
            pluginMap = new Dictionary<string, PackagedPlugin>();
        }

        /// <summary>
        /// Gets the plugins for provided registry. May try to resolve the data for the registry
        /// plugin modules if registry has not been seen before or if the cache was flushed.
        /// </summary>
        /// <returns>The plugins for registry or null if there was a failure.</returns>
        /// <param name="regWrapper">RegistryWrapper</param>
        public static List<PackagedPlugin> GetPluginsForRegistry(RegistryWrapper regWrapper,
                                                                 bool refresh = false) {
            if (regWrapper == null) {
                return null;
            }
            List<PackagedPlugin> pluginList;
            if (pluginCache.TryGetValue(regWrapper, out pluginList) && !refresh) {
                // data available
                return pluginList;
            } else {
                // this is a refresh so we remove the stale data first
                pluginCache.Remove(regWrapper);
                if (pluginList != null) {
                    var regUnigueKey = regWrapper.Model.GenerateUniqueKey();
                    foreach (var plugin in pluginList) {
                        var pluginKey = plugin.MetaData.UniqueKey;
                        var pKey = string.Join(Constants.STRING_KEY_BINDER,
                                               new string[] { regUnigueKey, pluginKey });
                        pluginMap.Remove(pKey); // remove the plugin from the map if there
                    }
                }
            }
            // data needs to be resolved
            pluginList = new List<PackagedPlugin>();
            foreach (var moduleLoc in regWrapper.Model.modules.module) {
                // is the loc a remote or local ?
                Uri pluginModuleUri = ResolvePluginModuleUri(regWrapper, moduleLoc);
                PackagedPlugin plugin;
                ResponseCode rc = ResolvePluginDetails(pluginModuleUri, regWrapper.Model, out plugin);
                switch (rc) {
                case ResponseCode.PLUGIN_RESOLVED:
                    // resolved so we add it
                    pluginList.Add(plugin);
                    break;
                default:
                    // TODO(krispy): log this?
                    Console.WriteLine(rc);
                    break;
                }
            }
            pluginCache.Add(regWrapper, pluginList);
            var regKey = regWrapper.Model.GenerateUniqueKey();
            foreach (var plugin in pluginList) {
                var pluginKey = plugin.MetaData.UniqueKey;
                var pKey = string.Join(Constants.STRING_KEY_BINDER,
                                       new string[] { regKey, pluginKey });
                try {
                    pluginMap.Add(pKey, plugin); // add the plugin to the map
                } catch (Exception e) {
                    // this means that two registries have the same plugin...
                    // TODO(krispy): figure out how to deal with this - for now throw
                    throw new Exception(string.Format("COLLISION!:\n" +
                                                      "Exception: {0}\n" +
                                                      "Plugin Key: {1}\n" +
                                                      "Registry: {2}\n" +
                                                      "- you likely have two " +
                                                      "registries with the same plugin."
                                                      , e, pKey, regWrapper.Location));
                }
            }
            return pluginList;
        }

        /// <summary>
        /// Resolves the plugin module URI since it could be local relative to the registry or a
        /// fully qualified Uri.
        /// </summary>
        /// <returns>The plugin module URI.</returns>
        /// <param name="regWrapper">Reg wrapper.</param>
        /// <param name="moduleLoc">Module location.</param>
        static Uri ResolvePluginModuleUri(RegistryWrapper regWrapper, string moduleLoc) {
            Uri pluginModuleUri;
            try {
                pluginModuleUri = new Uri(moduleLoc);
            } catch {
                // if local then rewrite as remote relative
                pluginModuleUri = ChangeRegistryUriIntoModuleUri(regWrapper.Location, moduleLoc);
            }

            return pluginModuleUri;
        }

        /// <summary>
        /// Resolves the plugin details. Is public in so that individual plugins can be re-resolved
        /// which is needed in some situations.
        /// </summary>
        /// <returns>The plugin details.</returns>
        /// <param name="uri">URI.</param>
        /// <param name="parent">Parent.</param>
        /// <param name="plugin">Plugin.</param>
        public static ResponseCode ResolvePluginDetails(Uri uri, Registry parent,
                                                 out PackagedPlugin plugin) {
            string xmlData;
            ResponseCode rc = UriDataFetchController
                .uriFetcher.BlockingFetchAsString(uri, out xmlData);
            if (rc != ResponseCode.FETCH_COMPLETE) {
                plugin = null;
                return rc;
            }
            PluginMetaData metaData;
            PluginDescription description;
            try {
                metaData = PluginMetaData.LoadFromString(xmlData);
                Uri descUri = GenerateDescriptionUri(uri, metaData);
                rc = UriDataFetchController.uriFetcher.BlockingFetchAsString(descUri, out xmlData);
                if (rc != ResponseCode.FETCH_COMPLETE) {
                    plugin = null;
                    return rc;
                }
                description = PluginDescription.LoadFromString(xmlData);
            } catch (Exception e) {
                Console.WriteLine(e);
                plugin = null;
                return ResponseCode.PLUGIN_METADATA_FAILURE;
            }
            plugin = new PackagedPlugin {
                Description = description,
                Location = uri,
                MetaData = metaData,
                ParentRegistry = parent
            };
            return ResponseCode.PLUGIN_RESOLVED;
        }

        /// <summary>
        /// Generates the description URI.
        /// Public for testing.
        /// </summary>
        /// <returns>The description URI.</returns>
        /// <param name="pluginUri">Plugin URI.</param>
        /// <param name="metaData">Meta data.</param>
        public static Uri GenerateDescriptionUri(Uri pluginUri, PluginMetaData metaData) {
            var descUri = new Uri(Utility.GetURLMinusSegment(pluginUri.AbsoluteUri));
            descUri = new Uri(descUri, metaData.artifactId + "/");
            descUri = new Uri(descUri, metaData.versioning.release + "/");
            descUri = new Uri(descUri, Constants.DESCRIPTION_FILE_NAME);
            return descUri;
        }

        /// <summary>
        /// Changes the registry URI into module URI.
        /// Public for testing.
        /// </summary>
        /// <returns>A registry Uri related module URI.</returns>
        /// <param name="registryUri">Registry URI.</param>
        /// <param name="localModuleName">Local module name.</param>
        public static Uri ChangeRegistryUriIntoModuleUri(Uri registryUri, string localModuleName) {
            var pluginUri = new Uri(Utility.GetURLMinusSegment(registryUri.AbsoluteUri));
            pluginUri = new Uri(pluginUri, localModuleName + "/");
            pluginUri = new Uri(pluginUri, Constants.MANIFEST_FILE_NAME);
            return pluginUri;
        }

        /// <summary>
        /// Refresh the specified registry plugin data.
        /// </summary>
        /// <param name="regWrapper">RegistryWrapper with a valid registry.</param>
        public static void Refresh(RegistryWrapper regWrapper) {
            GetPluginsForRegistry(regWrapper, true);
        }
    }
}
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
    using JarResolver;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Logging controller used for logging messages. All member classes of PackageManager that
    /// need to log strings should use this controller as it is environment aware and can be used
    /// during testcase execution.
    /// </summary>
    public static class LoggingController {
        public static bool testing = false;
        /// <summary>
        /// Log the specified msg to the context appropriate console.
        /// </summary>
        /// <param name="msg">Message to write to console.</param>
        public static void Log(string msg) {
            if (!testing) {
                if (SettingsController.VerboseLogging) {
                    Debug.Log(msg);
                }
            } else {
                Console.WriteLine(msg);
            }
        }
        /// <summary>
        /// Logs the warning to the context appropriate console.
        /// </summary>
        /// <param name="msg">Message to write as a warning to console.</param>
        public static void LogWarning(string msg) {
            if (!testing) {
                if (SettingsController.VerboseLogging) {
                    Debug.LogWarning(msg);
                }
            } else {
                Console.WriteLine(msg);
            }
        }
        /// <summary>
        /// Logs the error to the context appropriate console
        /// </summary>
        /// <param name="msg">Message to write as an error to the console.</param>
        public static void LogError(string msg) {
            if (!testing) {
                Debug.LogError(msg);
            } else {
                Console.WriteLine(msg);
            }
        }
    }

    /// <summary>
    /// Unity editor prefs abstraction. Mirrors partial API of Unity EditorPrefs.
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
    /// Unity environment data abstraction interface to allow decoupling and
    /// module isolation.
    /// </summary>
    public interface IUnityEnvironmentData {
        string GetApplicationDataPath();
    }

    /// <summary>
    /// Unity environment data implementation used as default implementation.
    /// </summary>
    public class UnityEnvironmentData : IUnityEnvironmentData {
        public string GetApplicationDataPath() {
            return Application.dataPath;
        }
    }

    /// <summary>
    /// Unity editor prefs implementation. The reason this exists is to allow for
    /// the separation of UnityEditor calls from the controllers. Used to support
    /// testing and enforce cleaner separations. Mirrors partial API of Unity
    /// EditorPrefs class.
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
        public static IEditorPrefs EditorPrefs { get; private set; }
        public static IUnityEnvironmentData EnvironmentData { get; private set; }
        static UnityController() {
            EditorPrefs = new UnityEditorPrefs();
            EnvironmentData = new UnityEnvironmentData();
        }

        /// <summary>
        /// Swaps the environment data.
        /// </summary>
        /// <param name="newEnvData">New env data.</param>
        public static void SwapEnvironmentData(IUnityEnvironmentData newEnvData) {
            EnvironmentData = newEnvData;
        }

        /// <summary>
        /// Swaps the editor prefs. Exposed for testing.
        /// </summary>
        /// <param name="newEditorPrefs">New editor prefs.</param>
        public static void SwapEditorPrefs(IEditorPrefs newEditorPrefs) {
            EditorPrefs = newEditorPrefs;
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
                return UnityController.EditorPrefs.GetString(Constants.KEY_DOWNLOAD_CACHE,
                                             GetDefaultDownloadPath());
            }
            set {
                if (Directory.Exists(value)) {
                    UnityController.EditorPrefs.SetString(Constants.KEY_DOWNLOAD_CACHE, value);
                } else {
                    throw new Exception("Download Cache location does not exist: " +
                                        value);
                }
            }
        }
        /// <summary>
        /// Verbose logging propery flag.
        /// </summary>
        public static bool VerboseLogging {
            get {
                return UnityController.EditorPrefs.GetBool(
                    Constants.VERBOSE_PACKAGE_MANANGER_LOGGING_KEY, true);
            }
            set {
                UnityController.EditorPrefs.SetBool(
                    Constants.VERBOSE_PACKAGE_MANANGER_LOGGING_KEY, value);
            }
        }
        /// <summary>
        /// Determines if the user should be able to see the plugin package files
        /// before installing a plugin.
        /// </summary>
        public static bool ShowInstallFiles {
            get {
                return UnityController.EditorPrefs.GetBool(Constants.SHOW_INSTALL_ASSETS_KEY,
                                                           true);
            }
            set {
                UnityController.EditorPrefs.SetBool(Constants.SHOW_INSTALL_ASSETS_KEY, value);
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

    /// <summary>
    /// Response codes used by controllers in PackageManager.
    /// </summary>
    public enum ResponseCode {
        /// <summary>
        /// Registry already in memory
        /// </summary>
        REGISTRY_ALREADY_PRESENT,
        /// <summary>
        /// Registry was added to recorded preferences
        /// </summary>
        REGISTRY_ADDED,
        /// <summary>
        /// Registry was removed from recorded preferences
        /// </summary>
        REGISTRY_REMOVED,
        /// <summary>
        /// Registry that was requested could not be found
        /// </summary>
        REGISTRY_NOT_FOUND,
        /// <summary>
        /// Requested plugin install halted because it is already installed
        /// </summary>
        PLUGIN_ALREADY_INSTALLED,
        /// <summary>
        /// Could not process the plugin binary package
        /// </summary>
        PLUGIN_BINARY_ERROR,
        /// <summary>
        /// Plugin was successfully installed
        /// </summary>
        PLUGIN_INSTALLED,
        /// <summary>
        /// Plugin resolution (getting its data from source) succeded
        /// </summary>
        PLUGIN_RESOLVED,
        /// <summary>
        /// Plugin was successfully removed
        /// </summary>
        PLUGIN_REMOVED,
        /// <summary>
        /// Plugin metadata was not processed due to an error
        /// </summary>
        PLUGIN_METADATA_FAILURE,
        /// <summary>
        /// Requested plugin was not found based on information provided
        /// </summary>
        PLUGIN_NOT_FOUND,
        /// <summary>
        /// Plugin is not installed in the project
        /// </summary>
        PLUGIN_NOT_INSTALLED,
        /// <summary>
        /// Plugin removal halted because of an error
        /// </summary>
        PLUGIN_NOT_REMOVED,
        /// <summary>
        /// Fetch of data resulted in an error
        /// </summary>
        FETCH_ERROR,
        /// <summary>
        /// Fetch of data took too long and exceeded timout period
        /// </summary>
        FETCH_TIMEOUT,
        /// <summary>
        /// Fetch of data completed successfully
        /// </summary>
        FETCH_COMPLETE,
        /// <summary>
        /// The XML data caused an exception
        /// </summary>
        XML_INVALID,
        /// <summary>
        /// The URI value does not point to a reachable location or is not well formed
        /// </summary>
        URI_INVALID,
    }

    /// <summary>
    /// URI data fetcher interface.
    /// </summary>
    public interface IUriDataFetcher {
        /// <summary>
        /// Thread blocking fetch for a string result.
        /// </summary>
        /// <returns>The fetch as string.</returns>
        /// <param name="uri">URI.</param>
        /// <param name="result">Result string.</param>
        ResponseCode BlockingFetchAsString(Uri uri, out string result);
        /// <summary>
        /// Thead blocking fetch for a byte[] result.
        /// </summary>
        /// <returns>The fetch as bytes.</returns>
        /// <param name="uri">URI.</param>
        /// <param name="result">Result bytes.</param>
        ResponseCode BlockingFetchAsBytes(Uri uri, out byte[] result);
    }

    /// <summary>
    /// URI fetcher. b/34930031 investigate using external fetch through compiled python lib
    /// </summary>
    public class UriFetcher : IUriDataFetcher {
        byte[] bytesResult;
        string textResult;
        bool isForBytes = false;

        private ResponseCode DoBlockingFetch(Uri uri) {
            var www = new WWW(uri.AbsoluteUri);
            double startTime = EditorApplication.timeSinceStartup;
            while (www.error == null && !www.isDone) {
                var elapsed = EditorApplication.timeSinceStartup - startTime;
                if (elapsed > Constants.FETCH_TIMOUT_THRESHOLD) {
                    LoggingController.Log("Fetch threshold exceeded.");
                    return ResponseCode.FETCH_TIMEOUT;
                }
            }
            if (www.error != null) {
                LoggingController.Log(www.error);
                return ResponseCode.FETCH_ERROR;
            }
            if (isForBytes) {
                bytesResult = www.bytes;
            } else {
                textResult = www.text;
            }
            return ResponseCode.FETCH_COMPLETE;
        }

        /// <summary>
        /// Blocking fetch of URI where the expected result is returned as
        /// byte information.
        /// </summary>
        /// <returns>A byte array.</returns>
        /// <param name="uri">URI location to fetch from.</param>
        /// <param name="result">Result is the container that holds the result
        /// byte data.</param>
        public ResponseCode BlockingFetchAsBytes(Uri uri, out byte[] result) {
            isForBytes = true;
            ResponseCode rc = DoBlockingFetch(uri);
            result = bytesResult;
            return rc;
        }

        /// <summary>
        /// Blocking fetch of URI where the expected result is returned as a
        /// string.
        /// </summary>
        /// <returns>A string.</returns>
        /// <param name="uri">URI location to fetch from.</param>
        /// <param name="result">Result is the container that holds the result
        /// string data.</param>
        public ResponseCode BlockingFetchAsString(Uri uri, out string result) {
            ResponseCode rc = DoBlockingFetch(uri);
            result = textResult;
            return rc;
        }
    }

    /// <summary>
    /// Intermediator URI data fetch controller.
    /// </summary>
    public static class UriDataFetchController {
        public static IUriDataFetcher uriFetcher = new UriFetcher();
        /// <summary>
        /// Swaps the URI data fetcher with another instance. Used in testcases
        /// to setup deterministic execution.
        /// </summary>
        /// <param name="newFetcher">New fetcher.</param>
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

        /// <summary>
        /// Gets or sets the default registry location. Is environment context aware.
        /// </summary>
        /// <value>The default registry location.</value>
        public static string DefaultRegistryLocation {
            get {
                return debugDefaultRegistryLocation;
            }
            /// <summary>
            /// Sets the default registry location only if testing enabled.
            /// </summary>
            /// <param name="value">Value.</param>
            set {
                if (testcase) {
                    debugDefaultRegistryLocation = value;
                } else {
                    LoggingController.LogError(
                        "Attempted to set DefaultRegistryLocation outside of testcase.");
                }
            }
        }
    }

    /// <summary>
    /// Registry wrapper pairs Uri of registry with Registry object. This allows
    /// for a cleaner separation of model and logic. This makes it easier to key
    /// a specific Registry model object on its registered Uri.
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
            public HashSet<string> registryLocation = new HashSet<string>();
            [XmlElement("lastUpdate")]
            public string lastUpdate;
            [XmlIgnore]
            public Dictionary<Uri, RegistryWrapper> wrapperCache =
                new Dictionary<Uri, RegistryWrapper>();
        }

        /// <summary>
        /// The registry database of all recorded registry locations. Serialzed/Deserialized to
        /// editor prefs.
        /// </summary>
        static RegistryDatabase regDb;

        /// <summary>
        /// Initializes the <see cref="T:Google.PackageManager.RegistryManagerController"/> class.
        /// </summary>
        static RegistryManagerController() {
            LoadRegistryDatabase();
        }

        /// <summary>
        /// Saves the registry database to editor prefs.
        /// </summary>
        static void SaveRegistryDatabase() {
            if (regDb == null) {
                return;
            }
            regDb.lastUpdate = DateTime.UtcNow.ToString("o");
            var xml = regDb.SerializeToXMLString();
            UnityController.EditorPrefs.SetString(Constants.KEY_REGISTRIES, xml);
        }

        /// <summary>
        /// Inflates the registry database from Unity editor preferences key.
        /// Force reloading causes a new object instance to be created.
        /// </summary>
        /// <param name="forceReload">If set to <c>true</c> force reload.</param>
        public static void LoadRegistryDatabase(bool forceReload = false) {
            var existingRegDB = regDb;
            var regDbXml = UnityController.EditorPrefs.GetString(Constants.KEY_REGISTRIES, null);
            if ((regDbXml == null || regDbXml.Length == 0) && existingRegDB == null) {
                CreateRegistryDatabase();
            } else {
                var readRegDb = RegistryDatabase.LoadFromString(regDbXml);

                if (existingRegDB != null) {
                    // compare time stamps
                    var existingComparedToOther = Convert.ToDateTime(existingRegDB.lastUpdate)
                                   .CompareTo(Convert.ToDateTime(readRegDb.lastUpdate));
                    if (existingComparedToOther > 0 || forceReload) {
                        // The existing db is newer than the loaded one or we are forcing a refresh
                        // The source of truth is always what has been recorded to the editor pref.
                        regDb = readRegDb;
                    }
                } else {
                    regDb = readRegDb;
                }
            }
            if (regDb.registryLocation.Count == 0) {
                // really there is no good reason to have an empty registry database
                AddRegistry(new Uri(TestableConstants.DefaultRegistryLocation));
            }
            RefreshRegistryCache();
        }

        /// <summary>
        /// Creates a new registry database instance with a default registry.
        /// Will indirectly destroy existing registry database held in memory.
        /// </summary>
        static void CreateRegistryDatabase() {
            regDb = new RegistryDatabase();
            regDb.registryLocation.Add(TestableConstants.DefaultRegistryLocation);
            regDb.lastUpdate = DateTime.UtcNow.ToString("o");
        }

        /// <summary>
        /// Refreshs the registry cache for the provided RegistryWrapper.
        /// </summary>
        /// <param name="wrapper">Wrapper.</param>
        public static void RefreshRegistryCache(RegistryWrapper wrapper = null) {
            var regLocs = new List<string>(regDb.registryLocation);
            if (wrapper != null) {
                regLocs.Clear();
                regLocs.Add(wrapper.Location.AbsoluteUri);
            }

            foreach (var regUri in regLocs) {
                var uri = new Uri(regUri);
                string xmlData;
                ResponseCode rc = UriDataFetchController
                    .uriFetcher.BlockingFetchAsString(uri, out xmlData);
                if (rc != ResponseCode.FETCH_COMPLETE) {
                    LoggingController.LogError(
                    string.Format("Failed attempt to fetch {0} got response code {1}", regUri, rc));
                    continue;
                }
                try {
                    regDb.wrapperCache[uri] = new RegistryWrapper {
                        Location = uri,
                        Model = Registry.LoadFromString(xmlData)
                    };
                } catch (Exception e) {
                    LoggingController.LogError(
                        string.Format("EXCEPTION: {0} inflating Registry {1} using returned xml." +
                                      "\n\n{2}\n\n", e, regUri, xmlData));
                    continue;
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
            if (regDb != null && regDb.registryLocation.Contains(uri.AbsoluteUri)) {
                return ResponseCode.REGISTRY_ALREADY_PRESENT;
            }

            string xmlData;
            ResponseCode rc = UriDataFetchController
                .uriFetcher.BlockingFetchAsString(uri, out xmlData);
            if (rc != ResponseCode.FETCH_COMPLETE) {
                LoggingController.LogError(
                    string.Format("Attempted fetch of {0} got response code {1}",
                                  uri.AbsoluteUri, rc));
                return rc;
            }

            try {
                var reg = Registry.LoadFromString(xmlData);
                regDb.registryLocation.Add(uri.AbsoluteUri);
                SaveRegistryDatabase();
                regDb.wrapperCache[uri] = new RegistryWrapper { Location = uri, Model = reg };
                var xml = regDb.SerializeToXMLString();
                UnityController.EditorPrefs.SetString(Constants.KEY_REGISTRIES, xml);
            } catch (Exception e) {
                LoggingController.LogError(
                    string.Format("EXCEPTION Adding Registry {0}: \n\n{1}", uri.AbsoluteUri, e));
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
                LoggingController.LogWarning("Attempted to remove a registry with null uri.");
                return ResponseCode.URI_INVALID;
            }
            if (regDb == null || regDb.wrapperCache == null) {
                LoadRegistryDatabase();
            }

            if (!regDb.wrapperCache.ContainsKey(uri)) {
                LoggingController.LogWarning(
                    string.Format("No registry to remove at {0}. Not found in cache.",
                                  uri.AbsoluteUri));
                return ResponseCode.REGISTRY_NOT_FOUND;
            }
            regDb.wrapperCache.Remove(uri);
            regDb.registryLocation.Remove(uri.AbsoluteUri);
            SaveRegistryDatabase();
            var xml = regDb.SerializeToXMLString();
            UnityController.EditorPrefs.SetString(Constants.KEY_REGISTRIES, xml);
            return ResponseCode.REGISTRY_REMOVED;
        }

        /// <summary>
        /// Gets all wrapped registries.
        /// </summary>
        /// <value>All wrapped registries.</value>
        public static List<RegistryWrapper> AllWrappedRegistries {
            get {
                var result = new List<RegistryWrapper>();
                result.AddRange(regDb.wrapperCache.Values);
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
            foreach (var wrapper in regDb.wrapperCache.Values) {
                if (wrapper.Model.GenerateUniqueKey().Equals(reg.GenerateUniqueKey())) {
                    result = wrapper.Location;
                    break;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Packaged plugin wrapper class that binds multiple models togeather. This
    /// makes it easier to pass around a bundled representation of a packaged
    /// plugin.
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
        static Dictionary<RegistryWrapper, List<PackagedPlugin>> pluginCache =
            new Dictionary<RegistryWrapper, List<PackagedPlugin>>();
        /// <summary>
        /// The plugin map is used to quickly lookup a specific plugin using its unique key.
        /// </summary>
        static Dictionary<string, PackagedPlugin> pluginMap =
            new Dictionary<string, PackagedPlugin>();
        /// <summary>
        /// Versionless plugin map is used to quickly lookup a specific plugin using its group and
        /// artifact.
        /// </summary>
        static Dictionary<string, PackagedPlugin> versionlessPluginMap =
            new Dictionary<string, PackagedPlugin>();

        /// <summary>
        /// Creates the versionless key.
        /// </summary>
        /// <returns>The versionless key.</returns>
        /// <param name="plugin">A PackagedPlugin</param>
        public static string CreateVersionlessKey(PackagedPlugin plugin) {
            return string.Join(Constants.STRING_KEY_BINDER, new string[] {
                plugin.MetaData.groupId,
                plugin.MetaData.artifactId
            });
        }

        /// <summary>
        /// Changes a versioned plugin unique key into a versionless one.
        /// </summary>
        /// <returns>Versionless plugin key.</returns>
        /// <param name="versionedPluginKey">Versioned plugin key.</param>
        public static string VersionedPluginKeyToVersionless(string versionedPluginKey) {
            return versionedPluginKey
                .Substring(0, versionedPluginKey.LastIndexOf(Constants.STRING_KEY_BINDER));
        }

        /// <summary>
        /// Uses the versionless key to lookup and return a packaged plugin. If
        /// no matching packaged plugin exists then returns null. The format of
        /// the versionless key is "plugin-groupId:plugin-artifactId".
        /// </summary>
        /// <returns>The plugin for versionless key.</returns>
        /// <param name="versionlessKey">Versionless plugin key.</param>
        public static PackagedPlugin GetPluginForVersionlessKey(string versionlessKey) {
            PackagedPlugin plugin = null;
            versionlessPluginMap.TryGetValue(versionlessKey, out plugin);
            return plugin;
        }

        /// <summary>
        /// Gets the list of all plugins across all registered registries. If
        /// refresh is true then the plugin data returned is gaurenteed to be
        /// up to date since each plugin source data is fetched prior to this
        /// method returning.
        /// </summary>
        /// <returns>The list of all plugins.</returns>
        /// <param name="refresh">If set to <c>true</c> refresh.</param>
        public static List<PackagedPlugin> GetListOfAllPlugins(bool refresh = false) {
            var result = new List<PackagedPlugin>();
            foreach (var wr in RegistryManagerController.AllWrappedRegistries) {
                result.AddRange(GetPluginsForRegistry(wr, refresh));
            }
            return result;
        }

        /// <summary>
        /// Gets the plugins for provided registry. May try to resolve the data for the registry
        /// plugin modules if registry has not been seen before or if the cache was flushed.
        /// </summary>
        /// <returns>The plugins for registry or null if there was a failure.</returns>
        /// <param name="regWrapper">RegistryWrapper</param>
        public static List<PackagedPlugin> GetPluginsForRegistry(RegistryWrapper regWrapper,
                                                                 bool refresh = false) {
            var pluginList = new List<PackagedPlugin>();
            if (regWrapper == null) {
                return pluginList;
            }
            if (!refresh) { // Just return what ever is known currently.
                if (pluginCache.TryGetValue(regWrapper, out pluginList)) {
                    // data available
                    return pluginList;
                }
                // did not find anything for the registry so we need to return an empty list
                return new List<PackagedPlugin>();
            }

            PurgeFromCache(regWrapper);

            pluginList = new List<PackagedPlugin>();
            // now there is no trace of plugins from the registry - time to rebuild data from source
            // the module locations are known once a registry is resolved
            foreach (var module in regWrapper.Model.modules.module) {
                // Is the module a remote or local?
                Uri pluginModuleUri = ResolvePluginModuleUri(regWrapper, module);
                PackagedPlugin plugin;
                ResponseCode rc =
                    ResolvePluginDetails(pluginModuleUri, regWrapper.Model, out plugin);
                switch (rc) {
                case ResponseCode.PLUGIN_RESOLVED:
                    // resolved so we add it
                    pluginList.Add(plugin);
                    AddOrUpdatePluginMap(plugin);
                    AddOrUpdateVersionlessPluginMap(plugin);
                    break;
                default:
                    LoggingController.LogWarning(
                        string.Format("Plugin not resolved: {0} for uri {1}", rc, pluginModuleUri));
                    break;
                }
            }

            pluginCache[regWrapper] = pluginList;

            foreach (var plugin in pluginList) {
                var versionLessKey = CreateVersionlessKey(plugin);
                pluginMap[versionLessKey] = plugin;
            }
            return pluginList;
        }

        /// <summary>
        /// Purges all plugins belonging to the provided registry wrapper from controller cache.
        /// </summary>
        /// <param name="regWrapper">Registry wrapper that is the parent of all the plugins to
        /// purge from the controller cache.</param>
        static void PurgeFromCache(RegistryWrapper regWrapper) {
            List<PackagedPlugin> pluginList;
            // a refresh has been requested - fetch is implied
            // this is a refresh so we remove the stale data first
            pluginCache[regWrapper] = null;
            RegistryManagerController.RefreshRegistryCache(regWrapper);
            // purge the correct plugins from the cache
            if (pluginCache.TryGetValue(regWrapper, out pluginList)) {
                if (pluginList == null) {
                    pluginCache[regWrapper] = new List<PackagedPlugin>();
                } else {
                    foreach (var plugin in pluginList) {
                        pluginMap[CreateVersionlessKey(plugin)] = null;
                    }
                }
            }
        }

        /// <summary>
        /// Adds the plugin to or updates the versionless plugin map plugin reference.
        /// </summary>
        /// <param name="plugin">Plugin.</param>
        static void AddOrUpdateVersionlessPluginMap(PackagedPlugin plugin) {
            versionlessPluginMap[CreateVersionlessKey(plugin)] = plugin;
        }

        /// <summary>
        /// Adds the plugin to or updates the plugin map plugin reference.
        /// </summary>
        /// <param name="plugin">Plugin.</param>
        static void AddOrUpdatePluginMap(PackagedPlugin plugin) {
            pluginMap[plugin.MetaData.UniqueKey] = plugin;
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
        /// Generates the binary URI location based on the plugin meta data and source of the
        /// metadata.
        /// Public for testing.
        /// </summary>
        /// <returns>The binary package URI.</returns>
        /// <param name="pluginUri">Plugin metadata URI.</param>
        /// <param name="metaData">Meta data object for plugin.</param>
        public static Uri GenerateBinaryUri(Uri pluginUri, PluginMetaData metaData) {
            var descUri = new Uri(Utility.GetURLMinusSegment(pluginUri.AbsoluteUri));
            descUri = new Uri(descUri, metaData.artifactId + "/");
            descUri = new Uri(descUri, metaData.versioning.release + "/");
            descUri = new Uri(descUri, GenerateBinaryFilename(metaData));
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

        /// <summary>
        /// Generates a package binary filename for the plugin.
        /// </summary>
        /// <returns>The binary filename.</returns>
        /// <param name="plugin">Plugin.</param>
        public static string GenerateBinaryFilename(PluginMetaData plugin) {
            return string.Format("{0}.{1}", plugin.artifactId, plugin.packaging);
        }
    }

    /// <summary>
    /// Unity asset database proxy interface.
    /// </summary>
    public interface IUnityAssetDatabaseProxy {
        string[] GetAllAssetPaths();
        string[] GetLabelsForAssetAtPath(string path);
        string[] FindAssets(string filter);
        string GUIDToAssetPath(string guid);
        void SetLabels(string path, string[] labels);
        void ImportPackage(string packagePath, bool interactive);
        bool DeleteAsset(string path);
        string[] FindAssets(string filter, string[] searchInFolders);
        void Refresh(ImportAssetOptions options = ImportAssetOptions.Default);
    }

    /// <summary>
    /// Unity engine asset database proxy to intermediate Unity AssertDatabase.
    /// </summary>
    public class UnityEngineAssetDatabaseProxy : IUnityAssetDatabaseProxy {
        public bool DeleteAsset(string path) {
            return AssetDatabase.DeleteAsset(path);
        }

        public string[] FindAssets(string filter) {
            return AssetDatabase.FindAssets(filter);
        }

        public string[] FindAssets(string filter, string[] searchInFolders) {
            return AssetDatabase.FindAssets(filter, searchInFolders);
        }

        public string[] GetAllAssetPaths() {
            return AssetDatabase.GetAllAssetPaths();
        }

        public string[] GetLabelsForAssetAtPath(string path) {
            return AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(path));
        }

        public string GUIDToAssetPath(string guid) {
            return AssetDatabase.GUIDToAssetPath(guid);
        }

        public void ImportPackage(string packagePath, bool interactive) {
            AssetDatabase.ImportPackage(packagePath, interactive);
        }

        public void SetLabels(string path, string[] labels) {
            AssetDatabase.SetLabels(AssetDatabase.LoadMainAssetAtPath(path), labels);
        }

        public void Refresh(ImportAssetOptions options = ImportAssetOptions.Default) {
            AssetDatabase.Refresh();
        }
    }

    /// <summary>
    /// Asset database controller that acts as an intermediary. Test cases can swap out the backing
    /// databaseProxy instance.
    /// </summary>
    public static class AssetDatabaseController {
        public static bool ImportInitiatedFromController { get; private set; }
        static IUnityAssetDatabaseProxy databaseProxy = new UnityEngineAssetDatabaseProxy();
        public static void SwapDatabaseProxy(IUnityAssetDatabaseProxy newProxy) {
            databaseProxy = newProxy;
        }
        public static string[] GetAllAssetPaths() {
            return databaseProxy.GetAllAssetPaths();
        }
        public static string[] GetLabelsForAssetAtPath(string path) {
            return databaseProxy.GetLabelsForAssetAtPath(path);
        }
        public static string[] FindAssets(string filter) {
            return databaseProxy.FindAssets(filter);
        }
        public static string GUIDToAssetPath(string guid) {
            return databaseProxy.GUIDToAssetPath(guid);
        }
        public static void SetLabels(string path, string[] labels) {
            databaseProxy.SetLabels(path, labels);
        }
        public static void ImportPackage(string packagePath, bool interactive) {
            ImportInitiatedFromController = true;
            databaseProxy.ImportPackage(packagePath, interactive);
        }
        public static bool DeleteAsset(string path) {
            return databaseProxy.DeleteAsset(path);
        }
        public static string[] FindAssets(string filter, string[] searchInFolders) {
            return databaseProxy.FindAssets(filter, searchInFolders);
        }
        public static void Refresh() {
            databaseProxy.Refresh();
        }
        public static void ClearImportFlag() {
            ImportInitiatedFromController = false;
        }
    }

    /// <summary>
    /// Project manager controller handles actions related to a project like installing, removing
    /// plugins.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectManagerController {
        static readonly HashSet<string> allProjectAssetLabels = new HashSet<string>();
        static ProjectPackages gpmPackagesInProject;
        /// <summary>
        /// The project dirty flag - set if new plugin package installed or target platform changed
        /// </summary>
        static bool projectDirty = false;
        static BuildTarget currentBuildTarget;

        static ProjectManagerController() {
            gpmPackagesInProject = InflateProjectRecord(GetProjectRecordPath());
            EditorApplication.update += Update;
            currentBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            EnsurePluginsDirectory();
        }

        /// <summary>
        /// Unity editor will call this method during the Unity editor update loop.
        /// </summary>
        static void Update() {
            if (EditorUserBuildSettings.activeBuildTarget != currentBuildTarget) {
                currentBuildTarget = EditorUserBuildSettings.activeBuildTarget;
                EnsurePluginsDirectory();
                projectDirty = true;
            }

            if (projectDirty) {
                // check project for re-resolve on target if ditry
                RefreshProject();
            }
        }

        /// <summary>
        /// Ensures the plugins directory exists, creates if missing.
        /// </summary>
        static void EnsurePluginsDirectory() {
            if (!AssetDatabase.IsValidFolder("Assets/Plugins")) {
                AssetDatabase.CreateFolder("Assets", "Plugins");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Plugins/Android")) {
                AssetDatabase.CreateFolder("Assets/Plugins", "Android");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Plugins/IOS")) {
                AssetDatabase.CreateFolder("Assets/Plugins", "IOS");
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Removes the client from the project
        /// </summary>
        /// <param name="clientName">Client name.</param>
        static void RemoveClient(string clientName) {
            LoggingController.Log(
                string.Format("Removing Client for Key {0}", clientName));
            List<ProjectClient> clients = GetAllClients();
            if (clients == null) {
                LoggingController.LogError(
                string.Format("No Client for Key {0}", clientName));
                return;
            }
            ProjectClient removeMe = null;
            foreach (var client in clients) {
                if (client.GenerateUniqueKey().StartsWith(clientName)) {
                    removeMe = client;
                    LoggingController.Log(
                        string.Format("Found client object to remove {0}", removeMe));
                }
            }
            if (removeMe != null) {
                gpmPackagesInProject.clients.Remove(removeMe);
                WriteProjectPackages();
                LoggingController.Log(
                        string.Format("Removed client object for {0}", clientName));
            }
        }

        /// <summary>
        /// Refreshs the client, resolves it's dependencies if needed.
        /// </summary>
        /// <param name="client">Client.</param>
        static void RefreshClient(ProjectClient client) {
            switch (currentBuildTarget) {
            case BuildTarget.Android:
                if (!client.resolvedForAndroid) { // Android deps have not been resolved
                    EnsureAllAssetsLabeled(client.Name);
                    PlayServicesSupport support = null;
                    if (!PlayServicesSupport.instances.TryGetValue(client.Name, out support)) {
                        support = PlayServicesSupport.CreateInstance(client.Name,
                                               UnityController.EditorPrefs.GetString(
                                                   Constants.ANDROID_SDK_ROOT_PREF_KEY),
                                               Constants.PROJECT_SETTINGS_KEY);
                    }
                    foreach (var packageDep in client.clientDependencies.androidDependencies) {
                        string[] packageIdsArray = null;
                        if (packageDep.args != null && packageDep.args.packageIds != null) {
                            packageIdsArray = new string[packageDep.args.packageIds.Count];
                            packageDep.args.packageIds.CopyTo(packageIdsArray);
                        }
                        string[] repositories = null;
                        if (packageDep.args != null && packageDep.args.repositories != null) {
                            repositories = new string[packageDep.args.repositories.Count];
                            packageDep.args.repositories.CopyTo(repositories);
                        }
                        support.DependOn(packageDep.group,
                                         packageDep.artifact,
                                         packageDep.version,
                                         packageIdsArray,
                                         repositories);
                    }

                    var dependencySet = support.ResolveDependencies(true);
                    client.depNames.AddRange(dependencySet.Keys);
                    WriteProjectPackages();
                    EnsurePluginsDirectory();

                    try {
                        LoggingController.Log(
                            string.Format("About to resolve for client: {0}", client.Name));
                        GooglePlayServices.PlayServicesResolver.Resolver.DoResolution(support,
                            "Assets/Plugins/Android",
                            GooglePlayServices.PlayServicesResolver.HandleOverwriteConfirmation,
                            () => {
                                AssetDatabase.Refresh();
                                LoggingController.Log(
                                    string.Format("Android resolution complete for client: {0}",
                                                  client.Name));
                                client.resolvedForAndroid = true;
                                WriteProjectPackages();
                                // tag/label all dependency files additive tags if needed
                                EnsureLabeledDependencies(client.Name);
                            });
                    } catch (Exception e) {
                        LoggingController.LogError(
                            string.Format("EXCEPTION during Android resolve dependencies: {0}\n{1}",
                                          e, e.StackTrace));
                    }
                }
                break;
            case BuildTarget.iOS:
                // TODO: b/34936552 implement for iOS POD deps
                break;
            default:
                break;
            }
        }

        /// <summary>
        /// Ensures all assets belonging to a packaged plugin are labeled.
        /// </summary>
        /// <param name="clientName">Client name.</param>
        static void EnsureAllAssetsLabeled(string clientName) {
            var client = GetClientForKey(clientName);
            if (client == null) {
                return;
            }
            foreach (var assetPath in client.assets) {
                var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset != null) {
                    var existingLabels = AssetDatabase.GetLabels(asset);
                    var labelSet = new HashSet<string>();
                    labelSet.Add(Constants.GPM_LABEL_MARKER);
                    labelSet.Add(string.Join(Constants.STRING_KEY_BINDER, new string[] {
                            Constants.GPM_LABEL_MARKER,
                            Constants.GPM_LABEL_CLIENT,
                            clientName
                        }));
                    labelSet.Add(string.Join(Constants.STRING_KEY_BINDER, new string[] {
                            Constants.GPM_LABEL_MARKER,
                            Constants.GPM_LABEL_KEY,
                            clientName,
                            client.version
                        }));
                    labelSet.UnionWith(existingLabels);
                    var labels = new string[labelSet.Count];
                    labelSet.CopyTo(labels);
                    AssetDatabase.SetLabels(
                        asset,
                        labels);
                }
            }
        }

        /// <summary>
        /// Ensures the dependency assets are labeled for the client.
        /// </summary>
        /// <param name="clientName">Client name.</param>
        static void EnsureLabeledDependencies(string clientName) {
            var client = GetClientForKey(clientName);
            if (client == null) {
                return;
            }
            foreach (var depName in client.depNames) {
                switch (currentBuildTarget) {
                case BuildTarget.Android:
                    var name = depName.Substring(depName.IndexOf(':') + 1);
                    var assets = AssetDatabase.FindAssets(name, new string[] {
                        "Assets/Plugins/Android"});
                    if (assets.Length > 0) {
                        var asset = AssetDatabase.LoadMainAssetAtPath(
                            AssetDatabase.GUIDToAssetPath(assets[0]));
                        if (asset != null) {
                            var existingLabels = AssetDatabase.GetLabels(asset);
                            var labelSet = new HashSet<string>();
                            labelSet.Add(Constants.GPM_LABEL_MARKER);
                            labelSet.Add(string.Join(Constants.STRING_KEY_BINDER, new string[] {
                            Constants.GPM_LABEL_MARKER,
                            Constants.GPM_LABEL_CLIENT,
                            clientName
                        }));
                            labelSet.UnionWith(existingLabels);
                            var labels = new string[labelSet.Count];
                            labelSet.CopyTo(labels);
                            AssetDatabase.SetLabels(
                                asset,
                                labels);
                        }
                    }
                    break;
                case BuildTarget.iOS:
                    LoggingController.LogError("IOS EnsureLabeledDependencies Not Implemented.");
                    break;
                }
            }
        }

        /// <summary>
        /// Refreshes the project by reading project file, checking each client package to see if
        /// the current target platform has been resolved and if not then it resolves and updates
        /// the list of assets associated with the client.
        /// </summary>
        static void RefreshProject() {
            var clients = GetAllClients();
            foreach (var client in clients) {
                RefreshClient(client);
            }
        }

        /// <summary>
        /// Reloads the project packages from the xml file living above the Assets directory.
        /// </summary>
        static void ReloadProjectPackages() {
            gpmPackagesInProject = InflateProjectRecord(GetProjectRecordPath());
        }

        /// <summary>
        /// Writes the project packages to an xml file living above the Assets directory.
        /// </summary>
        static void WriteProjectPackages() {
            if (gpmPackagesInProject != null) {
                try {
                    File.WriteAllText(GetProjectRecordPath(),
                                      gpmPackagesInProject.SerializeToXMLString());
                } catch (Exception e) {
                    LoggingController.LogError(
                        string.Format("Could not write project file due to exception - {0}", e));
                }
            }
        }

        /// <summary>
        /// Gets the client for client name or creates new one.
        /// </summary>
        /// <returns>The client for client name.</returns>
        /// <param name="clientName">Client name (versionless plugin key).</param>
        public static ProjectClient GetClientForKey(string clientName) {
            LoggingController.Log(
                string.Format("Getting Client for Key {0}", clientName));
            List<ProjectClient> clients = GetAllClients();
            if (clients == null) {
                return null;
            }
            ProjectClient projectClient = null;
            foreach (var client in clients) {
                try {
                    var v = client.GenerateUniqueKey();
                    if (PluginManagerController
                        .VersionedPluginKeyToVersionless(v).Equals(clientName)) {
                        // client exists in project
                        LoggingController.Log(
                            string.Format("Discovered client {0} in project record.", clientName));
                        projectClient = client;
                    }
                } catch {
                    // an un-initialized client in set - skip
                    continue;
                }
            }
            if (projectClient == null) {
                LoggingController.Log(
                        string.Format("Initialized new client {0}. Call SaveClient to persist.",
                                      clientName));
                projectClient = new ProjectClient();
                string[] keyComponents = clientName.Split(Constants.STRING_KEY_BINDER[0]);
                projectClient.groupId = keyComponents[0];
                projectClient.artifactId = keyComponents[1];
                projectClient.version = Constants.VERSION_UNKNOWN;
                gpmPackagesInProject.clients.Add(projectClient);
            }
            return projectClient;
        }

        /// <summary>
        /// Gets all clients listed in the project.xml that lives above the Assets directory.
        /// </summary>
        /// <returns>The all clients.</returns>
        public static List<ProjectClient> GetAllClients() {
            if (gpmPackagesInProject == null) {
                ReloadProjectPackages();
                if (gpmPackagesInProject == null) {
                    // no project to load
                    return null;
                }
            }
            return gpmPackagesInProject.clients;
        }

        /// <summary>
        /// Removes the client record for key from the project.xml that lives above the Assets
        /// directory as well as the in memory model object.
        /// </summary>
        /// <param name="clientName">Client name.</param>
        public static void RemoveClientForKey(string clientName) {
            var client = GetClientForKey(clientName);
            if (client != null) {
                gpmPackagesInProject.clients.Remove(client);
                WriteProjectPackages();
            }
        }

        /// <summary>
        /// Saves the client the project.xml that lives above the Assets directory as well as the
        /// in memory model object.
        /// </summary>
        /// <param name="clientName">Client name.</param>
        public static void SaveClient(string clientName) {
            var client = GetClientForKey(clientName);
            if (client != null) {
                WriteProjectPackages();
            }
        }

        /// <summary>
        /// Inflates the project record from an xml file at the path provided.
        /// </summary>
        /// <returns>The project record.</returns>
        /// <param name="projectFile">Project file.</param>
        static ProjectPackages InflateProjectRecord(string projectFile) {
            ProjectPackages result = null;
            if (File.Exists(projectFile)) {
                try {
                    result = ProjectPackages.LoadFromFile(projectFile);
                } catch (Exception e) {
                    LoggingController.LogError(string.Format("Exception loading project meta: {0}",
                                                             e));
                }
            }
            if (result == null) {
                result = new ProjectPackages();
                LoggingController.Log(
                        string.Format("Project GPM data does not exist. Creating new object."));
            }
            return result;
        }

        /// <summary>
        /// Gets the project record path that lives above the Assets directory.
        /// </summary>
        /// <returns>The project record path.</returns>
        static string GetProjectRecordPath() {
            return Path.Combine(Path.Combine(
                UnityController.EnvironmentData.GetApplicationDataPath(), ".."),
                Constants.PROJECT_RECORD_FILENAME);
        }

        /// <summary>
        /// Refreshs the list of asset labels that are present in the current project.
        /// This method does NOT modify the asset labels on assets, it just reads them all.
        /// </summary>
        public static void RefreshListOfAssetLabels() {
            allProjectAssetLabels.Clear();
            var paths = AssetDatabaseController.GetAllAssetPaths();
            foreach (var path in paths) {
                var labels = AssetDatabaseController.GetLabelsForAssetAtPath(path);
                if (labels.Length > 0) {
                    foreach (var label in labels) {
                        allProjectAssetLabels.Add(label);
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the plugin identified by pluginKey is installed in project.
        /// </summary>
        /// <returns><c>true</c>, if plugin installed in project was ised, <c>false</c>
        /// otherwise.</returns>
        /// <param name="pluginKey">Plugin key.</param>
        public static bool IsPluginInstalledInProject(string pluginKey) {
            var versionlessKey = PluginManagerController.VersionedPluginKeyToVersionless(pluginKey);
            var listOfClients = GetAllClients();
            if (listOfClients == null) {
                return false;
            }
            foreach (var client in listOfClients) {
                // Versionless comparison here
                if (PluginManagerController
                    .VersionedPluginKeyToVersionless(client.GenerateUniqueKey())
                    .Equals(versionlessKey)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if the specific plugin version is installed.
        /// </summary>
        /// <returns><c>true</c>, if plugin version in project, <c>false</c> otherwise.</returns>
        /// <param name="pluginKey">Plugin key (includes version).</param>
        public static bool IsPluginVersionInProject(string pluginKey) {
            var listOfClients = GetAllClients();
            if (listOfClients == null) {
                return false;
            }
            foreach (var client in listOfClients) {
                if (client.GenerateUniqueKey().Equals(pluginKey)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Installs the plugin into the current project. Will download the plugin package if needed
        /// but will check for local copy first in the download cache location. Calls the resolve
        /// dependencies regardless of if auto-resolution is enabled.
        /// </summary>
        /// <returns>A response code</returns>
        /// <param name="pluginKey">Plugin key.</param>
        public static ResponseCode InstallPlugin(string pluginKey) {
            LoggingController.Log(
                string.Format("Attempt install of plugin with key {0}.", pluginKey));
            // Is the plugin already installed and is it available to install.
            if (IsPluginInstalledInProject(pluginKey)) {
                LoggingController.Log("Plugin already installed!");
                return ResponseCode.PLUGIN_ALREADY_INSTALLED;
            }

            // non-version check
            bool upgrade = IsPluginInstalledInProject(pluginKey);

            var versionlessKey = PluginManagerController.VersionedPluginKeyToVersionless(pluginKey);
            PackagedPlugin plugin =
                PluginManagerController.GetPluginForVersionlessKey(versionlessKey);
            if (plugin == null) {
                LoggingController.Log(
                    string.Format("Versionless Plugin key {0} was not found.", versionlessKey));
                return ResponseCode.PLUGIN_NOT_FOUND;
            }
            // Generate download uri to binary and location it will be stored.
            var subPath =
                plugin.MetaData.UniqueKey.Replace(Constants.STRING_KEY_BINDER,
                                                  string.Format("{0}",
                                                                Path.DirectorySeparatorChar));
            var fileSystemLocation = new Uri(SettingsController.DownloadCachePath).AbsolutePath;
            var packageDirectory = Path.Combine(fileSystemLocation, subPath);
            var binaryFileName = PluginManagerController.GenerateBinaryFilename(plugin.MetaData);
            var fullPathFileName = Path.Combine(packageDirectory, binaryFileName);
            var binaryUri = PluginManagerController.GenerateBinaryUri(
                    plugin.Location, plugin.MetaData);
            LoggingController.Log(
                string.Format("Checking {0} for existing binary package...", fullPathFileName));
            if (!File.Exists(fullPathFileName)) {
                Directory.CreateDirectory(packageDirectory);
                // Download the binary data into the download cache location if needed.
                LoggingController.Log("Binary package not found. Will attempt to download...");
                byte[] data;
                ResponseCode rc =
                    UriDataFetchController
                        .uriFetcher.BlockingFetchAsBytes(binaryUri, out data);
                if (ResponseCode.FETCH_COMPLETE != rc) {
                    // Something went wrong - abort.
                    LoggingController.Log(
                        string.Format("Download of plugin binary data failed. {0}, {1}",
                                      rc,
                                      binaryUri));
                    return ResponseCode.PLUGIN_BINARY_ERROR;
                }
                LoggingController.Log(
                    string.Format("Binary package downloaded from {0}. " +
                                  "Writting to file system {1}...",
                                  binaryUri.AbsoluteUri,
                                  fullPathFileName));
                File.WriteAllBytes(fullPathFileName, data);
            }
            LoggingController.Log(string.Format("File {0} exists, importing now.",
                                                fullPathFileName));
            // Perform the import of the binary from the download location.
            AssetDatabaseController.ImportPackage(fullPathFileName,
                                                  SettingsController.ShowInstallFiles);
            // If auto deps resolution not on then call resolve deps.
            if (!VersionHandler.Enabled) {
                VersionHandler.UpdateVersionedAssets(true);
            }

            return ResponseCode.PLUGIN_INSTALLED;
        }

        /// <summary>
        /// Uninstalls the plugin from the current project.
        /// </summary>
        /// <returns>The plugin.</returns>
        /// <param name="pluginKey">Plugin key.</param>
        public static ResponseCode UninstallPlugin(string pluginKey) {
            LoggingController.Log(
                string.Format("Remove Plugin for key: {0}", pluginKey));

            var clientName =
                PluginManagerController
                    .VersionedPluginKeyToVersionless(pluginKey);

            // Get the client from the project.
            var client = GetClientForKey(clientName);
            if (client == null) {
                return ResponseCode.PLUGIN_NOT_FOUND;
            }

            var assetPathsToDelete = new List<string>();
            var clientSpecificLabels = new HashSet<string>();
            clientSpecificLabels.Add(string.Join(Constants.STRING_KEY_BINDER, new string[] {
                            Constants.GPM_LABEL_MARKER,
                            Constants.GPM_LABEL_CLIENT,
                            clientName
                        }));
            clientSpecificLabels.Add(string.Join(Constants.STRING_KEY_BINDER, new string[] {
                            Constants.GPM_LABEL_MARKER,
                            Constants.GPM_LABEL_KEY,
                            clientName,
                            client.version
                        }));
            // Check for co-ownership through labels of all assets and dependencies.
            var allAssetPathsToCheck = new List<string>();
            // All the non-plugins path assets (the ones that were actually imported).
            allAssetPathsToCheck.AddRange(client.assets);

            // This gets all the deps paths in Android plugins.
            foreach (var depName in client.depNames) {
                var name = depName.Substring(depName.IndexOf(':') + 1);
                var assets = AssetDatabase.FindAssets(name, new string[] {
                        "Assets/Plugins/Android"});
                if (assets.Length > 0) {
                    allAssetPathsToCheck.Add(AssetDatabase.GUIDToAssetPath(assets[0]));
                }
            }

            foreach (var assetPath in allAssetPathsToCheck) {
                var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset != null) {

                    var existingAssetLabelSet = new HashSet<string>();
                    existingAssetLabelSet.UnionWith(AssetDatabase.GetLabels(asset));

                    var remaining = new HashSet<string>(existingAssetLabelSet);
                    remaining.ExceptWith(clientSpecificLabels);

                    if (remaining.Count == 1 && remaining.Contains(Constants.GPM_LABEL_MARKER)) {
                        // no co-ownership - ok to delete
                        assetPathsToDelete.Add(assetPath);
                    } else if (remaining.Count > 1 &&
                               remaining.Contains(Constants.GPM_LABEL_MARKER)) {
                        // co-owned - remove client labels
                        var labels = new string[remaining.Count];
                        remaining.CopyTo(labels);
                        AssetDatabase.SetLabels(
                            asset,
                            labels);
                    }
                }
            }

            // Suspend resolution.
            bool vhEnabled = VersionHandler.Enabled;
            VersionHandler.Enabled = false;

            try {
                PlayServicesSupport.instances[clientName] = null;

                foreach (var assetPathToDelete in assetPathsToDelete) {
                    AssetDatabase.DeleteAsset(assetPathToDelete);
                }
                RemoveClient(clientName);

                AssetDatabaseController.Refresh();
            } catch (Exception ex) {
                LoggingController.LogError(ex.ToString());
            } finally {
                // Restore resume resolution if was suspended.
                VersionHandler.Enabled = vhEnabled;
            }

            return ResponseCode.PLUGIN_REMOVED;
        }

        /// <summary>
        /// Package postprocessor.
        /// </summary>
        public class PackagePostprocessor : AssetPostprocessor {
            /// <summary>
            /// This is called after importing of any number of assets is complete (when the Assets
            /// progress bar has reached the end).
            /// </summary>
            static void OnPostprocessAllAssets(string[] importedAssets,
                                               string[] deletedAssets,
                                               string[] movedAssets,
                                               string[] movedFromAssetPaths) {

                bool wasTriggeredByPackageManager =
                    AssetDatabaseController.ImportInitiatedFromController;

                var depsModels = new List<PackageDependencies>();
                // look for *gpm.dep.xml in importedAssets
                // TODO: b/34936751 handle case of multiple gpm.dep.xml files found
                foreach (string str in importedAssets) {
                    if (str.EndsWith(Constants.GPM_DEPS_XML_POSTFIX)) {
                        // this is the deps file - resolve fully
                        var res = File.ReadAllText(Path.GetFullPath(str));
                        try {
                            depsModels.Add(PackageDependencies.LoadFromString(res));
                        } catch (Exception e) {
                            LoggingController.Log(string.Format("{0}: \n{1}", e, res));
                        }
                        LoggingController.Log(
                            string.Format("OnPostprocessAllAssets: Dependencies xml: {0}", res));
                    }
                }
                foreach (var depsModel in depsModels) {
                    ProcessDepModel(depsModel, importedAssets);
                }

                foreach (var deleted in deletedAssets) {
                    LoggingController.Log(string.Format("Observed deletion of: {0}", deleted));
                }
            }
        }

        /// <summary>
        /// Processes the dependency model with the assets being imported.
        /// </summary>
        /// <param name="depsModel">Deps model.</param>
        /// <param name="importedAssets">Imported assets.</param>
        static void ProcessDepModel(PackageDependencies depsModel, string[] importedAssets) {
            string clientName = string.Format("{0}:{1}", depsModel.groupId, depsModel.artifactId);
            LoggingController.Log("Dependencies Client: " + clientName);
            var projectClient = GetClientForKey(clientName);
            if (projectClient.version.Equals(Constants.VERSION_UNKNOWN)) {
                // new packaged plugin install
                projectClient.version = depsModel.version;
            } else {
                // new version being installed over an older version?
                // TODO: b/34936656 version compare - make sure new version is newer or same
                // - if newer then need to remove all old assets
            }

            projectClient.assets.AddRange(importedAssets);
            projectClient.clientDependencies = depsModel;
            SaveClient(clientName);
            // mark project dirty
            projectDirty = true;
        }
    }
}
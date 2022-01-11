// <copyright file="ProjectSettings.cs" company="Google Inc.">
// Copyright (C) 2017 Google Inc. All Rights Reserved.
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using UnityEditor;

namespace Google {
    /// <summary>
    /// Enum to determine where a setting should be loaded from or saved to.
    /// </summary>
    [Flags]
    public enum SettingsLocation {

        /// <summary>
        /// Load from / save to the project settings.
        /// </summary>
        Project = (1 << 0),

        /// <summary>
        /// Load from / save to system wide settings.
        /// </summary>
        System = (1 << 1),

        /// <summary>
        /// Load from project settings if available and fallback to system wide settings if it
        /// isn't present.  Save to both project settings and system-wide settings.
        /// </summary>
        All = (1 << 0) | (1 << 1),
    }


    /// <summary>
    /// Interface for ProjectSettings and EditorPrefs used for testing.
    /// This is a UnityEditor.EditorPrefs compatible interface.
    /// </summary>
    public interface ISettings {
        /// <summary>
        /// Set an int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        void SetInt(string name, int value);

        /// <summary>
        /// Set a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        void SetBool(string name, bool value);

        /// <summary>
        /// Set a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        void SetFloat(string name, float value);

        /// <summary>
        /// Set a string property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        void SetString(string name, string value);

        /// <summary>
        /// Get an int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        int GetInt(string name, int defaultValue = 0);

        /// <summary>
        /// Get a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        bool GetBool(string name, bool defaultValue = false);

        /// <summary>
        /// Get a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        float GetFloat(string name, float defaultValue = 0.0f);

        /// <summary>
        /// Get a string property.
        /// This falls back to application-wide settings to allow for users to transition
        /// from application to project level settings.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        string GetString(string name, string defaultValue = "");

        /// <summary>
        /// Determine whether a setting is set.
        /// </summary>
        /// <param name="name">Name of the value to query.</param>
        bool HasKey(string name);

        /// <summary>
        /// Remove a setting.
        /// </summary>
        /// <param name="name">Name of the value to delete.</param>
        void DeleteKey(string name);

        /// <summary>
        /// Get all setting keys.
        /// </summary>
        IEnumerable<string> Keys { get; }
    }

    /// <summary>
    /// Default implementation of system wide settings.
    /// </summary>
    internal class EditorSettings : ISettings {
        /// <summary>
        /// Set a int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetInt(string name, int value) { EditorPrefs.SetInt(name, value); }

        /// <summary>
        /// Set a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetBool(string name, bool value) { EditorPrefs.SetBool(name, value); }

        /// <summary>
        /// Set a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetFloat(string name, float value) { EditorPrefs.SetFloat(name, value); }

        /// <summary>
        /// Set a string property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetString(string name, string value) { EditorPrefs.SetString(name, value); }

        /// <summary>
        /// Get an int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public int GetInt(string name, int defaultValue = 0) {
            return EditorPrefs.GetInt(name, defaultValue);
        }

        /// <summary>
        /// Get a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public bool GetBool(string name, bool defaultValue = false) {
            return EditorPrefs.GetBool(name, defaultValue);
        }

        /// <summary>
        /// Get a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public float GetFloat(string name, float defaultValue = 0.0f) {
            return EditorPrefs.GetFloat(name, defaultValue);
        }

        /// <summary>
        /// Get a string property.
        /// This falls back to application-wide settings to allow for users to transition
        /// from application to project level settings.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public string GetString(string name, string defaultValue = "") {
            return EditorPrefs.GetString(name, defaultValue);
        }

        /// <summary>
        /// Determine whether a setting is set.
        /// </summary>
        /// <param name="name">Name of the value to query.</param>
        public bool HasKey(string name) { return EditorPrefs.HasKey(name); }

        /// <summary>
        /// Remove a setting.
        /// </summary>
        /// <param name="name">Name of the value to delete.</param>
        public void DeleteKey(string name) { EditorPrefs.DeleteKey(name); }

        /// <summary>
        /// Get all setting keys.
        /// </summary>
        public IEnumerable<string> Keys {
            get {
                throw new NotImplementedException("It is not advised to get all system-wide " +
                                                  "settings.");
            }
        }
    }

    /// <summary>
    /// In-memory settings storage.
    /// </summary>
    internal class InMemorySettings : ISettings {
        /// <summary>
        /// In-memory storage for settings.
        /// </summary>
        private SortedDictionary<string, string> settings = new SortedDictionary<string, string>();

        /// <summary>
        /// Set a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        private void Set<T>(string name, T value) {
            settings[name] = value.ToString();
        }

        /// <summary>
        /// Set a int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetInt(string name, int value) { Set(name, value); }

        /// <summary>
        /// Set a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetBool(string name, bool value) { Set(name, value); }

        /// <summary>
        /// Set a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetFloat(string name, float value) { Set(name, value); }

        /// <summary>
        /// Set a string property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetString(string name, string value) { Set(name, value); }

        /// <summary>
        /// Get a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the setting if it's not set.</param>
        public int GetInt(string name, int defaultValue = 0) {
            int value;
            if (Int32.TryParse(GetString(name, defaultValue.ToString()), out value)) {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the setting if it's not set.</param>
        public bool GetBool(string name, bool defaultValue = false) {
            bool value;
            if (Boolean.TryParse(GetString(name, defaultValue.ToString()), out value)) {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the setting if it's not set.</param>
        public float GetFloat(string name, float defaultValue = 0.0f) {
            float value;
            if (Single.TryParse(GetString(name, defaultValue.ToString()), out value)) {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the setting if it's not set.</param>
        public string GetString(string name, string defaultValue = "") {
            string stringValue;
            if (settings.TryGetValue(name, out stringValue)) {
                return stringValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// Determine whether a setting is set.
        /// </summary>
        /// <param name="name">Name of the value to query.</param>
        public bool HasKey(string name) {
            string ignoredValue;
            return settings.TryGetValue(name, out ignoredValue);
        }

        /// <summary>
        /// Remove a setting.
        /// </summary>
        /// <param name="name">Name of the value to delete.</param>
        public void DeleteKey(string name) {
            settings.Remove(name);
        }

        /// <summary>
        /// Get all setting keys.
        /// </summary>
        public IEnumerable<string> Keys { get { return settings.Keys; } }
    }

    /// <summary>
    /// Provides storage of project or global settings.
    /// This class is compatible with UnityEditor.EditorPrefs allowing a user to read from
    /// either application or project level settings based upon the UseProjectSettings flag.
    /// </summary>
    public class ProjectSettings : ISettings {
        /// <summary>
        /// Whether to load settings from and save settings to disk.
        /// Exposed for testing.
        /// </summary>
        internal static bool persistenceEnabled = true;

        /// <summary>
        /// File to store project level settings.
        /// </summary>
        internal static readonly string PROJECT_SETTINGS_FILE = Path.Combine(
            "ProjectSettings", "GvhProjectSettings.xml");

        /// <summary>
        /// Used to lock the static class;
        /// </summary>
        private static readonly object classLock = new object();

        /// <summary>
        /// Logger used to log messages when loading / saving settings.
        /// Exposed for testing.
        /// </summary>
        internal static readonly Logger logger = new Logger();

        /// <summary>
        /// Delegate that checks out a file.
        /// </summary>
        internal delegate bool CheckoutFileDelegate(string filename, Logger logger);

        /// <summary>
        /// Function that checks out a file.
        /// Exposed for testing.
        /// </summary>
        internal static CheckoutFileDelegate checkoutFile = (filename, logger) => {
            return FileUtils.CheckoutFile(filename, logger);
        };

        /// <summary>
        /// Access system wide settings.
        /// Exposed for testing.
        /// </summary>
        internal static ISettings systemSettings = new EditorSettings();

        /// <summary>
        /// Access project settings
        /// Exposed for testing.
        /// </summary>
        internal static ISettings projectSettings = new InMemorySettings();

        /// <summary>
        /// Project settings that have been loaded.
        /// </summary>
        private static ISettings loadedSettings = null;

        /// <summary>
        /// Name of the module used to control whether project or global settings are used.
        /// </summary>
        private readonly string moduleName;

        /// <summary>
        /// Get the module name prefix.
        /// </summary>
        public string ModuleName { get { return moduleName; } }

        /// <summary>
        /// Create an instance of the settings class.
        /// </summary>
        /// <param name="moduleName">
        /// Name of the module that owns this class, used to serialize
        /// the UseProjectSettings option.
        /// </param>
        public ProjectSettings(string moduleName) {
            this.moduleName = moduleName;
        }

        /// <summary>
        /// Name of the setting that controls whether project settings are being used.
        /// Exposed for testing only.
        /// </summary>
        internal string UseProjectSettingsName {
            get { return moduleName + "UseProjectSettings"; }
        }

        /// <summary>
        /// Set to true to read settings in the project (default), false to read settings from
        /// the application using UnityEditor.EditorPrefs.
        /// </summary>
        public bool UseProjectSettings {
            get { return systemSettings.GetBool(UseProjectSettingsName, true); }
            set { systemSettings.SetBool(UseProjectSettingsName, value); }
        }

        /// <summary>
        /// Get the location to fetch settings from.
        /// </summary>
        private SettingsLocation GetLocation {
            get { return UseProjectSettings ? SettingsLocation.Project : SettingsLocation.System; }
        }

        /// <summary>
        /// Set an int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        /// <param name="location">Where to save the setting.</param>
        public void SetInt(string name, int value, SettingsLocation location) {
            if ((location & SettingsLocation.Project) != 0) {
                lock (classLock) {
                    LoadIfEmpty();
                    projectSettings.SetInt(name, value);
                    Save();
                }
            }
            if ((location & SettingsLocation.System) != 0) systemSettings.SetInt(name, value);
        }

        /// <summary>
        /// Set an int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetInt(string name, int value) {
            SetInt(name, value, SettingsLocation.All);
        }

        /// <summary>
        /// Set a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        /// <param name="location">Where to save the setting.</param>
        public void SetBool(string name, bool value, SettingsLocation location) {
            if ((location & SettingsLocation.Project) != 0) {
                lock (classLock) {
                    LoadIfEmpty();
                    projectSettings.SetBool(name, value);
                    Save();
                }
            }
            if ((location & SettingsLocation.System) != 0) systemSettings.SetBool(name, value);
        }

        /// <summary>
        /// Set a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetBool(string name, bool value) {
            SetBool(name, value, SettingsLocation.All);
        }

        /// <summary>
        /// Set a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        /// <param name="location">Where to save the setting.</param>
        public void SetFloat(string name, float value, SettingsLocation location) {
            if ((location & SettingsLocation.Project) != 0) {
                lock (classLock) {
                    LoadIfEmpty();
                    projectSettings.SetFloat(name, value);
                    Save();
                }
            }
            if ((location & SettingsLocation.System) != 0) systemSettings.SetFloat(name, value);
        }

        /// <summary>
        /// Set a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetFloat(string name, float value) {
            SetFloat(name, value, SettingsLocation.All);
        }

        /// <summary>
        /// Set a string property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        /// <param name="location">Where to save the setting.</param>
        public void SetString(string name, string value, SettingsLocation location) {
            if ((location & SettingsLocation.Project) != 0) {
                lock (classLock) {
                    LoadIfEmpty();
                    projectSettings.SetString(name, value);
                    Save();
                }
            }
            if ((location & SettingsLocation.System) != 0) systemSettings.SetString(name, value);
        }

        /// <summary>
        /// Set a string property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetString(string name, string value) {
            SetString(name, value, SettingsLocation.All);
        }

        /// <summary>
        /// Load project settings if they're not loaded or have been changed.
        /// </summary>
        private static void LoadIfEmpty() {
            lock (classLock) {
                if (loadedSettings != projectSettings) {
                    Load();
                    loadedSettings = projectSettings;
                }
            }
        }

        /// <summary>
        /// Get an int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        /// <param name="location">Where to read the setting from.</param>
        public int GetInt(string name, int defaultValue, SettingsLocation location) {
            int value = defaultValue;
            if ((location & SettingsLocation.System) != 0) {
                value = systemSettings.GetInt(name, value);
            }
            if ((location & SettingsLocation.Project) != 0) {
                lock (classLock) {
                    LoadIfEmpty();
                    value = projectSettings.GetInt(name, value);
                }
            }
            return value;
        }

        /// <summary>
        /// Get an int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public int GetInt(string name, int defaultValue = 0) {
            return GetInt(name, defaultValue, GetLocation);
        }

        /// <summary>
        /// Get a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        /// <param name="location">Where to read the setting from.</param>
        public bool GetBool(string name, bool defaultValue, SettingsLocation location) {
            bool value = defaultValue;
            if ((location & SettingsLocation.System) != 0) {
                value = systemSettings.GetBool(name, value);
            }
            if ((location & SettingsLocation.Project) != 0) {
                lock (classLock) {
                    LoadIfEmpty();
                    value = projectSettings.GetBool(name, value);
                }
            }
            return value;
        }

        /// <summary>
        /// Get a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public bool GetBool(string name, bool defaultValue = false) {
            return GetBool(name, defaultValue, GetLocation);
        }

        /// <summary>
        /// Get a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        /// <param name="location">Where to read the setting from.</param>
        public float GetFloat(string name, float defaultValue, SettingsLocation location) {
            float value = defaultValue;
            if ((location & SettingsLocation.System) != 0) {
                value = systemSettings.GetFloat(name, value);
            }
            if ((location & SettingsLocation.Project) != 0) {
                lock (classLock) {
                    LoadIfEmpty();
                    value = projectSettings.GetFloat(name, value);
                }
            }
            return value;
        }

        /// <summary>
        /// Get a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public float GetFloat(string name, float defaultValue = 0.0f) {
            return GetFloat(name, defaultValue, GetLocation);
        }

        /// <summary>
        /// Get a string property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        /// <param name="location">Where to read the setting from.</param>
        public string GetString(string name, string defaultValue, SettingsLocation location) {
            string value = defaultValue;
            if ((location & SettingsLocation.System) != 0) {
                value = systemSettings.GetString(name, value);
            }
            if ((location & SettingsLocation.Project) != 0) {
                lock (classLock) {
                    LoadIfEmpty();
                    value = projectSettings.GetString(name, value);
                }
            }
            return value;
        }

        /// <summary>
        /// Get a string property.
        /// This falls back to application-wide settings to allow for users to transition
        /// from application to project level settings.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public string GetString(string name, string defaultValue = "") {
            return GetString(name, defaultValue, GetLocation);
        }

        /// <summary>
        /// Determine whether a setting is set.
        /// </summary>
        /// <param name="name">Name of the value to query.</param>
        /// <param name="location">Where to search for the setting.</param>
        public bool HasKey(string name, SettingsLocation location) {
            bool hasKey = false;
            if ((location & SettingsLocation.Project) != 0) {
                lock (classLock) {
                    LoadIfEmpty();
                    hasKey |= projectSettings.HasKey(name);
                }
            }
            if (!hasKey && (location & SettingsLocation.System) != 0) {
                hasKey |= systemSettings.HasKey(name);
            }
            return hasKey;
        }

        /// <summary>
        /// Determine whether a setting is set.
        /// </summary>
        /// <param name="name">Name of the value to query.</param>
        public bool HasKey(string name) {
            return HasKey(name, GetLocation);
        }


        /// <summary>
        /// Remove a setting.
        /// </summary>
        /// <param name="name">Name of the value to delete.</param>
        public void DeleteKey(string name) {
            systemSettings.DeleteKey(name);
            lock (classLock) {
                LoadIfEmpty();
                projectSettings.DeleteKey(name);
                Save();
            }
        }

        /// <summary>
        /// Delete the specified set of keys (this will revert to default settings).
        /// </summary>
        /// <param name="names">Names of the values to delete.</param>
        public void DeleteKeys(IEnumerable<string> names) {
            lock (classLock) {
                LoadIfEmpty();
                foreach (var name in names) {
                    systemSettings.DeleteKey(name);
                    projectSettings.DeleteKey(name);
                }
                Save();
            }
        }

        /// <summary>
        /// Delete all project level settings.
        /// Exposed for testing.
        /// </summary>
        /// <param name="save">Whether to save the settings.</param>
        internal static void DeleteAllProjectKeys(bool save = true) {
            lock (classLock) {
                if (save) LoadIfEmpty();
                foreach (var key in new List<string>(projectSettings.Keys)) {
                    projectSettings.DeleteKey(key);
                }
                if (save) Save();
            }
        }

        /// <summary>
        /// Get all setting keys.
        /// </summary>
        public IEnumerable<string> Keys {
            get {
                throw new NotImplementedException("It is not possible to get all system keys.");
            }
        }

        /// <summary>
        /// Load project specific settings into the cache.
        /// </summary>
        ///
        /// Settings are loaded from an XML file in the following format:
        /// <projectSettings>
        ///   <projectSetting name="settingName0" value="settingValue0" />
        ///   <projectSetting name="settingName1" value="settingValue1" />
        ///   ...
        /// </projectSettings>
        ///
        /// <returns>true if settings are successfully loaded, false otherwise.</returns>
        private static bool Load() {
            lock (classLock) {
                DeleteAllProjectKeys(false);
                if (!persistenceEnabled || !XmlUtilities.ParseXmlTextFileElements(
                    PROJECT_SETTINGS_FILE, logger,
                    (reader, elementName, isStart, parentElementName, elementNameStack) => {
                        if (elementName == "projectSettings" && parentElementName == "") {
                            return true;
                        } else if (elementName == "projectSetting" &&
                                   parentElementName == "projectSettings") {
                            if (isStart) {
                                var key = reader.GetAttribute("name");
                                var value = reader.GetAttribute("value");
                                if (!String.IsNullOrEmpty(key)) {
                                    if (String.IsNullOrEmpty(value)) {
                                        projectSettings.DeleteKey(key);
                                    } else {
                                        projectSettings.SetString(key, value);
                                    }
                                }
                            }
                            return true;
                        }
                        return false;
                    })) {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Save in-memory project specific settings to the settings file.
        /// </summary>
        private static void Save() {
            lock (classLock) {
                if (projectSettings == null || !persistenceEnabled) {
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(PROJECT_SETTINGS_FILE));
                if (!checkoutFile(PROJECT_SETTINGS_FILE, logger)) {
                    logger.Log(
                        String.Format("Unable to checkout '{0}'. Project settings were not saved!",
                                      PROJECT_SETTINGS_FILE), LogLevel.Error);
                    return;
                }
                try {
                    using (var writer =
                           XmlWriter.Create(PROJECT_SETTINGS_FILE,
                                            new XmlWriterSettings {
                                                Encoding = new UTF8Encoding(false),
                                                Indent = true,
                                                IndentChars = "  ",
                                                NewLineChars = "\n",
                                                NewLineHandling = NewLineHandling.Replace
                                            })) {
                        writer.WriteStartElement("projectSettings");
                        foreach (var key in projectSettings.Keys) {
                            var value = projectSettings.GetString(key);
                            writer.WriteStartElement("projectSetting");
                            if (!String.IsNullOrEmpty(key) && !String.IsNullOrEmpty(value)) {
                                writer.WriteAttributeString("name", key);
                                writer.WriteAttributeString("value", value);
                            }
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                } catch (Exception exception) {
                    if (exception is IOException || exception is UnauthorizedAccessException) {
                        logger.Log(String.Format("Unable to write to '{0}' ({1}, " +
                                                 "Project settings were not saved!",
                                                 PROJECT_SETTINGS_FILE, exception), LogLevel.Error);
                        return;
                    }
                    throw exception;
                }
            }
        }
    }
}

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
using System.Xml;
using UnityEditor;

namespace Google {
    /// <summary>
    /// Provides storage of project or global settings.
    /// This class is compatible with UnityEditor.EditorPrefs allowing a user to read from
    /// either application or project level settings based upon the UseProjectSettings flag.
    /// </summary>
    internal class ProjectSettings {
        /// <summary>
        /// Enum to determine if setting should be saved to system-level EditorPrefs
        /// </summary>
        public enum SettingsSave {
            ProjectOnly,
            EditorPrefs,
            BothProjectAndEditorPrefs
        }

        /// <summary>
        /// File to store project level settings.
        /// </summary>
        private static readonly string PROJECT_SETTINGS_FILE = Path.Combine(
            "ProjectSettings", "GvhProjectSettings.xml");

        /// <summary>
        /// Backing store for Settings property.
        /// </summary>
        private static SortedDictionary<string, string> settings;

        /// <summary>
        /// Used to lock the static class;
        /// </summary>
        private static readonly object classLock = new object();

        /// <summary>
        /// Logger used to log messages when loading / saving settings.
        /// </summary>
        private static readonly Logger logger = new Logger();

        /// <summary>
        /// Name of the module used to control whether project or global settings are used.
        /// </summary>
        private readonly string moduleName;

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
        /// In-memory cache of project specific settings.
        /// </summary>
        private static SortedDictionary<string, string> Settings {
            get {
                LoadIfEmpty();
                return settings;
            }
        }

        /// <summary>
        /// Name of the setting that controls whether project settings are being used.
        /// </summary>
        private string UseProjectSettingsName {
            get { return moduleName + "UseProjectSettings"; }
        }

        /// <summary>
        /// Set to true to read settings in the project (default), false to read settings from
        /// the application using UnityEditor.EditorPrefs.
        /// </summary>
        public bool UseProjectSettings {
            get { return EditorPrefs.GetBool(UseProjectSettingsName, true); }
            set { EditorPrefs.SetBool(UseProjectSettingsName, value); }
        }

        /// <summary>
        /// Load settings if they're not loaded.
        /// </summary>
        private static void LoadIfEmpty() {
            lock (classLock) {
                if (settings == null) Load();
            }
        }

        /// <summary>
        /// Set a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        private static void Set<T>(string name, T value) {
            lock (classLock) {
                Settings[name] = value.ToString();
                Save();
            }
        }

        private void SavePreferences(SettingsSave saveLevel, Action saveToProject, Action
                                         saveToEditor) {
            switch (saveLevel) {
                case SettingsSave.ProjectOnly:
                    saveToProject();
                    break;
                case SettingsSave.EditorPrefs:
                    saveToEditor();
                    break;
                case SettingsSave.BothProjectAndEditorPrefs:
                default:
                    saveToEditor();
                    saveToProject();
                    break;
            }
        }

        /// <summary>
        /// Set a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        /// <param name="saveLevel">Determine how setting should save</param>
        public void SetBool(string name, bool value, SettingsSave saveLevel) {
            SavePreferences(saveLevel,
                () => { Set(name, value); },
                () => { EditorPrefs.SetBool(name, value); });
        }

        public void SetBool(string name, bool value) {
            SavePreferences(SettingsSave.BothProjectAndEditorPrefs,
                () => { Set(name, value); },
                () => { EditorPrefs.SetBool(name, value); });
        }

        /// <summary>
        /// Set a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetFloat(string name, float value, SettingsSave saveLevel) {
            SavePreferences(saveLevel, () => { Set(name, value); },
                () => { EditorPrefs.SetFloat(name, value); });
        }

        public void SetFloat(string name, float value) {
            SavePreferences(SettingsSave.BothProjectAndEditorPrefs,
                () => { Set(name, value); },
                () => { EditorPrefs.SetFloat(name, value); });
        }

        /// <summary>
        /// Set a int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetInt(string name, int value, SettingsSave saveLevel) {
            SavePreferences(saveLevel, () => { Set(name, value); },
                () => { EditorPrefs.SetInt(name, value); });
        }

        public void SetInt(string name, int value) {
            SavePreferences(SettingsSave.BothProjectAndEditorPrefs,
                () => { Set(name, value); },
                () => { EditorPrefs.SetInt(name, value); });
        }


        /// <summary>
        /// Set a string property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="value">Value to set.</param>
        public void SetString(string name, string value, SettingsSave saveLevel) {
            SavePreferences(saveLevel, () => { Set(name, value); },
                () => { EditorPrefs.SetString(name, value); });
        }

        public void SetString(string name, string value) {
            SavePreferences(SettingsSave.BothProjectAndEditorPrefs, () => { Set(name, value); },
                () => { EditorPrefs.SetString(name, value); });
        }

        /// <summary>
        /// Get a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the setting if it's not set.</param>
        private static string Get(string name, string defaultValue) {
            lock (classLock) {
                string stringValue;
                if (Settings.TryGetValue(name, out stringValue)) {
                    return stringValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the setting if it's not set.</param>
        private static bool Get(string name, bool defaultValue) {
            bool value;
            if (Boolean.TryParse(Get(name, defaultValue.ToString()), out value)) {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the setting if it's not set.</param>
        private static float Get(string name, float defaultValue) {
            float value;
            if (Single.TryParse(Get(name, defaultValue.ToString()), out value)) {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a project level setting.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the setting if it's not set.</param>
        private static int Get(string name, int defaultValue) {
            int value;
            if (Int32.TryParse(Get(name, defaultValue.ToString()), out value)) {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a string property.
        /// This falls back to application-wide settings to allow for users to transition
        /// from application to project level settings.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public string GetString(string name, string defaultValue = "") {
            var systemValue = EditorPrefs.GetString(name, defaultValue: defaultValue);
            return UseProjectSettings ? Get(name, systemValue) : systemValue;
        }

        /// <summary>
        /// Get a bool property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public bool GetBool(string name, bool defaultValue = false) {
            var systemValue = EditorPrefs.GetBool(name, defaultValue: defaultValue);
            return UseProjectSettings ? Get(name, systemValue) : systemValue;
        }

        /// <summary>
        /// Get a float property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public float GetFloat(string name, float defaultValue = 0.0f) {
            var systemValue = EditorPrefs.GetFloat(name, defaultValue: defaultValue);
            return UseProjectSettings ? Get(name, systemValue) : systemValue;
        }

        /// <summary>
        /// Get an int property.
        /// </summary>
        /// <param name="name">Name of the value.</param>
        /// <param name="defaultValue">Default value of the property if it isn't set.</param>
        public int GetInt(string name, int defaultValue = 0) {
            var systemValue = EditorPrefs.GetInt(name, defaultValue: defaultValue);
            return UseProjectSettings ? Get(name, systemValue) : systemValue;
        }

        /// <summary>
        /// Determine whether a setting is set.
        /// </summary>
        /// <param name="name">Name of the value to query.</param>
        public bool HasKey(string name) {
            if (UseProjectSettings) {
                string ignoredValue;
                lock (classLock) {
                    return Settings.TryGetValue(name, out ignoredValue);
                }
            } else {
                return EditorPrefs.HasKey(name);
            }
        }

        /// <summary>
        /// Remove all settings.
        /// </summary>
        public void DeleteAll() {
            EditorPrefs.DeleteAll();
            Clear();
            Save();
        }

        /// <summary>
        /// Remove a setting.
        /// </summary>
        /// <param name="name">Name of the value to delete.</param>
        public void DeleteKey(string name) {
            EditorPrefs.DeleteKey(name);
            lock (classLock) {
                Settings.Remove(name);
                Save();
            }
        }

        /// <summary>
        /// Delete the specified set of keys (this will revert to default settings).
        /// </summary>
        /// <param name="names">Names of the values to delete.</param>
        internal void DeleteKeys(IEnumerable<string> names) {
            foreach (var name in names) {
                if (HasKey(name)) DeleteKey(name);
            }
        }

        /// <summary>
        /// Clear in-memory settings.
        /// </summary>
        private static void Clear() {
            lock (classLock) {
                settings = new SortedDictionary<string, string>();
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
                Clear();
                if (!XmlUtilities.ParseXmlTextFileElements(
                    PROJECT_SETTINGS_FILE, logger,
                    (reader, elementName, isStart, parentElementName, elementNameStack) => {
                        if (elementName == "projectSettings" && parentElementName == "") {
                            return true;
                        } else if (elementName == "projectSetting" &&
                                   parentElementName == "projectSettings") {
                            if (isStart) {
                                var name = reader.GetAttribute("name");
                                var value = reader.GetAttribute("value");
                                if (!String.IsNullOrEmpty(name)) {
                                    if (String.IsNullOrEmpty(value)) {
                                        settings.Remove(name);
                                    } else {
                                        settings[name] = value;
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
                if (settings == null) {
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(PROJECT_SETTINGS_FILE));
                if (!FileUtils.CheckoutFile(PROJECT_SETTINGS_FILE, logger)) {
                    logger.Log(
                        String.Format("Unable to checkout '{0}'. Project settings were not saved!",
                                      PROJECT_SETTINGS_FILE), LogLevel.Error);
                    return;
                }
                using (var writer = new XmlTextWriter(new StreamWriter(PROJECT_SETTINGS_FILE)) {
                        Formatting = Formatting.Indented,
                    }) {
                    writer.WriteStartElement("projectSettings");
                    foreach (var kv in settings) {
                        writer.WriteStartElement("projectSetting");
                        if (!String.IsNullOrEmpty(kv.Key) && !String.IsNullOrEmpty(kv.Value)) {
                            writer.WriteAttributeString("name", kv.Key);
                            writer.WriteAttributeString("value", kv.Value);
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
            }
        }
    }
}

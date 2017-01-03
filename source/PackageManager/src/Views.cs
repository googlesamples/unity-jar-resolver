// <copyright file="Views.cs" company="Google Inc.">
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
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Registry manager view.
    /// </summary>
    public class RegistryManagerView : EditorWindow {
        Vector2 scrollPos;
        string newRegUri;
        List<RegistryWrapper> registires;

        void OnInspectorGUI() {
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Called by UnityEditor after lifecycle events:
        /// - Awake {open project}
        /// - AFTER:OnDisable {open different scene, change source code}
        /// </summary>
        void OnEnable() {
            registires = RegistryManagerController.AllWrappedRegistries;
        }

        /// <summary>
        /// Called by UnityEditor lifecycle events:
        /// - {open different scene, change source code}
        /// </summary>
        void OnDisable() {
            registires.Clear();
        }

        void OnGUI() {
            using (var h = new EditorGUILayout.HorizontalScope()) {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos)) {
                    scrollPos = scrollView.scrollPosition;
                    RenderAddRegistryForm();
                    RenderListOfRegistries();
                }
            }
        }

        /// <summary>
        /// Renders the list of known registries.
        /// </summary>
        void RenderListOfRegistries() {
            try {
                foreach (var wrappedReg in registires) {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(wrappedReg.Model.GenerateUniqueKey());
                    GUI.backgroundColor = Color.red;
                    if (GUILayout.Button("Remove Registry")) {
                        if (EditorUtility.DisplayDialog("Confirm Delete Registry",
                            "Are you sure you want to delete the registry?",
                                                        "Yes Delete It!",
                                                        "Cancel")) {
                            ResponseCode rc = RegistryManagerController
                                .RemoveRegistry(wrappedReg.Location);

                            if (ResponseCode.REGISTRY_NOT_FOUND == rc) {
                                EditorUtility.DisplayDialog("Registry Not Found!",
                                                            "There was a problem while trying to " +
                                                            "remove the registry. It was not " +
                                                            "found when we tried to remove it."
                                                            , "Ok");
                            }
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.LabelField(wrappedReg.Location.AbsoluteUri);
                }
            } catch (Exception e) {
                Debug.LogError(e);
            }
        }

        /// <summary>
        /// Renders the add registry form.
        /// </summary>
        void RenderAddRegistryForm() {
            EditorGUILayout.BeginHorizontal();
            newRegUri = EditorGUILayout.TextField("New Registry Uri:", newRegUri);
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Add Registry")) {
                try {
                    ResponseCode rc = RegistryManagerController.AddRegistry(new Uri(newRegUri));
                    if (ResponseCode.REGISTRY_ADDED == rc) {
                        newRegUri = "";
                        registires = RegistryManagerController.AllWrappedRegistries;
                    } else if (ResponseCode.REGISTRY_ALREADY_PRESENT == rc) {
                        EditorUtility.DisplayDialog("Registry Already Present",
                                                    "The registry was NOT added since it" +
                                                    "is already known.",
                                                    "Ok");
                    } else {
                        EditorUtility.DisplayDialog("Registry Location Not Valid",
                                                    string.Format(
                                                        "The registry cannot be added. An " +
                                                        "error has occured using the provided " +
                                                        "location.\n\n{0}", rc),
                                                    "Ok");
                    }
                } catch (Exception e) {
                    // failure - bad data
                    EditorUtility.DisplayDialog("Registry Location Processing Error",
                                                string.Format("A processing exception was " +
                                                              "generated while trying to add {0}." +
                                                              "\n\n{1}", newRegUri, e),
                                                "Ok");
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        [MenuItem("Assets/Play Services Resolver/Package Manager/Registries")]
        public static void ShowRegistriesManagerWindow() {
            var window = (RegistryManagerView)EditorWindow.GetWindow(
                typeof(RegistryManagerView), true, "Package Manager Registries");
            window.Show();
        }
    }

    /// <summary>
    /// Plugin manager view. Shows the available plugins and allows user to install/remove plugins
    /// from project using UI.
    /// </summary>
    public class PluginManagerView : EditorWindow {
        Vector2 scrollPos;
        List<PackagedPlugin> plugins;
        bool pluginDataDirty = true;
        Stack<string> installPlugins = new Stack<string>();
        Stack<string> uninstallPlugins = new Stack<string>();
        Stack<string> moreInfoPlugins = new Stack<string>();

        void OnInspectorGUI() {
            EditorUtility.SetDirty(this);
        }

        void RefreshPluginDataForWindow() {
            LoggingController.Log("RefreshPluginDataForWindow()");
            plugins = PluginManagerController.GetListOfAllPlugins();
            LoggingController.Log(
                string.Format("RefreshPluginDataForWindow: plugin count = {0}", plugins.Count));
        }

        void Update() {
            if (pluginDataDirty) {
                LoggingController.Log("plugin data marked dirty - refreshing...");
                pluginDataDirty = false;
                RefreshPluginDataForWindow();
                LoggingController.Log(
                    string.Format("plugin data marked dirty - refreshed and sees {0} plugins",
                                  plugins.Count));
            }

            while (installPlugins.Count > 0) {
                var pluginKey = installPlugins.Pop();
                ResponseCode rc = ProjectManagerController.InstallPlugin(pluginKey);
                if (ResponseCode.PLUGIN_NOT_INSTALLED == rc) {
                    EditorUtility.DisplayDialog("Plugin Install Error",
                                      "There was a problem installing the selected plugin.",
                                      "Ok");
                    LoggingController.Log(
                        string.Format("Could not install plugin with key {0}." +
                                      "Got {1} response code.", pluginKey, rc));
                } else {
                    pluginDataDirty = true;
                }
            }

            while (moreInfoPlugins.Count > 0) {
                var pluginKey = moreInfoPlugins.Pop();
                var plugin = PluginManagerController.GetPluginForVersionlessKey(
                    PluginManagerController.VersionedPluginKeyToVersionless(pluginKey));
                // popup with full description
                EditorUtility.DisplayDialog(
                    string.Format("{0}", plugin.MetaData.artifactId),
                                  plugin.Description.languages[0].fullDesc,
                                  "Ok");
            }

            while (uninstallPlugins.Count > 0) {
                var pluginKey = uninstallPlugins.Pop();
                ResponseCode rc = ProjectManagerController.UninstallPlugin(pluginKey);
                if (ResponseCode.PLUGIN_NOT_REMOVED == rc) {
                    EditorUtility.DisplayDialog("Plugin Uninstall Error",
                                      "There was a problem removing the selected plugin.",
                                      "Ok");
                    LoggingController.Log(
                        string.Format("Could not uninstall plugin with key {0}." +
                                      "Got {1} response code.",pluginKey,rc));
                } else {
                    pluginDataDirty = true;
                }
            }
        }

        void OnGUI() {
            using (var h = new EditorGUILayout.HorizontalScope()) {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos)) {
                    scrollPos = scrollView.scrollPosition;
                    if (plugins == null) {
                        plugins = PluginManagerController.GetListOfAllPlugins();
                    }
                    foreach (var plugin in plugins) {
                        RenderPluginDetails(plugin);
                    }
                }
            }
        }

        void RenderPluginDetails(PackagedPlugin plugin) {
            GUILayout.Space(5);
            // Name - Version - Short Desc
            var v = string.Format("{0} - version: {1} '{2}'",
                                  plugin.MetaData.artifactId,
                                  plugin.MetaData.version,
                                  plugin.Description
                                  .languages[0].shortDesc);
            var pluginKey = string.Format("{0}", plugin.MetaData.UniqueKey);

            EditorGUILayout.LabelField(v);
            // [More Info] - [Install|Remove|Update]
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("More Info")) {
                moreInfoPlugins.Push(pluginKey);
            }

            if (ProjectManagerController.IsPluginInstalledInProject(plugin.MetaData.UniqueKey)) {
                // delete or update
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Uninstall")) {
                    uninstallPlugins.Push(pluginKey);
                }
            } else {
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("Install")) {
                    installPlugins.Push(pluginKey);
                }
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(5);
        }

        [MenuItem("Assets/Play Services Resolver/Package Manager/Plugins")]
        public static void ShowPluginManagerWindow() {
            var window = (PluginManagerView)GetWindow(
                typeof(PluginManagerView), true, "Package Manager Plugins");
            window.Show();
        }
    }

    /// <summary>
    /// Settings manager view.
    /// </summary>
    public class SettingsManagerView : EditorWindow {
        void OnInspectorGUI() {
            EditorUtility.SetDirty(this);
        }

        void OnGUI() {
            EditorGUILayout.LabelField("Package Manager Settings");
            /// <summary>
            /// Download Cache Path
            /// </summary>
            SettingsController.DownloadCachePath =
                                  EditorGUILayout.TextField("Download Cache Path:",
                                                            SettingsController.DownloadCachePath);
            /// <summary>
            /// Display the package contents before installing a plugin.
            /// </summary>
            SettingsController.ShowInstallFiles =
                                  EditorGUILayout.ToggleLeft(
                                      "Show plugin package contents before install?",
                                      SettingsController.ShowInstallFiles);

            /// <summary>
            /// Toggle Verbose Logging
            /// </summary>
            SettingsController.VerboseLogging = EditorGUILayout.ToggleLeft(
                "Enable Verbose Logging",
                SettingsController.VerboseLogging);

        }

        [MenuItem("Assets/Play Services Resolver/Package Manager/Settings")]
        public static void ShowSettingsWindow() {
            var window = (SettingsManagerView)EditorWindow.GetWindow(
                typeof(SettingsManagerView), true, "Package Manager Settings");
            window.Show();
        }
    }

    /// <summary>
    /// Plugin removal context view.
    /// </summary>
    public static class PluginRemovalContextView {
        static readonly List<string> selectionLabels = new List<string>();

        /// <summary>
        /// Validates the do something.
        /// </summary>
        /// <returns><c>true</c>, if do something was validated, <c>false</c> otherwise.</returns>
        [MenuItem("Assets/Remove Associated Plugin", true)]
        static bool ValidateRemovePlugin() {
            selectionLabels.Clear();
            // is the current selected asset a GPM labeled asset?
            bool gpmAssetLabelFound = false;
            var assetGuids = Selection.assetGUIDs;
            foreach (var aGuid in assetGuids) {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(aGuid));
                var labels = AssetDatabase.GetLabels(asset);
                foreach (var label in labels) {
                    if (label.StartsWith(Constants.GPM_LABEL_MARKER)) {
                        gpmAssetLabelFound = true;
                        selectionLabels.Add(label);
                    }
                }
            }
            return gpmAssetLabelFound;
        }

        [MenuItem("Assets/Remove Associated Plugin")]
        public static void RemovePlugin() {
            var candidateRemovals = new List<string>(selectionLabels);
            var window = (PluginCandidateRemovalWindow)EditorWindow.GetWindow(
                typeof(PluginCandidateRemovalWindow), true, "Google Package Manager");
            window.candidateRemovals = candidateRemovals;
            window.Show();
        }
    }

    /// <summary>
    /// Plugin candidate removal window.
    /// </summary>
    public class PluginCandidateRemovalWindow : EditorWindow {
        void OnInspectorGUI() {
            EditorUtility.SetDirty(this);
        }

        public List<string> candidateRemovals = new List<string>();

        void OnGUI() {
            foreach (var cr in candidateRemovals) {
                EditorGUILayout.ToggleLeft(cr, true);
            }
        }
    }
}
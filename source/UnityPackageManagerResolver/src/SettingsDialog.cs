// <copyright file="SettingsDialog.cs" company="Google LLC">
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

namespace Google {

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Settings dialog for UnityPackageManagerResolver.
/// </summary>
public class UnityPackageManagerResolverSettingsDialog : EditorWindow
{
    /// <summary>
    /// Loads / saves settings for this dialog.
    /// </summary>
    private class Settings {
        /// <summary>
        /// Whether enable external registries to be added to this Unity project
        /// </summary>
        internal bool enable;

        /// <summary>
        /// Whether to prompt the user before to adding external registries.
        /// </summary>
        internal bool promptToAddRegistries;

        /// <summary>
        /// Whether to enable / disable verbose logging.
        /// </summary>
        internal bool verboseLoggingEnabled;

        /// <summary>
        /// Whether settings are project specific.
        /// </summary>
        internal bool useProjectSettings;

        /// <summary>
        /// Analytics settings.
        /// </summary>
        internal EditorMeasurement.Settings analyticsSettings;

        /// <summary>
        /// Load settings into the dialog.
        /// </summary>
        internal Settings() {
            enable = UnityPackageManagerResolver.Enable;
            promptToAddRegistries = UnityPackageManagerResolver.PromptToAddRegistries;
            verboseLoggingEnabled = UnityPackageManagerResolver.VerboseLoggingEnabled;
            useProjectSettings = UnityPackageManagerResolver.UseProjectSettings;
            analyticsSettings =
                new EditorMeasurement.Settings(UnityPackageManagerResolver.analytics);
        }

        /// <summary>
        /// Save dialog settings to preferences.
        /// </summary>
        internal void Save() {
            UnityPackageManagerResolver.Enable = enable;
            UnityPackageManagerResolver.PromptToAddRegistries = promptToAddRegistries;
            UnityPackageManagerResolver.VerboseLoggingEnabled = verboseLoggingEnabled;
            UnityPackageManagerResolver.UseProjectSettings = useProjectSettings;
            analyticsSettings.Save();
        }
    }

    private Settings settings;

    /// <summary>
    /// Load settings for this dialog.
    /// </summary>
    private void LoadSettings() {
        settings = new Settings();
    }

    /// <summary>
    /// Setup the window's initial position and size.
    /// </summary>
    public void Initialize() {
        minSize = new Vector2(350, 400);
        position = new Rect(UnityEngine.Screen.width / 3,
                            UnityEngine.Screen.height / 3,
                            minSize.x, minSize.y);
        UnityPackageManagerResolver.analytics.Report("settings/show", "Settings");
    }

    /// <summary>
    /// Called when the window is loaded.
    /// </summary>
    public void OnEnable() {
        LoadSettings();
    }

    /// <summary>
    /// Called when the GUI should be rendered.
    /// </summary>
    public void OnGUI() {
        GUI.skin.label.wordWrap = true;

        GUILayout.BeginVertical();

        GUILayout.Label(String.Format("{0} (version {1}.{2}.{3})",
                                      UnityPackageManagerResolver.PLUGIN_NAME,
                                      UnityPackageManagerResolverVersionNumber.Value.Major,
                                      UnityPackageManagerResolverVersionNumber.Value.Minor,
                                      UnityPackageManagerResolverVersionNumber.Value.Build));

        if (!UnityPackageManagerResolver.ScopedRegistriesSupported) {
            GUILayout.Label(
                String.Format("Only supported from Unity {0} and above.",
                    UnityPackageManagerResolver.MinimumUnityVersionString));
        }

        // Disable all GUI if scoped registries are not supported.
        GUI.enabled = UnityPackageManagerResolver.ScopedRegistriesSupported;

        GUILayout.BeginHorizontal();
        GUILayout.Label("Add package registries", EditorStyles.boldLabel);
        settings.enable = EditorGUILayout.Toggle(settings.enable);
        GUILayout.EndHorizontal();
        GUILayout.Label("When this option is enabled, Unity Package Manager registries " +
                        "discovered by this plugin will be added to the project's manifest. " +
                        "This allows Unity packages from additional sources to be " +
                        "discovered and managed by the Unity Package Manager.");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Prompt to add package registries", EditorStyles.boldLabel);
        settings.promptToAddRegistries = EditorGUILayout.Toggle(settings.promptToAddRegistries);
        GUILayout.EndHorizontal();
        GUILayout.Label("When this option is enabled, this plugin will prompt for confirmation " +
                        "before adding Unity Package Manager registries to the project's " +
                        "manifest.");

        settings.analyticsSettings.RenderGui();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Verbose logging", EditorStyles.boldLabel);
        settings.verboseLoggingEnabled = EditorGUILayout.Toggle(settings.verboseLoggingEnabled);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Use project settings", EditorStyles.boldLabel);
        settings.useProjectSettings = EditorGUILayout.Toggle(settings.useProjectSettings);
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        if (GUILayout.Button("Reset to Defaults")) {
            // Load default settings into the dialog but preserve the state in the user's
            // saved preferences.
            var backupSettings = new Settings();
            UnityPackageManagerResolver.RestoreDefaultSettings();
            UnityPackageManagerResolver.analytics.Report("settings/reset", "Settings Reset");
            LoadSettings();
            backupSettings.Save();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cancel")) {
            UnityPackageManagerResolver.analytics.Report("settings/cancel", "Settings Cancel");
            Close();
        }
        if (GUILayout.Button("OK")) {
            UnityPackageManagerResolver.analytics.Report(
                "settings/save",
                new KeyValuePair<string, string>[] {
                    new KeyValuePair<string, string>(
                        "enabled",
                        UnityPackageManagerResolver.Enable.ToString()),
                    new KeyValuePair<string, string>(
                        "promptToAddRegistries",
                        UnityPackageManagerResolver.PromptToAddRegistries.ToString()),
                    new KeyValuePair<string, string>(
                        "verboseLoggingEnabled",
                        UnityPackageManagerResolver.VerboseLoggingEnabled.ToString()),
                },
                "Settings Save");
            settings.Save();
            Close();

            UnityPackageManagerResolver.CheckRegistries();
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        // Re-enable GUI
        GUI.enabled = true;
    }
}

}  // namespace Google


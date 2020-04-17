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
/// Settings dialog for PackageManagerResolver.
/// </summary>
public class PackageManagerResolverSettingsDialog : EditorWindow
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
        /// Whether to prompt the user to migrate Version Handler to UPM packages after a
        /// registry has been added.
        /// </summary>
        internal bool promptToMigratePackages;

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
            enable = PackageManagerResolver.Enable;
            promptToAddRegistries = PackageManagerResolver.PromptToAddRegistries;
            promptToMigratePackages = PackageManagerResolver.PromptToMigratePackages;
            verboseLoggingEnabled = PackageManagerResolver.VerboseLoggingEnabled;
            useProjectSettings = PackageManagerResolver.UseProjectSettings;
            analyticsSettings =
                new EditorMeasurement.Settings(PackageManagerResolver.analytics);
        }

        /// <summary>
        /// Save dialog settings to preferences.
        /// </summary>
        internal void Save() {
            PackageManagerResolver.Enable = enable;
            PackageManagerResolver.PromptToAddRegistries = promptToAddRegistries;
            PackageManagerResolver.PromptToMigratePackages = promptToMigratePackages;
            PackageManagerResolver.VerboseLoggingEnabled = verboseLoggingEnabled;
            PackageManagerResolver.UseProjectSettings = useProjectSettings;
            analyticsSettings.Save();
        }
    }

    private Settings settings;

    private Vector2 scrollPosition = new Vector2(0, 0);

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
        minSize = new Vector2(350, 430);
        position = new Rect(UnityEngine.Screen.width / 3,
                            UnityEngine.Screen.height / 3,
                            minSize.x, minSize.y);
        PackageManagerResolver.analytics.Report("settings/show", "Settings");
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
                                      PackageManagerResolver.PLUGIN_NAME,
                                      PackageManagerResolverVersionNumber.Value.Major,
                                      PackageManagerResolverVersionNumber.Value.Minor,
                                      PackageManagerResolverVersionNumber.Value.Build));

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        if (!PackageManagerResolver.ScopedRegistriesSupported) {
            GUILayout.Label(
                String.Format("Only supported from Unity {0} and above.",
                    PackageManagerResolver.MinimumUnityVersionString));
        }

        // Disable all GUI if scoped registries are not supported.
        GUI.enabled = PackageManagerResolver.ScopedRegistriesSupported;

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

        GUILayout.BeginHorizontal();
        GUILayout.Label("Prompt to migrate packages", EditorStyles.boldLabel);
        settings.promptToMigratePackages = EditorGUILayout.Toggle(settings.promptToMigratePackages);
        GUILayout.EndHorizontal();
        GUILayout.Label("When this option is enabled, this plugin will search the Unity Package " +
                        "Manager (UPM) for available packages that are currently installed in " +
                        "the project in the `Assets` directory that have equivalent or newer " +
                        "versions available on UPM and prompt to migrate these packages.");

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
            PackageManagerResolver.RestoreDefaultSettings();
            PackageManagerResolver.analytics.Report("settings/reset", "Settings Reset");
            LoadSettings();
            backupSettings.Save();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cancel")) {
            PackageManagerResolver.analytics.Report("settings/cancel", "Settings Cancel");
            Close();
        }
        if (GUILayout.Button("OK")) {
            PackageManagerResolver.analytics.Report(
                "settings/save",
                new KeyValuePair<string, string>[] {
                    new KeyValuePair<string, string>(
                        "enabled",
                        PackageManagerResolver.Enable.ToString()),
                    new KeyValuePair<string, string>(
                        "promptToAddRegistries",
                        PackageManagerResolver.PromptToAddRegistries.ToString()),
                    new KeyValuePair<string, string>(
                        "promptToMigratePackages",
                        PackageManagerResolver.PromptToMigratePackages.ToString()),
                    new KeyValuePair<string, string>(
                        "verboseLoggingEnabled",
                        PackageManagerResolver.VerboseLoggingEnabled.ToString()),
                },
                "Settings Save");
            settings.Save();
            Close();

            PackageManagerResolver.CheckRegistries();
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();

        // Re-enable GUI
        GUI.enabled = true;
    }
}

}  // namespace Google


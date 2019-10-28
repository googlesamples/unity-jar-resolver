// <copyright file="SettingsDialog.cs" company="Google Inc.">
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

namespace Google {

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Settings dialog for VersionHandlerImpl.
/// </summary>
public class SettingsDialog : EditorWindow
{
    /// <summary>
    /// Loads / saves settings for this dialog.
    /// </summary>
    private class Settings {
        /// <summary>
        /// Whether the version handler is enabled.
        /// </summary>
        internal bool enabled;

        /// <summary>
        /// Whether to prompt the user before deleting obsolete files.
        /// </summary>
        internal bool cleanUpPromptEnabled;

        /// <summary>
        /// Whether to rename files to canonical filenames.
        /// </summary>
        internal bool renameToCanonicalFilenames;

        /// <summary>
        /// Whether to enable / disable verbose logging.
        /// </summary>
        internal bool verboseLoggingEnabled;

        /// <summary>
        /// Whether settings are project specific.
        /// </summary>
        internal bool useProjectSettings;

        /// <summary>
        /// Whether files can be disabled by renaming them.
        /// </summary>
        internal bool renameToDisableFilesEnabled;

        /// <summary>
        /// Analytics settings.
        /// </summary>
        internal EditorMeasurement.Settings analyticsSettings;

        /// <summary>
        /// Load settings into the dialog.
        /// </summary>
        internal Settings() {
            enabled = VersionHandlerImpl.Enabled;
            cleanUpPromptEnabled = VersionHandlerImpl.CleanUpPromptEnabled;
            renameToCanonicalFilenames = VersionHandlerImpl.RenameToCanonicalFilenames;
            verboseLoggingEnabled = VersionHandlerImpl.VerboseLoggingEnabled;
            useProjectSettings = VersionHandlerImpl.UseProjectSettings;
            renameToDisableFilesEnabled = VersionHandlerImpl.RenameToDisableFilesEnabled;
            analyticsSettings = new EditorMeasurement.Settings(VersionHandlerImpl.analytics);
        }

        /// <summary>
        /// Save dialog settings to preferences.
        /// </summary>
        internal void Save() {
            VersionHandlerImpl.Enabled = enabled;
            VersionHandlerImpl.CleanUpPromptEnabled = cleanUpPromptEnabled;
            VersionHandlerImpl.RenameToCanonicalFilenames = renameToCanonicalFilenames;
            VersionHandlerImpl.VerboseLoggingEnabled = verboseLoggingEnabled;
            VersionHandlerImpl.UseProjectSettings = useProjectSettings;
            VersionHandlerImpl.RenameToDisableFilesEnabled = renameToDisableFilesEnabled;
            analyticsSettings.Save();
            VersionHandlerImpl.BuildTargetChecker.HandleSettingsChanged();
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
        minSize = new Vector2(300, 288);
        position = new Rect(UnityEngine.Screen.width / 3,
                            UnityEngine.Screen.height / 3,
                            minSize.x, minSize.y);
        VersionHandlerImpl.analytics.Report("settings/show", "Settings");
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
        GUILayout.Label(String.Format("Version Handler (version {0}.{1}.{2})",
                                      VersionHandlerVersionNumber.Value.Major,
                                      VersionHandlerVersionNumber.Value.Minor,
                                      VersionHandlerVersionNumber.Value.Build));

        GUILayout.BeginHorizontal();
        GUILayout.Label("Enable version management", EditorStyles.boldLabel);
        settings.enabled = EditorGUILayout.Toggle(settings.enabled);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Rename to canonical filenames",
                        EditorStyles.boldLabel);
        settings.renameToCanonicalFilenames =
            EditorGUILayout.Toggle(settings.renameToCanonicalFilenames);
        GUILayout.EndHorizontal();
        GUILayout.Label("When this option is enabled the Version Handler strips " +
                        "metadata from filenames.  This can be a *very* slow operation " +
                        "as each renamed DLL causes the Unity editor to reload all DLLs.");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Prompt for obsolete file deletion",
                        EditorStyles.boldLabel);
        settings.cleanUpPromptEnabled = EditorGUILayout.Toggle(settings.cleanUpPromptEnabled);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Allow disabling files via renaming", EditorStyles.boldLabel);
        settings.renameToDisableFilesEnabled =
                                EditorGUILayout.Toggle(settings.renameToDisableFilesEnabled);
        GUILayout.EndHorizontal();

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
            VersionHandlerImpl.RestoreDefaultSettings();
            VersionHandlerImpl.analytics.Report("settings/reset", "Settings Reset");
            LoadSettings();
            backupSettings.Save();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cancel")) {
            VersionHandlerImpl.analytics.Report("settings/cancel", "Settings Cancel");
            Close();
        }
        if (GUILayout.Button("OK")) {
            VersionHandlerImpl.analytics.Report(
                "settings/save",
                new KeyValuePair<string, string>[] {
                    new KeyValuePair<string, string>(
                        "enabled",
                        VersionHandlerImpl.Enabled.ToString()),
                    new KeyValuePair<string, string>(
                        "cleanUpPromptEnabled",
                        VersionHandlerImpl.CleanUpPromptEnabled.ToString()),
                    new KeyValuePair<string, string>(
                        "renameToCanonicalFilenames",
                        VersionHandlerImpl.RenameToCanonicalFilenames.ToString()),
                    new KeyValuePair<string, string>(
                        "verboseLoggingEnabled",
                        VersionHandlerImpl.VerboseLoggingEnabled.ToString()),
                    new KeyValuePair<string, string>(
                        "renameToDisableFilesEnabled",
                        VersionHandlerImpl.RenameToDisableFilesEnabled.ToString()),
                },
                "Settings Save");
            settings.Save();
            Close();
            // If the handler has been enabled, refresh the asset database
            // to force it to run.
            if (settings.enabled) {
                AssetDatabase.Refresh();
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }
}

}  // namespace Google


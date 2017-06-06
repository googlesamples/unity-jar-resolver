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

using UnityEditor;
using UnityEngine;

/// <summary>
/// Settings dialog for VersionHandler.
/// </summary>
public class SettingsDialog : EditorWindow
{
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
    /// Setup the window's initial position and size.
    /// </summary>
    public void Initialize()
    {
        minSize = new Vector2(300, 200);
        position = new Rect(UnityEngine.Screen.width / 3,
                            UnityEngine.Screen.height / 3,
                            minSize.x, minSize.y);
    }

    /// <summary>
    /// Called when the window is loaded.
    /// </summary>
    public void OnEnable()
    {
        enabled = VersionHandler.Enabled;
        cleanUpPromptEnabled = VersionHandler.CleanUpPromptEnabled;
        renameToCanonicalFilenames = VersionHandler.RenameToCanonicalFilenames;
        verboseLoggingEnabled = VersionHandler.VerboseLoggingEnabled;
    }

    /// <summary>
    /// Called when the GUI should be rendered.
    /// </summary>
    public void OnGUI()
    {
        GUI.skin.label.wordWrap = true;
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Enable version management", EditorStyles.boldLabel);
        enabled = EditorGUILayout.Toggle(enabled);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Rename to canonical filenames",
                        EditorStyles.boldLabel);
        renameToCanonicalFilenames = EditorGUILayout.Toggle(renameToCanonicalFilenames);
        GUILayout.EndHorizontal();
        GUILayout.Label("When this option is enabled the Version Handler strips " +
                        "metadata from filenames.  This can be a *very* slow operation " +
                        "as each renamed DLL causes the Unity editor to reload all DLLs.");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Prompt for obsolete file deletion",
                        EditorStyles.boldLabel);
        cleanUpPromptEnabled = EditorGUILayout.Toggle(cleanUpPromptEnabled);
        GUILayout.EndHorizontal();


        GUILayout.BeginHorizontal();
        GUILayout.Label("Verbose logging", EditorStyles.boldLabel);
        verboseLoggingEnabled = EditorGUILayout.Toggle(verboseLoggingEnabled);
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        if (GUILayout.Button("OK")) {
            VersionHandler.Enabled = enabled;
            VersionHandler.CleanUpPromptEnabled = cleanUpPromptEnabled;
            VersionHandler.RenameToCanonicalFilenames = renameToCanonicalFilenames;
            VersionHandler.VerboseLoggingEnabled = verboseLoggingEnabled;
            Close();
            // If the handler has been enabled, refresh the asset database
            // to force it to run.
            if (enabled) {
                AssetDatabase.Refresh();
            }
        }
        GUILayout.EndVertical();
    }
}

}  // namespace Google


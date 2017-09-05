// <copyright file="IOResolverSettingsDialog.cs" company="Google Inc.">
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

namespace Google {

using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Settings dialog for IOSResolver.
/// </summary>
public class IOSResolverSettingsDialog : EditorWindow
{
    /// <summary>
    /// Loads / saves settings for this dialog.
    /// </summary>
    private class Settings {
        internal bool cocoapodsInstallEnabled;
        internal bool podfileGenerationEnabled;
        internal bool podToolExecutionViaShellEnabled;
        internal bool autoPodToolInstallInEditorEnabled;
        internal bool verboseLoggingEnabled;
        internal int cocoapodsIntegrationMenuIndex;

        /// <summary>
        /// Load settings into the dialog.
        /// </summary>
        internal Settings() {
            cocoapodsInstallEnabled = IOSResolver.CocoapodsInstallEnabled;
            podfileGenerationEnabled = IOSResolver.PodfileGenerationEnabled;
            podToolExecutionViaShellEnabled = IOSResolver.PodToolExecutionViaShellEnabled;
            autoPodToolInstallInEditorEnabled = IOSResolver.AutoPodToolInstallInEditorEnabled;
            verboseLoggingEnabled = IOSResolver.VerboseLoggingEnabled;
            cocoapodsIntegrationMenuIndex = FindIndexFromCocoapodsIntegrationMethod(
                IOSResolver.CocoapodsIntegrationMethodPref);
        }

        /// <summary>
        /// Save dialog settings to preferences.
        /// </summary>
        internal void Save() {
            IOSResolver.PodfileGenerationEnabled = podfileGenerationEnabled;
            IOSResolver.CocoapodsInstallEnabled = cocoapodsInstallEnabled;
            IOSResolver.PodToolExecutionViaShellEnabled = podToolExecutionViaShellEnabled;
            IOSResolver.AutoPodToolInstallInEditorEnabled = autoPodToolInstallInEditorEnabled;
            IOSResolver.VerboseLoggingEnabled = verboseLoggingEnabled;
            IOSResolver.CocoapodsIntegrationMethodPref =
                integrationMapping[cocoapodsIntegrationMenuIndex];
        }
    }

    private Settings settings;

    static string[] cocopodsIntegrationStrings = new string[] {
        "Xcode Workspace - Add Cocoapods to the Xcode workspace",
        "Xcode Project - Add Cocoapods to the Xcode project",
        "None - Do not integrate Cocoapods.",
    };

    // Menu item index to enum.
    private static IOSResolver.CocoapodsIntegrationMethod[] integrationMapping =
            new IOSResolver.CocoapodsIntegrationMethod[] {
        IOSResolver.CocoapodsIntegrationMethod.Workspace,
        IOSResolver.CocoapodsIntegrationMethod.Project,
        IOSResolver.CocoapodsIntegrationMethod.None,
    };

    // enum to index (linear search because there's no point in creating a reverse mapping
    // with such a small list).
    private static int FindIndexFromCocoapodsIntegrationMethod(
            IOSResolver.CocoapodsIntegrationMethod enumToFind) {
        for (int i = 0; i < integrationMapping.Length; i++) {
            if (integrationMapping[i] == enumToFind) return i;
        }
        throw new System.ArgumentException("Invalid CocoapodsIntegrationMethod.");
    }

    public void Initialize() {
        minSize = new Vector2(400, 310);
        position = new Rect(UnityEngine.Screen.width / 3, UnityEngine.Screen.height / 3,
                            minSize.x, minSize.y);
    }

    /// <summary>
    /// Load settings for this dialog.
    /// </summary>
    private void LoadSettings() {
        settings = new Settings();
    }

    public void OnEnable() {
        LoadSettings();
    }

    /// <summary>
    /// Called when the GUI should be rendered.
    /// </summary>
    public void OnGUI() {
        GUI.skin.label.wordWrap = true;
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Podfile Generation", EditorStyles.boldLabel);
        settings.podfileGenerationEnabled =
            EditorGUILayout.Toggle(settings.podfileGenerationEnabled);
        GUILayout.EndHorizontal();
        GUILayout.Label("Podfile generation is required to install Cocoapods.  " +
                        "It may be desirable to disable Podfile generation if frameworks " +
                        "are manually included in Unity's generated Xcode project.");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Cocoapods Integration", EditorStyles.boldLabel);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        settings.cocoapodsIntegrationMenuIndex = EditorGUILayout.Popup(
            settings.cocoapodsIntegrationMenuIndex, cocopodsIntegrationStrings);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (integrationMapping[settings.cocoapodsIntegrationMenuIndex] !=
                IOSResolver.CocoapodsIntegrationMethod.None && !settings.podfileGenerationEnabled) {
            GUILayout.Label("Cocoapod installation requires Podfile generation to be enabled.");
        } else if (integrationMapping[settings.cocoapodsIntegrationMenuIndex] ==
                   IOSResolver.CocoapodsIntegrationMethod.Workspace) {
            GUILayout.Label("Unity Cloud Build and Unity 5.5 and below do not open generated " +
                            "Xcode workspaces so this plugin will fall back to Xcode Project " +
                            "integration in those environments.");
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Use Shell to Execute Cocoapod Tool", EditorStyles.boldLabel);
        settings.podToolExecutionViaShellEnabled =
            EditorGUILayout.Toggle(settings.podToolExecutionViaShellEnabled);
        GUILayout.EndHorizontal();
        if (settings.podToolExecutionViaShellEnabled) {
            GUILayout.Label("Shell execution is useful when configuration in the shell " +
                            "environment (e.g ~/.profile) is required to execute Cocoapods tools.");
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Auto Install Cocoapod Tools in Editor", EditorStyles.boldLabel);
        settings.autoPodToolInstallInEditorEnabled =
            EditorGUILayout.Toggle(settings.autoPodToolInstallInEditorEnabled);
        GUILayout.EndHorizontal();
        if (settings.autoPodToolInstallInEditorEnabled) {
            GUILayout.Label("Automatically installs the Cocoapod tool if the editor isn't " +
                            "running in batch mode");
        } else {
            GUILayout.Label("Cocoapod tool installation can be performed via the menu option: " +
                            "Assets > Play Services Resolver > iOS Resolver > Install Cocoapods");
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Verbose Logging", EditorStyles.boldLabel);
        settings.verboseLoggingEnabled = EditorGUILayout.Toggle(settings.verboseLoggingEnabled);
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        if (GUILayout.Button("Reset to Defaults")) {
            // Load default settings into the dialog but preserve the state in the user's
            // saved preferences.
            var backupSettings = new Settings();
            IOSResolver.RestoreDefaultSettings();
            LoadSettings();
            backupSettings.Save();
        }

        GUILayout.BeginHorizontal();
        bool closeWindow = GUILayout.Button("Cancel");
        bool ok = GUILayout.Button("OK");
        closeWindow |= ok;
        if (ok) settings.Save();
        if (closeWindow) Close();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }
}

}  // namespace Google


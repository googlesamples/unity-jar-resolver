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
    bool cocoapodsInstallEnabled;
    bool podfileGenerationEnabled;
    bool podToolExecutionViaShellEnabled;
    bool verboseLoggingEnabled;

    public void Initialize() {
        minSize = new Vector2(300, 200);
        position = new Rect(UnityEngine.Screen.width / 3, UnityEngine.Screen.height / 3,
                            minSize.x, minSize.y);
    }

    public void OnEnable() {
        cocoapodsInstallEnabled = IOSResolver.CocoapodsInstallEnabled;
        podfileGenerationEnabled = IOSResolver.PodfileGenerationEnabled;
        podToolExecutionViaShellEnabled = IOSResolver.PodToolExecutionViaShellEnabled;
        verboseLoggingEnabled = IOSResolver.VerboseLoggingEnabled;
    }

    /// <summary>
    /// Called when the GUI should be rendered.
    /// </summary>
    public void OnGUI() {
        GUI.skin.label.wordWrap = true;
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Podfile Generation", EditorStyles.boldLabel);
        podfileGenerationEnabled = EditorGUILayout.Toggle(podfileGenerationEnabled);
        GUILayout.EndHorizontal();
        GUILayout.Label("Podfile generation is required to install Cocoapods.  " +
                        "It may be desirable to disable Podfile generation if frameworks " +
                        "are manually included in Unity's generated Xcode project.");

        EditorGUI.BeginDisabledGroup(podfileGenerationEnabled == false);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Cocoapods Installation", EditorStyles.boldLabel);
        cocoapodsInstallEnabled = EditorGUILayout.Toggle(cocoapodsInstallEnabled);
        GUILayout.EndHorizontal();
        if (!podfileGenerationEnabled) {
            GUILayout.Label("Cocoapod installation requires Podfile generation to be enabled.");
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Use Shell to Execute Cocoapod Tool", EditorStyles.boldLabel);
        podToolExecutionViaShellEnabled = EditorGUILayout.Toggle(podToolExecutionViaShellEnabled);
        GUILayout.EndHorizontal();
        if (podToolExecutionViaShellEnabled) {
            GUILayout.Label("When shell execution is enabled it is not possible to redirect " +
                            "error messages to Unity's console window.");
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Verbose Logging", EditorStyles.boldLabel);
        verboseLoggingEnabled = EditorGUILayout.Toggle(verboseLoggingEnabled);
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        bool closeWindow = GUILayout.Button("Cancel");
        bool ok = GUILayout.Button("OK");
        closeWindow |= ok;
        if (ok)
        {
            IOSResolver.PodfileGenerationEnabled = podfileGenerationEnabled;
            IOSResolver.CocoapodsInstallEnabled = cocoapodsInstallEnabled;
            IOSResolver.PodToolExecutionViaShellEnabled = podToolExecutionViaShellEnabled;
            IOSResolver.VerboseLoggingEnabled = verboseLoggingEnabled;
        }
        if (closeWindow) Close();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }
}

}  // namespace Google


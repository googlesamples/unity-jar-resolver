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

using System;
using System.Collections.Generic;
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
        internal bool podfileGenerationEnabled;
        internal bool podToolExecutionViaShellEnabled;
        internal bool podToolShellExecutionSetLang;
        internal bool autoPodToolInstallInEditorEnabled;
        internal bool verboseLoggingEnabled;
        internal int cocoapodsIntegrationMenuIndex;
        internal bool podfileAddUseFrameworks;
        internal bool podfileStaticLinkFrameworks;
        internal bool swiftFrameworkSupportWorkaroundEnabled;
        internal string swiftLanguageVersion;
        internal bool podfileAlwaysAddMainTarget;
        internal bool podfileAllowPodsInMultipleTargets;
        internal bool useProjectSettings;
        internal EditorMeasurement.Settings analyticsSettings;

        /// <summary>
        /// Load settings into the dialog.
        /// </summary>
        internal Settings() {
            podfileGenerationEnabled = IOSResolver.PodfileGenerationEnabled;
            podToolExecutionViaShellEnabled = IOSResolver.PodToolExecutionViaShellEnabled;
            podToolShellExecutionSetLang = IOSResolver.PodToolShellExecutionSetLang;
            autoPodToolInstallInEditorEnabled = IOSResolver.AutoPodToolInstallInEditorEnabled;
            verboseLoggingEnabled = IOSResolver.VerboseLoggingEnabled;
            cocoapodsIntegrationMenuIndex = FindIndexFromCocoapodsIntegrationMethod(
                IOSResolver.CocoapodsIntegrationMethodPref);
            podfileAddUseFrameworks = IOSResolver.PodfileAddUseFrameworks;
            podfileStaticLinkFrameworks = IOSResolver.PodfileStaticLinkFrameworks;
            swiftFrameworkSupportWorkaroundEnabled =
                IOSResolver.SwiftFrameworkSupportWorkaroundEnabled;
            swiftLanguageVersion = IOSResolver.SwiftLanguageVersion;
            podfileAlwaysAddMainTarget = IOSResolver.PodfileAlwaysAddMainTarget;
            podfileAllowPodsInMultipleTargets = IOSResolver.PodfileAllowPodsInMultipleTargets;
            useProjectSettings = IOSResolver.UseProjectSettings;
            analyticsSettings = new EditorMeasurement.Settings(IOSResolver.analytics);
        }

        /// <summary>
        /// Save dialog settings to preferences.
        /// </summary>
        internal void Save() {
            IOSResolver.PodfileGenerationEnabled = podfileGenerationEnabled;
            IOSResolver.PodToolExecutionViaShellEnabled = podToolExecutionViaShellEnabled;
            IOSResolver.PodToolShellExecutionSetLang = podToolShellExecutionSetLang;
            IOSResolver.AutoPodToolInstallInEditorEnabled = autoPodToolInstallInEditorEnabled;
            IOSResolver.VerboseLoggingEnabled = verboseLoggingEnabled;
            IOSResolver.CocoapodsIntegrationMethodPref =
                integrationMapping[cocoapodsIntegrationMenuIndex];
            IOSResolver.PodfileAddUseFrameworks = podfileAddUseFrameworks;
            IOSResolver.PodfileStaticLinkFrameworks = podfileStaticLinkFrameworks;
            IOSResolver.SwiftFrameworkSupportWorkaroundEnabled =
                swiftFrameworkSupportWorkaroundEnabled;
            IOSResolver.SwiftLanguageVersion = swiftLanguageVersion;
            IOSResolver.PodfileAlwaysAddMainTarget = podfileAlwaysAddMainTarget;
            IOSResolver.PodfileAllowPodsInMultipleTargets = podfileAllowPodsInMultipleTargets;
            IOSResolver.UseProjectSettings = useProjectSettings;
            analyticsSettings.Save();
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

    private Vector2 scrollPosition = new Vector2(0, 0);

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
        minSize = new Vector2(400, 715);
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
        GUILayout.Label(String.Format("iOS Resolver (version {0}.{1}.{2})",
                                      IOSResolverVersionNumber.Value.Major,
                                      IOSResolverVersionNumber.Value.Minor,
                                      IOSResolverVersionNumber.Value.Build));

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

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
        GUILayout.Label("Shell execution is useful when configuration in the shell " +
                        "environment (e.g ~/.profile) is required to execute Cocoapods tools.");

        if (settings.podToolExecutionViaShellEnabled) {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Set LANG When Using Shell to Execute Cocoapod Tool", EditorStyles.boldLabel);
            settings.podToolShellExecutionSetLang =
                EditorGUILayout.Toggle(settings.podToolShellExecutionSetLang);
            GUILayout.EndHorizontal();
            GUILayout.Label("Useful for versions of cocoapods that depend on the value of LANG.");
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
            GUILayout.Label(
                "Cocoapod tool installation can be performed via the menu option: " +
                "Assets > External Dependency Manager > iOS Resolver > Install Cocoapods");
        }

        if (settings.podfileGenerationEnabled) {
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Label("Podfile Configurations", EditorStyles.largeLabel);
            EditorGUILayout.Separator();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Add use_frameworks! to Podfile", EditorStyles.boldLabel);
            settings.podfileAddUseFrameworks =
                EditorGUILayout.Toggle(settings.podfileAddUseFrameworks);
            GUILayout.EndHorizontal();

            GUILayout.Label("Add the following line to Podfile. Required if any third-party " +
                            "Unity packages depends on Swift frameworks.");
            if (settings.podfileStaticLinkFrameworks) {
                GUILayout.Label("  use_frameworks! :linkage => :static");
            } else {
                GUILayout.Label("  use_frameworks!");
            }

            if (settings.podfileAddUseFrameworks) {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Link frameworks statically", EditorStyles.boldLabel);
                settings.podfileStaticLinkFrameworks =
                    EditorGUILayout.Toggle(settings.podfileStaticLinkFrameworks);
                GUILayout.EndHorizontal();
                GUILayout.Label("Link frameworks statically is recommended just in case any pod " +
                                "framework includes static libraries.");
            }

            if (IOSResolver.MultipleXcodeTargetsSupported) {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Always add the main target to Podfile", EditorStyles.boldLabel);
                settings.podfileAlwaysAddMainTarget =
                    EditorGUILayout.Toggle(settings.podfileAlwaysAddMainTarget);
                GUILayout.EndHorizontal();

                GUILayout.Label("Add the following lines to Podfile.");
                GUILayout.Label(String.Format("  target '{0}' do\n" +
                                              "  end", IOSResolver.XcodeMainTargetName));

                if (settings.podfileAlwaysAddMainTarget) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Allow the same pod to be in multiple targets",
                        EditorStyles.boldLabel);
                    settings.podfileAllowPodsInMultipleTargets =
                        EditorGUILayout.Toggle(settings.podfileAllowPodsInMultipleTargets);
                    GUILayout.EndHorizontal();

                    GUILayout.Label("Allow to add the same pod to multiple targets, if specified in " +
                                    "Dependencies.xml with 'addToAllTargets' attribute.");
                }
            }

            if (settings.podfileAddUseFrameworks) {
                GUILayout.BeginHorizontal();
                GUILayout.Label("(Recommended) Enable Swift Framework Support Workaround",
                    EditorStyles.boldLabel);
                settings.swiftFrameworkSupportWorkaroundEnabled =
                    EditorGUILayout.Toggle(settings.swiftFrameworkSupportWorkaroundEnabled);
                GUILayout.EndHorizontal();
                GUILayout.Label("This workround patches the Xcode project to properly link Swift " +
                                "Standard Library when some plugins depend on Swift Framework " +
                                "pods by:");
                GUILayout.Label("1. Add a dummy Swift file to Xcode project.");
                GUILayout.Label("2. Enable 'CLANG_ENABLE_MODULES' build settings and set " +
                                "'SWIFT_VERSION' to the value below.");

                if (settings.swiftFrameworkSupportWorkaroundEnabled) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Swift Framework Version",
                        EditorStyles.boldLabel);
                    settings.swiftLanguageVersion =
                        EditorGUILayout.TextField(settings.swiftLanguageVersion);
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Used to override 'SWIFT_VERSION' build setting in Xcode. " +
                                    "Leave it blank to prevent override.");
                }
            }

            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
        }

        settings.analyticsSettings.RenderGui();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Verbose Logging", EditorStyles.boldLabel);
        settings.verboseLoggingEnabled = EditorGUILayout.Toggle(settings.verboseLoggingEnabled);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Use project settings", EditorStyles.boldLabel);
        settings.useProjectSettings = EditorGUILayout.Toggle(settings.useProjectSettings);
        GUILayout.EndHorizontal();

        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUILayout.BeginVertical();
        GUILayout.Space(10);
        if (GUILayout.Button("Reset to Defaults")) {
            // Load default settings into the dialog but preserve the state in the user's
            // saved preferences.
            var backupSettings = new Settings();
            IOSResolver.RestoreDefaultSettings();
            IOSResolver.analytics.Report("settings/reset", "Settings Reset");
            LoadSettings();
            backupSettings.Save();
        }

        GUILayout.BeginHorizontal();
        bool closeWindow = GUILayout.Button("Cancel");
        if (closeWindow) IOSResolver.analytics.Report("settings/cancel", "Settings Cancel");
        bool ok = GUILayout.Button("OK");
        closeWindow |= ok;
        if (ok) {
            IOSResolver.analytics.Report(
                "settings/save",
                new KeyValuePair<string, string>[] {
                    new KeyValuePair<string, string>(
                        "podfileGenerationEnabled",
                        IOSResolver.PodfileGenerationEnabled.ToString()),
                    new KeyValuePair<string, string>(
                        "podToolExecutionViaShellEnabled",
                        IOSResolver.PodToolExecutionViaShellEnabled.ToString()),
                    new KeyValuePair<string, string>(
                        "podToolShellExecutionSetLang",
                        IOSResolver.PodToolShellExecutionSetLang.ToString()),
                    new KeyValuePair<string, string>(
                        "autoPodToolInstallInEditorEnabled",
                        IOSResolver.AutoPodToolInstallInEditorEnabled.ToString()),
                    new KeyValuePair<string, string>(
                        "verboseLoggingEnabled",
                        IOSResolver.VerboseLoggingEnabled.ToString()),
                    new KeyValuePair<string, string>(
                        "cocoapodsIntegrationMethod",
                        IOSResolver.CocoapodsIntegrationMethodPref.ToString()),
                    new KeyValuePair<string, string>(
                        "podfileAddUseFrameworks",
                        IOSResolver.PodfileAddUseFrameworks.ToString()),
                    new KeyValuePair<string, string>(
                        "podfileStaticLinkFrameworks",
                        IOSResolver.PodfileStaticLinkFrameworks.ToString()),
                    new KeyValuePair<string, string>(
                        "podfileAlwaysAddMainTarget",
                        IOSResolver.PodfileAlwaysAddMainTarget.ToString()),
                    new KeyValuePair<string, string>(
                        "podfileAllowPodsInMultipleTargets",
                        IOSResolver.PodfileAllowPodsInMultipleTargets.ToString()),
                    new KeyValuePair<string, string>(
                        "swiftFrameworkSupportWorkaroundEnabled",
                        IOSResolver.SwiftFrameworkSupportWorkaroundEnabled.ToString()),
                    new KeyValuePair<string, string>(
                        "swiftLanguageVersion",
                        IOSResolver.SwiftLanguageVersion.ToString()),
                },
                "Settings Save");
            settings.Save();
        }
        if (closeWindow) Close();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }
}

}  // namespace Google


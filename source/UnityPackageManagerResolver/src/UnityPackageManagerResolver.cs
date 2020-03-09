// <copyright file="UnityPackageManagerResolver.cs" company="Google LLC">
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

using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Google {

[InitializeOnLoad]
public class UnityPackageManagerResolver {
    /// <summary>
    /// Enables / disables external package registries for Unity Package
    /// Manager.
    /// </summary>
    static UnityPackageManagerResolver() {
        logger.Log("Loaded UnityPackageManagerResolver", LogLevel.Verbose);

        RunOnMainThread.Run(() => {
                // Load log preferences.
                VerboseLoggingEnabled = VerboseLoggingEnabled;

                // Turn off the feature immediately scoped registry is not support.
                // This is for the case when the user downgrade Unity version.
                if (!ScopedRegistrySupported) Enable = false;

                CheckRegistries();
            }, runNow: false);
    }

    /// <summary>
    /// Add the settings dialog for this module to the menu and show the
    /// window when the menu item is selected.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Unity Package Manager Resolver/Settings")]
    public static void ShowSettings() {
         UnityPackageManagerResolverSettingsDialog window =
             (UnityPackageManagerResolverSettingsDialog)EditorWindow.GetWindow(
             typeof(UnityPackageManagerResolverSettingsDialog), true, PluginName + " Settings");
         window.Initialize();
         window.Show();
    }

    /// <summary>
    /// Check registry status based on current settings.
    /// </summary>
    internal static void CheckRegistries() {
        if (!ScopedRegistrySupported) return;

        if (!ExecutionEnvironment.InBatchMode) {
            PromptToEnable();
        }

        UpdateManifest(Enable);
    }

    internal static string PromptTitleText = "Add Game Package Registry by Google?";
    internal static string PromptMessageText =
        "This will enable you to discover packages from Google through Unity Package Manager.";
    internal static string PromptOkText = "OK";
    internal static string PromptCancelText = "Cancel";

    /// <summary>
    /// Check registry status based on current settings.
    /// </summary>
    internal static void PromptToEnable() {
        if (!ScopedRegistrySupported) return;

        if (PromptForAddRegistry && !Enable) {
            bool result = EditorUtility.DisplayDialog(
                PromptTitleText,
                PromptMessageText,
                PromptOkText,
                PromptCancelText);
            Enable = result;
            UnityPackageManagerResolver.analytics.Report(
                "consent/dialog", String.Format("Prompt response: {0}", result ? "Yes" : "No"));
        }
        PromptForAddRegistry = false;
    }

    [MenuItem("Assets/External Dependency Manager/Unity Package Manager Resolver/Add Registries")]
    public static void AddRegistries() {
        UpdateManifest(true);
    }

    [MenuItem("Assets/External Dependency Manager/Unity Package Manager Resolver/Remove Registries")]
    public static void RemoveRegistries() {
        UpdateManifest(false);
    }

    /// <summary>
    /// Update manifest file based on the settings.
    /// </summary>
    public static void UpdateManifest(bool enable) {
        if (!ScopedRegistrySupported) return;

        PackageManifestModifier modifier = new PackageManifestModifier() { Logger = logger };
        if (!modifier.ReadManifest()) {
            UnityPackageManagerResolver.analytics.Report(
               "/registry_manifest/read/failed",
               "Update Manifest failed: Read/Parse manifest failed");
            Enable = false;
            return;
        }

        bool manifestModified = false;

        List<Dictionary<string, object>> foundRegistries =
            modifier.SearchRegistries(PackageManifestModifier.GOOGLE_REGISTRY_URL);

        bool registryExists = foundRegistries.Count > 0;

        if (enable && !registryExists) {
            logger.Log(String.Format("Adding {0} (url: {1}) to manifest.json",
                PackageManifestModifier.GOOGLE_REGISTRY_NAME,
                PackageManifestModifier.GOOGLE_REGISTRY_URL), LogLevel.Info);
            modifier.AddRegistry(
                PackageManifestModifier.GOOGLE_REGISTRY_NAME,
                PackageManifestModifier.GOOGLE_REGISTRY_URL,
                PackageManifestModifier.GOOGLE_REGISTRY_SCOPES);

            manifestModified = true;
        } else if (!enable && registryExists) {
            logger.Log(String.Format("Removing {0} (url: {1}) from manifest.json",
                PackageManifestModifier.GOOGLE_REGISTRY_NAME,
                PackageManifestModifier.GOOGLE_REGISTRY_URL), LogLevel.Info);
            modifier.RemoveRegistries(foundRegistries);
            manifestModified = true;
        }

        if (manifestModified) {
            if (modifier.WriteManifest()) {
                logger.Log("Successfully updated manifest.json", LogLevel.Info);
            } else {
                UnityPackageManagerResolver.analytics.Report(
                       "/registry_manifest/write/failed",
                       "Update Manifest failed: Write manifest failed");
            }
        }
    }

    /// <summary>
    /// Reset settings of this plugin to default values.
    /// </summary>
    internal static void RestoreDefaultSettings() {
        settings.DeleteKeys(PreferenceKeys);
        analytics.RestoreDefaultSettings();
    }

    // Keys in the editor preferences which control the behavior of this
    // module.
    private const string PreferenceEnable =
        "Google.UnityPackageManagerResolver.Enable";
    private const string PreferencePromptToEnable =
        "Google.UnityPackageManagerResolver.PromptForAddRegistry";
    private const string PreferenceVerboseLoggingEnabled =
        "Google.UnityPackageManagerResolver.VerboseLoggingEnabled";
    // List of preference keys, used to restore default settings.
    private static string[] PreferenceKeys = new[] {
        PreferenceEnable,
        PreferencePromptToEnable,
        PreferenceVerboseLoggingEnabled
    };

    // Name of this plugin.
    private const string PluginName = "Google Unity Package Manager Resolver";

    // Unity started supporting scoped registries from 2018.4.
    internal const float MinimumUnityVersionFloat = 2018.4f;
    internal const string MinimumUnityVersionString = "2018.4";

    // Settings used by this module.
    internal static ProjectSettings settings =
        new ProjectSettings("Google.UnityPackageManagerResolver.");

    /// <summary>
    /// Logger for this module.
    /// </summary>
    private static Logger logger = new Logger();

    // Analytics reporter.
    internal static EditorMeasurement analytics =
        new EditorMeasurement(settings, logger, VersionHandlerImpl.GA_TRACKING_ID,
            "com.google.external-dependency-manager", "Unity PackageManager Resolver", "",
            VersionHandlerImpl.PRIVACY_POLICY) {
        BasePath = "/upmresolver/",
        BaseQuery =
            String.Format("version={0}", UnityPackageManagerResolverVersionNumber.Value.ToString()),
        BaseReportName = "Unity Package Manager Resolver: ",
        InstallSourceFilename = Assembly.GetAssembly(typeof(UnityPackageManagerResolver)).Location
    };

    /// <summary>
    /// Whether to use project level settings.
    /// </summary>
    public static bool UseProjectSettings {
        get { return settings.UseProjectSettings; }
        set { settings.UseProjectSettings = value; }
    }

    /// <summary>
    /// Enable / disable management of external registries.
    /// </summary>
    public static bool Enable {
        get {
            return settings.GetBool(PreferenceEnable, defaultValue: false);
        }
        set { settings.SetBool(PreferenceEnable, value); }
    }

    /// <summary>
    /// Enable / disable prompting the user to enable/disable registries.
    /// </summary>
    public static bool PromptForAddRegistry {
        get { return settings.GetBool(PreferencePromptToEnable,
                                      defaultValue: true); }
        set { settings.SetBool(PreferencePromptToEnable, value); }
    }

    /// <summary>
    /// Enable / disable verbose logging.
    /// </summary>
    public static bool VerboseLoggingEnabled {
        get { return settings.GetBool(PreferenceVerboseLoggingEnabled,
                                      defaultValue: false); }
        set {
            settings.SetBool(PreferenceVerboseLoggingEnabled, value);
            logger.Level = System.Environment.CommandLine.Contains("-batchmode") || value ?
                LogLevel.Verbose : LogLevel.Info;
        }
    }

    /// <summary>
    /// Whether scoped registry is supported in current Unity editor.
    /// </summary>
    public static bool ScopedRegistrySupported {
        get {
            return VersionHandler.GetUnityVersionMajorMinor() >= MinimumUnityVersionFloat;
        }
    }
}
} // namespace Google

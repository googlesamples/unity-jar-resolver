// <copyright file="TestResolverBootstrapped.cs" company="Google Inc.">
// Copyright (C) 2018 Google Inc. All Rights Reserved.
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
using System.Collections;
using System.IO;

[UnityEditor.InitializeOnLoad]
public class TestEnabledCallback {

    /// <summary>
    /// Get entry points for the plugin.
    /// </summary>
    /// <returns>Map of a human readable strings to loaded types.</returns>
    private static Dictionary<string, Type> EntryPoints {
        get {
            return new Dictionary<string, Type>() {
                {
                    "Version Handler Implementation",
                    Google.VersionHandler.FindClass("Google.VersionHandlerImpl",
                                                    "Google.VersionHandlerImpl")
                },
                {
                    "Android Resolver",
                    Google.VersionHandler.FindClass("Google.JarResolver",
                                                    "GooglePlayServices.PlayServicesResolver")
                },
                {
                    "IOS Resolver",
                    Google.VersionHandler.FindClass("Google.IOSResolver", "Google.IOSResolver")
                },
                {
                    "Package Manager Resolver",
                    Google.VersionHandler.FindClass("Google.PackageManagerResolver", "Google.PackageManagerResolver")
                }
            };
        }
    }

    /// <summary>
    /// Whether the test succeeded.
    /// </summary>
    private static bool testSucceeded = true;

    /// <summary>
    /// This module can be executed multiple times when the Version Handler is enabling
    /// so this method uses a temporary file to determine whether the module has been executed
    /// once in a Unity session.
    /// </summary>
    /// <returns>true if the module was previously initialized, false otherwise.</returns>
    private static bool SetInitialized() {
        const string INITIALIZED_PATH = "Temp/TestEnabledCallbackInitialized";
        if (File.Exists(INITIALIZED_PATH)) return true;
        File.WriteAllText(INITIALIZED_PATH, "Ready");
        return false;
    }

    /// <summary>
    /// Register a method to call when the Version Handler has enabled all plugins in the project.
    /// </summary>
    static TestEnabledCallback() {
        // Disable stack traces for more condensed logs.
        UnityEngine.Application.stackTraceLogType = UnityEngine.StackTraceLogType.None;
        UnityEngine.Debug.Log("Ensure plugin components are not loaded.");
        if (!SetInitialized()) {
            foreach (var kv in EntryPoints) {
                if (kv.Value != null) {
                    UnityEngine.Debug.LogError(
                        String.Format("Detected {0} ({1}) class when it should not be enabled " +
                                      "after batch mode import.", kv.Key, kv.Value));
                    testSucceeded = false;
                }
            }
        }
        UnityEngine.Debug.Log("Set up callback on Version Handler completion.");
        Google.VersionHandler.UpdateCompleteMethods = new [] {
            ":TestEnabledCallback:VersionHandlerReady"
        };
        UnityEngine.Debug.Log("Enable plugin using the Version Handler.");
        Google.VersionHandler.UpdateNow();
    }

    /// <summary>
    /// Called when the Version Handler has enabled all managed plugins in a project.
    /// </summary>
    public static void VersionHandlerReady() {
        UnityEngine.Debug.Log("The plugin should now be enabled by the Version Handler.");
        foreach (var kv in EntryPoints) {
            if (kv.Value == null) {
                UnityEngine.Debug.LogError(
                    String.Format("{0} class not detected, it should be enabled after " +
                                  "the Version Handler has been executed.", kv.Key));
                testSucceeded = false;
            }
        }
        if (!testSucceeded) {
            UnityEngine.Debug.Log("Test failed");
            UnityEditor.EditorApplication.Exit(1);
        }
        UnityEngine.Debug.Log("Test passed");
        UnityEditor.EditorApplication.Exit(0);
    }
}

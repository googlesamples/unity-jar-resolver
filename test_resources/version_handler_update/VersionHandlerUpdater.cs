// <copyright file="TestResolverBootstrapped.cs" company="Google Inc.">
// Copyright (C) 2023 Google Inc. All Rights Reserved.
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

/// <summary>
/// Run Version Handler to ensure all the libraries are properly enabled.
/// </summary>
[UnityEditor.InitializeOnLoad]
public class VersionHandlerUpdater {
    /// <summary>
    /// Register a method to call when the Version Handler has enabled all plugins in the project.
    /// </summary>
    static VersionHandlerUpdater() {
        // Disable stack traces for more condensed logs.
        UnityEngine.Application.stackTraceLogType = UnityEngine.StackTraceLogType.None;
        UnityEngine.Debug.Log("Set up callback on Version Handler completion.");
        Google.VersionHandler.UpdateCompleteMethods = new [] {
            ":VersionHandlerUpdater:VersionHandlerReady"
        };
        UnityEngine.Debug.Log("Enable plugin using the Version Handler.");
        Google.VersionHandler.UpdateNow();
    }

    /// <summary>
    /// Called when the Version Handler has enabled all managed plugins in a project.
    /// </summary>
    public static void VersionHandlerReady() {
        UnityEngine.Debug.Log("The plugin should now be enabled by the Version Handler.");
        UnityEditor.EditorApplication.Exit(0);
    }
}

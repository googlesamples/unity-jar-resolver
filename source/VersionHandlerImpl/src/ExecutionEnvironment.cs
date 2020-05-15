// <copyright file="ExecutionEnvironment.cs" company="Google Inc.">
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
using System.Globalization;
using UnityEngine;

namespace Google {

/// <summary>
/// Class that describes Unity's execution state.
/// </summary>
internal class ExecutionEnvironment {

    /// <summary>
    /// Whether the editor was started in batch mode.
    /// </summary>
    public static bool InBatchMode {
        get { return Environment.CommandLine.ToLower().Contains("-batchmode"); }
    }

    /// <summary>
    /// Whether the editor was started with a method to executed.
    /// </summary>
    public static bool ExecuteMethodEnabled {
        get { return Environment.CommandLine.ToLower().Contains("-executemethod"); }
    }

    /// <summary>
    /// Whether the UI should be treated as interactive.
    /// </summary>
    internal static bool InteractiveMode {
        get {
            return !(Environment.CommandLine.ToLower().Contains("-gvh_noninteractive") ||
                     ExecutionEnvironment.InBatchMode);
        }
    }

    /// <summary>
    /// If the Unity version can't be parsed, return a safe-ish version number.
    /// </summary>
    private const float DEFAULT_UNITY_VERSION_MAJOR_MINOR = 5.4f;

    // Cached Unity version.
    private static float unityVersionMajorMinor = -1.0f;

    /// <summary>
    /// Returns the major/minor version of the unity environment we are running in
    /// as a float so it can be compared numerically.
    /// If the default
    /// </summary>
    public static float VersionMajorMinor {
        get {
            if (unityVersionMajorMinor >= 0.0f) return unityVersionMajorMinor;
            float result = DEFAULT_UNITY_VERSION_MAJOR_MINOR;
            string version = Application.unityVersion;
            if (!string.IsNullOrEmpty(version)) {
                int dotIndex = version.IndexOf('.');
                if (dotIndex > 0 && version.Length > dotIndex + 1) {
                    if (!float.TryParse(version.Substring(0, dotIndex + 2), NumberStyles.Any,
                                        CultureInfo.InvariantCulture, out result)) {
                        result = DEFAULT_UNITY_VERSION_MAJOR_MINOR;
                    }
                }
            }
            unityVersionMajorMinor = result;
            return result;
        }
    }
}

}

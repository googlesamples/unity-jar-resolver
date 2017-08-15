// <copyright file="UnityCompat.cs" company="Google Inc.">
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
using UnityEditor;
using UnityEngine;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GooglePlayServices {
// TODO(butterfield): Move to a new assembly, for common use between plugins.

// Provides an API for accessing Unity APIs that work accross various verisons of Unity.
public class UnityCompat {
    private const string Namespace = "GooglePlayServices.";
    private const string ANDROID_MIN_SDK_FALLBACK_KEY = Namespace + "MinSDKVersionFallback";
    private const string ANDROID_PLATFORM_FALLBACK_KEY = Namespace + "PlatformVersionFallback";
    private const string ANDROID_BUILD_TOOLS_FALLBACK_KEY = Namespace + "BuildToolsVersionFallback";
    private const int DEFAULT_ANDROID_MIN_SDK = 14;
    private const int DEFAULT_PLATFORM_VERSION = 25;
    private const string DEFAULT_BUILD_TOOLS_VERSION = "25.0.2";

    private const string UNITY_ANDROID_VERSION_ENUM_PREFIX = "AndroidApiLevel";
    private const string UNITY_ANDROID_EXTENSION_ASSEMBLY = "UnityEditor.Android.Extensions";
    private const string UNITY_ANDROID_SDKTOOLS_CLASS = "UnityEditor.Android.AndroidSDKTools";
    private const string UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS =
        "UnityEditor.Android.PostProcessAndroidPlayer";
    private const string WRITE_A_BUG =
        "Please report this as a bug with the version of Unity you are using at: " +
        "https://github.com/googlesamples/unity-jar-resolver/issues";

    private static int MinSDKVersionFallback {
        get { return EditorPrefs.GetInt(ANDROID_MIN_SDK_FALLBACK_KEY, DEFAULT_ANDROID_MIN_SDK); }
    }
    private static int AndroidPlatformVersionFallback {
        get { return EditorPrefs.GetInt(ANDROID_PLATFORM_FALLBACK_KEY, DEFAULT_PLATFORM_VERSION); }
    }
    private static string AndroidBuildToolsVersionFallback {
        get {
            return EditorPrefs.GetString(ANDROID_BUILD_TOOLS_FALLBACK_KEY,
                                         DEFAULT_BUILD_TOOLS_VERSION);
        }
    }

    private const float EPSILON = 1e-7f;

    // Parses a UnityEditor.AndroidSDKVersion enum for a value.
    private static int VersionFromAndroidSDKVersionsEnum(string enumName, string fallbackPrefKey,
                                                         int fallbackValue) {
        if (enumName.StartsWith(UNITY_ANDROID_VERSION_ENUM_PREFIX)) {
            enumName = enumName.Substring(UNITY_ANDROID_VERSION_ENUM_PREFIX.Length);
        }

        if (enumName == "Auto") {
            return -1;
        }

        int versionVal;
        bool validVersionString = Int32.TryParse(enumName, out versionVal);
        if (!validVersionString) {
            Debug.LogError("Could not determine the Android SDK Version from the Unity " +
                           "version enum. Resorting to reading a fallback value from the editor " +
                           "preferences " + fallbackPrefKey + ": " +
                           fallbackValue.ToString() + ". " +
                           WRITE_A_BUG);
        }
        return validVersionString ? versionVal : fallbackValue;
    }

    /// <summary>
    /// Parses the MinSDKVersion as an int from the Android Unity Player Settings.
    /// </summary>
    /// <remarks>
    /// The Unity API is supported all the way back to Unity 4.0, but results returned may need a
    /// different interpretation in future versions to give consistent versions.
    /// </remarks>
    /// <returns>the sdk value (ie. 24 for Android 7.0 Nouget)</returns>
    public static int GetAndroidMinSDKVersion() {
        int minSdkVersion = VersionFromAndroidSDKVersionsEnum(
            PlayerSettings.Android.minSdkVersion.ToString(),
            ANDROID_MIN_SDK_FALLBACK_KEY, MinSDKVersionFallback);
        if (minSdkVersion == -1)
            return MinSDKVersionFallback;
        return minSdkVersion;
    }

    /// <summary>
    /// Parses the TargetSDK as an int from the Android Unity Player Settings.
    /// </summary>
    /// <remarks>
    /// This was added in Unity 5.6, so previous versions actually use reflection to get the value
    /// from internal Unity APIs. This enum isn't guaranteed to return a value; it may return auto
    /// which means the implementation will have to decide what version to use.
    /// </remarks>
    /// <returns>The sdk value (ie. 24 for Android 7.0 Nouget). -1 means auto select.</returns>
    public static int GetAndroidTargetSDKVersion() {
        var property = typeof(UnityEditor.PlayerSettings.Android).GetProperty("targetSdkVersion");
        return property == null ? -1 :
            VersionFromAndroidSDKVersionsEnum(
                 Enum.GetName(property.PropertyType, property.GetValue(null, null)),
                 ANDROID_PLATFORM_FALLBACK_KEY, AndroidPlatformVersionFallback);
    }

    /// <summary>
    /// Returns whether the editor is running in batch mode.
    /// </summary>
    public static bool InBatchMode {
        get { return System.Environment.CommandLine.Contains("-batchmode"); }
    }

    private static Type AndroidSDKToolsClass {
        get {
            Type sdkTools = Type.GetType(
                UNITY_ANDROID_SDKTOOLS_CLASS + ", " + UNITY_ANDROID_EXTENSION_ASSEMBLY);
            if (sdkTools == null) {
                Debug.LogError("Could not find the " +
                    UNITY_ANDROID_SDKTOOLS_CLASS + " class via reflection. " + WRITE_A_BUG);
                return null;
            }
            return sdkTools;
        }
    }

    private static object SDKToolsInst {
        get {
            MethodInfo getInstanceMethod = null;
            Type sdkClass = AndroidSDKToolsClass;
            if (sdkClass != null)
                getInstanceMethod = sdkClass.GetMethod("GetInstance");
            if (getInstanceMethod == null) {
                Debug.LogError("Could not find the " +
                    "AndroidSDKToolsClass.GetInstance method via reflection. " + WRITE_A_BUG);
                return null;
            }

            return getInstanceMethod.Invoke(null, BindingFlags.NonPublic | BindingFlags.Static,
                                            null, new object[] {}, null);
        }
    }

    private static Type PostProcessAndroidPlayerClass {
        get {
            Type sdkTools = Type.GetType(
                UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS + ", " +
                UNITY_ANDROID_EXTENSION_ASSEMBLY);
            if (sdkTools == null) {
                Debug.LogError("Could not find the " +
                    UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS +
                    " class via reflection. " + WRITE_A_BUG);
                return null;
            }
            return sdkTools;
        }
    }

    private static object PostProcessAndroidPlayerInst {
        get {
            Type androidPlayerClass = PostProcessAndroidPlayerClass;
            ConstructorInfo constructor = null;
            if (androidPlayerClass != null)
                constructor = androidPlayerClass.GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] {}, null);
            if (constructor == null) {
                Debug.LogError("Could not find the " +
                    UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS +
                    " constructor via reflection. " + WRITE_A_BUG);
                return null;
            }

            return constructor.Invoke(null);
        }
    }

    // Gets the latest SDK version that's currently installed.
    // This is required for generating gradle builds.
    [Obsolete]
    public static int GetAndroidPlatform() {
        MethodInfo platformVersionMethod = null;
        Type sdkClass = AndroidSDKToolsClass;
        if (sdkClass != null) {
            platformVersionMethod = sdkClass.GetMethod("GetTopAndroidPlatformAvailable");
            if (platformVersionMethod != null) {
                return (int)platformVersionMethod.Invoke(SDKToolsInst,
                    BindingFlags.NonPublic, null, new object[] { null }, null);
            }
        }

        // In Unity 4.x the method to get the platform was different and complex enough that
        // another function was was made to get the value unfortunately on another class.
        sdkClass = PostProcessAndroidPlayerClass;
        if (sdkClass != null) {
            platformVersionMethod = sdkClass.GetMethod("GetAndroidPlatform",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (platformVersionMethod != null) {
                object inst = PostProcessAndroidPlayerInst;
                return (int)platformVersionMethod.Invoke(inst,
                    BindingFlags.NonPublic, null, new object[] {}, null);
            }
        }

        Debug.LogError(String.Format(
            "Could not find the {0}.GetTopAndroidPlatformAvailable or " +
            "{1}.GetAndroidPlatform methods via reflection. {2} Resorting to reading a fallback " +
            "value from the editor preferences {3}: {4}",
            UNITY_ANDROID_SDKTOOLS_CLASS, UNITY_ANDROID_POST_PROCESS_ANDROID_PLAYER_CLASS,
            WRITE_A_BUG, ANDROID_PLATFORM_FALLBACK_KEY, AndroidPlatformVersionFallback));

        return AndroidPlatformVersionFallback;
    }

    // Finds the latest build tools version that's currently installed.
    // This is required for generating gradle builds.
    [Obsolete]
    public static string GetAndroidBuildToolsVersion() {
        // TODO: We're using reflection to access Unity's SDK helpers to get at this info
        // but since this is likely fragile, and may not even be consistent accross versions
        // of Unity, we'll need to replace it with our own.
        MethodInfo buildToolsVersionMethod = null;
        Type sdkClass = AndroidSDKToolsClass;
        if (sdkClass != null)
            buildToolsVersionMethod = sdkClass.GetMethod("BuildToolsVersion");
        if (buildToolsVersionMethod != null) {
            return (string)buildToolsVersionMethod.Invoke(SDKToolsInst,
                BindingFlags.NonPublic, null, new object[] { null }, null);
        }

        Debug.LogError("Could not find the " + UNITY_ANDROID_SDKTOOLS_CLASS +
            ".BuildToolsVersion method via reflection. " + WRITE_A_BUG +
            " Resorting to reading a fallback value from the editor preferences " +
            ANDROID_BUILD_TOOLS_FALLBACK_KEY + ": " + AndroidBuildToolsVersionFallback);
        return AndroidBuildToolsVersionFallback;
    }

    /// <summary>
    /// Convert a BuildTarget to a BuildTargetGroup.
    /// </summary>
    /// Unfortunately the Unity API does not provide a way to get the current BuildTargetGroup from
    /// the currently active BuildTarget.
    /// <param name="target">BuildTarget to convert.</param>
    /// <returns>BuildTargetGroup enum value.</returns>
    private static BuildTargetGroup ConvertBuildTargetToBuildTargetGroup(BuildTarget buildTarget) {
        var buildTargetToGroup = new Dictionary<string, string>() {
            { "StandaloneOSXUniversal", "Standalone" },
            { "StandaloneOSXIntel", "Standalone" },
            { "StandaloneLinux", "Standalone" },
            { "StandaloneWindows64", "Standalone" },
            { "WSAPlayer", "WSA" },
            { "StandaloneLinux64", "Standalone" },
            { "StandaloneLinuxUniversal", "Standalone" },
            { "StandaloneOSXIntel64", "Standalone" },
        };
        string buildTargetString = buildTarget.ToString();
        string buildTargetGroupString;
        if (!buildTargetToGroup.TryGetValue(buildTargetString, out buildTargetGroupString)) {
            // If the conversion fails try performing a 1:1 mapping between the platform and group
            // as most build targets only map to one group.
            buildTargetGroupString = buildTargetString;
        }
        try {
            return (BuildTargetGroup)Enum.Parse(typeof(BuildTargetGroup), buildTargetGroupString);
        } catch (ArgumentException) {
            return BuildTargetGroup.Unknown;
        }
    }

    /// <summary>
    /// Get the bundle identifier for the active build target group *not* the selected build
    /// target group using Unity 5.6 and above's API.
    ///
    /// Unity 5.6 and above have the concept of an active build target and the selected build
    /// target.  The active build target is the target that is built when the user presses the
    /// build button.  The selected build target is the target that is currently selected in
    /// the build settings dialog but not active to build (i.e no Unity icon is visible next to
    /// the build target).
    /// </summary>
    private static string Unity56AndAboveApplicationIdentifier {
        get {
            var getApplicationIdentifierMethod =
                typeof(UnityEditor.PlayerSettings).GetMethod("GetApplicationIdentifier");
            if (getApplicationIdentifierMethod == null) return null;
            var activeBuildTargetGroup = ConvertBuildTargetToBuildTargetGroup(
                EditorUserBuildSettings.activeBuildTarget);
            if (activeBuildTargetGroup == BuildTargetGroup.Unknown) return null;
            return getApplicationIdentifierMethod.Invoke(
                null, new object [] { activeBuildTargetGroup } ) as string;
        }
        set {
            var setApplicationIdentifierMethod =
                typeof(UnityEditor.PlayerSettings).GetMethod("SetApplicationIdentifier");
            if (setApplicationIdentifierMethod == null) return;
            var activeBuildTargetGroup = ConvertBuildTargetToBuildTargetGroup(
                EditorUserBuildSettings.activeBuildTarget);
            if (activeBuildTargetGroup == BuildTargetGroup.Unknown) return;
            setApplicationIdentifierMethod.Invoke(
                null, new object [] { activeBuildTargetGroup, value } );
        }
    }

    /// <summary>
    /// Get the bundle identifier for the currently active build target using the Unity 5.5 and
    /// below API.
    /// </summary>
    private static string Unity55AndBelowBundleIdentifier {
        get {
            var property = typeof(UnityEditor.PlayerSettings).GetProperty("bundleIdentifier");
            if (property == null) return null;
            return (string)property.GetValue(null, null);
        }

        set {
            var property = typeof(UnityEditor.PlayerSettings).GetProperty("bundleIdentifier");
            if (property == null) return;
            property.SetValue(null, value, null);
        }
    }

    /// <summary>
    /// Get / set the bundle / application ID.
    /// </summary>
    /// This uses reflection to retrieve the property as it was renamed in Unity 5.6.
    public static string ApplicationId {
        get {
            var identifier = Unity56AndAboveApplicationIdentifier;
            if (identifier != null) return identifier;
            return Unity55AndBelowBundleIdentifier;
        }

        set {
            var identifier = Unity56AndAboveApplicationIdentifier;
            if (identifier != null) {
                Unity56AndAboveApplicationIdentifier = value;
                return;
            }
            Unity55AndBelowBundleIdentifier  = value;
        }
    }
}
}

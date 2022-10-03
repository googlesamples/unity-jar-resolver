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
using Google;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GooglePlayServices {
// TODO(butterfield): Move to a new assembly, for common use between plugins.

// Provides an API for accessing Unity APIs that work across various verisons of Unity.
public class UnityCompat {
    private const string Namespace = "GooglePlayServices.";
    private const string ANDROID_MIN_SDK_FALLBACK_KEY = Namespace + "MinSDKVersionFallback";
    private const string ANDROID_PLATFORM_FALLBACK_KEY = Namespace + "PlatformVersionFallback";
    private const int DEFAULT_ANDROID_MIN_SDK = 14;
    private const int DEFAULT_PLATFORM_VERSION = 25;

    private const string UNITY_ANDROID_VERSION_ENUM_PREFIX = "AndroidApiLevel";
    private const string UNITY_ANDROID_MIN_SDK_VERSION_PROPERTY = "minSdkVersion";
    private const string UNITY_ANDROID_TARGET_SDK_VERSION_PROPERTY = "targetSdkVersion";
    private const string UNITY_ANDROID_EXTENSION_ASSEMBLY = "UnityEditor.Android.Extensions";
    private const string UNITY_ANDROID_JAVA_TOOLS_CLASS = "UnityEditor.Android.AndroidJavaTools";
    private const string UNITY_ANDROID_SDKTOOLS_CLASS = "UnityEditor.Android.AndroidSDKTools";
    private const string UNITY_ANDROID_EXTERNAL_TOOLS_SETTINGS_CLASS =
        "UnityEditor.Android.AndroidExternalToolsSettings";
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

    // Parses a UnityEditor.AndroidSDKVersion enum for a value.
    private static int VersionFromAndroidSDKVersionsEnum(object enumValue, string fallbackPrefKey,
                                                         int fallbackValue) {
        string enumName = null;
        try {
            enumName = Enum.GetName(typeof(AndroidSdkVersions), enumValue);
        } catch (ArgumentException) {
            //Fall back on auto if the enum value is not parsable
            return -1;
        }

        // If the enumName is empty then enumValue was not represented in the enum,
        // most likely because Unity has not yet added the new version,
        // fall back on the raw enumValue
        if (String.IsNullOrEmpty(enumName)) return (int)enumValue;

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
            (object)PlayerSettings.Android.minSdkVersion,
            ANDROID_MIN_SDK_FALLBACK_KEY, MinSDKVersionFallback);
        if (minSdkVersion == -1)
            return MinSDKVersionFallback;
        return minSdkVersion;
    }

    /// <summary>
    /// Try to set the min Android SDK version.
    /// </summary>
    /// <param name="sdkVersion">SDK version to use, -1 for auto (newest installed SDK).</param>
    /// <returns>true if successful, false otherwise.</returns>
    public static bool SetAndroidMinSDKVersion(int sdkVersion) {
        return SetAndroidSDKVersion(sdkVersion, UNITY_ANDROID_MIN_SDK_VERSION_PROPERTY);
    }

    /// <summary>
    /// Parses the TargetSDK as an int from the Android Unity Player Settings.
    /// </summary>
    /// <remarks>
    /// This was added in Unity 5.6, so previous versions actually use reflection to get the value
    /// from internal Unity APIs. This enum isn't guaranteed to return a value; it may return auto
    /// which means the implementation will have to decide what version to use.
    /// </remarks>
    /// <returns>The sdk value (ie. 24 for Android 7.0 Nougat). If the newest Android SDK can't
    /// be found this returns the value of the AndroidPlatformVersionFallback property.</returns>
    public static int GetAndroidTargetSDKVersion() {
        var property = typeof(UnityEditor.PlayerSettings.Android).GetProperty("targetSdkVersion");
        int apiLevel = property == null ? -1 :
            VersionFromAndroidSDKVersionsEnum(
                 property.GetValue(null, null),
                 ANDROID_PLATFORM_FALLBACK_KEY, AndroidPlatformVersionFallback);
        if (apiLevel >= 0) return apiLevel;
        return FindNewestInstalledAndroidSDKVersion();
    }

    /// <summary>
    /// Try to set the target Android SDK version.
    /// </summary>
    /// <param name="sdkVersion">SDK version to use, -1 for auto (newest installed SDK).</param>
    /// <returns>true if successful, false otherwise.</returns>
    public static bool SetAndroidTargetSDKVersion(int sdkVersion) {
        return SetAndroidSDKVersion(sdkVersion, UNITY_ANDROID_TARGET_SDK_VERSION_PROPERTY);
    }

    private static bool SetAndroidSDKVersion(int sdkVersion, string propertyName) {
        var property = typeof(UnityEditor.PlayerSettings.Android).GetProperty(propertyName);
        if (property == null) return false;
        var enumValueString = sdkVersion >= 0 ?
            UNITY_ANDROID_VERSION_ENUM_PREFIX + sdkVersion.ToString() :
            UNITY_ANDROID_VERSION_ENUM_PREFIX + "Auto";
        try {
            property.SetValue(null, Enum.Parse(property.PropertyType, enumValueString), null);
            return true;
        } catch (ArgumentException) {
            // Ignore.
        }
        return false;
    }

    /// <summary>
    /// Returns whether the editor is running in batch mode.
    /// </summary>
    [ObsoleteAttribute("InBatchMode is obsolete, use ExecutionEnvironment.InBatchMode instead")]
    public static bool InBatchMode { get { return ExecutionEnvironment.InBatchMode; } }

    private static Type AndroidExternalToolsClass {
        get {
            return Type.GetType(UNITY_ANDROID_EXTERNAL_TOOLS_SETTINGS_CLASS + ", " +
                                UNITY_ANDROID_EXTENSION_ASSEMBLY);
        }
    }

    /// <summary>
    /// Get a property of the UnityEditor.Android.AndroidExternalToolsSettings class which was
    /// introduced in Unity 2019.
    /// </summary>
    /// <param name="propertyName">Name of the property to query.</param>
    /// <returns>Value of the string property or null if the property isn't found or isn't a
    /// string.</returns>
    private static string GetAndroidExternalToolsSettingsProperty(string propertyName) {
        string value = null;
        var androidExternalTools = AndroidExternalToolsClass;
        if (androidExternalTools != null) {
            var property = androidExternalTools.GetProperty(propertyName);
            if (property != null) {
                value = property.GetValue(null, null) as string;
            }
        }
        return value;
    }

    /// <summary>
    /// Get the JDK path from UnityEditor.Android.AndroidExternalToolsSettings if it's
    /// available.
    /// </summary>
    public static string AndroidExternalToolsSettingsJdkRootPath {
        get { return GetAndroidExternalToolsSettingsProperty("jdkRootPath"); }
    }

    /// <summary>
    /// Get the Android NDK path from UnityEditor.Android.AndroidExternalToolsSettings if it's
    /// available.
    /// </summary>
    public static string AndroidExternalToolsSettingsNdkRootPath {
        get { return GetAndroidExternalToolsSettingsProperty("ndkRootPath"); }
    }

    /// <summary>
    /// Get the Android SDK path from UnityEditor.Android.AndroidExternalToolsSettings if it's
    /// available.
    /// </summary>
    public static string AndroidExternalToolsSettingsSdkRootPath {
        get { return GetAndroidExternalToolsSettingsProperty("sdkRootPath"); }
    }

    /// <summary>
    /// Get the Gradle path from UnityEditor.Android.AndroidExternalToolsSettings if it's
    /// available.
    /// </summary>
    public static string AndroidExternalToolsSettingsGradlePath {
        get { return GetAndroidExternalToolsSettingsProperty("gradlePath"); }
    }

    private static Type AndroidJavaToolsClass {
        get {
            return Type.GetType(
                UNITY_ANDROID_JAVA_TOOLS_CLASS + ", " + UNITY_ANDROID_EXTENSION_ASSEMBLY);
        }
    }

    private static object AndroidJavaToolsInstance {
        get {
            try {
                return VersionHandler.InvokeStaticMethod(AndroidJavaToolsClass, "GetInstance",
                                                         null);
            } catch (Exception) {
                return null;
            }
        }
    }

    private static Type AndroidSDKToolsClass {
        get {
            return Type.GetType(UNITY_ANDROID_SDKTOOLS_CLASS + ", " +
                                UNITY_ANDROID_EXTENSION_ASSEMBLY);
        }
    }

    private static object AndroidSDKToolsInstance {
        get {
            var androidSdkToolsClass = AndroidSDKToolsClass;
            if (androidSdkToolsClass == null) {
                Debug.LogError(String.Format("{0} class does not exist. {1}",
                                             UNITY_ANDROID_SDKTOOLS_CLASS, WRITE_A_BUG));
                return null;
            }
            try {
                return VersionHandler.InvokeStaticMethod(androidSdkToolsClass, "GetInstance", null);
            } catch (Exception) {
            }
            // Unity 2018+ requires the AndroidJavaTools instance to get the SDK tools instance.
            try {
                return VersionHandler.InvokeStaticMethod(androidSdkToolsClass, "GetInstance",
                                                         new object[] { AndroidJavaToolsInstance });
            } catch (Exception e) {
                Debug.LogError(String.Format("Failed while calling {0}.GetInstance() ({1}). {2}",
                                             UNITY_ANDROID_SDKTOOLS_CLASS, e, WRITE_A_BUG));
            }
            return null;
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

    private static object PostProcessAndroidPlayerInstance {
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

    // Display a warning and get the fallback Android SDK version.
    private static int WarnOnAndroidSdkFallbackVersion() {
        int version = AndroidPlatformVersionFallback;
        Debug.LogWarning(String.Format(
            "Failed to determine the most recently installed Android SDK version. {0} " +
            "Resorting to reading a fallback value from the editor preferences {1}: {2}",
            WRITE_A_BUG, ANDROID_PLATFORM_FALLBACK_KEY, version));
        return version;
    }

    // Gets the latest SDK version that's currently installed.
    // This is required for generating gradle builds.
    public static int FindNewestInstalledAndroidSDKVersion() {
        var androidSdkToolsClass = AndroidSDKToolsClass;
        if (androidSdkToolsClass != null) {
            // To fetch the Android SDK version Unity runs tools in the Android SDK but doesn't
            // set the JAVA_HOME environment variable before launching them via these internal
            // methods. Therefore, we override JAVA_HOME in the process with the directory specified
            // in Unity's settings while querying the Android SDK and then restore the JAVA_HOME
            // variable to it's default state when we're finished.
            var previousJavaHome = Environment.GetEnvironmentVariable(JavaUtilities.JAVA_HOME);
            var javaHome = JavaUtilities.JavaHome;
            if (!String.IsNullOrEmpty(javaHome)) {
                Environment.SetEnvironmentVariable(JavaUtilities.JAVA_HOME, javaHome,
                                                   EnvironmentVariableTarget.Process);
            }
            try {
                var androidSdkToolsInstance = AndroidSDKToolsInstance;
                if (androidSdkToolsInstance == null) return WarnOnAndroidSdkFallbackVersion();
                // Unity 2019+ only has a method to list the installed targets, so we need to parse
                // the returned list to determine the newest Android SDK version.
                var listTargetPlatforms = androidSdkToolsClass.GetMethod(
                    "ListTargetPlatforms", BindingFlags.Instance | BindingFlags.NonPublic);
                if (listTargetPlatforms != null) {
                    var sdkVersionList = new List<int>();
                    const string PLATFORM_STRING_PREFIX = "android-";
                    IEnumerable<string> platformStrings = null;
                    try {
                        // Unity 2019+
                        platformStrings = (IEnumerable<string>)listTargetPlatforms.Invoke(
                            androidSdkToolsInstance, null);
                    } catch (Exception) {
                    }
                    if (platformStrings != null) {
                        try {
                            // Unity 2018+
                            platformStrings = (IEnumerable<string>)listTargetPlatforms.Invoke(
                                androidSdkToolsInstance, new object[] { AndroidJavaToolsInstance });
                        } catch (Exception) {
                        }
                    }
                    if (platformStrings != null) {
                        foreach (var platformString in platformStrings) {
                            if (platformString.StartsWith(PLATFORM_STRING_PREFIX)) {
                                int sdkVersion;
                                if (Int32.TryParse(
                                        platformString.Substring(PLATFORM_STRING_PREFIX.Length),
                                        out sdkVersion)) {
                                    sdkVersionList.Add(sdkVersion);
                                }
                            }
                        }
                        sdkVersionList.Sort();
                        var numberOfSdks = sdkVersionList.Count;
                        if (numberOfSdks > 0) {
                            return sdkVersionList[numberOfSdks - 1];
                        }
                    }
                }
                var getTopAndroidPlatformAvailable =
                    androidSdkToolsClass.GetMethod("GetTopAndroidPlatformAvailable");
                if (getTopAndroidPlatformAvailable != null) {
                    return (int)getTopAndroidPlatformAvailable.Invoke(
                        androidSdkToolsInstance, BindingFlags.NonPublic, null,
                        new object[] { null }, null);
                }
            } finally {
                Environment.SetEnvironmentVariable(JavaUtilities.JAVA_HOME, previousJavaHome,
                                                   EnvironmentVariableTarget.Process);
            }
        }

        // In Unity 4.x the method to get the platform was different and complex enough that
        // another function was was made to get the value unfortunately on another class.
        var postProcessAndroidPlayerClass = PostProcessAndroidPlayerClass;
        MethodInfo getAndroidPlatform =
            postProcessAndroidPlayerClass != null ?
            postProcessAndroidPlayerClass.GetMethod(
                "GetAndroidPlatform", BindingFlags.Instance | BindingFlags.NonPublic) : null;
        if (getAndroidPlatform != null) {
            return (int)getAndroidPlatform.Invoke(
                PostProcessAndroidPlayerInstance, BindingFlags.NonPublic, null,
                new object[] {}, null);
        }
        return WarnOnAndroidSdkFallbackVersion();
    }

    /// <summary>
    /// Convert a BuildTarget to a BuildTargetGroup.
    /// </summary>
    /// Unfortunately the Unity API does not provide a way to get the current BuildTargetGroup from
    /// the currently active BuildTarget.
    /// <param name="buildTarget">BuildTarget to convert.</param>
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
    /// </summary>
    /// <param name="buildTarget">Build target to query.</param>
    /// <returns>Application identifier if it can be retrieved, null otherwise.</returns>
    private static string GetUnity56AndAboveApplicationIdentifier(BuildTarget buildTarget) {
        var getApplicationIdentifierMethod =
            typeof(UnityEditor.PlayerSettings).GetMethod("GetApplicationIdentifier", new[]{typeof(BuildTargetGroup)});
        if (getApplicationIdentifierMethod == null) return null;
        var buildTargetGroup = ConvertBuildTargetToBuildTargetGroup(buildTarget);
        if (buildTargetGroup == BuildTargetGroup.Unknown) return null;
        return getApplicationIdentifierMethod.Invoke(
            null, new object [] { buildTargetGroup } ) as string;
    }

    /// <summary>
    /// Set the bundle identifier for the specified build target group in Unity 5.6 and above.
    ///
    /// </summary>
    /// <param name="buildTarget">Build target to query.</param>
    /// <param name="applicationIdentifier">Application ID to set.</param>
    /// <returns>true if successful, false otherwise.</returns>
    private static bool SetUnity56AndAboveApplicationIdentifier(BuildTarget buildTarget,
                                                                string applicationIdentifier) {
        var setApplicationIdentifierMethod =
            typeof(UnityEditor.PlayerSettings).GetMethod(
                "SetApplicationIdentifier",
                new[]{typeof(BuildTargetGroup), typeof(string)});
        if (setApplicationIdentifierMethod == null) return false;
        var buildTargetGroup = ConvertBuildTargetToBuildTargetGroup(buildTarget);
        if (buildTargetGroup == BuildTargetGroup.Unknown) return false;
        setApplicationIdentifierMethod.Invoke(
            null, new object [] { buildTargetGroup, applicationIdentifier } );
        return true;
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
    /// Get the bundle / application ID for a build target.
    /// </summary>
    /// <param name="buildTarget">Build target to query.
    /// This is ignored in Unity 5.5 and below as all build targets share the same ID.
    /// </param>
    /// <returns>Bundle / application ID for the build target.</returns>
    public static string GetApplicationId(BuildTarget buildTarget) {
        var identifier = GetUnity56AndAboveApplicationIdentifier(buildTarget);
        if (identifier != null) return identifier;
        return Unity55AndBelowBundleIdentifier;
    }

    /// <summary>
    /// Set the bundle / application ID for a build target.
    /// </summary>
    /// <param name="buildTarget">Build target to select.
    /// This is ignored in Unity 5.5 and below as all build targets share the same ID.
    /// </param>
    /// <param name="applicationIdentifier">Bundle / application ID to set.</param>
    public static void SetApplicationId(BuildTarget buildTarget, string applicationIdentifier) {
        if (!SetUnity56AndAboveApplicationIdentifier(buildTarget, applicationIdentifier)) {
            Unity55AndBelowBundleIdentifier = applicationIdentifier;
        }
    }

    /// <summary>
    /// Get / set the bundle / application ID.
    ///
    /// Unity 5.6 and above have the concept of an active build target and the selected build
    /// target.  The active build target is the target that is built when the user presses the
    /// build button.  The selected build target is the target that is currently selected in
    /// the build settings dialog but not active to build (i.e no Unity icon is visible next to
    /// the build target).
    /// </summary>
    /// This uses reflection to retrieve the property as it was renamed in Unity 5.6.
    public static string ApplicationId {
        get { return GetApplicationId(EditorUserBuildSettings.activeBuildTarget); }
        set { SetApplicationId(EditorUserBuildSettings.activeBuildTarget, value); }
    }
}
}

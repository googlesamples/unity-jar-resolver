// <copyright file="VersionHandler.cs" company="Google Inc.">
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

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System;

using UnityEngine;
using UnityEditor;

namespace Google {

/// <summary>
/// Enables the most recent version of the VersionHandler dll and provides an interface to
/// the VersionHandler's implementation.
/// </summary>
[InitializeOnLoad]
public class VersionHandler {
    const string VERSION_HANDLER_ASSEMBLY_NAME = "Google.VersionHandlerImpl";
    const string VERSION_HANDLER_IMPL_CLASS = "VersionHandlerImpl";
    static Regex VERSION_HANDLER_FILENAME_RE = new Regex(
        String.Format(".*[\\/]({0})(.*)(\\.dll)$",
                      VERSION_HANDLER_ASSEMBLY_NAME.Replace(".", "\\.")),
        RegexOptions.IgnoreCase);

    const string EXECUTED_PATH = "Temp/VersionHandlerBootstrapped";

    // Get the VersionHandler implementation class.
    private static Type Impl {
        get { return FindClass(VERSION_HANDLER_ASSEMBLY_NAME, VERSION_HANDLER_IMPL_CLASS); }
    }

    /// <summary>
    /// Schedule the process of enabling the version handler.
    /// In Unity 4.x it's not possible to enable a plugin DLL in a static constructor as it
    /// crashes the editor.
    /// </summary>
    static VersionHandler() {
        // Schedule the process if the version handler isn't disabled on the command line or
        // executed previously in this session.
        bool executed = File.Exists(EXECUTED_PATH);
        if (System.Environment.CommandLine.Contains("-gvh_disable") || executed) {
            if (!executed) {
                UnityEngine.Debug.Log(String.Format("{0} bootstrap disabled",
                                                    VERSION_HANDLER_ASSEMBLY_NAME));
            }
        } else {
            EditorApplication.update -= BootStrap;
            EditorApplication.update += BootStrap;
        }
    }

    /// <summary>
    /// Enable the latest VersionHandler DLL if it's not already loaded.
    /// </summary>
    static void BootStrap() {
        EditorApplication.update -= BootStrap;
        // If the VersionHandler assembly is already loaded we have nothing to do.
        if (Impl != null) {
            UnityEngine.Debug.Log(String.Format("{0} DLL is already loaded",
                                                VERSION_HANDLER_ASSEMBLY_NAME));
            return;
        }
        UnityEngine.Debug.Log(String.Format("Bootstrapping {0}", VERSION_HANDLER_ASSEMBLY_NAME));
        var assemblies = new List<Match>();
        foreach (string assetGuid in AssetDatabase.FindAssets("l:gvh")) {
            string filename = AssetDatabase.GUIDToAssetPath(assetGuid);
            var match = VERSION_HANDLER_FILENAME_RE.Match(filename);
            if (match.Success) assemblies.Add(match);
        }
        if (assemblies.Count == 0) {
            UnityEngine.Debug.LogWarning(String.Format("No {0} DLL found to bootstrap",
                                                       VERSION_HANDLER_ASSEMBLY_NAME));
            return;
        }
        // Sort assembly paths by version number.
        string mostRecentAssembly = null;
        var mostRecentVersionNumber = 0;
        foreach (var match in assemblies) {
            var filename = match.Groups[0].Value;
            var version = match.Groups[2].Value;
            // Convert a multi-component version number to a string.
            var components = version.Split(new [] { '.' });
            Array.Reverse(components);
            var versionNumber = 0;
            var componentMultiplier = 1000;
            var currentComponentMultiplier = 1;
            foreach (var component in components) {
                try {
                    versionNumber += Int32.Parse(component) * currentComponentMultiplier;
                } catch (FormatException) {
                    // Ignore the component.
                }
                currentComponentMultiplier *= componentMultiplier;
            }
            if (versionNumber > mostRecentVersionNumber) {
                mostRecentVersionNumber = versionNumber;
                mostRecentAssembly = filename;
            }
        }
        if (String.IsNullOrEmpty(mostRecentAssembly)) {
            UnityEngine.Debug.LogWarning(String.Format("Failed to get the most recent {0} DLL.  " +
                                                       "Unable to bootstrap.",
                                                       VERSION_HANDLER_ASSEMBLY_NAME));
            return;
        }
        using (var file = new StreamWriter(EXECUTED_PATH, true)) {
            file.WriteLine("OK");
        }
        if (VersionHandler.FindClass("UnityEditor", "UnityEditor.PluginImporter") != null) {
            EnableEditorPlugin(mostRecentAssembly);
        } else {
            ReimportPlugin(mostRecentAssembly);
        }
    }

    /// <summary>
    /// Force import a plugin by deleting metadata associated with the plugin.
    /// </summary>
    private static void ReimportPlugin(string path) {
        File.Delete(path + ".meta");
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    /// <summary>
    /// Enable the editor plugin at the specified path.
    /// </summary>
    private static void EnableEditorPlugin(string path) {
        PluginImporter importer = AssetImporter.GetAtPath(path) as PluginImporter;
        if (importer == null) {
            UnityEngine.Debug.Log(String.Format("Failed to enable editor plugin {0}", path));
            return;
        }
        importer.SetCompatibleWithEditor(true);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    /// <summary>
    /// Get a property by name.
    /// </summary>
    private static PropertyInfo GetPropertyByName(string propertyName) {
        var cls = Impl;
        if (cls == null) return null;
        return cls.GetProperty(propertyName);
    }

    /// <summary>
    /// Get a boolean property's value by name.
    /// </summary>
    private static bool GetBoolPropertyByName(string propertyName, bool defaultValue) {
        var prop = GetPropertyByName(propertyName);
        if (prop == null) return defaultValue;
        return (bool)prop.GetValue(null, null);
    }

    /// <summary>
    /// Set a boolean property's value by name,
    /// </summary>
    private static void SetBoolPropertyByName(string propertyName, bool value) {
        var prop = GetPropertyByName(propertyName);
        if (prop == null) return;
        prop.SetValue(null, value, null);
    }

    /// <summary>
    /// Enable / disable automated version handling.
    /// </summary>
    public static bool Enabled {
        get { return GetBoolPropertyByName("Enabled", false); }
        set { SetBoolPropertyByName("Enabled", value); }
    }

    /// <summary>
    /// Enable / disable prompting the user on clean up.
    /// </summary>
    public static bool CleanUpPromptEnabled {
        get { return GetBoolPropertyByName("CleanUpPromptEnabled", false); }
        set { SetBoolPropertyByName("CleanUpPromptEnabled", value); }
    }

    /// <summary>
    /// Enable / disable renaming to canonical filenames.
    /// </summary>
    public static bool RenameToCanonicalFilenames {
        get { return GetBoolPropertyByName("RenameToCanonicalFilenames", false); }
        set { SetBoolPropertyByName("RenameToCanonicalFilenames", value); }
    }

    /// <summary>
    /// Enable / disable verbose logging.
    /// </summary>
    public static bool VerboseLoggingEnabled {
        get { return GetBoolPropertyByName("VerboseLoggingEnabled", false); }
        set { SetBoolPropertyByName("VerboseLoggingEnabled", value); }
    }

    /// <summary>
    /// Show the settings menu.
    /// </summary>
    public static void ShowSettings() {
        InvokeStaticMethod(Impl, "ShowSettings", null);
    }

    /// <summary>
    /// Force version handler execution.
    /// </summary>
    public static void UpdateNow() {
        InvokeStaticMethod(Impl, "UpdateNow", null);
    }

    /// <summary>
    /// Delegate used to filter a file and directory names.
    /// </summary>
    /// <returns>true if the filename should be returned by an enumerator,
    /// false otherwise.</returns>
    /// <param name="filename">Name of the file / directory to filter.</param>
    public delegate bool FilenameFilter(string filename);

    /// <summary>
    /// Search the asset database for all files matching the specified filter.
    /// </summary>
    /// <returns>Array of matching files.</returns>
    /// <param name="assetsFilter">Filter used to query the
    /// AssetDatabase.  If this isn't specified, all assets are searched.
    /// </param>
    /// <param name="filter">Optional delegate to filter the returned
    /// list.</param>
    public static string[] SearchAssetDatabase(string assetsFilter = null,
                                               FilenameFilter filter = null) {
        return (string[])InvokeStaticMethod(Impl, "SearchAssetDatabase", null,
                                            new Dictionary<string, object> {
                                                { "assetsFilter", assetsFilter },
                                                { "filter", filter }
                                            });
    }

    /// <summary>
    /// Get all assets managed by this module.
    /// </summary>
    public static string[] FindAllAssets() {
        return (string[])InvokeStaticMethod(Impl, "FindAllAssets", null);
    }

    /// <summary>
    /// Find all files in the asset database with multiple version numbers
    /// encoded in their filename, select the most recent revisions and
    /// delete obsolete versions and files referenced by old manifests that
    /// are not present in the most recent manifests.
    /// </summary>
    public static void UpdateVersionedAssets(bool forceUpdate = false) {
        InvokeStaticMethod(Impl, "UpdateVersionedAssets", null,
                           new Dictionary<string, object> { { "forceUpdate", forceUpdate } });
    }

    // Returns the major/minor version of the unity environment we are running in
    // as a float so it can be compared numerically.
    public static float GetUnityVersionMajorMinor() {
        try {
            var version = InvokeStaticMethod(Impl, "GetUnityVersionMajorMinor", null);
            return (float)version;
        } catch (Exception) {
            return 0.0f;
        }
    }

    /// <summary>
    /// Find a class from an assembly by name.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to search for.</param>
    /// <param name="className">Name of the class to find.</param>
    /// <returns>The Type of the class if found, null otherwise.</returns>
    public static Type FindClass(string assemblyName, string className) {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (assembly.GetName().Name == assemblyName) {
                return Type.GetType(className + ", " + assembly.FullName);
            }
        }
        return null;
    }

    /// <summary>
    /// Call a method on an object with named arguments.
    /// </summary>
    /// <param name="objectInstance">Object to call a method on.</param>
    /// <param name="methodName">Name of the method to call.</param>
    /// <param name="arg">Positional arguments of the method.</param>
    /// <param name="namedArgs">Named arguments of the method.</param>
    /// <returns>object returned by the method.</returns>
    public static object InvokeInstanceMethod(
            object objectInstance, string methodName, object[] args,
            Dictionary<string, object> namedArgs = null) {
        return InvokeMethod(objectInstance.GetType(),
                            objectInstance, methodName, args: args,
                            namedArgs: namedArgs);
    }

    /// <summary>
    /// Call a static method on an object.
    /// </summary>
    /// <param name="type">Class to call the method on.</param>
    /// <param name="methodName">Name of the method to call.</param>
    /// <param name="arg">Positional arguments of the method.</param>
    /// <param name="namedArgs">Named arguments of the method.</param>
    /// <returns>object returned by the method.</returns>
    public static object InvokeStaticMethod(
            Type type, string methodName, object[] args,
            Dictionary<string, object> namedArgs = null) {
        return InvokeMethod(type, null, methodName, args: args,
                            namedArgs: namedArgs);
    }

    /// <summary>
    /// Call a method on an object with named arguments.
    /// </summary>
    /// <param name="type">Class to call the method on.</param>
    /// <param name="objectInstance">Object to call a method on.</param>
    /// <param name="methodName">Name of the method to call.</param>
    /// <param name="arg">Positional arguments of the method.</param>
    /// <param name="namedArgs">Named arguments of the method.</param>
    /// <returns>object returned by the method.</returns>
    public static object InvokeMethod(
            Type type, object objectInstance, string methodName,
            object[] args, Dictionary<string, object> namedArgs = null) {
        MethodInfo method = type.GetMethod(methodName);
        ParameterInfo[] parameters = method.GetParameters();
        int numParameters = parameters.Length;
        object[] parameterValues = new object[numParameters];
        int numPositionalArgs = args != null ? args.Length : 0;
        foreach (var parameter in parameters) {
            int position = parameter.Position;
            if (position < numPositionalArgs) {
                parameterValues[position] = args[position];
                continue;
            }
            object namedValue = parameter.RawDefaultValue;
            if (namedArgs != null) {
                object overrideValue;
                if (namedArgs.TryGetValue(parameter.Name, out overrideValue)) {
                    namedValue = overrideValue;
                }
            }
            parameterValues[position] = namedValue;
        }
        return method.Invoke(objectInstance, parameterValues);
    }
}

}  // namespace Google

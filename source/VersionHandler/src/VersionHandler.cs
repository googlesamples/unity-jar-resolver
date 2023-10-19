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
    const string VERSION_HANDLER_IMPL_CLASS = "Google.VersionHandlerImpl";
    static Regex VERSION_HANDLER_FILENAME_RE = new Regex(
        String.Format(".*[\\/]({0})(.*)(\\.dll)$",
                      VERSION_HANDLER_ASSEMBLY_NAME.Replace(".", "\\.")),
        RegexOptions.IgnoreCase);

    // File which indicates boot strapping is in progress.
    const string BOOT_STRAPPING_PATH = "Temp/VersionHandlerBootStrapping";
    // Value written to the boot strapping file to indicate the process is executing.
    const string BOOT_STRAPPING_COMMAND = "BootStrapping";
    // File which contains the set of methods to call when an update operation is complete.
    const string CALLBACKS_PATH = "Temp/VersionHandlerCallbacks";

    // Enumerating over loaded assemblies and retrieving each name is pretty expensive (allocates
    // ~2Kb per call multiplied by number of assemblies (i.e > 50)).  This leads to memory being
    // allocated that needs to be garbage collected which can reduce performance of the editor.
    // This caches any found types for each combined assembly name + class name.  Once a class
    // is found this dictionary retains a reference to the type.
    private static Dictionary<string, Type> typeCache = new Dictionary<string, Type>();

    // Get the VersionHandler implementation class.
    private static Type Impl {
        get { return FindClass(VERSION_HANDLER_ASSEMBLY_NAME, VERSION_HANDLER_IMPL_CLASS); }
    }

    // Get the VersionHandler implementation class, attempting to bootstrap the module first.
    private static Type BootStrappedImpl {
        get {
            if (Impl == null) BootStrap();
            return Impl;
        }
    }

    // Whether the VersionHandler implementation is being boot strapped.
    private static bool BootStrapping {
        get {
            return File.Exists(BOOT_STRAPPING_PATH);
        }

        set {
            var currentlyBootStrapping = BootStrapping;
            if (value != currentlyBootStrapping) {
                if (value) {
                    AddToBootStrappingFile(new List<string> { BOOT_STRAPPING_COMMAND });
                } else if (currentlyBootStrapping) {
                    // Forward any deferred properties.
                    UpdateCompleteMethods = UpdateCompleteMethodsInternal;
                    // Execute any scheduled method calls.
                    var duplicates = new HashSet<string>();
                    var executionList = new List<string>();
                    foreach (var command in ReadBootStrappingFile()) {
                        if (command == BOOT_STRAPPING_COMMAND) continue;
                        if (duplicates.Contains(command)) continue;
                        duplicates.Add(command);
                        executionList.Add(command);
                    }
                    while (executionList.Count > 0) {
                        var command = executionList[0];
                        executionList.RemoveAt(0);
                        // Rewrite the list just to handle the case where this assembly gets
                        // reloaded.
                        File.WriteAllText(BOOT_STRAPPING_PATH,
                                          String.Join("\n", executionList.ToArray()));
                        InvokeImplMethod(command);
                    }
                    UpdateCompleteMethodsInternal = null;
                    // Clean up the boot strapping file.
                    File.Delete(BOOT_STRAPPING_PATH);
                }
            }
        }
    }

    /// <summary>
    /// Schedule the process of enabling the version handler.
    /// In Unity 4.x it's not possible to enable a plugin DLL in a static constructor as it
    /// crashes the editor.
    /// </summary>
    static VersionHandler() {
        // Schedule the process if the version handler isn't disabled on the command line.
        if (System.Environment.CommandLine.ToLower().Contains("-gvh_disable")) {
            UnityEngine.Debug.Log(String.Format("{0} bootstrap disabled",
                                                VERSION_HANDLER_ASSEMBLY_NAME));
        } else {
            EditorApplication.update -= BootStrap;
            EditorApplication.update += BootStrap;
            // A workaround to make sure bootstrap continues if Unity reloads assemblies
            // during bootstrapping. The issue only observed in Unity 2019 and 2020
            float unityVersion = GetUnityVersionMajorMinor();
            if (unityVersion < 2021.0f && unityVersion >= 2019.0f) {
              var type = BootStrappedImpl;
            }
        }
    }

    // Add a line to the boot strapping file.
    private static void AddToBootStrappingFile(List<string> lines) {
        File.AppendAllText(BOOT_STRAPPING_PATH, String.Join("\n", lines.ToArray()) + "\n");
    }

    // Read lines from the boot strapping file.
    private static IEnumerable<string> ReadBootStrappingFile() {
        return File.ReadAllLines(BOOT_STRAPPING_PATH);
    }

    /// <summary>
    /// Enable the latest VersionHandler DLL if it's not already loaded.
    /// </summary>
    private static void BootStrap() {
        var bootStrapping = BootStrapping;
        var implAvailable = Impl != null;
        // If the VersionHandler assembly is already loaded or we're still bootstrapping we have
        // nothing to do.
        if (bootStrapping) {
            BootStrapping = !implAvailable;
            return;
        }
        EditorApplication.update -= BootStrap;
        if (implAvailable) return;

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
        var mostRecentVersionNumber = -1;
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
        BootStrapping = true;
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
        var cls = BootStrappedImpl;
        if (cls == null) return null;
        return cls.GetProperty(propertyName);
    }

    /// <summary>
    /// Get a boolean property's value by name.
    /// </summary>
    private static T GetPropertyByName<T>(string propertyName, T defaultValue) {
        var prop = GetPropertyByName(propertyName);
        if (prop == null) return defaultValue;
        return (T)prop.GetValue(null, null);
    }

    /// <summary>
    /// Set a boolean property's value by name,
    /// </summary>
    private static void SetPropertyByName<T>(string propertyName, T value) {
        var prop = GetPropertyByName(propertyName);
        if (prop == null) return;
        prop.SetValue(null, value, null);
    }

    /// <summary>
    /// Enable / disable automated version handling.
    /// </summary>
    public static bool Enabled {
        get { return GetPropertyByName("Enabled", false); }
        set { SetPropertyByName("Enabled", value); }
    }

    /// <summary>
    /// Enable / disable prompting the user on clean up.
    /// </summary>
    public static bool CleanUpPromptEnabled {
        get { return GetPropertyByName("CleanUpPromptEnabled", false); }
        set { SetPropertyByName("CleanUpPromptEnabled", value); }
    }

    /// <summary>
    /// Enable / disable renaming to canonical filenames.
    /// </summary>
    public static bool RenameToCanonicalFilenames {
        get { return GetPropertyByName("RenameToCanonicalFilenames", false); }
        set { SetPropertyByName("RenameToCanonicalFilenames", value); }
    }

    /// <summary>
    /// Enable / disable verbose logging.
    /// </summary>
    public static bool VerboseLoggingEnabled {
        get { return GetPropertyByName("VerboseLoggingEnabled", false); }
        set { SetPropertyByName("VerboseLoggingEnabled", value); }
    }

    /// <summary>
    /// Set the methods to call when the VersionHandler has finished updating.
    /// Each string in the specified list should have the format
    /// "assemblyname:classname:methodname".
    /// assemblyname can be empty to search all assemblies for classname.
    /// For example:
    /// ":MyClass:MyMethod"
    /// Would call MyClass.MyMethod() when the update process is complete.
    /// </summary>
    public static IEnumerable<string> UpdateCompleteMethods {
        get {
            return GetPropertyByName<IEnumerable<string>>("UpdateCompleteMethods",
                                                          UpdateCompleteMethodsInternal);
        }

        set {
            if (Impl != null) {
                SetPropertyByName("UpdateCompleteMethods", value);
            } else {
                UpdateCompleteMethodsInternal = value;
            }
        }
    }

    // Backing store for update methods until the VersionHandler is boot strapped.
    private static IEnumerable<string> UpdateCompleteMethodsInternal {
        get {
            if (File.Exists(CALLBACKS_PATH)) {
                return File.ReadAllText(CALLBACKS_PATH).Split(new [] { '\n' });
            } else {
                return new List<string>();
            }
        }

        set {
            File.WriteAllText(
                CALLBACKS_PATH,
                value == null ? "" : String.Join("\n", new List<string>(value).ToArray()));
        }
    }

    /// <summary>
    /// Show the settings menu.
    /// </summary>
    public static void ShowSettings() {
        InvokeImplMethod("ShowSettings");
    }

    /// <summary>
    /// Force version handler execution.
    /// </summary>
    public static void UpdateNow() {
        InvokeImplMethod("UpdateNow", schedule: true);
    }

    /// <summary>
    /// Delegate used to filter a file and directory names.
    /// </summary>
    /// <returns>true if the filename should be returned by an enumerator,
    /// false otherwise.</returns>
    /// <param name="filename">Name of the file / directory to filter.</param>
    public delegate bool FilenameFilter(string filename);

    // Cast an object to a string array if it's not null, or return an empty string array.
    private static string[] StringArrayFromObject(object obj) {
        return obj != null ? (string[])obj : new string[] {};
    }

    /// <summary>
    /// Search the asset database for all files matching the specified filter.
    /// </summary>
    /// <returns>Array of matching files.</returns>
    /// <param name="assetsFilter">Filter used to query the
    /// AssetDatabase.  If this isn't specified, all assets are searched.
    /// </param>
    /// <param name="filter">Optional delegate to filter the returned
    /// list.</param>
    /// <param name="directories">Directories to search for the assets in the project. Directories
    /// that don't exist are ignored.</param>
    public static string[] SearchAssetDatabase(string assetsFilter = null,
                                               FilenameFilter filter = null,
                                               IEnumerable<string> directories = null) {
        return StringArrayFromObject(InvokeImplMethod("SearchAssetDatabase", null,
                                                      namedArgs: new Dictionary<string, object> {
                                                          { "assetsFilter", assetsFilter },
                                                          { "filter", filter },
                                                          { "directories", directories },
                                                      }));
    }

    /// <summary>
    /// Get all assets managed by this module.
    /// </summary>
    public static string[] FindAllAssets() {
        return StringArrayFromObject(InvokeImplMethod("FindAllAssets"));
    }

    /// <summary>
    /// Find all files in the asset database with multiple version numbers
    /// encoded in their filename, select the most recent revisions and
    /// delete obsolete versions and files referenced by old manifests that
    /// are not present in the most recent manifests.
    /// </summary>
    public static void UpdateVersionedAssets(bool forceUpdate = false) {
        InvokeImplMethod("UpdateVersionedAssets",
                         args: new object[] { forceUpdate },
                         schedule: true);
    }

    private static float unityVersionMajorMinor = -1.0f;
    // Returns the major/minor version of the unity environment we are running in
    // as a float so it can be compared numerically.
    public static float GetUnityVersionMajorMinor() {
        if (unityVersionMajorMinor > 0.0f) return unityVersionMajorMinor;
        try {
            var version = InvokeImplMethod("GetUnityVersionMajorMinor");
            unityVersionMajorMinor = (float)version;
            return unityVersionMajorMinor;
        } catch (Exception) {
            return 0.0f;
        }
    }

    /// Call a static method on a type returning null if type is null.
    private static object InvokeImplMethod(string methodName, object[] args = null,
                                           Dictionary<string, object> namedArgs = null,
                                           bool schedule = false) {
        var type = BootStrappedImpl;
        if (type == null) {
            if (BootStrapping && schedule) {
                // Try scheduling execution until the implementation is loaded.
                AddToBootStrappingFile(new List<string> { methodName });
            }
            return null;
        }
        return InvokeStaticMethod(type, methodName, args, namedArgs: namedArgs);
    }

    /// <summary>
    /// Find a class from an assembly by name.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to search for.  If this is null or empty,
    /// the first class matching the specified name in all assemblies is returned.</param>
    /// <param name="className">Name of the class to find.</param>
    /// <returns>The Type of the class if found, null otherwise.</returns>
    public static Type FindClass(string assemblyName, string className) {
        Type type;
        bool hasAssemblyName = !String.IsNullOrEmpty(assemblyName);
        string fullName = hasAssemblyName ? className + ", " + assemblyName : className;
        if (typeCache.TryGetValue(fullName, out type)) {
            return type;
        }
        type = Type.GetType(fullName);
        if (type == null) {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                if (hasAssemblyName) {
                    if (assembly.GetName().Name == assemblyName) {
                        type = Type.GetType(className + ", " + assembly.FullName);
                        break;
                    }
                } else {
                    // Search for the first instance of a class matching this name in all
                    // assemblies.
                    foreach (var currentType in assembly.GetTypes()) {
                        if (currentType.FullName == className) {
                            type = currentType;
                            break;
                        }
                    }
                    if (type != null) break;
                }
            }

        }
        if (type != null) typeCache[fullName] = type;
        return type;
    }

    /// <summary>
    /// Call a method on an object with named arguments.
    /// </summary>
    /// <param name="objectInstance">Object to call a method on.</param>
    /// <param name="methodName">Name of the method to call.</param>
    /// <param name="args">Positional arguments of the method.</param>
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
    /// <param name="args">Positional arguments of the method.</param>
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
    /// <param name="args">Positional arguments of the method.</param>
    /// <param name="namedArgs">Named arguments of the method.</param>
    /// <returns>object returned by the method.</returns>
    public static object InvokeMethod(
            Type type, object objectInstance, string methodName,
            object[] args, Dictionary<string, object> namedArgs = null) {
        object[] parameterValues = null;
        int numberOfPositionalArgs = args != null ? args.Length : 0;
        int numberOfNamedArgs = namedArgs != null ? namedArgs.Count : 0;
        MethodInfo foundMethod = null;
        foreach (var method in type.GetMethods()) {
            if (method.Name != methodName) continue;
            var parameters = method.GetParameters();
            int numberOfParameters = parameters.Length;
            parameterValues = new object[numberOfParameters];
            int matchedPositionalArgs = 0;
            int matchedNamedArgs = 0;
            bool matchedAllRequiredArgs = true;
            foreach (var parameter in parameters) {
                var parameterType = parameter.ParameterType;
                int position = parameter.Position;
                if (position < numberOfPositionalArgs) {
                    var positionalArg = args[position];
                    // If the parameter type doesn't match, ignore this method.
                    if (positionalArg != null &&
                        !parameterType.IsAssignableFrom(positionalArg.GetType())) {
                        break;
                    }
                    parameterValues[position] = positionalArg;
                    matchedPositionalArgs ++;
                } else if (parameter.RawDefaultValue != DBNull.Value) {
                    object namedValue = parameter.RawDefaultValue;
                    if (numberOfNamedArgs > 0) {
                        object namedArg;
                        if (namedArgs.TryGetValue(parameter.Name, out namedArg)) {
                            // If the parameter type doesn't match, ignore this method.
                            if (namedArg != null &&
                                !parameterType.IsAssignableFrom(namedArg.GetType())) {
                                break;
                            }
                            namedValue = namedArg;
                            matchedNamedArgs ++;
                        }
                    }
                    parameterValues[position] = namedValue;
                } else {
                    matchedAllRequiredArgs = false;
                    break;
                }
            }
            // If all arguments were consumed by the method, we've found a match.
            if (matchedAllRequiredArgs &&
                matchedPositionalArgs == numberOfPositionalArgs &&
                matchedNamedArgs == numberOfNamedArgs) {
                foundMethod = method;
                break;
            }
        }
        if (foundMethod == null) {
            throw new Exception(String.Format("Method {0}.{1} not found", type.Name, methodName));
        }
        return foundMethod.Invoke(objectInstance, parameterValues);
    }

    /// <summary>
    /// Call a method on a static event.
    /// </summary>
    /// <param name="type">Class to call the method on.</param>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="action">Action to add/remove from the event.</param>
    /// <param name="getMethod">The func to get the method on the event.</param>
    /// <returns>True if the method is called successfully.</returns>
    private static bool InvokeStaticEventMethod(Type type, string eventName, Action action,
                                                Func<EventInfo, MethodInfo> getMethod) {
        EventInfo eventInfo = type.GetEvent(eventName);
        if (eventInfo != null) {
            MethodInfo method = getMethod(eventInfo);
            Delegate d = Delegate.CreateDelegate(
                    eventInfo.EventHandlerType, action.Target, action.Method);
            System.Object[] args = { d };
            if (method.IsStatic) {
                method.Invoke(null, args);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Call adder method on a static event.
    /// </summary>
    /// <param name="type">Class to call the method on.</param>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="action">Action to add from the event.</param>
    /// <returns>True if the action is added successfully.</returns>
    public static bool InvokeStaticEventAddMethod(Type type, string eventName, Action action) {
        return InvokeStaticEventMethod(type, eventName, action,
                                       eventInfo => eventInfo.GetAddMethod());
    }

    /// <summary>
    /// Call remover method on a static event.
    /// </summary>
    /// <param name="type">Class to call the method on.</param>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="action">Action to remove from the event.</param>
    /// <returns>True if the action is removed successfully.</returns>
    public static bool InvokeStaticEventRemoveMethod(Type type, string eventName, Action action) {
        return InvokeStaticEventMethod(type, eventName, action,
                                       eventInfo => eventInfo.GetRemoveMethod());
    }

    // Name of UnityEditor.AssemblyReloadEvents.beforeAssemblyReload event.
    private const string BeforeAssemblyReloadEventName = "beforeAssemblyReload";

    /// <summary>
    /// Register for beforeAssemblyReload event.
    /// Note that AssemblyReloadEvents is only availabe from Unity 2017.
    /// </summary>
    /// <param name="action">Action to register for.</param>
    /// <returns>True if the action is registered successfully.</returns>
    public static bool RegisterBeforeAssemblyReloadEvent(Action action) {
        Type eventType = VersionHandler.FindClass("UnityEditor",
                                                  "UnityEditor.AssemblyReloadEvents");
        if (eventType != null) {
            return InvokeStaticEventAddMethod(eventType, BeforeAssemblyReloadEventName, action);
        }
        return false;
    }

    /// <summary>
    /// Unregister for beforeAssemblyReload event.
    /// Note that AssemblyReloadEvents is only availabe from Unity 2017.
    /// </summary>
    /// <param name="action">Action to unregister for.</param>
    /// <returns>True if the action is unregistered successfully.</returns>
    public static bool UnregisterBeforeAssemblyReloadEvent(Action action) {
        Type eventType = VersionHandler.FindClass("UnityEditor",
                                                  "UnityEditor.AssemblyReloadEvents");
        if (eventType != null) {
            return InvokeStaticEventRemoveMethod(eventType, BeforeAssemblyReloadEventName, action);
        }
        return false;
    }
}

}  // namespace Google

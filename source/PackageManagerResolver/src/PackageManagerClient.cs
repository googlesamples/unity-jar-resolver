// <copyright file="PackageMigratorClient.cs" company="Google LLC">
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
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Google {

/// <summary>
/// Provides a safe, simple interface that can be used across any Unity version for interaction
/// with the Unity Package Manager.
/// </summary>
internal static class PackageManagerClient {

    /// <summary>
    /// Wrapper object.
    /// </summary>
    public class Wrapper {

        /// <summary>
        /// Wrapped instance.
        /// </summary>
        protected object instance;

        /// <summary>
        /// Construct an null wrapper.
        /// </summary>
        public Wrapper() {}

        /// <summary>
        /// Construct a wrapper around an object.
        /// </summary>
        /// <param name="instance">Object to wrap.</param>
        public Wrapper(object instance) {
            this.instance = instance;
        }


        /// <summary>
        /// Convert a collection of objects to a field separated string.
        /// </summary>
        /// <param name="objects">Collection of objects to convert to a list of strings.</param>
        /// <param name="separator">The string to use as a separator.</param>
        /// <returns>A string that consists of members of <paramref name="objects"/> delimited
        /// by <paramref name="separator"/> string.</returns>.
        protected static string ObjectCollectionToString<T>(IEnumerable<T> objects,
                                                            string separator) {
            var components = new List<string>();
            foreach (var obj in objects) components.Add(obj.ToString());
            return String.Join(separator, components.ToArray());
        }
    }

    /// <summary>
    /// Wraps an enumerable list of objects in a collection of wrappers.
    /// </summary>
    private class CollectionWrapper<T> where T : Wrapper, new() {

        /// <summary>
        /// Objects wrapped in instances of type T.
        /// </summary>
        private ICollection<T> wrapped = new List<T>();

        /// <summary>
        /// Wrap an enumerable set of objects by the wrapper class.
        /// </summary>
        public CollectionWrapper(IEnumerable instances) {
            if (instances != null) {
                foreach (var instance in instances) {
                    wrapped.Add(Activator.CreateInstance(typeof(T),
                                                         new object[] { instance }) as T);
                }
            }
        }

        /// <summary>
        /// Get the collection.
        /// </summary>
        public ICollection<T> Collection { get { return wrapped; } }
    }

    /// <summary>
    /// Wrapper for PackageManager.Error.
    /// </summary>
    public class Error : Wrapper {

        /// <summary>
        /// PackageManager.Error type.
        /// </summary>
        private static Type type;

        /// <summary>
        /// PackageManager.Error.errorCode property.
        /// </summary>
        private static PropertyInfo errorCodeProperty;

        /// <summary>
        /// PackageManager.Error.message property.
        /// </summary>
        private static PropertyInfo messageProperty;

        /// <summary>
        /// Message to use if "instance" is null.
        /// </summary>
        private string fallbackMessage;

        /// <summary>
        /// Wrap the supplied error object.
        /// </summary>
        /// <param name="error">Error instance to wrap.</param>
        public Error(object error) : base(error) {
            type = type ??
                VersionHandler.FindClass("UnityEditor", "UnityEditor.PackageManager.Error");
            if (type != null) {
                errorCodeProperty = errorCodeProperty ?? type.GetProperty("errorCode");
                messageProperty = messageProperty ?? type.GetProperty("message");
            }
            instance = error;
            fallbackMessage = instance != null ? "Package Manager Not Supported" : "";
        }

        /// <summary>
        /// Get the error code as a string.
        /// </summary>
        public string ErrorCodeString {
            get {
                return instance != null ?
                    errorCodeProperty.GetValue(instance, null).ToString() : "";
            }
        }

        /// <summary>
        /// Get the error message.
        /// </summary>
        public string Message {
            get {
                return instance != null ?
                    messageProperty.GetValue(instance, null) as string : fallbackMessage;
            }
        }

        /// <summary>
        /// Convert to a string.
        /// </summary>
        /// <returns>String representation.</returns>
        public override string ToString() {
            var components = new List<string>();
            var message = Message;
            if (!String.IsNullOrEmpty(message)) components.Add(message);
            var errorCodeString = ErrorCodeString;
            if (!String.IsNullOrEmpty(errorCodeString)) {
                components.Add(String.Format("({0})", errorCodeString));
            }
            return String.Join(" ", components.ToArray());
        }
    }

    /// <summary>
    /// Wrapper for PackageManager.AuthorInfo.
    /// </summary>
    public class AuthorInfo : Wrapper {

        /// <summary>
        /// PackageManager.AuthorInfo class.
        /// </summary>
        private static Type type;

        /// <summary>
        /// PackageManager.AuthorInfo.email property.
        /// </summary>
        private static PropertyInfo emailProperty;

        /// <summary>
        /// PackageManager.AuthorInfo.name property.
        /// </summary>
        private static PropertyInfo nameProperty;

        /// <summary>
        /// PackageManager.AuthorInfo.url property.
        /// </summary>
        private static PropertyInfo urlProperty;

        /// <summary>
        /// Wrap an AuthorInfo instance.
        /// </summary>
        /// <param name="authorInfo">Instance to wrap.</param>
        public AuthorInfo(object authorInfo) : base(authorInfo) {
            type = type ??
                VersionHandler.FindClass("UnityEditor", "UnityEditor.PackageManager.AuthorInfo");
            if (type != null) {
                emailProperty = emailProperty ?? type.GetProperty("email");
                nameProperty = nameProperty ?? type.GetProperty("name");
                urlProperty = urlProperty ?? type.GetProperty("url");
            }
            instance = authorInfo;
        }

        /// <summary>
        /// email of a package author.
        /// </summary>
        public string Email { get { return emailProperty.GetValue(instance, null) as string; } }

        /// <summary>
        /// Name of a package author.
        /// </summary>
        public string Name { get { return nameProperty.GetValue(instance, null) as string; } }

        /// <summary>
        /// URL of a package author.
        /// </summary>
        public string Url { get { return urlProperty.GetValue(instance, null) as string; } }

        /// <summary>
        /// Convert to a string.
        /// </summary>
        /// <returns>String representation.</returns>
        public override string ToString() {
            var components = new string[] {Name, Url, Email};
            var toStringComponents = new List<string>();
            foreach (var component in components) {
                if (!String.IsNullOrEmpty(component)) toStringComponents.Add(component);
            }
            return String.Join(", ", toStringComponents.ToArray());
        }
    }

    /// <summary>
    /// Wrapper for PackageManager.DependencyInfo.
    /// </summary>
    public class DependencyInfo : Wrapper {

        /// <summary>
        /// PackageManager.DependencyInfo class.
        /// </summary>
        private static Type type;

        /// <summary>
        /// PackageManager.DependencyInfo.name property.
        /// </summary>
        private static PropertyInfo nameProperty;

        /// <summary>
        /// PackageManager.DependencyInfo.version property.
        /// </summary>
        private static PropertyInfo versionProperty;

        /// <summary>
        /// Empty constructor for use in generics.
        /// </summary>
        public DependencyInfo() {}

        /// <summary>
        /// Wrap a DependencyInfo instance.
        /// </summary>
        /// <param name="dependencyInfo">Instance to wrap.</param>
        public DependencyInfo(object dependencyInfo) : base(dependencyInfo) {
            type = type ??
                VersionHandler.FindClass("UnityEditor",
                                         "UnityEditor.PackageManager.DependencyInfo");
            if (type != null) {
                nameProperty = nameProperty ?? type.GetProperty("name");
                versionProperty = versionProperty ?? type.GetProperty("version");
            }
            instance = dependencyInfo;
        }

        /// <summary>
        /// Get the name of the dependency.
        /// </summary>
        public string Name { get { return nameProperty.GetValue(instance, null) as string; } }

        /// <summary>
        /// Get the version of the dependency.
        /// </summary>
        public string Version { get { return versionProperty.GetValue(instance, null) as string; } }

        /// <summary>
        /// Convert to a string.
        /// </summary>
        /// <returns>String representation.</returns>
        public override string ToString() {
            return String.Format("{0}@{1}", Name, Version);
        }
    }

    /// <summary>
    /// Wrapper for PackageManager.VersionsInfo.
    /// </summary>
    public class VersionsInfo : Wrapper {

        /// <summary>
        /// PackageManager.VersionsInfo type.
        /// </summary>
        private static Type type;

        /// <summary>
        /// PackageManager.VersionsInfo.all property.
        /// </summary>
        private static PropertyInfo allProperty;

        /// <summary>
        /// PackageManager.VersionsInfo.compatible property.
        /// </summary>
        private static PropertyInfo compatibleProperty;

        /// <summary>
        /// PackageManager.VersionsInfo.latest property.
        /// </summary>
        private static PropertyInfo latestProperty;

        /// <summary>
        /// PackageManager.VersionsInfo.latestCompatible property.
        /// </summary>
        private static PropertyInfo latestCompatibleProperty;

        /// <summary>
        /// PackageManager.VersionsInfo.recommended property.
        /// </summary>
        private static PropertyInfo recommendedProperty;

        /// <summary>
        /// Empty constructor for use in generics.
        /// </summary>
        public VersionsInfo() {}

        /// <summary>
        /// Wrap a VersionsInfo instance.
        /// </summary>
        /// <param name="versionsInfo">Instance to wrap.</param>
        public VersionsInfo(object versionsInfo) : base(versionsInfo) {
            type = type ??
                VersionHandler.FindClass("UnityEditor", "UnityEditor.PackageManager.VersionsInfo");
            if (type != null) {
                allProperty = allProperty ?? type.GetProperty("all");
                compatibleProperty = compatibleProperty ?? type.GetProperty("compatible");
                latestProperty = latestProperty ?? type.GetProperty("latest");
                latestCompatibleProperty = latestCompatibleProperty ??
                    type.GetProperty("latestCompatible");
                recommendedProperty = recommendedProperty ?? type.GetProperty("recommended");
            }
            instance = versionsInfo;
        }

        /// <summary>
        /// All versions.
        /// </summary>
        public string[] All { get { return allProperty.GetValue(instance, null) as string[]; } }

        /// <summary>
        /// Compatible versions for the current version of Unity.
        /// </summary>
        public string[] Compatible {
            get {
                return compatibleProperty.GetValue(instance, null) as string[];
            }
        }

        /// <summary>
        /// Latest version of the package.
        /// </summary>
        public string Latest { get { return latestProperty.GetValue(instance, null) as string; } }

        /// <summary>
        /// Latest version compatible with the current version of Unity.
        /// </summary>
        public string LatestCompatible {
            get {
                return latestCompatibleProperty.GetValue(instance, null) as string;
            }
        }

        /// <summary>
        /// Recommended version of the package.
        /// </summary>
        public string Recommended {
            get {
                return recommendedProperty.GetValue(instance, null) as string;
            }
        }
    }

    /// <summary>
    /// Wrapper for PackageManager.PackageInfo.
    /// </summary>
    public class PackageInfo : Wrapper {

        /// <summary>
        /// PackageManager.PackageInfo class.
        /// </summary>
        private static Type typeStore;

        /// <summary>
        /// Get the PackageInfo class.
        /// </summary>
        public static Type Type {
            get {
                typeStore = typeStore ??
                    VersionHandler.FindClass("UnityEditor",
                                             "UnityEditor.PackageManager.PackageInfo");
                return typeStore;
            }
        }

        /// <summary>
        /// PackageManager.PackageInfo.author property.
        /// </summary>
        private static PropertyInfo authorProperty;

        /// <summary>
        /// PackageManager.PackageInfo.category property.
        /// </summary>
        private static PropertyInfo categoryProperty;

        /// <summary>
        /// PackageManager.PackageInfo.dependencies property.
        /// </summary>
        private static PropertyInfo dependenciesProperty;

        /// <summary>
        /// PackageManager.PackageInfo.description property.
        /// </summary>
        private static PropertyInfo descriptionProperty;

        /// <summary>
        /// PackageManager.PackageInfo.displayName property.
        /// </summary>
        private static PropertyInfo displayNameProperty;

        /// <summary>
        /// PackageManager.PackageInfo.keywords property.
        /// </summary>
        private static PropertyInfo keywordsProperty;

        /// <summary>
        /// PackageManager.PackageInfo.name property.
        /// </summary>
        private static PropertyInfo nameProperty;

        /// <summary>
        /// PackageManager.PackageInfo.packageId property.
        /// </summary>
        private static PropertyInfo packageIdProperty;

        /// <summary>
        /// PackageManager.PackageInfo.resolvedDependencies property.
        /// </summary>
        private static PropertyInfo resolvedDependenciesProperty;

        /// <summary>
        /// PackageManager.PackageInfo.version property.
        /// </summary>
        private static PropertyInfo versionProperty;

        /// <summary>
        /// PackageManager.PackageInfo.versions property.
        /// </summary>
        private static PropertyInfo versionsProperty;

        /// <summary>
        /// Empty constructor for use in generics.
        /// </summary>
        public PackageInfo() {}

        /// <summary>
        /// Wrap a packageInfo object.
        /// </summary>
        /// <param name="packageInfo">PackageInfo to wrap.</param>
        public PackageInfo(object packageInfo) : base(packageInfo) {
            var type = Type;
            if (type != null) {
                authorProperty = authorProperty ?? type.GetProperty("author");
                categoryProperty = categoryProperty ?? type.GetProperty("category");
                dependenciesProperty = dependenciesProperty ?? type.GetProperty("dependencies");
                descriptionProperty = descriptionProperty ?? type.GetProperty("description");
                displayNameProperty = displayNameProperty ?? type.GetProperty("displayName");
                keywordsProperty = keywordsProperty ?? type.GetProperty("keywords");
                nameProperty = nameProperty ?? type.GetProperty("name");
                packageIdProperty = packageIdProperty ?? type.GetProperty("packageId");
                resolvedDependenciesProperty = resolvedDependenciesProperty ??
                    type.GetProperty("resolvedDependencies");
                versionProperty = versionProperty ?? type.GetProperty("version");
                versionsProperty = versionsProperty ?? type.GetProperty("versions");
            }
        }

        /// <summary>
        /// Get the package author.
        /// </summary>
        public AuthorInfo Author {
            get {
                // Author isn't exposed until Unity 2018.3.
                var author = authorProperty != null ? authorProperty.GetValue(instance, null) :
                    null;
                return author != null ? new AuthorInfo(author) : null;
            }
        }

        /// <summary>
        /// Get the package category.
        /// </summary>
        public string Category {
            get { return categoryProperty.GetValue(instance, null) as string; }
        }

        /// <summary>
        /// Get the package dependencies.
        /// </summary>
        public ICollection<DependencyInfo> Dependencies {
            get {
                // Not available until Unity 2018.3.
                return dependenciesProperty != null ?
                    (new CollectionWrapper<DependencyInfo>(
                         dependenciesProperty.GetValue(instance, null) as IEnumerable)).Collection :
                    new DependencyInfo[] {};
            }
        }

        /// <summary>
        /// Get the package description.
        /// </summary>
        public string Description {
            get { return descriptionProperty.GetValue(instance, null) as string; }
        }

        /// <summary>
        /// Get the package display name.
        /// </summary>
        public string DisplayName {
            get { return displayNameProperty.GetValue(instance, null) as string; }
        }

        /// <summary>
        /// Get the package keywords.
        /// </summary>
        public ICollection<string> Keywords {
            get {
                // Not available until Unity 2018.3.
                var value = keywordsProperty != null ?
                    keywordsProperty.GetValue(instance, null) as string[] : null;
                return value ?? new string[] {};
            }
        }

        /// <summary>
        /// Get the package's unique name.
        /// </summary>
        public string Name {
            get { return nameProperty.GetValue(instance, null) as string; }
        }

        /// <summary>
        /// Get the package ID.
        /// </summary>
        public string PackageId {
            get { return packageIdProperty.GetValue(instance, null) as string; }
        }

        /// <summary>
        /// Get the resolved direct and indirect dependencies of this package.
        /// </summary>
        public ICollection<DependencyInfo> ResolvedDependencies {
            get {
                return resolvedDependenciesProperty != null ?
                    (new CollectionWrapper<DependencyInfo>(
                        resolvedDependenciesProperty.GetValue(instance, null)
                        as IEnumerable)).Collection : new DependencyInfo[] {};
            }
        }

        /// <summary>
        /// Get the package version.
        /// </summary>
        public string Version {
            get { return versionProperty.GetValue(instance, null) as string; }
        }

        /// <summary>
        /// Get available versions of this package.
        /// </summary>
        public ICollection<VersionsInfo> Versions {
            get {
                // Not available until Unity 2018.
                return versionsProperty != null ? (new CollectionWrapper<VersionsInfo>(
                        versionsProperty.GetValue(instance, null) as IEnumerable)).Collection :
                    new VersionsInfo[] {};
            }
        }


        /// <summary>
        /// Convert to a string.
        /// </summary>
        public override string ToString() {
            var components = new List<string>();
            components.Add(String.Format("displayName: '{0}'", DisplayName));
            components.Add(String.Format("name: {0}", Name));
            components.Add(String.Format("packageId: {0}", PackageId));
            components.Add(String.Format("author: '{0}'", Author));
            components.Add(String.Format("version: {0}", Version));
            components.Add(String.Format("availableVersions: [{0}]",
                                         ObjectCollectionToString(Versions, ", ")));
            components.Add(String.Format("dependencies: [{0}]",
                                         ObjectCollectionToString(Dependencies, ", ")));
            components.Add(String.Format("resolvedDependencies: [{0}]",
                                         ObjectCollectionToString(ResolvedDependencies, ", ")));
            components.Add(String.Format("category: '{0}'", Category));
            components.Add(String.Format("keywords: [{0}]",
                                         ObjectCollectionToString(Keywords, ", ")));
            return String.Join(", ", components.ToArray());
        }
    }

    /// <summary>
    /// Wrapper for PackageManager.Requests.Request.
    /// </summary>
    private class Request : Wrapper {

        /// <summary>
        /// PackageManager.Request type.
        /// </summary>
        private static Type type;

        /// <summary>
        /// PackageManager.Request.Error property.
        /// </summary>
        private static PropertyInfo errorProperty;

        /// <summary>
        /// PackageManager.Request.IsCompleted property.
        /// </summary>
        private static PropertyInfo isCompletedProperty;

        /// <summary>
        /// Initialize the wrapper.
        /// </summary>
        /// <param name="request">Request instance to wrap.</param>
        public Request(object request) : base(request) {
            type = type ??
                VersionHandler.FindClass("UnityEditor",
                                         "UnityEditor.PackageManager.Requests.Request");
            if (type != null) {
                errorProperty = errorProperty ?? type.GetProperty("Error");
                isCompletedProperty = isCompletedProperty ?? type.GetProperty("IsCompleted");
            }
        }

        /// <summary>
        /// Whether the request is complete.
        /// </summary>
        public bool IsComplete {
            get { return (bool)isCompletedProperty.GetValue(instance, null); }
        }

        /// <summary>
        /// Error associated with the request (valid when IsComplete is true).
        /// </summary>
        public Error Error {
            get {
                var error = errorProperty.GetValue(instance, null);
                return error != null ? new PackageManagerClient.Error(error) : null;
            }
        }
    }

    /// <summary>
    /// Request that wraps a class of type UnityEditor.PackageManager.Requests.typeName which
    /// returns a collection of objects of type T.
    /// </summary>
    private class CollectionRequest<T> : Request where T : Wrapper, new()  {

        /// <summary>
        /// UnityEditor.PackageManager.Requests.typeName class.
        /// </summary>
        private Type collectionRequestType;

        /// <summary>
        /// PackageManager.Requests.typeName property.
        /// </summary>
        private PropertyInfo resultProperty;

        /// <summary>
        /// Create a wrapper around UnityEditor.PackageManager.Requests.typeName.
        /// </summary>
        /// <param name="request">Object to wrap.</param>
        /// <param name="typeName">Name of the type under
        /// UnityEditor.PackageManager.Requests to wrap.</param>
        public CollectionRequest(object request, string typeName) : base(request) {
            collectionRequestType =
                VersionHandler.FindClass("UnityEditor",
                                         "UnityEditor.PackageManager.Requests." + typeName);
            if (collectionRequestType != null) {
                resultProperty = collectionRequestType.GetProperty("Result");
            }
        }

        /// <summary>
        /// Get the set of packages returned by the request.
        /// </summary>
        public ICollection<T> Result {
            get {
                return (new CollectionWrapper<T>(resultProperty.GetValue(instance, null) as
                                                 IEnumerable).Collection);
            }
        }
    }

    /// <summary>
    /// Wrapper for UnityEditor.PackageManager.Requests.AddRequest
    /// </summary>
    private class AddRequest : Request {

        /// <summary>
        /// UnityEditor.PackageManager.Requests.AddRequest class.
        /// </summary>
        private static Type addRequestType;

        /// <summary>
        /// PackageManager.Requests.AddRequest.Result property.
        /// </summary>
        private static PropertyInfo resultProperty;

        /// <summary>
        /// Create a wrapper around AddRequest.
        /// </summary>
        /// <param name="request">Object to wrap.</param>
        public AddRequest(object request) : base(request) {
            addRequestType = addRequestType ??
                VersionHandler.FindClass("UnityEditor",
                                         "UnityEditor.PackageManager.Requests.AddRequest");
            if (addRequestType != null) {
                resultProperty = resultProperty ?? addRequestType.GetProperty("Result");
            }
        }

        /// <summary>
        /// Get the installed package if successful, null otherwise.
        /// </summary>
        public PackageInfo Result {
            get {
                var result = resultProperty.GetValue(instance, null);
                return result != null ? new PackageInfo(result) : null;
            }
        }
    }

    /// <summary>
    /// Wrapper for UnityEditor.PackageManager.Requests.RemoveRequest
    /// </summary>
    private class RemoveRequest : Request {

        /// <summary>
        /// UnityEditor.PackageManager.Requests.RemoveRequest class.
        /// </summary>
        private static Type removeRequestType;

        /// <summary>
        /// PackageManager.Requests.RemoveRequest.Result property.
        /// </summary>
        private static PropertyInfo resultProperty;

        /// <summary>
        /// Create a wrapper around RemoveRequest.
        /// </summary>
        /// <param name="request">Object to wrap.</param>
        public RemoveRequest(object request) : base(request) {
            removeRequestType = removeRequestType ??
                VersionHandler.FindClass("UnityEditor",
                                         "UnityEditor.PackageManager.Requests.RemoveRequest");
            if (removeRequestType != null) {
                resultProperty = resultProperty ?? removeRequestType.GetProperty("PackageIdOrName");
            }
        }

        /// <summary>
        /// Get the removed package if successful, null otherwise.
        /// </summary>
        public string Result {
            get {
                var result = resultProperty.GetValue(instance, null);
                return result != null ? result as string : null;
            }
        }
    }

    /// <summary>
    /// Wrapper for UnityEditor.PackageManager.Requests.ListRequest
    /// </summary>
    private class ListRequest : CollectionRequest<PackageInfo> {

        /// <summary>
        /// Create a wrapper around ListRequest.
        /// </summary>
        /// <param name="request">Object to wrap.</param>
        public ListRequest(object request) : base(request, "ListRequest") {}
    }

    /// <summary>
    /// Wrapper for UnityEditor.PackageManager.Requests.SearchRequest
    /// </summary>
    private class SearchRequest : CollectionRequest<PackageInfo> {

        /// <summary>
        /// Create a wrapper around SearchRequest.
        /// </summary>
        /// <param name="request">Object to wrap.</param>
        public SearchRequest(object request) : base(request, "SearchRequest") {}
    }

    /// <summary>
    /// Wrapper for PackageManager.Client.
    /// </summary>
    private static class Client {
        /// <summary>
        /// PackageManager.Client static class.
        /// </summary>
        private static Type type;

        /// <summary>
        /// Method to add a package.
        /// </summary>
        private static MethodInfo addMethod;

        /// <summary>
        /// Method to remove a package.
        /// </summary>
        private static MethodInfo removeMethod;

        /// <summary>
        /// Method to list packages.
        /// </summary>
        private static MethodInfo listMethod;

        /// <summary>
        /// Method to list packages with an optional offline mode parameter.
        /// </summary>
        private static MethodInfo listMethodOfflineMode;

        /// <summary>
        /// Method to search for a package.
        /// </summary>
        private static MethodInfo searchMethod;

        /// <summary>
        /// Method to search for all available packages.
        /// </summary>
        private static MethodInfo searchAllMethod;

        /// <summary>
        /// Cache the PackageManager.Client class and methods.
        /// </summary>
        static Client() {
            type = type ??
                VersionHandler.FindClass("UnityEditor", "UnityEditor.PackageManager.Client");
            if (type != null) {
                addMethod = addMethod ?? type.GetMethod("Add", new [] { typeof(String) });
                removeMethod = removeMethod ?? type.GetMethod("Remove", new [] { typeof(String) });
                listMethod = listMethod ?? type.GetMethod("List", Type.EmptyTypes);
                listMethodOfflineMode =
                    listMethodOfflineMode ?? type.GetMethod("List", new [] { typeof(bool) });
                searchMethod = searchMethod ?? type.GetMethod("Search", new [] { typeof(String) });
                searchAllMethod = searchAllMethod ?? type.GetMethod("SearchAll", Type.EmptyTypes);
            }
        }

        /// <summary>
        /// Determine Whether the package manager is available.
        /// </summary>
        public static bool Available {
            get {
                // The current set of methods are checked for as this provides a baseline set
                // of functionality for listing, searching and adding / removing packages across
                // all versions of Unity since the package manager was introduced.
                // listMethodOfflineMode is available in Unity 2018 and above,
                // listMethod is available in Unity 2017.x so always utilize the behavior from
                // 2017 (i.e no offline queries).
                return type != null && addMethod != null && removeMethod != null &&
                    (listMethod != null || listMethodOfflineMode != null) && searchMethod != null;
            }
        }

        /// <summary>
        /// Add a package.
        /// </summary>
        /// <param name="packageIdOrName">Name of the package to add.</param>
        /// <returns>Request object to monitor progress.</returns>
        public static AddRequest Add(string packageIdOrName) {
            return new AddRequest(addMethod.Invoke(null, new object[] { packageIdOrName }));
        }

        /// <summary>
        /// Remove a package.
        /// </summary>
        /// <param name="packageIdOrName">Name of the package to add.</param>
        /// <returns>Request object to monitor progress.</returns>
        public static RemoveRequest Remove(string packageIdOrName) {
            return new RemoveRequest(removeMethod.Invoke(null, new object[] { packageIdOrName }));
        }

        /// <summary>
        /// List packages available to install.
        /// </summary>
        public static ListRequest List() {
            object request = listMethodOfflineMode != null ?
                listMethodOfflineMode.Invoke(null, new object[] { false }) :
                listMethod.Invoke(null, null);
            return new ListRequest(request);
        }

        /// <summary>
        /// Search for an available package.
        /// </summary>
        /// <param name="packageIdOrName">Name of the package to add.</param>
        /// <returns>Request object to monitor progress.</returns>
        public static SearchRequest Search(string packageIdOrName) {
            return new SearchRequest(searchMethod.Invoke(null, new object[] { packageIdOrName }));
        }

        /// <summary>
        /// Search for all available packages.
        /// </summary>
        /// <returns>Request object to monitor progress or null if SearchAll() isn't
        /// available.</returns>
        public static SearchRequest SearchAll() {
            return searchAllMethod != null ?
                new SearchRequest(searchAllMethod.Invoke(null, new object[] {})) : null;
        }
    }

    /// <summary>
    /// Enumerates through a set of items, optionally reporting progress.
    /// </summary>
    private class EnumeratorProgressReporter {
        /// <summary>
        /// Enumerator of packages to search for.
        /// </summary>
        private IEnumerator<string> enumerator;

        /// <summary>
        /// Called as the search progresses.
        /// </summary>
        private Action<float, string> progress = null;

        /// <summary>
        /// Number of items to search.
        /// </summary>
        private int itemCount;

        /// <summary>
        /// Number of items searched.
        /// </summary>
        private int itemIndex = 0;

        /// <summary>
        /// Construct the instance.
        /// </summary>
        /// <param name="items">Items to iterate through.</param>
        /// <param name="progressReporter">Reports progress as iteration proceeds.</param>
        public EnumeratorProgressReporter(ICollection<string> items,
                                   Action<float, string> progressReporter) {
            itemCount = items.Count;
            enumerator = items.GetEnumerator();
            progress = progressReporter;
        }

        /// <summary>
        /// Report progress.
        /// </summary>
        /// <param name="itemProgress">Progress through the operation.</param>
        /// <param name="item">Item being worked on.</param>
        protected void ReportProgress(float itemProgress, string item) {
            if (progress != null) {
                try {
                    progress(itemProgress, item);
                } catch (Exception e) {
                    PackageManagerClient.Logger.Log(
                        String.Format("Progress reporter raised exception {0}", e),
                        level: LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// Get the next item.
        /// </summary>
        protected string NextItem() {
            if (!enumerator.MoveNext()) {
                ReportProgress(1.0f, "");
                return null;
            }
            var item = enumerator.Current;
            ReportProgress((float)itemIndex / (float)itemCount, item);
            itemIndex++;
            return item;
        }
    }

    /// <summary>
    /// Result of a package remove operation.
    /// </summary>
    public class RemoveResult {

        /// <summary>
        /// Package ID involved in the remove operation.
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// Error.
        /// </summary>
        public Error Error { get; set; }

        /// <summary>
        /// Construct an empty result.
        /// </summary>
        public RemoveResult() {
          this.PackageId = "";
          this.Error = new Error(null);
        }
    }

    /// <summary>
    /// Result of a package add / install operation.
    /// </summary>
    public class InstallResult {

        /// <summary>
        /// Information about the installed package, null if no package is installed.
        /// </summary>
        public PackageInfo Package { get; set; }

        /// <summary>
        /// Error.
        /// </summary>
        public Error Error { get; set; }

        /// <summary>
        /// Construct an empty result.
        /// </summary>
        public InstallResult() {
          this.Package = null;
          this.Error = new Error(null);
        }
    }

    /// <summary>
    /// Adds a set of packages to the project.
    /// </summary>
    private class PackageInstaller : EnumeratorProgressReporter {

        /// <summary>
        /// Pending add request.
        /// </summary>
        private AddRequest request = null;

        /// <summary>
        /// Result of package installation.
        /// </summary>
        private Dictionary<string, InstallResult> installed =
            new Dictionary<string, InstallResult>();

        /// <summary>
        /// Called when the operation is complete.
        /// </summary>
        Action<Dictionary<string, InstallResult>> complete;

        /// <summary>
        /// Install a set of packages using the package manager.
        /// </summary>
        /// <param name="packageIdsOrNames">Package IDs or names to search for.</param>
        /// <param name="complete">Called when the operation is complete.</param>
        /// <param name="progress">Reports progress through the search.</param>
        public PackageInstaller(ICollection<string> packageIdsOrNames,
                                Action<Dictionary<string, InstallResult>> complete,
                                Action<float, string> progress = null) :
            base(packageIdsOrNames, progress) {
            this.complete = complete;
            InstallNext();
        }

        /// <summary>
        /// Install the next package.
        /// </summary>
        private void InstallNext() {
            var packageIdOrName = NextItem();
            if (String.IsNullOrEmpty(packageIdOrName)) {
                var completion = complete;
                complete = null;
                completion(installed);
                return;
            }
            request = Client.Add(packageIdOrName);
            RunOnMainThread.PollOnUpdateUntilComplete(() => {
                    if (!request.IsComplete) return false;
                    if (complete == null) return true;
                    installed[packageIdOrName] = new InstallResult() {
                        Package = request.Result,
                        Error = request.Error ?? new Error(null)
                    };
                    RunOnMainThread.Run(() => { InstallNext(); });
                    return true;
                });
        }
    }

    /// <summary>
    /// Result of a search operation.
    /// </summary>
    public class SearchResult {

        /// <summary>
        /// Packages found.
        /// </summary>
        public ICollection<PackageInfo> Packages { get; set; }

        /// <summary>
        /// Error.
        /// </summary>
        public Error Error { get; set; }

        /// <summary>
        /// Construct an empty result.
        /// </summary>
        public SearchResult() {
            Packages = new List<PackageInfo>();
            Error = new Error(null);
        }
    }

    /// <summary>
    /// Searches for packages by name.
    /// </summary>
    private class PackageSearcher : EnumeratorProgressReporter {

        /// <summary>
        /// Pending search request.
        /// </summary>
        private SearchRequest request = null;

        /// <summary>
        /// Packages found by the search.
        /// </summary>
        private Dictionary<string, SearchResult> found = new Dictionary<string, SearchResult>();

        /// <summary>
        /// Called when the operation is complete.
        /// </summary>
        private Action<Dictionary<string, SearchResult>> complete;

        /// <summary>
        /// Search for a set of packages in the package manager.
        /// </summary>
        /// <param name="packageIdsOrNames">Package IDs or names to search for.</param>
        /// <param name="complete">Called when the operation is complete.</param>
        /// <param name="progress">Reports progress through the search.</param>
        public PackageSearcher(ICollection<string> packageIdsOrNames,
                               Action<Dictionary<string, SearchResult>> complete,
                               Action<float, string> progress = null) :
                base(packageIdsOrNames, progress){
            this.complete = complete;
            SearchNext();
        }

        /// <summary>
        /// Perform the next search operation.
        /// </summary>
        private void SearchNext() {
            var packageIdOrName = NextItem();
            if (String.IsNullOrEmpty(packageIdOrName)) {
                var completion = complete;
                complete = null;
                completion(found);
                return;
            }

            request = Client.Search(packageIdOrName);
            RunOnMainThread.PollOnUpdateUntilComplete(() => {
                    if (!request.IsComplete) return false;
                    if (complete == null) return true;
                    var packages = new List<PackageInfo>();
                    var result = request.Result;
                    if (request.Result != null) packages.AddRange(result);
                    found[packageIdOrName] = new SearchResult() {
                        Packages = packages,
                        Error = request.Error ?? new Error(null)
                    };
                    RunOnMainThread.Run(() => { SearchNext(); });
                    return true;
                });
        }
    }

    /// <summary>
    /// Logger for this class.
    /// </summary>
    public static Logger Logger = PackageManagerResolver.logger;

    /// <summary>
    /// Job queue for package managers jobs.
    /// </summary>
    /// <remarks>
    /// PackageManager.Client operations are not thread-safe, each operation needs to be
    /// executed sequentially so this class simplifies the process of scheduling operations on
    /// the main thread.
    /// </remarks>
    private static RunOnMainThread.JobQueue jobQueue = new RunOnMainThread.JobQueue();

    /// <summary>
    /// Determine Whether the package manager is available.
    /// </summary>
    public static bool Available { get { return Client.Available; } }

    /// <summary>
    /// Add a package to the project.
    /// </summary>
    /// <param name="packageIdOrName">ID or name of the package to add.</param>
    /// <param name="complete">Called when the operation is complete.</param>
    public static void AddPackage(string packageIdOrName, Action<InstallResult> complete) {
        if (!Available) {
            complete(new InstallResult());
            return;
        }
        jobQueue.Schedule(() => {
                new PackageInstaller(new [] { packageIdOrName },
                                     (result) => {
                                         jobQueue.Complete();
                                         complete(result[packageIdOrName]);
                                     },
                                     progress: null);
            });
    }

    /// <summary>
    /// Add packages to the project.
    /// </summary>
    /// <param name="packageIdsOrNames">IDs or names of the packages to add.</param>
    /// <param name="complete">Called when the operation is complete.</param>
    /// <param name="progress">Reports progress through the installation.</param>
    public static void AddPackages(ICollection<string> packageIdsOrNames,
                                   Action<Dictionary<string, InstallResult>> complete,
                                   Action<float, string> progress = null) {
        if (!Client.Available) {
            if (progress != null) progress(1.0f, "");
            complete(new Dictionary<string, InstallResult>());
            return;
        }
        jobQueue.Schedule(() => {
                new PackageInstaller(packageIdsOrNames,
                                     (result) => {
                                         jobQueue.Complete();
                                         complete(result);
                                     },
                                     progress: progress);
            });
    }

    /// <summary>
    /// Remove a package from the project.
    /// </summary>
    /// <param name="packageIdOrName">ID or name of the package to add.</param>
    /// <param name="complete">Called when the operation is complete.</param>
    public static void RemovePackage(string packageIdOrName, Action<RemoveResult> complete) {
        if (!Available) {
            complete(new RemoveResult());
            return;
        }
        jobQueue.Schedule(() => {
                var result = Client.Remove(packageIdOrName);
                RunOnMainThread.PollOnUpdateUntilComplete(() => {
                        if (!result.IsComplete) return false;
                        jobQueue.Complete();
                        complete(new RemoveResult() {
                                PackageId = result.Result ?? packageIdOrName,
                                Error = result.Error ?? new Error(null)
                            });
                        return true;
                    });
            });
    }

    /// <summary>
    /// List all packages that the project current depends upon.
    /// </summary>
    /// <param name="complete">Action that is called with the list of packages in the
    /// project.</param>
    public static void ListInstalledPackages(Action<SearchResult> complete) {
        if (!Available) {
            complete(new SearchResult());
            return;
        }
        jobQueue.Schedule(() => {
                var request = Client.List();
                RunOnMainThread.PollOnUpdateUntilComplete(() => {
                        if (!request.IsComplete) return false;
                        jobQueue.Complete();
                        complete(new SearchResult() {
                                Packages = request.Result,
                                Error = request.Error ?? new Error(null)
                            });
                        return true;
                    });
            });
    }

    /// <summary>
    /// Search for all packages available for installation in the package manager.
    /// </summary>
    /// <param name="complete">Action that is called with the list of packages available for
    /// installation.</param>
    public static void SearchAvailablePackages(Action<SearchResult> complete) {
        jobQueue.Schedule(() => {
                var request = Client.SearchAll();
                if (request == null) {
                    jobQueue.Complete();
                    complete(new SearchResult());
                    return;
                }
                RunOnMainThread.PollOnUpdateUntilComplete(() => {
                        if (!request.IsComplete) return false;
                        jobQueue.Complete();
                        complete(new SearchResult() {
                                Packages = request.Result,
                                Error = request.Error ?? new Error(null)
                            });
                        return true;
                    });
            });
    }

    /// <summary>
    /// Search for a set of packages in the package manager.
    /// </summary>
    /// <param name="packageIdsOrNames">Packages to search for.</param>
    /// <param name="complete">Action that is called with a collection of available packages that
    /// is a result of each search string.</param>
    /// <param name="progress">Reports progress through the search.</param>
    public static void SearchAvailablePackages(
            ICollection<string> packageIdsOrNames,
            Action<Dictionary<string, SearchResult>> complete,
            Action<float, string> progress = null) {
        if (!Available) {
            if (progress != null) progress(1.0f, "");
            complete(new Dictionary<string, SearchResult>());
            return;
        }
        jobQueue.Schedule(() => {
                new PackageSearcher(packageIdsOrNames,
                                    (result) => {
                                        jobQueue.Complete();
                                        complete(result);
                                    },
                                    progress: progress);
            });
    }
}

}

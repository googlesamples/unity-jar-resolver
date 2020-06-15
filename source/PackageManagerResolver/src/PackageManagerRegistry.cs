// <copyright file="PackageManagerRegistry.cs" company="Google Inc.">
// Copyright (C) 2020 Google Inc. All Rights Reserved.
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

    /// <summary>
    /// UPM/NPM package registry.
    /// </summary>
    internal class PackageManagerRegistry {

        /// <summary>
        /// Construct an empty registry.
        /// </summary>
        public PackageManagerRegistry() {
            Scopes = new List<string>();
        }

        /// <summary>
        /// Registry name.
        /// </summary>
        public string Name;

        /// <summary>
        /// Registry URL.
        /// </summary>
        public string Url;

        /// <summary>
        /// Scopes for this registry.
        /// See https://docs.unity3d.com/Manual/upm-scoped.html and
        /// https://docs.npmjs.com/using-npm/scope.html
        /// </summary>
        public List<string> Scopes;

        /// <summary>
        /// Terms of service for the registry.
        /// </summary>
        public string TermsOfService;

        /// <summary>
        /// Privacy policy for the registry.
        /// </summary>
        public string PrivacyPolicy;

        /// <summary>
        /// Tag that indicates where this was created.
        /// </summary>
        /// <remarks>
        /// This can be used to display which file this was read from or where in code this
        /// registry was created.
        /// </remarks>
        public string CreatedBy = System.Environment.StackTrace;

        /// <summary>
        /// Arbitrary custom runtime data.
        /// </summary>
        /// <remarks>
        /// For example, this can be used associate this instance with the Dictionary representation
        /// parsed from a JSON file. Storing the parsed JSON Dictionary it's possible to easily
        /// remove object from the JSON document without requiring a separate data structure
        /// (e.g a map) to associate the two data representations.
        /// </remarks>
        public object CustomData;

        /// <summary>
        /// Convert to a human readable string excluding the TermsOfService and CreatedBy fields.
        /// </summary>
        /// <returns>String representation of this instance.</returns>
        public override string ToString() {
            return String.Format("name: {0}, url: {1}, scopes: {2}",
                                 Name, Url,
                                 Scopes != null ?
                                     String.Format("[{0}]", String.Join(", ", Scopes.ToArray())) :
                                     "[]");
        }

        /// <summary>
        /// Compare with this object.
        /// </summary>
        /// <param name="obj">Object to compare with.</param>
        /// <returns>true if both objects have the same contents excluding CreatedBy,
        /// false otherwise.</returns>
        public override bool Equals(System.Object obj) {
            var other = obj as PackageManagerRegistry;
            return other != null &&
                Name == other.Name &&
                Url == other.Url &&
                TermsOfService == other.TermsOfService &&
                PrivacyPolicy == other.PrivacyPolicy &&
                Scopes != null && other.Scopes != null &&
                (new HashSet<string>(Scopes)).SetEquals(other.Scopes) &&
                CustomData == other.CustomData;
        }

        /// <summary>
        /// Generate a hash of this object excluding CreatedBy.
        /// </summary>
        /// <returns>Hash of this object.</returns>
        public override int GetHashCode() {
            int hash = 0;
            if (!String.IsNullOrEmpty(Name)) hash ^= Name.GetHashCode();
            if (!String.IsNullOrEmpty(Url)) hash ^= Url.GetHashCode();
            if (!String.IsNullOrEmpty(TermsOfService)) hash ^= TermsOfService.GetHashCode();
            if (!String.IsNullOrEmpty(PrivacyPolicy)) hash ^= PrivacyPolicy.GetHashCode();
            if (Scopes != null) {
                foreach (var scope in Scopes) {
                    hash ^= scope.GetHashCode();
                }
            }
            if (CustomData != null) hash ^= CustomData.GetHashCode();
            return hash;
        }

        /// <summary>
        /// Convert a list of PackageManagerRegistry instances to a list of strings.
        /// </summary>
        /// <param name="registries">List of registries to convert to strings.</param>
        /// <returns>List of strings.</returns>
        public static List<string> ToStringList(
                IEnumerable<PackageManagerRegistry> registries) {
            var registryStrings = new List<string>();
            foreach (var registry in registries) {
                registryStrings.Add(registry.ToString());
            }
            return registryStrings;
        }

        /// <summary>
        /// Convert a list of PackageManagerRegistry instance to a newline separated string.
        /// </summary>
        /// <param name="registries">List of registries to convert to strings.</param>
        /// <returns>String representation of the list.</returns>
        public static string ToString(IEnumerable<PackageManagerRegistry> registries) {
            return String.Join("\n", ToStringList(registries).ToArray());
        }
    }
}

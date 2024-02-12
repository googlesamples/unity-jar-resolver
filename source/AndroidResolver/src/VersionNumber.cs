// <copyright file="VersionNumber.cs" company="Google Inc.">
// Copyright (C) 2019 Google Inc. All Rights Reserved.
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

    using UnityEditor;

    /// <summary>
    /// Get the version number of this plugin.
    /// </summary>
    public class AndroidResolverVersionNumber {

        /// <summary>
        /// Version number, patched by the build process.
        /// </summary>
        private const string VERSION_STRING = "1.2.179";

        /// <summary>
        /// Cached version structure.
        /// </summary>
        private static Version value = new Version(VERSION_STRING);

        /// <summary>
        /// Get the version number.
        /// </summary>
        public static Version Value { get { return value; } }
    }
}

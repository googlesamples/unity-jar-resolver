// <copyright file="Utilities.cs" company="Google Inc.">
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
namespace Google.PackageManager {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// A collection of useful utility methods.
    /// </summary>
    public static class Utility {
        public static void EnsureDirectory(string directoryPath) {
            (new DirectoryInfo(directoryPath)).Create();
        }

        /// <summary>
        /// Checks that the URI is valid in terms of what Unity supports.
        /// </summary>
        /// <returns><c>true</c>, if URI string is valid, <c>false</c>
        /// otherwise.</returns>
        /// <param name="uri">URI.</param>
        public static bool IsValidUriString(string uri) {
            Uri uriResult;
            return Uri.TryCreate(uri,UriKind.Absolute, out uriResult);
        }

        /// <summary>
        /// Returns an absolute Uri string with one segment removed from the end.
        ///
        /// For example:
        /// http://domain.com/segment1/segment2/segment3
        ///
        /// would be returned as
        ///
        /// http://domain.com/segment1/segment2/
        ///
        /// Also:
        ///
        /// http://domain.com/segment1/segment2/segment3
        /// and
        /// http://domain.com/segment1/segment2/segment3/
        ///
        /// would be treated the same regardless of the trailing slash.
        /// </summary>
        /// <returns>The absolute Uri minus the last segment.</returns>
        /// <param name="uri">URI to remove segment from</param>
        public static string GetURLMinusSegment(string uri) {
            Uri outUri;
            Uri.TryCreate(uri,UriKind.Absolute, out outUri);
            return outUri.AbsoluteUri.Remove(outUri.AbsoluteUri.Length -
                                             outUri.Segments.Last().Length);
        }
    }
}
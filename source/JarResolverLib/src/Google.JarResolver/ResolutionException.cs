// <copyright file="ResolutionException.cs" company="Google Inc.">
// Copyright (C) 2014 Google Inc. All Rights Reserved.
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

namespace Google.JarResolver
{
    using System;

    /// <summary>
    /// Resolution exception. This is a checked exception for resolution problems.
    /// (you can take the developer out of java, but not the java out of the developer).
    /// </summary>
    public class ResolutionException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Google.JarResolver.ResolutionException"/> class.
        /// </summary>
        /// <param name="msg">Message of the exception.</param>
        public ResolutionException(string msg)
            : base(msg)
        {
        }
    }
}

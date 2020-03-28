// <copyright file="TestCaseResult.cs" company="Google LLC">
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

using System;
using System.Collections.Generic;

namespace Google.IntegrationTester {

    /// <summary>
    /// Result of a test.
    /// </summary>
    public class TestCaseResult {

        /// <summary>
        /// Initialize the class.
        /// </summary>
        public TestCaseResult(TestCase testCase) {
            TestCaseName = testCase.Name;
            ErrorMessages = new List<string>();
            Skipped = false;
        }

        /// <summary>
        /// Name of the test case.  This does not need to be set by the test case.
        /// </summary>
        public string TestCaseName { get; set; }

        /// <summary>
        /// Error messages reported by a test case failure.
        /// </summary>
        public List<string> ErrorMessages { get; set; }

        /// <summary>
        /// Whether the test case was skipped.
        /// </summary>
        public bool Skipped { get; set; }

        /// <summary>
        /// Whether the test case succeeded.
        /// </summary>
        public bool Succeeded {
            get {
                return Skipped || ErrorMessages == null || ErrorMessages.Count == 0;
            }
        }

        /// <summary>
        /// Format the result as a string.
        /// </summary>
        /// <param name="includeFailureMessages">Include failure messages in the list.</param>
        public string FormatString(bool includeFailureMessages) {
            return String.Format("Test {0}: {1}{2}", TestCaseName,
                                 Skipped ? "SKIPPED" : Succeeded ? "PASSED" : "FAILED",
                                 includeFailureMessages && ErrorMessages != null &&
                                 ErrorMessages.Count > 0 ?
                                     "\n" + String.Join("\n", ErrorMessages.ToArray()) : "");
        }
    }
}

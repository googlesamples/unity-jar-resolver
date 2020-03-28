// <copyright file="TestCase.cs" company="Google LLC">
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

namespace Google.IntegrationTester {

    /// <summary>
    /// Test case class.
    ///
    /// This specifies a test case to execute.  Each test case has a name which is used to
    /// log the result of the test and the method to execute as part of the test case.
    /// </summary>
    public class TestCase {

        /// <summary>
        /// Test case delegate.
        /// </summary>
        /// <param name="testCase">Object executing this method.</param>
        /// <param name="testCaseComplete">Called when the test case is complete.</param>
        public delegate void MethodDelegate(TestCase testCase,
                                            Action<TestCaseResult> testCaseComplete);

        /// <summary>
        /// Name of the test case.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Delegate that runs the test case logic.
        /// </summary>
        public MethodDelegate Method { get; set; }
    }
}

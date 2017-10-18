// <copyright file="PlayServicesSupportTests.cs" company="Google Inc.">
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

namespace Google.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Google.JarResolver;
    using NUnit.Framework;

    /// <summary>
    /// Test data and methods to create it.
    /// </summary>
    internal static class TestData {
        // Whether to display verbose log messages.
        public const bool VERBOSE_LOGGING = false;

        // Path to test data, contains a mock SDK and maven repo.
        public const string PATH = "../../testData";

        // Stores expected data for Maven artifacts in TestData.PATH.
        public class PackageInfo {
            public string group;
            public string artifact;
            public string bestVersion;
        };

        // Maven artifacts available in TestData.PATH.
        public enum PackageId {
            Artifact,
            TransDep,
            SubDep,
        };

        // Properties of Maven artifacts in TestData.PATH.
        public static Dictionary<PackageId, PackageInfo> mavenArtifacts =
            new Dictionary<PackageId, PackageInfo> {
            {
                PackageId.Artifact,
                new PackageInfo {
                    group = "test",
                    artifact = "artifact",
                    bestVersion = "8.2.0-alpha"
                }
            },
            {
                PackageId.TransDep,
                new PackageInfo {
                    group = "test",
                    artifact = "transdep",
                    bestVersion = "1.0.0",
                }
            },
            {
                PackageId.SubDep,
                new PackageInfo {
                    group = "test",
                    artifact = "subdep",
                    bestVersion = "0.9",
                }
            }
        };

        /// <summary>
        /// Extension method of PackageId that returns the associated
        /// PackageInfo instance.
        /// </summary>
        internal static PackageInfo Info(this PackageId artifactId)
        {
            return mavenArtifacts[artifactId];
        }

        /// <summary>
        /// Extension method of PackageId that returns a version less key for the artifact
        /// in the form "group:artifact".
        /// </summary>
        internal static string VersionlessKey(this PackageId artifactId)
        {
            var info = artifactId.Info();
            return String.Format("{0}:{1}", info.group, info.artifact);
        }

        /// <summary>
        /// Create a PlayServicesSupport instance for testing.
        /// </summary>
        public static PlayServicesSupport CreateInstance(
            string instanceName = null, string sdkPath = null,
            string[] additionalRepositories = null, PlayServicesSupport.LogMessage logger = null)
        {
            var instance = PlayServicesSupport.CreateInstance(
                instanceName ?? "testInstance", sdkPath ?? PATH,
                additionalRepositories, Path.GetTempPath(), logger: logger ?? Console.WriteLine);
            PlayServicesSupport.verboseLogging = VERBOSE_LOGGING;
            return instance;
        }

        /// <summary>
        /// Extension method for PlayServicesSupport that calls DependOn with the specified
        /// artifact ID.
        /// </summary>
        internal static void DependOn(this PlayServicesSupport instance, PackageId artifactId,
                                      string versionSpecifier)
        {
            var info = artifactId.Info();
            instance.DependOn(info.group, info.artifact, versionSpecifier);
        }
    }

    /// <summary>
    /// Play services support tests.
    /// </summary>
    [TestFixture]
    public class PlayServicesSupportTests
    {
        /// <summary>
        /// Verify the logger delegate is called by the Log() method.
        /// </summary>
        [Test]
        public void TestLogger()
        {
            List<string> messageList = new List<string>();
            string logMessage = "this is a test";
            PlayServicesSupport.logger = (message, level) => messageList.Add(message);
            Assert.AreEqual(0, messageList.Count);
            PlayServicesSupport.Log(logMessage);
            Assert.AreEqual(1, messageList.Count);
            Assert.AreEqual(logMessage, messageList[0]);
        }
    }
}

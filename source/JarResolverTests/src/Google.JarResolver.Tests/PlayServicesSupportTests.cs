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
        /// Clear all PlayServicesSupport instances.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            PlayServicesSupport.ResetDependencies();
        }

        /// <summary>
        /// Simple, "happy path" tests.
        /// </summary>
        [Test]
        public void TestSimpleResolveDependencies()
        {
            PlayServicesSupport support = TestData.CreateInstance();

            Assert.True(Directory.Exists(support.SDK));

            support.DependOn(TestData.PackageId.Artifact, "LATEST");

            Dictionary<string, Dependency> deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);

            // Verify one single dependency is returned at the expected version.
            Assert.AreEqual(1, deps.Count);
            IEnumerator<Dependency> iter = deps.Values.GetEnumerator();
            iter.MoveNext();
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion,
                            iter.Current.BestVersion);
        }

        /// <summary>
        /// Tests adding another repo path.  This is simulated by giving
        /// the incorrect SDK path, and adding the correct repo path as
        /// an additional one.
        /// </summary>
        [Test]
        public void TestCustomRepoPath()
        {
            string[] repos = {Path.Combine(TestData.PATH, "extras/google/m2repository")};
            PlayServicesSupport support = TestData.CreateInstance(
                sdkPath: "..", additionalRepositories: repos);

            Assert.True(Directory.Exists(support.SDK));

            support.ClearDependencies();
            support.DependOn(TestData.PackageId.Artifact, "LATEST");

            Dictionary<string, Dependency> deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);

            // Verify one dependency is returned at the expected version.
            Assert.AreEqual(1, deps.Count);
            IEnumerator<Dependency> iter = deps.Values.GetEnumerator();
            iter.MoveNext();
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion,
                            iter.Current.BestVersion);
        }

        /// <summary>
        /// Tests resolving transitive dependencies.
        /// </summary>
        [Test]
        public void TestResolveDependencies()
        {
            PlayServicesSupport support = TestData.CreateInstance();

            Assert.True(Directory.Exists(support.SDK));

            support.DependOn(TestData.PackageId.Artifact, "LATEST");

            Dictionary<string, Dependency> deps =
                support.ResolveDependencies(false);
            Assert.NotNull(deps);

            // Verify one dependency is returned at the expected version.
            Assert.AreEqual(1, deps.Count);
            IEnumerator<Dependency> iter = deps.Values.GetEnumerator();
            iter.MoveNext();
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion,
                            iter.Current.BestVersion);

            // Check dependency with has transitive dependencies.
            support.DependOn(TestData.PackageId.TransDep, "1.0");

            deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);

            // One dependency should be present from the previous test and an additional two
            // for the transdep and subdep.
            Assert.AreEqual(3, deps.Count);
            Dependency d = deps[TestData.PackageId.Artifact.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion, d.BestVersion);
            d = deps[TestData.PackageId.TransDep.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.TransDep.Info().bestVersion, d.BestVersion);
            d = deps[TestData.PackageId.SubDep.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.SubDep.Info().bestVersion, d.BestVersion);

            // check constraining down to a later version - the LATEST
            // will make this fail.
            support.DependOn(TestData.PackageId.Artifact, "7.0.0");

            ResolutionException ex = null;
            try
            {
                deps = support.ResolveDependencies(false);
            }
            catch (ResolutionException e)
            {
                ex = e;
            }

                Assert.NotNull(ex);

            // Now add it as 7+ and LATEST and it will work.
            support.ClearDependencies();

            support.DependOn(TestData.PackageId.Artifact, "LATEST");
            support.DependOn(TestData.PackageId.Artifact, "7+");
            deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);
            d = deps[TestData.PackageId.Artifact.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion, d.BestVersion);

            // Test downversioning.
            support.ClearDependencies();

            support.DependOn(TestData.PackageId.Artifact, "1+");
            support.DependOn(TestData.PackageId.Artifact, "2+");
            support.DependOn(TestData.PackageId.Artifact, "7.0.0");

            deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);
            d = deps[TestData.PackageId.Artifact.VersionlessKey()];
            Assert.AreEqual("7.0.0", d.BestVersion);

            // test the transitive dep influencing a top level
            support.ClearDependencies();

            support.DependOn(TestData.PackageId.Artifact, "1+");
            support.DependOn(TestData.PackageId.SubDep, "0+");
            support.DependOn(TestData.PackageId.TransDep, "LATEST");

            deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);
            d = deps[TestData.PackageId.Artifact.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion, d.BestVersion);
            d = deps[TestData.PackageId.SubDep.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.SubDep.Info().bestVersion, d.BestVersion);
        }

        /// <summary>
        /// Tests the use latest version contraint.
        /// </summary>
        [Test]
        public void TestUseLatest()
        {
            PlayServicesSupport support = TestData.CreateInstance();

            Assert.True(Directory.Exists(support.SDK));

            support.DependOn(TestData.PackageId.Artifact, "1+");
            support.DependOn(TestData.PackageId.SubDep, "1.1.0");
            support.DependOn(TestData.PackageId.TransDep, "LATEST");

            Dictionary<string, Dependency> deps =
                support.ResolveDependencies(true);
            Assert.NotNull(deps);
            Dependency d = deps[TestData.PackageId.Artifact.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion, d.BestVersion);
            d = deps[TestData.PackageId.SubDep.VersionlessKey()];
            Assert.AreEqual("1.1.0", d.BestVersion);
        }

        /// <summary>
        /// Tests the multi client scenario where 2 clients have different dependencies.
        /// </summary>
        [Test]
        public void TestMultiClient()
        {
            PlayServicesSupport client1 = TestData.CreateInstance(instanceName: "client1");
            PlayServicesSupport client2 = TestData.CreateInstance(instanceName: "client2");

            client1.DependOn(TestData.PackageId.Artifact, "1+");
            client2.DependOn(TestData.PackageId.SubDep, "1.1.0");

            Dictionary<string, Dependency> deps =
                client1.ResolveDependencies(true);
            Assert.NotNull(deps);
            Dependency d = deps[TestData.PackageId.Artifact.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion, d.BestVersion);

            // client 1 needs to see client 2 deps
            d = deps[TestData.PackageId.SubDep.VersionlessKey()];
            Assert.AreEqual("1.1.0", d.BestVersion);

            // now check that client 2 sees them also
            deps =
                client2.ResolveDependencies(true);
            Assert.NotNull(deps);
            d = deps[TestData.PackageId.Artifact.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion, d.BestVersion);

            d = deps[TestData.PackageId.SubDep.VersionlessKey()];
            Assert.AreEqual("1.1.0", d.BestVersion);

            // Now clear client2's deps, and client1 should not see subdep
            client2.ClearDependencies();

            deps = client1.ResolveDependencies(true);
            Assert.NotNull(deps);
            d = deps[TestData.PackageId.Artifact.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion, d.BestVersion);

            Assert.False(deps.ContainsKey(TestData.PackageId.SubDep.VersionlessKey()));
        }

        /// <summary>
        /// Tests the latest resolution strategy when one dependency has
        /// a transitive dependency that is old, and the caller is requiring
        /// a newer version, then the dependencies are resolved with the
        /// useLatest flag == true.
        /// </summary>
        [Test]
        public void TestLatestResolution()
        {
            PlayServicesSupport client1 = TestData.CreateInstance();

            // TransDep needs SubDep 0.9.
            client1.DependOn(TestData.PackageId.TransDep, "1.0.0");

            // We'll set the top level dependency to require SubDep 1.0 or greater.
            client1.DependOn(TestData.PackageId.SubDep, "1.0+");

            Dictionary<string, Dependency> deps = null;

            // The following should fail since we need SubDep 0.9 and SubDep 1.1.0.
            ResolutionException ex = null;
            try
            {
                deps = client1.ResolveDependencies(false);
            }
            catch (ResolutionException e)
            {
                ex = e;
            }

            Assert.NotNull(ex, "Expected exception, but got none");

            // now try with useLatest == true, should have no exception
            ex = null;
            try
            {
                deps = client1.ResolveDependencies(true);
            }
            catch (ResolutionException e)
            {
                ex = e;
            }
            Assert.Null(ex, "unexpected exception");

            Assert.NotNull(deps);

            // Should have TransDep and SubDep.
            Assert.AreEqual(2, deps.Count,
                            String.Join(", ", new List<string>(deps.Keys).ToArray()));

            // Now check that that all the dependencies have the correct best version.
            Dependency d = deps[TestData.PackageId.TransDep.VersionlessKey()];
            Assert.NotNull(d, "could not find transdep");
            Assert.AreEqual(TestData.PackageId.TransDep.Info().bestVersion, d.BestVersion);

            d = deps[TestData.PackageId.SubDep.VersionlessKey()];
            Assert.NotNull(d, "could not find subdep");
            Assert.AreEqual("1.1.0", d.BestVersion);

            // Try without version wildcard.
            client1.ClearDependencies();

            // TransDep needs subdep 0.9.
            client1.DependOn(TestData.PackageId.TransDep, "1.0.0");

            // Configure top level dependency to require exactly subdep 1.1.0.
            client1.DependOn(TestData.PackageId.SubDep, "1.1.0");

            ex = null;
            try
            {
                deps = client1.ResolveDependencies(false);
            }
            catch (ResolutionException e)
            {
                ex = e;
            }

            Assert.NotNull(ex, "Expected exception, but got none");

            ex = null;
            try
            {
                deps = client1.ResolveDependencies(true);
            }
            catch (ResolutionException e)
            {
                ex = e;
            }
            Assert.Null(ex, "unexpected exception");

            Assert.NotNull(deps);

            // Should contain TransDep and SubDep.
            Assert.AreEqual(2, deps.Count);

            // now check that that all the dependencies have the correct
            // best version
            d = deps[TestData.PackageId.TransDep.VersionlessKey()];
            Assert.NotNull(d, "could not find transdep");
            Assert.AreEqual(TestData.PackageId.TransDep.Info().bestVersion, d.BestVersion);

            d = deps[TestData.PackageId.SubDep.VersionlessKey()];
            Assert.NotNull(d, "could not find subdep");
            Assert.AreEqual("1.1.0", d.BestVersion);
        }

        /// <summary>
        /// Tests the non active client scenario where the current client has
        /// no dependencies, but it resolves all the clients in the project.
        /// </summary>
        [Test]
        public void TestNonActiveClient()
        {
            PlayServicesSupport client1 = TestData.CreateInstance(instanceName: "client1");
            PlayServicesSupport client2 = TestData.CreateInstance(instanceName: "client2");

            client1.DependOn(TestData.PackageId.Artifact, "1+");
            client2.DependOn(TestData.PackageId.SubDep, "1.1.0");

            // now make a third client with no dependencies and make sure it
            // sees client1 & 2
            PlayServicesSupport client3 = TestData.CreateInstance(instanceName: "client3");

            // now check that client 2 sees them also
            Dictionary<string, Dependency> deps = client3.ResolveDependencies(true);
            Assert.NotNull(deps);
            Dependency d = deps[TestData.PackageId.Artifact.VersionlessKey()];
            Assert.AreEqual(TestData.PackageId.Artifact.Info().bestVersion, d.BestVersion);

            d = deps[TestData.PackageId.SubDep.VersionlessKey()];
            Assert.AreEqual("1.1.0", d.BestVersion);
        }

        /// <summary>
        /// Verify the logger delegate is called by the Log() method.
        /// </summary>
        [Test]
        public void TestLogger()
        {
            List<string> messageList = new List<string>();
            string logMessage = "this is a test";
            PlayServicesSupport.logger = (message) => messageList.Add(message);
            Assert.AreEqual(0, messageList.Count);
            PlayServicesSupport.Log(logMessage);
            Assert.AreEqual(1, messageList.Count);
            Assert.AreEqual(logMessage, messageList[0]);
        }
    }
}

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
    using System.Collections.Generic;
    using System.IO;
    using Google.JarResolver;
    using NUnit.Framework;

    /// <summary>
    /// Play services support tests.
    /// </summary>
    [TestFixture]
    public class PlayServicesSupportTests
    {
        /// <summary>
        /// Simple, "happy path" tests.
        /// </summary>
        [Test]
        public void TestSimpleResolveDependencies()
        {
            PlayServicesSupport support = PlayServicesSupport.CreateInstance(
                                              "testInstance",
                                              "../../testData",
                                              Path.GetTempPath());

            Assert.True(Directory.Exists(support.SDK));

            // happy path
            support.ResetDependencies();
            support.DependOn("test", "artifact", "LATEST");

            Dictionary<string, Dependency> deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);

            // should be only 1 and version 8.2.0-alpha
            Assert.True(deps.Count == 1);
            IEnumerator<Dependency> iter = deps.Values.GetEnumerator();
            iter.MoveNext();
            Assert.True(iter.Current.BestVersion == "8.2.0-alpha");
        }

        /// <summary>
        /// Tests adding another repo path.  This is simulated by giving
        /// the incorrect SDK path, and adding the correct repo path as
        /// an additional one.
        /// </summary>
        [Test]
        public void TestCustomRepoPath()
        {
            string[] repos = {"../../testData/extras/google/m2repository"};
            PlayServicesSupport support = PlayServicesSupport.CreateInstance(
                "testInstance",
                "..",
               repos,
                Path.GetTempPath());

            Assert.True(Directory.Exists(support.SDK));

            // happy path
            support.ResetDependencies();
            support.DependOn("test", "artifact", "LATEST");

            Dictionary<string, Dependency> deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);

            // should be only 1 and version 8.1
            Assert.True(deps.Count == 1);
            IEnumerator<Dependency> iter = deps.Values.GetEnumerator();
            iter.MoveNext();
            Assert.True(iter.Current.BestVersion == "8.2.0-alpha");
        }

        /// <summary>
        /// Tests resolving transitive dependencies.
        /// </summary>
        [Test]
        public void TestResolveDependencies()
        {
            PlayServicesSupport support = PlayServicesSupport.CreateInstance(
                "testInstance",
                "../../testData",
                Path.GetTempPath());

            Assert.True(Directory.Exists(support.SDK));

            support.ResetDependencies();

            // happy path
            support.DependOn("test", "artifact", "LATEST");

            Dictionary<string, Dependency> deps =
                support.ResolveDependencies(false);
            Assert.NotNull(deps);

            // should be only 1 and version 8.2.0-alpha
            Assert.True(deps.Count == 1);
            IEnumerator<Dependency> iter = deps.Values.GetEnumerator();
            iter.MoveNext();
            Assert.True(iter.Current.BestVersion == "8.2.0-alpha");

            // check dependency that has transitive dependencies
            support.DependOn("test", "transdep", "1.0");

            deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);

            // 1 is the previous test, then 2 for transdep and subdep.
            Assert.True(deps.Count == 3);
            Dependency d = deps["test:artifact"];
            Assert.True(d.BestVersion == "8.2.0-alpha");
            d = deps["test:transdep"];
            Assert.AreEqual(d.BestVersion, "1.0.0");
            d = deps["test:subdep"];
            Assert.True(d.BestVersion == "0.9");

            // check constraining down to a later version - the LATEST
            // will make this fail.
            support.DependOn("test", "artifact", "7.0.0");

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
            support.ResetDependencies();

            support.DependOn("test", "artifact", "LATEST");
            support.DependOn("test", "artifact", "7+");
            deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);
            d = deps["test:artifact"];
            Assert.True(d.BestVersion == "8.2.0-alpha");

            // Test downversioning.
            support.ResetDependencies();

            support.DependOn("test", "artifact", "1+");
            support.DependOn("test", "artifact", "2+");
            support.DependOn("test", "artifact", "7.0.0");

            deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);
            d = deps["test:artifact"];
            Assert.True(d.BestVersion == "7.0.0");

            // test the transitive dep influencing a top level
            support.ResetDependencies();

            support.DependOn("test", "artifact", "1+");
            support.DependOn("test", "subdep", "0+");
            support.DependOn("test", "transdep", "LATEST");

            deps = support.ResolveDependencies(false);
            Assert.NotNull(deps);
            d = deps["test:artifact"];
            Assert.True(d.BestVersion == "8.2.0-alpha");
            d = deps["test:subdep"];
            Assert.True(d.BestVersion == "0.9");
        }

        /// <summary>
        /// Tests the use latest version contraint.
        /// </summary>
        [Test]
        public void TestUseLatest()
        {
            PlayServicesSupport support = PlayServicesSupport.CreateInstance(
                "testInstance",
                "../../testData",
                Path.GetTempPath());

            Assert.True(Directory.Exists(support.SDK));

            support.DependOn("test", "artifact", "1+");
            support.DependOn("test", "subdep", "1.1.0");
            support.DependOn("test", "transdep", "LATEST");

            Dictionary<string, Dependency> deps =
                support.ResolveDependencies(true);
            Assert.NotNull(deps);
            Dependency d = deps["test:artifact"];
            Assert.True(d.BestVersion == "8.2.0-alpha");
            d = deps["test:subdep"];
            Assert.True(d.BestVersion == "1.1.0");
        }

        /// <summary>
        /// Tests the multi client scenario where 2 clients have different dependencies.
        /// </summary>
        [Test]
        public void TestMultiClient()
        {
            PlayServicesSupport client1 = PlayServicesSupport.CreateInstance(
                "client1",
                "../../testData",
                Path.GetTempPath());

            PlayServicesSupport client2 = PlayServicesSupport.CreateInstance(
                "client2",
                "../../testData",
                Path.GetTempPath());

            client1.ResetDependencies();
            client2.ResetDependencies();

            client1.DependOn("test", "artifact", "1+");
            client2.DependOn("test", "subdep", "1.1.0");

            Dictionary<string, Dependency> deps =
                client1.ResolveDependencies(true);
            Assert.NotNull(deps);
            Dependency d = deps["test:artifact"];
            Assert.True(d.BestVersion == "8.2.0-alpha");

            // client 1 needs to see client 2 deps
            d = deps["test:subdep"];
            Assert.True(d.BestVersion == "1.1.0");

            // now check that client 2 sees them also
            deps =
                client2.ResolveDependencies(true);
            Assert.NotNull(deps);
            d = deps["test:artifact"];
            Assert.True(d.BestVersion == "8.2.0-alpha");

            d = deps["test:subdep"];
            Assert.True(d.BestVersion == "1.1.0");

            // Now clear client2's deps, and client1 should not see subdep
            client2.ClearDependencies();

            deps = client1.ResolveDependencies(true);
            Assert.NotNull(deps);
            d = deps["test:artifact"];
            Assert.True(d.BestVersion == "8.2.0-alpha");

            Assert.False(deps.ContainsKey("test:subdep"));
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
            PlayServicesSupport client1 = PlayServicesSupport.CreateInstance(
                "client1",
                "../../testData",
                Path.GetTempPath());
            client1.ResetDependencies();

            //trans dep needs subdep 0.9
            client1.DependOn("test", "transdep", "1.0.0");

            // so top level require subdep 1.0 or greater
            client1.DependOn("test", "subdep", "1.0+");

            Dictionary<string, Dependency> deps = null;
            // this should fail since we need 0.9 and 1.1.0

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

            Assert.IsTrue(deps.Count == 2, "Expected 2 dependencies, got " + deps.Count);

            // now check that that all the dependencies have the correct
            // best version
            Dependency d = deps["test:transdep"];
            Assert.NotNull(d, "could not find transdep");
            Assert.IsTrue(d.BestVersion == "1.0.0", "Expected version 1.0.0, got " + d.BestVersion);

            d = deps["test:subdep"];
            Assert.NotNull(d, "could not find subdep");
            Assert.IsTrue(d.BestVersion == "1.1.0", "Expected version 1.1.0, got " + d.BestVersion);

            // try without wildcard
            client1.ResetDependencies();

            //trans dep needs subdep 0.9
            client1.DependOn("test", "transdep", "1.0.0");

            // so top level requires exactly subdep 1.1.0.
            client1.DependOn("test", "subdep", "1.1.0");

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

            Assert.IsTrue(deps.Count == 2, "Expected 2 dependencies, got " + deps.Count);

            // now check that that all the dependencies have the correct
            // best version
            d = deps["test:transdep"];
            Assert.NotNull(d, "could not find transdep");
            Assert.IsTrue(d.BestVersion == "1.0.0", "Expected version 1.0.0, got " + d.BestVersion);

            d = deps["test:subdep"];
            Assert.NotNull(d, "could not find subdep");
            Assert.IsTrue(d.BestVersion == "1.1.0", "Expected version 1.1.0, got " + d.BestVersion);


        }

        /// <summary>
        /// Tests the non active client scenario where the current client has
        /// no dependencies, but it resolves all the clients in the project.
        /// </summary>
        [Test]
        public void TestNonActiveClient()
        {
            PlayServicesSupport client1 = PlayServicesSupport.CreateInstance(
                                              "client1",
                                              "../../testData",
                                              Path.GetTempPath());

            PlayServicesSupport client2 = PlayServicesSupport.CreateInstance(
                                              "client2",
                                              "../../testData",
                                              Path.GetTempPath());

            client1.ResetDependencies();
            client2.ResetDependencies();

            client1.DependOn("test", "artifact", "1+");
            client2.DependOn("test", "subdep", "1.1.0");

            // now make a third client with no dependencies and make sure it
            // sees client1 & 2
            PlayServicesSupport client3 = PlayServicesSupport.CreateInstance(
                "client3",
                "../../testData",
                Path.GetTempPath());

            // now check that client 2 sees them also
            Dictionary<string, Dependency> deps =
                client3.ResolveDependencies(true);
            Assert.NotNull(deps);
            Dependency d = deps["test:artifact"];
            Assert.True(d.BestVersion == "8.2.0-alpha");

            d = deps["test:subdep"];
            Assert.True(d.BestVersion == "1.1.0");
        }

        /// <summary>
        /// Verify the logger delegate is called by the Log() method.
        /// </summary>
        [Test]
        public void TestLogger()
        {
            List<string> messageList = new List<string>();
            string logMessage = "this is a test";
            PlayServicesSupport support = PlayServicesSupport.CreateInstance(
                "log_test", "../../testData", Path.GetTempPath(),
                logger: (message) => messageList.Add(message));
            Assert.AreEqual(messageList.Count, 0);
            support.Log(logMessage);
            Assert.AreEqual(messageList.Count, 1);
            Assert.AreEqual(messageList[0], logMessage);
        }

        /// <summary>
        /// Verify candidate resolution fails with an exception if the SDK path isn't
        /// set.
        /// </summary>
        [Test]
        public void TestFindCandidate()
        {
            string sdk = System.Environment.GetEnvironmentVariable("ANDROID_HOME");
            try
            {
                PlayServicesSupport support = PlayServicesSupport.CreateInstance(
                    "find_candidate", "../../testData", Path.GetTempPath());
                System.Environment.SetEnvironmentVariable("ANDROID_HOME", null);
                Assert.Throws(typeof(ResolutionException),
                              delegate { support.DependOn("some.group", "a-package", "1.2.3"); },
                              "Expected ResolutionException as SDK path is not set");
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("ANDROID_HOME", sdk);
            }
        }
    }
}

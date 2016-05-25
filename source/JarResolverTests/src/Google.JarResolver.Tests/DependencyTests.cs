// <copyright file="DependencyTests.cs" company="Google Inc.">
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

namespace Google.JarResolvers.Test
{
    using Google.JarResolver;
    using NUnit.Framework;

    /// <summary>
    /// Dependency tests.
    /// </summary>
    [TestFixture]
    public class DependencyTests
    {
        /// <summary>
        /// Tests the constructor.
        /// </summary>
        [Test]
        public void TestConstructor()
        {
            Dependency dep = new Dependency("test", "artifact1", "1.0");
            Assert.NotNull(dep);
        }

        /// <summary>
        /// Tests an unitialized dependency.
        /// </summary>
        [Test]
        public void TestUninitialized()
        {
            Dependency dep = new Dependency("test", "artifact1", "1.0");

            // No metadata loaded - so best version should be empty.
            string ver = dep.BestVersion;
            Assert.IsNullOrEmpty(ver);
            Assert.IsNullOrEmpty(dep.BestVersionPath);
            Assert.IsNullOrEmpty(dep.RepoPath);

            // the key is based on the spec, so it should be set.
            string key = dep.Key;
            Assert.IsNotNullOrEmpty(key);

            // package need to be the prefix
            Assert.True(key.StartsWith("test"));
 
            // Versionless key should be set. and be the prefix of the key
            Assert.IsNotNullOrEmpty(dep.VersionlessKey);
            Assert.True(key.StartsWith(dep.VersionlessKey));

            Assert.False(dep.HasPossibleVersions);
        }

        /// <summary>
        /// Tests the is newer method.
        /// </summary>
        [Test]
        public void TestIsNewer()
        {
            Dependency dep09 = new Dependency("test", "artifact1", "0.9");
            Dependency dep = new Dependency("test", "artifact1", "1.0");
            Dependency dep2 = new Dependency("test", "artifact1", "2.0");
            Dependency dep11 = new Dependency("test", "artifact1", "1.1");
            Dependency dep101 = new Dependency("test", "artifact1", "1.0.1");
            Dependency dep31 = new Dependency("test", "artifact1", "3.1");
            Dependency dep32alpha = new Dependency("test", "artifact1", "3.2-alpha");
            Dependency dep32beta = new Dependency("test", "artifact1", "3.2-beta");
            Assert.False(dep09.IsNewer(dep));
            Assert.False(dep.IsNewer(dep));
            Assert.True(dep2.IsNewer(dep));
            Assert.True(dep11.IsNewer(dep));
            Assert.True(dep101.IsNewer(dep));
            Assert.True(dep31.IsNewer(dep));
            Assert.True(dep32alpha.IsNewer(dep31));
            Assert.True(dep32alpha.IsNewer(dep));
            Assert.True(dep32beta.IsNewer(dep32alpha));
        }

        /// <summary>
        /// Tests the is acceptable version method
        /// </summary>
        [Test]
        public void TestIsAcceptableVersion()
        {
            // concrete version. only one should be acceptable.
            Dependency dep = new Dependency("test", "artifact1", "1.0");

            Assert.True(dep.IsAcceptableVersion("1.0"));

            // trailing 0 is acceptable.
            Assert.True(dep.IsAcceptableVersion("1.0.0"));

            // 2 trailing 0s is ok too.
            Assert.True(dep.IsAcceptableVersion("1"));

            // greater major, or minor is not acceptable.
            Assert.False(dep.IsAcceptableVersion("2.0"));
            Assert.False(dep.IsAcceptableVersion("1.1"));
            Assert.False(dep.IsAcceptableVersion("1.0.1"));

            // Check the LATEST meta-version
            Dependency latest = new Dependency("test", "artifact1", "LATEST");

            // Any version is acceptable until one has been added
            Assert.True(latest.IsAcceptableVersion("0.1"));
            Assert.True(latest.IsAcceptableVersion("1.0"));
            Assert.True(latest.IsAcceptableVersion("1.1"));

            // Check the + on the minor
            Dependency greaterminor = new Dependency("test", "artifact1", "1.0+");
            Dependency greaterDot = new Dependency("test", "artifact1", "1.+");
            Assert.True(greaterminor.IsAcceptableVersion("1.0"));
            Assert.True(greaterminor.IsAcceptableVersion("1.0.0"));
            Assert.True(greaterminor.IsAcceptableVersion("1.0.1"));
            Assert.True(greaterminor.IsAcceptableVersion("1.1"));
            Assert.True(greaterminor.IsAcceptableVersion("1.1.1"));
            Assert.True(greaterminor.IsAcceptableVersion("1.12"));
            Assert.False(greaterminor.IsAcceptableVersion("0.1"));
            Assert.False(greaterminor.IsAcceptableVersion("0.0.4"));
            Assert.False(greaterminor.IsAcceptableVersion("LATEST"));
            Assert.False(greaterminor.IsAcceptableVersion("2.0"));
            Assert.False(greaterminor.IsAcceptableVersion("2.1"));

            Assert.True(greaterDot.IsAcceptableVersion("1.0"));
            Assert.True(greaterDot.IsAcceptableVersion("1.0.0"));
            Assert.True(greaterDot.IsAcceptableVersion("1.0.1"));
            Assert.True(greaterDot.IsAcceptableVersion("1.1"));
            Assert.True(greaterDot.IsAcceptableVersion("1.1.1"));
            Assert.True(greaterDot.IsAcceptableVersion("1.12"));
            Assert.False(greaterDot.IsAcceptableVersion("2.0"));
            Assert.False(greaterDot.IsAcceptableVersion("2.1"));

            // Check the + on the minor
            Dependency majorGreater = new Dependency("test", "artifact1", "1+");
            Assert.True(majorGreater.IsAcceptableVersion("1.0"));
            Assert.True(majorGreater.IsAcceptableVersion("1.0.0"));
            Assert.True(majorGreater.IsAcceptableVersion("1.0.1"));
            Assert.True(majorGreater.IsAcceptableVersion("1.1"));
            Assert.True(majorGreater.IsAcceptableVersion("1.1.1"));
            Assert.True(majorGreater.IsAcceptableVersion("2.0"));
            Assert.True(majorGreater.IsAcceptableVersion("2.1.0"));
        }

        /// <summary>
        /// Tests the possible versions management.
        /// </summary>
        [Test]
        public void TestPossibleVersions()
        {
            Dependency dep = new Dependency("test", "artifact1", "2.0");

            // adding multiple versions to a concrete version results in only that one.
           dep.AddVersion("0.1");

            Assert.False(dep.HasPossibleVersions);

            dep.AddVersion("1.0");
            dep.AddVersion("2.0.0");
            dep.AddVersion("3.0");

            Assert.True(dep.HasPossibleVersions);

            Assert.True(dep.BestVersion == "2.0.0");

            dep.RemovePossibleVersion(dep.BestVersion);

            Assert.False(dep.HasPossibleVersions);

            dep = new Dependency("test", "artifact1", "2.0+");

            // check plus
            dep.AddVersion("1.0");
            dep.AddVersion("2.0.0");
            dep.AddVersion("2.0.1");
            dep.AddVersion("2.1");
            dep.AddVersion("3.0");

            Assert.True(dep.HasPossibleVersions);
            Assert.True(dep.BestVersion == "2.1");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.True(dep.BestVersion == "2.0.1");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.True(dep.BestVersion == "2.0.0");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.IsNullOrEmpty(dep.BestVersion);
            Assert.False(dep.HasPossibleVersions);

            dep = new Dependency("test", "artifact1", "2.0+");

            // check plus
            dep.AddVersion("3.0");
            dep.AddVersion("2.2.0");
            dep.AddVersion("2.0.1");
            dep.AddVersion("2.1");
            dep.AddVersion("1.0");

            Assert.True(dep.HasPossibleVersions);
            Assert.True(dep.BestVersion == "2.2.0");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.True(dep.BestVersion == "2.1");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.True(dep.BestVersion == "2.0.1");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.IsNullOrEmpty(dep.BestVersion);
            Assert.False(dep.HasPossibleVersions);
        }

        /// <summary>
        /// Tests the refine version range method.
        /// </summary>
        [Test]
        public void TestRefineVersionRange()
        {
            Dependency dep = new Dependency("test", "artifact1", "2.0+");

            dep.AddVersion("3.0");
            dep.AddVersion("2.2.0");
            dep.AddVersion("2.0.1");
            dep.AddVersion("2.1");
            dep.AddVersion("1.0");

            // refinement with the same object should have no effect.
            Assert.True(dep.RefineVersionRange(dep));
            Assert.True(dep.HasPossibleVersions);
            Assert.True(dep.BestVersion == "2.2.0");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.True(dep.BestVersion == "2.1");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.True(dep.BestVersion == "2.0.1");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.IsNullOrEmpty(dep.BestVersion);
            Assert.False(dep.HasPossibleVersions);

            // refinement with a concrete version not compatible should fail.
            dep = new Dependency("test", "artifact1", "2.0+");
           
            dep.AddVersion("3.0");
            dep.AddVersion("2.2.0");
            dep.AddVersion("2.0.1");
            dep.AddVersion("2.1");
            dep.AddVersion("1.0");

            Dependency dep1 = new Dependency("test", "artifact1", "3.0");

            Assert.False(dep.RefineVersionRange(dep1));
            Assert.False(dep.HasPossibleVersions);

            // concrete included
            dep = new Dependency("test", "artifact1", "2.0+");

            dep.AddVersion("3.0");
            dep.AddVersion("2.2.0");
            dep.AddVersion("2.0.1");
            dep.AddVersion("2.1");
            dep.AddVersion("1.0");

            Dependency dep2 = new Dependency("test", "artifact1", "2.1.0");

            Assert.True(dep.RefineVersionRange(dep2));
            Assert.True(dep.BestVersion == "2.1");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.False(dep.HasPossibleVersions);

            // check overlapping ranges
            dep = new Dependency("test", "artifact1", "2.0+");

            dep.AddVersion("3.0");
            dep.AddVersion("2.2.0");
            dep.AddVersion("2.0.1");
            dep.AddVersion("2.1");
            dep.AddVersion("1.0");

            Dependency dep1plus = new Dependency("test", "artifact1", "2.1+");

            Assert.True(dep.RefineVersionRange(dep1plus));
            Assert.True(dep.BestVersion == "2.2.0");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.True(dep.BestVersion == "2.1");
            dep.RemovePossibleVersion(dep.BestVersion);
            Assert.False(dep.HasPossibleVersions);
        }
    }
}
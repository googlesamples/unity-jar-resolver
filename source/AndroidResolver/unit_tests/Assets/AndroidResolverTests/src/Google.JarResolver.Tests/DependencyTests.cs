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

namespace Google.JarResolvers.Test {
    using Google.JarResolver;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// Dependency tests.
    /// </summary>
    [TestFixture]
    public class DependencyTests {
        /// <summary>
        /// Tests an initialized dependency.
        /// </summary>
        [Test]
        public void TestConstructor() {
            Dependency dep = new Dependency("test", "artifact1", "1.0",
                                            packageIds: new [] { "tools" },
                                            repositories: new [] { "a/repo/path" },
                                            createdBy: "someone");
            Assert.That(dep.CreatedBy, Is.EqualTo("someone"));
            Assert.That(dep.Group, Is.EqualTo("test"));
            Assert.That(dep.Artifact, Is.EqualTo("artifact1"));
            Assert.That(dep.PackageIds, Is.EqualTo(new [] { "tools" }));
            Assert.That(dep.Repositories, Is.EqualTo(new [] { "a/repo/path" }));
            Assert.That(dep.VersionlessKey, Is.EqualTo("test:artifact1"));
            Assert.That(dep.Key, Is.EqualTo("test:artifact1:1.0"));
            Assert.That(dep.ToString(), Is.EqualTo("test:artifact1:1.0"));
        }

        /// <summary>
        /// Test version string comparison by sorting a list of versions.
        /// </summary>
        [Test]
        public void TestSortVersionStrings() {
            List<string> sorted = new List<string> {
                "3.2.1",
                "1.1",
                "1.0.0+",
                "1.2.0",
                "1.3.a+",
                "10",
                "1.1.0",
                "3.2.2",
                "1.3.b",
            };
            sorted.Sort(Dependency.versionComparer);
            Assert.That(sorted[0], Is.EqualTo("10"));
            Assert.That(sorted[1], Is.EqualTo("3.2.2"));
            Assert.That(sorted[2], Is.EqualTo("3.2.1"));
            Assert.That(sorted[3], Is.EqualTo("1.3.b"));
            Assert.That(sorted[4], Is.EqualTo("1.3.a+"));
            Assert.That(sorted[5], Is.EqualTo("1.2.0"));
            Assert.That(sorted[6], Is.EqualTo("1.1.0"));
            Assert.That(sorted[7], Is.EqualTo("1.1"));
            Assert.That(sorted[8], Is.EqualTo("1.0.0+"));
        }
    }
}

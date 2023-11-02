// <copyright file="PackageManagerRegistryTest.cs" company="Google LLC">
// Copyright (C) 2020 Google LLC All Rights Reserved.
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

namespace Google.PackageManagerResolver.Tests {
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Google;

    /// <summary>
    /// Tests the PackageManagerRegistry class.
    /// </summary>
    [TestFixture]
    public class PackageManagerRegistryTest {

        /// <summary>
        /// Construct a PackageManagerRegistry and use all accessors.
        /// </summary>
        [Test]
        public void TestConstruct() {
            var registry = new PackageManagerRegistry() {
                Name = "Reg",
                Url = "http://unity.reg.org",
                Scopes = new List<string> { "org.foo.bar" },
                TermsOfService = "http://unity.reg.org/terms",
                PrivacyPolicy = "http://unity.reg.org/privacy",
                CreatedBy = "foo.xml:123",
                CustomData = "hello world"
            };
            Assert.That(registry.Name, Is.EqualTo("Reg"));
            Assert.That(registry.Url, Is.EqualTo("http://unity.reg.org"));
            CollectionAssert.AreEquivalent(registry.Scopes, new List<string> { "org.foo.bar" });
            Assert.That(registry.TermsOfService, Is.EqualTo("http://unity.reg.org/terms"));
            Assert.That(registry.PrivacyPolicy, Is.EqualTo("http://unity.reg.org/privacy"));
            Assert.That(registry.CreatedBy, Is.EqualTo("foo.xml:123"));
            Assert.That(registry.CustomData, Is.EqualTo("hello world"));
        }

        /// <summary>
        /// Test object comparison and hash code.
        /// </summary>
        [Test]
        public void TestCompareAndGetHashCode() {
            var reg = new PackageManagerRegistry();
            Assert.That(reg.GetHashCode(), Is.EqualTo(0));

            var reg1 = new PackageManagerRegistry() {
                Name = "Reg",
                Url = "http://reg1.org",
                Scopes = new List<string> { "org.foo.bar" },
                TermsOfService = "http://reg1.org/terms",
                PrivacyPolicy = "http://reg1.org/privacy",
                CreatedBy = "foo.xml:123",
                CustomData = "hello world"
            };
            var reg2 = new PackageManagerRegistry() {
                Name = "Reg",
                Url = "http://reg1.org",
                Scopes = new List<string> { "org.foo.bar" },
                TermsOfService = "http://reg1.org/terms",
                PrivacyPolicy = "http://reg1.org/privacy",
                CreatedBy = "foo.xml:123",
                CustomData = "hello world"
            };
            Assert.That(reg1.Equals(reg2), Is.EqualTo(true));
            Assert.That(reg1.GetHashCode(), Is.EqualTo(reg2.GetHashCode()));

            reg2.CreatedBy = "foo2.xml:111";
            Assert.That(reg1.Equals(reg2), Is.EqualTo(true));
            Assert.That(reg1.GetHashCode(), Is.EqualTo(reg2.GetHashCode()));

            reg2.Name = "reg2";
            Assert.That(reg1.Equals(reg2), Is.EqualTo(false));
            Assert.That(reg1.GetHashCode(), Is.Not.EqualTo(reg2.GetHashCode()));

            reg2.Name = reg1.Name;
            reg2.Url = "http://reg2.org";
            Assert.That(reg1.Equals(reg2), Is.EqualTo(false));
            Assert.That(reg1.GetHashCode(), Is.Not.EqualTo(reg2.GetHashCode()));

            reg2.Url = reg1.Url;
            reg2.TermsOfService = "http://reg2.org/terms";
            Assert.That(reg1.Equals(reg2), Is.EqualTo(false));
            Assert.That(reg1.GetHashCode(), Is.Not.EqualTo(reg2.GetHashCode()));

            reg2.TermsOfService = reg1.TermsOfService;
            reg2.PrivacyPolicy = "http://reg2.org/privacy";
            Assert.That(reg1.Equals(reg2), Is.EqualTo(false));
            Assert.That(reg1.GetHashCode(), Is.Not.EqualTo(reg2.GetHashCode()));

            reg2.PrivacyPolicy = reg1.PrivacyPolicy;
            reg2.Scopes = null;
            Assert.That(reg1.Equals(reg2), Is.EqualTo(false));
            Assert.That(reg1.GetHashCode(), Is.Not.EqualTo(reg2.GetHashCode()));

            reg2.Scopes = new List<string> { "org.reg2" };
            Assert.That(reg1.Equals(reg2), Is.EqualTo(false));
            Assert.That(reg1.GetHashCode(), Is.Not.EqualTo(reg2.GetHashCode()));

            reg2.Scopes = reg1.Scopes;
            reg2.CustomData = "hello from reg2";
            Assert.That(reg1.Equals(reg2), Is.EqualTo(false));
            Assert.That(reg1.GetHashCode(), Is.Not.EqualTo(reg2.GetHashCode()));
        }

        /// <summary>
        /// Convert a PackageManagerRegistry to a string representation.
        /// </summary>
        [Test]
        public void TestToString() {
            var registry = new PackageManagerRegistry() {
                Name = "Reg",
                Url = "http://unity.reg.org",
                Scopes = new List<string> { "org.foo.bar", "org.foo.baz"},
                TermsOfService = "http://unity.reg.org/terms",
                CreatedBy = "foo.xml:123",
                CustomData = "hello world"
            };
            Assert.That(registry.ToString(),
                        Is.EqualTo("name: Reg, url: http://unity.reg.org, " +
                                   "scopes: [org.foo.bar, org.foo.baz]"));
        }

        /// <summary>
        /// Convert a list of PackageManagerRegistry instances to a list of strings.
        /// </summary>
        [Test]
        public void TestToStringList() {
            var registries = new PackageManagerRegistry[] {
                new PackageManagerRegistry() {
                    Name = "foo",
                    Url = "http://foo.com",
                    Scopes = new List<string>() { "foo.bar" },
                },
                new PackageManagerRegistry() {
                    Name = "bar",
                    Url = "http://bar.com"
                }
            };
            Assert.That(PackageManagerRegistry.ToStringList(registries),
                        Is.EqualTo(new List<string>() {
                                       "name: foo, url: http://foo.com, scopes: [foo.bar]",
                                       "name: bar, url: http://bar.com, scopes: []"
                                    }));
        }

        /// <summary>
        /// Convert a list of PackageManagerRegistry instances to a string.
        /// </summary>
        [Test]
        public void TestListToString() {
            var registries = new PackageManagerRegistry[] {
                new PackageManagerRegistry() {
                    Name = "foo",
                    Url = "http://foo.com",
                    Scopes = new List<string>() { "foo.bar" },
                },
                new PackageManagerRegistry() {
                    Name = "bar",
                    Url = "http://bar.com"
                }
            };
            Assert.That(PackageManagerRegistry.ToString(registries),
                        Is.EqualTo("name: foo, url: http://foo.com, scopes: [foo.bar]\n" +
                                   "name: bar, url: http://bar.com, scopes: []"));
        }
    }
}

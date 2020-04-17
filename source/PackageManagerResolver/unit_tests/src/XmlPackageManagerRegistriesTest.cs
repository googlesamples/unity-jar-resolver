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
    public class XmlPackageManagerRegistriesTest {

        /// <summary>
        /// Name of the test configuration file.
        /// </summary>
        const string TEST_CONFIGURATION_FILENAME = "TestRegistries.xml";

        /// <summary>
        /// Object under test.
        /// </summary>
        private XmlPackageManagerRegistries registries;

        /// <summary>
        /// Logger for this test.
        /// </summary>
        private Logger logger = new Logger() {
            Target = LogTarget.Console,
            Level = LogLevel.Debug
        };

        /// <summary>
        /// Write to the test registries file.
        /// </summary>
        /// <param name="configuration">String to write to the file.</param>
        private void WriteRegistries(string configuration) {
            if (File.Exists(TEST_CONFIGURATION_FILENAME)) File.Delete(TEST_CONFIGURATION_FILENAME);
            File.WriteAllText(TEST_CONFIGURATION_FILENAME, configuration);
        }

        /// <summary>
        /// Setup for the test
        /// </summary>
        [SetUp]
        public void Setup() {
            registries = new XmlPackageManagerRegistries();
        }

        /// <summary>
        /// Make sure that a constructed object is empty.
        /// </summary>
        [Test]
        public void TestRegistriesEmpty() {
            Assert.That(registries.Registries.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Add some items to the registries, clear and validate it's empty.
        /// </summary>
        [Test]
        public void TestClear() {
            registries.Registries["http://foo.bar.com"] = new PackageManagerRegistry() {
                Name = "foobar",
                Url = "http://foo.bar.com",
                Scopes = new List<string>() { "com.bar" }
            };
            Assert.That(registries.Registries.Count, Is.EqualTo(1));
            registries.Clear();
            Assert.That(registries.Registries.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Determine whether a filename is a container of UPM registries.
        /// </summary>
        [Test]
        public void TestIsRegistriesFile() {
            Assert.That(XmlPackageManagerRegistries.IsRegistriesFile(
                            "Assets/SomeRegistries.xml"),
                        Is.EqualTo(false));
            Assert.That(XmlPackageManagerRegistries.IsRegistriesFile(
                            "Assets/MyPlugin/SomeRegistries.xml"),
                        Is.EqualTo(false));
            Assert.That(XmlPackageManagerRegistries.IsRegistriesFile(
                            "Assets/Editor/SomeRegistries.txt"),
                        Is.EqualTo(false));
            Assert.That(XmlPackageManagerRegistries.IsRegistriesFile(
                            "Assets/Editor/SomeRegistries.xml"),
                        Is.EqualTo(true));
            Assert.That(XmlPackageManagerRegistries.IsRegistriesFile(
                            "Assets\\Editor\\SomeRegistries.xml"),
                        Is.EqualTo(true));
            Assert.That(XmlPackageManagerRegistries.IsRegistriesFile(
                            "Assets/MyPlugin/Editor/SomeRegistries.xml"),
                        Is.EqualTo(true));
            Assert.That(XmlPackageManagerRegistries.IsRegistriesFile(
                            "Assets\\MyPlugin\\Editor\\SomeRegistries.xml"),
                        Is.EqualTo(true));
        }

        /// <summary>
        /// Test Read() with a valid XML file.
        /// </summary>
        [Test]
        public void TestRead() {
            WriteRegistries("<registries>\n" +
                            "  <registry name=\"Reg1\"\n" +
                            "            url=\"https://reg1.com\"\n" +
                            "            termsOfService=\"https://reg1.com/terms\"\n" +
                            "            privacyPolicy=\"https://reg1.com/privacy\">\n" +
                            "    <scopes>\n" +
                            "      <scope>com.reg1</scope>\n" +
                            "    </scopes>\n" +
                            "  </registry>\n" +
                            "  <registry name=\"Reg2\"\n" +
                            "            url=\"https://reg2.com\">\n" +
                            "    <scopes>\n" +
                            "      <scope>com.reg2.foo</scope>\n" +
                            "      <scope>com.reg2.bar</scope>\n" +
                            "    </scopes>\n" +
                            "  </registry>\n" +
                            "</registries>\n");
            Assert.That(registries.Read(TEST_CONFIGURATION_FILENAME, logger), Is.EqualTo(true));
            Assert.That(registries.Registries.Count, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(registries.Registries.Keys,
                                           new [] { "https://reg1.com", "https://reg2.com"});
            var reg1 = registries.Registries["https://reg1.com"];
            Assert.That(reg1.Name, Is.EqualTo("Reg1"));
            Assert.That(reg1.Url, Is.EqualTo("https://reg1.com"));
            Assert.That(reg1.TermsOfService, Is.EqualTo("https://reg1.com/terms"));
            Assert.That(reg1.PrivacyPolicy, Is.EqualTo("https://reg1.com/privacy"));
            CollectionAssert.AreEquivalent(reg1.Scopes, new [] { "com.reg1" } );
            var reg2 = registries.Registries["https://reg2.com"];
            Assert.That(reg2.Name, Is.EqualTo("Reg2"));
            Assert.That(reg2.Url, Is.EqualTo("https://reg2.com"));
            Assert.That(reg2.TermsOfService, Is.EqualTo(""));
            CollectionAssert.AreEquivalent(reg2.Scopes, new [] { "com.reg2.foo", "com.reg2.bar" } );
        }

        /// <summary>
        /// Test Read() with a configuration that uses the same registry URL multiple times.
        /// </summary>
        [Test]
        public void TestReadDuplicateUrl() {
            WriteRegistries("<registries>\n" +
                            "  <registry name=\"Reg1\"\n" +
                            "            url=\"https://reg1.com\"\n" +
                            "            termsOfService=\"https://reg1.com/terms\"\n" +
                            "            privacyPolicy=\"https://reg1.com/privacy\">\n" +
                            "    <scopes>\n" +
                            "      <scope>com.reg1</scope>\n" +
                            "    </scopes>\n" +
                            "  </registry>\n" +
                            "  <registry name=\"Reg1\"\n" +
                            "            url=\"https://reg1.com\"\n" +
                            "            termsOfService=\"https://reg1.com/terms\"\n" +
                            "            privacyPolicy=\"https://reg1.com/privacy\">\n" +
                            "    <scopes>\n" +
                            "      <scope>com.reg1</scope>\n" +
                            "    </scopes>\n" +
                            "  </registry>\n" +
                            "  <registry name=\"Reg1 Other\"\n" +
                            "            url=\"https://reg1.com\">\n" +
                            "    <scopes>\n" +
                            "      <scope>com.reg1.foobar</scope>\n" +
                            "    </scopes>\n" +
                            "  </registry>\n" +
                            "</registries>\n");
            Assert.That(registries.Read(TEST_CONFIGURATION_FILENAME, logger), Is.EqualTo(true));
            Assert.That(registries.Registries.Count, Is.EqualTo(1));
            CollectionAssert.AreEquivalent(registries.Registries.Keys,
                                           new [] { "https://reg1.com" });
            var reg1 = registries.Registries["https://reg1.com"];
            Assert.That(reg1.Name, Is.EqualTo("Reg1"));
            Assert.That(reg1.Url, Is.EqualTo("https://reg1.com"));
            Assert.That(reg1.TermsOfService, Is.EqualTo("https://reg1.com/terms"));
            CollectionAssert.AreEquivalent(reg1.Scopes, new [] { "com.reg1" } );
        }

        /// <summary>
        /// Try reading a malformed configuration files.
        /// </summary>
        [Test]
        public void TestReadBrokenConfigs() {
            // Not XML.
            WriteRegistries("this is not xml");
            Assert.That(registries.Read(TEST_CONFIGURATION_FILENAME, logger), Is.EqualTo(false));

            // Invalid tag.
            WriteRegistries("<registries><nada></nada></registries>");
            Assert.That(registries.Read(TEST_CONFIGURATION_FILENAME, logger), Is.EqualTo(false));

            // Missing url attribute.
            WriteRegistries("<registries><registry name=\"foo\"></registry></registries>");
            Assert.That(registries.Read(TEST_CONFIGURATION_FILENAME, logger), Is.EqualTo(false));

            // Missing scopes block and scope entries.
            WriteRegistries("<registries>\n" +
                            "  <registry name=\"foo\"\n" +
                            "          url=\"http://foo.bar.com\">\n" +
                            "  </registry>\n" +
                            "</registries>");
            Assert.That(registries.Read(TEST_CONFIGURATION_FILENAME, logger), Is.EqualTo(false));

            // Missing scope entries.
            WriteRegistries("<registries>\n" +
                            "  <registry name=\"foo\"\n" +
                            "          url=\"http://foo.bar.com\">\n" +
                            "   <scopes></scopes>\n" +
                            "  </registry>\n" +
                            "</registries>");
            Assert.That(registries.Read(TEST_CONFIGURATION_FILENAME, logger), Is.EqualTo(false));
        }
    }
}

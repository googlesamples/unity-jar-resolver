// <copyright file="PackageManifestModifierTest.cs" company="Google LLC">
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
    /// Tests the PackageManifestModifier class.
    /// </summary>
    [TestFixture]
    public class PackageManifestModifierTest {

        /// <summary>
        /// Object under test.
        /// </summary>
        PackageManifestModifier modifier;

        /// <summary>
        /// Setup for the test
        /// </summary>
        [SetUp]
        public void Setup() {
            // Delete the temporary manifest if it exists.
            if (File.Exists(PackageManifestModifier.MANIFEST_FILE_PATH)) {
                File.Delete(PackageManifestModifier.MANIFEST_FILE_PATH);
            }

            // Create a modifier that uses a logs to the system console.
            modifier = new PackageManifestModifier();
            modifier.Logger.Target = LogTarget.Console;
            modifier.Logger.Level = LogLevel.Debug;
        }

        /// <summary>
        ///  Read a project manifest.
        /// </summary>
        private string ReadManifest() {
            return File.ReadAllText(PackageManifestModifier.MANIFEST_FILE_PATH);
        }

        /// <summary>
        /// Write a project manifest.
        /// </summary>
        /// <param name="manifest">JSON string to write to the manifest file.</param>>
        private void WriteManifest(string manifest) {
            var manifestDirectory = Path.GetDirectoryName(
                PackageManifestModifier.MANIFEST_FILE_PATH);
            if (!Directory.Exists(manifestDirectory)) Directory.CreateDirectory(manifestDirectory);
            File.WriteAllText(PackageManifestModifier.MANIFEST_FILE_PATH, manifest);
        }

        const string MANIFEST_SIMPLE =
            "{\n" +
            "  \"dependencies\": {\n" +
            "    \"com.bar.foo\": \"1.2.3\"\n" +
            "  },\n" +
            "  \"scopedRegistries\": [\n" +
            "    {\n" +
            "      \"name\": \"A UPM Registry\",\n" +
            "      \"url\": \"https://unity.foobar.com\",\n" +
            "      \"scopes\": [\n" +
            "        \"foobar.unity.voxels\"\n" +
            "      ]\n" +
            "    }\n" +
            "  ]\n" +
            "}";

        /// <summary>
        /// Test manifest with a few different registries.
        /// </summary>
        const string MANIFEST_MULTI_REGISTRIES =
            "{\n" +
            "  \"scopedRegistries\": [\n" +
            "    {\n" +
            "      \"name\": \"Reg1\",\n" +
            "      \"url\": \"https://reg1.com\",\n" +
            "      \"scopes\": [\n" +
            "        \"com.reg1.foo\",\n" +
            "        \"com.reg1.bar\"\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"name\": \"Reg1 Ext\",\n" +
            "      \"url\": \"https://reg1.com\",\n" +
            "      \"scopes\": [\n" +
            "        \"com.reg1.ext\"\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"name\": \"Reg2\",\n" +
            "      \"url\": \"https://unity.reg2.com\",\n" +
            "      \"scopes\": [\n" +
            "        \"com.reg2.bish\"\n" +
            "      ]\n" +
            "    }\n" +
            "  ]\n" +
            "}";

        /// <summary>
        /// Read a valid manifest.
        /// </summary>
        [Test]
        public void TestReadManifestValid() {
            WriteManifest(MANIFEST_SIMPLE);
            Assert.That(modifier.GetManifestJson(), Is.Empty);
            Assert.That(modifier.ReadManifest(), Is.EqualTo(true));
            Assert.That(modifier.GetManifestJson(), Is.EqualTo(MANIFEST_SIMPLE));

            var expected = new Dictionary<string, object>() {
                {
                    "dependencies",
                    new Dictionary<string, object>() {
                        {"com.bar.foo", "1.2.3"}
                    }
                },
                {
                    "scopedRegistries",
                    new List<Dictionary<string, object>>() {
                        new Dictionary<string, object>() {
                            { "name", "A UPM Registry" },
                            { "url", "https://unity.foobar.com" },
                            {
                                "scopes",
                                new List<string>() { "foobar.unity.voxels" }
                            }
                        }
                    }
                }
            };
            CollectionAssert.AreEquivalent(modifier.manifestDict, expected);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        [Test]
        public void TestCopyConstructor() {
            WriteManifest(MANIFEST_SIMPLE);
            Assert.That(modifier.ReadManifest(), Is.EqualTo(true));

            PackageManifestModifier copy = new PackageManifestModifier(modifier);

            CollectionAssert.AreEquivalent(copy.manifestDict, modifier.manifestDict);
            Assert.That(copy.GetManifestJson(), Is.EqualTo(MANIFEST_SIMPLE));
            Assert.That(copy.GetManifestJson(), Is.EqualTo(modifier.GetManifestJson()));
        }

        /// <summary>
        /// Try reading a manifest that doesn't exist.
        /// </summary>
        [Test]
        public void TestReadManifestMissing() {
            Assert.That(modifier.ReadManifest(), Is.EqualTo(false));
        }

        /// <summary>
        /// Try reading a manifest with invalid JSON.
        /// </summary>
        [Test]
        public void TestReadManifestInvalid() {
            WriteManifest("This is not valid JSON");
            Assert.That(modifier.ReadManifest(), Is.EqualTo(false));
        }

        /// <summary>
        /// Try to retrieve registries from a modifier that hasn't read a manifest.
        /// </summary>
        [Test]
        public void TestPackageManagerRegistriesWithNoManifestLoaded() {
            Assert.That(modifier.PackageManagerRegistries.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Parse registries from a manifest.
        /// </summary>
        [Test]
        public void TestPackageManagerRegistries() {
            WriteManifest(MANIFEST_MULTI_REGISTRIES);
            Assert.That(modifier.ReadManifest(), Is.EqualTo(true));

            var registries = modifier.PackageManagerRegistries;

            Assert.That(registries.ContainsKey("https://reg1.com"), Is.EqualTo(true));
            Assert.That(registries.ContainsKey("https://unity.reg2.com"), Is.EqualTo(true));

            var reg1 = registries["https://reg1.com"];
            Assert.That(reg1.Count, Is.EqualTo(2));
            Assert.That(reg1[0].Name, Is.EqualTo("Reg1"));
            Assert.That(reg1[0].Url, Is.EqualTo("https://reg1.com"));
            CollectionAssert.AreEquivalent(reg1[0].Scopes,
                                           new List<string>() { "com.reg1.foo", "com.reg1.bar" });
            Assert.That(reg1[1].Name, Is.EqualTo("Reg1 Ext"));
            Assert.That(reg1[1].Url, Is.EqualTo("https://reg1.com"));
            CollectionAssert.AreEquivalent(
                reg1[1].Scopes, new List<string>() { "com.reg1.ext" });

            var reg2 = registries["https://unity.reg2.com"];
            Assert.That(reg2.Count, Is.EqualTo(1));
            Assert.That(reg2[0].Name, Is.EqualTo("Reg2"));
            Assert.That(reg2[0].Url, Is.EqualTo("https://unity.reg2.com"));
            CollectionAssert.AreEquivalent(reg2[0].Scopes, new List<string>() { "com.reg2.bish" });
        }

        /// <summary>
        /// Try adding registries when a manifest isn't loaded.
        /// </summary>
        [Test]
        public void TestAddRegistriesWithNoManifestLoaded() {
            Assert.That(modifier.AddRegistries(new List<PackageManagerRegistry>()),
                        Is.EqualTo(false));
        }

        /// <summary>
        /// Add no registries to a manifest and write out the results.
        /// </summary>
        [Test]
        public void TestAddRegistriesEmptyAndWriteManifest() {
            WriteManifest(MANIFEST_MULTI_REGISTRIES);
            Assert.That(modifier.ReadManifest(), Is.EqualTo(true));
            Assert.That(modifier.AddRegistries(new List<PackageManagerRegistry>()),
                        Is.EqualTo(true));
            Assert.That(modifier.WriteManifest(), Is.EqualTo(true));
            Assert.That(ReadManifest(), Is.EqualTo(MANIFEST_MULTI_REGISTRIES));
        }

        /// <summary>
        /// Add some registries to the manifest and write out the results.
        /// </summary>
        [Test]
        public void TestAddRegistriesAndWriteManifest() {
            WriteManifest(MANIFEST_MULTI_REGISTRIES);
            Assert.That(modifier.ReadManifest(), Is.EqualTo(true));
            Assert.That(
                modifier.AddRegistries(new PackageManagerRegistry[] {
                        new PackageManagerRegistry() {
                            Name = "Reg1",
                            Url = "https://reg1.com",
                            Scopes = new List<string>() { "com.reg1.foo", "com.reg1.bar" }
                        },
                        new PackageManagerRegistry() {
                            Name = "Reg1 Ext",
                            Url = "https://reg1.com",
                            Scopes = new List<string>() { "com.reg1.ext" }
                        },
                        new PackageManagerRegistry() {
                            Name = "Reg2",
                            Url = "https://unity.reg2.com",
                            Scopes = new List<string>() { "com.reg2.bish" }
                        }
                    }),
                Is.EqualTo(true));
            Assert.That(modifier.WriteManifest(), Is.EqualTo(true));
            Assert.That(ReadManifest(), Is.EqualTo(MANIFEST_MULTI_REGISTRIES));
        }

        /// <summary>
        /// Try removing registries when the manifest isn't loaded.
        /// </summary>
        [Test]
        public void TestRemoveRegistriesWithNoManifestLoader() {
            Assert.That(modifier.RemoveRegistries(new List<PackageManagerRegistry>()),
                        Is.EqualTo(false));
        }

        /// <summary>
        /// Remove no registries from a manifest and write out the results.
        /// </summary>
        [Test]
        public void TestRemoveRegistriesEmptyAndWriteManifest() {
            WriteManifest(MANIFEST_MULTI_REGISTRIES);
            Assert.That(modifier.ReadManifest(), Is.EqualTo(true));
            Assert.That(modifier.RemoveRegistries(new List<PackageManagerRegistry>()),
                        Is.EqualTo(true));
            Assert.That(modifier.WriteManifest(), Is.EqualTo(true));
            Assert.That(ReadManifest(), Is.EqualTo(MANIFEST_MULTI_REGISTRIES));
        }

        /// <summary>
        /// Remove non-existent registries from a manifest and write out the results.
        /// </summary>
        [Test]
        public void TestRemoveRegistriesNonExistentAndWriteManifest() {
            WriteManifest(MANIFEST_MULTI_REGISTRIES);
            Assert.That(modifier.ReadManifest(), Is.EqualTo(true));
            Assert.That(
               modifier.RemoveRegistries(new PackageManagerRegistry[] {
                       new PackageManagerRegistry {
                           Name = "Texture It",
                           Url = "http://unity.cooltextures.org",
                           Scopes = new List<string>() { "org.cooltextures.textureit" }
                       }
                   }),
               Is.EqualTo(false));
            Assert.That(modifier.WriteManifest(), Is.EqualTo(true));
            Assert.That(ReadManifest(), Is.EqualTo(MANIFEST_MULTI_REGISTRIES));
        }

        /// <summary>
        /// Remove registries from a manifest and write out the results.
        /// </summary>
        [Test]
        public void TestRemoveRegistriesAndWriteManifest() {
            WriteManifest(MANIFEST_MULTI_REGISTRIES);
            Assert.That(modifier.ReadManifest(), Is.EqualTo(true));
            Assert.That(
               modifier.RemoveRegistries(new PackageManagerRegistry[] {
                        new PackageManagerRegistry() {
                            Name = "Reg1 Ext",
                            Url = "https://reg1.com",
                            Scopes = new List<string>() { "com.reg1.ext" }
                        },
                   }),
               Is.EqualTo(true));
            Assert.That(modifier.WriteManifest(), Is.EqualTo(true));
            Assert.That(
                ReadManifest(),
                Is.EqualTo("{\n" +
                           "  \"scopedRegistries\": [\n" +
                           "    {\n" +
                           "      \"name\": \"Reg2\",\n" +
                           "      \"url\": \"https://unity.reg2.com\",\n" +
                           "      \"scopes\": [\n" +
                           "        \"com.reg2.bish\"\n" +
                           "      ]\n" +
                           "    }\n" +
                           "  ]\n" +
                           "}"));
        }
    }
}

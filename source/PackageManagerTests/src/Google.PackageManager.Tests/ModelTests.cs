// <copyright file="PackageManagerModelTests.cs" company="Google Inc.">
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
namespace Google.PackageManager.Tests {
    using System.IO;
    using PackageManager;
    using NUnit.Framework;

    /// <summary>
    /// Package manager model tests.
    /// </summary>
    [TestFixture]
    public class PackageManagerModelTests {
        // Path to test data, contains a mock registry and settings config.
        public const string PATH = "../../testData";

        /// <summary>
        /// Tests root models are able to load from file.
        /// Root models include:
        /// - Repository
        /// - Description
        /// - PluginMetaData
        /// - PackageExportSettings
        /// </summary>
        [Test]
        public void TestLoadFromFile() {
            string registryPath = Path.Combine(PATH,"registry/registry.xml");
            Registry registry = Registry.LoadFromFile(registryPath);
            Assert.AreEqual("registry.google.unity",registry.groupId);
            Assert.AreEqual("jarresolver-google-registry",registry.artifactId);
            Assert.AreEqual("0.0.1.1",registry.version);
            Assert.AreEqual(1482171836,registry.lastUpdated);
            Assert.NotNull(registry.modules);
            Assert.AreEqual(1,registry.modules.module.Count);
            Assert.AreEqual("com.google.unity.example",registry.modules.module[0]);

            string barPluginPath = Path.Combine(PATH,
                       "registry/com.google.unity.example/package-manifest.xml");
            PluginMetaData pluginMetaData = PluginMetaData.LoadFromFile(barPluginPath);
            Assert.AreEqual("com.google.unity.example",pluginMetaData.groupId);
            Assert.AreEqual("gpm-example-plugin",pluginMetaData.artifactId);
            Assert.AreEqual("unitypackage",pluginMetaData.packaging);
            Assert.NotNull(pluginMetaData.versioning);
            Assert.AreEqual("1.0.0.0",pluginMetaData.versioning.release);
            Assert.NotNull(pluginMetaData.versioning.versions);
            Assert.AreEqual(1,pluginMetaData.versioning.versions.Count);
            Assert.AreEqual(0,pluginMetaData.lastUpdated);

            string barDescriptionPath = Path.Combine(PATH,
              "registry/com.google.unity.example/gpm-example-plugin/1.0.0.0/description.xml");
            PluginDescription description = PluginDescription.LoadFromFile(barDescriptionPath);
            Assert.NotNull(description.languages);
            Assert.AreEqual(1,description.languages.Count);
        }
        // TODO(krispy): add test cases - serialization, model differences
    }
}
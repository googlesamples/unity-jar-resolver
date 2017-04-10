// <copyright file="ControllerTests.cs" company="Google Inc.">
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
    using System.Collections.Generic;
    using System;

    internal static class TestData {
        public class MockEditorPrefs : IEditorPrefs {
            public Dictionary<string, string> data;

            public MockEditorPrefs() {
                data = new Dictionary<string, string>();
            }

            public void DeleteAll() {
                data.Clear();
            }

            public void DeleteKey(string key) {
                data.Remove(key);
            }

            string GetValue(string key, object defaultValue) {
                string tmp;
                if (data.TryGetValue(key, out tmp)) {
                    return tmp;
                }
                return string.Format("{0}", defaultValue);
            }

            public bool GetBool(string key, bool defaultValue = false) {
                return bool.Parse(GetValue(key, defaultValue));
            }

            public float GetFloat(string key, float defaultValue = 0) {
                return float.Parse(GetValue(key, defaultValue));
            }

            public int GetInt(string key, int defaultValue = 0) {
                return int.Parse(GetValue(key, defaultValue));
            }

            public string GetString(string key, string defaultValue = "") {
                return GetValue(key, defaultValue);
            }

            public bool HasKey(string key) {
                return data.ContainsKey(key);
            }

            void SetValue(string key, object value) {
                if (data.ContainsKey(key)) {
                    data[key] = string.Format("{0}", value);
                } else {
                    data.Add(key, string.Format("{0}", value));
                }
            }

            public void SetBool(string key, bool value) {
                SetValue(key, value);
            }

            public void SetFloat(string key, float value) {
                SetValue(key, value);
            }

            public void SetInt(string key, int value) {
                SetValue(key, value);
            }

            public void SetString(string key, string value) {
                SetValue(key, value);
            }
        }

        public static IEditorPrefs editorPrefs = new MockEditorPrefs();

        // Path to test data, contains a mock data.
        public const string PATH = "../../testData";

        public class MockFetcher : IUriDataFetcher {
            string textResult;
            ResponseCode rc;
            public MockFetcher(string giveText, ResponseCode giveResponse) {
                textResult = giveText;
                rc = giveResponse;
            }

            public ResponseCode BlockingFetchAsString(Uri uri, out string result) {
                result = textResult;
                return rc;
            }

            public ResponseCode BlockingFetchAsBytes(Uri uri, out byte[] result) {
                throw new NotImplementedException();
            }
        }

        public class MockMultiFetcher : IUriDataFetcher {
            List<string> textResults = new List<string>();
            List<ResponseCode> responses = new List<ResponseCode>();
            public int currentIndex = 0;

            public void ResetIndex() {
                currentIndex = 0;
            }

            public void AddResponse(string giveText, ResponseCode giveResponse) {
                textResults.Add(giveText);
                responses.Add(giveResponse);
            }

            public ResponseCode BlockingFetchAsString(Uri uri, out string result) {
                result = textResults[currentIndex];
                var responseCode = responses[currentIndex];
                ++currentIndex;
                if (currentIndex >= textResults.Count) {
                    throw new Exception("TEST CASE EXCEPTION - Multi Fetch exceeded index");
                }
                return responseCode;
            }

            public ResponseCode BlockingFetchAsBytes(Uri uri, out byte[] result) {
                throw new NotImplementedException();
            }
        }

        public class MockDeterministicFetcher : IUriDataFetcher {
            Dictionary<string, string> urlToXml = new Dictionary<string, string>();
            Dictionary<string, ResponseCode> uriToResponse = new Dictionary<string, ResponseCode>();
            public void AddResponse(string uri, string giveText, ResponseCode giveResponse) {
                if (!urlToXml.ContainsKey(uri)) {
                    urlToXml.Add(uri, giveText);
                } else {
                    urlToXml[uri] = giveText;
                }
                if (!uriToResponse.ContainsKey(uri)) {
                    uriToResponse.Add(uri, giveResponse);
                } else {
                    uriToResponse[uri] = giveResponse;
                }
            }
            public ResponseCode BlockingFetchAsString(Uri uri, out string result) {
                if (urlToXml.TryGetValue(uri.AbsoluteUri, out result)) {
                    ResponseCode r;
                    uriToResponse.TryGetValue(uri.AbsoluteUri, out r);
                    Console.WriteLine(string.Format("MockDeterministicFetcher:\nASK:{0}" +
                                                    "\nRESP:{1}\nDATA:{2}", uri, r, result));
                    return r;
                }
                result = null;
                return ResponseCode.FETCH_ERROR;
            }

            public ResponseCode BlockingFetchAsBytes(Uri uri, out byte[] result) {
                throw new NotImplementedException();
            }
        }
    }

    /// <summary>
    /// Test case set that excercises the PackageManagerController.
    /// </summary>
    [TestFixture]
    public class ControllerTests {
        TestData.MockDeterministicFetcher mockFetcher = new TestData.MockDeterministicFetcher();

        [SetUp]
        public void Setup() {
            UnityController.SwapEditorPrefs(new TestData.MockEditorPrefs());
            LoggingController.testing = true;
            TestableConstants.testcase = true;
            TestableConstants.DefaultRegistryLocation =
                                 new Uri(Path.GetFullPath(
                                     Path.Combine(TestData.PATH, "registry/registry.xml")))
                                 .AbsoluteUri;

            string testRegXmlPath = TestableConstants.DefaultRegistryLocation;
            mockFetcher.AddResponse((new Uri(testRegXmlPath)).AbsoluteUri,
                                    File.ReadAllText((new Uri(testRegXmlPath)).AbsolutePath),
                                    ResponseCode.FETCH_COMPLETE);
            var rb = new RegistryManagerController.RegistryDatabase();
            rb.registryLocation.Add(TestableConstants.DefaultRegistryLocation);
            UnityController.EditorPrefs.SetString(Constants.KEY_REGISTRIES,
                                                  rb.SerializeToXMLString());

            var u = new Uri(Path.GetFullPath(Path.Combine(TestData.PATH,"registry2/registry.xml")));
            mockFetcher.AddResponse(u.AbsoluteUri,
                                    File.ReadAllText(u.AbsolutePath),
                                    ResponseCode.FETCH_COMPLETE);

            UriDataFetchController.SwapUriDataFetcher(mockFetcher);
            UnityController.SwapEditorPrefs(TestData.editorPrefs);

            var rdb = new RegistryManagerController.RegistryDatabase();
            rdb.registryLocation.Add(TestableConstants.DefaultRegistryLocation);

            /// ISO 8601 format: yyyy-MM-ddTHH:mm:ssZ
            rdb.lastUpdate = DateTime.UtcNow.ToString("o"); // "o" = ISO 8601 formatting
            Console.Write(rdb.SerializeToXMLString());
            TestData.editorPrefs.SetString(Constants.KEY_REGISTRIES, rdb.SerializeToXMLString());

        }

        /// <summary>
        /// Tests the settings controller by toggleing the boolean settings and
        /// ensuring the DownloadCache location is set by default.
        /// </summary>
        [Test]
        public void TestSettingsController() {
            // DowloadCachePath should start with a value (system dependent).
            Assert.NotNull(SettingsController.DownloadCachePath,
                           "Why is download cache path null?");

            // Set and Get test.
            SettingsController.DownloadCachePath = Path.GetFullPath(TestData.PATH);
            Assert.NotNull(SettingsController.DownloadCachePath,
                           "Why is download cache path null?");
            Assert.AreEqual(Path.GetFullPath(TestData.PATH), SettingsController.DownloadCachePath,
                            "Why is download cache path different?");

            // Verbose logging Get/Set.
            Assert.IsTrue(SettingsController.VerboseLogging, "VerboseLogging to start true.");
            SettingsController.VerboseLogging = false;
            Assert.IsFalse(SettingsController.VerboseLogging);

            // Show install files Get/Set.
            Assert.IsTrue(SettingsController.ShowInstallFiles, "ShowInstallFiles to start true");
            SettingsController.ShowInstallFiles = false;
            Assert.IsFalse(SettingsController.ShowInstallFiles);
        }

        [Test]
        public void TestRegistryManagerController() {
            Assert.AreEqual(1, RegistryManagerController.AllWrappedRegistries.Count);

            var u = new Uri(Path.GetFullPath(Path.Combine(TestData.PATH,
                                                          "registry2/registry.xml")));
            Assert.AreEqual(ResponseCode.REGISTRY_ADDED,
                           RegistryManagerController.AddRegistry(u));
            Assert.AreEqual(2, RegistryManagerController.AllWrappedRegistries.Count);

            // Cannot add same uri.
            Assert.AreEqual(ResponseCode.REGISTRY_ALREADY_PRESENT,
                           RegistryManagerController.AddRegistry(u));

            // loading the database again should still be in same state
            RegistryManagerController.LoadRegistryDatabase();
            Assert.AreEqual(2,RegistryManagerController.AllWrappedRegistries.Count);

            Assert.AreEqual(ResponseCode.REGISTRY_REMOVED,
                            RegistryManagerController.RemoveRegistry(u));

            // Can't remove it a second time it won't be there.
            Assert.AreEqual(ResponseCode.REGISTRY_NOT_FOUND,
                            RegistryManagerController.RemoveRegistry(u));
        }

        [Test]
        public void TestPluginManagerController() {
            RegistryManagerController.LoadRegistryDatabase();
            Assert.AreEqual(1,RegistryManagerController.AllWrappedRegistries.Count);
            RegistryWrapper r = RegistryManagerController.AllWrappedRegistries[0];

            var u = new Uri(Path.GetFullPath(Path.Combine(
                TestData.PATH, "registry/com.google.unity.example/package-manifest.xml")));
            mockFetcher.AddResponse(u.AbsoluteUri,
                                    File.ReadAllText(u.AbsolutePath),
                                    ResponseCode.FETCH_COMPLETE);

            u = new Uri(Path.GetFullPath(Path.Combine(
                TestData.PATH,
                "registry/com.google.unity.example/gpm-example-plugin/1.0.0.0/description.xml")));
            mockFetcher.AddResponse(u.AbsoluteUri,
                                    File.ReadAllText(u.AbsolutePath),
                                    ResponseCode.FETCH_COMPLETE);

            // Test ChangeRegistryUriIntoModuleUri.
            var regU = new Uri(TestableConstants.DefaultRegistryLocation);
            var modName = "apples-oranges";
            var metaLoc = PluginManagerController.ChangeRegistryUriIntoModuleUri(regU, modName);
            Assert.IsTrue(metaLoc.AbsoluteUri.Contains(modName));
            Assert.IsTrue(metaLoc.AbsoluteUri.Contains(Constants.MANIFEST_FILE_NAME));

            // Test GetPluginForRegistry.
            var plugins = PluginManagerController.GetPluginsForRegistry(r, true);
            Assert.AreEqual(1, plugins.Count);
            var packagedPlugin = plugins[0];
            Assert.NotNull(packagedPlugin);
            Assert.AreEqual(r.Model, packagedPlugin.ParentRegistry);

            // Test GenerateDescriptionUri.
            var d = PluginManagerController.GenerateDescriptionUri(metaLoc,
                                                                   packagedPlugin.MetaData);
            Assert.IsTrue(d.AbsoluteUri.Contains(packagedPlugin.MetaData.artifactId));
            Assert.IsTrue(d.AbsoluteUri.Contains(packagedPlugin.MetaData.versioning.release));
            Assert.IsTrue(d.AbsoluteUri.Contains(Constants.DESCRIPTION_FILE_NAME));

            plugins = PluginManagerController.GetPluginsForRegistry(null);
            Assert.AreEqual(0, plugins.Count);

            // Test Refresh.
            PluginManagerController.Refresh(r);
            plugins = PluginManagerController.GetPluginsForRegistry(r);
            Assert.AreEqual(1, plugins.Count);
            packagedPlugin = plugins[0];
            Assert.NotNull(packagedPlugin);
            Assert.AreEqual(r.Model, packagedPlugin.ParentRegistry);
        }

        [Test]
        public void TestProjectManagerController() {
            // TODO - test refresh list of assets
            // TODO - test IsPluginInstalledInProject

        }
    }
}

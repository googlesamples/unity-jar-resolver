// <copyright file="ProjectSettingsTest.cs" company="Google Inc.">
// Copyright (C) 2019 Google Inc. All Rights Reserved.
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

namespace Google.VersionHandlerImpl.Tests {
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Google;

    /// <summary>
    /// Test the InMemorySettings class.
    /// </summary>
    [TestFixture]
    public class InMemorySettingsTest {

        /// <summary>
        /// Construct an empty instance.
        /// </summary>
        [Test]
        public void Construct() {
            ISettings settings = new InMemorySettings();
            Assert.That(settings.Keys, Is.EqualTo(new List<string>()));
        }

        /// <summary>
        /// Test Get*() methods with and without default values.
        /// </summary>
        [Test]
        public void Get() {
            ISettings settings = new InMemorySettings();
            Assert.That(settings.HasKey("int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("int"), Is.EqualTo(0));
            Assert.That(settings.GetInt("int", 42), Is.EqualTo(42));
            Assert.That(settings.GetBool("bool"), Is.EqualTo(false));
            Assert.That(settings.GetBool("bool", true), Is.EqualTo(true));
            Assert.That(settings.GetFloat("float"), Is.EqualTo(0.0f));
            Assert.That(settings.GetFloat("float", 3.14f), Is.EqualTo(3.14f));
            Assert.That(settings.GetString("string"), Is.EqualTo(""));
            Assert.That(settings.GetString("string", "nada"), Is.EqualTo("nada"));
            Assert.That(settings.Keys, Is.EqualTo(new List<string>()));
        }

        /// <summary>
        /// Test Set*() methods by fetching stored results.
        /// </summary>
        public void Set() {
            ISettings settings = new InMemorySettings();
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(0));
            settings.SetInt("an_int", 42);
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(42));
            Assert.That(settings.GetInt("an_int", 21), Is.EqualTo(42));

            Assert.That(settings.HasKey("a_bool"), Is.EqualTo(false));
            Assert.That(settings.GetBool("a_bool"), Is.EqualTo(false));
            settings.SetBool("a_bool", true);
            Assert.That(settings.HasKey("a_bool"), Is.EqualTo(true));
            Assert.That(settings.GetBool("a_bool"), Is.EqualTo(true));
            Assert.That(settings.GetBool("a_bool", false), Is.EqualTo(true));

            Assert.That(settings.HasKey("a_float"), Is.EqualTo(false));
            Assert.That(settings.GetFloat("a_float"), Is.EqualTo(0.0f));
            settings.SetFloat("a_float", 3.14f);
            Assert.That(settings.HasKey("a_float"), Is.EqualTo(true));
            Assert.That(settings.GetFloat("a_float"), Is.EqualTo(3.14f));
            Assert.That(settings.GetFloat("a_float", 0.707f), Is.EqualTo(3.14f));

            Assert.That(settings.HasKey("a_string"), Is.EqualTo(false));
            Assert.That(settings.GetString("a_string"), Is.EqualTo(""));
            settings.SetString("a_string", "nada");
            Assert.That(settings.HasKey("a_string"), Is.EqualTo(true));
            Assert.That(settings.GetString("a_string"), Is.EqualTo("nada"));
            Assert.That(settings.GetString("a_string", "casa"), Is.EqualTo("nada"));

            Assert.That(new HashSet<string>(settings.Keys),
                        Is.EqualTo(new HashSet<string> {
                                "an_int", "a_bool", "a_float", "a_string"
                            }));
        }
    }

    /// <summary>
    /// Test the ProjectSettings class.
    /// </summary>
    [TestFixture]
    public class ProjectSettingsTest {

        /// <summary>
        /// Isolate ProjectSettings from Unity APIs and global state.
        /// </summary>
        [SetUp]
        public void Setup() {
            ProjectSettings.persistenceEnabled = true;
            ProjectSettings.projectSettings = new InMemorySettings();
            ProjectSettings.systemSettings = new InMemorySettings();
            ProjectSettings.logger.Target = LogTarget.Console;
            ProjectSettings.checkoutFile = (filename, logger) => { return true; };
            // Delete any persisted settings.
            if (File.Exists(ProjectSettings.PROJECT_SETTINGS_FILE)) {
                (new System.IO.FileInfo(ProjectSettings.PROJECT_SETTINGS_FILE)).IsReadOnly = false;
                File.Delete(ProjectSettings.PROJECT_SETTINGS_FILE);
            }

        }

        /// <summary>
        /// Construct an empty settings object.
        /// </summary>
        [Test]
        public void Construct() {
            var settings = new ProjectSettings("myplugin");
            Assert.That(settings.ModuleName, Is.EqualTo("myplugin"));
            Assert.That(settings.UseProjectSettings, Is.EqualTo(true));
            Assert.Throws<NotImplementedException>(() => {
                    #pragma warning disable 0168
                    var keys = settings.Keys;
                    #pragma warning restore 0168
                });
        }

        /// <summary>
        /// Enable / disable project settings.
        /// </summary>
        [Test]
        public void UseProjectSettings() {
            var settings = new ProjectSettings("myplugin");
            Assert.That(settings.UseProjectSettings, Is.EqualTo(true));
            // The project preference hasn't been stored in system settings.
            Assert.That(ProjectSettings.systemSettings.HasKey(settings.UseProjectSettingsName),
                        Is.EqualTo(false));

            // Disable project settings and verify that it's stored in system settings.
            settings.UseProjectSettings = false;
            Assert.That(settings.UseProjectSettings, Is.EqualTo(false));
            Assert.That(ProjectSettings.systemSettings.GetBool(settings.UseProjectSettingsName),
                        Is.EqualTo(false));

            // Enable project settings and verify that it's stored in system settings.
            settings.UseProjectSettings = true;
            Assert.That(settings.UseProjectSettings, Is.EqualTo(true));
            Assert.That(ProjectSettings.systemSettings.GetBool(settings.UseProjectSettingsName),
                        Is.EqualTo(true));
        }

        /// <summary>
        /// Test persistence of project-level settings.
        /// </summary>
        [Test]
        public void PersistSettings() {
            var settings = new ProjectSettings("myplugin");

            Assert.That(settings.HasKey("an_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(0));
            Assert.That(settings.GetInt("an_int", 10), Is.EqualTo(10));
            settings.SetInt("an_int", 42);
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(42));
            Assert.That(settings.GetInt("an_int", 21), Is.EqualTo(42));

            Assert.That(settings.HasKey("a_bool"), Is.EqualTo(false));
            Assert.That(settings.GetBool("a_bool"), Is.EqualTo(false));
            Assert.That(settings.GetBool("a_bool", true), Is.EqualTo(true));
            settings.SetBool("a_bool", true);
            Assert.That(settings.HasKey("a_bool"), Is.EqualTo(true));
            Assert.That(settings.GetBool("a_bool"), Is.EqualTo(true));
            Assert.That(settings.GetBool("a_bool", false), Is.EqualTo(true));

            Assert.That(settings.HasKey("a_float"), Is.EqualTo(false));
            Assert.That(settings.GetFloat("a_float"), Is.EqualTo(0.0f));
            Assert.That(settings.GetFloat("a_float", 2.72f), Is.EqualTo(2.72f));
            settings.SetFloat("a_float", 3.14f);
            Assert.That(settings.HasKey("a_float"), Is.EqualTo(true));
            Assert.That(settings.GetFloat("a_float"), Is.EqualTo(3.14f));
            Assert.That(settings.GetFloat("a_float", 0.707f), Is.EqualTo(3.14f));

            Assert.That(settings.HasKey("a_string"), Is.EqualTo(false));
            Assert.That(settings.GetString("a_string"), Is.EqualTo(""));
            Assert.That(settings.GetString("a_string", "cansada"), Is.EqualTo("cansada"));
            settings.SetString("a_string", "nada");
            Assert.That(settings.HasKey("a_string"), Is.EqualTo(true));
            Assert.That(settings.GetString("a_string"), Is.EqualTo("nada"));
            Assert.That(settings.GetString("a_string", "casa"), Is.EqualTo("nada"));

            // Replace the backing stores for system and project settings.
            // This should force the project settings to be loaded from disk.
            ProjectSettings.projectSettings = new InMemorySettings();
            ProjectSettings.systemSettings = new InMemorySettings();
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(42));
            Assert.That(settings.HasKey("a_bool"), Is.EqualTo(true));
            Assert.That(settings.GetBool("a_bool"), Is.EqualTo(true));
            Assert.That(settings.HasKey("a_float"), Is.EqualTo(true));
            Assert.That(settings.GetFloat("a_float"), Is.EqualTo(3.14f));
            Assert.That(settings.HasKey("a_string"), Is.EqualTo(true));
            Assert.That(settings.GetString("a_string"), Is.EqualTo("nada"));

            // Force reload of settings.
            ProjectSettings.projectSettings = new InMemorySettings();
            ProjectSettings.systemSettings = new InMemorySettings();
            // Make sure that setting a value also loads other settings into the cache.
            settings.SetFloat("a_float", 0.707f);
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(42));
            Assert.That(settings.HasKey("a_bool"), Is.EqualTo(true));
            Assert.That(settings.GetBool("a_bool"), Is.EqualTo(true));
            Assert.That(settings.HasKey("a_float"), Is.EqualTo(true));
            Assert.That(settings.GetFloat("a_float"), Is.EqualTo(0.707f));
            Assert.That(settings.HasKey("a_string"), Is.EqualTo(true));
            Assert.That(settings.GetString("a_string"), Is.EqualTo("nada"));
        }

        /// <summary>
        /// Test attempting to store persisted settings to a read-only file.
        /// </summary>
        [Test]
        public void SaveToReadonlySettings() {
            // Create a readonly settings file.
            File.WriteAllText(ProjectSettings.PROJECT_SETTINGS_FILE,
                              "<projectSettings>\n" +
                              "  <projectSetting name=\"myplugin.bar\" value=\"True\" />\n" +
                              "</projectSettings>\n");
            (new System.IO.FileInfo(ProjectSettings.PROJECT_SETTINGS_FILE)).IsReadOnly = true;
            var settings = new ProjectSettings("myplugin");
            Assert.That(settings.HasKey("myplugin.foo", SettingsLocation.Project),
                        Is.EqualTo(false));
            Assert.That(settings.HasKey("myplugin.bar", SettingsLocation.Project),
                        Is.EqualTo(true));
            Assert.That(settings.GetBool("myplugin.bar", false, SettingsLocation.Project),
                        Is.EqualTo(true));
            settings.SetBool("myplugin.foo", true, SettingsLocation.Project);
            Assert.That(settings.HasKey("myplugin.foo", SettingsLocation.Project),
                        Is.EqualTo(true));
            Assert.That(settings.GetBool("myplugin.foo", false, SettingsLocation.Project),
                        Is.EqualTo(true));
        }

        /// <summary>
        /// Delete a persisted project-level setting.
        /// </summary>
        [Test]
        public void DeleteOneSetting() {
            var settings = new ProjectSettings("myplugin");

            settings.SetInt("an_int", 42);
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(42));

            // Replace the backing stores for system and project settings.
            // This should force the project settings to be loaded from disk.
            ProjectSettings.projectSettings = new InMemorySettings();
            ProjectSettings.systemSettings = new InMemorySettings();
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(42));

            settings.DeleteKey("an_int");
            settings.DeleteKey("non_existent");
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(0));

            ProjectSettings.projectSettings = new InMemorySettings();
            ProjectSettings.systemSettings = new InMemorySettings();
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(0));
        }

        /// <summary>
        /// Delete multiple settings.
        /// </summary>
        [Test]
        public void DeleteMultipleSettings() {
            var settings = new ProjectSettings("myplugin");

            settings.SetInt("an_int", 42);
            settings.SetInt("another_int", 21);
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(42));
            Assert.That(settings.HasKey("another_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("another_int"), Is.EqualTo(21));

            // Replace the backing stores for system and project settings.
            // This should force the project settings to be loaded from disk.
            ProjectSettings.projectSettings = new InMemorySettings();
            ProjectSettings.systemSettings = new InMemorySettings();
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(42));
            Assert.That(settings.HasKey("another_int"), Is.EqualTo(true));
            Assert.That(settings.GetInt("another_int"), Is.EqualTo(21));

            settings.DeleteKeys(new [] { "an_int", "another_int", "non_existent" });
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(0));
            Assert.That(settings.HasKey("another_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("another_int"), Is.EqualTo(0));

            ProjectSettings.projectSettings = new InMemorySettings();
            ProjectSettings.systemSettings = new InMemorySettings();
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(0));
            Assert.That(settings.HasKey("another_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("another_int"), Is.EqualTo(0));
        }

        /// <summary>
        /// Ensure settings are not persisted when persistence is disabled.
        /// </summary>
        [Test]
        public void PersistenceDisabled() {
            ProjectSettings.persistenceEnabled = false;
            var settings = new ProjectSettings("myplugin");

            settings.SetInt("an_int", 42);
            settings.SetBool("a_bool", true);
            settings.SetFloat("a_float", 3.14f);
            settings.SetString("a_string", "nada");

            // Replace the backing store for the project settings, this should not load settings
            // from disk.
            ProjectSettings.projectSettings = new InMemorySettings();
            ProjectSettings.systemSettings = new InMemorySettings();
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(0));
            Assert.That(settings.HasKey("a_bool"), Is.EqualTo(false));
            Assert.That(settings.GetBool("a_bool"), Is.EqualTo(false));
            Assert.That(settings.HasKey("a_float"), Is.EqualTo(false));
            Assert.That(settings.GetFloat("a_float"), Is.EqualTo(0.0f));
            Assert.That(settings.HasKey("a_string"), Is.EqualTo(false));
            Assert.That(settings.GetString("a_string"), Is.EqualTo(""));
        }

        /// <summary>
        /// Test storing system settings, ensuring they're not persisted as they're only in memory
        /// in this test environment.
        /// </summary>
        [Test]
        public void AccessSystemSettings() {
            var settings = new ProjectSettings("myplugin");

            Assert.That(settings.GetInt("an_int", 10, SettingsLocation.System), Is.EqualTo(10));
            settings.SetInt("an_int", 42, SettingsLocation.System);
            Assert.That(settings.GetInt("an_int", 21, SettingsLocation.System), Is.EqualTo(42));
            Assert.That(settings.HasKey("an_int", SettingsLocation.System), Is.EqualTo(true));
            Assert.That(settings.HasKey("an_int", SettingsLocation.Project), Is.EqualTo(false));
            // By default this is configured to read from the project settings so this should
            // be empty.
            Assert.That(settings.HasKey("an_int"), Is.EqualTo(false));

            Assert.That(settings.GetBool("a_bool", true, SettingsLocation.System),
                        Is.EqualTo(true));
            settings.SetBool("a_bool", true, SettingsLocation.System);
            Assert.That(settings.GetBool("a_bool", false, SettingsLocation.System),
                        Is.EqualTo(true));
            Assert.That(settings.HasKey("a_bool", SettingsLocation.System), Is.EqualTo(true));
            Assert.That(settings.HasKey("a_bool", SettingsLocation.Project), Is.EqualTo(false));
            Assert.That(settings.HasKey("a_bool"), Is.EqualTo(false));

            Assert.That(settings.GetFloat("a_float", 2.72f, SettingsLocation.System),
                        Is.EqualTo(2.72f));
            settings.SetFloat("a_float", 3.14f, SettingsLocation.System);
            Assert.That(settings.GetFloat("a_float", 0.707f, SettingsLocation.System),
                        Is.EqualTo(3.14f));
            Assert.That(settings.HasKey("a_float", SettingsLocation.System), Is.EqualTo(true));
            Assert.That(settings.HasKey("a_float", SettingsLocation.Project), Is.EqualTo(false));
            Assert.That(settings.HasKey("a_float"), Is.EqualTo(false));

            Assert.That(settings.GetString("a_string", "cansada", SettingsLocation.System),
                        Is.EqualTo("cansada"));
            settings.SetString("a_string", "nada", SettingsLocation.System);
            Assert.That(settings.GetString("a_string", "casa", SettingsLocation.System),
                        Is.EqualTo("nada"));
            Assert.That(settings.HasKey("a_string"), Is.EqualTo(false));
            Assert.That(settings.HasKey("a_string", SettingsLocation.System), Is.EqualTo(true));
            Assert.That(settings.HasKey("a_string", SettingsLocation.Project), Is.EqualTo(false));
            Assert.That(settings.HasKey("a_string"), Is.EqualTo(false));

            // Replace the backing stores for system and project settings.
            ProjectSettings.projectSettings = new InMemorySettings();
            ProjectSettings.systemSettings = new InMemorySettings();

            Assert.That(settings.HasKey("an_int"), Is.EqualTo(false));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(0));
            Assert.That(settings.HasKey("a_bool"), Is.EqualTo(false));
            Assert.That(settings.GetBool("a_bool"), Is.EqualTo(false));
            Assert.That(settings.HasKey("a_float"), Is.EqualTo(false));
            Assert.That(settings.GetFloat("a_float"), Is.EqualTo(0.0f));
            Assert.That(settings.HasKey("a_string"), Is.EqualTo(false));
            Assert.That(settings.GetString("a_string"), Is.EqualTo(""));
        }

        /// <summary>
        /// Ensure project settings override system settings when project settings are enabled.
        /// </summary>
        [Test]
        public void OverrideSystemSettings() {
            var settings = new ProjectSettings("myplugin");

            settings.SetInt("an_int", 21, SettingsLocation.System);
            settings.SetInt("an_int", 42, SettingsLocation.Project);
            Assert.That(settings.GetInt("an_int", 0, SettingsLocation.System), Is.EqualTo(21));
            Assert.That(settings.GetInt("an_int", 0, SettingsLocation.Project), Is.EqualTo(42));
            Assert.That(settings.GetInt("an_int"), Is.EqualTo(42));

            settings.SetBool("a_bool", true, SettingsLocation.System);
            settings.SetBool("a_bool", false, SettingsLocation.Project);
            Assert.That(settings.GetBool("a_bool", false, SettingsLocation.System),
                        Is.EqualTo(true));
            Assert.That(settings.GetBool("a_bool", true, SettingsLocation.Project),
                        Is.EqualTo(false));
            Assert.That(settings.GetBool("a_bool"), Is.EqualTo(false));

            settings.SetFloat("a_float", 3.14f, SettingsLocation.System);
            settings.SetFloat("a_float", 0.707f, SettingsLocation.Project);
            Assert.That(settings.GetFloat("a_float", 2.72f, SettingsLocation.System),
                        Is.EqualTo(3.14f));
            Assert.That(settings.GetFloat("a_float", 2.72f, SettingsLocation.Project),
                        Is.EqualTo(0.707f));

            settings.SetString("a_string", "foo", SettingsLocation.System);
            settings.SetString("a_string", "bar", SettingsLocation.Project);
            Assert.That(settings.GetString("a_string", "bish", SettingsLocation.System),
                        Is.EqualTo("foo"));
            Assert.That(settings.GetString("a_string", "bosh", SettingsLocation.Project),
                        Is.EqualTo("bar"));
            Assert.That(settings.GetString("a_string"), Is.EqualTo("bar"));
        }
    }
}

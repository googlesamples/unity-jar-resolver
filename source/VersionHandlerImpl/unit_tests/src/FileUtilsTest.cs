// <copyright file="FileUtilsTest.cs" company="Google LLC">
// Copyright (C) 2020 Google LLC. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHBar WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
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
    /// Test the ProjectSettings class.
    /// </summary>
    [TestFixture]
    public class FileUtilsTest {

        /// <summary>
        /// Isolate ProjectSettings from Unity APIs and global state.
        /// </summary>
        [SetUp]
        public void Setup() {
        }

        /// <summary>
        /// Test FileUtils.IsUnderDirectory()
        /// </summary>
        [Test]
        public void IsUnderDirectory() {
            Assert.That(FileUtils.IsUnderDirectory("Foo", "Foo"), Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderDirectory("Foo", "Foo/"), Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderDirectory("Foo/", "Foo"), Is.EqualTo(true));
            Assert.That(FileUtils.IsUnderDirectory("Foo/", "Foo/"), Is.EqualTo(true));
            Assert.That(FileUtils.IsUnderDirectory("Foo/Bar", "Foo"),
                        Is.EqualTo(true));
            Assert.That(FileUtils.IsUnderDirectory("Foo/Bar", "Foo/"),
                        Is.EqualTo(true));
            Assert.That(FileUtils.IsUnderDirectory("Foo/Bar", "Foo/Bar"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderDirectory("Foo/Bar", "Foo/Bar/"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderDirectory("Foo/Bar/", "Foo/Bar"),
                        Is.EqualTo(true));
            Assert.That(FileUtils.IsUnderDirectory("Foo/Bar/", "Foo/Bar/"),
                        Is.EqualTo(true));

            Assert.That(FileUtils.IsUnderDirectory("", ""), Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderDirectory("Foo", ""), Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderDirectory("", "Foo"), Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderDirectory("Foo", "/"), Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderDirectory("Foo", "Some"), Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderDirectory("Foo/Bar", "Bar"),
                        Is.EqualTo(false));

        }

        /// <summary>
        /// Test FileUtils.GetPackageDirectoryType()
        /// </summary>
        [Test]
        public void GetPackageDirectoryType() {
            Assert.That(FileUtils.GetPackageDirectoryType(""),
                        Is.EqualTo(FileUtils.PackageDirectoryType.None));
            Assert.That(FileUtils.GetPackageDirectoryType("Foo/Bar"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.None));
            Assert.That(FileUtils.GetPackageDirectoryType("Packages"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.None));
            Assert.That(FileUtils.GetPackageDirectoryType("Packages/"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.None));
            Assert.That(FileUtils.GetPackageDirectoryType("Packages/com.company.pkg"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.None));
            Assert.That(FileUtils.GetPackageDirectoryType("Packages/com.company.pkg/"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.AssetDatabasePath));
            Assert.That(FileUtils.GetPackageDirectoryType("Packages/com.company.pkg/Foo"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.AssetDatabasePath));
            Assert.That(FileUtils.GetPackageDirectoryType("Packages/com.company.pkg/Foo/Bar"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.AssetDatabasePath));
            Assert.That(FileUtils.GetPackageDirectoryType("Library"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.None));
            Assert.That(FileUtils.GetPackageDirectoryType("Library/PackageCache"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.None));
            Assert.That(FileUtils.GetPackageDirectoryType("Library/PackageCache/"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.None));
            Assert.That(FileUtils.GetPackageDirectoryType(
                        "Library/PackageCache/com.company.pkg@1.2.3"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.None));
            Assert.That(FileUtils.GetPackageDirectoryType(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.PhysicalPath));
            Assert.That(FileUtils.GetPackageDirectoryType(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo/Bar"),
                        Is.EqualTo(FileUtils.PackageDirectoryType.PhysicalPath));
        }

        /// <summary>
        /// Test FileUtils.IsUnderPackageDirectory()
        /// </summary>
        [Test]
        public void IsUnderPackageDirectory() {
            Assert.That(FileUtils.IsUnderPackageDirectory(""),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderPackageDirectory("Foo/Bar"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderPackageDirectory("Packages"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderPackageDirectory("Packages/"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderPackageDirectory("Packages/com.company.pkg"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderPackageDirectory("Packages/com.company.pkg/Foo"),
                        Is.EqualTo(true));
            Assert.That(FileUtils.IsUnderPackageDirectory("Packages/com.company.pkg/Foo/Bar"),
                        Is.EqualTo(true));
            Assert.That(FileUtils.IsUnderPackageDirectory("Library"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderPackageDirectory("Library/PackageCache"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderPackageDirectory("Library/PackageCache/"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3"),
                        Is.EqualTo(false));
            Assert.That(FileUtils.IsUnderPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo"),
                        Is.EqualTo(true));
            Assert.That(FileUtils.IsUnderPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo/Bar"),
                        Is.EqualTo(true));
        }

        /// <summary>
        /// Test FileUtils.GetPackageDirectory()
        /// </summary>
        [Test]
        public void GetPackageDirectory() {
            const string expectedAssetDBPath = "Packages/com.company.pkg";
            const string expectedActualPath = "Library/PackageCache/com.company.pkg@1.2.3";

            Assert.That(FileUtils.GetPackageDirectory(""), Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory("Foo/Bar"), Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory("Packages"), Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory("Packages/"), Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg"),
                        Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg/"),
                        Is.EqualTo(expectedAssetDBPath));
            Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg/Foo"),
                        Is.EqualTo(expectedAssetDBPath));
            Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg/Foo/Bar"),
                        Is.EqualTo(expectedAssetDBPath));
            Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg",
                        FileUtils.PackageDirectoryType.AssetDatabasePath),
                        Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg/",
                        FileUtils.PackageDirectoryType.AssetDatabasePath),
                        Is.EqualTo(expectedAssetDBPath));
            Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg/Foo",
                        FileUtils.PackageDirectoryType.AssetDatabasePath),
                        Is.EqualTo(expectedAssetDBPath));
            Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg/Foo/Bar",
                        FileUtils.PackageDirectoryType.AssetDatabasePath),
                        Is.EqualTo(expectedAssetDBPath));
            // The following test does not work since it requires UnityEngine namespace
            // TODO: Switch to IntegrationTest framework
            // Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg",
            //             FileUtils.PackageDirectoryType.PhysicalPath),
            //             Is.EqualTo(expectedActualPath));
            // Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg/Foo",
            //             FileUtils.PackageDirectoryType.PhysicalPath),
            //             Is.EqualTo(expectedActualPath));
            // Assert.That(FileUtils.GetPackageDirectory("Packages/com.company.pkg/Foo/Bar",
            //             FileUtils.PackageDirectoryType.PhysicalPath),
            //             Is.EqualTo(expectedActualPath));
            Assert.That(FileUtils.GetPackageDirectory("Library"),
                        Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory("Library/PackageCache"),
                        Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory("Library/PackageCache/"),
                        Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3"),
                        Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/"),
                        Is.EqualTo(expectedActualPath));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo"),
                        Is.EqualTo(expectedActualPath));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo/Bar"),
                        Is.EqualTo(expectedActualPath));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3",
                        FileUtils.PackageDirectoryType.AssetDatabasePath),
                        Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/",
                        FileUtils.PackageDirectoryType.AssetDatabasePath),
                        Is.EqualTo(expectedAssetDBPath));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo",
                        FileUtils.PackageDirectoryType.AssetDatabasePath),
                        Is.EqualTo(expectedAssetDBPath));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo/Bar",
                        FileUtils.PackageDirectoryType.AssetDatabasePath),
                        Is.EqualTo(expectedAssetDBPath));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3",
                        FileUtils.PackageDirectoryType.PhysicalPath),
                        Is.EqualTo(""));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/",
                        FileUtils.PackageDirectoryType.PhysicalPath),
                        Is.EqualTo(expectedActualPath));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo",
                        FileUtils.PackageDirectoryType.PhysicalPath),
                        Is.EqualTo(expectedActualPath));
            Assert.That(FileUtils.GetPackageDirectory(
                        "Library/PackageCache/com.company.pkg@1.2.3/Foo/Bar",
                        FileUtils.PackageDirectoryType.PhysicalPath),
                        Is.EqualTo(expectedActualPath));

        }

        /// <summary>
        /// Test FileUtils.GetRelativePathFromAssetsOrPackagesFolder()
        /// </summary>
        [Test]
        public void GetRelativePathFromAssetsOrPackagesFolder() {
            string basePath;
            string relativePath;
            bool result;

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Assets", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(false));
            Assert.That(basePath, Is.EqualTo(""));
            Assert.That(relativePath, Is.EqualTo("Assets"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Assets/", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Assets"));
            Assert.That(relativePath, Is.EqualTo(""));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Assets/Foo", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Assets"));
            Assert.That(relativePath, Is.EqualTo("Foo"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Assets/Foo/", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Assets"));
            Assert.That(relativePath, Is.EqualTo("Foo/"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Assets/Foo/Bar", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Assets"));
            Assert.That(relativePath, Is.EqualTo("Foo/Bar"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Packages/com.company.pkg", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(false));
            Assert.That(basePath, Is.EqualTo(""));
            Assert.That(relativePath, Is.EqualTo("Packages/com.company.pkg"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Packages/com.company.pkg/", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Packages/com.company.pkg"));
            Assert.That(relativePath, Is.EqualTo(""));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Packages/com.company.pkg/Foo", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Packages/com.company.pkg"));
            Assert.That(relativePath, Is.EqualTo("Foo"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Packages/com.company.pkg/Foo/", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Packages/com.company.pkg"));
            Assert.That(relativePath, Is.EqualTo("Foo/"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Packages/com.company.pkg/Foo/Bar", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Packages/com.company.pkg"));
            Assert.That(relativePath, Is.EqualTo("Foo/Bar"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Library/PackageCache/com.company.pkg@1.2.3", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(false));
            Assert.That(basePath, Is.EqualTo(""));
            Assert.That(relativePath, Is.EqualTo("Library/PackageCache/com.company.pkg@1.2.3"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Library/PackageCache/com.company.pkg@1.2.3/", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Library/PackageCache/com.company.pkg@1.2.3"));
            Assert.That(relativePath, Is.EqualTo(""));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Library/PackageCache/com.company.pkg@1.2.3/Foo",
                    out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Library/PackageCache/com.company.pkg@1.2.3"));
            Assert.That(relativePath, Is.EqualTo("Foo"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Library/PackageCache/com.company.pkg@1.2.3/Foo/",
                    out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Library/PackageCache/com.company.pkg@1.2.3"));
            Assert.That(relativePath, Is.EqualTo("Foo/"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Library/PackageCache/com.company.pkg@1.2.3/Foo/Bar",
                    out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(basePath, Is.EqualTo("Library/PackageCache/com.company.pkg@1.2.3"));
            Assert.That(relativePath, Is.EqualTo("Foo/Bar"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "/Foo", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(false));
            Assert.That(basePath, Is.EqualTo(""));
            Assert.That(relativePath, Is.EqualTo("/Foo"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Foo", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(false));
            Assert.That(basePath, Is.EqualTo(""));
            Assert.That(relativePath, Is.EqualTo("Foo"));

            result = FileUtils.GetRelativePathFromAssetsOrPackagesFolder(
                    "Foo/Bar", out basePath, out relativePath);
            Assert.That(result, Is.EqualTo(false));
            Assert.That(basePath, Is.EqualTo(""));
            Assert.That(relativePath, Is.EqualTo("Foo/Bar"));
        }

        /// <summary>
        /// Test FileUtils.ReplaceBaseAssetsOrPackagesFolder()
        /// </summary>
        [Test]
        public void ReplaceBaseAssetsOrPackagesFolder() {
            Assert.That(
                FileUtils.ReplaceBaseAssetsOrPackagesFolder("Assets/Bar", "Foo"),
                Is.EqualTo("Foo/Bar"));
            Assert.That(
                FileUtils.ReplaceBaseAssetsOrPackagesFolder("Assets/Bar", "Assets/Foo"),
                Is.EqualTo("Assets/Foo/Bar"));
            Assert.That(
                FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                    "Packages/com.company.pkg/Bar", "Foo"),
                Is.EqualTo("Foo/Bar"));
            Assert.That(
                FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                    "Packages/com.company.pkg/Bar", "Assets/Foo"),
                Is.EqualTo("Assets/Foo/Bar"));
            Assert.That(
                FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                    "Library/PackageCache/com.company.pkg@1.2.3/Bar", "Foo"),
                Is.EqualTo("Foo/Bar"));
            Assert.That(
                FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                    "Library/PackageCache/com.company.pkg@1.2.3/Bar", "Assets/Foo"),
                Is.EqualTo("Assets/Foo/Bar"));

            Assert.That(
                FileUtils.ReplaceBaseAssetsOrPackagesFolder(
                    "Foo/Bar", "Assets"),
                Is.EqualTo("Foo/Bar"));
        }

        /// <summary>
        /// Test FileUtils.IsValidGuid() when it returns true
        /// </summary>
        [Test]
        public void IsValidGuid_TrueCases() {
            Assert.That(
                FileUtils.IsValidGuid("4b7c4a82-79ca-4eb5-a154-5d78a3b3d3d7"),
                Is.EqualTo(true));

            Assert.That(
                FileUtils.IsValidGuid("017885d9f22374a53844077ede0ccda6"),
                Is.EqualTo(true));
        }

        /// <summary>
        /// Test FileUtils.IsValidGuid() when it returns false
        /// </summary>
        [Test]
        public void IsValidGuid_FalseCases() {
            Assert.That(
                FileUtils.IsValidGuid(""),
                Is.EqualTo(false));
            Assert.That(
                FileUtils.IsValidGuid(null),
                Is.EqualTo(false));
            Assert.That(
                FileUtils.IsValidGuid("00000000-0000-0000-0000-000000000000"),
                Is.EqualTo(false));
            Assert.That(
                FileUtils.IsValidGuid("00000000000000000000000000000000"),
                Is.EqualTo(false));
            Assert.That(
                FileUtils.IsValidGuid("g000000000000000000000000000000"),
                Is.EqualTo(false));
            Assert.That(
                FileUtils.IsValidGuid("   "),
                Is.EqualTo(false));
            Assert.That(
                FileUtils.IsValidGuid("12300000 0000 0000 0000 000000000000"),
                Is.EqualTo(false));
            Assert.That(
                FileUtils.IsValidGuid("12300000\n0000\n0000\n0000\n000000000000"),
                Is.EqualTo(false));
        }
    }
}

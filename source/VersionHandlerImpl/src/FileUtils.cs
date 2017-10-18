// <copyright file="Logger.cs" company="Google Inc.">
// Copyright (C) 2017 Google Inc. All Rights Reserved.
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

namespace Google {
    using System;
    using System.IO;

    /// <summary>
    /// Utility methods to assist with file management in Unity.
    /// </summary>
    internal class FileUtils {
        /// <summary>
        /// Extension of Unity metadata files.
        /// </summary>
        internal const string META_EXTENSION = ".meta";

        /// <summary>
        /// Delete a file or directory if it exists.
        /// </summary>
        /// <param name="path">Path to the file or directory to delete if it exists.</param>
        /// <param name="includeMetaFiles">Whether to delete Unity's associated .meta file(s).
        /// </param>
        /// <returns>true if *any* files or directories were deleted, false otherwise.</returns>
        public static bool DeleteExistingFileOrDirectory(string path,
                                                         bool includeMetaFiles = true)
        {
            bool deletedFileOrDirectory = false;
            if (includeMetaFiles && !path.EndsWith(META_EXTENSION)) {
                deletedFileOrDirectory = DeleteExistingFileOrDirectory(path + META_EXTENSION);
            }
            if (Directory.Exists(path)) {
                var di = new DirectoryInfo(path);
                di.Attributes &= ~FileAttributes.ReadOnly;
                foreach (string file in Directory.GetFileSystemEntries(path)) {
                    DeleteExistingFileOrDirectory(file, includeMetaFiles: includeMetaFiles);
                }
                Directory.Delete(path);
                deletedFileOrDirectory = true;
            }
            else if (File.Exists(path)) {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                File.Delete(path);
                deletedFileOrDirectory = true;
            }
            return deletedFileOrDirectory;
        }

        /// <summary>
        /// Copy the contents of a directory to another directory.
        /// </summary>
        /// <param name="sourceDir">Path to copy the contents from.</param>
        /// <param name="targetDir">Path to copy to.</param>
        public static void CopyDirectory(string sourceDir, string targetDir) {
            Func<string, string> sourceToTargetPath = (path) => {
                return Path.Combine(targetDir, path.Substring(sourceDir.Length + 1));
            };
            foreach (string sourcePath in
                     Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories)) {
                Directory.CreateDirectory(sourceToTargetPath(sourcePath));
            }
            foreach (string sourcePath in
                     Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) {
                if (!sourcePath.EndsWith(META_EXTENSION)) {
                    File.Copy(sourcePath, sourceToTargetPath(sourcePath));
                }
            }
        }
    }
}

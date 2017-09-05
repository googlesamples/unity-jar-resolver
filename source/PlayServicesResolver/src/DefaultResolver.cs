// <copyright file="DefaultResolver.cs" company="Google Inc.">
// Copyright (C) 2015 Google Inc. All Rights Reserved.
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

namespace GooglePlayServices
{
    using UnityEditor;
    using Google.JarResolver;
    using System.IO;
    using UnityEngine;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Default resolver base class.
    /// </summary>
    /// <remarks> This class contains the default implementation of the
    /// standard methods used to resolve the play-services dependencies.
    /// The intention is that common, stable methods are implemented here, and
    /// subsequent versions of the resolver would extend this class to modify the
    /// behavior.
    /// </remarks>
    public abstract class DefaultResolver : IResolver
    {
        // Namespace for resources under the src/scripts directory embedded within this assembly.
        protected const string EMBEDDED_RESOURCES_NAMESPACE = "PlayServicesResolver.scripts.";

        #region IResolver implementation

        /// <summary>
        /// Version of the resolver - 1.0.0
        /// </summary>
        public virtual int Version()
        {
            return MakeVersionNumber(1, 0, 0);
        }

        /// <summary>
        /// Enables automatic resolution.
        /// </summary>
        /// <param name="flag">If set to <c>true</c> flag.</param>
        public virtual void SetAutomaticResolutionEnabled(bool flag)
        {
            SettingsDialog.EnableAutoResolution = flag;
        }

        /// <summary>
        /// Returns true if automatic resolution is enabled.
        /// </summary>
        /// <returns><c>true</c>, if resolution enabled was automaticed, <c>false</c> otherwise.</returns>
        public virtual bool AutomaticResolutionEnabled()
        {
            return SettingsDialog.EnableAutoResolution;
        }

        /// <summary>
        /// Returns true if Android package installation is enabled.
        /// </summary>
        /// <returns><c>true</c>, package installation is enabled, <c>false</c> otherwise.
        /// </returns>
        public virtual bool AndroidPackageInstallationEnabled()
        {
            return SettingsDialog.InstallAndroidPackages;
        }

        /// <summary>
        /// Checks based on the asset changes, if resolution should occur.
        /// </summary>
        /// <remarks>
        /// The resolution only happens if a script file (.cs, or .js) was imported
        /// or if an Android plugin was deleted.  This allows for changes to
        /// assets that do not affect the dependencies to happen without processing.
        /// This also avoids an infinite loop when a version of a dependency is
        /// deleted during resolution.
        /// </remarks>
        /// <returns><c>true</c>, if auto resolution should happen, <c>false</c> otherwise.</returns>
        /// <param name="importedAssets">Imported assets.</param>
        /// <param name="deletedAssets">Deleted assets.</param>
        /// <param name="movedAssets">Moved assets.</param>
        /// <param name="movedFromAssetPaths">Moved from asset paths.</param>
        [Obsolete]
        public virtual bool ShouldAutoResolve(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths) { return false; }

        /// <summary>
        /// Shows the settings dialog.
        /// </summary>
        public virtual void ShowSettingsDialog() { ShowSettings(); }

        /// <summary>
        /// Show the settings dialog.
        /// This method is used when a Resolver isn't instanced.
        /// </summary>
        internal static void ShowSettings()
        {
            SettingsDialog window = (SettingsDialog)EditorWindow.GetWindow(
                typeof(SettingsDialog), true, "Android Resolver Settings");
            window.Initialize();
            window.Show();
        }

        /// <summary>
        /// Does the resolution of the play-services aars.
        /// </summary>
        /// <param name="svcSupport">Svc support.</param>
        /// <param name="destinationDirectory">Destination directory.</param>
        /// <param name="handleOverwriteConfirmation">Handle overwrite confirmation.</param>
        /// <param name="resolutionComplete">Delegate called when resolution is complete.</param>
        public virtual void DoResolution(
            PlayServicesSupport svcSupport,
            string destinationDirectory,
            PlayServicesSupport.OverwriteConfirmation handleOverwriteConfirmation,
            System.Action resolutionComplete)
        {
            resolutionComplete();
        }

        /// <summary>
        /// Called during Update to allow the resolver to check the bundle ID of the application
        /// to see whether resolution should be triggered again.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///      <returns>Array of packages that should be re-resolved if resolution should occur,
        /// null otherwise.</returns>
        [Obsolete]
        public virtual string[] OnBundleId(string bundleId) { return OnBuildSettings(); }

        /// <summary>
        /// Called during Update to allow the resolver to check any build settings of managed
        /// packages to see whether resolution should be triggered again.
        /// </summary>
        /// <returns>Array of packages that should be re-resolved if resolution should occur,
        /// null otherwise.</returns>
        public virtual string[] OnBuildSettings() { return null; }

        /// <summary>
        /// Determine whether to replace a dependency with a new version.
        /// </summary>
        /// <param name="oldDependency">Previous version of the dependency.</param>
        /// <param name="newDependency">New version of the dependency.</param>
        /// <returns>true if the dependency should be replaced, false otherwise.</returns>
        public virtual bool ShouldReplaceDependency(Dependency oldDependency,
                                                    Dependency newDependency) {
            return false;
        }

        #endregion

        /// <summary>
        /// Compatibility method for synchronous implementations of DoResolution().
        /// </summary>
        /// <param name="svcSupport">Svc support.</param>
        /// <param name="destinationDirectory">Destination directory.</param>
        /// <param name="handleOverwriteConfirmation">Handle overwrite confirmation.</param>
        public virtual void DoResolution(
            PlayServicesSupport svcSupport,
            string destinationDirectory,
            PlayServicesSupport.OverwriteConfirmation handleOverwriteConfirmation)
        {
            DoResolution(svcSupport, destinationDirectory, handleOverwriteConfirmation,
                () => {});
        }

        /// <summary>
        /// Makes the version number.
        /// </summary>
        /// <remarks>This combines the major/minor/point version components into
        /// an integer.  If multiple resolvers are registered, then the greatest version
        /// is used.
        /// </remarks>
        /// <returns>The version number.</returns>
        /// <param name="maj">Maj.</param>
        /// <param name="min">Minimum.</param>
        /// <param name="pt">Point.</param>
        internal int MakeVersionNumber(int maj, int min, int pt)
        {
            return maj * 10000 + min + 100 + pt;
        }

        /// <summary>
        /// Gets a value indicating whether this version of Unity supports aar files.
        /// </summary>
        /// <value><c>true</c> if supports aar files; otherwise, <c>false</c>.</value>
        internal bool SupportsAarFiles
        {
            get
            {
                // Get the version number.
                string majorVersion = Application.unityVersion.Split('.')[0];
                int ver;
                if (!int.TryParse(majorVersion, out ver))
                {
                    ver = 4;
                }
                return ver >= 5;
            }
        }

        /// <summary>
        /// Create a temporary directory.
        /// </summary>
        /// <returns>If temporary directory creation fails, return null.</returns>
        public static string CreateTemporaryDirectory()
        {
            int retry = 100;
            while (retry-- > 0)
            {
                string temporaryDirectory = Path.Combine(Path.GetTempPath(),
                                                         Path.GetRandomFileName());
                if (File.Exists(temporaryDirectory))
                {
                    continue;
                }
                Directory.CreateDirectory(temporaryDirectory);
                return temporaryDirectory;
            }
            return null;
        }

        /// <summary>
        /// Extract an AAR (or zip file) to the specified directory.
        /// </summary>
        /// <param name="aarFile">Name of the AAR file to extract.</param>
        /// <param name="extract_filenames">List of files to extract from the AAR.  If this array
        /// is empty or null all files are extracted.</param>
        /// <param name="outputDirectory">Directory to extract the AAR file to.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal virtual bool ExtractAar(string aarFile, string[] extractFilenames,
                                         string outputDirectory)
        {
            try {
                string aarPath = Path.GetFullPath(aarFile);
                string extractFilesArg = extractFilenames != null && extractFilenames.Length > 0 ?
                    " \"" + String.Join("\" \"", extractFilenames) + "\"" : "";
                CommandLine.Result result = CommandLine.Run(JavaUtilities.JarBinaryPath,
                                                            "xvf " + "\"" + aarPath + "\"" +
                                                            extractFilesArg,
                                                            workingDirectory: outputDirectory);
                if (result.exitCode != 0) {
                    Debug.LogError("Error expanding " + aarPath + " err: " +
                                   result.exitCode + ": " + result.stderr);
                    return false;
                }
            }
            catch (Exception e) {
                Debug.LogError(e);
                throw e;
            }
            return true;
        }

        /// <summary>
        /// Create an AAR from the specified directory.
        /// </summary>
        /// <param name="aarFile">AAR file to create.</param>
        /// <param name="inputDirectory">Directory which contains the set of files to store
        /// in the AAR.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal virtual bool ArchiveAar(string aarFile, string inputDirectory) {
            try {
                string aarPath = Path.GetFullPath(aarFile);
                CommandLine.Result result = CommandLine.Run(
                    JavaUtilities.JarBinaryPath,
                    String.Format("cvf \"{0}\" -C \"{1}\" .", aarPath, inputDirectory));
                if (result.exitCode != 0) {
                    Debug.LogError(String.Format("Error archiving {0}\n" +
                                                 "Exit code: {1}\n" +
                                                 "{2}\n" +
                                                 "{3}\n",
                                                 aarPath, result.exitCode, result.stdout,
                                                 result.stderr));
                    return false;
                }
            } catch (Exception e) {
                Debug.LogError(e);
                throw e;
            }
            return true;
        }

        // Native library ABI subdirectories supported by Unity.
        internal const string NATIVE_LIBRARY_ABI_DIRECTORY_ARMEABI_V7A = "armeabi-v7a";
        internal const string NATIVE_LIBRARY_ABI_DIRECTORY_X86 = "x86";
        // Directories that contain native libraries within a Unity Android library project.
        internal static string[] NATIVE_LIBRARY_DIRECTORIES = new string[] { "libs", "jni" };
        // Map of Unity ABIs (see AndroidTargetDeviceAbi) to the ABI directory.
        internal static Dictionary<string, string> UNITY_ABI_TO_NATIVE_LIBRARY_ABI_DIRECTORY =
            new Dictionary<string, string> { {"armv7", NATIVE_LIBRARY_ABI_DIRECTORY_ARMEABI_V7A},
                                             {"x86", NATIVE_LIBRARY_ABI_DIRECTORY_X86} };

        /// <summary>
        /// Replaces the variables in the AndroidManifest file.
        /// </summary>
        /// <param name="exploded">Exploded.</param>
        internal void ReplaceVariables(string exploded) {
            string manifest = Path.Combine(exploded, "AndroidManifest.xml");
            if (File.Exists(manifest)) {
                StreamReader sr = new StreamReader(manifest);
                string body = sr.ReadToEnd();
                sr.Close();
                body = body.Replace("${applicationId}", UnityCompat.ApplicationId);
                using (var wr = new StreamWriter(manifest, false)) {
                    wr.Write(body);
                }
            }
        }

        /// <summary>
        /// Gets the directory names of currently targeted ABIs.
        /// </summary>
        /// <returns>returns the hashset of directory names, ie. "x86".</returns>
        internal HashSet<string> GetSelectedABIDirs(string currentAbi) {
            var activeAbis = new HashSet<string>();
            string abiDir;
            if (UNITY_ABI_TO_NATIVE_LIBRARY_ABI_DIRECTORY.TryGetValue(currentAbi, out abiDir)) {
                activeAbis.Add(abiDir);
            } else {
                activeAbis.UnionWith(UNITY_ABI_TO_NATIVE_LIBRARY_ABI_DIRECTORY.Values);
            }
            return activeAbis;
        }

        /// <summary>
        /// Explodes a single aar file.  This is done by calling the
        /// JDK "jar" command, then moving the classes.jar file.
        /// </summary>
        /// <param name="dir">the parent directory of the plugin.</param>
        /// <param name="aarFile">Aar file to explode.</param>
        /// <param name="antProject">true to explode into an Ant style project or false
        /// to repack the processed AAR as a new AAR.</param>
        /// <param name="abi">ABI of the AAR or null if it's universal.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal virtual bool ProcessAar(string dir, string aarFile, bool antProject,
                                         out string abi) {
            PlayServicesSupport.Log(String.Format("ProcessAar {0} {1} antProject={2}",
                                                  dir, aarFile, antProject), verbose: true);
            abi = null;
            string workingDir = Path.Combine(dir, Path.GetFileNameWithoutExtension(aarFile));
            PlayServicesSupport.DeleteExistingFileOrDirectory(workingDir, includeMetaFiles: true);
            Directory.CreateDirectory(workingDir);
            if (!ExtractAar(aarFile, null, workingDir)) return false;
            ReplaceVariables(workingDir);

            string nativeLibsDir = null;
            if (antProject) {
                // Create the libs directory to store the classes.jar and non-Java shared
                // libraries.
                string libDir = Path.Combine(workingDir, "libs");
                nativeLibsDir = libDir;
                Directory.CreateDirectory(libDir);

                // Move the classes.jar file to libs.
                string classesFile = Path.Combine(workingDir, "classes.jar");
                string targetClassesFile = Path.Combine(libDir, Path.GetFileName(classesFile));
                if (File.Exists(targetClassesFile)) File.Delete(targetClassesFile);
                if (File.Exists(classesFile)) {
                    File.Move(classesFile, targetClassesFile);
                } else {
                    // Generate an empty classes.jar file.
                    string temporaryDirectory = CreateTemporaryDirectory();
                    if (temporaryDirectory == null) return false;
                    ArchiveAar(targetClassesFile, temporaryDirectory);
                }
            }

            // Copy non-Java shared libraries (.so) files from the "jni" directory into the
            // lib directory so that Unity's legacy (Ant-like) build system includes them in the
            // built APK.
            string jniLibDir = Path.Combine(workingDir, "jni");
            nativeLibsDir = nativeLibsDir ?? jniLibDir;
            if (Directory.Exists(jniLibDir)) {
                if (jniLibDir != nativeLibsDir) {
                    PlayServicesSupport.CopyDirectory(jniLibDir, nativeLibsDir);
                    PlayServicesSupport.DeleteExistingFileOrDirectory(jniLibDir,
                                                                      includeMetaFiles: true);
                }
                // Remove shared libraries for all ABIs that are not required for the selected
                // target ABI.
                var currentAbi = PlayServicesResolver.AndroidTargetDeviceAbi;
                var activeAbis = GetSelectedABIDirs(currentAbi);
                foreach (var directory in Directory.GetDirectories(nativeLibsDir)) {
                    var abiDir = Path.GetFileName(directory);
                    if (!activeAbis.Contains(abiDir)) {
                        PlayServicesSupport.DeleteExistingFileOrDirectory(
                            directory, includeMetaFiles: true);
                    }
                }
                abi = currentAbi;
            }

            if (antProject) {
                // Create the project.properties file which indicates to
                // Unity that this directory is a plugin.
                string projectProperties = Path.Combine(workingDir, "project.properties");
                if (!File.Exists(projectProperties)) {
                    File.WriteAllLines(projectProperties, new [] {
                        "# Project target.",
                        "target=android-9",
                        "android.library=true"
                    });
                }
                // Clean up the aar file.
                PlayServicesSupport.DeleteExistingFileOrDirectory(Path.GetFullPath(aarFile),
                                                                  includeMetaFiles: true);
                // Add a tracking label to the exploded files.
                PlayServicesResolver.LabelAssets(new [] { workingDir });
            } else {
                // Add a tracking label to the exploded files just in-case packaging fails.
                PlayServicesResolver.LabelAssets(new [] { workingDir });
                // Create a new AAR file.
                PlayServicesSupport.DeleteExistingFileOrDirectory(Path.GetFullPath(aarFile),
                                                                  includeMetaFiles: true);
                if (!ArchiveAar(aarFile, workingDir)) return false;
                // Clean up the exploded directory.
                PlayServicesSupport.DeleteExistingFileOrDirectory(workingDir,
                                                                  includeMetaFiles: true);
            }
            return true;
        }

        /// <summary>
        /// Extract a list of embedded resources to the specified path creating intermediate
        /// directories if they're required.
        /// </summary>
        /// <param name="resourceNameToTargetPath">Each Key is the resource to extract and each
        /// Value is the path to extract to.</param>
        protected static void ExtractResources(List<KeyValuePair<string, string>>
                                                   resourceNameToTargetPaths) {
            foreach (var kv in resourceNameToTargetPaths) ExtractResource(kv.Key, kv.Value);
        }

        /// <summary>
        /// Extract an embedded resource to the specified path creating intermediate directories
        /// if they're required.
        /// </summary>
        /// <param name="resourceName">Name of the resource to extract.</param>
        /// <param name="targetPath">Target path.</param>
        protected static void ExtractResource(string resourceName, string targetPath) {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            var stream = typeof(GooglePlayServices.ResolverVer1_1).Assembly.
                GetManifestResourceStream(resourceName);
            if (stream == null) {
                UnityEngine.Debug.LogError(String.Format("Failed to find resource {0} in assembly",
                                                         resourceName));
                return;
            }
            var data = new byte[stream.Length];
            stream.Read(data, 0, (int)stream.Length);
            File.WriteAllBytes(targetPath, data);
        }
    }
}


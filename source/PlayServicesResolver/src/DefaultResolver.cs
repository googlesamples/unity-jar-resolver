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
    using Google;
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
        /// Auto-resolution is never enabled in batch mode.  Each build setting change must be
        /// manually followed by DoResolution().
        /// </summary>
        /// <returns><c>true</c>, if resolution enabled was automaticed, <c>false</c> otherwise.</returns>
        public virtual bool AutomaticResolutionEnabled()
        {
            return SettingsDialog.EnableAutoResolution && !ExecutionEnvironment.InBatchMode;
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
        /// <param name="resolutionComplete">Delegate called when resolution is complete.</param>
        public virtual void DoResolution(PlayServicesSupport svcSupport,
                                         string destinationDirectory,
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

        #endregion

        /// <summary>
        /// Compatibility method for synchronous implementations of DoResolution().
        /// </summary>
        /// <param name="svcSupport">Svc support.</param>
        /// <param name="destinationDirectory">Destination directory.</param>
        public virtual void DoResolution(PlayServicesSupport svcSupport,
                                         string destinationDirectory)
        {
            DoResolution(svcSupport, destinationDirectory, () => {});
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
                    String.Format("cvf{0} \"{1}\" -C \"{2}\" .",
                                  aarFile.ToLower().EndsWith(".jar") ? "" : "M", aarPath,
                                  inputDirectory));
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
        // Directories that contain native libraries within a Unity Android library project.
        private static string[] NATIVE_LIBRARY_DIRECTORIES = new string[] { "libs", "jni" };

        /// <summary>
        /// Get the set of native library ABIs in an exploded AAR.
        /// </summary>
        /// <param name="aarDirectory">Directory to search for ABIs.</param>
        /// <returns>Set of ABI directory names in the exploded AAR or null if none are
        /// found.</returns>
        internal AndroidAbis AarDirectoryFindAbis(string aarDirectory) {
            var foundAbis = new HashSet<string>();
            foreach (var libDirectory in NATIVE_LIBRARY_DIRECTORIES) {
                foreach (var abiDir in AndroidAbis.AllSupported) {
                    if (Directory.Exists(Path.Combine(aarDirectory,
                                                      Path.Combine(libDirectory, abiDir)))) {
                        foundAbis.Add(abiDir);
                    }
                }
            }
            return foundAbis.Count > 0 ? new AndroidAbis(foundAbis) : null;
        }

        /// <summary>
        /// Explodes a single aar file.  This is done by calling the
        /// JDK "jar" command, then moving the classes.jar file.
        /// </summary>
        /// <param name="dir">The directory to unpack / explode the AAR to.  If antProject is true
        /// the ant project will be located in Path.Combine(dir, Path.GetFileName(aarFile)).</param>
        /// <param name="aarFile">Aar file to explode.</param>
        /// <param name="antProject">true to explode into an Ant style project or false
        /// to repack the processed AAR as a new AAR.</param>
        /// <param name="abis">ABIs in the AAR or null if it's universal.</param>
        /// <returns>true if successful, false otherwise.</returns>
        internal bool ProcessAar(string dir, string aarFile, bool antProject,
                                  out AndroidAbis abis) {
            PlayServicesResolver.Log(String.Format("ProcessAar {0} {1} antProject={2}",
                                                   dir, aarFile, antProject),
                                     level: LogLevel.Verbose);
            abis = null;
            string aarDirName = Path.GetFileNameWithoutExtension(aarFile);
            // Output directory for the contents of the AAR / JAR.
            string outputDir = Path.Combine(dir, aarDirName);
            string stagingDir = FileUtils.CreateTemporaryDirectory();
            if (stagingDir == null) {
                PlayServicesResolver.Log(String.Format(
                        "Unable to create temporary directory to process AAR {0}", aarFile),
                    level: LogLevel.Error);
                return false;
            }
            try {
                string workingDir = Path.Combine(stagingDir, aarDirName);
                FileUtils.DeleteExistingFileOrDirectory(workingDir);
                Directory.CreateDirectory(workingDir);
                if (!PlayServicesResolver.ExtractZip(aarFile, null, workingDir)) return false;
                PlayServicesResolver.ReplaceVariablesInAndroidManifest(
                    Path.Combine(workingDir, "AndroidManifest.xml"),
                    PlayServicesResolver.GetAndroidApplicationId(),
                    new Dictionary<string, string>());

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
                        FileUtils.MoveFile(classesFile, targetClassesFile);
                    } else {
                        // Some libraries publish AARs that are poorly formatted (e.g missing
                        // a classes.jar file).  Firebase's license AARs at certain versions are
                        // examples of this.  When Unity's internal build system detects an Ant
                        // project or AAR without a classes.jar, the build is aborted.  This
                        // generates an empty classes.jar file to workaround the issue.
                        string emptyClassesDir = Path.Combine(stagingDir, "empty_classes_jar");
                        if (!ArchiveAar(targetClassesFile, emptyClassesDir)) return false;
                    }
                }

                // Copy non-Java shared libraries (.so) files from the "jni" directory into the
                // lib directory so that Unity's legacy (Ant-like) build system includes them in the
                // built APK.
                string jniLibDir = Path.Combine(workingDir, "jni");
                nativeLibsDir = nativeLibsDir ?? jniLibDir;
                if (Directory.Exists(jniLibDir)) {
                    var abisInArchive = AarDirectoryFindAbis(workingDir);
                    if (jniLibDir != nativeLibsDir) {
                        FileUtils.CopyDirectory(jniLibDir, nativeLibsDir);
                        FileUtils.DeleteExistingFileOrDirectory(jniLibDir);
                    }
                    if (abisInArchive != null) {
                        // Remove shared libraries for all ABIs that are not required for the
                        // selected ABIs.
                        var activeAbisSet = AndroidAbis.Current.ToSet();
                        var abisInArchiveSet = abisInArchive.ToSet();
                        var abisInArchiveToRemoveSet = new HashSet<string>(abisInArchiveSet);
                        abisInArchiveToRemoveSet.ExceptWith(activeAbisSet);

                        Func<IEnumerable<string>, string> setToString = (setToConvert) => {
                            return String.Join(", ", (new List<string>(setToConvert)).ToArray());
                        };
                        PlayServicesResolver.Log(
                            String.Format(
                                "Target ABIs [{0}], ABIs [{1}] in {2}, will remove [{3}] ABIs",
                                setToString(activeAbisSet),
                                setToString(abisInArchiveSet),
                                aarFile,
                                setToString(abisInArchiveToRemoveSet)),
                            level: LogLevel.Verbose);

                        foreach (var abiToRemove in abisInArchiveToRemoveSet) {
                            abisInArchiveSet.Remove(abiToRemove);
                            FileUtils.DeleteExistingFileOrDirectory(Path.Combine(nativeLibsDir,
                                                                                 abiToRemove));
                        }
                        abis = new AndroidAbis(abisInArchiveSet);
                    }
                }

                if (antProject) {
                    // Create the project.properties file which indicates to Unity that this
                    // directory is a plugin.
                    string projectProperties = Path.Combine(workingDir, "project.properties");
                    if (!File.Exists(projectProperties)) {
                        File.WriteAllLines(projectProperties, new [] {
                            "# Project target.",
                            "target=android-9",
                            "android.library=true"
                        });
                    }
                    PlayServicesResolver.Log(
                        String.Format("Creating Ant project: Replacing {0} with {1}", aarFile,
                                      outputDir), level: LogLevel.Verbose);
                    // Clean up the aar file.
                    FileUtils.DeleteExistingFileOrDirectory(Path.GetFullPath(aarFile));
                    // Create the output directory.
                    FileUtils.MoveDirectory(workingDir, outputDir);
                    // Add a tracking label to the exploded files.
                    PlayServicesResolver.LabelAssets(new [] { outputDir });
                } else {
                    // Add a tracking label to the exploded files just in-case packaging fails.
                    PlayServicesResolver.Log(String.Format("Repacking {0} from {1}",
                                                           aarFile, workingDir),
                                             level: LogLevel.Verbose);
                    // Create a new AAR file.
                    FileUtils.DeleteExistingFileOrDirectory(Path.GetFullPath(aarFile));
                    if (!ArchiveAar(aarFile, workingDir)) return false;
                    PlayServicesResolver.LabelAssets(new [] { aarFile });
                }
            } catch (Exception e) {
                PlayServicesResolver.Log(String.Format("Failed to process AAR {0} ({1}",
                                                       aarFile, e),
                                         level: LogLevel.Error);
            } finally {
                // Clean up the temporary directory.
                FileUtils.DeleteExistingFileOrDirectory(stagingDir);
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

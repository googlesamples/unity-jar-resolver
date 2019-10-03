using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace Google {
    [InitializeOnLoad]
    public class PackageUninstallWindow : EditorWindow {
        // Variables for parsing Unity package manifests
        private const string VERSION_TOKEN = "_v*";
        private const string FILE_EXTENSION = ".txt";
        private const string PLAY_SERVICES_RESOLVER = "play-services-resolver";

        /// <summary>
        /// Uninstall the Unity packages selected by the user
        /// </summary>
        [MenuItem("Assets/Play Services Resolver/Version Handler/Uninstall Package")]
        public static void UninstallPackage() {
            // Get list of installed packages
            List<KeyValuePair<string, string>> displayList =
                GetPackageList(VersionHandlerImpl.FindAllAssets());

            // Remove play-services-resolver from the list
            // TODO: Replace hard-coded package name by finding the package's assembly
            KeyValuePair<string, string> playServicesResolver =
                displayList.Find(kvp => kvp.Value == PLAY_SERVICES_RESOLVER);
            displayList.Remove(new KeyValuePair<string, string>(playServicesResolver.Key,
                                                                playServicesResolver.Value));

            // Create a checkbox window listing installed packages
            MultiSelectWindow window =
                MultiSelectWindow.CreateMultiSelectWindow("Uninstall Packages");

            // Create window event handlers
            Action uninstallPackages = () => {
                // Iterated through all packages checked for uninstall

                RunOnMainThread.Run(() => { DeletePackages(window.SelectedItems); });
            };

            // Display MultiSelectWindow
            window.AvailableItems = new List<KeyValuePair<string, string>>(displayList);
            window.Caption = "Select packages to uninstall";
            window.OnApply = uninstallPackages;
            window.Show();
        }

        /// <summary>
        /// Get a List of Unity packages
        /// </summary>
        /// <param name="assets">List of Unity assets</param>
        /// <returns>A List of key-value pairs of filepath-PackageName</returns>
        private static List<KeyValuePair<string, string>> GetPackageList(string[] assets) {
            List<VersionHandlerImpl.ManifestReferences> manifestFiles = GetManifests(assets);
            List<KeyValuePair<string, string>> installedPackages =
                new List<KeyValuePair<string, string>>();
            foreach (VersionHandlerImpl.ManifestReferences manifest in manifestFiles) {
                string filepath = manifest.currentMetadata.filename;
                string filename = Path.GetFileNameWithoutExtension(manifest.filenameCanonical);
                installedPackages.Add(new KeyValuePair<string, string>(filepath, filename));
            }

            return installedPackages;
        }

        /// <summary>
        /// Get a type List of currently installed Unity package manifest files. The List type
        /// is Google.VersionHandlerImpl.ManifestReferences
        /// </summary>
        /// <param name="filepaths">A string array of Unity asset filepaths. The root of the
        /// filepaths is the Unity project folder</param>
        /// <returns>A List of manifest files, the List type is
        /// Google.VersionHandlerImpl.ManifestReferences</returns>
        private static List<VersionHandlerImpl.ManifestReferences>
            GetManifests(string[] filepaths) {
            VersionHandlerImpl.FileMetadataSet metadataSet =
                VersionHandlerImpl.FileMetadataSet.ParseFromFilenames(filepaths);

            List<VersionHandlerImpl.ManifestReferences> manifestFiles =
                VersionHandlerImpl.ManifestReferences.FindAndReadManifests(metadataSet);

            return manifestFiles;
        }

        /// <summary>
        /// Delete a selection of installed Unity packages
        /// </summary>
        /// <param name="markedPackages">A HashSet of filepaths of the packages to be
        /// uninstalled</param>
        private static void DeletePackages(HashSet<string> markedPackages) {
            // Get list of installed packages
            List<KeyValuePair<string, string>> packageList =
                GetPackageList(VersionHandlerImpl.FindAllAssets());

            // Track dependent assets
            Dictionary<string, int> manifestAssets = new Dictionary<string, int>();

            // Count each manifest file as a self-dependent asset
            foreach (KeyValuePair<string, string> package in packageList) {

                // Do not track assets to be deleted for cross dependencies
                bool skipMarked = false;
                foreach (string markedPackage in markedPackages) {
                    if (markedPackage == package.Key) {
                        skipMarked = true;
                        break;
                    }
                }
                if (skipMarked) {
                    continue;
                }

                // Track unmarked packages, for preservation
                if (manifestAssets.ContainsKey(package.Key)) {
                    manifestAssets[package.Key] += 1;
                }
                else {
                    manifestAssets.Add(package.Key, 1);
                }

                // Track unmarked assets, for preservation
                List<VersionHandlerImpl.ManifestReferences> manifestList =
                    GetManifests(new string[] {package.Key});
                foreach (VersionHandlerImpl.ManifestReferences manifest in manifestList) {
                    foreach (string asset in manifest.currentFiles) {
                        if (manifestAssets.ContainsKey(asset)) {
                            manifestAssets[asset] += 1;
                        }
                        else {
                            manifestAssets.Add(asset, 1);
                        }
                    }
                }
            }

            // Delete marked packages
            string deletedAssets, preservedAssets;
            DeleteIndependentAssets(markedPackages,
                                    manifestAssets,
                                    out deletedAssets,
                                    out preservedAssets);

            Debug.Log("Uninstalled packages\n\n" +
                      "The following assets were deleted:\n\n" +
                      deletedAssets + "\n\n" +
                      "Packages were deleted successfully\n\n" +
                      "NOTE: The following shared dependency assets were preserved:\n\n" +
                      preservedAssets);
        }

        /// <summary>
        /// Delete assets with no breakable dependencies
        /// manifest
        /// </summary>
        /// <param name="markedPackages">A HashSet of filepaths of the packages to be
        /// uninstalled</param>
        /// <param name="manifestAssets">A Dictionary of assets across all manifest files.
        /// The dictionary key is the asset file path, the value is the requency of the
        /// asset's occurence. An asset with more than one occurrence is a breakable
        /// dependency</param>
        /// <param name="deletedAssets">A string of the filepaths of the deleted assets,
        /// separated by the new line character. This parameter is passed by reference by
        /// its caller</param>
        /// <param name="preservedAssets">A string of the filepaths of the preserved assets,
        /// separated by the new line character. This parameter is passed by reference by
        /// its caller</param>
        private static void DeleteIndependentAssets(HashSet<string> markedPackages,
                                                    Dictionary<string, int> manifestAssets,
                                                    out string deletedAssets,
                                                    out string preservedAssets) {
            deletedAssets = null;
            preservedAssets = null;

            // Iterate through manifestAssets
            // Delete assets with 0 occurrences
            // >= 1 occurrence means there's a cross-dependency
            string assetDirectory = null;
            foreach (string markedPackage in markedPackages) {
                List<VersionHandlerImpl.ManifestReferences> manifestList =
                    GetManifests(new string[] { markedPackage });
                foreach (VersionHandlerImpl.ManifestReferences manifest in manifestList) {

                    // Only delete manifest file if it has no dependencies
                    manifest.currentFiles.Add(markedPackage);

                    // Delete assets in the package marked for uninstall
                    foreach (string asset in manifest.currentFiles) {
                        if (!File.Exists(asset)) {
                            continue; // File already deleted
                        }
                        if (manifestAssets.ContainsKey(asset)) {
                            preservedAssets += asset + '\n';
                        }
                        else {
                            assetDirectory = Path.GetDirectoryName(asset);
                            AssetDatabase.MoveAssetToTrash(asset);
                            deletedAssets += '\n' + asset;
                            DeleteDirectory(assetDirectory);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively deletes an empty directory and subdirectories
        /// </summary>
        /// <param name="packagePath">The directory path of a Unity asset</param>
        private static void DeleteDirectory(string packagePath) {
            string[] subDirectories = Directory.GetDirectories(packagePath);
            foreach (string subDirectory in subDirectories) {
                DeleteDirectory(subDirectory);
            }

            // Delete a directory only if it is empty
            if (Directory.GetFileSystemEntries(packagePath).Length == 0) {
                AssetDatabase.MoveAssetToTrash(packagePath);
            }
        }
    }
} // namespace Google

// <copyright file="PackageUninstallWindow.cs" company="Google LLC">
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

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace Google {

/// <summary>
/// A unique class to create the multi-select window to uninstall packages managed by
/// VersionHandler.
/// </summary>
[InitializeOnLoad]
public class PackageUninstallWindow : MultiSelectWindow {
    // Hardcoded text for the window.
    private static string windowTitle = "Uninstall Managed Packages";
    private static string caption =
            "Select packages to uninstall.\n\n" +
            "NOTE: If the files in the package have been moved, VersionHandler cannot properly "+
            "remove moved files.";
    private static string applylable = "Uninstall Selected Packages";

    /// <summary>
    /// Show the window for the user to uninstall packages managed by Version Handler.
    /// </summary>
    [MenuItem("Assets/External Dependency Manager/Version Handler/Uninstall Managed Packages")]
    public static void UninstallPackage() {
        // Display MultiSelectWindow
        var window =MultiSelectWindow.CreateMultiSelectWindow<PackageUninstallWindow>(windowTitle);
        window.AvailableItems = GetSelectionList();
        window.Sort(1);
        window.Caption = caption;
        window.ApplyLabel = applylable;
        window.OnApply = () => {
            if (window.SelectedItems.Count > 0) {
                VersionHandlerImpl.ManifestReferences.DeletePackages(window.SelectedItems);
                if (window.SelectedItems.Count == window.AvailableItems.Count) {
                    VersionHandlerImpl.analytics.Report("uninstallpackagewindow/confirm/all",
                            "Confirm to Uninstall All Packages");
                } else {
                    VersionHandlerImpl.analytics.Report("uninstallpackagewindow/confirm/subset",
                            "Confirm to Uninstall a Subset of Packages");
                }
            }
        };
        window.OnCancel = () => {
            VersionHandlerImpl.analytics.Report("uninstallpackagewindow/cancel",
                    "Cancel to Uninstall Packages");
        };
        window.Show();
        VersionHandlerImpl.analytics.Report("uninstallpackagewindow/show",
                "Show Uninstall Package Window");
    }

    /// <summary>
    /// Get a List of packages for seleciton.
    /// </summary>
    /// <returns>A List of key-value pairs of canonical name to display name</returns>
    private static List<KeyValuePair<string, string>> GetSelectionList() {
        var manifests = VersionHandlerImpl.ManifestReferences.FindAndReadManifestsInAssetsFolder();
        List<KeyValuePair<string, string>> selections =
                new List<KeyValuePair<string, string>>();
        foreach (var pkg in manifests) {
            if (!String.IsNullOrEmpty(pkg.filenameCanonical) && pkg.metadataByVersion != null) {
                string filename = Path.GetFileNameWithoutExtension(pkg.filenameCanonical);
                var versions = new List<string>();
                foreach (var fileMetadata in pkg.metadataByVersion.Values) {
                    versions.Add(fileMetadata.versionString);
                }
                string displayName =
                        String.Format("{0} version: [{1}]", filename,
                                String.Join(", ", versions.ToArray()));
                selections.Add(
                        new KeyValuePair<string, string>(pkg.filenameCanonical, displayName));
            }
        }
        return selections;
    }
}
} // namespace Google

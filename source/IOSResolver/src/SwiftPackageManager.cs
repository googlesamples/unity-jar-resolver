// <copyright file="SwiftPackageManager.cs" company="Google Inc.">
// Copyright (C) 2022 Google Inc. All Rights Reserved.
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
#if UNITY_IOS

using Google.JarResolver;
using GooglePlayServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEditor;

namespace Google {
  /// <summary>
  /// Represents a single Swift package framework to be added to the project.
  /// This corresponds to the <swiftPackage> tag.
  /// </summary>
  internal class SwiftPackage {
    /// <summary>
    /// Name of the package framework. (e.g. "FirebaseAnalytics")
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Whether the framework should be weakly linked. Defaults to false.
    /// </summary>
    public bool Weak { get; set; }

    /// <summary>
    /// Comma-separated list of Cocoapods that this package replaces.
    /// (e.g. "Firebase/Analytics,Firebase/Core")
    /// </summary>
    public string ReplacesPod { get; set; }

    /// <summary>
    /// A reference back to the remote package this framework belongs to.
    /// </summary>
    public RemoteSwiftPackage RemotePackage { get; set; }
  }

  /// <summary>
  /// Represents a remote Swift package repository.
  /// This corresponds to the <remoteSwiftPackage> tag.
  /// </summary>
  internal class RemoteSwiftPackage {
    /// <summary>
    /// The git URL of the package repository.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// The version string for the package. (e.g. "9.4.0")
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Whether to use "upToNextMinor" versioning.
    /// </summary>
    public bool UpToNextMinor { get; set; }

    /// <summary>
    /// Whether to use "upToNextMajor" versioning.
    /// </summary>
    public bool UpToNextMajor { get; set; }

    /// <summary>
    /// List of the specific package frameworks defined within this remote package.
    /// </summary>
    public List<SwiftPackage> Packages { get; set; } = new List<SwiftPackage>();

    /// <summary>
    /// The file path where this package was defined.
    /// </summary>
    public string DefinedIn { get; set; }
  }

  /// <summary>
  /// Parses Swift Package Manager dependencies from *Dependencies.xml files.
  /// </summary>
  internal class SwiftPackageManager : XmlDependencies {

    /// <summary>
    /// List of packages that have been parsed.
    /// </summary>
    public List<RemoteSwiftPackage> SwiftPackages = new List<RemoteSwiftPackage>();

    public SwiftPackageManager() {
      dependencyType = "SPM dependencies";
    }

    /// <summary>
    /// Reads and parses the dependencies from a given XML file.
    /// </summary>
    protected override bool Read(string filename, Logger logger) {
      var packages = new List<RemoteSwiftPackage>();
      var trueStrings = new HashSet<string> { "true", "1" };

      try {
        XDocument doc = XDocument.Load(filename);
        foreach (var remotePackageElement in doc.Descendants("remoteSwiftPackage")) {
          var remotePackage = new RemoteSwiftPackage {
            Url = (string)remotePackageElement.Attribute("url"),
            Version = (string)remotePackageElement.Attribute("version"),
            UpToNextMinor = trueStrings.Contains(((string)remotePackageElement.Attribute("upToNextMinor") ?? "").ToLower()),
            UpToNextMajor = trueStrings.Contains(((string)remotePackageElement.Attribute("upToNextMajor") ?? "").ToLower()),
            DefinedIn = filename
          };

          if (string.IsNullOrEmpty(remotePackage.Url) || string.IsNullOrEmpty(remotePackage.Version)) {
            logger.Log(string.Format("Skipping remoteSwiftPackage in {0} due to missing 'url' or 'version' attribute.", filename), level: LogLevel.Warning);
            continue;
          }

          foreach (var packageElement in remotePackageElement.Elements("swiftPackage")) {
            var swiftPackage = new SwiftPackage {
              Name = (string)packageElement.Attribute("name"),
              Weak = trueStrings.Contains(((string)packageElement.Attribute("weak") ?? "").ToLower()),
              ReplacesPod = (string)packageElement.Attribute("replacesPod"),
              RemotePackage = remotePackage
            };

            if (string.IsNullOrEmpty(swiftPackage.Name)) {
              logger.Log(string.Format("Skipping swiftPackage in {0} due to missing 'name' attribute.", filename), level: LogLevel.Warning);
              continue;
            }
            remotePackage.Packages.Add(swiftPackage);
          }
          packages.Add(remotePackage);
        }
        SwiftPackages.AddRange(packages);
      } catch (System.Exception e) {
        logger.Log(string.Format("Error parsing Swift Package Manager dependencies from {0}: {1}", filename, e.ToString()), level: LogLevel.Error);
        return false;
      }
      return true;
    }

    /// <summary>
    /// Resolves the Swift Package Manager dependencies, handling conflicts.
    /// </summary>
    /// <param name="packages">The list of swift packages to resolve.</param>
    /// <param name="logger">A logger for reporting messages.</param>
    /// <returns>A list of resolved packages.</returns>
    internal static List<RemoteSwiftPackage> Resolve(List<RemoteSwiftPackage> packages, Logger logger) {
      var resolvedPackages = new Dictionary<string, RemoteSwiftPackage>();

      // Resolve remote package version conflicts. Highest version wins.
      foreach (var package in packages) {
        if (resolvedPackages.TryGetValue(package.Url, out var existingPackage)) {
          var existingVersion = new Version(existingPackage.Version);
          var newVersion = new Version(package.Version);

          if (newVersion > existingVersion) {
            logger.Log(string.Format(
              "SPM package version conflict for {0}. Using version {1} from {2} instead of {3} from {4}.",
              package.Url, package.Version, package.DefinedIn, existingPackage.Version, existingPackage.DefinedIn),
              level: LogLevel.Warning);
            package.Packages.AddRange(existingPackage.Packages);
            resolvedPackages[package.Url] = package;
          } else {
            existingPackage.Packages.AddRange(package.Packages);
          }
        } else {
          resolvedPackages[package.Url] = package;
        }
      }

      // Resolve inner swiftPackage conflicts.
      foreach (var remotePackage in resolvedPackages.Values) {
        var packageGroups = remotePackage.Packages.GroupBy(p => p.Name)
                                              .ToDictionary(g => g.Key, g => g.ToList());

        var finalPackages = new List<SwiftPackage>();
        foreach (var group in packageGroups) {
          if (group.Value.Count == 1) {
            finalPackages.Add(group.Value.First());
            continue;
          }

          var mergedPackage = new SwiftPackage {
            Name = group.Key,
            Weak = group.Value.All(p => p.Weak),
            ReplacesPod = string.Join(",", group.Value.Select(p => p.ReplacesPod)
                                                        .Where(rp => !string.IsNullOrEmpty(rp))
                                                        .SelectMany(rp => rp.Split(','))
                                                        .Select(p => p.Trim())
                                                        .Distinct()),
            RemotePackage = remotePackage
          };
          finalPackages.Add(mergedPackage);
        }
        remotePackage.Packages = finalPackages;
      }

      return resolvedPackages.Values.ToList();
    }

    /// <summary>
    /// Extracts the list of Cocoapods that are replaced by the given Swift packages.
    /// </summary>
    /// <param name="resolvedPackages">A list of resolved Swift packages.</param>
    /// <returns>A unique list of pod names to be removed.</returns>
    internal static HashSet<string> GetReplacedPods(List<RemoteSwiftPackage> resolvedPackages) {
      var replacedPods = new HashSet<string>();
      foreach (var package in resolvedPackages) {
        foreach (var swiftPackage in package.Packages) {
          if (!string.IsNullOrEmpty(swiftPackage.ReplacesPod)) {
            foreach (var pod in swiftPackage.ReplacesPod.Split(',')) {
              replacedPods.Add(pod.Trim());
            }
          }
        }
      }
      return replacedPods;
    }

    /// <summary>
    /// Modifies the Xcode project to add the Swift Package dependencies.
    /// </summary>
    /// <param name="resolvedPackages">The list of resolved packages to add.</param>
    /// <param name="projectPath">The path to the Xcode project.</param>
    /// <param name="logger">A logger for reporting messages.</param>
    internal static void AddPackagesToProject(List<RemoteSwiftPackage> resolvedPackages, string projectPath, Logger logger) {
      if (VersionHandler.GetUnityVersionMajorMinor() < 2021.3f) {
        logger.Log("Swift Package Manager integration is only supported in Unity 2021.3 and newer. Disabling.", level: LogLevel.Warning);
        return;
      }

      string pbxProjectPath = UnityEditor.iOS.Xcode.PBXProject.GetPBXProjectPath(projectPath);
      var project = new UnityEditor.iOS.Xcode.PBXProject();
      project.ReadFromFile(pbxProjectPath);

      string frameworkTargetGuid = project.GetUnityFrameworkTargetGuid();

      foreach (var remotePackage in resolvedPackages) {
        try {
          string methodName;
          if (remotePackage.UpToNextMajor) {
            methodName = "AddRemotePackageReferenceAtVersionUpToNextMajor";
          } else if (remotePackage.UpToNextMinor) {
            methodName = "AddRemotePackageReferenceAtVersionUpToNextMinor";
          } else {
            methodName = "AddRemotePackageReferenceAtVersion";
          }

          var packageGuid = VersionHandler.InvokeInstanceMethod(project, methodName, new object[] { remotePackage.Url, remotePackage.Version });
          logger.Log(string.Format("Added SPM package {0} version {1} to project.", remotePackage.Url, remotePackage.Version), level: LogLevel.Info);

          foreach (var swiftPackage in remotePackage.Packages) {
            VersionHandler.InvokeInstanceMethod(project, "AddRemotePackageFrameworkToProject", new object[] { frameworkTargetGuid, swiftPackage.Name, packageGuid, swiftPackage.Weak });
            logger.Log(string.Format("  - Added framework {0} to project.", swiftPackage.Name), level: LogLevel.Info);
          }
        } catch (Exception e) {
          logger.Log(string.Format("Failed to add Swift Package {0}. Error: {1}", remotePackage.Url, e.Message), level: LogLevel.Error);
        }
      }

      project.WriteToFile(pbxProjectPath);
    }
  }
}
#endif // UNITY_IOS

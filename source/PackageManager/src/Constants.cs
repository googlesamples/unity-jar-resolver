// <copyright file="Constants.cs" company="Google Inc.">
// Copyright (C) 2016 Google Inc. All Rights Reserved.
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
namespace Google.PackageManager {
    /// <summary>
    /// Collection of constants relating to the PackageManager.
    /// </summary>
    public class Constants {
        /// <summary>
        /// The name of the plugin package manifest file.
        /// </summary>
        public const string MANIFEST_FILE_NAME = "package-manifest.xml";
        /// <summary>
        /// The name of the plugin description file.
        /// </summary>
        public const string DESCRIPTION_FILE_NAME = "description.xml";
        /// <summary>
        /// A marker used to prefix asset strings.
        /// </summary>
        public const string GPM_LABEL_MARKER = "gpm";
        /// <summary>
        /// Key value used to get/set Unity editor user preferences for registry
        /// location data.
        /// </summary>
        public const string KEY_REGISTRIES = "gpm_registries";
        /// <summary>
        /// Key value for get/set Unity editor user preferences for location of
        /// the package manager download cache location.
        /// </summary>
        public const string KEY_DOWNLOAD_CACHE = "gpm_local_cache";
        /// <summary>
        /// The constant value of the download cache directory name.
        /// </summary>
        public const string GPM_CACHE_NAME = "GooglePackageManagerCache";

        // TODO(krispy): when final location is settled it should go here
        public const string DEFAULT_REGISTRY_LOCATION =
            "https://raw.githubusercontent.com/kuccello/gpm_test/master/registry.xml";
        /// <summary>
        /// The verbose package mananger logging key.
        /// </summary>
        public const string VERBOSE_PACKAGE_MANANGER_LOGGING_KEY = "gpm_verbose";
        /// <summary>
        /// The show install assets key.
        /// </summary>
        public const string SHOW_INSTALL_ASSETS_KEY = "gpm_showInstallAssets";
        /// <summary>
        /// The string key binder used in key string concatenation
        /// </summary>
        public const string STRING_KEY_BINDER = ":";
        /// <summary>
        /// The gpm label key used in labeling assets
        /// </summary>
        public const string GPM_LABEL_KEY = "key";
        /// <summary>
        /// The gpm label client used in labeling assets
        /// </summary>
        public const string GPM_LABEL_CLIENT = "client";
        /// <summary>
        /// The fetch timout threshold - no fetch for external data should take longer
        /// </summary>
        public const double FETCH_TIMOUT_THRESHOLD = 10.0d;
        /// <summary>
        /// The gpm deps xml postfix used to check for pacakge deps.
        /// </summary>
        public const string GPM_DEPS_XML_POSTFIX = "gpm.dep.xml";
        /// <summary>
        /// The android sdk root Unity editor preference key.
        /// </summary>
        public const string ANDROID_SDK_ROOT_PREF_KEY = "AndroidSdkRoot";
        /// <summary>
        /// The project settings key.
        /// </summary>
        public const string PROJECT_SETTINGS_KEY = "ProjectSettings";
        /// <summary>
        /// The project record filename stored above Assets
        /// </summary>
        public const string PROJECT_RECORD_FILENAME = "project.gpm.xml";
        /// <summary>
        /// The version unknown marker.
        /// </summary>
        public const string VERSION_UNKNOWN = "-";
    }
}

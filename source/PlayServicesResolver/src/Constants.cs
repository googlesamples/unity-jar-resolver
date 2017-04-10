// <copyright file="Constants.cs" company="Google Inc.">
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
namespace Google.PlayServicesResolver {
    /// <summary>
    /// Collection of constants relating to the PlayServicesResolver set of classes.
    /// </summary>
    public static class Constants {
        public const string ANDROID_PLUGIN_ASSET_DIRECTORY = "Assets/Plugins/Android";

        public const string SETTINGS_KEY_AUTO_RESOLUTION =
            "GooglePlayServices.AutoResolverEnabled";
        public const string SETTINGS_KEY_INSTALL_ANDROID_PACKAGES =
            "GooglePlayServices.AndroidPackageInstallationEnabled";
    }
}

// <copyright file="ResolutionRunner.cs" company="Google Inc.">
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

/// <summary>
/// This class provides an example of how to do the following from an automated build environment:
/// * Enable the version handler
/// * Execute Android dependency resolution
/// * Perform a custom build step
/// </summary>
public class ResolutionRunner {

    /// <summary>
    /// This method registers a set of methods to call when the Version Handler has enabled all
    /// plugins in a project.
    /// </summary>
    public static void EnableResolver() {
        Google.VersionHandler.UpdateCompleteMethods = new [] {
            ":ResolutionRunner:ResolverEnabled"
        };
        Google.VersionHandler.UpdateNow();
    }

    /// <summary>
    /// This method is called when Version Handler has enabled all managed plugins in a project.
    /// At this point it uses the Android Resolver (Google.JarResolver) component to perform
    /// Android dependency resolution and finally execute a custom build step.
    /// </summary>
    public static void ResolverEnabled() {
#if UNITY_ANDROID
        // Execute Android dependency resolution.
        // NOTE: This is executed using reflection as the Android Resolver may not be loaded
        // when this file is compiled by Unity.  In the case the Android Resolver is initially
        // disabled, the Version Handler enables the plugin when it initializes
        // (see EnableResolver).
        Google.VersionHandler.InvokeStaticMethod(
            Google.VersionHandler.FindClass("Google.JarResolver",
                                            "GooglePlayServices.PlayServicesResolver"),
            "Resolve", args: null,
            namedArgs: new System.Collections.Generic.Dictionary<string, object> { {
                    "resolutionComplete", BuildYourApplication },
            });
#else
        BuildYourApplication();
#endif  // UNITY_ANDROID
    }

    /// <summary>
    /// This method is called after the Android dependency resolution is complete.
    /// You should replace the implementation of this method to build / export your application.
    /// </summary>
    public static void BuildYourApplication() {
        UnityEngine.Debug.Log("Ready to build");
        // TODO: Perform your build steps here.

        // TODO: You may want to change the exit code of the application if your build fails.
        UnityEditor.EditorApplication.Exit(0);
    }
}

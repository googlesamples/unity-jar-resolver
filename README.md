# JarResolver-Readme
Google Play Services Jar Resolver Library for Unity

# Overview

This library is intended to be used by any Unity plugin that requires access to
Google play-services or Android support libraries on Android.  The goal is to
minimize the risk of having multiple or conflicting versions of client
libraries included in the Unity project.


With this library, a plugin declares the dependencies needed and these are
resolved by using the play-services and support repositories that are part of
the Android SDK.  These repositories are used to store .aar files in a Maven
(Gradle) compatible repository.

This library implements a subset of the resolution logic used by these build
tools so the same functionality is available in Unity.

# Background
Many Unity plugins have dependencies on Google Play Services.
Dependencies on Google Play Services can cause version conflicts and
duplicate resource definitions.  In some cases, including the entire
Google Play Services client library makes it difficult to keep the number
of methods in your app (including framework APIs, library methods,
and your own code) under the 65,536 limit.

Android Studio addressed this starting with version 6.5 of Play Services.
Starting then, you can include the individual components of Play Services in
your project instead of the entire library.  This makes the project overhead
smaller.  The result is more resources for your application,
and maybe even a smaller application.

The unity-jar-resolver project brings this capability to Unity projects.
Each plugin or application declare the dependency needed e.g. play-services-games,
and the version 8.4+. Then the resolver library copies over the best
version of the play services libraries needed by all the plugins in the project.

To use this plugin, developers need to install the "Support Repository"
and the "Google Repository" in the Android SDK Manager.

Developers can clone this project from GitHub and include it in their project.
Plugin creators are encouraged to adopt this library as well, easing integration for their customers.

The list of the Play Services components on https://developers.google.com/android/guides/setup.


# Requirements

This library only works with Unity version 4.6.8 or higher.

The library relies on the installation of the Android Support Repository and
the Google Repository SDK components.  These are found in the "extras" section.

Building using Ubuntu

sudo apt-get install monodevelop nunit-console

# Packaging

The plugin consists of several C# DLLs that contain
 the logic to resolve the dependencies for both Android and iOS (using CocoaPods),
the logic to resolve dependencies and copy them into Unity projects, and logic
to remove older versions of the client libraries as well as older versions of the
JarResolver DLLs.

(Assets/Google Play Services/Resolve Client Jars).  In order to support
Unity version 4.x, this class also converts the aar file
to a java plugin project.  The second C# file is SampleDependencies.cs
which is the model for plugin developers to copy and add the specific
dependencies needed.

During resolution, all the dependencies from all the plugins are merged and resolved.

# Usage
  1. Add the unitypackage to your plugin project (assuming you are developing a
plugin).

  2. Copy the SampleDependencies.cs file to another name specific to your plugin
and add the dependencies your plugin needs.

Reflection is used to access the resolver in order to behave correctly when the
project is being loaded into Unity and there is no specific order of class
initialization.

For Android dependencies first create and instance of the resolver object:
```
// Setup the resolver using reflection as the module may not be
    // available at compile time.
    Type playServicesSupport = Google.VersionHandler.FindClass(
      "Google.JarResolver", "Google.JarResolver.PlayServicesSupport");
    if (playServicesSupport == null) {
      return;
    }
    svcSupport = svcSupport ?? Google.VersionHandler.InvokeStaticMethod(
      playServicesSupport, "CreateInstance",
      new object[] {
          "GooglePlayGames",
          EditorPrefs.GetString("AndroidSdkRoot"),
          "ProjectSettings"
      });
```

Then add dependencies. For example to depend on
play-services-games version 9.6.0, you need to specify the package, artifact,
and version as well as the packageId from the SDK manager in case a updated
version needs to be downloaded from the SDK Manager in order to build.
```
    Google.VersionHandler.InvokeInstanceMethod(
      svcSupport, "DependOn",
      new object[] {
      "com.google.android.gms",
      "play-services-games",
      "9.6.0" },
      namedArgs: new Dictionary<string, object>() {
          {"packageIds", new string[] { "extra-google-m2repository" } }
      });
```
The version value supports both specific versions such as 8.1.0,
and also the trailing '+' indicating "or greater" for
the portion of the number preceding the period.  For example 8.1.+ would match
8.1.2, but not 8.2.  The string "8+" would resolve to any version greater or
equal to 8.0. The meta version  'LATEST' is also supported meaning the greatest
version available, and "0+" indicates any version.

# Android manifest variable processing

Some aar files (notably play-services-measurement) contain variables that
are processed by the Android Gradle plugin.  Unfortunately, Unity does not perform
the same processing, so this plugin handles known cases of this variable substition
by exploding the aar and replacing ${applicationId} with the bundleID.


# iOS Dependency Management
iOS dependencies are identified using Cocoapods.  Cocoapods is run as a post build
process step.  The libraries are downloaded and injected into the XCode project
file directly, rather than creating a separate xcworkspace.

To add a dependency you first need an instance of the resolver.  Reflection is
used to safely handle race conditions when Unity is loading the project and the
order of class initialization is not known.
```
    Type iosResolver = Google.VersionHandler.FindClass(
  "Google.IOSResolver", "Google.IOSResolver");
    if (iosResolver == null) {
      return;
    }
```

Dependencies for iOS are added by referring to CocoaPods.  The libraries and
frameworks are added to the Unity project, so they will automatically be included.

This example add the GooglePlayGames pod, version 5.0 or greater,
disabling bitcode generation.

```
    Google.VersionHandler.InvokeStaticMethod(
      iosResolver, "AddPod",
      new object[] { "GooglePlayGames" },
      namedArgs: new Dictionary<string, object>() {
          { "version", "5.0+" },
          { "bitcodeEnabled", false },
      });
```

# Disabling automatic resolution

Automatic resolution can be disabled in the Settings dialog,
Assets > Google Play Services > Settings.

# How it works

When the dependency is added, the maven-metadata.xml file is read for this
dependency.  If there are no versions available (or the dependency is not
found), there is an exception thrown.  When the metadata is read, the list of
known versions is filtered based on the version constraint.  The remaining list
of version is known as <em>possible versions</em>.

The greatest value of the possible versions is known as the <em>best version</em>.
The best version is what is used to perform resolution.

Resolution is done by following the steps:

  1. All dependencies are added to the "unresolved" list.  Then for each dependency
in unresolved:
  2. check if there is already a candidate artifact
     1. if there is not, use the greatest version available (within the constraint) as
the candidate and remove from the unresolved list.
     2. If there is an existing candidate, check if the unresolved version is satisfied
by the candidate version.
        1. If it is, remove it from the unresolved list.
        2. If it is not, remove possible versions from the dependencies that have
non-concrete version constraints (i.e. have a + in the version).
        3. If there
        4. If there are still possible versions to check, add  the dependency to the end
of the unresolved list for re-processing with a new version candidate.
        5. If there are no possible versions, then the SDK Manager is used to download
and updated versions of the libraries based on the packageId.
        6. If there still are no possible versions to resolve both the candidate and the
unresolved dependencies, then either fail resolution with an exception, or use
the greatest version value.
     3. When a candidate version is selected, the pom file is read for that version and
the

     4. If there is a candidate version, add it to the candidate list and remove from
the unresolved.
  3. Process transitive dependencies
     5. for each candidate artifact, read the pom file for dependencies and add them to
the unresolved list.

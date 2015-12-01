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

# Requirements

This library only works with Unity version 4.6.8 or higher.

The library relies on the installation of the Android Support Repository and
the Google Repository SDK components.  These are found in the "extras" section.

# Packaging

The plugin consists of a C# DLL that contains the logic to resolve the dependencies
and the logic to resolve dependencies and copy them into Unity projects.
This includes removing older versions of the client libraries.

There also are 2 C# files.  The first, PlayServicesResolver.cs,
creates an AssetPostprocessor instance that is used to trigger background
resolution of the dependencies.  It also adds a menu item
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

```
instance.DependOn(group, artifact, version);
```

This declares that the this application now depends on the specified artifact.
Where:

  * group: indicates the artifact group, e.g. "com.android.support" or
"com.google.android.gms"
  * artifact: indicates the artifact, e.g. "appcompat-v7" or
"play-services-appinvite"
  * version: indicates the version.  The version value supports both specific
versions such as 8.1.0, and also the trailing '+' indicating "or greater" for
the portion of the number preceding the period.  For example 8.1.+ would match
8.1.2, but not 8.2.  The string "8+" would resolve to any version greater or
equal to 8.0. The meta version  'LATEST' is also supported meaning the greatest
version available, and "0+" indicates any version.

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
        5. If there are no possible versions to resolve both the candidate and the
unresolved dependencies, then either fail resolution with an exception, or use
the greatest version value (depending on the useLatest flag passed to resolve).
     3. When a candidate version is selected, the pom file is read for that version and
the

     4. If there is a candidate version, add it to the candidate list and remove from
the unresolved.
  3. Process transitive dependencies
     5. for each candidate artifact, read the pom file for dependencies and add them to
the unresolved list.


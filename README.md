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

This library only works with Unity version 5.0 or better.

The library relies on the installation of the Android Support Repository and
the Google Repository SDK components.  These are found in the "extras" section.

# Packaging

The library is packaged as a DLL which implements a Unity editor extension.
The library contains no runtime components.

There is sample code demonstrating how to declare dependencies and trigger
resolution both from the menu, and as a background process when assets change.

# Usage

  1. Copy the file [JarResolverLib.dll](Assets/Editor/JarResolver.dll) to the Assets/Editor folder in your Unity
project.

The dependency information is stored statically, so all plugins register their
dependency in a common location.  In the worse case, this results in multiple calls to
resolve the dependencies, but they all get the same resolution outcome.

  2. Create an instance of PlayServicesSupport.  Pass in a name for the 
client (needs to use valid filename characters), the path to the Android SDK,
and the path to the settings directory.  For Unity, this is "ProjectSettings".

```
PlayServicesSupport instance = PlayServicesSupport.CreateInstance("myPlugin",
   EditorPrefs.GetString("AndroidSdkRoot"),
    "ProjectSettings");
```

  3. Specify the dependencies for your plugin.  In your editor script, add a line
for each direct dependency:

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

When the dependency is added, the maven-metadata.xml file is read for this
dependency.  If there are no versions available (or the dependency is not
found), there is an exception thrown.  When the metadata is read, the list of
known versions is filtered based on the version constraint.  The remaining list
of version is known as <em>possible versions</em>. 

The greatest value of the possible versions is known as the <em>best version</em>.  The best version is what is used to perform resolution.

  2. Call Resolve.  In your editor script call:

```
PlayServicesSupport.ResolveDependencies(useLatest);
```

Performs resolution of the dependencies.  The parameter useLatest, if true,
causes the latest version of any dependency to be used in the case of a
conflict.  If this flag is false, the resolution will fail by throwing an
exception indicating the dependencies cannot be resolved.

The return value is a dictionary of dependencies needed.  The key is the
"versionless" key of the dependency, and the value is the Dependency object.

  3. Copy/Update the dependencies in your project.  Once ResolveDependencies
returns, you need to copy and update the dependencies in your project.  To do
this, call

```
PlayServicesSupport.CopyDependencies(

deps, "Assets/Plugins/Android", confirm);
```

Where:

  * <strong>deps</strong> is the dictionary returned from ResolveDependencies
  * <strong>"Assets/Plugins/Android" </strong>is the project relative path of where to copy the client libraries.
  * <strong>confirm</strong> is a delegate of type  PlayServicesSupport.OverwriteConfirmation which is
called when an a dependency already exists in the project.  The delegate should
return true to have the old dependency be overwritten by the new.

The signature of PlayServicesSupport.OverwriteConfirmation is

```
bool OverwriteConfirmation (Dependency oldDep, Dependency newDep)
```

# Calling resolution from the menu

If you want to make the resolution process manually started from the menu, you
can use this class as a starter.  It declares the dependencies statically (so
other library clients will resolve them as well), and adds a menu item to
perform resolution and copy the results.  See
[ManualResolution](Assets/Editor/ManualResolution.cs)

# Calling resolution in the background

If you want to make the resolution process automatically started when there is
a change to the assets, you can kick off the process by implementing an
AssetPostprocessor.  See

[BackgroundResolution](Assets/Editor/BackgroundResolution.cs)

# How Resolution works

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

# Code location and building instructions

There are 3 assemblies that are part of the solution. 

  1. The Unity Editor UI components
  2. The Jar resolver library
  3. The unit tests.

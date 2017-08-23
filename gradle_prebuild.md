# Gradle Prebuild

## Overview

Gradle Prebuild refers to both a
[Python script](source/PlayServicesResolver/Scripts/generate_gradle_prebuild.py)
and an optional feature of the Android Resolver in the
Play Services Resolver plugin. The Python script builds an Android package from
a set of dependencies and uses Proguard to strip unused symbols. The resulting
package can then be used as a dependency in another build (even with a closed
toolchain such as Unity).

The feature in the Android Resolver Unity plugin leverages the script to add
Proguard stripping support for Unity and replaces the default dependency
resolution and macro expansion. It can be enabled in the settings as part of the
[Android Resolver](source/PlayServicesResolver)
(in the `Assets -> Play Services Resolver -> Android Resolver -> Settings`).

The main benefits of using Gradle Prebuild with Unity Android builds are:

  * Enables Proguard stripping, which massively reduces the APK size and dex
    count for common Android dependencies.
  * Replaces custom code in the
    [Play Services Resolver plugin](source/PlayServicesResolver)
    for handling dependency resolution and macro substitution with a mature
    pipeline (Gradle) that already handles that much more gracefully.

#### Drawbacks

  * Because Gradle Prebuild uses a single combined dependency, the actual list
    of transitive dependencies is abstracted from the user, so it's not very
    clear what is being included in the build as a result.
  * If the developer chooses to build by exporting a Gradle project and building
    externally, using the Gradle Prebuild will make it less clear
    what dependencies are included without any other benefit; though it's an
    extra step, you could simply add Proguard options to the generated Gradle
    project to achieve the same affect.

## Using Gradle Prebuild in Unity

Gradle Prebuild comes with the Android Resolver plugin and can be enabled in
`Assets -> Play Services Resolver -> Android Resolver -> Settings` with the
"Prebuild With Gradle (Experimental)" setting.

This replaces the resolver and is triggered in place of the normal “Resolve
Dependencies” command or auto-resolution when any dependencies are modified,
such as bundle ID or ABI settings.

The Gradle Prebuild feature of the Android Resolver plugin leverages the
stand-alone tool
[generate_gradle_prebuild.py](source/PlayServicesResolver/Scripts/generate_gradle_prebuild.py)
to do the build work, by generating the necessary configuration files for the
script with settings determined from Unity and the Android package manager.
If auto-resolve is enabled, the plugin keeps track of when any of these inputs
change so that it can re-run the Gradle Prebuild script with updated
configuration settings and regenerate the output. The output package is copied
to `Assets/Plugins/Android/MergedDependencies` as an exploded package (in other
words, an extracted
[AAR Android Library package](https://developer.android.com/studio/projects/android-library.html#aar-contents))
that works in Unity version 4.7+.

If auto-resolve is not enabled, you can trigger resolution manually from the
menu option:
`Assets -> Play Services Resolver -> Android Resolver -> Resolve Client Jars`


## Pipeline

#### Android Resolver

The [Android Resolver](source/PlayServicesResolver) plugin is used the same
way to define the inputs whether or not GradlePrebuild is used. The plugin does
the same work either way for determining if any of the input files or dependent
Unity configuration has changed and initiating auto-resolution if necessary.

The plugin (Gradle Prebuild feature) directs the
[generate_gradle_prebuild.py](source/PlayServicesResolver/Scripts/generate_gradle_prebuild.py)
script to write the output to `Assets/Plugins/Android/MergedDependencies`,
which the plugin then also scrubs to remove unused ABI libraries.

#### generate_gradle_prebuild.py

See:
[generate_gradle_prebuild.py](source/PlayServicesResolver/Scripts/generate_gradle_prebuild.py)
for details on invoking the script directly.

The script performs the following actions:

  0. Extracts gradle-template.zip containing all of the files needed to execute
     a Gradle build. Some files contain variables intended to be replaced.
  0. Uses arguments passed on the command line to fill in values in the template
     files (ie. build.gradle).
  0. Executes the generated Gradle build using the
     transformClassesAndResourcesWithProguard target.
  0. The intermediate files are copied to the output using
     [volatile_paths.json](source/PlayServicesResolver/scripts/volatile_paths.json)
     which defines a mapping of intermediate generated files to output files for
     the package. Since the Android build tools version is backwards compatible,
     the plugin uses a fixed version that's been tested and is maintained along
     with volatile_paths.json to the highest version available. This guarantees
     that the generated intermediate files are stable.

## Gotchas

#### Tool version matching

The most brittle part of the plugin is that Unity does not provide an API to
get the version of the Android tools that Unity's internal build will use. In
the past, it was possible to use reflection to get the Android tools version
using private Unity APIs to get these values, but now the Android
Package Manager is queried directly, which means if Unity restricts or caps the
versions of the tools it uses it can create tool discrepancies.

For example, in general, Unity will try to detect the latest Android platform
available on the machine. However, if Unity is hard-coded to only work up to
version 25 (Nougat), it may simply use the highest version available <= 25.

It's difficult for the Android Resolver plugin to determine what Unity will do,
and this behavior also varies with Unity versions, so currently we use the
latest version available through the Android SDK Package Manager.


## Future Improvements

One issue with the plugin is that all dependencies are rolled together into a
single opaque dependency so it's hard to see what all of the transitive
dependencies are. A simple solution to this issue is to have the plugin generate
a text file with the list of dependencies included to make it possible for the
developer to see what's included.


Another problem is that the Gradle build configuration, managed by the Gradle
Prebuild Unity plugin, is internal to the plugin. To make this more flexible,
there should be a method for users to tweak the configuration used in the
Prebuild.

Users may already be trying to get more flexibility with the build pipeline by
exporting a Gradle project from Unity. However, the Gradle build configurations
exported from Unity only reference packages present in the Unity project and do
not specify dependency declarations to be resolved by Gradle. To help make this
process better, the plugin could attempt to post-process Gradle build
configurations exported by Unity and inject the dependency declarations as
Gradle includes, without having to copy all of the dependency packages into the
project.


## Debugging

You can turn on verbose logging in Unity (in the
`Assets -> Play Services Resolver -> Android Resolver -> Settings` via the
"Verbose Logging" setting) to see exactly how the script is invoked and the
arguments passed to it. The configuration file (`config.json`) built by Gradle
Prebuild in the [Play Services Resolver plugin](source/PlayServicesResolver)
is passed to the
[Gradle Prebuild script](source/PlayServicesResolver/Scripts/generate_gradle_prebuild.py)
with the list of dependencies and build tool versions to be used. The
configuration file can be found in the Project’s Temp folder. And the resulting
generated Gradle build can be inspected in the `Temp/GenGradle` folder.

In case of an error, it’s possible to manually re-invoke the Gradle build from
`Temp/GenGradle` using:

    ./gradlew :transformClassesAndResourcesWithProguard

**Note:** This is actually the target that
[generate_gradle_prebuild.py](source/PlayServicesResolver/Scripts/generate_gradle_prebuild.py)
uses. This intermediate build target causes all dependent build steps to be
executed in order, and is used instead of a full build because all of the
intermediate files that are needed for the new package are produced by this
step. Any futher work would be superfluous. Besides, none of the application
code is included here; it's a build entirely made from a list of dependencies,
so similar to building a library in C, there's no need to perform the link step.

If the error is reported by Proguard, it’s possible that one of your
dependencies has an incomplete Proguard configuration, and you can try editing
the Proguard file in the build directory to add Proguard declarations to isolate
the problem. Once you’ve got it working, you’ll need to patch the Proguard file
in the problematic AAR file.


## Windows

On Windows, Python isn’t guaranteed to be installed, so we use PyInstaller to
wrap the script in an executable. More details can be found on that here:
[readme_for_generate_gradle_prebuild_exe.md](source/PlayServicesResolver/scripts/readme_for_generate_gradle_prebuild_exe.md)

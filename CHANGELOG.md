# Version 1.2.47 - Aug 29, 2017
## New Features
* Android and iOS dependencies can now be specified using *Dependencies.xml
  files.  This is now the preferred method for registering dependencies,
  we may remove the API for dependency addition in future.
* Added "Reset to Defaults" button to each settings dialog to restore default
  settings.
* Android Resolver now validates the configured JDK is new enough to build
  recently released Android libraries.
## Bug Fixes
* Fixed a bug that caused dependencies with the "LATEST" version specification
  to be ignored when using the Gradle mode of the Android Resolver.
* Fixed a race condition when running Android Resolution.
* Fixed Android Resolver logging if a PlayServicesSupport instance is created
  with no logging enabled before the Android Resolver is initialized.
* Fixed iOS resolver dialog in Unity 4.
* Fixed iOS Cocoapod Xcode project integration in Unity 4.

# Version 1.2.46 - Aug 22, 2017
## Bug Fixes
* GradlePrebuild Android resolver on Windows now correctly locates dependent
  data files.

# Version 1.2.45 - Aug 22, 2017
## Bug Fixes
* Improved Android package auto-resolution and fixed clean up of stale
  dependencies when using Gradle dependency resolution.

# Version 1.2.44 - Aug 21, 2017
## Bug Fixes
* Enabled autoresolution for Gradle Prebuild.
* Made the command line dialog windows have selectable text.
* Fixed incorrect "Android Settings" dialog disabled groups.
* Updated PlayServicesResolver android platform detection to use the package
  manager instead of the 'android' tool.
* UnityCompat reflection methods 'GetAndroidPlatform' and
  'GetAndroidBuildToolsVersion' are now Obsolete due to dependence on the
  obsolete 'android' build tool.

# Version 1.2.43 - Aug 18, 2017
## Bug Fixes
* Fixed Gradle resolution in the Android Resolver when running
  PlayServicesResolver.Resolve() in parallel or spawning multiple
  resolutions before the previous resolve completed.

# Version 1.2.42 - Aug 17, 2017
## Bug Fixes
* Fixed Xcode project level settings not being applied by IOS Resolver when
  Xcode project pod integration is enabled.

# Version 1.2.41 - Aug 15, 2017
## Bug Fixes
* IOS Resolver's Xcode workspace pod integration is now disabled when Unity
  Cloud Build is detected.  Unity Cloud Build does not follow the same build
  process as the Unity editor and fails to open the generated xcworkspace at
  this time.

# Version 1.2.40 - Aug 15, 2017
## Bug Fixes
* Moved Android Resolver Gradle Prebuild scripts into Google.JarResolver.dll.
  They are now extracted from the DLL when required.
* AARs / JARs are now cleaned up when switching the Android resolution
  strategy.

# Version 1.2.39 - Aug 10, 2017
## New Features
* Android Resolver now supports resolution with Gradle.  This enables support
  for non-local artifacts.
## Bug Fixes
* Android Resolver's Gradle Prebuild now uses Android build tools to determine
  the Android platform tools version rather than relying upon internal Unity
  APIs.
* Android Resolver's Gradle Prebuild now correctly strips binaries that are
  not required for the target ABI.

# Version 1.2.38 - Aug 7, 2017
## Bug Fixes
* Fixed an issue in VersionHandler where disabled targets are ignored if
  the "Any Platform" flag is set on a plugin DLL.

# Version 1.2.37 - Aug 3, 2017
## New Features
* Exposed GooglePlayServices.PlayServicesResolver.Resolve() so that it's
  possible for a script to be notified when AAR / Jar resolution is complete.
  This makes it easier to setup a project to build from the command line.

# Version 1.2.36 - Aug 3, 2017
## New Features
* VersionHandler.UpdateCompleteMethods allows a user to provide a list of
  methods to be called when VersionHandlerImpl has completed an update.
  This makes it easier to import a plugin and wait for VersionHandler to
  execute prior executing a build.

# Version 1.2.35 - Jul 28, 2017
## New Features
* VersionHandler will now rename Linux libraries so they can target Unity
  versions that require different file naming.  Libraries need to be labelled
  gvh_linuxlibname-${basename} in order to be considered for renaming.
  e.g gvh\_linuxlibname-MyLib will be named MyLib.so in Unity 5.5 and below and
  libMyLib.so in Unity 5.6 and above.

# Version 1.2.34 - Jul 28, 2017
## Bug Fixes
* Made VersionHandler bootstrap module more robust when calling static
  methods before the implementation DLL is loaded.

# Version 1.2.33 - Jul 27, 2017
## New Features
* Added a bootstrap module for VersionHandler so the implementation
  of the VersionHandler module can be versioned without resulting in
  a compile error when imported at different versions across multiple
  plugins.

# Version 1.2.32 - Jul 20, 2017
## New Features
* Added support for build target selection based upon .NET framework
  version in the VersionHandler.
  When appling either gvh\_dotnet-3.5 or gvh\_dotnet-4.5 labels to
  assets, the VersionHandler will only enable the asset for the
  specified set of build targets when the matching .NET framework version
  is selected in Unity 2017's project settings.  This allows assets
  to be provided in a plugin that need to differ based upon .NET version.

# Version 1.2.31 - Jul 5, 2017
## Bug Fixes
* Force expansion of AARs with native components when using Unity 2017
  with the internal build system.  In contrast to Unity 5.x, Unity 2017's
  internal build system does not include native libraries included in AARs.
  Forcing expansion of AARs with native components generates an
  Ant / Eclipse project for each AAR which is correctly included by Unity
  2017's internal build system.

# Version 1.2.30 - Jul 5, 2017
## Bug Fixes
* Fixed Cocoapods being installed when the build target isn't iOS.
* Added support for malformed AARs with missing classes.jar.

# Version 1.2.29 - Jun 16, 2017
## New Features
* Added support for the Android sdkmanager tool.

# Version 1.2.28 - Jun 8, 2017
## Bug Fixes
* Fixed non-shell command line execution (regression from
  Cocoapod installation patch).

# Version 1.2.27 - Jun 7, 2017
## Bug Fixes
* Added support for stdout / stderr redirection when executing
  commands in shell mode.
  This fixes CocoaPod tool installation when shell mode is
  enabled.
* Fixed incremental builds when additional sources are specified
  in the Podfile.

# Version 1.2.26 - Jun 7, 2017
## Bug Fixes
* Fixed a crash when importing Version Handler into Unity 4.7.x.

# Version 1.2.25 - Jun 7, 2017
## Bug Fixes
* Fixed an issue in the Jar Resolver which incorrectly notified
  event handlers of bundle ID changes when the currently selected
  (not active) build target changed in Unity 5.6 and above.

# Version 1.2.24 - Jun 6, 2017
## New Features
* Added option to control file renaming in Version Handler settings.
  Disabling file renaming (default option) significantly increases
  the speed of file version management operations with the downside
  that any files that are referenced directly by canonical filename
  rather than asset ID will no longer be valid.
* Improved logging in the Version Handler.
## Bug Fixes
* Fixed an issue in the Version Handler which caused it to not
  re-enable plugins when re-importing a custom package with disabled
  version managed files.

# Version 1.2.23 - May 26, 2017
## Bug Fixes
* Fixed a bug with gradle prebuild resolver on windows.

# Version 1.2.22 - May 19, 2017
## Bug Fixes
* Fixed a bug in the iOS resolver with incremental builds.
* Fixed misdetection of Cocoapods support with Unity beta 5.6.

# Version 1.2.21 - May 8, 2017
## Bug Fixes
* Fix for https://github.com/googlesamples/unity-jar-resolver/issues/48
  Android dependency version number parsing when "-alpha" (etc.) are
  included in dependency (AAR / JAR) versions.

# Version 1.2.20 - May 8, 2017
## Bug Fixes
* Attempted to fix
  https://github.com/googlesamples/unity-jar-resolver/issues/48
  where a NullReferenceException could occur if a target file does not
  have a valid version string.

# Version 1.2.19 - May 4, 2017
## Bug Fixes
* Fixed Jar Resolver exploding and deleting AAR files it isn't managing.

# Version 1.2.18 - May 4, 2017
## New Features
* Added support for preserving Unity pods such as when GVR is enabled.

# Version 1.2.17 - Apr 20, 2017
## Bug Fixes
* Fixed auto-resolution when an Android application ID is modified.

# Version 1.2.16 - Apr 17, 2017
## Bug Fixes
* Fixed Unity version number parsing on machines with a locale that uses
  "," for decimal points.
* Fixed null reference exception if JDK path isn't set.

# Version 1.2.15 - Mar 17, 2017
## New Features
* Added warning when the Jar Resolver's background resolution is disabled.
## Bug Fixes
* Fixed support of AARs with native libraries when using Gradle.
* Fixed extra repository paths when resolving dependencies.

# Version 1.2.14 - Mar 7, 2017
## New Features
* Added experimental Android resolution using Gradle.
  This alternative resolver supports proguard stripping with Unity's
  internal build system.
* Added Android support for single ABI builds when using AARs include
  native libraries.
* Disabled Android resolution on changes to all .cs and .js files.
  File patterns that are monitored for auto-resolution can be added
  using PlayServicesResolver.AddAutoResolutionFilePatterns().
* Added tracking of resolved AARs and JARs so they can be cleaned up
  if they're no longer referenced by a project.
* Added persistence of AAR / JAR version replacement for each Unity
  session.
* Added settings dialog to the iOS resolver.
* Integrated Cocoapod tool installation in the iOS resolver.
* Added option to run pod tool via the shell.
## Bug Fixes
* Fixed build of some source Cocoapods (e.g Protobuf).
* VersionHandler no longer prompts to delete obsolete manifests.
* iOS resolver handles Cocoapod installation when using Ruby < 2.2.2.
* Added workaround for package version selection when including
  Google Play Services on Android.
* Fixed support for pods that reference static libraries.
* Fixed support for resource-only pods.

# Version 1.2.12 - Feb 14, 2017
## Bug Fixes
* Fixed re-explosion of AARs when the bundle ID is modified.

# Version 1.2.11 - Jan 30, 2017
## New Features
* Added support for Android Studio builds.
* Added support for native (C/C++) shared libraries in AARs.

# Version 1.2.10 - Jan 11, 2017
## Bug Fixes
* Fixed SDK manager path retrieval.
* Also, report stderr when it's not possible to run the "pod" tool.
* Handle exceptions thrown by Unity.Cecil on asset rename
* Fixed IOSResolver to handle PlayerSettings.iOS.targetOSVersionString

# Version 1.2.9 - Dec 7, 2016
## Bug Fixes
* Improved error reporting when "pod repo update" fails.
* Added detection of xml format xcode projects generated by old Cocoapods
  installations.

# Version 1.2.8 - Dec 6, 2016
## Bug Fixes
* Increased speed of JarResolver resolution.
* Fixed JarResolver caches getting out of sync with requested dependencies
  by removing the caches.
* Fixed JarResolver explode cache always being rewritten even when no
  dependencies change.

# Version 1.2.7 - Dec 2, 2016
## Bug Fixes
* Fixed VersionHandler build errors with Unity 5.5, due to the constantly
  changing BuildTarget enum.
* Added support for Unity configured JDK Path rather than requiring
  JAVA_HOME to be set in the Jar Resolver.

# Version 1.2.6 - Nov 15, 2016
## Bug Fixes
* Fixed IOSResolver errors when iOS support is not installed.
* Added fallback to "pod" executable search which queries the Ruby Gems
  package manager for the binary install location.

# Version 1.2.5 - Nov 3, 2016
## Bug Fixes
* Added crude support for source only Cocoapods to the IOSResolver.

# Version 1.2.4 - Oct 27, 2016
## Bug Fixes
* Automated resolution of out of date pod repositories.

# Version 1.2.3 - Oct 25, 2016
## Bug Fixes
* Fixed exception when reporting conflicting depedencies.

# Version 1.2.2 - Oct 17, 2016
## Bug Fixes
* Fixed issue working with Unity 5.5
* Fixed issue with PlayServicesResolver corrupting other iOS dependencies.
* Updated build script to use Unity distributed tools for building.

# Version 1.2.1 - Jul 25, 2016
## Bug Fixes
* Removed 1.2 Resolver and hardcoded whitelist of AARs to expand.
* Improved error reporting when the "jar" executable can't be found.
* Removed the need to set JAVA_HOME if "jar" is in the user's path.
* Fixed spurious copying of partially matching AARs.
* Changed resolver to only copy / expand when source AARs change.
* Auto-resolution of dependencies is now performed when the Android
  build target is selected.

## New Features
* Expand AARs that contain manifests with variable expansion like
  ${applicationId}.
* Added optional logging in the JarResolverLib module.
* Integration with the Android SDK manager for dependencies that
  declare required Android SDK packages.

# Version 1.2.0 - May 11 2016
## Bug Fixes
* Handles resolving dependencies when the artifacts are split across 2 repos.
* #4 Misdetecting version for versions like 1.2-alpha.  These are now string
  compared if alphanumeric
* Removed resolver creation via reflection since it did not work all the time.
  Now a resolver needs to be loaded externally (which is existing behavior).

## New Features
* Expose PlayServicesResolver properties to allow for script access.
* Explodes firebase-common and firebase-measurement aar files to support
  ${applicationId} substitution.

# Version 1.1.1 - 25 Feb 2016
## Bug Fixes
* #1 Spaces in project path not handled when exploding Aar file.
* #2 Script compilation error: TypeLoadException.

# Version 1.1.0 - 5 Feb 2016
## New Features
* Adds friendly alert when JAVA_HOME is not set on Windows platforms.
* Adds flag for disabling background resolution.
* Expands play-services-measurement and replaces ${applicationId} with the
  bundle Id.

 ## Bug Fixes
* Fixes infinite loop of resolution triggered by resolution.

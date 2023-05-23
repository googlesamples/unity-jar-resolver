
# EDM4U Usage Troubleshooting Guide

---
## Table of contents
1.  [Resolver and Target Platform Build Debug Process](#introduction)
2.  [General Tips](#general_tips)
    1. [Enable Verbose Logging](#verbose_loggin)
    2. [Turn off auto-resolution on Android](#android_auto_resolution)
3. [Android](#android)
    1. [Fixing "Resolution Failed" errors](#resolution_failed)
    2. [Use Force Resolve if having issues with resolution](#force_resolve)
    3. [JDK, SDK, NDK, Gradle Issues (including locating)](#jdk_sdk_ndk)
    4. [Enable Custom Main Gradle Template, a.k.a. mainTemplate.gradle](#custom_main_gradle_template)
    5. [Enable Jetifier if you can](#jetifier)
4.  [iOS](#ios)
    1. [Investigate Cocoapods if iOS resolution fails](#cocoapods)
    2. [Prefer opening Xcode Workspace files in Xcode to opening Xcode Project Files](#xcode_workspace_files)
    3. [Win32 Errors when building Xcode Workspace/Project on Mac](#win32_errors)
    4. [Runtime Swift Issues](#swift)
---

# Resolver and Target Platform Build Debug Process<a id="introduction"></a>


The following is a roughly chronological process for debugging and exploring the use of EDM4U when building your game/app for a particular platform. The first section ("General Tips'') applies regardless of your target, while the remaining two have to do with which target you are building for (Android or or iOS).

Consider each step within a section in order: If you do not have an issue with the step, move on to the next; If you do have an issue, attempt the listed steps and proceed once the issue is cleared.

Throughout the process, additionally address and explore both warnings and error messages displayed in the Unity Editor console and/or device logs. Oftentimes, seemingly unrelated errors can cause issues and, even when they don't, they can hide bigger issues.

<div class="warning" style='padding:0.1em; background-color:#b6d7a8; color:#000000'>
<span>
<p><strong>Note:</strong> This guide assumes you have already tested and verified the expected functionality of your Unity game/app in the Editor when compiling for the  <a href="https://docs.unity3d.com/Manual/BuildSettings.html#:%7E:text=Select-,Switch%20Platforms,-.%0AIf%20Unity">target platform</a>.</p>
<p>If you have not done so yet, perform your tests and resolve any issues you find before returning to this process.</p>
</span>
</div>
<p><p><p>


# **General Tips**<a id="general_tips"></a>

If at any point you want or need more resolver or build information, consider enabling verbose logging and reading the log after trying to build again

### **Enable Verbose Logging**<a id="verbose_loggin"></a>

#### <ins>Android</ins>

Enable **Verbose Logging** in **Assets &gt; External Dependency Manager &gt; Android Resolver &gt; Settings**

#### <ins>iOS</ins>

Enable **Verbose Logging** in **Assets &gt; External Dependency Manager &gt; iOS Resolver &gt; Settings**

### **Turn off auto-resolution on Android**<a id="android_auto_resolution"></a>

When the auto-resolution feature is enabled, the Android resolver can trigger when assets are changed or when AppDomain is reloaded, which happens every time you press the Play button in the editor. Dependency resolution can be slow when the Unity project is big or when the resolver needs to download and patch many Android libraries. You can improve iteration time by disabling most of the auto-resolution features and manually triggering resolution instead.

* Manual resolution is available through  **Assets &gt; External Dependency Manager &gt; Android Resolver &gt; Resolve** and  **Assets &gt; External Dependency Manager &gt; Android Resolver &gt; Force Resolve** menu items.
* Turn off "Enable Auto-Resolution" in the settings menu to prevent resolution triggered by assets changes and AppDomain reloads.
* Turn off "Enable Resolution on Build" in the settings menu to speed up build time.

# **Android**<a id="android"></a>

### **Fixing "Resolution Failed" errors**<a id="resolution_failed"></a>

If EDM4U fails resolution, try the following sequentially, making sure to check whether resolution succeeded between each. If a section heading includes "if you can", perform the step unless you *know* you cannot or that there are issues with doing so in your project.

### **Use Force Resolve if having issues with resolution**<a id="force_resolve"></a>

* When trying to build, if you receive errors, try to resolve dependencies by clicking **Assets &gt; External Dependency Manager &gt; Android Resolver &gt; Resolve**
* If this fails, try **Assets &gt; External Dependency Manager &gt; Android Resolver &gt; Force Resolve.** While this is slower, it is more dependendable as it clears old intermediate data.

### **JDK, SDK, NDK, Gradle Issues (including locating)**<a id="jdk_sdk_ndk"></a>

* Reasons to do this:
    * If **Force Resolve** is failing and you have not done this yet, try this.
    * If you receive error logs about Unity being unable to locate the `JDK`, `Android SDK Tools` or `NDK`
    * This issue is mostly observed in Unity 2019 and 2020.
* What it does:
    * Toggling the external tool settings forces Unity to acknowledge them as it may not have loaded them properly.
* What to do:
    * Enter **Unity &gt; Preferences&gt; External Tools**
    * Toggle the `JDK`, `Android SDK`, `Android NDK` and `Gradle` **checkboxes** such that they have the opposite value of what they started with
    * Toggle them back to their original values
    * Try **Force Resolve** and/or building again

### **Enable Custom Main Gradle Template, a.k.a. mainTemplate.gradle**<a id="custom_main_gradle_template"></a>

* By default, EDM4U used [a custom Gradle script](https://github.com/googlesamples/unity-jar-resolver/blob/master/source/AndroidResolver/scripts/download_artifacts.gradle) to download Android libraries to the "Assets/Plugins/Android/" folder. This can be problematic in several cases:
    * When Unity adds some common Android libraries with specific versions to the Gradle by default, play-core or game-activity. This would very likely cause duplicate class errors during build time.
    * When multiple Unity plugins depend on the same Android libraries with a very different range of versions. The Gradle project generated by Unity can handle resolution far better than the custom script in EDM4U.
    * Downloading large amounts of Android libraries can be slow.
* If you do this and are on Unity 2019.3+ you *must* enable  [**Custom Gradle Properties Template**](https://docs.unity3d.com/Manual/class-PlayerSettingsAndroid.html#Publishing) to enable AndroidX and Jetifier, which are described in the next step.

### **Enable Jetifier if you can**<a id="jetifier"></a>

* Android 9 introduced a new set of support libraries (AndroidX) which use the same class name but under a different package name. If your project has dependencies (including transitive dependencies) on both AndroidX and the older Android Support Libraries, duplicated class errors in `com.google.android.support.*` and `com.google.androidx.*` will occur during build time.  [Jetifier](https://developer.android.com/tools/jetifier) is a tool to resolve such cases. In general, you should *enable Jetifier if your target Android version is 9+, or API level 28+*. 
* Android Resolver can configure Unity projects to enable Jetifier. This feature can be enabled by the **Use Jetifier** option in Android Resolver settings. When enabled,
    * Android Resolver uses Jetifier to patch every Android library it downloaded to the "Assets/Plugins/Android" folder.
    * When **Custom Main Gradle Template** is enabled, it injects scripts to enable AndroidX and Jetifier to this template, prior to Unity 2019.3. After Unity 2019.3, AndroidX and Jetifier can only be enabled in **Custom Gradle Properties Template**.
* When using Unity 2019.3 or above, it is recommended to enable **Custom Gradle Properties Template**, regardless if you are using **Custom Main Gradle Template** or not. 

# **iOS**<a id="ios"></a>

If EDM4U fails resolution, try the following sequentially, making sure to check whether resolution succeeded between each. 

### **Investigate Cocoapods if iOS resolution fails**<a id="cocoapods"></a>

* First of all make sure it's  [properly installed](https://guides.cocoapods.org/using/getting-started.html)
    * Verify that  [`pod install` and `pod update` run without errors](https://guides.cocoapods.org/using/pod-install-vs-update.html) in the folder where the Podfile is (usually the root folder of the Xcode project).
* Cocoapods text encoding issues  when building from Mac
    * Do this if you are building on a Mac and see the following in the cocoapods log
    `WARNING: CocoaPods requires your terminal to be using UTF-8 encoding.`
    * When building for iOS, Cocoapod installation may fail with an error about the language locale, or UTF-8 encoding. There are currently several different ways to work around the issue.
        * From the terminal, run `pod install` directly, and open the resulting `xcworkspace` file.
        * Downgrade the version of Cocoapods to 1.10.2. The issue exists only in version 1.11 and newer.
        * In your `~/.bash_profile` or equivalent, add `export LANG=en_US.UTF-8`

### **Prefer opening Xcode Workspace files in Xcode to opening Xcode Project Files**<a id="xcode_workspace_files"></a>

Try to  [build iOS builds from Xcode Workspaces](https://developer.apple.com/library/archive/featuredarticles/XcodeConcepts/Concept-Workspace.html) generated by Cocoapods rather than Xcode projects:

* Rationale:
    * Unity by default only generates `.xcodeproject` files. If EDM4U is in the project, it first generates Podfiles from all iOS dependencies specified in files named "Dependencies.xml" with a prefix (ex. "AppDependencies.xml") , then runs Cocoapods, which generates an `.xcworkspace` file
    * In this case, it is recommended to open the generated project by double-clicking on `.xcworkspace` instead of `.xcodeproject` since the former contains references to pods.
* If you are building in an environment you cannot open Xcode workspaces from (such as unity cloud build) then go into the **iOS resolver settings**, enter the dropdown **Cocoapods Integration** and select **Xcode project**

### **Win32 Errors when building Xcode Workspace/Project on Mac**<a id="win32_errors"></a>

If the Unity Editor's console displays  [build output that mentions win32 errors](https://issuetracker.unity3d.com/issues/webgl-builderror-constant-il2cpp-build-error-after-osx-12-dot-3-upgrade), upgrade to a more recent LTS version of Unity after 2020.3.40f1.

* While workarounds exist, upgrading is the fastest, most convenient and most reliable way to handle it.

### **Runtime Swift Issues**<a id="swift"></a>

If you run into an issue when trying to run the game with error logs that mention Swift, try the following:

* Turn on `Enable Swift Framework Support Workaround` in **Assets &gt; External Dependency Manager &gt; iOS Resolver &gt; Settings**.
    * Read  the description in the settings menu.
    * Make sure those changes are made to the generated Xcode project.

If you are still experiencing issues at this point, investigate whether troubleshooting the product that utilizes EDM4U works differently or better now. Consider filing an issue here or with the product you are employing that utilizes EDM4U.
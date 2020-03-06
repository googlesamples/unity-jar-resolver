namespace GooglePlayServices {
    using UnityEditor;
    using UnityEditor.Callbacks;
    using UnityEngine;
    internal class PlayServicesPreBuild {
        // Flag to ensure that we only warn once per build.
        private static bool HasWarned;

        /// <summary>
        ///     Add a pre-build hook to warn the user if they have disabled
        ///     the Android auto-resolution functionality and they are building.
        /// </summary>
        [PostProcessScene(0)]
        private static void WarnIfAutoResolveDisabled() {
            if (HasWarned ||
                EditorApplication.isPlaying ||
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                return;

            if (SettingsDialog.AutoResolutionDisabledWarning &&
                !(SettingsDialog.EnableAutoResolution || SettingsDialog.AutoResolveOnBuild)) {
                Debug.LogWarning("Warning: Auto-resolution of Android dependencies is disabled! " +
                                 "Ensure you have run the resolver manually." +
                                 "\n\nWith auto-resolution of Android dependencies disabled you " +
                                 "must manually resolve dependencies using the " +
                                 "\"Assets > External Dependency Manager > Android Resolver > " +
                                 "Resolve\" menu item.\n\nFailure to resolve Android " +
                                 "dependencies will result in an non-functional " +
                                 "application.\nTo enable auto-resolution, navigate to " +
                                 "\"Assets > External Dependency Manager > Android Resolver > " +
                                 "Settings\" and check \"Enable Auto-resolution\"");
            }

            HasWarned = true;
        }

        /// Once the build is complete, call back to this method and reset the
        /// Generated flag to set up for the next build.
        [PostProcessBuild(0)]
        private static void BuildComplete(BuildTarget target, string pathToBuiltProject) {
            HasWarned = false;
        }
    }
}

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

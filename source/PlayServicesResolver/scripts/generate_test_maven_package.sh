#!/bin/bash -eu
#
# Creates a test maven package.

declare -r MAVEN_PACKAGE_METADATA_VERSION_TEMPLATE="\
      <version>VERSION</version>"

declare -r MAVEN_PACKAGE_METADATA_TEMPLATE="\
<metadata>
  <groupId>GROUP</groupId>
  <artifactId>ARTIFACT</artifactId>
  <versioning>
    <release>RELEASE_VERSION</release>
    <versions>
VERSIONS
    </versions>
    <lastUpdated/>
  </versioning>
</metadata>
"

declare -r MAVEN_POM_DEPENDENCY_TEMPLATE="\
    <dependency>
      <groupId>GROUP</groupId>
      <artifactId>ARTIFACT</artifactId>
      <version>VERSION</version>
    </dependency>"

declare -r MAVEN_POM_TEMPLATE="\
<project xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"
         xmlns=\"http://maven.apache.org/POM/4.0.0\"
         xsi:schemaLocation=\"http://maven.apache.org/POM/4.0.0 \
http://maven.apache.org/xsd/maven-4.0.0.xsd\">
  <modelVersion>4.0.0</modelVersion>
  <groupId>GROUP</groupId>
  <artifactId>ARTIFACT</artifactId>
  <version>VERSION</version>
  <packaging>aar</packaging>
  <dependencies>
DEPENDENCIES
  </dependencies>
</project>
"

declare -r ANDROID_MANIFEST_TEMPLATE='\
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
          package="GROUP"
          android:versionCode="1"
          android:versionName="1.0">
  <uses-sdk android:minSdkVersion="14"/>
  <application>
    <meta-data android:name="GROUP:ARTIFACT" android:value="VERSION"/>
  </application>
</manifest>
'

main() {
  if [[ $# -ne 5 ]]; then
    echo "\
Usage: $(basename $0) repo group package version 'dependencies'

Generates a local test Maven repository artifact including Maven POM and
group metadata.  The generated artifact is a unique Android AAR.

repo:
  Local repository directory
group:
  Group for the artifact (e.g org.something.somethingelse)
package:
  Name of the package to create.
version:
  Version of the package to create.
dependencies:
  Space separated list of dependencies to write into the package POM in the
  form group:artifact:version where version is a Maven version expression.
" >&2
    exit 1
  fi

  local -r repo="${1}"
  local -r group="${2}"
  local -r artifact="${3}"
  local -r version="${4}"
  local -r dependencies=(${5})

  local -r package_dir="${repo}/${group//.//}/${artifact}"
  mkdir -p "${package_dir}"
  local -r artifact_dir="${package_dir}/${version}"
  mkdir -p "${artifact_dir}"

  local -r maven_metadata="${package_dir}/maven-metadata.xml"
  local versions_xml=""
  for current_version in \
    $(find "${package_dir}" -mindepth 1 -maxdepth 1 -type d | \
      sort -n | \
      sed "s@${package_dir}/@@"); do
    versions_xml="${versions_xml}\
$(echo "${MAVEN_PACKAGE_METADATA_VERSION_TEMPLATE/VERSION/${current_version}}")\\
"
  done
  echo "${MAVEN_PACKAGE_METADATA_TEMPLATE}" | \
    sed "s@GROUP@${group}@;\
         s@ARTIFACT@${artifact}@;\
         s@RELEASE_VERSION@${version}@;
         s@VERSIONS@${versions_xml}@;" > \
      "${maven_metadata}"

  local -r artifact_file_basename="${artifact_dir}/${artifact}-${version}"
  local -r pom="${artifact_file_basename}.pom"
  local dependency_xml=""
  if [[ ${#dependencies[@]} -gt 0 ]]; then
    for dependency in "${dependencies[@]}"; do
    local tokens=(${dependency//:/ })
    if [[ ${#tokens[@]} -ne 3 ]]; then
      echo "Ignoring invalid dependency ${dependency}" >&2
      continue
    fi
    local xml_block=$(\
        echo "${MAVEN_POM_DEPENDENCY_TEMPLATE}" | \
        sed "s@GROUP@${tokens[0]}@;\
               s@ARTIFACT@${tokens[1]}@;\
               s@VERSION@${tokens[2]}@;")
    dependency_xml="${dependency_xml}${xml_block}"
    done
  fi

  echo "${MAVEN_POM_TEMPLATE}" | \
    sed "s@GROUP@${group}@;\
         s@ARTIFACT@${artifact}@;\
         s@VERSION@${version}@;\
         s@DEPENDENCIES@$(echo "${dependency_xml}" | \
                          sed 's/$/\\/g')\@;" | \
    sed 's/\\$//' > "${pom}"

  local -r aar_tmp_dir="${artifact_dir}/tmp"
  mkdir -p "${aar_tmp_dir}"
  pushd "${aar_tmp_dir}" >/dev/null
  echo "${ANDROID_MANIFEST_TEMPLATE}" | \
    sed "s@GROUP@${group}@;
         s@ARTIFACT@${artifact}@;
         s@VERSION@${version}@;" > AndroidManifest.xml
  touch R.txt
  touch proguard.txt
  zip classes.jar R.txt
  zip -d classes.jar R.txt
  zip "../$(basename "${artifact_file_basename}.aar")" *
  popd >/dev/null
  rm -rf "${aar_tmp_dir}"
}

main "$@"

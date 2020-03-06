#!/bin/bash -eu
# Copyright (C) 2019 Google Inc. All Rights Reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
# http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

: ${GRADLE_USER_HOME:=${HOME}/.gradle}

usage() {
  echo "\
Usage: $(basename $0) output_maven_dir module1..moduleN

Copies the specified list of modules from the gradle cache to the
target directory.

For example:
$(basename $0) . androidx.annotation:annotation:1.0.0

would copy the module 'androidx.annotation:annotation:1.0.0' and POM to
the directory './androidx/annotation/annotation/1.0.0'
"
  exit 1
}

main() {
  [ $# -eq 0 ] && usage
  local cache=${GRADLE_USER_HOME}/caches/modules-2/files-2.1
  local output_dir="${1}"
  shift 1
  local modules="$@"
  for module in ${modules}; do
    local -a components=(${module//:/ })
    local group="${components[0]}"
    local group_dir="${group//./\/}"
    local artifact="${components[1]}"
    local version="${components[2]}"
    local source_dir="${cache}/${group}/${artifact}/${version}"
    local target_dir="${output_dir}/${group//.//}/${artifact}/${version}"
    # Gradle stores a hash of each file in the directory structure so use
    # a wildcard to ignore it when copying.
    mkdir -p "${target_dir}"
    find "${source_dir}" -type f | xargs -I@ cp "@" "${target_dir}"
  done
}

main "$@"

#!/bin/bash -exu

declare -r CURRENT_DIR="$(cd "$(dirname "$0")"; pwd)"
: ${REPO_DIR="$(cd "${CURRENT_DIR}/../../../"; pwd)"}
: ${SCRIPTS_DIR="$(cd "${REPO_DIR}/../../"; pwd)"}
: ${FILE_TO_PROCESS="${CURRENT_DIR}/readme.txt"}

help() {
  echo "\
Usage: $(basename $0)

Generate a maven repository from lines following the '==='
delimiter in ${FILE_TO_PROCESS}.

Each line consists of:
repo group artifact version dependencies

Each line is passed to generate_test_maven_package.sh as arguments
to create a test maven artifact.

This script can be configured using the following variables:
REPO_DIR:
  Root directory of the maven repository to modify.
  Defaults to ${ROOT_DIR}
SCRIPTS_DIR:
  Directory which contains the generate_test_maven_package.sh script.
  Defaults to ${SCRIPTS_DIR}
FILE_TO_PROCESS:
  File to process, defaults to ${FILE_TO_PROCESS}.
" >&2
  exit 1
}

main() {
  if [[ ! -e "${FILE_TO_PROCESS}" ]]; then
    echo "Unable to find ${FILE_TO_PROCESS} in the current directory" >&2
    help
  fi

  cd "${CURRENT_DIR}"
  find "${CURRENT_DIR}" -mindepth 1 -maxdepth 1 -type d | xargs rm -rf
  # Extract all non-blank lines from the file to process
  awk '{ if (p) { print $0 } } /^===/ { p = 1 }' "${FILE_TO_PROCESS}" | \
    grep -v '^$' | \
    sed 's@^@'"${SCRIPTS_DIR}"'/generate_test_maven_package.sh '"${REPO_DIR}"' @' \
    | /bin/bash -xeu
}

main

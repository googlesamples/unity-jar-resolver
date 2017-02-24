#!/usr/bin/python
# Copyright 2017 Google Inc. All Rights Reserved.
#
#  Licensed under the Apache License, Version 2.0 (the "License");
#  you may not use this file except in compliance with the License.
#  You may obtain a copy of the License at
#
#  http://www.apache.org/licenses/LICENSE-2.0
#
#  Unless required by applicable law or agreed to in writing, software
#  distributed under the License is distributed on an "AS IS" BASIS,
#  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
#  See the License for the specific language governing permissions and
#    limitations under the License.

"""Script to generate a merged Eclipse/Ant style package from AAR dependencies.

This script handles:
* Resolving AAR dependencies and macro expansion.
* Running proguard stripping.
* Generating a package for Eclipse / Ant build systems (e.g Unity) containing
  the final merged set of all dependencies.
"""

import argparse
import json
import os
import platform   # Needed to detect platform for gradle wrapper execution.
import re
import shutil
import subprocess
import sys
import zipfile

DEFAULT_BUILD_PATH = "GenGradle"
DEFAULT_OUTPUT_PATH = "MergedDeps"

# The TEMPLATE_ZIP and INTERMEDIATE_PATHS_CONFIG are assumed to be in the same
# place as this script. This may change in the future when adding support for
# users to have finer control over the gradle build.
TEMPLATE_ZIP = "gradle-template.zip"
INTERMEDIATE_PATHS_CONFIG = "volatile_paths.json"
SRCAAR_EXT = "srcaar"

MANIFEST_TEMPLATE = "src/main/AndroidManifest.xml"
LOCAL_PROPS_TEMPLATE = "local.properties"
BUILD_TEMPLATE = "build.gradle"

M2REPO_LOCAL_COPY = "m2repository"
PROGUARD_USER_FILE = "proguard-user.txt"


def merge_dir_tree(src, dst, ignore=None):
  """Merges a copy of the source directory tree with a destination.

  shutil.copytree assumes the destination doesn't exist or it will overwrite the
    whole directory. This achieves the same effect except preserves files in
    the destination that aren't in the source.

  This function does not handle sym links or other similar OS edge cases.

  Args:
    src: Provides the path to copy from.
    dst: Provides the path to merge into.
    ignore: same as shutil.copytree's ignore argument, works with
      shutil.ignore_patterns factory function.
  """
  paths = os.listdir(src)

  ignored_names = set()
  if ignore:
    ignored_names = ignore(src, paths)

  for item in os.listdir(src):
    if item in ignored_names:
      continue
    s = os.path.join(src, item)
    d = os.path.join(dst, item)
    if os.path.isdir(s):
      if not os.path.exists(d):
        os.makedirs(d)
      merge_dir_tree(s, d, ignore)
    else:
      # copy2 preserves the file attributes
      shutil.copy2(s, d)


class PatchGradleBuildTemplate(object):
  """Substitutes variables in the build.gradle with values from the config.

  In addition to the basic named variable substitution and constructing the
  dependency list, we also inject additional m2repositories if provided.

  Args:
    gradle_build_template: A string representing the file to be written with
      {named_var} style macro replacement.
    json_config: The json object representing the loaded configuration file
      passed in on the command line.
    m2path: A local merged copy of all m2repositories to be included in the
      build.

  Returns:
    A string representing the fully generated file contents.
  """

  def __init__(self, json_config, m2path=None):
    self.json_config = json_config
    self.local_m2_path = m2path

  def __call__(self, gradle_build_template):
    deps = self.json_config.get("project_deps")

    gradle_deps = "\n".join(["  compile '" + ":".join(d) + "'" for d in deps])
    # make a copy of the dict, so we can update it without side-effects
    template_vars = dict(self.json_config.get("config"))
    template_vars.update({
        "gradle_deps": "",
        "extra_maven_repos": "",
        "project_deps": gradle_deps,
        "plugins": ""
    })

    if self.local_m2_path:
      template_vars["extra_maven_repos"] = (
          "  maven {\n"
          "    url '%s'\n"
          "  }") % self.local_m2_path

    return str.format(gradle_build_template, **template_vars)


def _fix_package_names(repo_path):
  """Replaces all files in the repo_path ending in .srcaar with .aar."""
  names = os.listdir(repo_path)
  for name in names:
    pathname = os.path.join(repo_path, name)
    filename, file_extension = os.path.splitext(pathname)
    if os.path.isdir(pathname):
      _fix_package_names(pathname)
    else:
      if file_extension == os.path.extsep + SRCAAR_EXT:
        os.rename(pathname, os.path.extsep.join((filename, "aar")))


def write_template_vars(template_path, cb):
  """Passes file contents through a callable and writes the result to the file.

  Modifies the template_path inplace with results from passing its content
  through the callable: cb.

  Args:
    template_path: The path to the file to be read, processed and then written.
    cb: A callable which takes a string (with the template file content) and
      returns the modified string that should be written to the file.
  """
  with open(template_path, "r") as in_file:
    template_content = in_file.read()
  with open(template_path, "w") as out_file:
    out_file.write(cb(template_content))


def generate_gradle_build(build_path, json_config):
  """Generates a gradle project that can build from a set of dependencies.

  Args:
    build_path: The path to create the gradle build project. If it already
      exists, it will regenerate the project and preserve existing
      intermediates. This allows incremenetal builds.
    json_config: A json object containing:
      * config: A dictionary of string {var} macro replacements.
      * project_deps: An array of dependencies as
        [namespace, package, version] triplets.
      * m2paths: Optional array of extra m2repository search paths.
        The m2repositories are copied and support aars listed as .srcaar.
  """
  config = json_config.get("config")

  # Create the output folder.
  if not os.path.exists(build_path):
    os.makedirs(build_path)

  # Unzip the template files.
  template_zip_path = os.path.join(os.path.dirname(__file__), TEMPLATE_ZIP)
  zip_ref = zipfile.ZipFile(template_zip_path, "r")
  zip_ref.extractall(build_path)

  # Python's zipfile.extract doesn't preserve file mode, so we need to set the
  # attributes to preserve the execution mode of the gradle script.
  for f in zip_ref.infolist():
    path = os.path.join(build_path, f.filename)
    mode = f.external_attr >> 16 & 0xFFF
    os.chmod(path, mode)

  # Copy the m2 repositories locally, if there are any, and handle renaming
  # any packages with .srcaar extensions to .aar.
  m2paths = json_config.get("extra_m2repositories")
  dest_repo_path = ""
  if m2paths:
    # Build the local m2repository path, that we'll copy everything into.
    dest_repo_path = os.path.join(build_path, M2REPO_LOCAL_COPY)

    # Delete and recopy everything to prevent from accumulating stale packages.
    if os.path.exists(dest_repo_path):
      shutil.rmtree(dest_repo_path)
    for m2path in m2paths:
      merge_dir_tree(m2path, dest_repo_path,
                     ignore=shutil.ignore_patterns("*.meta"))

    # Replace any packages with .srcaar extensions with .aar.
    _fix_package_names(dest_repo_path)

  # Merge all passed in proguard configs into a USER config
  extra_proguard_configs = json_config.get("extra_proguard_configs")
  if extra_proguard_configs:
    dest_proguard_config = os.path.join(build_path, PROGUARD_USER_FILE)
    with open(dest_proguard_config, "a") as output_config:
      for proguard_config in extra_proguard_configs:
        with open(proguard_config, "r") as input_config:
          output_config.write(input_config.read())

  # Gradle doesn't seem to expand env vars in local.properties, so we'll just do
  # the expansion here.
  config["android_sdk_dir"] = os.path.expandvars(config["android_sdk_dir"])

  # This creates a handler for basic named variable substitution.
  replace_with_config_vars = lambda template: str.format(template, **config)
  patch_gradle_build_with_config = PatchGradleBuildTemplate(json_config,
                                                            M2REPO_LOCAL_COPY)

  # Replace the variables in the templates.
  write_template_vars(os.path.join(build_path, MANIFEST_TEMPLATE),
                      replace_with_config_vars)
  write_template_vars(os.path.join(build_path, LOCAL_PROPS_TEMPLATE),
                      replace_with_config_vars)
  write_template_vars(os.path.join(build_path, BUILD_TEMPLATE),
                      patch_gradle_build_with_config)


def execute_gradle_build(build_path):
  """Executes the gradle script at the build_path via the gradle wrapper."""
  gradle_script = "gradlew"
  if platform.system() == "Windows":
    gradle_script += ".bat"
  gradle_script = os.path.join(build_path, gradle_script)
  try:
    p = subprocess.Popen(
        [gradle_script, "transformClassesAndResourcesWithProguard"],
        cwd=build_path)
    p.communicate()
  except Exception, e:
    raise RuntimeError("Failed running gradle build", e)
  if p.returncode != 0:
    raise RuntimeError("Failed running gradle build")


def map_intermediates(search_path, search_re, dest_path, root=None):
  r"""Maps intermediate build files to output files.

  Recursively searches from search_path for files matching the search_re
  regular expression. It returns an array with each match as a tuple, mapping
  the path matched to the dest_path and filling in any match group references in
  dest_path. If the search_path references a file instead of a directory, the
  file is mapped to the dest_path and the regex is skipped.

  For example:
    search_path: "build/intermediates/exploded-aar",
    search_re: "(?:^|.*[/\\\\])jni[/\\\\](.*)",
    dest_path: "libs/\\1"

  This would search recursively from the exploded-aar directory and match any
  file where the path contains jni/<match group 1>  (where the folder must be
  exactly "jni"; it cannot just end in jni). The rest of the path in the match
  group is substituted in the dest path, so the resulting path will be:
  libs/<whatever was in match group 1>

  Let's say this file existed:
  "build/intermediates/exploded-aar/example/jni/armeabi-v7a/libAnalytics.so"

  The regex would be tested against: "example/jni/armeabi-v7a/libAnalytics.so"
  which would match with match group1 containing:
  "armeabi-v7a/libAnalytics.so"

  The dest_path would substitute the match group and result in:
  "libs/armeabi-v7a/libAnalytics.so"

  The resulting tuple in the returned array would be:
  ("example/path/jni/armeabi-v7a/libAnalytics.so",
   "libs/armeabi-v7a/libAnalytics.so")

  Args:
    search_path: The search path indicates where to start the recursive search
      to test the regex. The result mapping sources are relative to this path.
    search_re: The search_re is a regular expression that matches paths under
      the search path that should be mapped to the dest_path.
    dest_path: The dest_path is the location to put the matched source files and
      can reference capture groups in the search_re using \#, where # is the
      match group number.

  Returns:
    An array of tuples of mapping source paths to destinations. Source paths are
      relative to the search_path and dest paths to the output path.
  """
  if os.path.isfile(search_path):
    return [(search_path, dest_path)]

  if not root:
    root = search_path

  paths = []

  for name in os.listdir(search_path):
    pathname = os.path.join(search_path, name)
    # strip root from the pathname so we're only regex matching the parts
    # after the search path
    rel_path = os.path.relpath(pathname, root)
    if os.path.isdir(pathname):
      paths += map_intermediates(pathname, search_re, dest_path, root)
    else:
      # the passing of the regex with all possible path separators is really
      # confusing. So to make things simpler, we'll keep the regex path
      # delimeter as always /, and just modify the paths.
      path_for_re = rel_path.replace(os.path.sep, "/")
      if re.match(search_re, path_for_re):
        target_path = re.sub(search_re, dest_path, path_for_re)
        paths.append((rel_path, os.path.normpath(target_path)))
  return paths


def copy_outputs(build_path, output_path):
  """Copies build intermediates to the output path according to mapping rules.

  This relies on the volatile_paths.json to define what gets copied.
  See the comment in the json file for how the mapping is defined.

  Args:
    build_path: build_path is where the gradle build project is generated.
    output_path: The output_path is the location to copy the build artifacts to
      in eclipse format.
  """
  json_path_mapping_cfg_file = os.path.join(os.path.dirname(__file__),
                                            INTERMEDIATE_PATHS_CONFIG)
  json_path_mapping_cfg = None
  with open(json_path_mapping_cfg_file, "r") as input_file:
    json_path_mapping_cfg = json.loads(input_file.read())

  if "__comment__" in json_path_mapping_cfg:
    del json_path_mapping_cfg["__comment__"]

  if os.path.exists(output_path):
    shutil.rmtree(output_path)

  for _, mapping in json_path_mapping_cfg.iteritems():
    # Update the relative path in the search_path to a full path.
    mapping["search_path"] = os.path.join(build_path, mapping["search_path"])
    # **mapping matches args with dict: "search_path, search_re, and dest_path"
    paths = map_intermediates(**mapping)

    for pair in paths:
      from_path = os.path.join(mapping["search_path"], pair[0])
      to_path = os.path.join(output_path, pair[1])
      dest_dir = os.path.dirname(to_path)
      if not os.path.exists(dest_dir):
        os.makedirs(dest_dir)
      shutil.copy2(from_path, to_path)


CONFIG_HELP = """JSON Config file.
The root should be a dictionary containing two entries:
config:       Contains a dictionary of string mappings for variable replacement
              in the template files extracted from %s.
project_deps: Contains an array of dependencies as [namespace, package, version]
              triplets.

Example config file:
{
  "config": {
    "app_id": "whatever.you.want",
    "sdk_version": "25",
    "min_sdk_version": "14",
    "build_tools_version": "23.0.3",
    "android_sdk_dir": "/Library/Android/sdk"
  },
  "project_deps": [
    ["com.google.android.gms", "play-services-base", "10.+"],
    ["com.google.firebase", "firebase-analytics", "10.+"],
    ["com.google.firebase", "firebase-common", "10.+"]
  ]
}
""" % TEMPLATE_ZIP


def process_args():
  """Handles reading the command line arguments.

  This sets up an ArgumentParser, and reads the inputs.

  Raises:
    Exception: An exception is raised if any of the required json config entries
      are missing.

  Returns:
    A tuple with all of the loaded inputs containing:
      * json config - A json object loaded from the config file passed in.
      * build path - Full path for the build path passed in.
      * output path - Full path for the output path passed in.
  """
  parser = argparse.ArgumentParser(description=(
      "A script to generate a merged eclipse-style package from AAR "
      "dependencies by generating an android gradle build script and using it "
      "to generate build artifacts."))

  parser.add_argument("-c", help=CONFIG_HELP, metavar="FILE", required=True)
  parser.add_argument(
      "-b",
      help=("Override intermediate build path.\n"
            "The default path is: \"%s\", relative to the current working "
            "directory.") % DEFAULT_BUILD_PATH,
      metavar="PATH",
      required=False)
  parser.add_argument(
      "-o",
      help=("Override destination path.\n"
            "The default path is: \"%s\", relative to the current working "
            "directory.") % DEFAULT_OUTPUT_PATH,
      metavar="PATH",
      required=False)
  args = parser.parse_args()

  config_file = args.c

  if args.b:
    build_path = args.b
  else:
    build_path = DEFAULT_BUILD_PATH
  build_path = os.path.abspath(build_path)

  if args.o:
    output_path = args.o
  else:
    output_path = DEFAULT_OUTPUT_PATH
  output_path = os.path.abspath(output_path)

  # parse json config file
  with open(config_file, "r") as input_file:
    json_config_string = input_file.read()
  json_config = json.loads(json_config_string)

  exception_str = ("The \"%s\" section of the json config file is missing. See"
                   " sample/sampledeps.json.")
  required_sections = ["config", "project_deps"]
  for required_section in required_sections:
    if not json_config.get(required_section):
      raise Exception(exception_str % required_section)

  return (json_config, build_path, output_path)


def main():
  (jsobj, build_path, output_path) = process_args()
  generate_gradle_build(build_path, jsobj)
  execute_gradle_build(build_path)
  copy_outputs(build_path, output_path)

  return 0


if __name__ == "__main__":
  sys.exit(main())

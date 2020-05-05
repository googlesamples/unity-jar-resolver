#!/usr/bin/python
#
# Copyright 2020 Google LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

r"""A script to import a .unitypackage into a project without Unity.

Example usage:
  import_unity_package.py --projects=path/to/unity/project \
                          --packages=mypackage.unitypackage
"""

import os
import shutil
import tarfile
import tempfile
from absl import app
from absl import flags
from absl import logging

FLAGS = flags.FLAGS

flags.DEFINE_multi_string(
    "projects", None, "Paths to Unity project directories to unpack packages "
    "into. This should be the directory that contains the Assets directory, "
    "i.e my/project not my/project/Assets")
flags.DEFINE_multi_string(
    "packages", None, "Set of packages to unpack into a project. Packages are "
    "unpacked in the order they're specified.")


def files_exist(paths_to_check):
  """Determine whether the specified files exist.

  Args:
    paths_to_check: List of files to check whether they exist.

  Returns:
    List of files that do not exist.
  """
  return [p for p in paths_to_check if not os.path.isfile(os.path.realpath(p))]


def directories_exist(paths_to_check):
  """Determine whether the specified directories exist.

  Args:
    paths_to_check: List of directories to check whether they exist.

  Returns:
    List of directories that do not exist.
  """
  return [p for p in paths_to_check if not os.path.isdir(os.path.realpath(p))]


def unpack_to_directory(directory, packages):
  """Unpack a set of .unitypackage files to a directory.

  Args:
    directory: Directory to unpack into.
    packages: List of .unitypackage filesname to unpack.

  Returns:
    Dictionary containing a list of files that could not be extracted, keyed by
    package archive filename.
  """
  ignored_files_by_package = {}
  for unitypackage in packages:
    with tarfile.open(unitypackage) as unitypackage_file:
      member_names = unitypackage_file.getnames()
      guid_to_path = {}
      extracted_files = set()

      # Map each asset GUID to an extract path the path of each extracted asset.
      for filename in member_names:
        if os.path.basename(filename) == "pathname":
          guid = os.path.dirname(filename)
          with unitypackage_file.extractfile(filename) as pathname_file:
            pathname = pathname_file.read().decode("utf8").strip()
            if guid and pathname:
              extracted_files.add(filename)
              guid_to_path[guid] = pathname

      # Extract each asset to the appropriate path in the output directory.
      for filename in member_names:
        basename = os.path.basename(filename)
        if basename == "asset" or basename == "asset.meta":
          guid = os.path.dirname(filename)
          if guid:
            pathname = guid_to_path.get(guid)
            if pathname:
              with unitypackage_file.extractfile(filename) as member_file:
                extension = os.path.splitext(basename)[1]
                output_filename = os.path.join(directory, pathname + extension)
                os.makedirs(os.path.dirname(output_filename), exist_ok=True)
                with open(output_filename, "wb") as output_file:
                  shutil.copyfileobj(member_file, output_file)
                  extracted_files.add(filename)

      # Returns the list of files that could not be extracted in the archive's
      # order.
      ignored_files = []
      for member in member_names:
        if member not in extracted_files:
          if unitypackage_file.getmember(member).isfile():
            ignored_files.append(member)
      if ignored_files:
        ignored_files_by_package[unitypackage] = ignored_files
  return ignored_files_by_package


def main(unused_argv):
  """Unpacks a set of .unitypackage files into a set of Unity projects.

  Args:
    unused_argv: Not used.

  Returns:
    0 if successful, 1 otherwise.
  """
  # Make sure all input files and output directories exist.
  missing_packages = files_exist(FLAGS.packages)
  missing_projects = directories_exist(FLAGS.projects)
  if missing_packages:
    logging.error("Specified packages %s not found.", missing_packages)
  if missing_projects:
    logging.error("Specified projects %s not found.", missing_projects)
  if missing_packages or missing_projects:
    return 1

  with tempfile.TemporaryDirectory() as unpack_directory:
    # Unpack all packages into a single directory.
    for package, files in unpack_to_directory(unpack_directory, FLAGS.packages):
      logging.error("Failed to unpack files %s from package %s", package, files)

    # Copy unpacked packages into each project.
    for project in FLAGS.projects:
      for dirname, _, filenames in os.walk(unpack_directory):
        for filename in filenames:
          source_filename = os.path.join(dirname, filename)
          relative_filename = source_filename[len(unpack_directory) + 1:]
          if os.path.isfile(source_filename):
            target_filename = os.path.join(project, relative_filename)
            os.makedirs(os.path.dirname(target_filename), exist_ok=True)
            shutil.copyfile(source_filename, target_filename)
  return 0


if __name__ == "__main__":
  flags.mark_flag_as_required("projects")
  flags.mark_flag_as_required("packages")
  app.run(main)

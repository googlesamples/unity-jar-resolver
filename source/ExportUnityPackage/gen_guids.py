#!/usr/bin/python
#
# Copyright 2016 Google LLC
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

r"""This script generates stable guids for Unity asset paths.

Since the command for this is typically generated from a blaze script, the paths
passed in are typically relative to the root google3 dir. So we'll use the
fact that this script is in google3 somewhere to find the root and fix the
paths to be fully qualified.

The json file is expected to already exist as a precaution to avoid accidentally
creating a different one in the wrong location. The guids json file has a simple
format:
{
  "<version>": {
    "<asset path>": "<guid>",
    ...
  },
  ...
}

Example usage (output from blaze):

python firebase/app/client/unity/gen_guids.py \
  --guids_file="firebase/app/client/unity/unity_packer/guids.json" \
  --version="1.0.0" \
  "Firebase/Plugins/Firebase.Database.dll" \
  "Firebase/Plugins/Firebase.Database.Unity.dll"

"""

import json
import os
import re
import uuid

from absl import app
from absl import flags
from absl import logging
import distutils.version

FLAGS = flags.FLAGS

flags.DEFINE_string("guids_file", None,
                    "Path to the guids.json file to modify.")
flags.DEFINE_string("version", "1.0.0",
                    "The plugin version the GUID will be created for.")
flags.DEFINE_boolean("generate_new_guids", False,
                     "Whether to generate new GUIDs for command line "
                     "specified files (arguments following the flags). "
                     "If this is disabled, GUIDs will only be generated for "
                     "files that are not referenced by any package version.")

GUID_REGEX = re.compile(r"^[0-9a-fA-F]{32}$")


def validate_guid_data(guid_data):
  """Validate the specified matches the expected format for plugin asset GUIDs.

  Args:
    guid_data: Dictionary in the following form to validate...
      {
        "<version>": {
          "<asset path>": "<guid>",
          ...
        },
        ...
      }
      where <version> is a plugin version, <asset path> is the path of the asset
      within a plugin and <guid> is the GUID assigned to the asset.

  Raises:
    ValueError: If the dictionary doesn't match the expected format.
  """
  # Validate the dictionary.
  for version, guids_by_asset_paths in guid_data.items():
    if not isinstance(guids_by_asset_paths, dict):
      raise ValueError("Version %s contains invalid GUID object %s" %
                       (version, guids_by_asset_paths))
    # version can be anything that can be converted to a string.
    for asset_path, guid in guids_by_asset_paths.items():
      if not GUID_REGEX.match(guid):
        raise ValueError("Version %s, asset path %s references invalid "
                         "GUID %s" % (version, asset_path, guid))


def read_guid_data(filename):
  """Read GUIDs from JSON file.

  Args:
    filename: File to read asset GUIDs from.

  Returns:
    Dictionary in the form expected by validate_guid_data().

  Raises:
    ValueError: If the JSON file doesn't match the expected format.
  """
  with open(filename, "r") as guids_file:
    guid_data = json.load(guids_file)
  validate_guid_data(guid_data)
  return guid_data


def remove_duplicate_guids(guid_data):
  """Remove duplicate GUIDs that are present for prior versions of a plugin.

  Args:
    guid_data: Dictionary in the form expected by validate_guid_data().
      This dictionary is modified in-place.
  """
  # Compress map by removing duplicate GUIDs.
  sorted_versions = sorted(guid_data, key=distutils.version.LooseVersion)
  for version_index, version in enumerate(sorted_versions):
    current_guids_by_filename = guid_data[version]
    for filename in list(current_guids_by_filename.keys()):
      current_guid = current_guids_by_filename[filename]
      # Iterate through all versions prior to the current version.
      for previous_version_index in range(version_index - 1, 0, -1):
        # If the previous version contains the current GUID, remove it from the
        # current map.
        previous_guids_by_filename = (
            guid_data[sorted_versions[previous_version_index]])
        if previous_guids_by_filename.get(filename) == current_guid:
          del current_guids_by_filename[filename]
          break


def get_guids_by_asset_paths_for_version(guid_data, version):
  """Get asset GUIDs by path for a plugin version.

  Args:
    guid_data: Data to query for asset GUIDs.  This should be a dictionary in
      the form expected by validate_guid_data().
    version: Version dictionary to find in guid_data.

  Returns:
    Dictionary that is referenced by the "version" section of the guid_data
    dictionary. The returned value can be mutated to extend the guid_data
    object.
  """
  guids_by_asset_paths = guid_data.get(version, {})
  guid_data[version] = guids_by_asset_paths
  return guids_by_asset_paths


def get_all_asset_paths(guid_data, version):
  """Get all asset paths and their newest associated GUIDs.

  Args:
    guid_data: Dictionary in the form expected by validate_guid_data().
    version: Maximum version to search for assets.

  Returns:
    Dictionary of asset path to GUID.
  """
  all_guids_by_asset_paths = {}
  # Aggregate guids for older versions of files.
  max_version = distutils.version.LooseVersion(version)
  for current_version in sorted(guid_data, key=distutils.version.LooseVersion,
                                reverse=True):
    # Skip all versions after the current version.
    if distutils.version.LooseVersion(current_version) > max_version:
      continue
    # Add all guids for files to the current version.
    guids_by_asset_paths = guid_data[current_version]
    for asset_path, guid in guids_by_asset_paths.items():
      if asset_path not in all_guids_by_asset_paths:
        all_guids_by_asset_paths[asset_path] = guid
  return all_guids_by_asset_paths


def generate_guids_for_asset_paths(guid_data, version, asset_paths,
                                   generate_new_guids):
  """Generate GUIDs for a set of asset paths.

  Args:
    guid_data: Dictionary in the form expected by validate_guid_data() to insert
      the GUIDs into.
    version: Plugin version the asset paths were introduced.
    asset_paths: Asset paths to generate GUIDs for.  These paths should be the
      location of the assets when imported in a Unity plugin.
    generate_new_guids: Whether to generate new GUIDs for files that have GUIDs
      in prior versions of the plugin.
  """
  all_guids_by_asset_paths = get_all_asset_paths(guid_data, version)
  guids_by_asset_paths = get_guids_by_asset_paths_for_version(guid_data,
                                                              version)
  for asset_path in set(asset_paths):
    new_guid = uuid.uuid4().hex
    if generate_new_guids:
      guids_by_asset_paths[asset_path] = new_guid
    elif asset_path not in all_guids_by_asset_paths:
      guids_by_asset_paths[asset_path] = (
          guids_by_asset_paths.get(asset_path, new_guid))


def write_guid_data(filename, guid_data):
  """Write a GUIDs JSON file.

  Args:
    filename: File to write data to.
    guid_data: Dictionary in the form expected by validate_guid_data() to write
      to the file.
  """
  output = json.dumps(guid_data, indent=4, sort_keys=True)
  with open(filename, "wt") as guids_file:
    for line in output.splitlines():
      guids_file.write(line.rstrip() + "\n")


def main(argv_paths):
  """Generates stable guids for Unity package assets.

  Since the command for this is typically generated from a blaze rule, the paths
  passed in are typically relative to the root google3 dir. So we'll use the
  fact that this script is in google3 somewhere to find the root and fix the
  paths to be fully qualified.

  Args:
    argv_paths: List of google3 relative paths to generate new guids for. The
    relative script path is included in index 0, and is ignored.

  Returns:
    The exit code status; 1 for error, 0 for success.
  """
  # if it's not a cwd relative path, check if it's a google3 relative path.
  guids_file_path = FLAGS.guids_file
  if not os.path.exists(guids_file_path):
    logging.error("Could not find the guids file (%s) to modify. If you wish "
                  "to start a new guids file at this path, please create the "
                  "empty file first.", FLAGS.guids_file)
    return 1

  guid_data = read_guid_data(guids_file_path)
  remove_duplicate_guids(guid_data)
  generate_guids_for_asset_paths(guid_data, FLAGS.version, set(argv_paths[1:]),
                                 FLAGS.generate_new_guids)
  write_guid_data(guids_file_path, guid_data)
  return 0


if __name__ == "__main__":
  flags.mark_flag_as_required("guids_file")
  app.run(main)

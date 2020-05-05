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

"""Tests for import_unity_package.py."""

import os
import shutil
import sys
from absl import flags
from absl.testing import absltest

# pylint: disable=C6204
# pylint: disable=W0403
sys.path.append(os.path.dirname(__file__))
import import_unity_package
# pylint: enable=C6204
# pylint: enable=W0403

FLAGS = flags.FLAGS

# Location of test data.
TEST_DATA_PATH = os.path.join(os.path.dirname(__file__), "test_data")

class ImportUnityPackageTest(absltest.TestCase):
  """Test import_unity_package.py."""

  def setUp(self):
    """Create a temporary directory."""
    self.temp_dir = os.path.join(FLAGS.test_tmpdir, "temp")
    os.makedirs(self.temp_dir)

  def tearDown(self):
    """Clean up the temporary directory."""
    shutil.rmtree(self.temp_dir)

  def test_files_exist(self):
    """Test file existance check."""
    non_existent_file = os.path.join(FLAGS.test_tmpdir, "foo/bar.txt")
    existent_file = os.path.join(FLAGS.test_tmpdir, "a_file.txt")
    with open(existent_file, "wt") as test_file:
      test_file.write("hello")
    self.assertEqual(
        import_unity_package.files_exist([existent_file, non_existent_file]),
        [non_existent_file])

  def test_directories_exist(self):
    """Test directory existence check."""
    non_existent_dir = os.path.join(FLAGS.test_tmpdir, "foo/bar")
    existent_dir = os.path.join(FLAGS.test_tmpdir, "an/available/dir")
    os.makedirs(existent_dir, exist_ok=True)
    self.assertEqual(
        import_unity_package.directories_exist([non_existent_dir,
                                                existent_dir]),
        [non_existent_dir])

  def read_contents_file(self, test_package_filename):
    """Read the contents file for the specified test package.

    Args:
      test_package_filename: File to read the expected contents of.

    Returns:
      Sorted list of filenames read from
      (test_package_filename + ".contents.txt").
    """
    contents = []
    with open(test_package_filename + ".contents.txt", "rt") as contents_file:
      return [l.strip() for l in contents_file.readlines() if l.strip()]

  def list_files_in_temp_dir(self):
    """List files in the temporary directory.

    Returns:
      Sorted list of files relative to the temporary directory.
    """
    files = []
    for dirpath, _, filenames in os.walk(self.temp_dir):
      for basename in list(filenames):
        filename = os.path.join(dirpath, basename)
        if os.path.isfile(filename):
          files.append(filename[len(self.temp_dir) + 1:])
    return sorted(files)

  def test_unpack_to_directory_valid_archive(self):
    """Unpack a valid unitypackage into a directory."""
    packages = [
        os.path.join(TEST_DATA_PATH,
                     "external-dependency-manager-1.2.144.unitypackage"),
        os.path.join(TEST_DATA_PATH,
                     "external-dependency-manager-1.2.153.unitypackage")
    ]
    self.assertEqual(import_unity_package.unpack_to_directory(self.temp_dir,
                                                              packages), {})

    expected_files = set(self.read_contents_file(packages[0]))
    expected_files = expected_files.union(self.read_contents_file(packages[1]))

    self.maxDiff = None
    self.assertEqual(self.list_files_in_temp_dir(),
                     sorted(expected_files))

  def test_unpack_to_directory_invalid_archive(self):
    """Unpack a broken unitypackage into a directory."""
    # This archive has been modified so that 9b7b6f84d4eb4f549252df73305e17c8
    # does not have a path.
    packages = [
        os.path.join(
            TEST_DATA_PATH,
            "external-dependency-manager-1.2.144-broken.unitypackage")
    ]
    self.assertEqual(
        import_unity_package.unpack_to_directory(self.temp_dir, packages),
        {packages[0]: [
            "9b7b6f84d4eb4f549252df73305e17c8/asset.meta",
            "9b7b6f84d4eb4f549252df73305e17c8/asset"]})


if __name__ == "__main__":
  absltest.main()


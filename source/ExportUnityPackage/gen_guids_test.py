# Copyright 2019 Google LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

"""Tests for gen_guids.py script."""

import copy
import json
import os
import shutil
import sys

from absl import flags
from absl.testing import absltest

# pylint: disable=C6204
# pylint: disable=W0403
sys.path.append(os.path.dirname(__file__))
import gen_guids
# pylint: enable=C6204
# pylint: enable=W0403

FLAGS = flags.FLAGS


TEST_JSON = """\
{
    "0.0.1": {
        "pea/nut": "7311924048bd457bac6d713576c952da"
    },
    "1.2.3": {
        "foo/bar": "ba9f9118207d46248936105077947525",
        "bish/bosh": "a308bff8c54a4c8d987dad79e96ed5e1"
    },
    "2.3.4": {
        "foo/bar": "ae88c0972b7448b5b36def1716f1d711",
        "a/new/file": "816270c2a2a348e59cb9b7b096a24f50"
    }
}
"""

# Dictionary representation of TEST_JSON
TEST_DICT = {
    "0.0.1": {
        "pea/nut": "7311924048bd457bac6d713576c952da",
    },
    "1.2.3": {
        "foo/bar": "ba9f9118207d46248936105077947525",
        "bish/bosh": "a308bff8c54a4c8d987dad79e96ed5e1"
    },
    "2.3.4": {
        "foo/bar": "ae88c0972b7448b5b36def1716f1d711",
        "a/new/file": "816270c2a2a348e59cb9b7b096a24f50"
    }
}


def delete_temporary_directory_contents():
  """Delete the contents of the temporary directory."""
  # If the temporary directory is populated, delete everything in there.
  directory = FLAGS.test_tmpdir
  for path in os.listdir(directory):
    full_path = os.path.join(directory, path)
    if os.path.isdir(full_path):
      shutil.rmtree(full_path)
    else:
      os.unlink(full_path)


class GenGuidsTest(absltest.TestCase):
  """Test the gen_guids module."""

  def tearDown(self):
    """Clean up the temporary directory."""
    delete_temporary_directory_contents()
    super(GenGuidsTest, self).tearDown()

  def test_validate_guid_data(self):
    """Ensure validate_guid_data() catches invalid JSON data."""
    gen_guids.validate_guid_data({})
    gen_guids.validate_guid_data({"1.2.3": {}})
    gen_guids.validate_guid_data(
        {"1.2.3": {"foo/bar": "0123456789abcdef0123456789abcdef"}})
    with self.assertRaises(ValueError):
      gen_guids.validate_guid_data({"1.2.3": {"foo/bar": "notaguid"}})

  def test_read_guid_data(self):
    """Read GUIDs from a JSON file."""
    guids_filename = os.path.join(FLAGS.test_tmpdir, "guids.json")
    with open(guids_filename, "w") as guids_file:
      guids_file.write(TEST_JSON)
    guids_data = gen_guids.read_guid_data(guids_filename)
    self.assertDictEqual(TEST_DICT, guids_data)

  def test_write_guid_data(self):
    """Write GUIDs to a JSON file."""
    guids_filename = os.path.join(FLAGS.test_tmpdir, "guids.json")
    gen_guids.write_guid_data(guids_filename, TEST_DICT)
    with open(guids_filename, "rt") as guids_file:
      guids_json = guids_file.read()
    self.assertDictEqual(TEST_DICT, json.loads(guids_json))

  def test_remove_duplicate_guids(self):
    """Ensure duplicate GUIDs are removed from a dictionary of asset GUIDs."""
    duplicate_data = copy.deepcopy(TEST_DICT)
    duplicate_data["2.3.4"]["bish/bosh"] = "a308bff8c54a4c8d987dad79e96ed5e1"
    gen_guids.remove_duplicate_guids(duplicate_data)
    self.assertDictEqual(TEST_DICT, duplicate_data)

  def test_get_guids_by_asset_paths_for_version(self):
    """Get GUIDs for each asset path for a specific version."""
    guids_by_asset_paths = gen_guids.get_guids_by_asset_paths_for_version(
        copy.deepcopy(TEST_DICT), "1.2.3")
    self.assertDictEqual({
        "foo/bar": "ba9f9118207d46248936105077947525",
        "bish/bosh": "a308bff8c54a4c8d987dad79e96ed5e1"
    }, guids_by_asset_paths)
    self.assertDictEqual({}, gen_guids.get_guids_by_asset_paths_for_version(
        copy.deepcopy(TEST_DICT), "100.0.0"))

  def test_get_all_asset_paths(self):
    """Get GUIDs for all assets up to the specified version."""
    guids_by_asset_paths = gen_guids.get_all_asset_paths(TEST_DICT, "1.2.3")
    self.assertDictEqual({
        "pea/nut": "7311924048bd457bac6d713576c952da",
        "foo/bar": "ba9f9118207d46248936105077947525",
        "bish/bosh": "a308bff8c54a4c8d987dad79e96ed5e1"
    }, guids_by_asset_paths)
    guids_by_asset_paths = gen_guids.get_all_asset_paths(TEST_DICT, "2.3.4")
    self.assertDictEqual({
        "pea/nut": "7311924048bd457bac6d713576c952da",
        "foo/bar": "ae88c0972b7448b5b36def1716f1d711",
        "bish/bosh": "a308bff8c54a4c8d987dad79e96ed5e1",
        "a/new/file": "816270c2a2a348e59cb9b7b096a24f50"
    }, guids_by_asset_paths)

  def test_generate_guids_for_asset_paths(self):
    """Generate GUIDs for a set of asset paths."""
    guid_data = copy.deepcopy(TEST_DICT)
    gen_guids.generate_guids_for_asset_paths(
        guid_data, "2.3.4", ("pea/nut", "another/file", "more/things"), False)
    self.assertEqual(TEST_DICT["0.0.1"]["pea/nut"],
                     guid_data["0.0.1"].get("pea/nut"))
    self.assertNotEmpty(guid_data["2.3.4"].get("another/file"))
    self.assertNotEmpty(guid_data["2.3.4"].get("more/things"))

    guid_data = copy.deepcopy(TEST_DICT)
    gen_guids.generate_guids_for_asset_paths(
        guid_data, "2.3.4", ("pea/nut", "another/file", "more/things"), True)
    self.assertNotEqual(TEST_DICT["0.0.1"]["pea/nut"],
                        guid_data["2.3.4"].get("pea/nut"))
    self.assertNotEmpty(guid_data["2.3.4"].get("pea/nut"))
    self.assertNotEmpty(guid_data["2.3.4"].get("another/file"))
    self.assertNotEmpty(guid_data["2.3.4"].get("more/things"))


if __name__ == "__main__":
  absltest.main()

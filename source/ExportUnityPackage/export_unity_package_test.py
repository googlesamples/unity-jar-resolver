#!/usr/bin/python
# Copyright 2018 Google LLC
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

"""Tests for Unity packager export_unity_package.py."""

import collections
import copy
import filecmp
import json
import os
import platform
import re
import shutil
import stat
import sys
import tarfile
import time
from absl import flags
from absl.testing import absltest

# pylint: disable=C6204
# pylint: disable=W0403
sys.path.append(os.path.dirname(__file__))
import export_unity_package
# pylint: enable=C6204
# pylint: enable=W0403

FLAGS = flags.FLAGS

# Location of test data.
TEST_DATA_PATH = os.path.join(os.path.dirname(__file__), "test_data")

try:
  unicode("")  # See whether unicode class is available (Python < 3)
except NameError:
  unicode = str  # pylint: disable=redefined-builtin,invalid-name


class DuplicateGuidsCheckerTest(absltest.TestCase):
  """Test the DuplicateGuidsChecker class."""

  def test_add_guid_and_path(self):
    checker = export_unity_package.DuplicateGuidsChecker()
    checker.add_guid_and_path("816270c2a2a348e59cb9b7b096a24f50",
                              "Firebase/Plugins/Firebase.Analytics.dll")
    checker.add_guid_and_path("7311924048bd457bac6d713576c952da",
                              "Firebase/Plugins/Firebase.App.dll")
    checker.add_guid_and_path("7311924048bd457bac6d713576c952da",
                              "Firebase/Plugins/Firebase.Auth.dll")
    self.assertEqual(
        set(["Firebase/Plugins/Firebase.Analytics.dll"]),
        checker._paths_by_guid.get("816270c2a2a348e59cb9b7b096a24f50"))
    self.assertEqual(
        set(["Firebase/Plugins/Firebase.App.dll",
             "Firebase/Plugins/Firebase.Auth.dll"]),
        checker._paths_by_guid.get("7311924048bd457bac6d713576c952da"))

  def test_check_for_duplicate_guids(self):
    """Ensure an exception is raised if multiple files are found per GUID."""
    checker = export_unity_package.DuplicateGuidsChecker()
    checker.add_guid_and_path("816270c2a2a348e59cb9b7b096a24f50",
                              "Firebase/Plugins/Firebase.Analytics.dll")
    checker.add_guid_and_path("7311924048bd457bac6d713576c952da",
                              "Firebase/Plugins/Firebase.App.dll")
    checker.add_guid_and_path("7311924048bd457bac6d713576c952da",
                              "Firebase/Plugins/Firebase.Auth.dll")
    with self.assertRaises(export_unity_package.DuplicateGuidsError) as (
        context):
      checker.check_for_duplicates()
    self.assertEqual(
        {"7311924048bd457bac6d713576c952da": set([
            "Firebase/Plugins/Firebase.App.dll",
            "Firebase/Plugins/Firebase.Auth.dll"])},
        context.exception.paths_by_guid)

  def test_check_for_duplicate_guids_no_duplicates(self):
    """Ensure an exception is not raised if there are no duplicate GUIDs."""
    checker = export_unity_package.DuplicateGuidsChecker()
    checker.add_guid_and_path("816270c2a2a348e59cb9b7b096a24f50",
                              "Firebase/Plugins/Firebase.Analytics.dll")
    checker.add_guid_and_path("7311924048bd457bac6d713576c952da",
                              "Firebase/Plugins/Firebase.App.dll")
    checker.add_guid_and_path("275bd6b96a28470986154b9a995e191c",
                              "Firebase/Plugins/Firebase.Auth.dll")
    checker.check_for_duplicates()


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


class SafeDictGetValueTest(absltest.TestCase):
  """Test reading from a dictionary with error checking."""

  def test_safe_dict_get_value_empty(self):
    """Get a value from an empty dictionary."""
    self.assertEqual(None, export_unity_package.safe_dict_get_value({}, "test"))

  def test_safe_dict_get_value_empty_with_default(self):
    """Get a value from an empty dictionary with a default value."""
    self.assertEqual("hello", export_unity_package.safe_dict_get_value(
        {}, "test", default_value="hello"))

  def test_safe_dict_get_value_any_class(self):
    """Get a value from a dictionary of any class."""
    self.assertEqual("hello", export_unity_package.safe_dict_get_value(
        {"test": "hello"}, "test"))
    self.assertEqual(["hello"], export_unity_package.safe_dict_get_value(
        {"test": ["hello"]}, "test"))

  def test_safe_dict_get_value_matching_class(self):
    """Get a value from a dictionary with a matching class."""
    self.assertEqual("hello", export_unity_package.safe_dict_get_value(
        {"test": "hello"}, "test", value_classes=[str]))
    self.assertEqual(u"hello", export_unity_package.safe_dict_get_value(
        {"test": u"hello"}, "test", value_classes=[str, unicode]))
    self.assertEqual(["hello"], export_unity_package.safe_dict_get_value(
        {"test": ["hello"]}, "test", value_classes=[list]))

  def test_safe_dict_get_value_matching_class_from_default(self):
    """Get a value from a dictionary with a matching class."""
    self.assertEqual("hello", export_unity_package.safe_dict_get_value(
        {"test": "hello"}, "test", default_value="goodbye"))
    self.assertEqual(u"hello", export_unity_package.safe_dict_get_value(
        {"test": u"hello"}, "test", default_value="goodbye"))
    self.assertEqual(["hello"], export_unity_package.safe_dict_get_value(
        {"test": ["hello"]}, "test", default_value=["goodbye"]))

  def test_safe_dict_get_value_mismatching_class(self):
    """Get a value from a dictionary with a mismatching class."""
    self.assertEqual(None, export_unity_package.safe_dict_get_value(
        {"bish": ["hello"]}, "bish", value_classes=[str]))
    self.assertEqual(None, export_unity_package.safe_dict_get_value(
        {"test": "hello"}, "test", value_classes=[list]))

  def test_safe_dict_get_value_mismatching_class_from_default(self):
    """Get a value from a dictionary with a mismatching class."""
    self.assertEqual("goodbye", export_unity_package.safe_dict_get_value(
        {"test": ["hello"]}, "test", default_value="goodbye"))
    self.assertEqual(["goodbye"], export_unity_package.safe_dict_get_value(
        {"test": "hello"}, "test", default_value=["goodbye"]))


class SafeDictSetValueTest(absltest.TestCase):
  """Test write to a dictionary with error checking."""

  def test_safe_dict_set_value_non_dict_type(self):
    """Set a value to a non-dict type."""
    # Set a value to None
    self.assertEqual(
        None, export_unity_package.safe_dict_set_value(None, "test", None))
    self.assertEqual(
        None, export_unity_package.safe_dict_set_value(None, "test", "value"))

    # Set a value to a list
    self.assertEqual([],
                     export_unity_package.safe_dict_set_value([], "test", None))
    self.assertEqual([],
                     export_unity_package.safe_dict_set_value([], "test",
                                                              "value"))

    # Set a value to an integer
    self.assertEqual(1,
                     export_unity_package.safe_dict_set_value(1, "test", None))
    self.assertEqual(
        1, export_unity_package.safe_dict_set_value(1, "test", "value"))

    # Set a value to a string
    self.assertEqual(
        "node", export_unity_package.safe_dict_set_value("node", "test", None))
    self.assertEqual(
        "node",
        export_unity_package.safe_dict_set_value("node", "test", "value"))

    # Set a value to a set
    self.assertEqual({"item1"},
                     export_unity_package.safe_dict_set_value({"item1"}, "test",
                                                              None))
    self.assertEqual({"item1"},
                     export_unity_package.safe_dict_set_value({"item1"}, "test",
                                                              "value"))

  def test_safe_dict_set_value_to_dict(self):
    """Set a value to a dict."""
    empty_dict = {}
    empty_dict_return = export_unity_package.safe_dict_set_value(
        empty_dict, "test", "value")
    empty_dict_expected = {"test": "value"}
    self.assertEqual(empty_dict_expected, empty_dict)
    self.assertEqual(empty_dict_expected, empty_dict_return)

    dict_with_other_key = {"some_key": "some_value"}
    dict_with_other_key_return = export_unity_package.safe_dict_set_value(
        dict_with_other_key, "test", "value")
    dict_with_other_key_expected = {"some_key": "some_value", "test": "value"}
    self.assertEqual(dict_with_other_key_expected, dict_with_other_key)
    self.assertEqual(dict_with_other_key_expected, dict_with_other_key_return)

    dict_with_existing_key = {"test": "other_value"}
    dict_with_existing_key_return = export_unity_package.safe_dict_set_value(
        dict_with_existing_key, "test", "value")
    dict_with_existing_key_expected = {"test": "value"}
    self.assertEqual(dict_with_existing_key_expected, dict_with_existing_key)
    self.assertEqual(dict_with_existing_key_expected,
                     dict_with_existing_key_return)

  def test_safe_dict_set_value_none_to_dict(self):
    """Set None to a dict."""
    empty_dict = {}
    empty_dict_return = export_unity_package.safe_dict_set_value(
        empty_dict, "test", None)
    self.assertEqual({}, empty_dict)
    self.assertEqual({}, empty_dict_return)

    dict_with_other_key = {"some_key": "some_value"}
    dict_with_other_key_return = export_unity_package.safe_dict_set_value(
        dict_with_other_key, "test", None)
    self.assertEqual({"some_key": "some_value"}, dict_with_other_key)
    self.assertEqual({"some_key": "some_value"}, dict_with_other_key_return)

    dict_with_existing_key = {"test": "some_value"}
    dict_with_existing_key_return = export_unity_package.safe_dict_set_value(
        dict_with_existing_key, "test", None)
    self.assertEqual({}, dict_with_existing_key)
    self.assertEqual({}, dict_with_existing_key_return)


class GuidDatabaseTest(absltest.TestCase):
  """Test reading GUIDs from .meta files and the GUID cache."""

  def test_init_and_query_guids(self):
    """Read GUIDs from JSON string."""
    database = export_unity_package.GuidDatabase(
        export_unity_package.DuplicateGuidsChecker(),
        {
            "1.0.0": {
                "A/B.cs": "ba9f9118207d46248936105077947525",
                "C/D.dll": "84bde502cd4a4a98add4c90441d7e158"
            },
            "1.2.3": {
                "A/B.cs": "df2d7d4d6f6345609df6159fe468b61f"
            }
        },
        "1.2.3")

    self.assertEqual("df2d7d4d6f6345609df6159fe468b61f",
                     database.get_guid("A/B.cs"))
    self.assertEqual("84bde502cd4a4a98add4c90441d7e158",
                     database.get_guid("C/D.dll"))
    with self.assertRaises(export_unity_package.MissingGuidsError) as context:
      unused_guid = database.get_guid("E/F.png")
    self.assertEqual(["E/F.png"], context.exception.missing_guid_paths)

  def test_init_duplicate_guids(self):
    """Initialize the GUID database with duplicate GUIDs."""
    duplicate_guids_checker = export_unity_package.DuplicateGuidsChecker()
    unused_database = export_unity_package.GuidDatabase(
        duplicate_guids_checker,
        {
            "1.0.0": {
                "A/B.cs": "ba9f9118207d46248936105077947525",
                "C/D.cs": "ba9f9118207d46248936105077947525"
            }
        },
        "1.0.0")
    with self.assertRaises(export_unity_package.DuplicateGuidsError) as context:
      duplicate_guids_checker.check_for_duplicates()
    self.assertEqual({"ba9f9118207d46248936105077947525": set(["A/B.cs",
                                                               "C/D.cs"])},
                     context.exception.paths_by_guid)

  def test_read_guids_from_assets(self):
    """Read GUIDs from a tree of Unity assets."""
    database = export_unity_package.GuidDatabase(
        export_unity_package.DuplicateGuidsChecker(), "", "1.0.0")

    with self.assertRaises(export_unity_package.MissingGuidsError) as context:
      database.read_guids_from_assets([
          export_unity_package.Asset(
              "PlayServicesResolver/Editor/Google.VersionHandler.dll", None,
              collections.OrderedDict(
                  [("guid", "06f6f385a4ad409884857500a3c04441")])),
          export_unity_package.Asset(
              "Firebase/Plugins/Firebase.Analytics.dll", None,
              collections.OrderedDict()),
          export_unity_package.Asset(
              "Firebase/Plugins/Firebase.App.dll", None,
              collections.OrderedDict())])
    self.assertEqual(["Firebase/Plugins/Firebase.Analytics.dll",
                      "Firebase/Plugins/Firebase.App.dll"],
                     context.exception.missing_guid_paths)
    self.assertEqual(
        "06f6f385a4ad409884857500a3c04441",
        database.get_guid(
            "PlayServicesResolver/Editor/Google.VersionHandler.dll"))


class YamlSerializerTest(absltest.TestCase):
  """Test reading / writing YAML."""

  def test_read_yaml(self):
    """Read YAML into an ordered dictionary."""
    serializer = export_unity_package.YamlSerializer()
    yaml_dict = serializer.load("Object: [1, 2, 3]\n"
                                "AnotherObject:\n"
                                "  someData: foo\n"
                                "  moreData: bar\n"
                                "  otherData:\n")
    self.assertEqual([(0, "Object"),
                      (1, "AnotherObject")],
                     list(enumerate(yaml_dict)))
    self.assertEqual([1, 2, 3], yaml_dict["Object"])
    self.assertEqual([(0, "someData"), (1, "moreData"), (2, "otherData")],
                     list(enumerate(yaml_dict["AnotherObject"])))
    self.assertEqual("foo", yaml_dict["AnotherObject"]["someData"])
    self.assertEqual("bar", yaml_dict["AnotherObject"]["moreData"])
    self.assertEqual(None, yaml_dict["AnotherObject"]["otherData"])

  def test_write_yaml(self):
    """Write YAML from an ordered dictionary."""
    serializer = export_unity_package.YamlSerializer()
    object_list = [1, 2, 3]
    yaml_string = serializer.dump(collections.OrderedDict(
        [("Object", object_list),
         ("AnotherObject",
          collections.OrderedDict(
              # Also, ensure unicode strings are serialized as plain strings.
              [("someData", u"foo"),
               ("moreData", "bar"),
               ("otherData", None)])),
         ("Object2", object_list)]))
    self.assertEqual("Object:\n"
                     "- 1\n"
                     "- 2\n"
                     "- 3\n"
                     "AnotherObject:\n"
                     "  someData: foo\n"
                     "  moreData: bar\n"
                     "  otherData:\n"
                     "Object2:\n"
                     "- 1\n"
                     "- 2\n"
                     "- 3\n",
                     yaml_string)


class MergeOrderedDictsTest(absltest.TestCase):
  """Test merging ordered dictionaries."""

  def test_merge_with_empty(self):
    """"Merge a dictionary with an empty dictionary."""
    merge_into = collections.OrderedDict()
    merge_from = collections.OrderedDict(
        [("a", collections.OrderedDict(
            [("b", [1, 2, 3]),
             ("c", "hello")])),
         ("d", "bye")])
    self.assertEqual(merge_into,
                     export_unity_package.merge_ordered_dicts(
                         merge_into,
                         copy.deepcopy(merge_from)))
    self.assertEqual(merge_from, merge_into)

  def test_merge_from_empty(self):
    """"Merge an empty dictionary with a dictionary."""
    expected = collections.OrderedDict(
        [("a", collections.OrderedDict(
            [("b", [1, 2, 3]),
             ("c", "hello")])),
         ("d", "bye")])
    merge_into = copy.deepcopy(expected)
    merge_from = collections.OrderedDict()
    self.assertEqual(merge_into,
                     export_unity_package.merge_ordered_dicts(merge_into,
                                                              merge_from))
    self.assertEqual(expected, merge_into)

  def test_merge_non_dictionaries(self):
    """Try merging non-dictionary objects."""
    self.assertEqual(["do not merge"],
                     export_unity_package.merge_ordered_dicts(["do not merge"],
                                                              {"something"}))
    self.assertEqual({"something"},
                     export_unity_package.merge_ordered_dicts({"something"},
                                                              ["do not merge"]))

  def test_merge_nested(self):
    """Merge nested items in a dictionary with another dictionary."""
    merge_into = collections.OrderedDict(
        [("a", collections.OrderedDict(
            [("b", [1, 2, 3]),
             ("c", collections.OrderedDict(
                 [("hello", "goodbye"),
                  ("bonjour", "au revior")]))])),
         ("d", "foo")])
    merge_from = collections.OrderedDict(
        [("a", collections.OrderedDict(
            [("b", [4, 5, 6]),
             ("c", collections.OrderedDict(
                 [("bonjour", "is french")]))]))])
    expected = collections.OrderedDict(
        [("a", collections.OrderedDict(
            [("b", [4, 5, 6]),
             ("c", collections.OrderedDict(
                 [("hello", "goodbye"),
                  ("bonjour", "is french")]))])),
         ("d", "foo")])
    self.assertEqual(merge_into,
                     export_unity_package.merge_ordered_dicts(
                         merge_into, merge_from))
    self.assertEqual(expected, merge_into)

  def test_merge_first_second(self):
    """Merge nested items in a list of dictionaries."""
    merge_into = collections.OrderedDict(
        [("PluginImporter", collections.OrderedDict(
            [("platformData", [
                collections.OrderedDict(
                    [("first", collections.OrderedDict(
                        [("Any", None)])),
                     ("second", collections.OrderedDict(
                         [("enabled", 1),
                          ("settings", collections.OrderedDict(
                              [("CPU", "AnyCPU")]))]))]),
                collections.OrderedDict(
                    [("first", collections.OrderedDict(
                        [("Standalone", "Linux")])),
                     ("second", collections.OrderedDict(
                         [("enabled", 1),
                          ("settings", collections.OrderedDict(
                              [("CPU", "x86")]))]))]),
            ])]))
        ])
    merge_from = collections.OrderedDict(
        [("PluginImporter", collections.OrderedDict(
            [("platformData", [
                collections.OrderedDict(
                    [("first", collections.OrderedDict(
                        [("Any", None)])),
                     ("second", collections.OrderedDict(
                         [("enabled", 0),
                          ("settings", collections.OrderedDict(
                              [("CPU", "AnyCPU")]))]))]),
                collections.OrderedDict(
                    [("first", collections.OrderedDict(
                        [("Standalone", "Windows")])),
                     ("second", collections.OrderedDict(
                         [("enabled", 1),
                          ("settings", collections.OrderedDict(
                              [("CPU", "x86_64")]))]))]),
            ])]))
        ])
    expected = collections.OrderedDict(
        [("PluginImporter", collections.OrderedDict(
            [("platformData", [
                collections.OrderedDict(
                    [("first", collections.OrderedDict(
                        [("Any", None)])),
                     ("second", collections.OrderedDict(
                         [("enabled", 0),
                          ("settings", collections.OrderedDict(
                              [("CPU", "AnyCPU")]))]))]),
                collections.OrderedDict(
                    [("first", collections.OrderedDict(
                        [("Standalone", "Linux")])),
                     ("second", collections.OrderedDict(
                         [("enabled", 1),
                          ("settings", collections.OrderedDict(
                              [("CPU", "x86")]))]))]),
                collections.OrderedDict(
                    [("first", collections.OrderedDict(
                        [("Standalone", "Windows")])),
                     ("second", collections.OrderedDict(
                         [("enabled", 1),
                          ("settings", collections.OrderedDict(
                              [("CPU", "x86_64")]))]))]),
            ])]))
        ])
    self.assertEqual(merge_into,
                     export_unity_package.merge_ordered_dicts(
                         merge_into, merge_from))
    self.assertEqual(expected, merge_into)


class ConfigurationBlockTest(absltest.TestCase):
  """Test parsing common configuration options from JSON."""

  def test_block_with_sections(self):
    """Test parsing a block with conditionally enabled sections."""
    block = export_unity_package.ConfigurationBlock(json.loads(
        """
        {
          "sections": ["foo", "bar"]
        }
        """))
    self.assertEqual(set(["bar", "foo"]), block.sections)
    self.assertTrue(block.get_enabled(set(["foo"])))
    self.assertTrue(block.get_enabled(set(["bar"])))
    self.assertTrue(block.get_enabled(set(["bar", "baz"])))
    self.assertFalse(block.get_enabled(set(["baz"])))
    self.assertFalse(block.get_enabled(set()))

  def test_block_with_no_sections(self):
    """Test parsing a block with no conditionally enabled sections."""
    block = export_unity_package.ConfigurationBlock(json.loads("{}"))
    self.assertEqual(set(), block.sections)
    self.assertTrue(block.get_enabled(set(["foo", "bar"])))
    self.assertTrue(block.get_enabled(set()))


class ProjectConfigurationTest(absltest.TestCase):
  """Test parsing the project configuration from JSON."""

  def setUp(self):
    """Setup a common configuration for a subset of tests."""
    super(ProjectConfigurationTest, self).setUp()
    self.old_flag = FLAGS.enforce_semver
    # Turn off the enforce for most of the test
    FLAGS.enforce_semver = False

  def tearDown(self):
    """Clean up the temporary directory."""
    super(ProjectConfigurationTest, self).tearDown()
    FLAGS.enforce_semver = self.old_flag

  def test_no_packages(self):
    """Test parsing a project with no packages."""
    config = export_unity_package.ProjectConfiguration({"packages": []},
                                                       set(), None)
    self.assertEqual([], list(config.packages))
    self.assertEqual({}, config.packages_by_name)
    self.assertEqual(set(), config.selected_sections)
    self.assertEqual(["<unnamed>"], [build.name for build in config.builds])

  def test_no_packages_with_version(self):
    """Test parsing a project with no packages with version."""
    config = export_unity_package.ProjectConfiguration({"packages": []}, set(),
                                                       "1.22.333")
    self.assertEqual([], list(config.packages))
    self.assertEqual({}, config.packages_by_name)
    self.assertEqual(set(), config.selected_sections)
    self.assertEqual(["<unnamed>"], [build.name for build in config.builds])

  def test_version_no_enforce(self):
    """Test parsing a project version with no SemVer enforcement."""
    FLAGS.enforce_semver = False
    self.assertEqual(None,
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), None).version)

    self.assertEqual("",
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), "").version)

    self.assertEqual("1",
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), "1").version)

    self.assertEqual("1.2",
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), "1.2").version)

    self.assertEqual("1a.2.3",
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), "1a.2.3").version)

    self.assertEqual(".2.3",
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), ".2.3").version)

  def test_version_with_enforce(self):
    """Test parsing a project version with SemVer enforcement."""
    FLAGS.enforce_semver = True

    self.assertEqual("0.0.0",
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), "0.0.0").version)
    self.assertEqual("1.2.3",
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), "1.2.3").version)
    self.assertEqual("111.22.3333",
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), "111.22.3333").version)
    self.assertEqual("1.0.0-preview",
                     export_unity_package.ProjectConfiguration(
                         {"packages": []}, set(), "1.0.0-preview").version)

    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      export_unity_package.ProjectConfiguration({"packages": []}, set(), None)

    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      export_unity_package.ProjectConfiguration({"packages": []}, set(), "")

    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      export_unity_package.ProjectConfiguration({"packages": []}, set(), "1")

    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      export_unity_package.ProjectConfiguration({"packages": []}, set(), "1.2")

    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      export_unity_package.ProjectConfiguration({"packages": []}, set(),
                                                "1a.2.3")

    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      export_unity_package.ProjectConfiguration({"packages": []}, set(), ".2.3")

  def test_enabled_package(self):
    """Test parsing a project with an enabled package."""
    config = export_unity_package.ProjectConfiguration(
        {"packages": [{"name": "FirebaseApp.unitypackage"}]}, set(), None)
    self.assertEqual(["FirebaseApp.unitypackage"],
                     list(config.packages_by_name.keys()))
    self.assertLen(config.packages, 1)
    self.assertEqual("FirebaseApp.unitypackage", config.packages[0].name)
    self.assertEqual(set(), config.selected_sections)

  def test_enable_package_and_builds_by_section(self):
    """Test parsing a project with conditionally enabled packages."""
    config = export_unity_package.ProjectConfiguration(
        {
            "packages": [
                {"name": "FirebaseApp.unitypackage"},
                {"name": "FirebaseAppExperimental.unitypackage",
                 "sections": ["experimental"]},
                {"name": "FirebaseAppTest.unitypackage",
                 "sections": ["debug", "test"]}
            ],
            "builds": [
                {"name": "production"},
                {"name": "experimental", "sections": ["experimental"]},
                {"name": "debug", "sections": ["debug", "test"]},
            ],
        }, set(), None)
    self.assertCountEqual(["FirebaseApp.unitypackage"],
                          config.packages_by_name.keys())
    self.assertCountEqual(["production"], [b.name for b in config.builds])
    self.assertCountEqual(set(), config.selected_sections)

    config.selected_sections = set(["experimental"])
    self.assertCountEqual(["FirebaseApp.unitypackage",
                           "FirebaseAppExperimental.unitypackage"],
                          config.packages_by_name.keys())
    self.assertCountEqual(["experimental", "production"],
                          [b.name for b in config.builds])
    self.assertCountEqual(set(["experimental"]), config.selected_sections)

    config.selected_sections = set(["debug"])
    self.assertCountEqual(["FirebaseApp.unitypackage",
                           "FirebaseAppTest.unitypackage"],
                          config.packages_by_name.keys())
    self.assertCountEqual(["debug", "production"],
                          [b.name for b in config.builds])
    self.assertCountEqual(set(["debug"]), config.selected_sections)

    config.selected_sections = set(["test"])
    self.assertCountEqual(["FirebaseApp.unitypackage",
                           "FirebaseAppTest.unitypackage"],
                          config.packages_by_name.keys())
    self.assertCountEqual(["debug", "production"],
                          [b.name for b in config.builds])
    self.assertCountEqual(set(["test"]), config.selected_sections)

    config.selected_sections = set(["experimental", "debug"])
    self.assertCountEqual(["FirebaseApp.unitypackage",
                           "FirebaseAppExperimental.unitypackage",
                           "FirebaseAppTest.unitypackage"],
                          config.packages_by_name.keys())
    self.assertCountEqual(["debug", "experimental", "production"],
                          [b.name for b in config.builds])
    self.assertCountEqual(set(["debug", "experimental"]),
                          config.selected_sections)

  def test_duplicate_packages(self):
    """Test parsing a project with duplicate packages."""
    config_json = {
        "packages": [
            {"name": "FirebaseApp.unitypackage", "sections": ["public"]},
            {"name": "FirebaseApp.unitypackage", "sections": ["experimental"]},
            {"name": "FirebaseApp.unitypackage", "sections": ["debug", "test"]}
        ]
    }

    config = export_unity_package.ProjectConfiguration(config_json,
                                                       set(["public"]), None)
    self.assertEqual(["FirebaseApp.unitypackage"],
                     list(config.packages_by_name.keys()))

    config = export_unity_package.ProjectConfiguration(config_json,
                                                       set(["experimental"]),
                                                       None)
    self.assertEqual(["FirebaseApp.unitypackage"],
                     list(config.packages_by_name.keys()))

    # Enable conflicting packages for export.
    with self.assertRaises(export_unity_package.ProjectConfigurationError) as (
        context):
      config = export_unity_package.ProjectConfiguration(
          config_json, set(["debug", "experimental"]), None)
    self.assertRegexMatch(
        str(context.exception),
        [r"Package.*FirebaseApp\.unitypackage.*'debug', 'experimental'"])

  def test_package_includes(self):
    """Test parsing a project with packages that include each other."""
    config_json = {
        "packages": [
            {"name": "FirebaseApp.unitypackage"},
            {"name": "Parse.unitypackage"},
            {"name": "FirebaseAnalytics.unitypackage",
             "includes": ["FirebaseApp.unitypackage", "Parse.unitypackage"]},
            {"name": "PlayServicesResolver.unitypackage"}
        ]
    }

    config = export_unity_package.ProjectConfiguration(config_json, set(), None)
    self.assertCountEqual(
        ["FirebaseApp.unitypackage", "Parse.unitypackage"],
        [include.name for include in (
            config.packages_by_name[
                "FirebaseAnalytics.unitypackage"].includes)])
    self.assertEqual([], config.packages_by_name[
        "PlayServicesResolver.unitypackage"].includes)

  def test_package_include_circular_reference(self):
    """Test parsing a project with circular reference in includes."""
    config_json = {
        "packages": [
            {"name": "FirebaseApp.unitypackage",
             "includes": ["CommonUtilities.unitypackage"]},
            {"name": "Parse.unitypackage",
             "includes": ["PlayServicesResolver.unitypackage"]},
            {"name": "CommonUtilities.unitypackage",
             "includes": ["Parse.unitypackage",
                          "FirebaseAnalytics.unitypackage"]},
            {"name": "FirebaseAnalytics.unitypackage",
             "includes": ["FirebaseApp.unitypackage", "Parse.unitypackage"]},
            {"name": "PlayServicesResolver.unitypackage"}
        ]
    }

    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      export_unity_package.ProjectConfiguration(config_json, set(), None)

  def test_package_include_missing(self):
    """Test parsing a project with a missing reference in includes."""
    config_json = {
        "packages": [
            {"name": "FirebaseApp.unitypackage",
             "includes": ["CommonUtilities.unitypackage"]},
            {"name": "CommonUtilities.unitypackage",
             "includes": ["Parse.unitypackage"]},
            {"name": "FirebaseAnalytics.unitypackage",
             "includes": ["FirebaseApp.unitypackage"]}
        ]
    }

    with self.assertRaises(export_unity_package.ProjectConfigurationError) as (
        context):
      export_unity_package.ProjectConfiguration(config_json, set(), None)
    self.assertIn("Parse.unitypackage", str(context.exception))

  def test_write_failure_due_to_duplicate_output_files(self):
    """Test writing a project that maps multiple packages to the same files."""
    config = export_unity_package.ProjectConfiguration(
        {"packages": [
            {"name": "FirebaseApp.unitypackage"},
            {"name": "FirebaseAnalytics.unitypackage",
             "sections": ["experimental"]},
            {"name": "FirebaseAuth.unitypackage",
             "sections": ["public"]},
        ],
         "builds": [
             {"name": "public", "enabled_sections": ["public"]},
             {"name": "experimental", "enabled_sections": ["experimental"]},
         ]}, set(), None)
    with self.assertRaises(export_unity_package.ProjectConfigurationError) as (
        context):
      config.write(
          export_unity_package.GuidDatabase(
              export_unity_package.DuplicateGuidsChecker(), {}, "1.2.3"),
          ["assets"], "output", 0)
    self.assertRegexMatch(
        str(context.exception),
        ["FirebaseApp.unitypackage .* ['public', 'experimental']"])

  def test_write_with_build_configs(self):
    """Test writing the contents of a project with build configs."""

    expected_guid_database = export_unity_package.GuidDatabase(
        export_unity_package.DuplicateGuidsChecker(), {}, "1.2.3")
    expected_assets_dirs = ["some/assets"]
    expected_output_dir = "an/output/dir"
    expected_timestamp = 123456789
    test_case_instance = self

    package_configuration_class = export_unity_package.PackageConfiguration

    class FakeWritePackageConfiguration(
        export_unity_package.PackageConfiguration):
      """PackageConfiguration with write()/write_upm() replaced with a fake."""

      def __init__(self, project, package_json):
        """Initialize this object with JSON package data.

        Args:
          project: ProjectConfiguration instance this package was parsed from.
          package_json: Package dictionary parsed from JSON project data.

        Raises:
          ProjectConfigurationError: If the package has no name.
        """
        # Restore the class before init so super() functions correctly.
        export_unity_package.PackageConfiguration = package_configuration_class
        super(FakeWritePackageConfiguration, self).__init__(project,
                                                            package_json)
        export_unity_package.PackageConfiguration = (
            FakeWritePackageConfiguration)

      def write(self, guid_database, assets_dirs, output_dir, timestamp,
                package_filename=None):
        """Stubbed out implementation of write().

        Args:
          guid_database: Must equal expected_guid_database.
          assets_dirs: Must equal expected_assets_dir.
          output_dir: Must equal expected_output_dir.
          timestamp: Must equal expected_timestamp.
          package_filename: Returned by this method.

        Returns:
          Value of package_filename.
        """
        test_case_instance.assertEqual(expected_guid_database, guid_database)
        test_case_instance.assertEqual(expected_assets_dirs, assets_dirs)
        test_case_instance.assertEqual(expected_output_dir, output_dir)
        test_case_instance.assertEqual(expected_timestamp, timestamp)
        return package_filename

      def write_upm(self,
                    guid_database,
                    assets_dirs,
                    output_dir,
                    timestamp,
                    package_filename=None):
        """Stubbed out implementation of write_upm().

        Args:
          guid_database: Must equal expected_guid_database.
          assets_dirs: Must equal expected_assets_dir.
          output_dir: Must equal expected_output_dir.
          timestamp: Must equal expected_timestamp.
          package_filename: Returned by this method.

        Returns:
          Value of package_filename.
        """
        test_case_instance.assertEqual(expected_guid_database, guid_database)
        test_case_instance.assertEqual(expected_assets_dirs, assets_dirs)
        test_case_instance.assertEqual(expected_output_dir, output_dir)
        test_case_instance.assertEqual(expected_timestamp, timestamp)
        return package_filename

    try:
      export_unity_package.PackageConfiguration = FakeWritePackageConfiguration
      config = export_unity_package.ProjectConfiguration(
          {
              "packages": [
                  {
                      "name": "FirebaseApp.unitypackage",
                      "common_manifest": {
                          "name": "com.firebase.app"
                      },
                      "export_upm": 1
                  },
                  {
                      "name": "FirebaseSpecialSauce.unitypackage",
                      "sections": ["experimental"],
                      "common_manifest": {
                          "name": "com.firebase.special_sauce"
                      },
                      "export_upm": 1
                  },
              ],
              "builds": [
                  {
                      "name": "public",
                      "enabled_sections": ["public"]
                  },
                  {
                      "name":
                          "experimental",
                      "enabled_sections": ["experimental"],
                      "package_name_replacements": [
                          {
                              "match": r"^(Firebase)([^.]+)(\.unitypackage)$",
                              "replacement": r"\1Secret\2Experiment\3"
                          },
                          {
                              "match": r"^(com\.firebase\..+)(\.tgz)$",
                              "replacement": r"\1-preview\2"
                          },
                      ]
                  },
              ]
          }, set(), "1.2.3")
      self.assertCountEqual(
          [("FirebaseApp.unitypackage", "public"),
           ("FirebaseSecretAppExperiment.unitypackage", "experimental"),
           ("FirebaseSecretSpecialSauceExperiment.unitypackage",
            "experimental")],
          [(filename, build.name) for filename, build in config.write(
              expected_guid_database, expected_assets_dirs,
              expected_output_dir, expected_timestamp).items()])
      self.assertCountEqual(
          [("com.firebase.app-1.2.3.tgz", "public"),
           ("com.firebase.app-1.2.3-preview.tgz", "experimental"),
           ("com.firebase.special_sauce-1.2.3-preview.tgz", "experimental")],
          [(filename, build.name) for filename, build in config.write(
              expected_guid_database, expected_assets_dirs, expected_output_dir,
              expected_timestamp, for_upm=True).items()])
    finally:
      export_unity_package.PackageConfiguration = package_configuration_class


class BuildConfigurationTest(absltest.TestCase):
  """Test parsing a build configuration."""

  def setUp(self):
    """Setup a common configuration for a subset of tests."""
    super(BuildConfigurationTest, self).setUp()
    self.config_json = {
        "name": "Debug",
        "sections": ["internal"],
        "enabled_sections": ["debug", "test"],
        "package_name_replacements": [
            {"match": "Firebase",
             "replacement": "Fire"},
            {"match": r"([^.]+)\.unitypackage",
             "replacement": r"\1Debug.unitypackage"},
        ],
    }

  def test_create_empty(self):
    """Create an empty build config."""
    config = export_unity_package.BuildConfiguration({})
    self.assertEqual(set(), config.sections)
    self.assertEqual(set(), config.enabled_sections)
    self.assertEqual("<unnamed>", config.name)
    self.assertEqual([], config.package_name_replacements)

  def test_create(self):
    """Create a named build config."""
    config = export_unity_package.BuildConfiguration(self.config_json)
    self.assertEqual(set(["internal"]), config.sections)
    self.assertEqual(set(["debug", "test"]), config.enabled_sections)
    self.assertEqual("Debug", config.name)
    self.assertEqual([("Firebase", "Fire"),
                      (r"([^.]+)\.unitypackage",
                       r"\1Debug.unitypackage")],
                     [(pattern_re.pattern, repl)
                      for pattern_re, repl in config.package_name_replacements])

  def test_create_invalid_regex(self):
    """Create a build config with an invalid regular expression."""
    with self.assertRaises(export_unity_package.ProjectConfigurationError) as (
        context):
      export_unity_package.BuildConfiguration({
          "package_name_replacements": [{
              "match": r"(invalid",
              "replacement": "ignored"
          }]
      })
    self.assertRegexMatch(str(context.exception), [r"\(invalid"])

  def test_apply_package_name_replacements(self):
    """Replace a package name with build config replacements."""
    config = export_unity_package.BuildConfiguration(self.config_json)
    self.assertEqual("HotFireMagicExDebug.unitypackage",
                     config.apply_package_name_replacements(
                         "HotFirebaseMagicEx.unitypackage"))

  def test_apply_invalid_package_name_replacement(self):
    """Try to apply an invalid replacement."""
    config = export_unity_package.BuildConfiguration({
        "package_name_replacements": [{
            "match": r"([^.]+)\.ext",
            "replacement": r"\2something\1"
        }]
    })
    with self.assertRaises(export_unity_package.ProjectConfigurationError) as (
        context):
      config.apply_package_name_replacements("test.ext")
    self.assertRegexMatch(str(context.exception), [r"\\2something\\1"])

  def test_create_package_name_map(self):
    """Create a map of renamed package names."""
    config = export_unity_package.BuildConfiguration(self.config_json)
    self.assertEqual(
        {
            "HotFirebaseMagicEx.unitypackage":
                "HotFireMagicExDebug.unitypackage",
            "FirebasePerf.unitypackage":
                "FirePerformanceDebug.unitypackage"
        },
        config.create_package_name_map({
            "HotFirebaseMagicEx.unitypackage":
                "HotFirebaseMagicEx.unitypackage",
            "FirebasePerf.unitypackage":
                "FirebasePerformance.unitypackage"
        }))


class PackageConfigurationTest(absltest.TestCase):
  """Test parsing a package configuration."""

  def setUp(self):
    """Create an empty project config."""
    super(PackageConfigurationTest, self).setUp()
    self.project = export_unity_package.ProjectConfiguration({}, set(),
                                                             "1.2.3")
    # Metadata before write() is called.
    self.expected_manifest_metadata_prebuild = copy.deepcopy(
        export_unity_package.DEFAULT_METADATA_TEMPLATE)
    self.expected_manifest_metadata_prebuild["labels"] = [
        "gvh", "gvh_manifest", "gvh_version-1.2.3",
        "gvhp_manifestname-0NiceName", "gvhp_manifestname-1Test"]
    # Metadata when write() is called.
    self.expected_manifest_metadata = copy.deepcopy(
        export_unity_package.DEFAULT_METADATA_TEMPLATE)
    self.expected_manifest_metadata["labels"] = [
        "gvh", "gvh_manifest", "gvh_version-1.2.3",
        "gvhp_exportpath-Foo/Bar/Test_version-1.2.3_manifest.txt",
        "gvhp_manifestname-0Test"]
    # Metadata before write_upm() is called.
    self.expected_upm_manifest_metadata_prebuild = copy.deepcopy(
        export_unity_package.DEFAULT_METADATA_TEMPLATE)
    self.expected_upm_manifest_metadata_prebuild["labels"] = [
        "gupmr_manifest", "gvh", "gvh_version-1.2.3"]
    # Metadata when write_upm() is called.
    self.expected_upm_manifest_metadata = copy.deepcopy(
        export_unity_package.DEFAULT_METADATA_TEMPLATE)
    self.expected_upm_manifest_metadata["labels"] = [
        "gupmr_manifest", "gvh", "gvh_version-1.2.3",
        "gvhp_exportpath-package.json"]

  def test_create_no_name(self):
    """Create a package config with no name."""
    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      export_unity_package.PackageConfiguration(self.project, {})

  def test_create_defaults_no_version(self):
    """Create a package config with default values."""
    old_flag = FLAGS.enforce_semver
    FLAGS.enforce_semver = False
    config = export_unity_package.PackageConfiguration(
        export_unity_package.ProjectConfiguration({}, set(), None),
        {"name": "Test.unitypackage"})
    self.assertEqual("Test.unitypackage", config.name)
    self.assertTrue(config.export)
    self.assertEqual(None, config.manifest_path)
    self.assertEqual(None, config.manifest_filename)
    self.assertEqual(
        None,
        config.get_manifest_metadata(
            export_unity_package.VERSION_HANDLER_MANIFEST_TYPE_LEGACY))
    self.assertEqual(
        None,
        config.get_manifest_metadata(
            export_unity_package.VERSION_HANDLER_MANIFEST_TYPE_UPM))
    self.assertEqual([], config.imports)
    self.assertEqual([], config.includes)
    self.assertEqual([], config.exclude_paths)
    self.assertEqual(set([]), config.labels)
    self.assertEqual(None, config.version)
    self.assertEqual("Test", config.package_name)
    self.assertEqual("Test", config.common_package_display_name)
    self.assertEqual(None, config.common_manifest)
    self.assertEqual(None, config.common_package_name)
    self.assertEqual(None, config.common_package_description)
    self.assertFalse(config.export_upm)
    self.assertEqual(None, config.tarball_name)
    self.assertEqual(None, config.upm_package_config)
    self.assertEqual(None, config.upm_manifest)

    FLAGS.enforce_semver = old_flag

  def test_create_defaults(self):
    """Create a package config with default values."""
    config = export_unity_package.PackageConfiguration(
        self.project, {"name": "Test.unitypackage"})
    self.assertEqual("Test.unitypackage", config.name)
    self.assertTrue(config.export)
    self.assertEqual(None, config.manifest_path)
    self.assertEqual(None, config.manifest_filename)
    self.assertEqual(
        None,
        config.get_manifest_metadata(
            export_unity_package.VERSION_HANDLER_MANIFEST_TYPE_LEGACY))
    self.assertEqual(
        self.expected_upm_manifest_metadata_prebuild,
        config.get_manifest_metadata(
            export_unity_package.VERSION_HANDLER_MANIFEST_TYPE_UPM))
    self.assertEqual([], config.imports)
    self.assertEqual([], config.includes)
    self.assertEqual([], config.exclude_paths)
    self.assertEqual(set(["gvh", "gvh_version-1.2.3"]), config.labels)
    self.assertEqual("1.2.3", config.version)
    self.assertEqual("Test", config.package_name)
    self.assertEqual("Test", config.common_package_display_name)
    self.assertEqual(None, config.common_manifest)
    self.assertEqual(None, config.common_package_name)
    self.assertEqual(None, config.common_package_description)
    self.assertFalse(config.export_upm)
    self.assertEqual(None, config.tarball_name)
    self.assertEqual(None, config.upm_package_config)
    self.assertEqual(None, config.upm_manifest)

  def test_create_non_defaults(self):
    """Create a package config with non-default values."""
    config = export_unity_package.PackageConfiguration(
        self.project, {
            "name": "Test.unitypackage",
            "export": 0,
            "manifest_path": "Foo/Bar",
            "exclude_paths": ["a/b/c"],
            "common_manifest": {
                "name": "com.company.test",
                "description": "Desc",
                "display_name": "NiceName"
            },
            "export_upm": 1,
            "upm_package_config": {
                "manifest": {
                    "unity": "2017.1"
                }
            }
        })
    self.assertEqual("Test.unitypackage", config.name)
    self.assertFalse(config.export)
    self.assertEqual("Foo/Bar", config.manifest_path)
    self.assertEqual("Foo/Bar/Test_version-1.2.3_manifest.txt",
                     config.manifest_filename)
    self.assertEqual(
        self.expected_manifest_metadata_prebuild,
        config.get_manifest_metadata(
            export_unity_package.VERSION_HANDLER_MANIFEST_TYPE_LEGACY))
    self.assertEqual(
        self.expected_upm_manifest_metadata_prebuild,
        config.get_manifest_metadata(
            export_unity_package.VERSION_HANDLER_MANIFEST_TYPE_UPM))
    self.assertEqual([], config.imports)
    self.assertEqual([], config.includes)
    self.assertEqual([re.compile("a/b/c")], config.exclude_paths)
    self.assertEqual(set(["gvh", "gvh_version-1.2.3"]), config.labels)
    self.assertEqual("1.2.3", config.version)
    self.assertEqual("Test", config.package_name)
    self.assertEqual("NiceName", config.common_package_display_name)
    self.assertEqual(
        {"name": "com.company.test", "description": "Desc",
         "display_name": "NiceName"},
        config.common_manifest)
    self.assertEqual("com.company.test", config.common_package_name)
    self.assertEqual("Desc", config.common_package_description)
    self.assertTrue(config.export_upm)
    self.assertEqual("com.company.test-1.2.3.tgz", config.tarball_name)
    self.assertEqual({"manifest": {
        "unity": "2017.1"
    }}, config.upm_package_config)
    self.assertEqual({"unity": "2017.1"}, config.upm_manifest)

  def test_create_non_defaults_desc_list(self):
    """Create a package config with description as a list."""
    config = export_unity_package.PackageConfiguration(
        self.project, {
            "name": "Test.unitypackage",
            "export": 0,
            "manifest_path": "Foo/Bar",
            "exclude_paths": ["a/b/c"],
            "common_manifest": {
                "name": "com.company.test",
                "description": ["Desc", "123"],
                "display_name": "NiceName"
            },
            "export_upm": 1,
            "upm_package_config": {
                "manifest": {
                    "unity": "2017.1"
                }
            }
        })
    self.assertEqual("Test.unitypackage", config.name)
    self.assertFalse(config.export)
    self.assertEqual("Foo/Bar", config.manifest_path)
    self.assertEqual("Foo/Bar/Test_version-1.2.3_manifest.txt",
                     config.manifest_filename)
    self.assertEqual(
        self.expected_manifest_metadata_prebuild,
        config.get_manifest_metadata(
            export_unity_package.VERSION_HANDLER_MANIFEST_TYPE_LEGACY))
    self.assertEqual(
        self.expected_upm_manifest_metadata_prebuild,
        config.get_manifest_metadata(
            export_unity_package.VERSION_HANDLER_MANIFEST_TYPE_UPM))
    self.assertEqual([], config.imports)
    self.assertEqual([], config.includes)
    self.assertEqual([re.compile("a/b/c")], config.exclude_paths)
    self.assertEqual(set(["gvh", "gvh_version-1.2.3"]), config.labels)
    self.assertEqual("1.2.3", config.version)
    self.assertEqual("Test", config.package_name)
    self.assertEqual("NiceName", config.common_package_display_name)
    self.assertEqual(
        {"name": "com.company.test", "description": ["Desc", "123"],
         "display_name": "NiceName"},
        config.common_manifest)
    self.assertEqual("com.company.test", config.common_package_name)
    self.assertEqual("Desc123", config.common_package_description)
    self.assertTrue(config.export_upm)
    self.assertEqual("com.company.test-1.2.3.tgz", config.tarball_name)
    self.assertEqual({"manifest": {
        "unity": "2017.1"
    }}, config.upm_package_config)
    self.assertEqual({"unity": "2017.1"}, config.upm_manifest)

  def test_imports(self):
    """Create a package configuration that includes other configs."""
    config = export_unity_package.PackageConfiguration(
        self.project,
        {
            "name": "Test.unitypackage",
            "imports": [
                {
                    "importer": "PluginImporter",
                    "paths": ["PlayServicesResolver/Editor/"
                              "Google.VersionHandler.*"]
                },
                {
                    "importer": "DefaultImporter",
                    "paths": ["PlayServicesResolver/Editor/*_manifest*.txt"],
                }
            ]
        })
    self.assertEqual(
        [set(["PlayServicesResolver/Editor/Google.VersionHandler.*"]),
         set(["PlayServicesResolver/Editor/*_manifest*.txt"])],
        [asset_config.paths for asset_config in config.imports])

  def test_write_manifest(self):
    """Write a package manifest."""
    config = export_unity_package.PackageConfiguration(
        self.project,
        {"name": "Test.unitypackage",
         "export": 0,
         "manifest_path": "Foo/Bar"})
    output_directory = os.path.join(FLAGS.test_tmpdir, "manifest")
    try:
      os.makedirs(output_directory)
      manifest_asset = config.write_manifest(
          output_directory,
          [export_unity_package.Asset(
              "zebra/head.fbx", None,
              export_unity_package.DEFAULT_METADATA_TEMPLATE),
           export_unity_package.Asset(
               "moose/tail.png", None,
               export_unity_package.DEFAULT_METADATA_TEMPLATE),
           export_unity_package.Asset(
               "bear/paw.fplmesh", None,
               export_unity_package.DEFAULT_METADATA_TEMPLATE)])
      self.assertEqual("Foo/Bar/Test_version-1.2.3_manifest.txt",
                       manifest_asset.filename)
      self.assertEqual("Foo/Bar/Test_version-1.2.3_manifest.txt",
                       manifest_asset.filename_guid_lookup)
      manifest_absolute_path = export_unity_package.posix_path(os.path.join(
          output_directory, "Foo/Bar/Test_version-1.2.3_manifest.txt"))
      self.assertEqual(manifest_absolute_path,
                       manifest_asset.filename_absolute)
      self.assertFalse(manifest_asset.is_folder)
      self.assertEqual(self.expected_manifest_metadata,
                       manifest_asset.importer_metadata)
      with open(manifest_absolute_path, "rt") as manifest:
        self.assertEqual(
            "Assets/bear/paw.fplmesh\n"
            "Assets/moose/tail.png\n"
            "Assets/zebra/head.fbx\n",
            manifest.read())
    finally:
      shutil.rmtree(output_directory)

  def test_write_upm_manifest(self):
    """Write a package manifest for UPM package."""
    config = export_unity_package.PackageConfiguration(
        self.project, {
            "name": "Test.unitypackage",
            "export": 0,
            "manifest_path": "Foo/Bar",
            "common_manifest": {
                "name": "com.company.test",
                "display_name": "Test",
                "description": "Test description",
                "keywords": ["test keyword"],
                "author": {
                    "name": "company",
                    "email": "someone@email.com",
                    "url": "https://test.company.com/"
                }
            },
            "export_upm": 1,
            "upm_package_config": {
                "manifest": {
                    "unity": "2017.1",
                    "dependencies": {
                        "com.company.dep": "1.6.8"
                    }
                }
            }
        })
    expected_manifest = {
        "name": "com.company.test",
        "version": "1.2.3",
        "displayName": "Test",
        "description": "Test description",
        "keywords": ["test keyword"],
        "author": {
            "name": "company",
            "email": "someone@email.com",
            "url": "https://test.company.com/"
        },
        "unity": "2017.1",
        "dependencies": {
            "com.company.dep": "1.6.8"
        }
    }
    output_directory = os.path.join(FLAGS.test_tmpdir, "manifest")
    try:
      os.makedirs(output_directory)
      manifest_asset = config.write_upm_manifest(output_directory)
      self.assertEqual("package.json", manifest_asset.filename)
      self.assertEqual("com.company.test/package.json",
                       manifest_asset.filename_guid_lookup)
      manifest_absolute_path = export_unity_package.posix_path(
          os.path.join(output_directory, "package.json"))
      self.assertEqual(manifest_absolute_path, manifest_asset.filename_absolute)
      self.assertFalse(manifest_asset.is_folder)
      self.assertEqual(self.expected_upm_manifest_metadata,
                       manifest_asset.importer_metadata)
      with open(manifest_absolute_path, "rt") as manifest:
        self.assertEqual(expected_manifest, json.loads(manifest.read()))
    finally:
      shutil.rmtree(output_directory)


class AssetTest(absltest.TestCase):
  """Test the Asset class."""

  def setUp(self):
    """Create expected metadata."""
    super(AssetTest, self).setUp()
    self.default_metadata = copy.deepcopy(
        export_unity_package.DEFAULT_IMPORTER_METADATA_TEMPLATE)
    self.default_metadata["labels"] = ["gvh", "gvh_version-1.2.3"]
    self.staging_dir = os.path.join(FLAGS.test_tmpdir, "staging")
    os.makedirs(self.staging_dir)

    self.assets_dir = os.path.join(TEST_DATA_PATH, "Assets")
    self.asset_list = [
        export_unity_package.Asset("bar", "bar", self.default_metadata),
        export_unity_package.Asset("foo/bar", "foo/bar", self.default_metadata),
    ]

  def tearDown(self):
    """Clean up the temporary directory."""
    super(AssetTest, self).tearDown()
    delete_temporary_directory_contents()

  def test_init(self):
    """Initialize an Asset instance."""
    asset = export_unity_package.Asset("libFooBar.so", "a/path/to/libFooBar.so",
                                       collections.OrderedDict())
    self.assertEqual("libFooBar.so", asset.filename)
    self.assertEqual("a/path/to/libFooBar.so", asset.filename_absolute)
    self.assertEqual(
        collections.OrderedDict([("labels", ["gvhp_exportpath-libFooBar.so"])]),
        asset.importer_metadata)
    self.assertEqual("libFooBar.so", asset.filename_guid_lookup)
    self.assertEqual(False, asset.is_folder)
    self.assertEqual(collections.OrderedDict(),
                     asset.importer_metadata_original)

  def test_init_override_guid_lookup(self):
    """Initialize an Asset instance with overridden GUID Lookup filename."""
    asset = export_unity_package.Asset(
        "libFooBar.so",
        "a/path/to/libFooBar.so",
        collections.OrderedDict(),
        filename_guid_lookup="foo.bar/libFooBar.so")
    self.assertEqual("libFooBar.so", asset.filename)
    self.assertEqual("a/path/to/libFooBar.so", asset.filename_absolute)
    self.assertEqual("foo.bar/libFooBar.so", asset.filename_guid_lookup)
    self.assertEqual(False, asset.is_folder)
    self.assertEqual(
        collections.OrderedDict([("labels", ["gvhp_exportpath-libFooBar.so"])]),
        asset.importer_metadata)
    self.assertEqual(collections.OrderedDict(),
                     asset.importer_metadata_original)

  def test_init_folder(self):
    """Initialize an folder Asset instance."""
    asset = export_unity_package.Asset(
        "foo/bar", "foo/bar", collections.OrderedDict(), is_folder=True)
    self.assertEqual("foo/bar", asset.filename)
    self.assertEqual("foo/bar", asset.filename_absolute)
    self.assertEqual("foo/bar", asset.filename_guid_lookup)
    self.assertEqual(True, asset.is_folder)
    self.assertEqual(
        collections.OrderedDict([("labels", ["gvhp_exportpath-foo/bar"])]),
        asset.importer_metadata)
    self.assertEqual(collections.OrderedDict(),
                     asset.importer_metadata_original)

  def test_repr(self):
    """Convert an asset to a string."""
    self.assertRegexMatch(
        repr(export_unity_package.Asset("libFooBar.so", None,
                                        self.default_metadata)),
        ["filename=libFooBar.so metadata=.*gvh_version-1.2.3.*"])

  def test_importer_metadata(self):
    """Generate importer metadata for a path."""
    expected_metadata = copy.deepcopy(self.default_metadata)
    expected_metadata["labels"] = sorted(
        expected_metadata["labels"] +
        ["gvhp_exportpath-Plugins/noarch/libFooBar.so"])
    asset = export_unity_package.Asset("Plugins/noarch/libFooBar.so", None,
                                       self.default_metadata)
    self.assertEqual(self.default_metadata, asset.importer_metadata_original)
    self.assertEqual(expected_metadata, asset.importer_metadata)

    metadata_linuxlibname = copy.deepcopy(self.default_metadata)
    metadata_linuxlibname["labels"] = sorted(metadata_linuxlibname["labels"] +
                                             ["gvh_linuxlibname-FooBar"])

    expected_metadata = copy.deepcopy(metadata_linuxlibname)
    expected_metadata["labels"] = sorted(
        expected_metadata["labels"] +
        ["gvhp_exportpath-Plugins/x86/libFooBar.so"])
    asset = export_unity_package.Asset("Plugins/x86/libFooBar.so", None,
                                       metadata_linuxlibname)
    self.assertEqual(metadata_linuxlibname, asset.importer_metadata_original)
    self.assertEqual(expected_metadata, asset.importer_metadata)

    expected_metadata = copy.deepcopy(metadata_linuxlibname)
    expected_metadata["labels"] = sorted(
        expected_metadata["labels"] +
        ["gvhp_exportpath-Plugins/x86_64/libFooBar.so"])
    asset = export_unity_package.Asset("Plugins/x86_64/libFooBar.so", None,
                                       metadata_linuxlibname)
    self.assertEqual(metadata_linuxlibname, asset.importer_metadata_original)
    self.assertEqual(expected_metadata, asset.importer_metadata)

  def test_add_labels_to_metadata(self):
    """Add labels to importer metadata."""
    metadata = export_unity_package.Asset.add_labels_to_metadata(
        self.default_metadata, set(["foo", "bar"]))
    self.assertEqual(
        ["bar", "foo", "gvh", "gvh_version-1.2.3"],
        metadata["labels"])
    self.assertEqual(metadata, self.default_metadata)

  def test_add_labels_to_metadata_empty(self):
    """Add an empty list of labels to empty metadata."""
    metadata = export_unity_package.Asset.add_labels_to_metadata(
        collections.OrderedDict([("labels", [])]), set([]))
    self.assertEqual(None, metadata.get("labels"))

  def test_disable_unsupported_platforms(self):
    """Disable unsupported platforms for shared libraries."""
    all_platforms_enabled = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    platform_data = all_platforms_enabled["PluginImporter"]["platformData"]
    platform_data["Any"]["enabled"] = 1
    platform_data["Any"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Editor"]["enabled"] = 1
    platform_data["Editor"]["settings"]["CPU"] = "AnyCPU"
    platform_data["OSXIntel"]["enabled"] = 1
    platform_data["OSXIntel"]["settings"]["CPU"] = "x86"
    platform_data["OSXIntel64"]["enabled"] = 1
    platform_data["OSXIntel64"]["settings"]["CPU"] = "x86_64"
    platform_data["OSXUniversal"]["enabled"] = 1
    platform_data["OSXUniversal"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Linux"]["enabled"] = 1
    platform_data["Linux"]["settings"]["CPU"] = "x86"
    platform_data["Linux64"]["enabled"] = 1
    platform_data["Linux64"]["settings"]["CPU"] = "x86_64"
    platform_data["LinuxUniversal"]["enabled"] = 1
    platform_data["LinuxUniversal"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Win"]["enabled"] = 1
    platform_data["Win"]["settings"]["CPU"] = "x86"
    platform_data["Win64"]["enabled"] = 1
    platform_data["Win64"]["settings"]["CPU"] = "x86_64"

    expected_metadata = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    platform_data = expected_metadata["PluginImporter"]["platformData"]
    platform_data["Any"]["enabled"] = 0
    platform_data["Any"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Editor"]["enabled"] = 1
    platform_data["Editor"]["settings"]["CPU"] = "AnyCPU"
    platform_data["OSXIntel"]["enabled"] = 1
    platform_data["OSXIntel"]["settings"]["CPU"] = "x86"
    platform_data["OSXIntel64"]["enabled"] = 1
    platform_data["OSXIntel64"]["settings"]["CPU"] = "x86_64"
    platform_data["OSXUniversal"]["enabled"] = 1
    platform_data["OSXUniversal"]["settings"]["CPU"] = "AnyCPU"
    filename = "Plugins/x86/Foo/bar.bundle"
    metadata = export_unity_package.Asset.disable_unsupported_platforms(
        copy.deepcopy(all_platforms_enabled), filename)
    self.assertEqual(expected_metadata, metadata)
    expected_metadata["labels"] = [
        "gvhp_exportpath-Plugins/x86/Foo/bar.bundle"]
    self.assertEqual(expected_metadata, export_unity_package.Asset(
        filename, None, all_platforms_enabled).importer_metadata)

    expected_metadata = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    platform_data = expected_metadata["PluginImporter"]["platformData"]
    platform_data["Any"]["enabled"] = 0
    platform_data["Any"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Editor"]["enabled"] = 1
    platform_data["Editor"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Linux"]["enabled"] = 1
    platform_data["Linux"]["settings"]["CPU"] = "x86"
    platform_data["Linux64"]["enabled"] = 1
    platform_data["Linux64"]["settings"]["CPU"] = "x86_64"
    platform_data["LinuxUniversal"]["enabled"] = 1
    platform_data["LinuxUniversal"]["settings"]["CPU"] = "AnyCPU"
    filename = "Assets/Plugins/x86_64/Foo/bar.so"
    metadata = export_unity_package.Asset.disable_unsupported_platforms(
        copy.deepcopy(all_platforms_enabled), filename)
    self.assertEqual(expected_metadata, metadata)
    expected_metadata["labels"] = [
        "gvhp_exportpath-Assets/Plugins/x86_64/Foo/bar.so"]
    self.assertEqual(expected_metadata, export_unity_package.Asset(
        filename, None, all_platforms_enabled).importer_metadata)

    expected_metadata = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    platform_data = expected_metadata["PluginImporter"]["platformData"]
    platform_data["Any"]["enabled"] = 0
    platform_data["Any"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Editor"]["enabled"] = 1
    platform_data["Editor"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Win"]["enabled"] = 1
    platform_data["Win"]["settings"]["CPU"] = "x86"
    platform_data["Win64"]["enabled"] = 1
    platform_data["Win64"]["settings"]["CPU"] = "x86_64"
    filename = "A/Path/To/Assets/Plugins/x86_64/Foo/bar.dll"
    metadata = export_unity_package.Asset.disable_unsupported_platforms(
        copy.deepcopy(all_platforms_enabled), filename)
    self.assertEqual(expected_metadata, metadata)
    expected_metadata["labels"] = [
        "gvhp_exportpath-A/Path/To/Assets/Plugins/x86_64/Foo/bar.dll"]
    self.assertEqual(expected_metadata, export_unity_package.Asset(
        filename, None, all_platforms_enabled).importer_metadata)

    expected_metadata = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    platform_data = expected_metadata["PluginImporter"]["platformData"]
    platform_data["Any"]["enabled"] = 0
    platform_data["Any"]["settings"]["CPU"] = "AnyCPU"
    filename = "Plugins/Plugin.dll"
    metadata = export_unity_package.Asset.disable_unsupported_platforms(
        copy.deepcopy(expected_metadata), filename)
    self.assertEqual(expected_metadata, metadata)
    expected_metadata["labels"] = ["gvhp_exportpath-Plugins/Plugin.dll"]
    self.assertEqual(expected_metadata, export_unity_package.Asset(
        filename, None, expected_metadata).importer_metadata)

  def test_platform_data_get_entry(self):
    """Retrieve an entry from a PluginImporter.platformData list."""
    unity_5_6_format = collections.OrderedDict([
        ("PluginImporter", collections.OrderedDict([
            ("serializedVersion", 2),
            ("platformData", [
                collections.OrderedDict([
                    ("data", collections.OrderedDict([
                        ("first", collections.OrderedDict([
                            ("Standalone", "Linux")])),
                        ("second", collections.OrderedDict([
                            ("enabled", 1)]))])
                    )])
            ])
        ]))
    ])
    first, second = export_unity_package.Asset.platform_data_get_entry(
        unity_5_6_format["PluginImporter"]["platformData"][0])
    self.assertEqual(collections.OrderedDict([("Standalone", "Linux")]), first)
    self.assertEqual(collections.OrderedDict([("enabled", 1)]), second)

    unity_2017_format = collections.OrderedDict([
        ("PluginImporter", collections.OrderedDict([
            ("serializedVersion", 2),
            ("platformData", [
                collections.OrderedDict([
                    ("first", collections.OrderedDict([
                        ("Standalone", "Linux")])),
                    ("second", collections.OrderedDict([
                        ("enabled", 1)]))])
            ])
        ]))
    ])
    first, second = export_unity_package.Asset.platform_data_get_entry(
        unity_2017_format["PluginImporter"]["platformData"][0])
    self.assertEqual(collections.OrderedDict([("Standalone", "Linux")]), first)
    self.assertEqual(collections.OrderedDict([("enabled", 1)]), second)

  def test_set_cpu_for_desktop_platforms_serializationv1(self):
    """Set CPU field for enabled desktop platforms in v1 metadata format."""
    linux_enabled = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    linux_enabled["PluginImporter"]["platformData"]["Linux"]["enabled"] = 1
    expected_metadata = copy.deepcopy(linux_enabled)
    expected_metadata["PluginImporter"]["platformData"]["Linux"]["settings"][
        "CPU"] = "x86"
    linux_enabled_with_cpu = (
        export_unity_package.Asset.set_cpu_for_desktop_platforms(linux_enabled))
    self.assertEqual(expected_metadata, linux_enabled_with_cpu)

  def test_set_cpu_for_desktop_platforms_serializationv2(self):
    """Set CPU field for enabled desktop platforms in v2 metadata format."""
    linux_enabled = collections.OrderedDict([
        ("PluginImporter", collections.OrderedDict([
            ("serializedVersion", 2),
            ("platformData", [
                collections.OrderedDict([
                    ("first", collections.OrderedDict([
                        ("Standalone", "Linux")])),
                    ("second", collections.OrderedDict([
                        ("enabled", 1)]))])
            ])
        ]))
    ])
    expected_metadata = copy.deepcopy(linux_enabled)
    expected_metadata["PluginImporter"]["platformData"][0]["second"][
        "settings"] = collections.OrderedDict([("CPU", "x86")])
    linux_enabled_with_cpu = (
        export_unity_package.Asset.set_cpu_for_desktop_platforms(linux_enabled))
    self.assertEqual(expected_metadata, linux_enabled_with_cpu)

  def test_set_cpu_for_android_serializationv1(self):
    """Set CPU field for the enabled Android platform in v1 metadata format."""
    android_enabled = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    android_enabled["PluginImporter"]["platformData"]["Android"]["enabled"] = 1
    expected_metadata = copy.deepcopy(android_enabled)
    expected_metadata["PluginImporter"]["platformData"]["Android"]["settings"][
        "CPU"] = "ARMv7"
    android_enabled_with_cpu = (
        export_unity_package.Asset.set_cpu_for_android(android_enabled, "ARMv7"))
    self.assertEqual(expected_metadata, android_enabled_with_cpu)

  def test_set_cpu_for_android_serializationv2(self):
    """Set CPU field for the enabled Android platform in v2 metadata format."""
    android_enabled = collections.OrderedDict([
        ("PluginImporter", collections.OrderedDict([
            ("serializedVersion", 2),
            ("platformData", [
                collections.OrderedDict([
                    ("first", collections.OrderedDict([
                        ("Android", "Android")])),
                    ("second", collections.OrderedDict([
                        ("enabled", 1)]))])
            ])
        ]))
    ])
    expected_metadata = copy.deepcopy(android_enabled)
    expected_metadata["PluginImporter"]["platformData"][0]["second"][
        "settings"] = collections.OrderedDict([("CPU", "ARMv7")])
    android_enabled_with_cpu = (
        export_unity_package.Asset.set_cpu_for_android(android_enabled, "ARMv7"))
    self.assertEqual(expected_metadata, android_enabled_with_cpu)

  def test_apply_any_platform_selection_serializationv1(self):
    """Modify v1 importer metadata to enable all platforms."""
    # Enable all platforms.
    any_platform_enabled = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    platform_data = any_platform_enabled["PluginImporter"]["platformData"]
    platform_data["Any"]["enabled"] = 1
    # Remove some platforms, these should be re-added to the metadata and
    # enabled.
    del platform_data["Win"]
    del platform_data["Win64"]
    del platform_data["WindowsStoreApps"]
    del platform_data["iOS"]
    del platform_data["tvOS"]

    expected_metadata = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    platform_data = expected_metadata["PluginImporter"]["platformData"]
    platform_data["Android"]["enabled"] = 1
    platform_data["Any"]["enabled"] = 1
    platform_data["Editor"]["enabled"] = 1
    platform_data["Linux"]["enabled"] = 1
    platform_data["Linux64"]["enabled"] = 1
    platform_data["LinuxUniversal"]["enabled"] = 1
    platform_data["OSXIntel"]["enabled"] = 1
    platform_data["OSXIntel64"]["enabled"] = 1
    platform_data["OSXUniversal"]["enabled"] = 1
    platform_data["Web"]["enabled"] = 1
    platform_data["WebStreamed"]["enabled"] = 1
    platform_data["Win"]["enabled"] = 1
    platform_data["Win64"]["enabled"] = 1
    platform_data["WindowsStoreApps"]["enabled"] = 1
    platform_data["iOS"]["enabled"] = 1
    platform_data["tvOS"]["enabled"] = 1

    all_platforms_enabled = (
        export_unity_package.Asset.apply_any_platform_selection(
            any_platform_enabled))
    self.assertEqual(expected_metadata, all_platforms_enabled)

    # If Any is disabled, do not modify any platform states.
    unmodified_metadata = (
        export_unity_package.Asset.apply_any_platform_selection(
            copy.deepcopy(
                export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)))
    self.assertEqual(export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE,
                     unmodified_metadata)

  def test_apply_any_platform_selection_serializationv2(self):
    """Modify v2 importer metadata to enable all platforms."""
    def create_default_platform_data(target, name):
      """Create a platform data entry.

      Args:
        target: Name of the build target.
        name: Name of the platform.

      Returns:
        Ordered dictionary with the platformData metadata entry.
      """
      return collections.OrderedDict([
          ("first", collections.OrderedDict([(target, name)])),
          ("second", collections.OrderedDict([("enabled", 0)]))])

    all_platform_data = [create_default_platform_data("Any", None)]
    for platform in export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE[
        "PluginImporter"]["platformData"]:
      if platform != "Any":
        all_platform_data.append(create_default_platform_data(
            export_unity_package.PLATFORM_TARGET_BY_PLATFORM[platform],
            platform))

    all_disabled = collections.OrderedDict([
        ("PluginImporter", collections.OrderedDict([
            ("serializedVersion", 2),
            ("platformData", copy.deepcopy(all_platform_data)),
        ]))
    ])

    # If "Any" isn't enabled, make sure the data isn't modified.
    unmodified_metadata = (
        export_unity_package.Asset.apply_any_platform_selection(
            copy.deepcopy(all_disabled)))
    self.assertEqual(all_disabled, unmodified_metadata)

    # Enable the "Any" platform (first in the list) and remove some platforms
    # from the list then verify all platforms are enabled.
    any_enabled = copy.deepcopy(all_disabled)
    platform_data = any_enabled["PluginImporter"]["platformData"]
    platform_data[0]["second"]["enabled"] = 1
    any_enabled["PluginImporter"]["platformData"] = platform_data[:-3]

    all_enabled = export_unity_package.Asset.apply_any_platform_selection(
        any_enabled)
    expected_metadata = copy.deepcopy(all_disabled)
    for config in expected_metadata["PluginImporter"]["platformData"]:
      config["second"]["enabled"] = 1
    self.assertEqual(expected_metadata, all_enabled)

  def test_create_metadata(self):
    """Test Asset.create_metadata()."""
    asset = export_unity_package.Asset(
        "Google.VersionHandler.dll",
        os.path.join(self.assets_dir,
                     "PlayServicesResolver/Editor/Google.VersionHandler.dll"),
        self.default_metadata)

    expected_metadata_path = os.path.join(self.staging_dir,
                                          "Google.VersionHandler.dll.meta")

    asset.create_metadata(expected_metadata_path,
                          "06f6f385a4ad409884857500a3c04441",
                          timestamp=123456789)

    # Check metadata
    with open(expected_metadata_path) as (metadata):
      self.assertEqual(
          "fileFormatVersion: 2\n"
          "guid: 06f6f385a4ad409884857500a3c04441\n"
          "labels:\n"
          "- gvh\n"
          "- gvh_version-1.2.3\n"
          "- gvhp_exportpath-Google.VersionHandler.dll\n"
          "timeCreated: 123456789\n"
          "DefaultImporter:\n"
          "  userData:\n"
          "  assetBundleName:\n"
          "  assetBundleVariant:\n", metadata.read())

  def test_create_metadata_folder(self):
    """Test Asset.create_metadata()."""
    asset = export_unity_package.Asset(
        "foo/bar", "foo/bar", self.default_metadata, is_folder=True)

    expected_metadata_path = os.path.join(self.staging_dir,
                                          "Google.VersionHandler.dll.meta")

    asset.create_metadata(expected_metadata_path,
                          "5187848eea9240faaec2deb7d66107db")

    # Check metadata
    with open(expected_metadata_path) as (metadata):
      self.assertEqual(
          "fileFormatVersion: 2\n"
          "guid: 5187848eea9240faaec2deb7d66107db\n"
          "timeCreated: 0\n"
          "folderAsset: true\n"
          "DefaultImporter:\n"
          "  userData:\n"
          "  assetBundleName:\n"
          "  assetBundleVariant:\n", metadata.read())

  def test_write(self):
    """Test Asset.write()."""
    asset_path = os.path.join(
        self.assets_dir,
        "PlayServicesResolver/Editor/Google.VersionHandler.dll")

    asset = export_unity_package.Asset("foo/bar/Google.VersionHandler.dll",
                                       asset_path, self.default_metadata)

    expected_asset_path = os.path.join(
        self.staging_dir, "06f6f385a4ad409884857500a3c04441/asset")
    expected_metadata_path = os.path.join(
        self.staging_dir, "06f6f385a4ad409884857500a3c04441/asset.meta")
    expected_pathname_path = os.path.join(
        self.staging_dir, "06f6f385a4ad409884857500a3c04441/pathname")

    asset.write(self.staging_dir, "06f6f385a4ad409884857500a3c04441",
                timestamp=123456789)

    # Compare asset
    self.assertTrue(filecmp.cmp(asset_path, expected_asset_path))

    # Check metadata
    with open(expected_metadata_path) as (metadata):
      self.assertEqual(
          "fileFormatVersion: 2\n"
          "guid: 06f6f385a4ad409884857500a3c04441\n"
          "labels:\n"
          "- gvh\n"
          "- gvh_version-1.2.3\n"
          "- gvhp_exportpath-foo/bar/Google.VersionHandler.dll\n"
          "timeCreated: 123456789\n"
          "DefaultImporter:\n"
          "  userData:\n"
          "  assetBundleName:\n"
          "  assetBundleVariant:\n", metadata.read())

    # Check pathname file
    with open(expected_pathname_path) as (pathname):
      self.assertEqual("Assets/foo/bar/Google.VersionHandler.dll",
                       pathname.read())

  def test_write_folder(self):
    """Test Asset.write() for folder asset."""

    asset = export_unity_package.Asset(
        "foo/bar", "foo/bar", self.default_metadata, is_folder=True)

    output_dir = asset.write(self.staging_dir,
                             "5187848eea9240faaec2deb7d66107db")

    # Should do nothing when writing a folder asset.
    self.assertIsNone(output_dir)

  def test_write_upm(self):
    """Test Asset.write_upm()."""
    asset_path = os.path.join(
        self.assets_dir,
        "PlayServicesResolver/Editor/Google.VersionHandler.dll")

    asset = export_unity_package.Asset("foo/bar/Google.VersionHandler.dll",
                                       asset_path, self.default_metadata)

    expected_asset_path = os.path.join(
        self.staging_dir, "package/foo/bar/Google.VersionHandler.dll")
    expected_metadata_path = os.path.join(
        self.staging_dir, "package/foo/bar/Google.VersionHandler.dll.meta")

    asset.write_upm(self.staging_dir, "06f6f385a4ad409884857500a3c04441",
                    timestamp=123456789)

    # Compare asset
    self.assertTrue(filecmp.cmp(asset_path, expected_asset_path))

    # Check metadata
    with open(expected_metadata_path) as (metadata):
      self.assertEqual(
          "fileFormatVersion: 2\n"
          "guid: 06f6f385a4ad409884857500a3c04441\n"
          "labels:\n"
          "- gvh\n"
          "- gvh_version-1.2.3\n"
          "- gvhp_exportpath-foo/bar/Google.VersionHandler.dll\n"
          "timeCreated: 123456789\n"
          "DefaultImporter:\n"
          "  userData:\n"
          "  assetBundleName:\n"
          "  assetBundleVariant:\n", metadata.read())

  def test_write_upm_folder(self):
    """Test Asset.write_upm() for folder asset."""
    asset = export_unity_package.Asset(
        "foo/bar", "foo/bar", self.default_metadata, is_folder=True)

    expected_folder_path = os.path.join(self.staging_dir, "package/foo/bar")
    expected_metadata_path = os.path.join(self.staging_dir,
                                          "package/foo/bar.meta")

    asset.write_upm(self.staging_dir, "5187848eea9240faaec2deb7d66107db")

    # Compare asset
    self.assertTrue(os.path.isdir(expected_folder_path))

    # Check metadata
    with open(expected_metadata_path) as (metadata):
      self.assertEqual(
          "fileFormatVersion: 2\n"
          "guid: 5187848eea9240faaec2deb7d66107db\n"
          "timeCreated: 0\n"
          "folderAsset: true\n"
          "DefaultImporter:\n"
          "  userData:\n"
          "  assetBundleName:\n"
          "  assetBundleVariant:\n", metadata.read())


class AssetConfigurationTest(absltest.TestCase):
  """Test the AssetConfiguration class."""

  def setUp(self):
    """Create an empty package config and expected metadata."""
    super(AssetConfigurationTest, self).setUp()
    self.package = export_unity_package.PackageConfiguration(
        export_unity_package.ProjectConfiguration({}, set(), "1.2.3"),
        {"name": "Test.unitypackage", "manifest_path": "Foo/Bar"})
    self.labels = set(["gvh", "gvh_version-1.2.3"])
    self.default_metadata = copy.deepcopy(
        export_unity_package.DEFAULT_IMPORTER_METADATA_TEMPLATE)
    self.default_metadata["labels"] = sorted(list(self.labels))
    self.plugin_metadata = copy.deepcopy(
        export_unity_package.PLUGIN_IMPORTER_METADATA_TEMPLATE)
    self.plugin_metadata["labels"] = sorted(list(self.labels))
    self.override_metadata = {
        "PluginImporter": {
            "platformData": {
                "Editor": {
                    "enabled": 1
                }
            }
        }
    }

  def test_create_empty(self):
    """Create an empty config."""
    config = export_unity_package.AssetConfiguration(self.package, {})
    self.assertEqual(self.default_metadata, config.importer_metadata)
    self.assertEqual(set(self.labels), config.labels)
    self.assertEqual(set(), config.paths)
    self.assertEqual({}, config.override_metadata)

  def test_labels(self):
    """Test labels property."""
    self.assertEqual(
        self.labels.union(set(["fun-label"])),
        export_unity_package.AssetConfiguration(self.package, {
            "importer": "DefaultImporter",
            "labels": ["fun-label"]
        }).labels)

  def test_paths(self):
    """Test paths property."""
    self.assertEqual(
        set([
            "foo/bar",
            "bar",
        ]),
        export_unity_package.AssetConfiguration(self.package, {
            "importer": "DefaultImporter",
            "paths": [
                "bar",
                "foo/bar",
            ]
        }).paths)

  def test_override_metadata(self):
    """Test override_metadata property."""
    self.assertEqual(
        self.override_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {
                "importer": "DefaultImporter",
                "override_metadata": self.override_metadata
            }).override_metadata)

  def test_override_metadata_upm(self):
    """Test override_metadata_upm property."""
    self.assertEqual(
        self.override_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {
                "importer": "DefaultImporter",
                "override_metadata_upm": self.override_metadata
            }).override_metadata_upm)

  def test_importer_metadata_default(self):
    """Create default metadata."""
    self.assertEqual(
        self.default_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {"importer": "DefaultImporter"}).importer_metadata)

  def test_importer_metadata_invalid(self):
    """Try to create metadata with an invalid importer."""
    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      unused_metadata = export_unity_package.AssetConfiguration(
          self.package, {"importer": "InvalidImporter"}).importer_metadata

  def test_create_importer_metadata_editor_only(self):
    """Create metadata that only targets the editor."""
    self.plugin_metadata["PluginImporter"]["platformData"]["Editor"][
        "enabled"] = 1
    self.assertEqual(
        self.plugin_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {"importer": "PluginImporter",
                           "platforms": ["Editor"]}).importer_metadata)

  def test_importer_metadata_android_only(self):
    """Create metadata that only targets Android."""
    self.plugin_metadata["PluginImporter"]["platformData"]["Android"][
        "enabled"] = 1
    self.assertEqual(
        self.plugin_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {"importer": "PluginImporter",
                           "platforms": ["Android"]}).importer_metadata)

  def test_importer_metadata_android_only_armv7(self):
    """Create metadata with ARMv7 CPU set."""
    self.plugin_metadata["PluginImporter"]["platformData"]["Android"][
        "enabled"] = 1
    self.plugin_metadata["PluginImporter"]["platformData"]["Android"][
        "settings"]["CPU"] = "ARMv7"
    self.assertEqual(
        self.plugin_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {"importer": "PluginImporter",
                           "platforms": ["Android"],
                           "cpu": "ARMv7"}).importer_metadata)

  def test_importer_metadata_ios_only(self):
    """Create metadata that only targets iOS."""
    self.plugin_metadata["PluginImporter"]["platformData"]["iOS"]["enabled"] = 1
    self.assertEqual(
        self.plugin_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {"importer": "PluginImporter",
                           "platforms": ["iOS"]}).importer_metadata)

  def test_importer_metadata_tvos_only(self):
    """Create metadata that only targets tvOS."""
    self.plugin_metadata["PluginImporter"]["platformData"]["tvOS"]["enabled"] = 1
    self.assertEqual(
        self.plugin_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {"importer": "PluginImporter",
                           "platforms": ["tvOS"]}).importer_metadata)

  def test_importer_metadata_standalone_invalid_cpu(self):
    """Create metadata with an invalid CPU."""
    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      unused_metadata = export_unity_package.AssetConfiguration(
          self.package, {"importer": "PluginImporter",
                         "platforms": ["Standalone"],
                         "cpu": "Crusoe"}).importer_metadata

  def test_importer_metadata_standalone_only_any_cpu(self):
    """Create metadata that only targets standalone (desktop)."""
    platform_data = self.plugin_metadata["PluginImporter"]["platformData"]
    platform_data["Linux"]["enabled"] = 1
    platform_data["Linux"]["settings"]["CPU"] = "x86"
    platform_data["Linux64"]["enabled"] = 1
    platform_data["Linux64"]["settings"]["CPU"] = "x86_64"
    platform_data["LinuxUniversal"]["enabled"] = 1
    platform_data["LinuxUniversal"]["settings"]["CPU"] = "AnyCPU"
    platform_data["OSXIntel"]["enabled"] = 1
    platform_data["OSXIntel"]["settings"]["CPU"] = "x86"
    platform_data["OSXIntel64"]["enabled"] = 1
    platform_data["OSXIntel64"]["settings"]["CPU"] = "x86_64"
    platform_data["OSXUniversal"]["enabled"] = 1
    platform_data["OSXUniversal"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Win"]["enabled"] = 1
    platform_data["Win"]["settings"]["CPU"] = "x86"
    platform_data["Win64"]["enabled"] = 1
    platform_data["Win64"]["settings"]["CPU"] = "x86_64"
    self.assertEqual(
        self.plugin_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {"importer": "PluginImporter",
                           "platforms": ["Standalone"]}).importer_metadata)

  def test_importer_metadata_standalone_only_x86(self):
    """Create metadata that only targets standalone (desktop) x86."""
    platform_data = self.plugin_metadata["PluginImporter"]["platformData"]
    platform_data["Linux"]["enabled"] = 1
    platform_data["Linux"]["settings"]["CPU"] = "x86"
    platform_data["LinuxUniversal"]["enabled"] = 1
    platform_data["LinuxUniversal"]["settings"]["CPU"] = "AnyCPU"
    platform_data["OSXIntel"]["enabled"] = 1
    platform_data["OSXIntel"]["settings"]["CPU"] = "x86"
    platform_data["OSXUniversal"]["enabled"] = 1
    platform_data["OSXUniversal"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Win"]["enabled"] = 1
    platform_data["Win"]["settings"]["CPU"] = "x86"
    self.assertEqual(
        self.plugin_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {"importer": "PluginImporter",
                           "platforms": ["Standalone"],
                           "cpu": "x86"}).importer_metadata)

  def test_importer_metadata_standalone_only_x86_64(self):
    """Create metadata that only targets standalone (desktop) x86_64."""
    platform_data = self.plugin_metadata["PluginImporter"]["platformData"]
    platform_data["Linux64"]["enabled"] = 1
    platform_data["Linux64"]["settings"]["CPU"] = "x86_64"
    platform_data["LinuxUniversal"]["enabled"] = 1
    platform_data["LinuxUniversal"]["settings"]["CPU"] = "AnyCPU"
    platform_data["OSXIntel64"]["enabled"] = 1
    platform_data["OSXIntel64"]["settings"]["CPU"] = "x86_64"
    platform_data["OSXUniversal"]["enabled"] = 1
    platform_data["OSXUniversal"]["settings"]["CPU"] = "AnyCPU"
    platform_data["Win64"]["enabled"] = 1
    platform_data["Win64"]["settings"]["CPU"] = "x86_64"
    self.assertEqual(
        self.plugin_metadata,
        export_unity_package.AssetConfiguration(
            self.package, {"importer": "PluginImporter",
                           "platforms": ["Standalone"],
                           "cpu": "x86_64"}).importer_metadata)


class AssetPackageAndProjectFileOperationsTest(absltest.TestCase):
  """Tests for file operation methods."""

  def setUp(self):
    """Unpack resources to a temporary directory."""
    super(AssetPackageAndProjectFileOperationsTest, self).setUp()
    self.old_flag = FLAGS.enforce_semver
    FLAGS.enforce_semver = False
    self.assets_dir = os.path.join(TEST_DATA_PATH, "Assets")
    self.staging_dir = os.path.join(FLAGS.test_tmpdir, "staging")
    self.package = export_unity_package.PackageConfiguration(
        export_unity_package.ProjectConfiguration({}, set(), None),
        {"name": "Test.unitypackage"})
    os.makedirs(self.staging_dir)
    self.version_handler_dll_metadata = collections.OrderedDict(
        [("fileFormatVersion", 2),
         ("guid", "06f6f385a4ad409884857500a3c04441"),
         ("labels", ["gvh", "gvh_teditor", "gvh_v1.2.86.0",
                     "gvhp_exportpath-PlayServicesResolver/Editor/" +
                     "Google.VersionHandler.dll"]),
         ("PluginImporter", collections.OrderedDict(
             [("externalObjects", collections.OrderedDict()),
              ("serializedVersion", 2),
              ("iconMap", collections.OrderedDict()),
              ("executionOrder", collections.OrderedDict()),
              ("isPreloaded", 0),
              ("isOverridable", 0),
              ("platformData", [
                  collections.OrderedDict(
                      [("first", collections.OrderedDict(
                          [("Any", None)])),
                       ("second", collections.OrderedDict(
                           [("enabled", 0),
                            ("settings", collections.OrderedDict())]))]),
                  collections.OrderedDict(
                      [("first", collections.OrderedDict(
                          [("Editor", "Editor")])),
                       ("second", collections.OrderedDict(
                           [("enabled", 1),
                            ("settings", collections.OrderedDict(
                                [("DefaultValueInitialized", True)]))]))]),
                  collections.OrderedDict(
                      [("first", collections.OrderedDict(
                          [("Windows Store Apps", "WindowsStoreApps")])),
                       ("second", collections.OrderedDict(
                           [("enabled", 0),
                            ("settings", collections.OrderedDict(
                                [("CPU", "AnyCPU")]))]))])]),
              ("userData", None),
              ("assetBundleName", None),
              ("assetBundleVariant", None)]))])

    self.expected_metadata_analytics = copy.deepcopy(
        export_unity_package.DEFAULT_IMPORTER_METADATA_TEMPLATE)
    self.expected_metadata_analytics["labels"] = [
        "gvhp_exportpath-Firebase/Plugins/Firebase.Analytics.dll"]
    self.expected_metadata_app = copy.deepcopy(
        export_unity_package.DEFAULT_IMPORTER_METADATA_TEMPLATE)
    self.expected_metadata_app["labels"] = [
        "gvhp_exportpath-Firebase/Plugins/Firebase.App.dll"]
    self.expected_metadata_auth = copy.deepcopy(
        export_unity_package.DEFAULT_IMPORTER_METADATA_TEMPLATE)
    self.expected_metadata_auth["labels"] = [
        "gvhp_exportpath-Firebase/Plugins/Firebase.Auth.dll"]

  def tearDown(self):
    """Clean up the temporary directory."""
    super(AssetPackageAndProjectFileOperationsTest, self).tearDown()
    FLAGS.enforce_semver = self.old_flag
    delete_temporary_directory_contents()

  def test_find_files_no_wildcards(self):
    """Walk a set of paths with no wildcards."""
    config = export_unity_package.AssetConfiguration(
        self.package,
        {"paths": ["Firebase/Plugins/Firebase.App.dll",
                   "Firebase/Plugins/Firebase.Analytics.dll"]})
    found_assets = config.find_assets([self.assets_dir])

    self.assertCountEqual(
        [("Firebase/Plugins/Firebase.Analytics.dll",
          self.expected_metadata_analytics),
         ("Firebase/Plugins/Firebase.App.dll", self.expected_metadata_app)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets])

  def test_find_files_non_existant_file(self):
    """Walk a set of paths with no wildcards."""
    config = export_unity_package.AssetConfiguration(
        self.package,
        {"paths": ["Firebase/Plugins/Firebase.Analytics.dll",
                   "Firebase/AFileThatDoesNotExist"]})
    found_assets = config.find_assets([self.assets_dir])
    self.assertEqual(
        ["Firebase/Plugins/Firebase.Analytics.dll"],
        [asset.filename for asset in found_assets])

  def test_find_assets_using_directory(self):
    """Walk a set of paths using a directory."""
    config = export_unity_package.AssetConfiguration(
        self.package, {"paths": ["Firebase"]})
    found_assets = config.find_assets([self.assets_dir])
    self.assertCountEqual(
        [("Firebase/Plugins/Firebase.Analytics.dll",
          self.expected_metadata_analytics),
         ("Firebase/Plugins/Firebase.App.dll", self.expected_metadata_app),
         ("Firebase/Plugins/Firebase.Auth.dll", self.expected_metadata_auth)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets])

  def test_find_assets_in_multiple_directories(self):
    """Search multiple directories for assets."""
    config = export_unity_package.AssetConfiguration(
        self.package, {"paths": ["Plugins/Firebase.Analytics.dll",
                                 "Editor/Google.VersionHandler.dll"]})
    found_assets = config.find_assets(
        [os.path.join(self.assets_dir, "Firebase"),
         os.path.join(self.assets_dir, "PlayServicesResolver")])
    self.assertCountEqual(
        [export_unity_package.posix_path(os.path.join(
            self.assets_dir, "Firebase/Plugins/Firebase.Analytics.dll")),
         export_unity_package.posix_path(os.path.join(
             self.assets_dir,
             "PlayServicesResolver/Editor/Google.VersionHandler.dll"))],
        [asset.filename_absolute for asset in found_assets])

  def test_find_assets_using_wildcard(self):
    """Walk a set of paths using a wildcard."""
    config = export_unity_package.AssetConfiguration(
        self.package, {"paths": ["Firebase/Plugins/Firebase.A*t*.dll"]})
    found_assets = config.find_assets([self.assets_dir])
    self.assertCountEqual(
        [("Firebase/Plugins/Firebase.Analytics.dll",
          self.expected_metadata_analytics),
         ("Firebase/Plugins/Firebase.Auth.dll", self.expected_metadata_auth)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets])

  def test_find_assets_with_metadata(self):
    """Walk a set of paths using a wildcard with metadata."""
    config = export_unity_package.AssetConfiguration(
        self.package,
        {"paths": ["PlayServicesResolver/Editor/Google.VersionHandler.*"]})
    found_assets = config.find_assets([self.assets_dir])
    self.assertCountEqual(
        [("PlayServicesResolver/Editor/Google.VersionHandler.dll",
          self.version_handler_dll_metadata)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets])

  def test_find_assets_with_override_metadata(self):
    """Find an asset and override parts of its metadata."""
    config = export_unity_package.AssetConfiguration(
        self.package,
        collections.OrderedDict([
            ("paths", ["PlayServicesResolver/Editor/Google.VersionHandler.*"]),
            ("override_metadata", collections.OrderedDict([
                ("PluginImporter", collections.OrderedDict([
                    ("platformData", [collections.OrderedDict([
                        ("first", collections.OrderedDict([
                            ("Editor", "Editor")])),
                        ("second", collections.OrderedDict([
                            ("enabled", 0)]))
                    ])])
                ]))
            ]))
        ]))
    expected_metadata = copy.deepcopy(self.version_handler_dll_metadata)
    expected_metadata["PluginImporter"]["platformData"][1]["second"][
        "enabled"] = 0

    # Metadata with find_assets(for_upm=False) should be overridden.
    found_assets = config.find_assets([self.assets_dir])
    self.assertCountEqual(
        [("PlayServicesResolver/Editor/Google.VersionHandler.dll",
          expected_metadata)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets])

    # Metadata with find_assets(for_upm=True) should be overridden.
    found_assets_upm = config.find_assets([self.assets_dir], for_upm=True)
    self.assertCountEqual(
        [("PlayServicesResolver/Editor/Google.VersionHandler.dll",
          expected_metadata)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets_upm
        ])

  def test_find_assets_with_override_metadata_upm(self):
    """Find an asset and override parts of its metadata."""
    config = export_unity_package.AssetConfiguration(
        self.package,
        collections.OrderedDict([
            ("paths", ["PlayServicesResolver/Editor/Google.VersionHandler.*"]),
            ("override_metadata_upm",
             collections.OrderedDict([
                 ("PluginImporter",
                  collections.OrderedDict([("platformData", [
                      collections.OrderedDict([
                          ("first",
                           collections.OrderedDict([("Editor", "Editor")])),
                          ("second", collections.OrderedDict([("enabled", 0)]))
                      ])
                  ])]))
             ]))
        ]))

    # Metadata with find_assets(for_upm=False) should remain unchanged.
    found_assets = config.find_assets([self.assets_dir])
    self.assertCountEqual(
        [("PlayServicesResolver/Editor/Google.VersionHandler.dll",
          self.version_handler_dll_metadata)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets])

    # Metadata with find_assets(for_upm=True) should be overridden.
    expected_metadata = copy.deepcopy(self.version_handler_dll_metadata)
    expected_metadata["PluginImporter"]["platformData"][1]["second"][
        "enabled"] = 0
    found_assets_upm = config.find_assets([self.assets_dir], for_upm=True)
    self.assertCountEqual(
        [("PlayServicesResolver/Editor/Google.VersionHandler.dll",
          expected_metadata)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets_upm
        ])

  def test_find_assets_with_exclusions(self):
    """Find assets for a package with a subset of files excluded."""
    config_json = {
        "packages": [
            {"name": "FirebaseAppAndAuth.unitypackage",
             "imports": [
                 {"paths": [
                     "Firebase/Plugins/Firebase.App.dll",
                     "Firebase/Plugins/Firebase.Auth.dll"
                 ]}
             ]},
            {"name": "PlayServicesResolver.unitypackage",
             "imports": [
                 {"paths": [
                     "PlayServicesResolver/Editor/Google.VersionHandler.dll"
                 ]}
             ]},
            {"name": "FirebaseAnalytics.unitypackage",
             "imports": [
                 {"paths": [
                     "Firebase/Plugins/Firebase.Analytics.dll"
                 ]}
             ],
             "exclude_paths": [r".*\.Auth\.dll$"],
             "includes": ["FirebaseAppAndAuth.unitypackage",
                          "PlayServicesResolver.unitypackage"]
            }
        ]
    }
    config = export_unity_package.ProjectConfiguration(config_json, set(), None)
    package = config.packages_by_name["FirebaseAnalytics.unitypackage"]
    found_assets = package.find_assets([self.assets_dir])
    self.assertCountEqual(
        [("Firebase/Plugins/Firebase.Analytics.dll",
          self.expected_metadata_analytics),
         ("Firebase/Plugins/Firebase.App.dll", self.expected_metadata_app),
         ("PlayServicesResolver/Editor/Google.VersionHandler.dll",
          self.version_handler_dll_metadata)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets])

  def test_find_assets_via_includes(self):
    """Find assets by transitively searching included packages."""
    config_json = {
        "packages": [
            {"name": "FirebaseApp.unitypackage",
             "imports": [{"paths": ["Firebase/Plugins/Firebase.App.dll"]}]},
            {"name": "PlayServicesResolver.unitypackage",
             "imports": [
                 {"paths": [
                     "PlayServicesResolver/Editor/Google.VersionHandler.dll"
                 ]}
             ]},
            {"name": "FirebaseAnalytics.unitypackage",
             "imports": [
                 {"paths": [
                     "Firebase/Plugins/Firebase.Analytics.dll"
                 ]}
             ],
             "includes": ["FirebaseApp.unitypackage",
                          "PlayServicesResolver.unitypackage"]
            }
        ]
    }
    config = export_unity_package.ProjectConfiguration(config_json, set(), None)
    package = config.packages_by_name["FirebaseAnalytics.unitypackage"]
    found_assets = package.find_assets([self.assets_dir])

    self.assertCountEqual(
        [("Firebase/Plugins/Firebase.Analytics.dll",
          self.expected_metadata_analytics),
         ("Firebase/Plugins/Firebase.App.dll", self.expected_metadata_app),
         ("PlayServicesResolver/Editor/Google.VersionHandler.dll",
          self.version_handler_dll_metadata)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets])

  def test_find_assets_via_includes_with_conflicting_metadata(self):
    """Find assets with conflicting metadata."""
    config_json = {
        "packages": [
            {"name": "PlayServicesResolver.unitypackage",
             "imports": [
                 {"paths": [
                     "PlayServicesResolver/Editor/Google.VersionHandler.dll"
                 ]}
             ]},
            {"name": "PlayServicesResolverConflicting.unitypackage",
             "imports": [
                 {"labels": ["conflicting"],
                  "paths": [
                      "PlayServicesResolver/Editor/Google.VersionHandler.dll"
                  ]
                 }
             ]
            },
            {"name": "FirebaseAnalytics.unitypackage",
             "imports": [
                 {"paths": ["Firebase/Plugins/Firebase.Analytics.dll"]}
             ],
             "includes": [
                 "PlayServicesResolver.unitypackage",
                 "PlayServicesResolverConflicting.unitypackage"
             ]
            }
        ]
    }

    config = export_unity_package.ProjectConfiguration(config_json, set(), None)
    package = config.packages_by_name["FirebaseAnalytics.unitypackage"]
    with self.assertRaises(export_unity_package.ProjectConfigurationError) as (
        context):
      package.find_assets([self.assets_dir])
    self.assertRegexMatch(
        str(context.exception),
        [r"File .*Google\.VersionHandler\.dll imported with different import "
         r"settings in .*PlayServicesResolver\.unitypackage', "
         r".*PlayServicesResolverConflicting\.unitypackage'"])

  def test_find_assets_for_upm(self):
    """Find assets for UPM package.

    This should exclude assets from included packages.
    """
    config_json = {
        "packages": [{
            "name": "FirebaseApp.unitypackage",
            "imports": [{
                "paths": ["Firebase/Plugins/Firebase.App.dll"]
            }]
        }, {
            "name":
                "PlayServicesResolver.unitypackage",
            "imports": [{
                "paths": [
                    "PlayServicesResolver/Editor/Google.VersionHandler.dll"
                ]
            }]
        }, {
            "name":
                "FirebaseAnalytics.unitypackage",
            "imports": [{
                "paths": ["Firebase/Plugins/Firebase.Analytics.dll"]
            }],
            "includes": [
                "FirebaseApp.unitypackage", "PlayServicesResolver.unitypackage"
            ]
        }]
    }
    config = export_unity_package.ProjectConfiguration(config_json, set(), None)
    package = config.packages_by_name["FirebaseAnalytics.unitypackage"]
    found_assets = package.find_assets([self.assets_dir], for_upm=True)

    self.assertCountEqual(
        [("Firebase/Plugins/Firebase.Analytics.dll",
          self.expected_metadata_analytics)],
        [(asset.filename, asset.importer_metadata) for asset in found_assets])

  def test_asset_write_metadata(self):
    """Write asset metadata to a file."""
    metadata_filename = os.path.join(FLAGS.test_tmpdir, "metadata")
    export_unity_package.Asset.write_metadata(
        metadata_filename,
        [collections.OrderedDict(
            [("someMetadata", None),
             ("otherData", "foo")
            ]),
         collections.OrderedDict(
             [("packageInfo",
               collections.OrderedDict(
                   [("version", 1),
                    ("format", "max")]))
             ]),
         collections.OrderedDict([("someMetadata", 1)]),
         collections.OrderedDict(
             [("packageInfo",
               collections.OrderedDict(
                   [("format", "ultra"),
                    ("enabled", 1)]))
             ]),
        ])
    with open(metadata_filename) as metadata_file:
      self.assertEqual("someMetadata: 1\n"
                       "otherData: foo\n"
                       "packageInfo:\n"
                       "  version: 1\n"
                       "  format: ultra\n"
                       "  enabled: 1\n",
                       metadata_file.read())

  def test_asset_write(self):
    """Add an asset to a plugin archive staging area."""
    asset = export_unity_package.Asset(
        "Firebase/Plugins/Firebase.App.dll",
        os.path.join(self.assets_dir, "Firebase/Plugins/Firebase.App.dll"),
        collections.OrderedDict(
            [("someMetadata", 1),
             ("otherData", "foo"),
             ("packageInfo",
              collections.OrderedDict(
                  [("version", 1)]))]))
    asset_dir = asset.write(self.staging_dir,
                            "fa42a2182ad64806b8f78e9de4cc4f78", 123456789)
    self.assertTrue(os.path.join(self.staging_dir,
                                 "fa42a2182ad64806b8f78e9de4cc4f78"),
                    asset_dir)
    self.assertTrue(filecmp.cmp(os.path.join(asset_dir, "asset"),
                                asset.filename_absolute))

    with open(os.path.join(asset_dir, "pathname")) as (
        asset_pathname_file):
      self.assertEqual("Assets/Firebase/Plugins/Firebase.App.dll",
                       asset_pathname_file.read())

    with open(os.path.join(asset_dir, "asset.meta")) as (
        asset_metadata_file):
      self.assertEqual(
          "fileFormatVersion: 2\n"
          "guid: fa42a2182ad64806b8f78e9de4cc4f78\n"
          "labels:\n"
          "- gvhp_exportpath-Firebase/Plugins/Firebase.App.dll\n"
          "timeCreated: 123456789\n"
          "someMetadata: 1\n"
          "otherData: foo\n"
          "packageInfo:\n"
          "  version: 1\n",
          asset_metadata_file.read())

  def test_package_create_archive(self):
    """Create a unitypackage archive."""
    test_case_dir = os.path.join(FLAGS.test_tmpdir, "test_create_archive")
    os.makedirs(test_case_dir)
    use_tar = export_unity_package.FLAGS.use_tar
    try:
      # Disable use of tar command line application until this is reproducible on
      # macOS.
      export_unity_package.FLAGS.use_tar = (platform.system() != "Darwin")

      archive_dir = os.path.join(test_case_dir, "archive_dir")
      os.makedirs(archive_dir)
      # Create some files to archive.
      test_files = {"a/b/c.txt": "hello",
                    "d/e.txt": "world"}
      for filename, contents in test_files.items():
        input_filename = os.path.join(os.path.join(archive_dir, filename))
        os.makedirs(os.path.dirname(input_filename))
        with open(input_filename, "wt") as input_file:
          input_file.write(contents)

      # Create an archive.
      archive_filename = os.path.join(test_case_dir, "archive.unitypackage")
      export_unity_package.PackageConfiguration.create_archive(archive_filename,
                                                               archive_dir, 0)

      # Unpack the archive and make sure the expected files were stored.
      with tarfile.open(archive_filename, "r:gz") as archive_file:
        self.assertCountEqual(
            ["a", "a/b", "a/b/c.txt", "d", "d/e.txt"],
            archive_file.getnames())
        for filename in test_files:
          embedded_file = archive_file.extractfile(filename)
          self.assertEqual(test_files[filename],
                           embedded_file.read().decode("utf8"))

      # Touch all files in the archive.
      for filename in test_files:
        input_filename = os.path.join(os.path.join(archive_dir, filename))
        file_stat = os.stat(input_filename)
        os.utime(input_filename, (file_stat.st_atime, file_stat.st_mtime - 60))

      # Create another archive.
      other_archive_filename = os.path.join(test_case_dir,
                                            "archive2.unitypackage")
      os.rename(archive_filename, other_archive_filename)
      # Wait before writing another archive so that any potential changes to
      # timestamps can be written.
      time.sleep(1)
      # We create the archive with the original filename as the filename is
      # embedded in the archive.
      export_unity_package.PackageConfiguration.create_archive(archive_filename,
                                                               archive_dir, 0)
      self.assertTrue(filecmp.cmp(archive_filename, other_archive_filename))

    finally:
      shutil.rmtree(test_case_dir)
      export_unity_package.FLAGS.use_tar = use_tar

  def test_package_write(self):
    """Write a .unitypackage file."""
    project = export_unity_package.ProjectConfiguration(
        {"packages": [
            {"name": "FirebaseApp.unitypackage",
             "manifest_path": "Firebase/Editor",
             "imports": [
                 {"paths": [
                     "Firebase/Plugins/Firebase.App.dll",
                     "PlayServicesResolver/Editor/Google.VersionHandler.dll",
                 ]}
             ]}
        ]}, set(), "1.0.0")
    package = project.packages_by_name["FirebaseApp.unitypackage"]
    unitypackage = package.write(
        export_unity_package.GuidDatabase(
            export_unity_package.DuplicateGuidsChecker(),
            {
                "1.0.0": {
                    "Firebase/Editor/FirebaseApp_version-1.0.0_manifest.txt":
                    "08d62f799cbd4b02a3ff77313706a3c0",
                    "Firebase/Plugins/Firebase.App.dll":
                    "7311924048bd457bac6d713576c952da"
                }
            }, "1.0.0"),
        [self.assets_dir], self.staging_dir, 0)

    self.assertEqual(os.path.join(self.staging_dir, "FirebaseApp.unitypackage"),
                     unitypackage)

    with tarfile.open(unitypackage, "r:gz") as unitypackage_file:
      self.assertCountEqual(
          ["06f6f385a4ad409884857500a3c04441",
           "06f6f385a4ad409884857500a3c04441/asset",
           "06f6f385a4ad409884857500a3c04441/asset.meta",
           "06f6f385a4ad409884857500a3c04441/pathname",
           "08d62f799cbd4b02a3ff77313706a3c0",
           "08d62f799cbd4b02a3ff77313706a3c0/asset",
           "08d62f799cbd4b02a3ff77313706a3c0/asset.meta",
           "08d62f799cbd4b02a3ff77313706a3c0/pathname",
           "7311924048bd457bac6d713576c952da",
           "7311924048bd457bac6d713576c952da/asset",
           "7311924048bd457bac6d713576c952da/asset.meta",
           "7311924048bd457bac6d713576c952da/pathname"],
          unitypackage_file.getnames())
      unitypackage_file.extractall(self.staging_dir)
      self.assertTrue(filecmp.cmp(
          os.path.join(self.assets_dir, "Firebase/Plugins/Firebase.App.dll"),
          os.path.join(self.staging_dir,
                       "7311924048bd457bac6d713576c952da/asset")))
      with open(os.path.join(self.staging_dir,
                             "08d62f799cbd4b02a3ff77313706a3c0/asset"),
                "rt") as manifest:
        self.assertEqual(
            "Assets/Firebase/Plugins/Firebase.App.dll\n"
            "Assets/PlayServicesResolver/Editor/Google.VersionHandler.dll\n",
            manifest.read())
      with open(os.path.join(
          self.staging_dir, "06f6f385a4ad409884857500a3c04441/asset.meta")) as (
              metadata):
        self.assertEqual(
            "fileFormatVersion: 2\n"
            "guid: 06f6f385a4ad409884857500a3c04441\n"
            "labels:\n"
            "- gvh\n"
            "- gvh_teditor\n"
            "- gvh_v1.2.86.0\n"
            "- gvh_version-1.0.0\n"
            "- gvhp_exportpath-PlayServicesResolver/Editor/"
            "Google.VersionHandler.dll\n"
            "timeCreated: 0\n"
            "PluginImporter:\n"
            "  externalObjects: {}\n"
            "  serializedVersion: 2\n"
            "  iconMap: {}\n"
            "  executionOrder: {}\n"
            "  isPreloaded: 0\n"
            "  isOverridable: 0\n"
            "  platformData:\n"
            "  - first:\n"
            "      Any:\n"
            "    second:\n"
            "      enabled: 0\n"
            "      settings: {}\n"
            "  - first:\n"
            "      Editor: Editor\n"
            "    second:\n"
            "      enabled: 1\n"
            "      settings:\n"
            "        DefaultValueInitialized: true\n"
            "  - first:\n"
            "      Windows Store Apps: WindowsStoreApps\n"
            "    second:\n"
            "      enabled: 0\n"
            "      settings:\n"
            "        CPU: AnyCPU\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n", metadata.read())

  def test_package_write_with_includes_and_export(self):
    """Write a .unitypackage file."""
    # This is a slighty more complicated case
    # FirebaseAuth.unitypackage:
    #   export=1, generate manifest, includes=[FirebaseApp, FirebaseAnalytics]
    # FirebaseAnalytics.unitypackage:
    #   export=1, generate manifest, includes=[FirebaseApp]
    # FirebaseApp.unitypackage:
    #   export=0, generate manifest, includes=[VersionHandler]
    # VersionHandler.unitypackage:
    #   export=0, no manifest, includes=[]
    project = export_unity_package.ProjectConfiguration(
        {
            "packages": [{
                "name": "FirebaseApp.unitypackage",
                "manifest_path": "Firebase/Editor",
                "imports": [{
                    "paths": ["Firebase/Plugins/Firebase.App.dll",]
                }],
                "includes": ["VersionHandler.unitypackage"],
                "export": 0
            }, {
                "name": "VersionHandler.unitypackage",
                "imports": [{
                    "paths": [
                        "PlayServicesResolver/Editor/Google.VersionHandler.dll",
                    ]
                }],
                "export": 0
            }, {
                "name": "FirebaseAnalytics.unitypackage",
                "manifest_path": "Firebase/Editor",
                "imports": [{
                    "paths": ["Firebase/Plugins/Firebase.Analytics.dll",]
                }],
                "includes": ["FirebaseApp.unitypackage"]
            }, {
                "name":
                    "FirebaseAuth.unitypackage",
                "manifest_path":
                    "Firebase/Editor",
                "imports": [{
                    "paths": ["Firebase/Plugins/Firebase.Auth.dll",]
                }],
                "includes": [
                    "FirebaseApp.unitypackage", "FirebaseAnalytics.unitypackage"
                ]
            }]
        }, set(), "1.0.0")
    package = project.packages_by_name["FirebaseAuth.unitypackage"]
    unitypackage = package.write(
        export_unity_package.GuidDatabase(
            export_unity_package.DuplicateGuidsChecker(), {
                "1.0.0": {
                    "Firebase/Editor/FirebaseApp_version-1.0.0_manifest.txt":
                        "08d62f799cbd4b02a3ff77313706a3c0",
                    ("Firebase/Editor/"
                     "FirebaseAnalytics_version-1.0.0_manifest.txt"):
                        "4a3f361c622e4b88b6f61a126cc8083d",
                    "Firebase/Editor/FirebaseAuth_version-1.0.0_manifest.txt":
                        "2b2a3eb537894428a96778fef31996e2",
                    "Firebase/Plugins/Firebase.App.dll":
                        "7311924048bd457bac6d713576c952da",
                    "Firebase/Plugins/Firebase.Analytics.dll":
                        "816270c2a2a348e59cb9b7b096a24f50",
                    "Firebase/Plugins/Firebase.Auth.dll":
                        "275bd6b96a28470986154b9a995e191c"
                }
            }, "1.0.0"), [self.assets_dir], self.staging_dir, 0)

    self.assertEqual(
        os.path.join(self.staging_dir, "FirebaseAuth.unitypackage"),
        unitypackage)

    with tarfile.open(unitypackage, "r:gz") as unitypackage_file:
      self.assertCountEqual([
          "06f6f385a4ad409884857500a3c04441",
          "06f6f385a4ad409884857500a3c04441/asset",
          "06f6f385a4ad409884857500a3c04441/asset.meta",
          "06f6f385a4ad409884857500a3c04441/pathname",
          "08d62f799cbd4b02a3ff77313706a3c0",
          "08d62f799cbd4b02a3ff77313706a3c0/asset",
          "08d62f799cbd4b02a3ff77313706a3c0/asset.meta",
          "08d62f799cbd4b02a3ff77313706a3c0/pathname",
          "4a3f361c622e4b88b6f61a126cc8083d",
          "4a3f361c622e4b88b6f61a126cc8083d/asset",
          "4a3f361c622e4b88b6f61a126cc8083d/asset.meta",
          "4a3f361c622e4b88b6f61a126cc8083d/pathname",
          "2b2a3eb537894428a96778fef31996e2",
          "2b2a3eb537894428a96778fef31996e2/asset",
          "2b2a3eb537894428a96778fef31996e2/asset.meta",
          "2b2a3eb537894428a96778fef31996e2/pathname",
          "7311924048bd457bac6d713576c952da",
          "7311924048bd457bac6d713576c952da/asset",
          "7311924048bd457bac6d713576c952da/asset.meta",
          "7311924048bd457bac6d713576c952da/pathname",
          "816270c2a2a348e59cb9b7b096a24f50",
          "816270c2a2a348e59cb9b7b096a24f50/asset",
          "816270c2a2a348e59cb9b7b096a24f50/asset.meta",
          "816270c2a2a348e59cb9b7b096a24f50/pathname",
          "275bd6b96a28470986154b9a995e191c",
          "275bd6b96a28470986154b9a995e191c/asset",
          "275bd6b96a28470986154b9a995e191c/asset.meta",
          "275bd6b96a28470986154b9a995e191c/pathname"
      ], unitypackage_file.getnames())
      unitypackage_file.extractall(self.staging_dir)
      self.assertTrue(
          filecmp.cmp(
              os.path.join(self.assets_dir,
                           "Firebase/Plugins/Firebase.App.dll"),
              os.path.join(self.staging_dir,
                           "7311924048bd457bac6d713576c952da/asset")))
      self.assertTrue(
          filecmp.cmp(
              os.path.join(self.assets_dir,
                           "Firebase/Plugins/Firebase.Auth.dll"),
              os.path.join(self.staging_dir,
                           "275bd6b96a28470986154b9a995e191c/asset")))
      self.assertTrue(
          filecmp.cmp(
              os.path.join(self.assets_dir,
                           "Firebase/Plugins/Firebase.Analytics.dll"),
              os.path.join(self.staging_dir,
                           "816270c2a2a348e59cb9b7b096a24f50/asset")))
      # Verify FirebaseApp_version-1.0.0_manifest.txt
      with open(
          os.path.join(self.staging_dir,
                       "08d62f799cbd4b02a3ff77313706a3c0/asset"),
          "rt") as manifest:
        self.assertEqual(
            "Assets/Firebase/Plugins/Firebase.App.dll\n"
            "Assets/PlayServicesResolver/Editor/Google.VersionHandler.dll\n",
            manifest.read())
      # Verify FirebaseAnalytics_version-1.0.0_manifest.txt
      with open(
          os.path.join(self.staging_dir,
                       "4a3f361c622e4b88b6f61a126cc8083d/asset"),
          "rt") as manifest:
        self.assertEqual(
            "Assets/Firebase/Plugins/Firebase.Analytics.dll\n"
            "Assets/Firebase/Plugins/Firebase.App.dll\n"
            "Assets/PlayServicesResolver/Editor/Google.VersionHandler.dll\n",
            manifest.read())
      # Verify FirebaseAuth_version-1.0.0_manifest.txt
      with open(
          os.path.join(self.staging_dir,
                       "2b2a3eb537894428a96778fef31996e2/asset"),
          "rt") as manifest:
        self.assertEqual(
            "Assets/Firebase/Plugins/Firebase.Analytics.dll\n"
            "Assets/Firebase/Plugins/Firebase.App.dll\n"
            "Assets/Firebase/Plugins/Firebase.Auth.dll\n"
            "Assets/PlayServicesResolver/Editor/Google.VersionHandler.dll\n",
            manifest.read())

  def test_package_write_upm(self):
    """Write a .tgz file."""
    # This is a slightly complicated case
    # play-services-resolver:
    #   export_upm=1, includes=[ios-resolver, jar-resolver]
    #   Google.VersionHandlerImpl_*.dll overridden to be enabled in editor
    # ios-resolver:
    #   export_upm=1
    # jar-resolver:
    #   export_upm=0

    expected_override_metadata = {"Editor": {"enabled": 1}}

    project = export_unity_package.ProjectConfiguration(
        {
            "packages": [{
                "name": "jar-resolver.unitypackage",
                "imports": [{
                    "paths": [
                        "PlayServicesResolver/Editor/Google.JarResolver_*.dll",
                    ]
                }],
                "common_manifest": {
                    "name": "com.google.jar-resolver",
                },
            }, {
                "name": "ios-resolver.unitypackage",
                "imports": [{
                    "paths": [
                        "PlayServicesResolver/Editor/Google.IOSResolver_*.dll",
                    ]
                }],
                "common_manifest": {
                    "name": "com.google.ios-resolver",
                },
                "export_upm": 1,
            }, {
                "name": "play-services-resolver.unitypackage",
                "imports": [{
                    "paths": [
                        "PlayServicesResolver/Editor/Google.VersionHandler.dll",
                    ]
                }, {
                    "paths": [
                        "PlayServicesResolver/Editor/"
                        "Google.VersionHandlerImpl_*.dll",
                    ],
                    "override_metadata_upm": {
                        "PluginImporter": {
                            "platformData": expected_override_metadata,
                        }
                    },
                }],
                "manifest_path": "PlayServicesResolver/Editor",
                "readme": "PlayServicesResolver/Editor/README.md",
                "changelog": "PlayServicesResolver/Editor/CHANGELOG.md",
                "license": "PlayServicesResolver/Editor/LICENSE",
                "documentation": "PlayServicesResolver/Doc",
                "includes":
                    ["ios-resolver.unitypackage", "jar-resolver.unitypackage"],
                "common_manifest": {
                    "name": "com.google.play-services-resolver",
                    "display_name": "Play Services Resolver",
                },
                "export_upm": 1,
                "upm_package_config": {
                    "manifest": {
                        "unity": "2017.1",
                        "dependencies": {
                            "com.some.third-party-package": "1.2.3"
                        },
                    }
                }
            }]
        }, set(), "1.0.0")
    package = project.packages_by_name["play-services-resolver.unitypackage"]

    expected_tarball_name = "com.google.play-services-resolver-1.0.0.tgz"

    self.assertEqual(expected_tarball_name, package.tarball_name)

    unitypackage = package.write_upm(
        export_unity_package.GuidDatabase(
            export_unity_package.DuplicateGuidsChecker(), {
                "1.0.0": {
                    "com.google.play-services-resolver/README.md":
                        "baa27a4c0385454899a759d9852966b7",
                    "com.google.play-services-resolver/CHANGELOG.md":
                        "000ce82791494e44b04c7a6f9a31151c",
                    "com.google.play-services-resolver/LICENSE.md":
                        "94717c1d977f445baed18e00605e3d7c",
                    "PlayServicesResolver/Editor/"
                    "play-services-resolver_version-1.0.0_manifest.txt":
                        "353f6aace2cd42adb1343fc6a808f62e",
                    "com.google.play-services-resolver/package.json":
                        "782a38c5f19e4bb99e927976c8daa9ac",
                    "com.google.play-services-resolver/PlayServicesResolver":
                        "fa7daf703ad1430dad0cd8b764e5e6d2",
                    "com.google.play-services-resolver/PlayServicesResolver/"
                    "Editor":
                        "2334cd7684164851a8a53db5bd5923ca",
                }
            }, "1.0.0"), [self.assets_dir], self.staging_dir, 0)

    expected_manifest = {
        "name": "com.google.play-services-resolver",
        "displayName": "Play Services Resolver",
        "version": "1.0.0",
        "unity": "2017.1",
        "keywords": [
            "vh-name:play-services-resolver",
            "vh-name:Play Services Resolver"
        ],
        "dependencies": {
            "com.some.third-party-package": "1.2.3",
            "com.google.ios-resolver": "1.0.0"
        },
    }
    self.assertEqual(
        os.path.join(self.staging_dir, expected_tarball_name), unitypackage)

    with tarfile.open(unitypackage, "r:gz") as unitypackage_file:
      # Check included files.
      self.assertCountEqual([
          "package",
          "package/package.json",
          "package/package.json.meta",
          "package/README.md",
          "package/README.md.meta",
          "package/CHANGELOG.md",
          "package/CHANGELOG.md.meta",
          "package/LICENSE.md",
          "package/LICENSE.md.meta",
          "package/PlayServicesResolver",
          "package/PlayServicesResolver.meta",
          "package/PlayServicesResolver/Editor",
          "package/PlayServicesResolver/Editor.meta",
          "package/PlayServicesResolver/Editor/Google.VersionHandler.dll",
          "package/PlayServicesResolver/Editor/Google.VersionHandler.dll.meta",
          "package/PlayServicesResolver/Editor/"
          "play-services-resolver_version-1.0.0_manifest.txt",
          "package/PlayServicesResolver/Editor/"
          "play-services-resolver_version-1.0.0_manifest.txt.meta",
          "package/PlayServicesResolver/Editor/"
          "Google.VersionHandlerImpl_v1.2.87.0.dll",
          "package/PlayServicesResolver/Editor/"
          "Google.VersionHandlerImpl_v1.2.87.0.dll.meta",
          "package/Documentation~",
          "package/Documentation~/index.md",
      ], unitypackage_file.getnames())
      unitypackage_file.extractall(self.staging_dir)

      self.assertTrue(
          filecmp.cmp(
              os.path.join(
                  self.assets_dir,
                  "PlayServicesResolver/Editor/Google.VersionHandler.dll"),
              os.path.join(
                  self.staging_dir, "package/PlayServicesResolver/Editor/"
                  "Google.VersionHandler.dll")))

      self.assertTrue(
          filecmp.cmp(
              os.path.join(
                  self.assets_dir,
                  "PlayServicesResolver/Doc/index.md"),
              os.path.join(
                  self.staging_dir, "package/Documentation~/index.md")))

      # Check package.json
      with open(os.path.join(self.staging_dir, "package/package.json"),
                "rt") as manifest:
        self.assertEqual(expected_manifest, json.loads(manifest.read()))

      # Check folder metadata
      with open(
          os.path.join(self.staging_dir,
                       "package/PlayServicesResolver.meta")) as (metadata):
        self.assertEqual(
            "fileFormatVersion: 2\n"
            "guid: fa7daf703ad1430dad0cd8b764e5e6d2\n"
            "timeCreated: 0\n"
            "folderAsset: true\n"
            "DefaultImporter:\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n", metadata.read())

      # Check overridden metadata
      with open(
          os.path.join(
              self.staging_dir, "package/PlayServicesResolver/Editor/"
              "Google.VersionHandlerImpl_v1.2.87.0.dll.meta")) as (metadata):
        serializer = export_unity_package.YamlSerializer()
        yaml_dict = serializer.load(metadata.read())
        self.assertEqual(expected_override_metadata,
                         yaml_dict["PluginImporter"]["platformData"])

  def test_package_write_upm_documentation_as_file(self):
    """Test write_upm() with documentation path as a file."""
    project = export_unity_package.ProjectConfiguration(
        {
            "packages": [{
                "name": "play-services-resolver.unitypackage",
                "imports": [{
                    "paths": [
                        "PlayServicesResolver/Editor/Google.VersionHandler.dll",
                    ]
                }],
                "manifest_path": "PlayServicesResolver/Editor",
                # Use README.md as documentation.
                "documentation": "PlayServicesResolver/Editor/README.md",
                "common_manifest": {
                    "name": "com.google.play-services-resolver",
                },
                "export_upm": 1
            }]
        }, set(), "1.0.0")
    package = project.packages_by_name["play-services-resolver.unitypackage"]

    upm_package = package.write_upm(
        export_unity_package.GuidDatabase(
            export_unity_package.DuplicateGuidsChecker(), {
                "1.0.0": {
                    "PlayServicesResolver/Editor/README.md":
                        "baa27a4c0385454899a759d9852966b7",
                    "PlayServicesResolver/Editor/"
                    "play-services-resolver_version-1.0.0_manifest.txt":
                        "353f6aace2cd42adb1343fc6a808f62e",
                    "com.google.play-services-resolver/package.json":
                        "782a38c5f19e4bb99e927976c8daa9ac",
                    "com.google.play-services-resolver/PlayServicesResolver":
                        "fa7daf703ad1430dad0cd8b764e5e6d2",
                    "com.google.play-services-resolver/PlayServicesResolver/"
                    "Editor":
                        "2334cd7684164851a8a53db5bd5923ca",
                }
            }, "1.0.0"), [self.assets_dir], self.staging_dir, 0)

    with tarfile.open(upm_package, "r:gz") as upm_package_file:
      # Check included files.
      self.assertCountEqual([
          "package",
          "package/package.json",
          "package/package.json.meta",
          "package/PlayServicesResolver",
          "package/PlayServicesResolver.meta",
          "package/PlayServicesResolver/Editor",
          "package/PlayServicesResolver/Editor.meta",
          "package/PlayServicesResolver/Editor/Google.VersionHandler.dll",
          "package/PlayServicesResolver/Editor/Google.VersionHandler.dll.meta",
          "package/PlayServicesResolver/Editor/"
          "play-services-resolver_version-1.0.0_manifest.txt",
          "package/PlayServicesResolver/Editor/"
          "play-services-resolver_version-1.0.0_manifest.txt.meta",
          "package/Documentation~",
          "package/Documentation~/index.md",
      ], upm_package_file.getnames())
      upm_package_file.extractall(self.staging_dir)

      self.assertTrue(
          filecmp.cmp(
              os.path.join(
                  self.assets_dir,
                  "PlayServicesResolver/Editor/README.md"),
              os.path.join(
                  self.staging_dir, "package/Documentation~/index.md")))

  def test_package_write_upm_missing_readme(self):
    """Test write_upm() with misconfigured readme path."""
    project = export_unity_package.ProjectConfiguration(
        {
            "packages": [{
                "name": "play-services-resolver.unitypackage",
                "imports": [{
                    "paths": [
                        "PlayServicesResolver/Editor/Google.VersionHandler.dll",
                    ]
                }],
                "manifest_path": "PlayServicesResolver/Editor",
                "readme": "a/nonexist/path/README.md",
                "common_manifest": {
                    "name": "com.google.play-services-resolver",
                },
                "export_upm": 1
            }]
        }, set(), "1.0.0")
    package = project.packages_by_name["play-services-resolver.unitypackage"]

    with self.assertRaises(export_unity_package.ProjectConfigurationError):
      package.write_upm(
          export_unity_package.GuidDatabase(
              export_unity_package.DuplicateGuidsChecker(), {
                  "1.0.0": {
                      "PlayServicesResolver/Editor/README.md":
                          "baa27a4c0385454899a759d9852966b7",
                      "PlayServicesResolver/Editor/"
                      "play-services-resolver_version-1.0.0_manifest.txt":
                          "353f6aace2cd42adb1343fc6a808f62e",
                      "com.google.play-services-resolver/package.json":
                          "782a38c5f19e4bb99e927976c8daa9ac",
                      "com.google.play-services-resolver/PlayServicesResolver":
                          "fa7daf703ad1430dad0cd8b764e5e6d2",
                      "com.google.play-services-resolver/PlayServicesResolver/"
                      "Editor":
                          "2334cd7684164851a8a53db5bd5923ca",
                  }
              }, "1.0.0"), [self.assets_dir], self.staging_dir, 0)


class TestVersionHandler(absltest.TestCase):
  """Test methods that generate Version Handler labels and filenames."""

  def test_version_handler_tag(self):
    """Generate a label or filename field for the Version Handler."""
    self.assertEqual(
        "gvh_manifest",
        export_unity_package.version_handler_tag(
            islabel=True,
            field=export_unity_package.VERSION_HANDLER_MANIFEST_FIELD_PREFIX,
            value=None))
    self.assertEqual(
        "manifest",
        export_unity_package.version_handler_tag(
            islabel=False,
            field=export_unity_package.VERSION_HANDLER_MANIFEST_FIELD_PREFIX,
            value=None))
    self.assertEqual(
        "gvh_version-1.2.3-beta2",
        export_unity_package.version_handler_tag(
            islabel=True,
            field=export_unity_package.VERSION_HANDLER_VERSION_FIELD_PREFIX,
            value="1.2.3_beta2"))
    self.assertEqual(
        "version-1.2.3-beta2",
        export_unity_package.version_handler_tag(
            islabel=False,
            field=export_unity_package.VERSION_HANDLER_VERSION_FIELD_PREFIX,
            value="1.2.3_beta2"))
    self.assertEqual(
        "", export_unity_package.version_handler_tag(
            islabel=False, field=None, value=None))

  def test_version_handler_filename(self):
    """Generate a filename for the Version Handler."""
    self.assertEqual(
        "a/b/c/myplugin_version-1.2.3_manifest.txt",
        export_unity_package.version_handler_filename(
            "a/b/c/myplugin.txt",
            [(export_unity_package.VERSION_HANDLER_VERSION_FIELD_PREFIX,
              "1.2.3"),
             (export_unity_package.VERSION_HANDLER_MANIFEST_FIELD_PREFIX,
              None)]))


class FileOperationsTest(absltest.TestCase):
  """Test file utility methods."""

  def setUp(self):
    """Unpack resources to a temporary directory."""
    super(FileOperationsTest, self).setUp()
    self.assets_dir = os.path.join(TEST_DATA_PATH, "Assets")
    self.temp_dir = os.path.join(FLAGS.test_tmpdir, "copy_temp")
    self.expected_mode = stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR
    if platform.system() == 'Windows':
      # Windows doesn't support the executable mode so ignore it in tests.
      self.expected_mode = self.expected_mode & ~stat.S_IXUSR
    os.makedirs(self.temp_dir)

  def tearDown(self):
    """Clean up the temporary directory."""
    super(FileOperationsTest, self).tearDown()
    shutil.rmtree(self.temp_dir)

  def test_copy_and_set_rwx(self):
    """Copy a file and set it to readable / writeable and executable."""
    source_path = os.path.join(
        self.assets_dir,
        "PlayServicesResolver/Editor/play-services-resolver_v1.2.87.0.txt")
    target_path = os.path.join(self.temp_dir,
                               "play-services-resolver_v1.2.87.0.txt")
    self.assertFalse(os.path.exists(target_path))

    export_unity_package.copy_and_set_rwx(source_path, target_path)
    self.assertTrue(os.path.exists(target_path))
    self.assertTrue(filecmp.cmp(source_path, target_path))
    self.assertEqual(self.expected_mode,
                     os.stat(target_path).st_mode & stat.S_IRWXU)

  def test_copy_and_set_rwx_new_dir(self):
    """Copy a file into a non-existent directory."""
    source_path = os.path.join(
        self.assets_dir,
        "PlayServicesResolver/Editor/play-services-resolver_v1.2.87.0.txt")
    target_path = os.path.join(
        self.temp_dir,
        "a/nonexistent/directory/play-services-resolver_v1.2.87.0.txt")
    self.assertFalse(os.path.exists(target_path))

    export_unity_package.copy_and_set_rwx(source_path, target_path)
    self.assertTrue(os.path.exists(target_path))
    self.assertTrue(filecmp.cmp(source_path, target_path))
    self.assertEqual(self.expected_mode,
                     os.stat(target_path).st_mode & stat.S_IRWXU)

  def test_copy_files_to_dir(self):
    """Test copying files into a directory using a variety of target paths."""
    original_copy_and_set_rwx = export_unity_package.copy_and_set_rwx
    try:
      copied_files = []
      export_unity_package.copy_and_set_rwx = (
          lambda source, target: copied_files.append((source, target)))
      self.assertEqual(
          ["an/output/dir/something/to/copy.txt",
           "an/output/dir/a/target/path.txt",
           "an/output/dir/some/root/path.txt"],
          export_unity_package.copy_files_to_dir(
              ["something/to/copy.txt",
               "something/else/to_copy.txt:a/target/path.txt",
               "yet/another/file.txt:/some/root/path.txt"],
              "an/output/dir"))
      self.assertEqual(
          [("something/to/copy.txt", "an/output/dir/something/to/copy.txt"),
           ("something/else/to_copy.txt", "an/output/dir/a/target/path.txt"),
           ("yet/another/file.txt", "an/output/dir/some/root/path.txt")],
          copied_files)
    finally:
      export_unity_package.copy_and_set_rwx = original_copy_and_set_rwx

  def test_copy_dir_to_dir(self):
    """Test copying directory into a directory recursively."""
    source_path = os.path.join(
        self.assets_dir,
        "PlayServicesResolver")
    target_path = os.path.join(
        FLAGS.test_tmpdir,
        "a/nonexistent/directory")
    self.assertFalse(os.path.exists(target_path))

    export_unity_package.copy_and_set_rwx(source_path, target_path)
    self.assertTrue(os.path.exists(target_path))
    cmp_result = filecmp.dircmp(source_path, target_path)
    self.assertFalse(cmp_result.left_only or cmp_result.right_only or
                     cmp_result.diff_files)
    # NOTE: Folders have the executable bit set on Windows.
    self.assertEqual(
        stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR,
        os.stat(os.path.join(target_path, "Editor")).st_mode & stat.S_IRWXU)
    self.assertEqual(
        self.expected_mode,
        (os.stat(os.path.join(target_path, "Editor.meta")).st_mode &
            stat.S_IRWXU))

  def test_find_in_dirs(self):
    """Test find_in_dirs."""
    self.assertEqual(
        export_unity_package.find_in_dirs(
            "PlayServicesResolver", [self.assets_dir]),
        os.path.join(self.assets_dir, "PlayServicesResolver"))
    self.assertEqual(
        export_unity_package.find_in_dirs(
            "PlayServicesResolver/Editor.meta", [self.assets_dir]),
        os.path.join(self.assets_dir, "PlayServicesResolver/Editor.meta"))
    self.assertEqual(
        export_unity_package.find_in_dirs("PlayServicesResolver", []), None)
    self.assertEqual(
        export_unity_package.find_in_dirs(
            "a/nonexisting/file", [self.assets_dir]),
        None)

class ReadJsonFileTest(absltest.TestCase):
  """Test reading a JSON file."""

  def setUp(self):
    """Create a temporary directory."""
    super(ReadJsonFileTest, self).setUp()
    self.temp_dir = os.path.join(FLAGS.test_tmpdir, "json_temp")
    os.makedirs(self.temp_dir)

  def tearDown(self):
    """Clean up the temporary directory."""
    super(ReadJsonFileTest, self).tearDown()
    shutil.rmtree(self.temp_dir)

  def test_read_json_file_into_ordered_dict(self):
    """Read JSON into an OrderedDict."""
    json_filename = os.path.join(self.temp_dir, "test.json")
    with open(json_filename, "wt") as json_file:
      json_file.write("{\n"
                      "  \"this\": \"is\",\n"
                      "  \"just\": \"a\",\n"
                      "  \"test\": \"of\",\n"
                      "  \"ordered\": \"json\",\n"
                      "  \"parsing\": 1\n"
                      "}\n")
    dictionary = export_unity_package.read_json_file_into_ordered_dict(
        json_filename)
    self.assertEqual(
        collections.OrderedDict([("this", "is"),
                                 ("just", "a"),
                                 ("test", "of"),
                                 ("ordered", "json"),
                                 ("parsing", 1)]), dictionary)

  def test_read_json_file_into_ordered_dict_missing_file(self):
    """Try to read a non-existent JSON file."""
    with self.assertRaises(IOError) as context:
      export_unity_package.read_json_file_into_ordered_dict(
          os.path.join(self.temp_dir, "missing.json"))
    self.assertRegexMatch(str(context.exception),
                          [r".*missing\.json"])

  def test_read_json_file_into_ordered_dict_malformed_json(self):
    """Try to read invalid JSON."""
    json_filename = os.path.join(self.temp_dir, "test.json")
    with open(json_filename, "wt") as json_file:
      json_file.write("{\n"
                      "  \"json\": \"dislikes\",\n"
                      "  \"trailing\": \"commas\",\n"
                      "}\n")
    with self.assertRaises(ValueError) as context:
      export_unity_package.read_json_file_into_ordered_dict(
          json_filename)
    self.assertRegexMatch(str(context.exception),
                          [r".*test\.json"])


if __name__ == "__main__":
  absltest.main()

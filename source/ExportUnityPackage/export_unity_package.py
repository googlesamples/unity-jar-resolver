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

r"""A script to build Unity packages without Unity.

This script enables plugins and assets created for Unity to be packaged into
a Unity package which can be loaded in Unity and supports the appropriate meta
data. The script takes a config file and the root directory of the assets
to be packed.

The config supports multiple Unity package definitions, and each contains
an inclusive set of files with wildcard support grouped by platform settings.

Example usage:
  export_unity_package.py --config_file=exports.json \
    --guids_file=guids.json \
    --plugins_version="1.0.0" \
    --assets_dir="/tmp/unityBundle"

The json file should have the following format:
{
    "packages": [
        {
            # Name of the Unity package to export.
            "name": "yourpackage.unitypackage",

            # Whether this package should be exported for the sections enabled.
            # If this is empty the package will always be built. If this
            # specifies a list of sections it will only be built if the
            # enabled_sections flag contains the enabled sections in this list.
            "sections": ["some_section"],

            # Files to import into the package.
            "imports": [
                {
                    # Whether this package should be exported for the sections
                    # enabled.
                    # If this is empty the package will always be built. If this
                    # specifies a list of sections it will only be built if the
                    # enabled_sections flag contains the enabled sections in
                    # this list.
                    "sections": ["some_section"],

                    # How / when to import (load) the file in Unity.
                    # * PluginImporter specifies the file should be imported as
                    #   a C# DLL for the platforms specified by the "platforms"
                    #   field.
                    # * DefaultImporter specifies that the file should be
                    #   imported using Unity's default settings for the file
                    #   type derived from the file extension.
                    # This field defaults to "DefaultImporter".
                    "importer": "PluginImporter",

                    # Platforms targeted when the PluginImporter is used.
                    # Can be a list containing any Unity platform name e.g:
                    # * Any: Meta platform that targets all platforms.
                    # * Editor: Unity editor.
                    # * Standalone: Meta platform that targets all desktop
                    #    platforms including the editor.
                    # * Android
                    # * iOS
                    # * tvOS
                    "platforms": ["Editor", "Standalone", "Android", "iOS"],

                    # CPUs supported by standalone or editor platforms when the
                    # "PluginImporter" is the importer and platforms contains
                    # one of "Standalone", "LinuxUniversal", "OSXUniversal"
                    # or "Editor".
                    "cpu": "AnyCPU",   # (or "x86" or "x86_64")

                    # Labels to apply to the asset.  These are used to find
                    # assets quickly in the asset database and change import
                    # setting via plugins like the Play Services Resolver.
                    "labels": [
                        "gvh",
                        ...
                    ],

                    # Asset metadata YAML to override the existing metadata for
                    # the file.  This should either be a string containing YAML
                    # or a JSON dictionary.
                    # For example, the following uses a JSON dictionary to
                    # disable the plugin for the "Any" platform.
                    "override_metadata": {
                      "PluginImporter": {
                        "platformData": {
                          "Any": {
                            "enabled": 0
                        }
                      }
                    },

                    # Asset metadata YAML to override the existing metadata for
                    # the file for Unity Package Manager package.  This should
                    # either be a string containing YAML or a JSON dictionary.
                    # For example, the following uses a JSON dictionary to
                    # enable the plugin for the "Editor" platform for UPM
                    # package.
                    "override_metadata_upm": {
                      "PluginImporter": {
                        "platformData": {
                          "Editor": {
                            "enabled": 1
                        }
                      }
                    },

                    # Files to import with the importer and label settings
                    # applied.
                    # Each item in this list can be one of the following:
                    # - Filename: Includes just this file.
                    # - Directory: Recursively includes the directory.
                    # - Unix shell-style wildcard (glob): Includes all files
                    #   matching the pattern.
                    "paths": [
                        "Firebase/Plugins/App.dll",
                        ...
                    ]
                },
                ...
            ],
            # Transitively includes all files from the set of packages specified
            # by this list.
            "includes": [ "anotherpackage.unitypackage" ],

            # List of regular expression strings which exclude files included
            # in this plugin.  This applies to this plugin if it's exported and
            # all plugins that depend upon it.
            "exclude_paths": [
                "Firebase/Samples/Auth/.*",
            ],

            # Whether to export this package (enabled by default).
            "export": 1,

            # Path of the manifest in the package with the basename of the
            # manifest file.  If a path isn't specified, a manifest isn't
            # generated.
            # e.g
            # My/Cool/ShaderToolkit
            # would be expanded to...
            # My/Cool/${package_name}_v${version}_manifest.txt
            #
            # ${package_name} is derived from the output filename and
            # ${version} is specified via the command line --plugins_version
            # argument.
            "manifest_path": "Firebase/Editor/FirebaseAnalytics",

            # Path to the readme document. The file must be included through
            # FLAGS.assets_dir, FLAGS.assets_zip or FLAG.asset_file, and is not
            # required to be in "imports" section.
            "readme": "path/to/a/Readme.md",

            # Path to the changelog document. The file must be included through
            # FLAGS.assets_dir, FLAGS.assets_zip or FLAG.asset_file, and is not
            # required to be in "imports" section.
            "changelog": "path/to/a/Changelog.md",

            # Path to the license document. The file must be included through
            # FLAGS.assets_dir, FLAGS.assets_zip or FLAG.asset_file, and is not
            # required to be in "imports" section.
            "license": "path/to/a/License.md",

            # Path to the documents. The path can be a specific file or a folder
            # containing index.md. The file/folder must be included through
            # FLAGS.assets_dir, FLAGS.assets_zip or FLAG.asset_file, and is not
            # required to be in "imports" section.
            "documentaiton": "path/to/a/Document.md",

            # Common package information used to generate package manifest.
            # Required if "export_upm" is 1
            "common_manifest": {
                # Package name used in the manifest file. Required if
                # "export_upm" is 1.
                "name": "com.google.firebase.app",

                # Display name for the package. Optional.
                "display_name": "Firebase App (Core)",

                # Description for the package. Optional.
                # This can be a single string or a list of strings which will be
                # joined into single string for manifest.
                "description": "This is core library for Firebase",
                "description": [ "This is core library ", "for Firebase" ],

                # A list of keywords for the package. Potentially used for
                # filtering or searching. Optional.
                # Add "vh-name:legacy_manifest_name" to link this package to
                # a renamed package imported as an asset package.
                # Note that this script will automatically add
                # "vh-name:current_package_name" to keywords.
                "keywords": [ "Google", "Firebase", "vh-name:MyOldName"],

                # Author information for the package. Optional.
                "author": {
                  "name" : "Google Inc",
                  "email" : "someone@google.com",
                  "url": "https://firebase.google.com/"
                }
            },

            # Whether to export this package for Unity Package Manager, i.e.
            # .tgz tarball (disabled by default)
            "export_upm": 0,

            # Package configuration for Unity Package Manager package. Optional.
            "upm_package_config": {
                # Manifest information for package.json used by Unity Package
                # Manager. Optional.
                "manifest" : {
                    # This defines the package's minimum supported Unity version
                    # in the form "major.minor", for example "2019.1". The
                    # minimum valid version here is "2017.1". Optional.
                    "unity": "2017.1",

                    # A map containing this package's additional dependencies
                    # where the keys are package names and the values are
                    # specific versions, e.g. "1.2.3". This script will also
                    # automatically includes packages listed in "includes", if
                    # it is set to export for UPM.
                    "dependencies": {
                        "com.some.third-party-package": "1.2.3"
                    }
                }
            },
        },
        ...
    ],

    # Optional build configurations for the project.
    # All packages in the project are exported for each build configuration
    # listed in this section.
    "builds": [
      {
        # Name of this build config for logging purposes.
        "name": "debug",

        # Whether this build config should be executed for the sections enabled.
        # If this is empty, it will always be executed.
        "sections": ["debug"],

        # Sections that should be enabled when exporting packages with this
        # build config.  This set of sections are added to the sections
        # specified on the command line before packages are exported.
        "enabled_sections": ["early_access"],

        # List of regular expressions and replacement strings applied to
        # package names before they're exported.
        # For example:
        # { "match": "foo(.*)\\.bar", "replacement": "foo\\1Other.bar" }
        # Changes the package name "foo123.bar" to "foo123Other.bar".
        "package_name_replacements": [
          {
            "match": "(.*)(\\.unitypackage)",
            "replacement": "\\1EarlyAccess\\2"
          },
        ]
      },
      ...
    ]
}
"""

import collections
import copy
import glob
import gzip
import json
import os
import platform
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import traceback
import zipfile
from absl import app
from absl import flags
from absl import logging
import packaging.version
import yaml

FLAGS = flags.FLAGS

flags.DEFINE_string("config_file", None, ("Config file that describes how to "
                                          "pack the unity assets."))
flags.DEFINE_string("guids_file", None, "Json file with stable guids cache.")
flags.DEFINE_string("plugins_version", None, "Version of the plugins to "
                    "package.")
flags.DEFINE_boolean("use_tar", True, "Whether to use the tar command line "
                     "application, when available, to generate archives rather "
                     "than Python's tarfile module.  NOTE: On macOS tar / gzip "
                     "generate Unity compatible but non-reproducible archives.")
flags.DEFINE_boolean(
    "enforce_semver", True, "Whether to enforce semver (major.minor.patch) for"
    "plugins_version.  This is required to build UPM package.")
flags.DEFINE_multi_string("assets_dir", ".", "Directory containing assets to "
                          "package.")
flags.DEFINE_multi_string("assets_zip", None, "Zip files containing assets to "
                          "package.")
flags.DEFINE_multi_string("asset_file", None,
                          "File to copy in a directory to search for assets. "
                          "This is in the format "
                          "'input_filename:asset_filename' where "
                          "input_filename if the path to the file to copy and "
                          "asset_filename is the path to copy to in directory "
                          "to stage assets.")
flags.DEFINE_integer("timestamp", 1480838400,  # 2016-12-04
                     "Timestamp to use for each file. "
                     "Set to 0 to use the current time.")
flags.DEFINE_string("owner", "root",
                    "Username of file owner in each generated package.")
flags.DEFINE_string("group", "root",
                    "Username of file group in each generated package.")
flags.DEFINE_string("output_dir", "output",
                    "Directory to write the resulting Unity package files.")
flags.DEFINE_string("output_zip", None, "Zip file to archive the output Unity "
                    "packages.")
flags.DEFINE_boolean(
    "output_upm", False, "Whether output packages as tgz for"
    "Unity Package Manager.")
flags.DEFINE_boolean("output_unitypackage", True, "Whether output packages as "
                     "asset packages.")
flags.DEFINE_multi_string("additional_file", None,
                          "Additional file in the format "
                          "'input_filename:output_filename', which copies the "
                          "specified input_filename to output_filename under "
                          "the output_dir.  This can be used to store "
                          "additional files in the output directory or zip "
                          "file.  If the ':output_filename' portion of the "
                          "argument isn't specified, the file will be written "
                          "to the same path as the specified input_filename "
                          "under the output_dir.")
flags.DEFINE_spaceseplist(
    "enabled_sections", None,
    ("List of sections to include in the set of packages. "
     "Package specifications that do not specify any sections are always "
     "included."))

# Default metadata for all Unity 5.3+ assets.
DEFAULT_METADATA_TEMPLATE = collections.OrderedDict(
    [("fileFormatVersion", 2),
     ("guid", None),  # A unique GUID *must* be specified for all assets.
     ("labels", None),  # Can optionally specific a list of asset label strings.
     ("timeCreated", 0)])

# A minimal set of Importer meta data.
#
# This importer is used if nothing more specific is needed and Unity can often
# infer the correct meta data using this by directory structure. This method is
# used if the json import group's "importer" field is "DefaultImporter".
DEFAULT_IMPORTER_DATA = [("userData", None),
                         ("assetBundleName", None),
                         ("assetBundleVariant", None)]
DEFAULT_IMPORTER_METADATA_TEMPLATE = collections.OrderedDict(
    [("DefaultImporter", collections.OrderedDict(DEFAULT_IMPORTER_DATA))])

DEFAULT_FOLDER_METADATA_TEMPLATE = collections.OrderedDict([
    ("folderAsset", True),
    ("DefaultImporter", collections.OrderedDict(DEFAULT_IMPORTER_DATA))
])

PLATFORM_SETTINGS_DISABLED = [("enabled", 0)]
DEFAULT_PLATFORM_SETTINGS_EMPTY_DISABLED = collections.OrderedDict(
    PLATFORM_SETTINGS_DISABLED +
    [("settings", {})])

DEFAULT_PLATFORM_SETTINGS_DISABLED = collections.OrderedDict(
    PLATFORM_SETTINGS_DISABLED +
    [("settings", collections.OrderedDict(
        [("CPU", "AnyCPU")]))])

DEFAULT_PLATFORM_SETTINGS_EDITOR = collections.OrderedDict(
    PLATFORM_SETTINGS_DISABLED +
    [("settings", collections.OrderedDict(
        [("CPU", "AnyCPU"),
         ("DefaultValueInitialized", True),
         ("OS", "AnyOS")]))])

# When desktop platforms are disabled Unity expects the CPU to be set to None.
DEFAULT_PLATFORM_SETTINGS_DISABLED_CPU_NONE = collections.OrderedDict(
    PLATFORM_SETTINGS_DISABLED +
    [("settings", collections.OrderedDict(
        [("CPU", "None")]))])

DEFAULT_PLATFORM_SETTINGS_DISABLED_IOS = collections.OrderedDict(
    PLATFORM_SETTINGS_DISABLED +
    [("settings", collections.OrderedDict(
        [("CompileFlags", None),
         ("FrameworkDependencies", None)]))])

DEFAULT_PLATFORM_SETTINGS_DISABLED_TVOS = collections.OrderedDict(
    PLATFORM_SETTINGS_DISABLED +
    [("settings", collections.OrderedDict(
        [("CompileFlags", None),
         ("FrameworkDependencies", None)]))])

PLUGIN_IMPORTER_METADATA_TEMPLATE = collections.OrderedDict(
    [("PluginImporter", collections.OrderedDict(
        [("serializedVersion", 1),
         ("iconMap", {}),
         ("executionOrder", {}),
         ("isPreloaded", 0),
         ("platformData", collections.OrderedDict(
             [("Android", copy.deepcopy(
                 DEFAULT_PLATFORM_SETTINGS_DISABLED)),
              ("Any", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_EMPTY_DISABLED)),
              ("Editor", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_EDITOR)),
              ("Linux", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_CPU_NONE)),
              ("Linux64", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_CPU_NONE)),
              ("LinuxUniversal", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_CPU_NONE)),
              ("OSXIntel", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_CPU_NONE)),
              ("OSXIntel64", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_CPU_NONE)),
              ("OSXUniversal", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_CPU_NONE)),
              ("Web", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_EMPTY_DISABLED)),
              ("WebStreamed", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_EMPTY_DISABLED)),
              ("Win", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_CPU_NONE)),
              ("Win64", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_CPU_NONE)),
              ("WindowsStoreApps", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED)),
              ("iOS", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_IOS)),
              ("tvOS", copy.deepcopy(
                  DEFAULT_PLATFORM_SETTINGS_DISABLED_TVOS)),
             ]))
        ] + DEFAULT_IMPORTER_DATA))
    ])

# Map of platforms to targets.
# Unity 5.6+ metadata requires a tuple of (target, name) for each platform.
# This ignores targets like "Facebook" which overlap with more common targets
# like "Standalone".
PLATFORM_TARGET_BY_PLATFORM = {
    "Any": "Any",
    "Editor": "Editor",
    "Android": "Android",
    "Linux": "Standalone",
    "Linux64": "Standalone",
    "LinuxUniversal": "Standalone",
    "OSXIntel": "Standalone",
    "OSXIntel64": "Standalone",
    "OSXUniversal": "Standalone",
    "Web": "WebGL",
    "WebStreamed": "",
    "Win": "Standalone",
    "Win64": "Standalone",
    "WindowsStoreApps": "Windows Store Apps",
    "iOS": "iPhone",
    "tvOS": "tvOS",
}

# Alias for standalone platforms specified by the keys of
# CPU_BY_DESKTOP_PLATFORM.
STANDALONE_PLATFORM_ALIAS = "Standalone"
# Maps architecture specific platform selections to "universal"
# platforms.  Universal in Unity doesn't really mean that it can target any
# architecture, instead it is master flag that controls whether the asset is
# enabled for export.
ARCH_SPECIFIC_TO_UNIVERSAL_PLATFORM = {
    "Linux": "LinuxUniversal",
    "Linux64": "LinuxUniversal",
    "OSXIntel": "OSXUniversal",
    "OSXIntel64": "OSXUniversal",
}

# Set of supported platforms for each shared library extension.
PLATFORMS_BY_SHARED_LIBRARY_EXTENSION = {
    ".so": set(["Any", "Editor", "Linux", "Linux64", "LinuxUniversal"]),
    ".bundle": set(["Any", "Editor", "OSXIntel", "OSXIntel64", "OSXUniversal"]),
    ".dll": set(["Any", "Editor", "Win", "Win64"])
}

# Desktop platform to CPU mapping.
CPU_BY_DESKTOP_PLATFORM = {
    "Linux": "x86",
    "OSXIntel": "x86",
    "Win": "x86",
    "Linux64": "x86_64",
    "OSXIntel64": "x86_64",
    "Win64": "x86_64",
    "LinuxUniversal": "AnyCPU",
    "OSXUniversal": "AnyCPU",
}
# CPU to desktop platform mapping.
DESKTOP_PLATFORMS_BY_CPU = {
    "x86": [p for p, c in CPU_BY_DESKTOP_PLATFORM.items() if c == "x86"],
    "x86_64": [p for p, c in CPU_BY_DESKTOP_PLATFORM.items() if c == "x86_64"],
    "AnyCPU": CPU_BY_DESKTOP_PLATFORM.keys(),
}

# Unity 5.6 and beyond modified the PluginImporter format such that platforms
# are enabled using a list of dictionaries with the keys "first" and "second"
# controlling platform settings.  This constant matches the keys in entries of
# the PluginImporter.platformData list.
UNITY_5_6_PLATFORM_DATA_KEYS = ["first", "second"]

# Prefix for labels that are applied to files managed by the VersionHandler
# module.
VERSION_HANDLER_LABEL_PREFIX = "gvh"
# Prefix for version numbers in VersionHandler filenames and labels.
VERSION_HANDLER_VERSION_FIELD_PREFIX = "version-"
VERSION_HANDLER_MANIFEST_FIELD_PREFIX = "manifest"
# Separator for filenames and fields parsed by the VersionHandler.
VERSION_HANDLER_FIELD_SEPARATOR = "_"
# Prefix for labels that are applied to files managed by the Unity Package
# Manager module.
UPM_RESOLVER_LABEL_PREFIX = "gupmr"
UPM_RESOLVER_MANIFEST_FIELD_PREFIX = "manifest"
# Separator for filenames and fields parsed by the UPM Resolver.
UPM_RESOLVER_FIELD_SEPARATOR = "_"
# Prefix for latest labels that are applied to files managed by the
# VersionHandler module.
VERSION_HANDLER_PRESERVE_LABEL_PREFIX = "gvhp"
VERSION_HANDLER_PRESERVE_MANIFEST_NAME_FIELD_PREFIX = "manifestname-"
VERSION_HANDLER_PRESERVE_EXPORT_PATH_FIELD_PREFIX = "exportpath-"
VERSION_HANDLER_MANIFEST_TYPE_LEGACY = 0
VERSION_HANDLER_MANIFEST_TYPE_UPM = 1
# Prefix for canonical Linux library names.
VERSION_HANDLER_LINUXLIBNAME_FIELD_PREFIX = "linuxlibname-"
# Canonical prefix of Linux shared libraries.
LINUX_SHARED_LIBRARY_PREFIX = "lib"
# Extension used by Linux shared libraries.
LINUX_SHARED_LIBRARY_EXTENSION = ".so"
# Path relative to the "Assets" dir of native Linux plugin libraries.
LINUX_SHARED_LIBRARY_PATH = re.compile(
    r"^Plugins/(x86|x86_64)/(.*{ext})".format(
        ext=LINUX_SHARED_LIBRARY_EXTENSION.replace(".", r"\.")))
# Path components required for native desktop libraries.
SHARED_LIBRARY_PATH = re.compile(
    r"(^|/)Plugins/(x86|x86_64)/(.*/|)[^/]+\.(so|dll|bundle)$")
# Prefix of the keywords to be added to UPM manifest to link to legacy manifest.
UPM_KEYWORDS_MANIFEST_PREFIX = "vh-name:"
# Everything in a Unity plugin - at the moment - lives under the Assets
# directory
ASSETS_DIRECTORY = "Assets"
# Extension for asset metadata files.
ASSET_METADATA_FILE_EXTENSION = ".meta"
# Valid version for asset package in form of major.minor.patch(-preview)
VALID_VERSION_RE = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(-preview)?$")

# Documentation folder and filename for UPM package.
UPM_DOCUMENTATION_DIRECTORY = "Documentation~"
UPM_DOCUMENTATION_FILENAME = "index.md"

# String and unicode classes used to check types with safe_dict_get_value()
try:
  unicode("")  # See whether unicode class is available (Python < 3)
  STR_OR_UNICODE = [str, unicode]
except NameError:
  STR_OR_UNICODE = [str]
  unicode = str  # pylint: disable=redefined-builtin,invalid-name

def posix_path(path):
  """Convert path separators to POSIX style.

  Args:
    path: Path to convert.

  Returns:
    Path with POSIX separators, i.e / rather than \\.
  """
  return path.replace('\\', '/')


class MissingGuidsError(Exception):
  """Raised when GUIDs are missing for input files in export_package().

  Attributes:
    missing_guid_paths: List of files missing GUIDs.
  """

  def __init__(self, missing_guid_paths):
    """Initialize the instance.

    Args:
      missing_guid_paths: List of files missing GUIDs.
    """
    self.missing_guid_paths = sorted(list(set(missing_guid_paths)))
    super(MissingGuidsError, self).__init__(self.__str__())

  def __str__(self):
    """Retrieves a description of this error."""
    guids_file = FLAGS.guids_file if FLAGS.guids_file else ""
    plugins_version = FLAGS.plugins_version if FLAGS.plugins_version else ""
    return (("There were asset paths without a known guid. "
             "generate guids for these assets:\n\n"
             "{gen_guids} "
             "--guids_file=\"{guids_file}\" "
             "--version=\"{plugins_version}\" \"").format(
                 gen_guids=os.path.realpath(
                   os.path.join(os.path.dirname(__file__), "gen_guids.py")),
                 guids_file=guids_file, plugins_version=plugins_version) +
            "\" \"".join(self.missing_guid_paths) + "\"")


class DuplicateGuidsError(Exception):
  """Raised when GUIDs are duplicated for multiple export paths.

  Attributes:
    paths_by_guid: GUIDs that have multiple paths associated with them.
  """

  def __init__(self, paths_by_guid):
    self.paths_by_guid = paths_by_guid
    super(DuplicateGuidsError, self).__init__(self.__str__())

  def __str__(self):
    """Retrieves a description of this error."""
    return ("Found duplicate GUIDs that map to multiple paths.\n%s" %
            "\n".join(["%s --> %s" % (guid, str(sorted(paths)))
                       for guid, paths in self.paths_by_guid.items()]))


class DuplicateGuidsChecker(object):
  """Ensures no duplicate GUIDs are present in the project.

  Attributes:
    _paths_by_guid: Set of file paths by GUID.
  """

  def __init__(self):
    """Initialize this instance."""
    self._paths_by_guid = collections.defaultdict(set)

  def add_guid_and_path(self, guid, path):
    """Associate an export path with a GUID.

    Args:
      guid: GUID to add to this instance.
      path: Path associated with this GUID.
    """
    self._paths_by_guid[guid].add(posix_path(path))

  def check_for_duplicates(self):
    """Check the set of GUIDs for duplicate paths.

    Raises:
      DuplicateGuidsError: If multiple paths are found for the same GUID.
    """
    conflicting_paths_by_guid = dict(
        [(guid, paths)
         for guid, paths in self._paths_by_guid.items() if len(paths) > 1])
    if conflicting_paths_by_guid:
      raise DuplicateGuidsError(conflicting_paths_by_guid)


class YamlSerializer(object):
  """Loads and saves YAML files preserving the order of elements."""

  class OrderedLoader(yaml.Loader):
    """Overrides the default YAML loader to construct nodes as OrderedDict."""
    _initialized = False

    @classmethod
    def initialize(cls):
      """Installs the construct_mapping constructor on the Loader."""
      if not cls._initialized:
        cls.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG,
                            cls._construct_mapping)
        cls._initialized = True

    @staticmethod
    def _construct_mapping(loader, node):
      """Constructs an OrderedDict from a YAML node.

      Args:
        loader: yaml.Loader loading the file.
        node: Node being mapped to a python data structure.

      Returns:
        OrderedDict for the YAML node.
      """
      loader.flatten_mapping(node)
      return collections.OrderedDict(loader.construct_pairs(node))

  class OrderedDumper(yaml.Dumper):
    """Overrides the default YAML serializer.

    By default maps items to the OrderedDict structure, None to an empty
    strings and disables aliases.
    """
    _initialized = False

    @classmethod
    def initialize(cls):
      """Installs the representers on this class."""
      if not cls._initialized:
        # By default map data structures to OrderedDict.
        cls.add_representer(collections.OrderedDict, cls._represent_map)
        # By default map None to empty strings.
        cls.add_representer(type(None), cls._represent_none)
        # By default map unicode to strings.
        cls.add_representer(unicode, cls._represent_unicode)
        cls._initialized = True

    @staticmethod
    def _represent_unicode(dumper, data):
      """Strip the unicode tag from a yaml dump.

      Args:
        dumper: Generates the mapping.
        data: Data to generate the representer for.

      Returns:
        String mapping for unicode data.
      """
      return dumper.represent_scalar(u"tag:yaml.org,2002:str", data)

    @staticmethod
    def _represent_map(dumper, data):
      """Return a default representer for a map.

      Args:
         dumper: Generates the mapping.
         data: Data to generate a representor for.

      Returns:
         The default mapping for a map.
      """
      return dumper.represent_mapping(
          yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, data.items())

    @staticmethod
    def _represent_none(dumper, unused_data):
      """Return a representer for None that emits an empty string.

      Args:
         dumper: Generates the mapping.
         unused_data: Unused.

      Returns:
         A mapping that returns an empty string for None entries.
      """
      return dumper.represent_scalar(u"tag:yaml.org,2002:null", "")

    def ignore_aliases(self, unused_data):
      """Disable data structure aliases.

      Returns:
        True always.
      """
      return True

  def __init__(self, *unused_argv):
    """Create the serializer."""
    YamlSerializer.OrderedLoader.initialize()
    YamlSerializer.OrderedDumper.initialize()

  def load(self, yaml_string):
    """Load yaml from a string into this class.

    Args:
      yaml_string: String to load YAML from.

    Returns:
      OrderedDict loaded from YAML.
    """
    return yaml.load(yaml_string, Loader=YamlSerializer.OrderedLoader)

  def dump(self, data):
    """Generate a YAML string from the data in this class.

    Args:
      data: Set of Python data structures to dump to YAML.

    Returns:
      YAML string representation of this class.
    """
    return yaml.dump(data, Dumper=YamlSerializer.OrderedDumper,
                     default_flow_style=False)


def merge_ordered_dicts(merge_into, merge_from):
  """Merge ordered dicts.

  Merge nodes of merge_from into merge_into.

  - If a node exists in merge_into and merge_from and they're both a dictionary,
     merge them together.
  - If a node exists in merge_into and merge_from and both values are lists of
    dictionaries where each dictionary contains the keys "first" and "second",
    merge the lists using the value of "first" in each dictionary as the merge
    key. This allows modification of the platform targeting data structure in
    Unity asset metadata.

  In all other cases, replace the node in merge_info with the value from
  merge_from.

  Args:
    merge_into: OrderedDict instance to merge values into.
    merge_from: OrderedDict instance to merge values from.

  Returns:
    Value of merge_into.
  """

  def list_contains_dictionaries_with_keys(list_to_query, expected_keys):
    """Check a list for dictionaries with exactly the specified keys.

    Args:
      list_to_query: List to query.
      expected_keys: Keys to search for in each dictionary in the list.

    Returns:
      True if the list contains dictionaries with exactly the specified
      keys, False otherwise.
    """
    list_matches = False
    if issubclass(list_to_query.__class__, list):
      list_matches = list_to_query and True
      for item in list_to_query:
        if not (issubclass(item.__class__, dict) and
                sorted(item.keys()) == expected_keys):
          list_matches = False
          break
    return list_matches

  if (issubclass(merge_from.__class__, dict) and
      issubclass(merge_into.__class__, dict)):
    for merge_from_key, merge_from_value in merge_from.items():
      merge_into_value = merge_into.get(merge_from_key)
      if merge_into_value is not None:
        if (issubclass(merge_into_value.__class__, dict) and
            issubclass(merge_from_value.__class__, dict)):
          merge_ordered_dicts(merge_into_value, merge_from_value)
          continue
        if (list_contains_dictionaries_with_keys(
            merge_into_value, UNITY_5_6_PLATFORM_DATA_KEYS) and
            list_contains_dictionaries_with_keys(
                merge_from_value, UNITY_5_6_PLATFORM_DATA_KEYS)):
          for merge_from_list_item in merge_from_value:
            # Try finding the dictionary to merge based upon the hash of the
            # "first" item value.
            merged = None
            key = str(merge_from_list_item["first"])
            for merge_into_list_item in merge_into_value:
              if str(merge_into_list_item["first"]) == key:
                merge_ordered_dicts(merge_into_list_item, merge_from_list_item)
                merged = merge_into_list_item
                break
            # If the dictionary wasn't merged, add it to the list.
            if not merged:
              merge_into_value.append(merge_from_list_item)
          continue
      merge_into[merge_from_key] = merge_from_value
  return merge_into


def safe_dict_get_value(tree_node, key, default_value=None, value_classes=None):
  """Safely retrieve a value from a node in a tree read from JSON or YAML.

  The JSON and YAML parsers returns nodes that can be container or non-container
  types.  This method internally checks the node to make sure it's a dictionary
  before querying for the specified key.  If a default value or value_class are
  specified, this method also ensures the returned value is derived from the
  same type as default_value or value_type.

  Args:
    tree_node: Node to query.
    key: Key to retrieve from the node.
    default_value: Default value if the key doesn't exist in the node or the
      node isn't derived from dictionary.
    value_classes: List of expected classes of the key value.  If the returned
      type does not match one of these classes the default value is returned.
      If this is not specified, the class of the default_value is used instead.

  Returns:
    Value corresponding to key in the tree_node or default_value if the key does
    not exist or doesn't match the expected class.
  """
  if not issubclass(tree_node.__class__, dict):
    return default_value

  value = tree_node.get(key)
  if value is None:
    value = default_value
  elif default_value is not None or value_classes:
    if not value_classes:
      value_classes = [default_value.__class__]
      if default_value.__class__ == str:
        value_classes.append(unicode)

    matches_class = False
    for value_class in value_classes:
      if issubclass(value.__class__, value_class):
        matches_class = True
        break

    if not matches_class:
      logging.warning("Expected class %s instead of class %s while reading key "
                      "%s from %s.  Will use value %s instead of %s.\n%s",
                      value_classes, value.__class__, key, tree_node,
                      default_value, value, "".join(traceback.format_stack()))
      value = default_value
  return value


def safe_dict_set_value(tree_node, key, value):
  """Safely set a value to a node in a tree read from JSON or YAML.

  The JSON and YAML parsers returns nodes that can be container or non-container
  types.  This method internally checks the node to make sure it's a dictionary
  before setting for the specified key.  If value is None, try to remove key
  from tree_node.

  Args:
    tree_node: Node to set.
    key: Key of the entry to be added to the node.
    value: Value of the entry to be added to the node. If None, try to remove
      the entry from tree_node.

  Returns:
    Return tree_node
  """
  if not issubclass(tree_node.__class__, dict):
    return tree_node

  if value is None:
    if key in tree_node:
      del tree_node[key]
  else:
    tree_node[key] = value

  return tree_node


class GuidDatabase(object):
  """Reads GUIDs from .meta files and a GUID cache.

  Attributes:
    _guids_by_path: Cache of GUIDs by path.
    _duplicate_guids_checker: Instance of DuplicateGuidsChecker to ensure no
      duplicate GUIDs are present.
  """

  def __init__(self, duplicate_guids_checker, guids_json,
               plugin_version):
    """Initialize the database with data from the GUIDs database.

    Args:
      duplicate_guids_checker: Instance of DuplicateGuidsChecker.
      guids_json: JSON dictionary that contains the GUIDs to search for.
        See firebase/app/client/unity/gen_guids.py for the format.
        This can be None to not initialize the database.
      plugin_version: Version to use for GUID selection in the specified JSON.
    """
    self._guids_by_path = {}
    self._duplicate_guids_checker = duplicate_guids_checker

    if guids_json:
      guid_map = safe_dict_get_value(guids_json, plugin_version,
                                     default_value={})
      for filename, guid in guid_map.items():
        self.add_guid(posix_path(filename), guid)

      if plugin_version:
        # Aggregate guids for older versions of files.
        current_version = packaging.version.Version(plugin_version)
        for version in sorted(guids_json, key=packaging.version.Version,
                              reverse=True):
          # Skip all versions after and including the current version.
          if packaging.version.Version(version) >= current_version:
            continue
          # Add all guids for files to the current version.
          guids_by_filename = guids_json[version]
          for filename in guids_by_filename:
            if filename not in guid_map:
              self.add_guid(filename, guids_by_filename[filename])

  def add_guid(self, path, guid):
    """Add a GUID for the specified path to the guid_map and GUID checker.

    Args:
      path: Path associated with the GUID.
      guid: GUID for the asset at the path.
    """
    path = posix_path(path)
    self._guids_by_path[path] = guid
    self._duplicate_guids_checker.add_guid_and_path(guid, path)

  def read_guids_from_assets(self, assets):
    """Read GUIDs from a set of metadata files into the database.

    Args:
      assets: List of Asset instances to read GUIDs from.

    Raises:
      MissingGuidsError: If GUIDs are missing for any of the specified assets.
      DuplicateGuidsError: If any of the read GUIDs are duplicates.
    """
    missing_guid_paths = []
    for asset in assets:
      existing_guid = None
      metadata_guid = safe_dict_get_value(asset.importer_metadata, "guid",
                                          value_classes=STR_OR_UNICODE)
      try:
        existing_guid = self.get_guid(asset.filename_guid_lookup)
      except MissingGuidsError:
        pass
      guid = metadata_guid if metadata_guid else existing_guid
      if guid:
        self.add_guid(asset.filename_guid_lookup, guid)
      else:
        missing_guid_paths.append(asset.filename_guid_lookup)
    if missing_guid_paths:
      raise MissingGuidsError(missing_guid_paths)
    self._duplicate_guids_checker.check_for_duplicates()

  def get_guid(self, path):
    """Get a GUID for the specified path.

    Args:
      path: Asset export path to retrieve the GUID of.

    Returns:
      GUID of the asset.

    Raises:
      MissingGuidsError: If the GUID isn't found.
    """
    path = posix_path(path)
    guid = self._guids_by_path.get(path)
    if not guid:
      raise MissingGuidsError([path])
    return guid


def copy_and_set_rwx(source_path, target_path):
  """Copy a file/folder and set the target to readable / writeable & executable.

  Args:
    source_path: File to copy from.
    target_path: Path to copy to.
  """
  logging.debug("Copying %s --> %s", source_path, target_path)
  file_mode = stat.S_IRWXU | stat.S_IRWXG | stat.S_IROTH | stat.S_IXOTH

  if os.path.isfile(source_path):
    target_dir = os.path.dirname(target_path)
    if not os.path.exists(target_dir):
      os.makedirs(target_dir)
    shutil.copy(source_path, target_path)
    os.chmod(target_path, file_mode)
  elif os.path.isdir(source_path):
    shutil.copytree(source_path, target_path)
    os.chmod(target_path, file_mode)
    for current_dir, directories, filenames in os.walk(target_path):
      for directory in [os.path.join(current_dir, d) for d in directories]:
        os.chmod(directory, file_mode)
      for filename in [os.path.join(current_dir, f) for f in filenames]:
        os.chmod(filename, file_mode)

def version_handler_tag(islabel=True, field=None, value=None):
  """Generate a VersionHandler filename or label.

  For more information, see
  third_party/unity/unity_jar_resolver/source/VersionHandler

  Args:
    islabel: Whether the generated field is a label. If this is false this
      simply returns the field and value.
    field: Type of the field.
    value: Value of the field.

  Returns:
    Label string.
  """
  label_components = [VERSION_HANDLER_LABEL_PREFIX] if islabel else []
  if field:
    if value:
      label_components.append(
          field + value.replace(VERSION_HANDLER_FIELD_SEPARATOR, "-"))
    else:
      label_components.append(field)
  return VERSION_HANDLER_FIELD_SEPARATOR.join(label_components)


def version_handler_filename(filename, field_value_list):
  """Generate a VersionHandler filename.

  For more information, see
  third_party/unity/unity_jar_resolver/source/VersionHandler

  Args:
    filename: Base filename VersionHandler fields are injected into.
    field_value_list: List of (field, value) tuples added to the end of the
      filename.

  Returns:
    Filename for a file managed by the VersionHandler.
  """
  filename_prefix, extension = os.path.splitext(filename)
  directory, basename = os.path.split(filename_prefix)
  components = [os.path.join(directory,
                             basename.replace(VERSION_HANDLER_FIELD_SEPARATOR,
                                              "-"))]
  fields = []
  for field, value in field_value_list:
    fields.append(version_handler_tag(islabel=False, field=field, value=value))
  if fields:
    components.extend([VERSION_HANDLER_FIELD_SEPARATOR,
                       VERSION_HANDLER_FIELD_SEPARATOR.join(fields)])
  components.append(extension)
  return posix_path("".join(components))


class Asset(object):
  """Asset to export.

  Attributes:
    _filename: Relative path of the file referenced by this asset.
    _filename_guid_lookup: Filename to reference GUID.
    _filename_absolute: Absolute path of the file referenced by this asset.
    _importer_metadata: OrderedDict of Unity asset metadata used to construct
      this class.
    _is_folder: Whether this asser is for a folder.
  """

  def __init__(self,
               filename,
               filename_absolute,
               importer_metadata,
               filename_guid_lookup=None,
               is_folder=False):
    """Initialize an asset.

    Args:
      filename: Name of the file referenced by this asset.
      filename_absolute: Absolute path of the file referenced by this
        asset. If this is None, the filename argument is used instead.
      importer_metadata: OrderedDict of Unity importer metadata for the file.
      filename_guid_lookup: Filename to reference GUID. If this is None, the
        filename argument is used instead.
      is_folder: Whether this asser is for a folder.
    """
    self._filename = filename
    self._filename_guid_lookup = filename_guid_lookup or filename
    self._filename_absolute = filename_absolute or filename
    self._importer_metadata = importer_metadata
    self._is_folder = is_folder

  def __eq__(self, other):
    """Overrides == operator."""
    if isinstance(other, Asset):
      return (self.filename == other.filename and
              self.filename_guid_lookup == other.filename_guid_lookup and
              self.filename_absolute == other.filename_absolute and
              self.importer_metadata == other.importer_metadata and
              self.is_folder == other.is_folder)
    return False

  def __ne__(self, other):
    """Overrides != operator (Required in Python2)."""
    return not self.__eq__(other)

  @property
  def filename(self):
    """Get the name of the file referenced by this asset.

    Returns:
      Filename string.
    """
    return posix_path(self._filename)

  @property
  def filename_absolute(self):
    """Get the absolute path of the file referenced by this asset.

    Returns:
      Filename string.
    """
    return posix_path(self._filename_absolute)

  @property
  def filename_guid_lookup(self):
    """Get the filename to reference GUID.

    Returns:
      Filename string.
    """
    return posix_path(self._filename_guid_lookup)

  @property
  def is_folder(self):
    """Get whether this asset is for a folder.

    Returns:
      Boolean whether this asset is for a folder
    """
    return self._is_folder

  def __repr__(self):
    """Returns a human readable string.

    Returns:
      A human readable representation of this object.
    """
    return "<Asset filename=%s metadata=%s>" % (self.filename,
                                                self.importer_metadata)

  @staticmethod
  def add_labels_to_metadata(importer_metadata, labels):
    """Add to the labels field of Unity asset metadata OrderedDict.

    Args:
      importer_metadata: OrderedDict to modify.
      labels: Set of labels to add to asset_metadata.

    Returns:
      Modified importer_metadata.
    """
    existing_labels = safe_dict_get_value(importer_metadata, "labels",
                                          value_classes=[list])
    new_labels = set(existing_labels or []).union(labels)
    if new_labels:
      importer_metadata["labels"] = sorted(new_labels)
    elif existing_labels is not None:
      del importer_metadata["labels"]
    return importer_metadata

  @staticmethod
  def disable_unsupported_platforms(importer_metadata, filename):
    """Disable all platforms that are not supported by a shared library asset.

    If the asset references a shared library, disable all platforms that are
    not supported by the asset based upon the asset's filename extension.

    Args:
      importer_metadata: Metadata to modify. This is modified in-place.
      filename: Name of the asset file.

    Returns:
      Modified importer_metadata.
    """
    filename = posix_path(os.path.normpath(filename))
    is_shared_library = SHARED_LIBRARY_PATH.search(filename)
    if not is_shared_library:
      return importer_metadata

    supported_platforms = PLATFORMS_BY_SHARED_LIBRARY_EXTENSION.get(
        os.path.splitext(filename)[1])
    if not supported_platforms:
      return importer_metadata

    plugin_importer = safe_dict_get_value(importer_metadata, "PluginImporter",
                                          default_value={})
    serialized_version = safe_dict_get_value(
        plugin_importer, "serializedVersion", default_value=1)
    if serialized_version != 1:
      logging.warning("Unsupported platformData version %d.  "
                      "Unable to configure platforms for shared library "
                      "%s", serialized_version, filename)
      return importer_metadata

    platform_data = safe_dict_get_value(plugin_importer, "platformData",
                                        default_value={})
    disable_platforms = sorted(set(platform_data).difference(
        set(supported_platforms)))
    # Disable the Any platform if any platforms are disabled.
    if disable_platforms:
      any_config = platform_data.get("Any", collections.OrderedDict())
      any_config["enabled"] = 0
      platform_data["Any"] = any_config
    # Disable all platforms in the set.
    for current_platform in disable_platforms:
      platform_data[current_platform] = copy.deepcopy(
          PLUGIN_IMPORTER_METADATA_TEMPLATE[
              "PluginImporter"]["platformData"][current_platform])
    logging.debug("Disabled platforms %s for %s", disable_platforms, filename)
    return importer_metadata

  @staticmethod
  def platform_data_get_entry(platform_data_item):
    """Retrieve a platform entry from an item in the platformData list.

    The PluginImporter.platformData is a list when
    PluginImporter.serializedVersion is 2.  This list can either contain a
    dictionary of platform information dictionaries indexed by "first" /
    "second" (Unity 2017+) or a list of dictionaries containing just a "data"
    entry (Unity 5.6) which in turn contain a platform information entry with
    "first" / "second".  This method retrieves the values of the
    "first" / "second" tuples from an entry in the platformData list.

    Args:
      platform_data_item: Entry in the platformData list.

    Returns:
      Tuple of values retrieved from the "first" and "second" entries of the
      platform information dictionary.
    """
    entry = platform_data_item.get("data", platform_data_item)
    return (safe_dict_get_value(entry, "first",
                                default_value=collections.OrderedDict()),
            safe_dict_get_value(entry, "second",
                                default_value=collections.OrderedDict()))

  @staticmethod
  def set_cpu_for_desktop_platforms(importer_metadata):
    """Enable CPU(s) for each enabled desktop platform in the metadata.

    Args:
      importer_metadata: Metadata to modify.

    Returns:
      Modified importer_metadata.
    """
    plugin_importer = safe_dict_get_value(
        importer_metadata, "PluginImporter", default_value={})
    serialized_version = safe_dict_get_value(
        plugin_importer, "serializedVersion", default_value=1)

    if serialized_version == 1:
      platform_data = safe_dict_get_value(plugin_importer, "platformData",
                                          default_value={})
      for platform_name, options in platform_data.items():
        if not safe_dict_get_value(options, "enabled", default_value=0):
          continue
        # Override the CPU of the appropriate platforms.
        cpu = CPU_BY_DESKTOP_PLATFORM.get(platform_name)
        if not cpu:
          continue
        settings = options.get("settings", collections.OrderedDict())
        if settings.get("CPU", "None") == "None":
          settings["CPU"] = cpu
          options["settings"] = settings
    else:
      platform_data = safe_dict_get_value(plugin_importer, "platformData",
                                          default_value=[])
      for entry in platform_data:
        # Parse the platform name tuple from the "first" dictionary.
        first, second = Asset.platform_data_get_entry(entry)
        platform_tuple = list(first.items())[0]
        if len(platform_tuple) < 2:
          continue
        unused_platform_target, platform_name = platform_tuple
        if not second.get("enabled", 0):
          continue
        # Override the CPU of the appropriate platforms.
        cpu = CPU_BY_DESKTOP_PLATFORM.get(platform_name)
        if not cpu:
          continue
        settings = safe_dict_get_value(second, "settings",
                                       default_value=collections.OrderedDict())
        if settings.get("CPU", "None") == "None":
          settings["CPU"] = cpu
          second["settings"] = settings
    return importer_metadata

  @staticmethod
  def set_cpu_for_android(importer_metadata, cpu_string):
    """Sets the CPU for Android in the metadata if enabled.

    Args:
      importer_metadata: Metadata to modify.
      cpu_string: The desired CPU string value.

    Returns:
      Modified importer_metadata.
    """
    plugin_importer = safe_dict_get_value(
        importer_metadata, "PluginImporter", default_value={})
    serialized_version = safe_dict_get_value(
        plugin_importer, "serializedVersion", default_value=1)

    if serialized_version == 1:
      platform_data = safe_dict_get_value(plugin_importer, "platformData",
                                          default_value={})
      for platform_name, options in platform_data.items():
        if not safe_dict_get_value(options, "enabled", default_value=0):
          continue
        if not cpu_string:
          continue
        if platform_name == "Android":
          settings = options.get("settings", collections.OrderedDict())
          settings["CPU"] = cpu_string
          options["settings"] = settings
    else:
      platform_data = safe_dict_get_value(plugin_importer, "platformData",
                                          default_value=[])
      for entry in platform_data:
        # Parse the platform name tuple from the "first" dictionary.
        first, second = Asset.platform_data_get_entry(entry)
        platform_tuple = list(first.items())[0]
        if len(platform_tuple) < 2:
          continue
        unused_platform_target, platform_name = platform_tuple
        if not second.get("enabled", 0):
          continue
        if not cpu_string:
          continue
        settings = safe_dict_get_value(second, "settings",
                                       default_value=collections.OrderedDict())
        if platform_name == "Android":
          settings["CPU"] = cpu_string
          second["settings"] = settings
    return importer_metadata

  @staticmethod
  def apply_any_platform_selection(importer_metadata):
    """Enable / disable all platforms if the "Any" platform is enabled.

    Args:
      importer_metadata: Metadata to modify. This is modified in-place.

    Returns:
      Modified importer_metadata.
    """
    plugin_importer = safe_dict_get_value(
        importer_metadata, "PluginImporter", default_value={})
    serialized_version = safe_dict_get_value(
        plugin_importer, "serializedVersion", default_value=1)

    if serialized_version == 1:
      platform_data = safe_dict_get_value(plugin_importer, "platformData",
                                          default_value={})
      # Check PluginImporter.platformData.Any.enabled for the Any platform
      # enabled in Unity 4 & early 5 metadata.
      any_enabled = platform_data.get("Any", {}).get("enabled", 0)
      if not any_enabled:
        return importer_metadata
      # If the Any platform is present and either enabled or disabled, enable
      # for disable all platforms.
      for platform_name, default_config in PLUGIN_IMPORTER_METADATA_TEMPLATE[
          "PluginImporter"]["platformData"].items():
        config = platform_data.get(platform_name)
        if config is None:
          config = copy.deepcopy(default_config)
        config["enabled"] = any_enabled
        platform_data[platform_name] = config
    else:
      # Search for the Any platform and retrieve if it's present and
      # enabled / disabled if Unity 5.4+ metadata.
      platform_data = safe_dict_get_value(plugin_importer, "platformData",
                                          default_value=[])
      any_enabled = 0
      for entry in platform_data:
        first, second = Asset.platform_data_get_entry(entry)
        if "Any" in first:
          any_enabled = second.get("enabled", 0)
          break
      if not any_enabled:
        return importer_metadata
      remaining_platforms = [platform_name for platform_name in (
          PLUGIN_IMPORTER_METADATA_TEMPLATE[
              "PluginImporter"]["platformData"]) if platform_name != "Any"]
      new_platform_data = []
      unity_5_6_format = False
      # Modify the "enabled" field of each platform in the metadata.
      for entry in platform_data:
        unity_5_6_format = "data" in entry
        # Parse the platform name tuple from the "first" dictionary.
        first, second = Asset.platform_data_get_entry(entry)
        platform_tuple = list(first.items())[0]
        if len(platform_tuple) >= 2:
          unused_platform_target, platform_name = platform_tuple
          if platform_name in remaining_platforms:
            remaining_platforms.remove(platform_name)
          entry = copy.deepcopy(entry)
          _, second = Asset.platform_data_get_entry(entry)
          second["enabled"] = any_enabled
        new_platform_data.append(entry)

      # Add all platforms that were not present in the default metadata.
      for platform_name in remaining_platforms:
        platform_target = PLATFORM_TARGET_BY_PLATFORM.get(platform_name)
        entry = collections.OrderedDict([
            ("first", collections.OrderedDict([
                (platform_target, platform_name)])),
            ("second", collections.OrderedDict([
                ("enabled", any_enabled)]))])
        if unity_5_6_format:
          entry = collections.OrderedDict([("data", entry)])
        new_platform_data.append(entry)
      plugin_importer["platformData"] = new_platform_data
    return importer_metadata

  @property
  def importer_metadata_original(self):
    """Get the original metadata section used to import this asset.

    Returns:
      Importer section of Unity asset metadata as an OrderedDict.
    """
    return self._importer_metadata

  @property
  def importer_metadata(self):
    """Get the Unity metadata section used to import this asset.

    Returns:
      Importer section of Unity asset metadata as an OrderedDict.
    """
    # If this is a linux library label the asset with the basename of
    # the library so that it can be renamed to work with different versions
    # of Unity.
    linuxlibname_label_prefix = version_handler_tag(
        field=VERSION_HANDLER_LINUXLIBNAME_FIELD_PREFIX)
    labels = set()
    if not [l for l in labels if l.startswith(linuxlibname_label_prefix)]:
      match = LINUX_SHARED_LIBRARY_PATH.match(self._filename)
      if match:
        basename = os.path.basename(match.group(2))
        # Strip prefix and extension.
        if basename.startswith(LINUX_SHARED_LIBRARY_PREFIX):
          basename = basename[len(LINUX_SHARED_LIBRARY_PREFIX):]
        if basename.endswith(LINUX_SHARED_LIBRARY_EXTENSION):
          basename = basename[:-len(LINUX_SHARED_LIBRARY_EXTENSION)]
        labels.add(version_handler_tag(
            field=VERSION_HANDLER_LINUXLIBNAME_FIELD_PREFIX,
            value=basename))

    # Add gvhp_exportpath- label
    labels.add(VERSION_HANDLER_PRESERVE_LABEL_PREFIX +
               VERSION_HANDLER_FIELD_SEPARATOR +
               VERSION_HANDLER_PRESERVE_EXPORT_PATH_FIELD_PREFIX +
               self.filename)

    metadata = copy.deepcopy(self._importer_metadata)
    metadata = Asset.add_labels_to_metadata(metadata, labels)
    metadata = Asset.disable_unsupported_platforms(metadata, self._filename)
    metadata = Asset.apply_any_platform_selection(metadata)
    metadata = Asset.set_cpu_for_desktop_platforms(metadata)
    return metadata

  @staticmethod
  def write_metadata(filename, metadata_list):
    """Write asset metadata to a file.

    Args:
      filename: Name of the file to write.
      metadata_list: List of OrderedDict instances to combine, in the
        specified order, to generate the data structure to serialize in YAML to
        the metadata file.
    """
    output_metadata = collections.OrderedDict()
    for metadata in metadata_list:
      merge_ordered_dicts(output_metadata, metadata)
    # Unity does throws exceptions when encountering empty lists of labels,
    # so filter them from the metadata.
    if not output_metadata.get("labels") and "labels" in output_metadata:
      del output_metadata["labels"]
    with open(filename, "wt", encoding='utf-8') as metadata_file:
      metadata_file.write(YamlSerializer().dump(output_metadata))

  def write(self, output_dir, guid, timestamp=-1):
    """Write a asset and it's metadata to output_dir.

    Given an asset file, path and metadata, this method creates a Unity plugin
    directory structure that facilitates the creation of a .unitypackage
    archive.

    This method generates:
    * <guid>/asset
      Copy of the file referenced by asset_filename.
    * <guid>/asset.meta
      Metadata for this asset including `importer_metadata`.
    * <guid>/pathname
      Text file which contains export_path.

    Args:
      output_dir: Output directory where the unity package can be staged for
        archiving. The final .unitypackage archive can be built from the files
        placed here.
      guid: The guid to use to pack the asset. This will override the GUID in
        any existing metadata.
      timestamp: Timestamp to write into the metadata file if a timestamp
        does not already exist in importer_metadata.  If this argument < 0 the
        timestamp is set to the creation time of the source file.

    Returns:
      Directory containing the asset, asset.meta and pathname files.
      Return None if the asset is for a folder.

    Raises:
      RuntimeError: If the asset is exported again with a different path or
        asset contents.
    """
    # Ignore folder asset when writing to unitypackage
    if self.is_folder:
      return None

    # Create the output directory.
    output_asset_dir = os.path.join(output_dir, guid)
    if not os.path.exists(output_asset_dir):
      os.makedirs(output_asset_dir)

    # Copy the asset to the output folder.
    output_asset_filename = os.path.join(output_asset_dir, "asset")
    copy_and_set_rwx(self.filename_absolute, output_asset_filename)

    # Create the "asset.meta" file.
    output_asset_metadata_filename = (output_asset_filename +
                                      ASSET_METADATA_FILE_EXTENSION)

    self.create_metadata(output_asset_metadata_filename, guid, timestamp)

    # Create the "pathname" file.
    # export_filename is the path of the file when it's imported into a Unity
    # project.
    with open(os.path.join(output_asset_dir, "pathname"), "wt",
        encoding='utf-8') as (pathname_file):
      pathname_file.write(posix_path(os.path.join(ASSETS_DIRECTORY,
                                                  self.filename)))
    return output_asset_dir

  def write_upm(self, output_dir, guid, timestamp=-1):
    """Write a asset and it's metadata to output_dir for UPM package.

    Given an asset file, path and metadata, this method creates a Unity custom
    package structure that facilitates the creation of a .tgz archive.

    This method generates:
    * path/to/asset/asset_filename
      Copy of the file referenced by asset_filename, or create folders if this
      asset is for a folder.
    * path/to/asset/asset_filename.meta
      Metadata for this asset.

    Args:
      output_dir: Output directory where the unity package can be staged for
        archiving. The final .unitypackage archive can be built from the files
        placed here.
      guid: The guid to use to pack the asset. This will override the GUID in
        any existing metadata.
      timestamp: Timestamp to write into the metadata file if a timestamp does
        not already exist in importer_metadata.  If this argument < 0 the
        timestamp is set to the creation time of the source file.

    Returns:
      Directory containing the asset and asset.meta.

    Raises:
      RuntimeError: If the asset is exported again with a different path or
        asset contents.
    """
    # Create the output directory.
    output_asset = os.path.join(output_dir, "package", self.filename)
    output_asset_dir = os.path.dirname(output_asset)

    if self.is_folder:
      os.makedirs(output_asset)
    else:
      # Copy the asset to the output folder.
      copy_and_set_rwx(self.filename_absolute, output_asset)

    # Create the "path/to/asset/asset_filename.meta" file.
    output_asset_metadata_filename = (
        output_asset + ASSET_METADATA_FILE_EXTENSION)

    self.create_metadata(output_asset_metadata_filename, guid, timestamp)

    return output_asset_dir

  def create_metadata(self, filename, guid, timestamp=-1):
    """Create metadata file for the asset.

    Args:
      filename: Filename for the metadata.
      guid: The guid to use to pack the asset. This will override the GUID in
        any existing metadata.
      timestamp: Timestamp to write into the metadata file if a timestamp does
        not already exist in importer_metadata.  If this argument < 0 the
        timestamp is set to the creation time of the source file, or 0 if this
        asset is a folder.

    Raises:
      RuntimeError: If the asset is exported again with a different path or
        asset contents.
    """
    if self.is_folder:
      importer_metadata = copy.deepcopy(DEFAULT_FOLDER_METADATA_TEMPLATE)
    else:
      importer_metadata = self.importer_metadata

    # If a timestamp is specified on the command line override the timestamp
    # in the metadata if it isn't set.
    if timestamp < 0:
      if self.is_folder:
        timestamp = 0
      else:
        timestamp = int(os.path.getctime(self.filename_absolute))
    timestamp = safe_dict_get_value(
        importer_metadata, "timeCreated", default_value=timestamp)

    Asset.write_metadata(filename, [
        DEFAULT_METADATA_TEMPLATE, importer_metadata,
        collections.OrderedDict([("guid", guid), ("timeCreated", timestamp)])
    ])

  @staticmethod
  def sorted_by_filename(assets):
    """Sort a sequence of assets by filename.

    Args:
      assets: Sequence of assets to sort.

    Returns:
      List of Asset instances sorted by filename.
    """
    return sorted([asset for asset in assets],
                  key=lambda asset: asset.filename)


class ProjectConfigurationError(Exception):
  """Raised when there is an error parsing the project configuration."""
  pass


class ConfigurationBlock(object):
  """Common attributes for all export configuration blocks.

  Attributes:
    _json: JSON data for the configuration block.
  """

  def __init__(self, json_data):
    """Initialize the configuration block.

    Args:
      json_data: JSON dictionary to read common configuration from.
    """
    self._json = json_data

  @property
  def sections(self):
    """Get the sections that enable this block.

    Returns:
      List of strings indicating when this block should be enabled.
    """
    return set(safe_dict_get_value(self._json, "sections", default_value=[]))

  def get_enabled(self, sections):
    """Determine whether this block is enabled given `sections`.

    Args:
      sections: Set of sections that are enabled for the block.

    Returns:
      True if this block specifies no sections or the specified sections
      overlap this block's sections whitelist, False otherwise.
    """
    block_sections = self.sections
    return ((not block_sections) or
            block_sections.intersection(sections))


class AssetConfiguration(ConfigurationBlock):
  """Export configuration for a set of assets.

  Attributes:
    _package: PackageConfiguration instance this asset group was parsed from.
    _json: Dictionary containing the raw asset group configuration.
  """

  def __init__(self, package, asset_json):
    """Initialize this asset configuration from JSON.

    Args:
      package: PackageConfiguration instance `asset_json` was parsed from.
      asset_json: JSON dictionary for this set of assets.

    Raises:
      ProjectConfigurationError: If an invalid field is specified.
    """
    super(AssetConfiguration, self).__init__(asset_json)
    self._package = package
    self._asset_json = asset_json

  @property
  def importer_metadata(self):
    """Get the Unity metadata section used to import this asset.

    Returns:
      Importer section of Unity asset metadata as an OrderedDict.

    Raises:
      ProjectConfigurationError: If the importer type or the cpu string for a
        PluginImporter is invalid.
    """
    importer_type = safe_dict_get_value(self._json, "importer",
                                        default_value="DefaultImporter")
    importer_metadata = None
    if importer_type == "DefaultImporter":
      importer_metadata = copy.deepcopy(DEFAULT_IMPORTER_METADATA_TEMPLATE)
    elif importer_type == "PluginImporter":
      platforms = set(safe_dict_get_value(
          self._json, "platforms", default_value=["Editor", "Android", "iOS",
                                                  "tvOS",
                                                  STANDALONE_PLATFORM_ALIAS]))
      cpu_string = safe_dict_get_value(self._json, "cpu",
                                       default_value="AnyCPU")

      # Select standalone platforms.
      standalone_platforms = []
      if STANDALONE_PLATFORM_ALIAS in platforms:
        standalone_platforms = DESKTOP_PLATFORMS_BY_CPU.get(cpu_string)
        if not standalone_platforms:
          raise ProjectConfigurationError(
              "Unknown cpu type %s for package %s, paths %s" % (
                  cpu_string, self._package.name, str(self.paths)))
        platforms.remove(STANDALONE_PLATFORM_ALIAS)
        # Enable "universal" platforms where applicable.
        universal_platforms = set()
        for standalone in standalone_platforms:
          universal_platform = ARCH_SPECIFIC_TO_UNIVERSAL_PLATFORM.get(
              standalone)
          if universal_platform:
            universal_platforms.add(universal_platform)
        platforms = platforms.union(standalone_platforms).union(
            universal_platforms)

      # Enable selected platforms.
      importer_metadata = copy.deepcopy(PLUGIN_IMPORTER_METADATA_TEMPLATE)
      platform_data = importer_metadata["PluginImporter"]["platformData"]
      for target_platform in platforms:
        platform_data_options = platform_data.get(target_platform,
                                                  collections.OrderedDict())
        platform_data[target_platform] = platform_data_options
        platform_data_options["enabled"] = 1
      importer_metadata = Asset.set_cpu_for_desktop_platforms(
          importer_metadata)
      if "Android" in platforms and cpu_string != "AnyCPU":
        importer_metadata = Asset.set_cpu_for_android(
            importer_metadata, cpu_string)
    else:
      raise ProjectConfigurationError(
          "Unknown importer type %s for package %s, paths %s" % (
              importer_type, self._package.name, str(self.paths)))
    Asset.add_labels_to_metadata(importer_metadata, self.labels)
    return importer_metadata

  @property
  def labels(self):
    """Get the set of asset labels that should be applied to this asset.

    Returns:
      Set of asset label strings to apply to the assets.
    """
    return set(safe_dict_get_value(
        self._json, "labels", default_value=[])).union(self._package.labels)

  @property
  def paths(self):
    """Get the set of paths to include in this group of assets.

    Returns:
      Set of paths to include in this group.
    """
    return set(safe_dict_get_value(self._json, "paths", default_value=[]))

  @property
  def override_metadata(self):
    """Get the metadata used override.

    Returns:
      OrderedDict metadata to merge over the metadata in each asset referenced
      by this AssetConfiguration instance.
    """
    return safe_dict_get_value(self._json, "override_metadata",
                               default_value=collections.OrderedDict(),
                               value_classes=[dict])

  @property
  def override_metadata_upm(self):
    """Get the metadata used override for UPM package.

    Returns:
      OrderedDict metadata to merge over the metadata in each asset referenced
      by this AssetConfiguration instance.
    """
    return safe_dict_get_value(
        self._json,
        "override_metadata_upm",
        default_value=collections.OrderedDict(),
        value_classes=[dict])

  def find_assets(self, assets_dirs, for_upm=False):
    """Find the assets referenced by the `paths` attribute.

    Args:
      assets_dirs: List of root directories to search for paths referenced by
        this asset group.
      for_upm: Whether this is for packaging for Unity Package Manager

    Returns:
      List of Asset instances referencing file paths found under root_dir
      matching the patterns in the `paths` attribute. All returned paths are
      relative to the specified assets_dir.
    """
    matching_files = set()
    assets_dir_by_matching_file = {}
    paths_matching_no_files = []
    for wildcard_path in sorted(self.paths):
      found_assets = []
      for assets_dir in assets_dirs:
        assets_dir = os.path.normpath(assets_dir)
        for path in glob.glob(os.path.join(assets_dir, wildcard_path)):
          if os.path.isdir(path):
            for current_root, _, files in os.walk(path):
              for filename in files:
                if not AssetConfiguration._is_metadata_file(filename):
                  relative_path = os.path.relpath(os.path.join(
                      current_root, filename), assets_dir)
                  found_assets.append(relative_path)
                  matching_files.add(relative_path)
                  assets_dir_by_matching_file[relative_path] = assets_dir
          elif not AssetConfiguration._is_metadata_file(path):
            relative_path = os.path.relpath(path, assets_dir)
            found_assets.append(relative_path)
            matching_files.add(relative_path)
            assets_dir_by_matching_file[relative_path] = assets_dir
      if not found_assets:
        paths_matching_no_files.append(wildcard_path)
    if paths_matching_no_files:
      logging.warning(
          "Package %s references paths that match no files %s",
          self._package.name, str(paths_matching_no_files))
    importer_metadata = self.importer_metadata

    assets = []
    # If metadata exists, read it for each file in this group creating a list
    # Asset instances that references each filename and associated metadata.
    for filename in sorted(matching_files):
      assets_dir = assets_dir_by_matching_file[filename]
      asset_metadata_filename = os.path.join(
          assets_dir, filename + ASSET_METADATA_FILE_EXTENSION)
      asset_metadata = copy.deepcopy(importer_metadata)
      if os.path.exists(asset_metadata_filename):
        existing_asset_metadata = collections.OrderedDict()
        with open(asset_metadata_filename, "rt", encoding='utf-8') as (
            asset_metadata_file):
          existing_asset_metadata = YamlSerializer().load(
              asset_metadata_file.read())
        if existing_asset_metadata:
          # If the file already has metadata use it, preserving the labels from
          # this instance.
          Asset.add_labels_to_metadata(existing_asset_metadata, self.labels)
          asset_metadata = existing_asset_metadata

      merge_ordered_dicts(asset_metadata, self.override_metadata)
      # Override metadata again using "override_metadata_upm"
      if for_upm:
        merge_ordered_dicts(asset_metadata, self.override_metadata_upm)

      assets.append(Asset(filename, os.path.join(assets_dir, filename),
                          asset_metadata))

    return Asset.sorted_by_filename(assets)

  @staticmethod
  def _is_metadata_file(path):
    """Returns true if the path references a Unity asset metadata file.

    Args:
      path: Path of the file to query.

    Returns:
      True if the file is an asset metadata file, False otherwise.
    """
    return os.path.splitext(path)[1] == ".meta"


class PackageConfiguration(ConfigurationBlock):
  """Export configuration for a package.

  Attributes:
    _project: ProjectConfiguration instance this package was parsed from.
    _json: Dictionary containing the raw package configuration.
  """

  def __init__(self, project, package_json):
    """Initialize this object with JSON package data.

    Args:
      project: ProjectConfiguration instance this package was parsed from.
      package_json: Package dictionary parsed from JSON project data.

    Raises:
      ProjectConfigurationError: If the package has no name.
    """
    super(PackageConfiguration, self).__init__(package_json)
    self._project = project
    self._json = package_json
    if not safe_dict_get_value(self._json, "name",
                               value_classes=STR_OR_UNICODE):
      raise ProjectConfigurationError("Package found with no name")

  @property
  def name(self):
    """Get the name of the exported package.

    Returns:
      Name of package as string.
    """
    return self._json["name"]

  @property
  def version(self):
    """Get the version of this project.

    Returns:
      Version as a string in form of "major.minor.revision"
    """

    return self._project.version

  @property
  def tarball_name(self):
    """Get the tarball filename for Unity Package Manager package.

    Returns:
      Tarball filename as string.
    """

    if not self.common_package_name or not self.version:
      return None
    return self.common_package_name + "-" + self.version + ".tgz"

  @property
  def export_upm(self):
    """Get whether to export this package as a Unity Package Manager package.

    Returns:
      True if the package should be exported.
    """
    return safe_dict_get_value(self._json, "export_upm", default_value=0) == 1

  @property
  def upm_package_config(self):
    """Get the package configuration for Unity Package Manager package.

    Returns:
      Package configuration as dict.
    """

    return safe_dict_get_value(self._json, "upm_package_config")

  @property
  def upm_manifest(self):
    """Get the package manifest for Unity Package Manager package.

    Returns:
      UPM manifest as dict.
    """
    package_info = self.upm_package_config
    if not package_info:
      return None

    return safe_dict_get_value(package_info, "manifest")

  @property
  def common_manifest(self):
    """Get common package manifest.

    Returns:
      Manifest as dict.
    """
    return safe_dict_get_value(self._json, "common_manifest")

  @property
  def common_package_name(self):
    """Get the common name of this package.

    Returns:
      Name of package as string.
    """
    package_info = self.common_manifest
    if not package_info:
      return None

    return safe_dict_get_value(package_info, "name")

  @property
  def common_package_display_name(self):
    """Get the common display name of this package.

    Returns:
      Name of package as string.
    """
    package_info = self.common_manifest
    if not package_info:
      return self.package_name

    return safe_dict_get_value(package_info, "display_name",
                               default_value=self.package_name)

  @property
  def common_package_description(self):
    """Get the common description of this package.

    Returns:
      Name of package as string.
    """
    package_info = self.common_manifest
    if not package_info:
      return None

    description = safe_dict_get_value(package_info, "description")
    if description:
      if issubclass(description.__class__, list):
        description = "".join(description)
      else:
        description = str(description)
    return description

  def _get_includes(self, recursive=True):
    """Get transitively included packages of this package.

    Args:
      recursive: Whether to recursively traverse includes.

    Returns:
      List of PackageConfiguration instances.

    Raises:
      ProjectConfigurationError: If any referenced packages are not present
        in the project.
    """
    packages_by_name = self._project.packages_by_name
    included_packages_by_name = {}
    missing_package_names = []
    for include_package_name in safe_dict_get_value(self._json, "includes",
                                                    default_value=[]):
      package = packages_by_name.get(include_package_name)
      if package:
        included_packages_by_name[package.name] = package
        if recursive:
          included_packages_by_name.update(
              dict([(pkg.name, pkg) for pkg in package.includes]))
      else:
        missing_package_names.append(include_package_name)
    if missing_package_names:
      raise ProjectConfigurationError(
          "%s includes missing packages %s" % (
              self.name, missing_package_names))
    return list(included_packages_by_name.values())

  @property
  def includes(self):
    """Get transitively included packages of this package.

    Returns:
      List of PackageConfiguration instances.

    Raises:
      ProjectConfigurationError: If any referenced packages are not present
        in the project.
    """
    return self._get_includes()

  def check_circular_references_in_includes(self, parents):
    """Searches for circular references in includes.

    Args:
      parents: List of parents packages including this package.

    Raises:
      ProjectConfigurationError: If a circular reference is detected.
    """
    parent_names = [parent.name for parent in parents]
    if self.name in parent_names:
      raise ProjectConfigurationError(
          ("Circular package inclusion detected when checking %s which is "
           "included by [%s]") % (self.name, " --> ".join(parent_names)))
    for include in self._get_includes(recursive=False):
      parents.append(self)
      include.check_circular_references_in_includes(parents)
      del parents[-1]

  @property
  def export(self):
    """Get whether this package should be exported.

    Returns:
      True if the package should be exported to `name`, False otherwise.
    """
    return safe_dict_get_value(self._json, "export", default_value=1) == 1

  @property
  def exclude_paths(self):
    """Get the set of paths that should be excluded from this package.

    Returns:
      Set of regular expressions that match paths which should be excluded from
      this package.
    """
    return [re.compile(path)
            for path in safe_dict_get_value(self._json, "exclude_paths",
                                            default_value=[])]

  def find_assets(self, assets_dirs, check_for_duplicates=True, for_upm=False):
    """Find all assets referenced by this package.

    Args:
      assets_dirs: List of paths to directories to search for files referenced
        by this package.
      check_for_duplicates: Whether to raise an exception when duplicate assets
        are found with different import settings.
      for_upm: Whether this is for packaging for UPM package.

    Returns:
      List of Asset instances for all files imported by this package.

    Raises:
      ProjectConfigurationError: If more than one file has been included with
        different import settings.
    """
    # Dictionary of asset lists indexed by package name.
    assets_by_package_name = collections.defaultdict(list)
    for asset_config in self.imports:
      if asset_config.get_enabled(self._project.selected_sections):
        assets_by_package_name[self.name].extend(
            asset_config.find_assets(assets_dirs, for_upm))

    # Only add asset from "includes" package if this is not for UPM package.
    if not for_upm:
      for package in self.includes:
        logging.debug("%s including assets from %s", self.name, package.name)
        assets_by_package_name[package.name].extend(
            package.find_assets(assets_dirs, check_for_duplicates=False))

    package_and_assets_by_filename = collections.defaultdict(list)
    for package_name, assets in assets_by_package_name.items():
      for asset in assets:
        package_and_assets_by_filename[asset.filename].append(
            (package_name, asset))

    # Check for duplicate assets with different import settings.
    duplicate_assets_errors = []
    for filename, package_and_assets in package_and_assets_by_filename.items():
      if len(package_and_assets) <= 1:
        continue
      # Look for assets with different import settings for this file.
      differing_metadata_packages = set()
      previous_package_name = None
      previous_asset = None
      for package_and_asset in package_and_assets:
        package_name, asset = package_and_asset
        if previous_package_name and (previous_asset.importer_metadata !=
                                      asset.importer_metadata):
          differing_metadata_packages.add(previous_package_name)
          differing_metadata_packages.add(package_name)
        previous_package_name = package_name
        previous_asset = asset

      if differing_metadata_packages:
        duplicate_assets_errors.append(
            ("File %s imported with different import settings in "
             "packages %s") % (filename, sorted(differing_metadata_packages)))
    if check_for_duplicates and duplicate_assets_errors:
      raise ProjectConfigurationError("\n".join(duplicate_assets_errors))

    # Deduplicate the list of assets that were found.
    found_assets = Asset.sorted_by_filename(
        [package_and_assets[0][1] for package_and_assets in (
            package_and_assets_by_filename.values())])

    # Filter out excluded assets.
    exclude_paths = self.exclude_paths
    if exclude_paths:
      assets_with_exclusions_removed = []
      for asset in found_assets:
        include = True
        for exclude_path in exclude_paths:
          if exclude_path.match(asset.filename):
            include = False
            break
        if include:
          assets_with_exclusions_removed.append(asset)
      found_assets = assets_with_exclusions_removed

    logging.debug("Found assets for package %s: %s", self.name,
                  [asset.filename for asset in found_assets])
    return found_assets

  @property
  def manifest_path(self):
    """Get the manifest path of the package in the exported archive.

    Returns:
      Path of the directory that will contain the manifest in the exported
      archive or None if no manifest should be created.
    """
    return safe_dict_get_value(self._json, "manifest_path",
                               value_classes=STR_OR_UNICODE)

  @property
  def package_name(self):
    """Get the name of this package configuration.

    Returns:
      String as the name without extension.
    """

    return os.path.splitext(self.name)[0]

  @property
  def manifest_filename(self):
    """Get the filename of the manifest that will be generated by this package.

    Returns:
      Filename of the manifest in the exported archive or None if no manifest
      should be created.
    """
    path = self.manifest_path
    version = self._project.version
    if path and version:
      return version_handler_filename(
          os.path.join(path, self.package_name + ".txt"),
          [[VERSION_HANDLER_VERSION_FIELD_PREFIX, version],
           [VERSION_HANDLER_MANIFEST_FIELD_PREFIX, None]])
    return None

  def get_manifest_metadata(self,
                            manifest_type=VERSION_HANDLER_MANIFEST_TYPE_LEGACY):
    """Get the importer metadata for the manifest generated by this package.

    Args:
      manifest_type: Type of the manifest, ex. legacy (.txt) or upm
        (package.json)

    Returns:
      OrderedDict of importer metadata if a manifest is generated by this
      package, None otherwise.
    """

    if not self._project.version:
      return None

    if (manifest_type == VERSION_HANDLER_MANIFEST_TYPE_LEGACY and
        not self.manifest_filename):
      return None
    metadata = copy.deepcopy(DEFAULT_METADATA_TEMPLATE)
    labels = self.labels
    if manifest_type == VERSION_HANDLER_MANIFEST_TYPE_LEGACY:
      labels.add(
          version_handler_tag(field=VERSION_HANDLER_MANIFEST_FIELD_PREFIX))

      # Add gvhp_manifestname-0DisplayName
      priority = 0
      if self.common_package_display_name:
        labels.add(VERSION_HANDLER_PRESERVE_LABEL_PREFIX +
                   VERSION_HANDLER_FIELD_SEPARATOR +
                   VERSION_HANDLER_PRESERVE_MANIFEST_NAME_FIELD_PREFIX +
                   str(priority) + self.common_package_display_name)
        priority += 1
      if self.package_name != self.common_package_display_name:
        labels.add(VERSION_HANDLER_PRESERVE_LABEL_PREFIX +
                   VERSION_HANDLER_FIELD_SEPARATOR +
                   VERSION_HANDLER_PRESERVE_MANIFEST_NAME_FIELD_PREFIX +
                   str(priority) + self.package_name)

    elif manifest_type == VERSION_HANDLER_MANIFEST_TYPE_UPM:
      # gupmr_manifest
      labels.add(UPM_RESOLVER_LABEL_PREFIX + UPM_RESOLVER_FIELD_SEPARATOR +
                 UPM_RESOLVER_MANIFEST_FIELD_PREFIX)
    Asset.add_labels_to_metadata(metadata, labels)

    return metadata

  def write_manifest(self, output_dir, assets):
    """Write the manifest for this package to the specified directory.

    Args:
      output_dir: Directory to write the manifest into.
      assets: Assets to write to the manifest, typically returned by
        find_assets().

    Returns:
      Asset instance that references the generated manifest.
    """
    manifest_filename = self.manifest_filename
    if not manifest_filename:
      return None
    manifest_absolute_path = os.path.join(output_dir, manifest_filename)
    manifest_directory = os.path.dirname(manifest_absolute_path)
    if not os.path.exists(manifest_directory):
      os.makedirs(manifest_directory)
    with open(manifest_absolute_path, "wt", encoding='utf-8') as manifest_file:
      manifest_file.write(
          "%s\n" % "\n".join([posix_path(os.path.join(ASSETS_DIRECTORY,
                                                      asset.filename))
                              for asset in Asset.sorted_by_filename(assets)]))
    # Retrieve a template manifest asset if it exists.
    manifest_asset = [asset for asset in assets
                      if asset.filename == manifest_filename]
    return manifest_asset[0] if manifest_asset else (
      Asset(manifest_filename, manifest_absolute_path,
            self.get_manifest_metadata(VERSION_HANDLER_MANIFEST_TYPE_LEGACY)))

  def write_upm_manifest(self, output_dir):
    """Write UPM manifest for this package to the specified directory.

    Args:
      output_dir: Directory to write the manifest into.

    Returns:
      Asset instance that references the generated manifest.

    Raises:
      ProjectConfigurationError: If this package or its dependencies does not
        have package name under common_manifest.
    """

    manifest_filename = "package.json"
    manifest_absolute_path = os.path.join(output_dir, manifest_filename)
    manifest_directory = os.path.dirname(manifest_absolute_path)
    if not os.path.exists(manifest_directory):
      os.makedirs(manifest_directory)

    # Compose package.json
    package_manifest = {}
    package_manifest["name"] = self.common_package_name
    if not package_manifest["name"]:
      raise ProjectConfigurationError(
          "Detected package %s has missing package name under common_manifest" %
          (self.name))
    package_manifest["version"] = self.version

    common_manifest = self.common_manifest
    # Add manifest info from common_manifest to package.json
    if common_manifest:
      for common_manifest_key, upm_manifest_key in (("display_name",
                                                     "displayName"),
                                                    ("keywords", "keywords"),
                                                    ("author", "author")):
        common_manifest_value = safe_dict_get_value(common_manifest,
                                                    common_manifest_key)
        safe_dict_set_value(package_manifest, upm_manifest_key,
                            common_manifest_value)

    # Add description.
    safe_dict_set_value(package_manifest, "description",
                        self.common_package_description)

    # Add additonal keywords to link back to legacy manifest.
    if self.export and self.manifest_path:
      keywords = safe_dict_get_value(package_manifest, "keywords",
                                     default_value=[])
      keywords.append(UPM_KEYWORDS_MANIFEST_PREFIX + self.package_name)
      if self.common_package_display_name != self.package_name:
        keywords.append(UPM_KEYWORDS_MANIFEST_PREFIX +
                        self.common_package_display_name)
      package_manifest["keywords"] = keywords

    # Add minimum Unity version
    if self.upm_manifest:
      safe_dict_set_value(package_manifest, "unity",
                          safe_dict_get_value(self.upm_manifest, "unity"))
      dependencies = safe_dict_get_value(
          self.upm_manifest, "dependencies", default_value={})
    else:
      dependencies = {}

    # Add additional dependencies from "includes"
    missing_deps = []
    include_packages = [
        pkg for pkg in self._get_includes(recursive=False) if pkg.export_upm
    ]
    for include in include_packages:
      if include.common_package_name:
        dependencies[include.common_package_name] = include.version
      else:
        missing_deps.append(include.name)
    if missing_deps:
      raise ProjectConfigurationError(
          ("Detected multiple dependencies by %s has missing package name" +
           "\n%s") % (self.name, "\n".join(missing_deps)))
    package_manifest["dependencies"] = dependencies

    with open(manifest_absolute_path, "wt", encoding='utf-8') as manifest_file:
      json.dump(package_manifest, manifest_file, indent=2)

    return Asset(
        manifest_filename,
        manifest_absolute_path,
        self.get_manifest_metadata(VERSION_HANDLER_MANIFEST_TYPE_UPM),
        filename_guid_lookup=os.path.join(self.common_package_name,
                                          manifest_filename))

  @property
  def imports(self):
    """Get the AssetConfiguration instances from this package.

    Returns:
      List of AssetConfiguration instances.
    """
    return [AssetConfiguration(self, import_json)
            for import_json in safe_dict_get_value(self._json, "imports",
                                                   default_value=[])]

  @property
  def labels(self):
    """Get the set of labels that should be added to assets in this package.

    Returns:
      Set of asset label strings to apply to assets in the package.
    """
    label_set = set()
    version = self._project.version
    if version:
      label_set = label_set.union(
          [version_handler_tag(),
           version_handler_tag(field=VERSION_HANDLER_VERSION_FIELD_PREFIX,
                               value=version)])
    return label_set

  @staticmethod
  def create_archive(archive_filename, input_directory, timestamp):
    """Create a .unitypackage archive from a directory.

    Args:
      archive_filename: Name of the archive file to create.
      input_directory: Directory to archive.
      timestamp: Timestamp to apply to the archive and all files in the archive
        or -1 to use the current time.
    """
    archive_filename = os.path.realpath(archive_filename)
    cwd = os.getcwd()
    try:
      os.chdir(input_directory)
      # Create a deterministically ordered set of filesystem entries.
      input_filenames = []
      for current_dir, directories, filenames in os.walk(os.path.curdir):
        for directory in directories:
          input_filenames.append(os.path.join(current_dir, directory))
        for filename in filenames:
          input_filenames.append(os.path.join(current_dir, filename))
      input_filenames = sorted(
          [os.path.normpath(filename) for filename in input_filenames])

      archive_dir = os.path.dirname(archive_filename)
      if not os.path.exists(archive_dir):
        os.makedirs(archive_dir)

      # Create a tar.gz archive.
      tar_available = (platform.system() == "Linux" or
                       platform.system() == "Darwin")
      gnu_tar_available = platform.system() == "Linux"
      # Whether a reproducible tar.gz is required.
      if tar_available and FLAGS.use_tar:
        # tarfile is 10x slower than the tar command so use the command line
        # tool where it's available and can generate a reproducible archive.
        list_filename = os.path.join(tempfile.mkdtemp(), "input_files.txt")
        try:
          # Create a list of input files to workaround command line length
          # limits.
          with open(list_filename, "wt", encoding='utf-8') as list_file:
            list_file.write("%s\n" % "\n".join(input_filenames))

          tar_args = ["tar"]
          tar_args.extend(["-c", "-z", "-f", archive_filename])
          if gnu_tar_available:
            if FLAGS.timestamp:
              tar_args.append("--mtime=@%d" % FLAGS.timestamp)
            # Hard code the user and group of files in the tar file so that
            # the process is reproducible.
            tar_args.extend(["--owner=%s" % FLAGS.owner,
                             "--group=%s" % FLAGS.group])
            tar_args.append("--no-recursion")
          else: # Assume BSD tar.
            # Set the modification time of each file since BSD tar doesn't have
            # an option to override this.
            if FLAGS.timestamp:
              for filename in input_filenames:
                os.utime(filename, (FLAGS.timestamp, FLAGS.timestamp))
            # Don't recurse directories.
            tar_args.append("-n")
            # Avoid creating mac metadata files with name started with "."
            if platform.system() == "Darwin":
              tar_args.append("--no-mac-metadata")
          tar_args.extend(["-T", list_filename])
          # Disable timestamp in the gzip header.
          tar_env = os.environ.copy()
          tar_env["GZIP"] = "-n"
          subprocess.check_call(tar_args, cwd=input_directory, env=tar_env)
        finally:
          shutil.rmtree(os.path.dirname(list_filename))
      else:
        with open(archive_filename, "wb") as gzipped_tar_file:
          with gzip.GzipFile(
              # The filename in the archive must end with .tar or .tar.gz for
              # Unity to open it on Windows.
              os.path.splitext(archive_filename)[0] + ".tar.gz",
              fileobj=gzipped_tar_file, mode="wb",
              mtime=(timestamp if timestamp >= 0 else None)) as gzip_file:
            with tarfile.open(mode="w|", fileobj=gzip_file,
                              format=tarfile.USTAR_FORMAT, dereference=True,
                              errorlevel=2) as tar_file:
              def reproducible_tarinfo(tarinfo):
                """Patch TarInfo so that it generates a reproducible archive.

                Args:
                  tarinfo: TarInfo to modify.

                Returns:
                  Modified tarinfo.
                """
                tarinfo.mtime = timestamp if timestamp >= 0 else tarinfo.mtime
                tarinfo.uid = 0
                tarinfo.gid = 0
                tarinfo.uname = FLAGS.owner
                tarinfo.gname = FLAGS.group
                return tarinfo

              for filename in input_filenames:
                tar_file.add(filename, recursive=False,
                             filter=reproducible_tarinfo)

    finally:
      os.chdir(cwd)

  def write(self, guid_database, assets_dirs, output_dir, timestamp,
            package_filename=None):
    """Creates a .unitypackage file from a package dictionary.

    Creates a Unity package given the import directory and the package
    dictionary containing the groups of imports.

    Args:
      guid_database: GuidDatabase instance which contains GUIDs for each
        exported asset.
      assets_dirs: List of paths to directories to search for files referenced
        by this package.
      output_dir: Directory where to write the exported .unitypackage.
      timestamp: Timestamp to apply to all packaged assets in the archive.
        If this value is less than 0, the creation time of each input file is
        used instead.
      package_filename: Filename to write package to in the output_dir.

    Returns:
      Path to the created .unitypackage file.

    Raises:
      MissingGuidsError: If GUIDs are missing for input files.
      DuplicateGuidsError: If any duplicate GUIDs are present.
      ProjectConfigurationError: If files are imported multiple times with
        different metadata.
    """
    package_filename = package_filename or self.name
    unity_package_file = os.path.join(output_dir, package_filename)

    # Create a directory to stage the source assets and assemble the package.
    temporary_dir = tempfile.mkdtemp()
    try:
      generated_assets_dir = os.path.join(temporary_dir, "generated_assets")
      os.makedirs(generated_assets_dir)
      staging_dir = os.path.join(temporary_dir, "plugin")
      os.makedirs(staging_dir)

      logging.info("Packaging %s to %s...", self.name, unity_package_file)

      assets = self.find_assets(assets_dirs)
      # If a manifest is enabled, write it into the assets directory.
      manifest_asset = self.write_manifest(generated_assets_dir, assets)
      if manifest_asset:
        assets.append(manifest_asset)

      # Add manifest files for the include packages
      for include_package in self.includes:
        include_manifest_asset = include_package.write_manifest(
            generated_assets_dir, include_package.find_assets(assets_dirs))
        if include_manifest_asset:
          assets.append(include_manifest_asset)

      # Populate the GUID database and check for any duplicates.
      guid_database.read_guids_from_assets(assets)

      # Process all assets and stage all files for packaging in the staging
      # area.
      for asset in Asset.sorted_by_filename(assets):
        asset_file = asset.write(
            staging_dir, guid_database.get_guid(asset.filename_guid_lookup),
            timestamp)
        logging.info("- Processed %s --> %s", asset.filename, asset_file)

      # Create the .unitypackage file.
      PackageConfiguration.create_archive(unity_package_file, staging_dir,
                                          timestamp)
      logging.info("Created %s for %s", unity_package_file, self.name)
    finally:
      shutil.rmtree(temporary_dir)

    return unity_package_file

  def write_upm(self,
                guid_database,
                assets_dirs,
                output_dir,
                timestamp,
                package_filename=None):
    """Creates a .tgz file from a package dictionary.

    Creates a UPM package given the import directory and the package
    dictionary containing the groups of imports.

    Args:
      guid_database: GuidDatabase instance which contains GUIDs for each
        exported asset.
      assets_dirs: List of paths to directories to search for files referenced
        by this package.
      output_dir: Directory where to write the exported .tgz.
      timestamp: Timestamp to apply to all packaged assets in the archive. If
        this value is less than 0, the creation time of each input file is used
        instead.
      package_filename: Filename to write package to in the output_dir.

    Returns:
      Path to the created .tgz file.

    Raises:
      MissingGuidsError: If GUIDs are missing for input files.
      DuplicateGuidsError: If any duplicate GUIDs are present.
      ProjectConfigurationError: If files are imported multiple times with
        different metadata.
    """
    package_filename = package_filename or self.tarball_name
    unity_package_file = os.path.join(output_dir, package_filename)

    # Create a directory to stage the source assets and assemble the package.
    temporary_dir = tempfile.mkdtemp()
    try:
      generated_assets_dir = os.path.join(temporary_dir, "generated_assets")
      os.makedirs(generated_assets_dir)
      staging_dir = os.path.join(temporary_dir, "plugin")
      os.makedirs(staging_dir)

      logging.info("Packaging %s to %s...", self.name, unity_package_file)

      assets = self.find_assets(assets_dirs, for_upm=True)

      # Create package.json
      manifest_asset = self.write_upm_manifest(generated_assets_dir)
      if manifest_asset:
        assets.append(manifest_asset)

      manifest_asset = self.write_manifest(generated_assets_dir, assets)
      if manifest_asset:
        assets.append(manifest_asset)

      # Move README.md, CHANGELOG.md and LICENSE.md to root folder.
      for config_name, to_location in (("readme", "README.md"),
                                       ("changelog", "CHANGELOG.md"),
                                       ("license", "LICENSE.md")):
        from_location = safe_dict_get_value(self._json, config_name)
        if from_location:
          abs_from_location = find_in_dirs(from_location, assets_dirs)
          if abs_from_location:
            # Create default metadata
            metadata = copy.deepcopy(DEFAULT_METADATA_TEMPLATE)
            labels = self.labels
            Asset.add_labels_to_metadata(metadata, labels)

            assets.append(Asset(
                to_location,
                abs_from_location,
                metadata,
                filename_guid_lookup=os.path.join(self.common_package_name,
                                                  to_location),
                is_folder=False))
          else:
            raise ProjectConfigurationError(
                "Cannot find '%s' at '%s' for package '%s'. Perhaps it "
                "is not included in assets_dir or assets_zip?" % (
                    config_name, from_location, self.name))

      # Add all folder assets to generate .meta
      folders = set()
      for asset in assets:
        filepath = os.path.os.path.dirname(asset.filename)
        while filepath:
          folders.add(filepath)
          filepath = os.path.os.path.dirname(filepath)

      for folder in folders:
        # Same folder from each package needs to have an unique GUID. Therefore,
        # filename_guid_lookup is set to "package-name/path/to/folder"
        assets.append(
            Asset(
                folder,
                folder,
                DEFAULT_METADATA_TEMPLATE,
                filename_guid_lookup=os.path.join(self.common_package_name,
                                                  folder),
                is_folder=True))

      # Populate the GUID database and check for any duplicates.
      guid_database.read_guids_from_assets(assets)

      # Process all assets and stage all files for packaging in the staging
      # area.
      for asset in Asset.sorted_by_filename(assets):
        asset_file = asset.write_upm(
            staging_dir, guid_database.get_guid(asset.filename_guid_lookup),
            timestamp)
        logging.info("- Processed %s --> %s", asset.filename, asset_file)

      # Copy documents to "Documentation~" folder.
      # See https://docs.unity3d.com/Manual/cus-layout.html
      # All documentation folders and files must not include a .meta file
      # otherwise the documentation links in the Unity Package Manager window
      # will not work
      source_doc = safe_dict_get_value(self._json, "documentation")
      if source_doc:
        # Try to find the source doc from assets directory
        source_doc = find_in_dirs(source_doc, assets_dirs)
        if os.path.isfile(source_doc):
          # Copy file
          target_doc = os.path.join(staging_dir, "package",
                                    UPM_DOCUMENTATION_DIRECTORY,
                                    UPM_DOCUMENTATION_FILENAME)
          logging.info("- Copying doc file %s --> %s", source_doc, target_doc)
          copy_and_set_rwx(source_doc, target_doc)
        elif os.path.isdir(source_doc):
          target_doc_dir = os.path.join(staging_dir, "package",
                                        UPM_DOCUMENTATION_DIRECTORY)
          # Check if index.md exists
          if not os.path.exists(os.path.join(source_doc,
                                             UPM_DOCUMENTATION_FILENAME)):
            raise ProjectConfigurationError(
                "Cannot find index.md under '%s' for package '%s'. Perhaps it "
                "is not included in assets_dir or assets_zip?" % (
                    source_doc, self.name))

          logging.info("- Copying doc folder %s --> %s",
                       source_doc, target_doc_dir)
          copy_and_set_rwx(source_doc, target_doc_dir)
        else:
          raise ProjectConfigurationError(
              "Cannot find documentation at '%s' for package '%s'. Perhaps the "
              "file/folder is not included in assets_dir or assets_zip?" % (
                  from_location, self.name))

      # Create the .tgz file.
      PackageConfiguration.create_archive(unity_package_file, staging_dir,
                                          timestamp)
      logging.info("Created %s for %s", unity_package_file, self.name)
    finally:
      shutil.rmtree(temporary_dir)

    return unity_package_file


class BuildConfiguration(ConfigurationBlock):
  """Build configuration used to modify enabled sections and export settings.

  Attributes:
    _json: JSON dictionary this configuration is read from.
    _replacements: List of (regular_expression, replacement_string) tuples where
      regular_expression is a RegexObject instance and replacement_string is
      a string that can be passed to re.sub() as part of a replacement.
  """

  def __init__(self, build_json):
    """Parse a build configuration.

    Args:
      build_json: Dictionary with the configuration to parse.

    Raises:
      ProjectConfigurationError: If a package name replacement regular
        expression fails to compile.
    """
    super(BuildConfiguration, self).__init__(build_json)
    self._json = build_json
    self._replacements = []
    for match_replacement in (safe_dict_get_value(
        self._json, "package_name_replacements", default_value=[])):
      match_regexp_string = safe_dict_get_value(match_replacement, "match",
                                                value_classes=STR_OR_UNICODE)
      replacement_string = safe_dict_get_value(match_replacement, "replacement",
                                               value_classes=STR_OR_UNICODE)
      if match_regexp_string and replacement_string is not None:
        try:
          self._replacements.append((re.compile(match_regexp_string),
                                     replacement_string))
        except re.error as error:
          raise ProjectConfigurationError(
              "Failed to compile package name replacement regular expression "
              "'%s' for build config %s (%s)" % (match_regexp_string, self.name,
                                                 str(error)))

  @property
  def name(self):
    """Get the name of the build configuration.

    Returns:
      Name of build configuration for logging.
    """
    return safe_dict_get_value(self._json, "name", default_value="<unnamed>")

  @property
  def enabled_sections(self):
    """Get the set of sections that should be enabled when exporting.

    Returns:
      Set of export section strings that should be enabled when exporting with
      this build configuration.
    """
    return set(safe_dict_get_value(self._json, "enabled_sections",
                                   default_value=[]))

  @property
  def package_name_replacements(self):
    """Get the set of replacements to apply to package names.

    Returns:
      List of (regular_expression, replacement_string) tuples where
      regular_expression is a RegexObject instance and replacement_string is
      a string that can be passed to re.sub() as part of a replacement.
    """
    return list(self._replacements)

  def apply_package_name_replacements(self, package_name):
    """Apply package name replacements to the specified string.

    Args:
      package_name: String modified by package name replacements.

    Returns:
      Package name modified by the replacements returned by
      the package_name_replacements property.

    Raises:
      ProjectConfigurationError: If a package name replacement fails.
    """
    new_package_name = package_name
    for match_regexp, replacement_string in self._replacements:
      try:
        new_package_name = match_regexp.sub(replacement_string,
                                            new_package_name)
      except re.error as error:
        raise ProjectConfigurationError(
            "Failed to apply package name replacement '%s' --> '%s' from build "
            "config %s to package name %s (%s)" % (
                match_regexp.pattern, replacement_string, self.name,
                package_name, str(error)))
    return new_package_name

  def create_package_name_map(self, package_names_map):
    """Create a dictionary which maps existing to replaced package names.

    Args:
      package_names_map: Map of package name to filename to replace to generate
        a dictionary from.

    Returns:
      Dictionary of replacement package names keyed by the original package
      name.

    Raises:
      ProjectConfigurationError: If multiple package names are mapped to the
        same target package name or a package name replacement fails.
    """
    new_by_old_package_names = collections.defaultdict(list)
    for package_name, filename_to_replace in package_names_map.items():
      new_by_old_package_names[package_name].append(
          self.apply_package_name_replacements(filename_to_replace))

    duplicate_new_names_strings = []
    for old_name, new_names in new_by_old_package_names.items():
      if len(new_names) > 1:
        duplicate_new_names_strings.append("%s --> %s" % (old_name, new_names))
    if duplicate_new_names_strings:
      raise ProjectConfigurationError(
          "Multiple packages map in build config %s (sections %s) map to the "
          "same export name %s" % (self.name, self.enabled_sections,
                                   ", ".join(duplicate_new_names_strings)))
    return dict([(old, new[0])
                 for old, new in new_by_old_package_names.items()])


class ProjectConfiguration(object):
  """Reads a project configuration JSON file.

  Attributes:
    _packages: All packages parsed from the configuration.
    _json: JSON dictionary this configuration is read from.
    _packages_by_name: Dictionary of enabled PackageConfiguration instances
      indexed by name.
    _selected_sections: Export sections used to enable this project.
    _version: Version of the project or None if no version was specified on
      initialization.
    _all_builds: All available build configuration instances.
    _builds: Set of builds filtered by enabled export sections.
  """

  def __init__(self, export_configuration_dict, selected_sections, version):
    """Parse an project configuration string.

    Args:
      export_configuration_dict: Dictionary with the configuration to parse.
      selected_sections: Set of enabled export section strings. This is used to
        filter the loaded package configurations.
        See PackageConfiguration.get_enabled().
      version: Version number of this project. Can be None if this is
        not versioned.

    Raises:
      ProjectConfigurationError: If any project data contains errors.
    """
    self._json = export_configuration_dict
    self._packages = [PackageConfiguration(self, package_json)
                      for package_json in safe_dict_get_value(
                          self._json, "packages", default_value=[])]
    self._packages_by_name = collections.OrderedDict()
    self._version = version
    if FLAGS.enforce_semver:
      if version is None:
        raise ProjectConfigurationError(
            "Version number is required to export to Unity Package Manager " +
            "packages.")
      elif not VALID_VERSION_RE.match(version):
        raise ProjectConfigurationError(
            "Invalid version '%s'.  Should be 'major.minor.patch(-preview)' in "
            "numbers" % version)

    self._all_builds = [BuildConfiguration(build_json)
                        for build_json in safe_dict_get_value(
                            self._json, "builds", default_value=[{}])]
    self._builds = []
    self._selected_sections = None

    # pylint: disable=g-missing-from-attributes
    self.selected_sections = selected_sections

  @property
  def packages_by_name(self):
    """Get the list of packages from the configuration indexed by name.

    Returns:
      Dictionary of PackageConfiguration instances indexed by name.
    """
    return collections.OrderedDict(self._packages_by_name)

  @property
  def packages(self):
    """Get the list of packages from the configuration.

    Returns:
      List of PackageConfiguration instances.
    """
    return list(self._packages_by_name.values())

  @property
  def version(self):
    """Get the version of this project.

    Returns:
      Version string or None if no version was specified on initialization.
    """
    return self._version

  @property
  def selected_sections(self):
    """Get the sections used to initialize this instance.

    Returns:
      Set of currently selected section strings enabled for this project.
    """
    return set(self._selected_sections)

  @selected_sections.setter
  def selected_sections(self, sections):
    """Filter the set of packages and build configurations by export sections.

    This method changes the set of selected packages returned by the
    packages and packages_by_name properties and build configurations by the
    builds property.

    Args:
      sections: Set of enabled export section strings. This is used to
        filter the loaded package and build configurations.

    Raises:
      ProjectConfigurationError: If any project data contains errors.
    """
    # If the set of selected sections hasn't changed, do nothing.
    sections = set(sections)
    if sections == self._selected_sections:
      return

    # Filter by enabled packages and bucket by name.
    package_list_by_name = collections.OrderedDict()
    for package in self._packages:
      if package.get_enabled(sections):
        packages_list = package_list_by_name.get(package.name, [])
        packages_list.append(package)
        package_list_by_name[package.name] = packages_list
      else:
        logging.debug("Package %s not enabled for sections %s (supports %s)",
                      package.name, sections, package.sections)

    # Find any duplicate packages.
    duplicate_package_names = (
        [name for name, pkgs in package_list_by_name.items() if len(pkgs) > 1])
    if duplicate_package_names:
      raise ProjectConfigurationError(
          ("Package(s) %s configured to export to the same path with "
           "enabled sections %s") % (sorted(duplicate_package_names),
                                     sorted(sections)))

    previous_selected_sections = self._selected_sections
    previous_packages_by_name = self._packages_by_name
    try:
      self._selected_sections = sections
      self._packages_by_name = collections.OrderedDict([
          (name, pkgs[0]) for name, pkgs in package_list_by_name.items()])

      # Check for circular references in includes.
      for package in self.packages:
        package.check_circular_references_in_includes([])
    except ProjectConfigurationError as error:
      self._selected_sections = previous_selected_sections
      self._packages_by_name = previous_packages_by_name
      raise error

    # Create a filtered set of build configuration instances.
    self._builds = []
    for build in self._all_builds:
      if build.get_enabled(sections):
        self._builds.append(build)
      else:
        logging.debug("Build %s not enabled for sections %s (supports %s)",
                      build.name, sections, build.sections)

  @property
  def builds(self):
    """Get selected build configurations for this project.

    Returns:
      List of BuildConfiguration instances associated with this project.
    """
    return list(self._builds)

  def write(self,
            guid_database,
            assets_dirs,
            output_dir,
            timestamp,
            for_upm=False):
    """Export all enabled packages using the project build configs.

    Args:
      guid_database: GuidDatabase instance which contains GUIDs for each
        exported asset.
      assets_dirs: List of paths to directories containing assets to import.
        This is combined with the path of each asset referenced by the project.
      output_dir: Directory where to write the exported .unitypackage.
      timestamp: Timestamp to apply to all packaged assets in each archive. If
        this value is less than 0, the creation time of each input file is used
        instead.
      for_upm: Whether write for Unity Package Manager package.

    Returns:
      Dictionary of BuildConfiguration instances indexed by the exported
      package filename generated by the build config.

    Raises:
      ProjectConfigurationError: If an error occurs while exporting the project.
      MissingGuidsError: If any asset GUIDs are missing.
    """
    selected_sections = self.selected_sections
    build_by_package_filename = {}

    try:
      build_sections_and_package_name_maps = []
      build_indices_by_package_filename = collections.defaultdict(list)
      # Generate package name maps for each build configuration.
      builds = self.builds
      for build_index, build in enumerate(builds):
        # Apply the build config.
        build_sections = selected_sections.union(build.enabled_sections)
        self.selected_sections = build_sections

        # Create the package filename mapping.
        packages_by_name = self.packages_by_name
        package_name_map = None
        if for_upm:
          package_name_to_output_filename = {
              pkg.name: pkg.tarball_name
              for pkg in packages_by_name.values()
              if pkg.export_upm
          }
        else:
          package_name_to_output_filename = {
              pkg.name: pkg.name
              for pkg in packages_by_name.values()
              if pkg.export
          }
        package_name_map = build.create_package_name_map(
            package_name_to_output_filename)

        # Store the build configuration.
        build_sections_and_package_name_maps.append(
            (build, build_sections, package_name_map))

        # Map each filename to a set of build indices so it's possible to
        # check for duplicates later.
        for filename in package_name_map.values():
          build_indices_by_package_filename[filename].append(build_index)

      # Check for multiple build configurations exporting to the same filenames.
      duplicate_filename_errors = []
      for filename, build_indices in build_indices_by_package_filename.items():
        if len(build_indices) > 1:
          duplicate_filename_errors.append(
              "%s exported by multiple builds %s" % (
                  filename,
                  str([builds[index].name for index in build_indices])))
      if duplicate_filename_errors:
        raise ProjectConfigurationError(
            ("Detected multiple builds exporting packages to the same "
             "file(s).\n"
             "%s") % "\n".join(duplicate_filename_errors))

      missing_guid_paths = []
      for build, build_sections, package_name_map in (
          build_sections_and_package_name_maps):
        # Apply the build config.
        logging.info("Building %s using sections %s", build.name,
                     str(build_sections))
        self.selected_sections = build_sections
        packages_by_name = self.packages_by_name

        # Export all packages.
        for package_name, package_filename in package_name_map.items():
          package = packages_by_name[package_name]
          try:
            if for_upm:
              filename = package.write_upm(
                  guid_database,
                  assets_dirs,
                  output_dir,
                  timestamp,
                  package_filename=package_filename)
            else:
              filename = package.write(
                  guid_database,
                  assets_dirs,
                  output_dir,
                  timestamp,
                  package_filename=package_filename)
            build_by_package_filename[filename] = build
          except MissingGuidsError as missing_guids_error:
            logging.error("Missing GUIDs while writing %s (%s)",
                          package.name, missing_guids_error.missing_guid_paths)
            missing_guid_paths.extend(missing_guids_error.missing_guid_paths)
          except DuplicateGuidsError as error:
            raise ProjectConfigurationError(
                "Duplicate GUIDs detecting while writing package %s to %s "
                "(%s)" % (package.name, package_filename, str(error)))

      if missing_guid_paths:
        raise MissingGuidsError(missing_guid_paths)
    finally:
      self.selected_sections = selected_sections
    return build_by_package_filename


def read_json_file_into_ordered_dict(json_filename):
  """Load JSON into an OrderedDict.

  By default the JSON parser reads data into a tree of Python dictionaries that
  do not preserve order.  This method reads JSON into a tree of OrderedDict
  instances that preserves order and provides some compatibility with YAML.

  Args:
    json_filename: File to read JSON from.

  Returns:
    OrderedDict read from json_filename.

  Raises:
    IOError: If the file can't be read.
    ValueError: If there is a parse error while reading the file.
  """
  json_dict = None
  with open(json_filename, "rt", encoding='utf-8') as json_file:
    try:
      json_dict = json.loads(json_file.read(),
                             object_pairs_hook=collections.OrderedDict)
    except ValueError as error:
      raise ValueError("Failed to load JSON file %s (%s)" % (json_filename,
                                                             str(error)))
  return json_dict


def create_directory_from_zip(zip_filename):
  """Optionally creates a directory from a zip file.

  Args:
    zip_filename: Zip file to extract.

  Returns:
    Temporary directory containing the contents of the zip file.

  Raises:
    IOError: If an error occurs while extracting the zip file.
  """
  temporary_dir = tempfile.mkdtemp()
  try:
    logging.debug("Unpacking zip file %s to %s...", zip_filename, temporary_dir)
    with zipfile.ZipFile(zip_filename, "r") as zip_file:
      zip_file.extractall(temporary_dir)
    logging.debug("Unpacked zip file %s to %s", zip_filename, temporary_dir)
  except IOError as error:
    shutil.rmtree(temporary_dir)
    raise error
  return temporary_dir


def write_zipfile(zip_filename, source_directory):
  """Write the contents of a directory to a zip file.

  Args:
    zip_filename: Zip filename to create.
    source_directory: Directory to archive.

  Raises:
    IOError: If an error occurs while archiving.
  """
  if os.path.exists(zip_filename):
    os.unlink(zip_filename)

  logging.debug("Archiving directory %s to %s...", source_directory,
                zip_filename)
  with zipfile.ZipFile(zip_filename, "w", allowZip64=True) as zip_file:
    for current_root, _, filenames in os.walk(source_directory):
      for filename in filenames:
        fullpath = os.path.join(current_root, filename)
        zip_file.write(fullpath, os.path.relpath(fullpath, source_directory))
  logging.debug("Archived directory %s to %s", source_directory, zip_filename)


def copy_files_to_dir(colon_separated_input_output_filenames,
                      output_dir):
  """Copy any additional files to the output directory.

  Args:
    colon_separated_input_output_filenames: List of colon separated strings
      that specifies "input_filename:output_filename"  paths.
      input_filename is the path to copy.  output_filename can be optionally
      specified to copy input_filename into a different path under output_dir.
      If output_filename isn't specified, this method uses the input_filename.
    output_dir: Directory to copy the files into.

  Returns:
    List of files copied to the output directory.
  """
  copied_files = []
  for additional_file in colon_separated_input_output_filenames:
    additional_file_args = additional_file.split(":")
    input_filename = posix_path(additional_file_args[0])

    # Get the output filename.
    output_filename = input_filename
    if len(additional_file_args) > 1:
      output_filename = additional_file_args[1]
    output_filename = os.path.splitdrive(output_filename)[1]

    # Remove the drive or root directory from the output filename.
    if os.path.normpath(output_filename).startswith(os.path.sep):
      output_filename = output_filename[len(os.path.sep):]
    output_filename = posix_path(os.path.join(output_dir, output_filename))

    # Copy the file to the output directory.
    copy_and_set_rwx(input_filename, output_filename)
    copied_files.append(posix_path(output_filename))
  return copied_files


def find_in_dirs(filename, directories):
  """Find the file under given directories.

  Args:
    filename: File/Folder name to find.
    directories: List of directories to search for.

  Returns:
    If found, return the combined path with directory and filename.
    Return None, otherwise.
  """
  for directory in directories:
    candidate = os.path.join(directory, filename)
    if os.path.exists(candidate):
      return candidate
  return None


def main(unused_argv):
  """Builds Unity packages from sets of files within an assets folder.

  Args:
    unused_argv: List of arguments passed on the command line after the command.

  Returns:
    The exit code status; 1 for error, 0 for success.
  """
  if not FLAGS.config_file:
    print >> sys.stderr, main.__doc__
    return 1

  enabled_sections = set(FLAGS.enabled_sections or [])
  assets_dirs = list(FLAGS.assets_dir or [])
  temporary_assets_dirs = []
  output_dir = FLAGS.output_dir

  try:
    if FLAGS.output_zip:
      output_dir = tempfile.mkdtemp()
    elif not os.path.exists(output_dir):
      try:
        os.makedirs(output_dir)
      except FileExistsError:
        # This can be racy with other build scripts.
        pass
    elif not os.path.isdir(output_dir):
      logging.error("output_dir %s is not a directory", output_dir)
      return 1

    if FLAGS.output_upm and not FLAGS.enforce_semver:
      logging.error("enforce_semver flag should be True when output_upm flag "
                    "is set to True")
      return 1

    if FLAGS.assets_zip:
      try:
        for asset_zip_file in FLAGS.assets_zip:
          temporary_assets_dirs.append(
              create_directory_from_zip(asset_zip_file))
      except IOError as error:
        logging.error("Failed to extract assets zip file %s (%s)",
                      FLAGS.assets_zip, str(error))
        return 1

    if FLAGS.asset_file:
      asset_files_dir = tempfile.mkdtemp()
      temporary_assets_dirs.append(asset_files_dir)
      try:
        copy_files_to_dir(FLAGS.asset_file, asset_files_dir)
      except IOError as error:
        logging.error("Failed while copying input files (%s)", str(error))
        return 1

    duplicate_guids_checker = DuplicateGuidsChecker()
    guids_json = {}
    if FLAGS.guids_file:
      try:
        guids_json = read_json_file_into_ordered_dict(FLAGS.guids_file)
      except (IOError, ValueError) as error:
        logging.error("Failed to load GUIDs JSON from %s (%s)",
                      FLAGS.guids_file, str(error))
        return 1
    guid_database = GuidDatabase(duplicate_guids_checker, guids_json,
                                 FLAGS.plugins_version)
    try:
      duplicate_guids_checker.check_for_duplicates()
    except DuplicateGuidsError as duplicate_guids_error:
      logging.error(str(duplicate_guids_error))
      return 1

    try:
      project = ProjectConfiguration(
          read_json_file_into_ordered_dict(FLAGS.config_file), enabled_sections,
          FLAGS.plugins_version)
    except (IOError, ValueError, ProjectConfigurationError) as error:
      logging.error("Error while parsing project configuration from %s (%s)",
                    FLAGS.config_file, str(error))
      return 1

    assets_dirs.extend(temporary_assets_dirs)

    if FLAGS.output_unitypackage:
      try:
        project.write(
            guid_database,
            assets_dirs,
            output_dir,
            FLAGS.timestamp,
            for_upm=False)
      except ProjectConfigurationError as error:
        logging.error(str(error))
        return 1

      # Copy any additional files to the output directory.
      try:
        copy_files_to_dir(FLAGS.additional_file or [], output_dir)
      except IOError as error:
        logging.error("Failed while copying additional output files (%s)",
                      str(error))
        return 1

    # Generate tgz packages for Unity Package Manager
    if FLAGS.output_upm:
      try:
        project.write(
            guid_database,
            assets_dirs,
            output_dir,
            FLAGS.timestamp,
            for_upm=True)
      except ProjectConfigurationError as error:
        logging.error(str(error))
        return 1

    # Generate the output zip file if one is requested.
    if FLAGS.output_zip:
      try:
        write_zipfile(FLAGS.output_zip, output_dir)
      except IOError as error:
        logging.error("Failed when writing output zip file %s (%s)",
                      FLAGS.output_zip, str(error))
        return 1

  finally:
    for temporary_dir in temporary_assets_dirs:
      shutil.rmtree(temporary_dir)
    if output_dir != FLAGS.output_dir:
      shutil.rmtree(output_dir)

  return 0

if __name__ == "__main__":
  flags.mark_flag_as_required("config_file")
  app.run(main)

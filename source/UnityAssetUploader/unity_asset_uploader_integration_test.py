#!/usr/bin/python
#
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

from io import StringIO
import os
import sys
import unittest
from unittest.mock import patch

import unity_asset_uploader


unity_username = os.environ.get('UNITY_USERNAME')
unity_password = os.environ.get('UNITY_PASSWORD')
unity_package_id = os.environ.get('UNITY_PACKAGE_ID')
unity_package_path = os.environ. get('UNITY_PACKAGE_PATH')


class TestUploaderMethods(unittest.TestCase):

    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_display_session_id(self, mock_stdout):
        unity_asset_uploader.display_session_id(
            unity_username,
            unity_password,
            None)

        assert 'session_id' in mock_stdout.getvalue()

    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_display_publisher_info(self, mock_stdout):
        unity_asset_uploader.display_publisher_info(
            unity_username,
            unity_password,
            None)

        out = mock_stdout.getvalue().strip()
        assert 'publisher_id' in out
        assert 'publisher_name' in out
        assert 'package_ids' in out

    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_display_listings(self, mock_stdout):
        unity_asset_uploader.display_listings(
            unity_username,
            unity_password,
            None)

        out = mock_stdout.getvalue().strip()
        assert 'package_id' in out
        assert 'package_version_id' in out

    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_upload_package(self, mock_stdout):
        unity_asset_uploader.upload_package(
            unity_username,
            unity_password,
            None,
            unity_package_id,
            unity_package_path)

        out = mock_stdout.getvalue().strip()
        assert "{'status': 'ok'}" in out


if __name__ == '__main__':
    unittest.main()

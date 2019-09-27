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

from http import client, HTTPStatus
from io import StringIO
import os
import sys
import unittest
from unittest.mock import call, patch, MagicMock
from urllib import parse

import unity_asset_uploader


unity_username = 'UNITY_USERNAME'
unity_password = 'UNITY_PASSWORD'


class TestUploaderMethods(unittest.TestCase):

    def setUp(self):
        self.parsed_params = parse.urlencode({
            'user': unity_username,
            'pass': unity_password,
            'unityversion': unity_asset_uploader.DEFAULT_UNITY_VERSION,
            'toolversion': unity_asset_uploader.DEFAULT_TOOLS_VERSION,
        })

        self.expected_login_call = call(
            'GET',
            "/login?{}".format(self.parsed_params),
            headers={'Accept': 'application/json'})

        self.expected_login_and_metadata_calls = [
            call(
                'GET',
                "/login?{}".format(self.parsed_params),
                headers={'Accept': 'application/json'}),
            call('GET',
                 ("/api/asset-store-tools/metadata/0.json?{}" +
                  "&xunitysession=this_session_id").format(
                     self.parsed_params),
                 headers={'Accept': 'application/json'}),
        ]

        self.expected_metadata = {
            'status': 'ok',
            'xunitysession': 'this_session_id',
            'publisher': {
                'id': 'publisher_id',
                'name': 'publisher_name'
            },
            'packages': {
                '123': {
                    'id': 'version_123',
                    'name': 'package_one',
                    'version_name': '1.0.0',
                    'icon_url': 'https://example.com/icon',
                    'preview_url': 'https://example.com/image',
                    'project_path': '/mock_project',
                    'root_guid': '1234567890',
                    'root_path': '/mock_project/mock_path.unityasset',
                    'status': 'draft',
                },
                '456': {
                    'id': 'version_456',
                    'name': 'package_two',
                    'version_name': '1.1.2',
                    'icon_url': 'https://example.com/icon2',
                    'preview_url': 'https://example.com/image2',
                    'project_path': '/mock_project',
                    'root_guid': '0987654321',
                    'root_path': '/mock_project/mock_path2.unityasset',
                    'status': 'published',
                }
            }
        }

    @patch('unity_asset_uploader.json')
    @patch('unity_asset_uploader.client.HTTPSConnection')
    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_display_session_id(self, mock_stdout, mock_connection, mock_json):
        getresponse = mock_connection.return_value.getresponse.return_value
        getresponse.status = HTTPStatus.OK
        mock_json.load.return_value = self.expected_metadata

        unity_asset_uploader.display_session_id(
            unity_username,
            unity_password,
            None)

        mock_connection.return_value.request.assert_called_with(
            'GET', "/login?{}".format(self.parsed_params), headers={
                'Accept': 'application/json'})

        mock_json.load.assert_called_with(
            mock_connection.return_value.getresponse.return_value)
        self.assertIn('this_session_id', mock_stdout.getvalue())

    @patch('unity_asset_uploader.json')
    @patch('unity_asset_uploader.client.HTTPSConnection')
    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_get_metadata(self, mock_stdout, mock_connection, mock_json):
        getresponse = mock_connection.return_value.getresponse.return_value
        getresponse.status = HTTPStatus.OK
        mock_json.load.return_value = self.expected_metadata

        session = unity_asset_uploader.AssetStoreSession(
            username=unity_username,
            password=unity_password)

        metadata = session.get_metadata()

        request = mock_connection.return_value.request
        self.assertEqual(self.expected_login_and_metadata_calls,
                         request.call_args_list)

        mock_json.load.assert_called_with(
            mock_connection.return_value.getresponse.return_value)
        self.assertEqual(self.expected_metadata, metadata)

    @patch('unity_asset_uploader.json')
    @patch('unity_asset_uploader.client.HTTPSConnection')
    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_display_publisher_info(
            self, mock_stdout, mock_connection, mock_json):
        getresponse = mock_connection.return_value.getresponse.return_value
        getresponse.status = HTTPStatus.OK
        mock_json.load.return_value = self.expected_metadata

        unity_asset_uploader.display_publisher_info(
            unity_username,
            unity_password,
            None)

        request = mock_connection.return_value.request
        self.assertEqual(self.expected_login_and_metadata_calls,
                         request.call_args_list)

        mock_json.load.assert_called_with(
            mock_connection.return_value.getresponse.return_value)

        out = mock_stdout.getvalue()
        self.assertIn('publisher_id', out)
        self.assertIn('publisher_name', out)
        self.assertIn('123', out)
        self.assertIn('456', out)

    @patch('unity_asset_uploader.json')
    @patch('unity_asset_uploader.client.HTTPSConnection')
    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_display_listings(self, mock_stdout, mock_connection, mock_json):
        getresponse = mock_connection.return_value.getresponse.return_value
        getresponse.status = HTTPStatus.OK
        mock_json.load.return_value = self.expected_metadata

        unity_asset_uploader.display_listings(
            unity_username,
            unity_password,
            None)

        request = mock_connection.return_value.request
        self.assertEqual(self.expected_login_and_metadata_calls,
                         request.call_args_list)

        mock_json.load.assert_called_with(
            mock_connection.return_value.getresponse.return_value)

        out = mock_stdout.getvalue()
        expected_456 = self.expected_metadata['packages']['456']
        package_two_values = expected_456.keys
        for key in unity_asset_uploader.LISTING_TRAITS:
            self.assertIn("{}={}".format(key, expected_456[key]), out)

    @patch('unity_asset_uploader.open')
    @patch('unity_asset_uploader.json')
    @patch('unity_asset_uploader.client.HTTPSConnection')
    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_upload_package_draft(
            self, mock_stdout, mock_connection, mock_json, mock_open):
        getresponse = mock_connection.return_value.getresponse.return_value
        getresponse.status = HTTPStatus.OK
        mock_json.load.return_value = self.expected_metadata
        mock_file = MagicMock()
        read = mock_open.return_value.__enter__.return_value.read
        read.return_value = 'encoded_package_body'

        unity_asset_uploader.upload_package(
            unity_username,
            unity_password,
            None,
            '123',
            '/mock_project/mock_path.unityasset')

        expected_123 = self.expected_metadata['packages']['123']
        upload_params = parse.urlencode({
            'xunitysession': 'this_session_id',
            'root_guid': expected_123['root_guid'],
            'root_path': expected_123['root_path'],
            'project_path': expected_123['project_path'],
        })

        parsed_params = "{}&{}".format(upload_params, self.parsed_params)

        pat = "/api/asset-store-tools/package/version_123/unitypackage.json?{}"

        expected_call_args = list.copy(self.expected_login_and_metadata_calls)
        expected_call_args.append(call(
            'PUT',
            pat.format(parsed_params),
            'encoded_package_body',
            headers={'Accept': 'application/json'}))

        request = mock_connection.return_value.request
        self.assertEqual(expected_call_args, request.call_args_list)

        mock_json.load.assert_called_with(
            mock_connection.return_value.getresponse.return_value)
        mock_open.assert_called_with(
            '/mock_project/mock_path.unityasset', 'rb')

    @patch('unity_asset_uploader.open')
    @patch('unity_asset_uploader.json')
    @patch('unity_asset_uploader.client.HTTPSConnection')
    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_upload_package_published_error(
            self, mock_stdout, mock_connection, mock_json, mock_open):
        getresponse = mock_connection.return_value.getresponse.return_value
        getresponse.status = HTTPStatus.OK
        mock_json.load.return_value = self.expected_metadata
        mock_file = MagicMock()
        read = mock_open.return_value.__enter__.return_value.read
        read.return_value = 'encoded_package_body'

        with self.assertRaises(unity_asset_uploader.InvalidRequestError) as cm:
            unity_asset_uploader.upload_package(
                unity_username,
                unity_password,
                None,
                '456',
                '/mock_project2/mock_path.unityasset')

            self.assertIn('no draft created for package 456', cm.exception.output)

        request = mock_connection.return_value.request
        self.assertEqual(self.expected_login_and_metadata_calls,
                         request.call_args_list)

        mock_json.load.assert_called_with(
            mock_connection.return_value.getresponse.return_value)
        mock_open.assert_not_called()

    @patch('unity_asset_uploader.open')
    @patch('unity_asset_uploader.json')
    @patch('unity_asset_uploader.client.HTTPSConnection')
    @patch('unity_asset_uploader.sys.stdout', new_callable=StringIO)
    def test_upload_package_not_found_error(
            self, mock_stdout, mock_connection, mock_json, mock_open):
        getresponse = mock_connection.return_value.getresponse.return_value
        getresponse.status = HTTPStatus.OK
        mock_json.load.return_value = self.expected_metadata
        mock_file = MagicMock()
        read = mock_open.return_value.__enter__.return_value.read
        read.return_value = 'encoded_package_body'

        with self.assertRaises(unity_asset_uploader.InvalidRequestError) as cm:
            unity_asset_uploader.upload_package(
                unity_username,
                unity_password,
                None,
                '789',
                '/mock_project3/mock_path.unityasset')
            error_msg = 'could not find package with version ID 789'
            self.assertIn(error_msg, cm.exception.output)

        request = mock_connection.return_value.request
        self.assertEqual(self.expected_login_and_metadata_calls,
                         request.call_args_list)

        mock_json.load.assert_called_with(
            mock_connection.return_value.getresponse.return_value)
        mock_open.assert_not_called()


if __name__ == '__main__':
    unittest.main()

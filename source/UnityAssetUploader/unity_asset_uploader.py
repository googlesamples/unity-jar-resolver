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

"""Command-line interface to authenticate with the unity asset store
servers, get information about publisher accounts, and upload new asset
packages.
"""

import argparse
from http import client
from urllib import parse
import json
import os
import sys

DEFAULT_TOOLS_VERSION = 'v4.1.0'
DEFAULT_UNITY_VERSION = '5.6.0f3'

_DEFAULT_SERVER = 'kharma.unity3d.com'
LISTING_TRAITS = [
    'name',
    'version_name',
    'icon_url',
    'preview_url',
    'root_guid',
    'status']

_HTTP_METHOD_GET = 1
_HTTP_METHOD_POST = 2
_HTTP_METHOD_PUT = 3


class InvalidRequestError(Exception):
    """Raised when the required parameters are not provided for a request."""
    pass


class RequestError(Exception):
    """Raised when a request to the asset store fails or does not return
        expected output."""
    pass


class AssetStoreSession(object):
    """Stores data about a unity asset store session, including login info,
        server, and store/tool version.
    """

    def __init__(
            self,
            username=None,
            password=None,
            session_id=None,
            server=_DEFAULT_SERVER,
            unity_version=DEFAULT_UNITY_VERSION,
            tools_version=DEFAULT_TOOLS_VERSION):
        """Create an instance of AssetStoreSession.
        Args:
            username: With password, one option for authenticating request.
            password: With username, one option for authenticating request.
            session_id: Option 2 for authenticating request.
            server: The http server to which to direct requests.
            unity_version: Version of unity used to include with request.
            tools_version: Version of Asset Store Tools to include.
        """
        self.username = username
        self.password = password
        self.session_id = session_id
        self.session_id = None
        self.server = server
        self.unity_version = unity_version
        self.tools_version = tools_version

    def _encode_path_and_params(self, path, params={}):
        """Encodes the path string with the params dict as query string.

        Args:
            path: Path string for request, e.g. /login.
            params: Parameters for request.

        Returns:
            Encoded path+query string, e.g. /login?username=User_1...
        """
        encoded_params = parse.urlencode(params)
        return "{}?{}".format(path, encoded_params)

    def _make_request(
            self,
            path,
            method=_HTTP_METHOD_GET,
            params=None,
            headers=None,
            body=None):
        """ Places an https request and returns the decoded JSON response object.

        Args:
            path: Path portion of url to retrieve,
                e.g. "/api/asset-store-tools/metadata/0.json"
            method: Http method to use as static flag above,
                e.g. _HTTP_METHOD_GET.
            params: Any additional params to include with request.
            headers: Any additional headers to include with request.
            body: Form body for PUT requests.

        Returns:
            JSON-decoded response data.

        Raises:
            RequestFailedError if response is not found.
        """
        params = dict(params) if params else {}
        headers = dict(headers) if headers else {}

        if not self.session_id:
            self._login()

        if self.username:
            params['user'] = self.username
        if self.password:
            params['pass'] = self.password
        params['unityversion'] = self.unity_version
        params['toolversion'] = self.tools_version
        params['xunitysession'] = self.session_id

        headers['Accept'] = 'application/json'

        encoded_params = parse.urlencode(params)

        try:
            connection = client.HTTPSConnection(self.server)
            if method == _HTTP_METHOD_GET:
                connection.request(
                    'GET',
                    self._encode_path_and_params(path, params),
                    headers=headers)
            elif method == _HTTP_METHOD_POST:
                headers['Content-Type'] = 'application/x-www-form-urlencoded'
                connection.request(
                    'POST',
                    path,
                    parse.urlencode(params),
                    headers=headers)
            elif method == _HTTP_METHOD_PUT:
                connection.request(
                    'PUT',
                    self._encode_path_and_params(path, params),
                    body,
                    headers=headers)
            else:
                raise ValueError("Invalid http method provided.")
            response = connection.getresponse()
            if response.status > client.FOUND:
                print("Response: {}".format(response.read()))
                raise Exception("Error making http request: {} {}".format(
                    response.status, response.reason))

            response_ob = json.load(response)
        finally:
            connection.close()

        return response_ob

    def _login(self):
        """ Places an https request to /login with either username/password or session_id.

        Returns:
            JSON-decoded response data.

        Raises:
            RequestFailedError if response is not found.
        """
        params = {}

        if self.username and self.password:
            params['user'] = self.username
            params['pass'] = self.password
        elif self.session_id:
            params['xunitysession'] = self.session_id
        else:
            raise InvalidRequestError(
                'Either username and password or session_id is required.')
        params['unityversion'] = self.unity_version
        params['toolversion'] = self.tools_version

        try:
            connection = client.HTTPSConnection(self.server)
            encoded_path = self._encode_path_and_params("/login", params)
            connection.request(
                'GET',
                encoded_path,
                headers={'Accept': 'application/json'})
            response = connection.getresponse()
            if response.status > client.FOUND:
                print("Response: {}".format(response.read()))
                raise RequestError("Error making https request: {} {}".format(
                    response.status, response.reason))

            response_ob = json.load(response)
            self.session_id = response_ob['xunitysession']
            if not self.session_id:
                raise RequestError(
                    'Unable to login to unity asset store server.')
        finally:
            connection.close()

        return response_ob

    def get_session_id(self):
        """ Retrieve session ID for non-login calls that provide user and pass.

        Returns:
            Unity store session ID as string.
        """
        response_ob = self._login()
        if not self.session_id:
            raise RequestError('No xunitysession found in Http response.')
        return self.session_id

    def get_metadata(self):
        """ Get publisher/package metadata from
            /api/asset-store-tools/metadata/0.json.

        Metadata contains JSON-encoded information about the publisher and
        their packages on the asset store, including ID, project
        path/version, draft status, icon assets, and more.

        Returns:
            JSON-formatted metadata.
        """
        return self._make_request('/api/asset-store-tools/metadata/0.json')

    def get_publisher_info(self):
        response_ob = self.get_metadata()
        return response_ob['publisher']

    def get_display_listings(self):
        response_ob = self.get_metadata()
        return response_ob['packages']

    def upload_package(self, package_id, package_path):
        metadata = self.get_metadata()

        if package_id not in metadata['packages']:
            raise InvalidRequestError(
                'Error: could not find package with version ID {}'.format(
                    package_id))
        package = metadata['packages'][package_id]

        # If package is not in "draft" state, a new draft must be created by calling
        # the publisher assetstore endpoint.
        if package['status'] != 'draft':
            raise InvalidRequestError(
                "Error: no draft created for package {}. ".format(package_id) +
                "Please create a draft via " +
                "https://publisher.assetstore.unity3d.com")

        path = "/api/asset-store-tools/package/{}/unitypackage.json".format(
            package['id'])
        params = {
            'xunitysession': self.session_id,
            'root_guid': package['root_guid'],
            'root_path': package['root_path'],
            'project_path': package['project_path']}

        try:
            with open(package_path, 'rb') as package_file:
                response_ob = self._make_request(
                    path,
                    method=_HTTP_METHOD_PUT,
                    params=params,
                    body=package_file.read())

                package_file.close()
                return response_ob
        except Exception as ex:
            raise RequestError('Exception while processing package upload.')


def get_session_id(
        username,
        password,
        session_id,
        server=_DEFAULT_SERVER,
        unity_version=DEFAULT_UNITY_VERSION,
        tools_version=DEFAULT_TOOLS_VERSION):
    """Retrieve xunitysession for non-login calls that provide user and pass.

    Args:
        username: With password, one option for authenticating request.
        password: With username, one option for authenticating request.
        session_id: Option 2 for authenticating request. If provided, simply
            returns this value.
        server: The http server to which to direct requests.
        unity_version: Version of unity used to include with request.
        tools_version: Version of Asset Store Tools to include with request.

    Returns:
        Unity store session ID as string.
    """
    session = AssetStoreSession(
        server=server,
        username=username,
        password=password,
        session_id=session_id,
        unity_version=unity_version,
        tools_version=tools_version)
    return session.get_session_id()


def display_session_id(
        username,
        password,
        session_id,
        server=_DEFAULT_SERVER,
        unity_version=DEFAULT_UNITY_VERSION,
        tools_version=DEFAULT_TOOLS_VERSION):
    """Print auth session ID. Requires username and password args.

    Args:
        username: With password, one option for authenticating request.
        password: With username, one option for authenticating request.
        session_id: Option 2 for authenticating request.
        server: The http server to which to direct requests.
        unity_version: Version of unity used to include with request.
        tools_version: Version of Asset Store Tools to include with request.
    """
    session = AssetStoreSession(
        username=username,
        password=password,
        session_id=session_id,
        server=server,
        unity_version=unity_version,
        tools_version=tools_version)
    print("session_id={}".format(session.get_session_id()))


def display_publisher_info(
        username,
        password,
        session_id,
        server=_DEFAULT_SERVER,
        unity_version=DEFAULT_UNITY_VERSION,
        tools_version=DEFAULT_TOOLS_VERSION):
    """Print publisher ID and name.

    Args:
        username: With password, one option for authenticating request.
        password: With username, one option for authenticating request.
        session_id: Option 2 for authenticating request.
        server: The http server to which to direct requests.
        unity_version: Version of unity used to include with request.
        tools_version: Version of Asset Store Tools to include with request.
    """
    session = AssetStoreSession(
        username=username,
        password=password,
        session_id=session_id,
        server=server,
        unity_version=unity_version,
        tools_version=tools_version)
    metadta = session.get_metadata()
    print("publisher_id={}; publisher_name={}; package_ids=({})".format(
        metadta['publisher']['id'],
        metadta['publisher']['name'],
        ' '.join(metadta['packages'].keys())))


def display_listings(
        username,
        password,
        session_id,
        server=_DEFAULT_SERVER,
        unity_version=DEFAULT_UNITY_VERSION,
        tools_version=DEFAULT_TOOLS_VERSION):
    """Print information about each package listed under publisher account.

    Args:
        username: With password, one option for authenticating request.
        password: With username, one option for authenticating request.
        session_id: Option 2 for authenticating request.
        server: The http server to which to direct requests.
        unity_version: Version of unity used to include with request.
        tools_version: Version of Asset Store Tools to include with request.
    """
    session = AssetStoreSession(
        username=username,
        password=password,
        session_id=session_id,
        server=server,
        unity_version=unity_version,
        tools_version=tools_version)
    metadata = session.get_metadata()
    packages = metadata['packages']
    for package_id in packages:
        package = packages[package_id]
        output = [
            "package_id={}".format(package_id),
            "package_version_id={}".format(package['id'])
        ]
        for trait in LISTING_TRAITS:
            output.append("{}={}".format(trait, package[trait]))
        print('; '.join(output))


def upload_package(
        username,
        password,
        session_id,
        package_id,
        package_path,
        server=_DEFAULT_SERVER,
        unity_version=DEFAULT_UNITY_VERSION,
        tools_version=DEFAULT_TOOLS_VERSION):
    """Upload a local unitypackage file to unity asset store.

    Args:
        username: With password, one option for authenticating request.
        password: With username, one option for authenticating request.
        session_id: Option 2 for authenticating request.
        package_id: ID of package to upload, as retrieved from
            session.get_display_listings().
        package_path: Path to package source, as retrieved from
            session.get_display_listings().
        server: The http server to which to direct requests.
        unity_version: Version of unity used to include with request.
        tools_version: Version of Asset Store Tools to include with request.
    """
    session = AssetStoreSession(
        username=username,
        password=password,
        session_id=session_id,
        server=server,
        unity_version=unity_version,
        tools_version=tools_version)
    response_ob = session.upload_package(package_id, package_path)
    status = response_ob.get('status', '<unknown>')
    print("status={}".format(status))
    if status != 'ok':
        raise RequestError(
            'Non-success response from Asset Store request: {}'.format(
                status))
    print(response_ob)


def parse_commandline_args():
    """Parses command line arguments."""
    parser = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter)

    # Auth arguments. Either username+password or session_id is required.
    parser.add_argument(
        '--username',
        default=os.environ.get('UNITY_USERNAME'),
        help='Username of the Unity publisher account.')

    parser.add_argument(
        '--password',
        default=os.environ.get('UNITY_PASSWORD'),
        help='Password of the Unity publisher account.')

    parser.add_argument(
        '--session_id',
        default=None,
        help='Session ID / auth key returned from display_session_id.')

    parser.add_argument(
        '--server',
        default=_DEFAULT_SERVER,
        help='Server component of the asset store API URL.')

    # Miscellaneous args.
    parser.add_argument(
        '--unity_version',
        default=DEFAULT_UNITY_VERSION,
        help='Version of Unity to report to the asset store.')

    parser.add_argument(
        '--tools_version',
        default=DEFAULT_TOOLS_VERSION,
        help='Version of Tools plugin to report to the asset store.')

    # Command subparsers
    command = parser.add_subparsers(dest='command')

    command.add_parser(
        'display_session_id',
        help=display_session_id.__doc__)

    command.add_parser(
        'display_publisher_info',
        help=display_publisher_info.__doc__)

    command.add_parser(
        'display_listings',
        help=display_listings.__doc__)

    upload_package_command = command.add_parser(
        'upload_package',
        help=upload_package.__doc__)

    upload_package_command.add_argument(
        '--package_id',
        help='Package ID of the package to upload from package_path.',
        default=os.environ.get('UNITY_PACKAGE_ID'))

    upload_package_command.add_argument(
        '--package_path',
        help='Path to the .unitypackage file to upload.',
        default=os.environ.get('UNITY_PACKAGE_PATH'))

    # Verify either username+password or session_id is provided.
    args = parser.parse_args()
    if (args.session_id is None and (
            args.username is None or args.password is None)):
        sys.stderr.write(
            'Either --session_id or --username and --password required.')
        parser.print_help()
        return 1
    return args


def run_command(args):

    if args.command == 'display_session_id':
        display_session_id(
            args.username,
            args.password,
            args.session_id,
            args.server,
            args.unity_version,
            args.tools_version)

    elif args.command == 'display_publisher_info':
        display_publisher_info(
            args.username,
            args.password,
            args.session_id,
            args.server,
            args.unity_version,
            args.tools_version)

    elif args.command == 'display_listings':
        display_listings(
            args.username,
            args.password,
            args.session_id,
            args.server,
            args.unity_version,
            args.tools_version)

    elif args.command == 'upload_package':
        upload_package(
            args.username,
            args.password,
            args.session_id,
            args.package_id,
            args.package_path,
            args.server,
            args.unity_version,
            args.tools_version)


def main():
    args = parse_commandline_args()
    return run_command(args)


if __name__ == '__main__':
    sys.exit(main())

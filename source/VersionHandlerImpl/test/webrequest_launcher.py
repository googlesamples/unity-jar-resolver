#!/usr/bin/env python3
# Copyright (C) 2019 Google Inc. All Rights Reserved.
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
#  limitations under the License.

import http
import http.server
import json
import socketserver
import subprocess
import sys
import threading
import urllib.parse


class TestServer(socketserver.TCPServer):
  """TCPServer that starts an integration test ."""

  # If the process is restarted, this will reuse the OS socket used by this
  # server.
  allow_reuse_address = True

  def __init__(self, server_address, RequestHandlerClass,
               bind_and_activate=True):
    """Initialize with no callable for the service_actions() event.

    Args:
      server_address: Address of the server.
      RequestHandlerClass: Class that handles requests.
      bind_and_activate: The constructor automatically attempts to invoke
        server_bind() and server_activate().
    """
    super().__init__(server_address, RequestHandlerClass, bind_and_activate)
    self.__service_actions_callable = None

  def set_service_actions_callable(self, callback):
    """Stores a method that is called on the next poll of service_actions.

    Args:
     callback: Callable to call on the next poll of service_actions.
    """
    self.__service_actions_callable = callback

  def service_actions(self):
    """Calls the callable registered with set_service_actions_callable()."""
    if self.__service_actions_callable:
      self.__service_actions_callable()
      self.__service_actions_callable = None


class TestHandler(http.server.BaseHTTPRequestHandler):
  """Echos GET and POST requests."""

  def build_response(self):
    """Build a common response for GET and POST requests.

    Writes a successful response with the JSON payload containing:
    * requested path
    * parsed query as a dictionary
    * parsed form data as a dictionary
    * constant data in the "data" field.
    """
    url = urllib.parse.urlparse(self.path)
    query = urllib.parse.parse_qs(url.query) if url.query else {}

    # If the request has a payload, parse it.
    form_data = {}
    content_length = int(self.headers.get("Content-Length", "0"))
    if (content_length and
        self.headers.get("Content-Type", "") ==
        "application/x-www-form-urlencoded"):
      form_data = urllib.parse.parse_qs(
        self.rfile.read(content_length).decode())

    # Echo headers that start with "Echo".
    echo_headers = {}
    for header_key, header_value in self.headers.items():
      if header_key.startswith("Echo"):
        echo_headers[header_key] = header_value

    response = {
      "data": "Hello from a test server",
      "headers": echo_headers,
      "path": self.path,
      "query": query}
    if form_data:
      response["form"] = form_data

    self.send_response(http.HTTPStatus.OK)
    for header_key, header_value in echo_headers.items():
      self.send_header(header_key, header_value)
    self.end_headers()
    self.wfile.write(json.dumps(response, sort_keys=True).encode())

  def do_GET(self):
    """Handle a GET request.

    Writes a success header with the requested path and query and some constant
    data in the response.
    """
    self.build_response()

  def do_POST(self):
    """Handle a POST request.

    Writes a success header with the requested path and query, posted form data
    and some constant data in the response.
    """
    self.build_response()


class TestRunner(threading.Thread):
  """Runs a test process, stopping the TCP server when the test is complete."""

  def __init__(self, tcp_server, test_args, **kwargs):
    """Initialize the test runner.

    Args:
      test_args: Subprocess arguments to start the test.
      tcp_server: Server to stop when the test is complete.
      **kwargs: Arguments to pass to the Thread constructor.
    """
    self.tcp_server = tcp_server
    self.test_args = test_args
    self.process_result = -1
    super().__init__(**kwargs)

  def __call__(self):
    """Starts the thread."""
    self.start()

  def run(self):
    """Start the test subprocess and stop the server when it's complete."""
    with subprocess.Popen(self.test_args) as proc:
      proc.wait()
      self.process_result = proc.returncode
      self.tcp_server.shutdown()

def main():
  """Run a test web server and start a test by forking a process.

  The test web server is started by this method which runs a subprocess using
  the arguments of this script.  When the subprocess is complete the server is
  shut down.
  """
  subprocess_arguments = sys.argv[1:]
  runner = None
  with TestServer(("localhost", 8000), TestHandler) as httpd:
    runner = TestRunner(httpd, subprocess_arguments)
    httpd.set_service_actions_callable(runner)
    httpd.serve_forever()
  return runner.process_result if runner else -1

if __name__ == '__main__':
  sys.exit(main())

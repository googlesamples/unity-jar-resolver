# Unity Asset Uploader

Command-line interface to authenticate with the unity asset store
servers, get information about publisher accounts, and upload new asset
packages.

## Requirements

* Python 3
* A Unity Asset Store [publisher account](https://publisher.assetstore.unity3d.com).

## Usage
```
positional arguments:
  {display_session_id,display_publisher_info,display_listings,upload_package}
    display_session_id  Print (possibly refreshed) auth session ID.
    display_publisher_info
                        Print publisher ID and name.
    display_listings    Print information about each package listed under
                        publisher account.
    upload_package      Upload a local unitypackage file to unity asset store.
                        Args:
                            package_id: Package ID to upload, retrieved from get_display_listings.
                            package_path: Path to source, retrieved from get_display_listings.

global arguments:
  -h, --help            show this help message and exit
  --username USERNAME   Username of the Unity publisher account. With --password, one of two ways to
                        authenticate requests.
                        Defaults to environment variable UNITY_USERNAME.
  --password PASSWORD   Password of the Unity publisher account. With --username, one of two ways to
                        authenticate requests.
                        Defaults to environment variable UNITY_PASSWORD.
  --session_id SESSION_ID
                        Session ID / auth key returned from display_session_id. Option 2 for
                        authenticating requests.
  --server SERVER       Server component of the asset store API URL.
  --unity_version UNITY_VERSION
                        Version of Unity to report to the asset store.
  --tools_version TOOLS_VERSION
                        Version of Tools plugin to report to the asset store.
```

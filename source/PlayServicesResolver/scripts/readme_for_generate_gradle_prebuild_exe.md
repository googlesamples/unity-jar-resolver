What is generate_gradle_prebuild.exe?
==================================================
A Windows executable version of generate_gradle_prebuild.py.
This lets Windows users run the Python script without having Python installed.

How do I create generate_gradle_prebuild.exe?
==========================================================
We use PyInstaller (http://www.pyinstaller.org/) to bundle the Python dll
and support libraries together into one executable.

Installation instructions here:
https://pyinstaller.readthedocs.io/en/stable/installation.html

Once installed, you can generate the executable with:
pyinstaller.exe --onefile generate_gradle_prebuild.py

The executable generate_gradle_prebuild.exe is output to the `dist` directory.

When executed, it extracts it's own bootloader, the script, and all of the
python dependencies to a temporary folder for execution. As a consequence
there are some gotchas with finding data files. Read more about that here:
https://pythonhosted.org/PyInstaller/operating-mode.html
https://pythonhosted.org/PyInstaller/runtime-information.html

Why PyInstaller?
===============
There are a lot of Python-to-executable bundlers. PyInstaller appears to have
the best platform support. It works on Mac and Linux as well as Windows.
It also seems to be the most popular in the community.


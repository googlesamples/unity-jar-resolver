// <copyright file="CommandLine.cs" company="Google Inc.">
// Copyright (C) 2016 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>
#if UNITY_ANDROID

namespace GooglePlayServices
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    using System;
    using UnityEditor;

    public static class CommandLine
    {
        /// <summary>
        /// Result from Run().
        /// </summary>
        public class Result
        {
            /// String containing the standard output stream of the tool.
            public string stdout;
            /// String containing the standard error stream of the tool.
            public string stderr;
            /// Exit code returned by the tool when execution is complete.
            public int exitCode;
        };

        /// <summary>
        /// Called when a RunAsync() completes.
        /// </summary>
        public delegate void CompletionHandler(Result result);

        /// <summary>
        /// Asynchronously execute a command line tool, calling the specified delegate on
        /// completion.
        /// </summary>
        /// <param name="toolPath">Tool to execute.</param>
        /// <param name="arguments">String to pass to the tools' command line.</param>
        /// <param name="workingDirectory">Directory to execute the tool from.</param>
        /// <param name="completionDelegate">Called when the tool completes.</param>
        /// <param name="stdin">List of lines to write to the standard input.</param>
        /// <param name="stdoutHandler">Additional handler for the standard output stream.</param>
        /// <param name="stderrHandler">Additional handler for the standard error stream.</param>
        public static void RunAsync(
            string toolPath, string arguments, string workingDirectory,
            CompletionHandler completionDelegate, string[] stdin = null,
            DataReceivedEventHandler stdoutHandler = null,
            DataReceivedEventHandler stderrHandler = null)
        {
            System.Threading.Thread thread =
                new System.Threading.Thread(new System.Threading.ThreadStart(
                    () => {
                        Result result = Run(toolPath, arguments, workingDirectory,
                                            stdin: stdin, stdoutHandler: stdoutHandler,
                                            stderrHandler: stderrHandler);
                        completionDelegate(result);
                    }));
            thread.Start();
        }

        /// <summary>
        /// Execute a command line tool.
        /// </summary>
        /// <param name="toolPath">Tool to execute.</param>
        /// <param name="arguments">String to pass to the tools' command line.</param>
        /// <param name="workingDirectory">Directory to execute the tool from.</param>
        /// <param name="stdin">List of lines to write to the standard input.</param>
        /// <param name="stdoutHandler">Additional handler for the standard output stream.</param>
        /// <param name="stderrHandler">Additional handler for the standard error stream.</param>
        /// <returns>CommandLineTool result if successful, raises an exception if it's not
        /// possible to execute the tool.</returns>
        public static Result Run(
            string toolPath, string arguments, string workingDirectory, string[] stdin = null,
            DataReceivedEventHandler stdoutHandler = null,
            DataReceivedEventHandler stderrHandler = null)
        {
            List<string>[] stdouterr = new List<string>[] { new List<string>(),
                                                            new List<string>() };
            System.Text.Encoding inputEncoding = Console.InputEncoding;
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Process process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            if (stdin != null) process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.FileName = toolPath;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.OutputDataReceived += (unusedSender, args) => stdouterr[0].Add(args.Data);
            if (stdoutHandler != null) process.OutputDataReceived += stdoutHandler;
            process.ErrorDataReceived += (unusedSender, args) => stdouterr[1].Add(args.Data);
            if (stderrHandler != null) process.ErrorDataReceived += stderrHandler;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (stdin != null)
            {
                foreach (string line in stdin)
                {
                    process.StandardInput.WriteLine(line);
                }
                process.StandardInput.Close();
            }
            process.WaitForExit();
            Result result = new Result();
            result.stdout = String.Join(Environment.NewLine, stdouterr[0].ToArray());
            result.stderr = String.Join(Environment.NewLine, stdouterr[1].ToArray());
            result.exitCode = process.ExitCode;
            Console.InputEncoding = inputEncoding;
            return result;
        }

        /// <summary>
        /// Get an executable extension.
        /// </summary>
        /// <returns>Platform specific extension for executables.</returns>
        public static string GetExecutableExtension()
        {
            return (UnityEngine.RuntimePlatform.WindowsEditor ==
                    UnityEngine.Application.platform) ? ".exe" : "";
        }

        /// <summary>
        /// Locate an executable in the system path.
        /// </summary>
        /// <param name="exeName">Executable name without a platform specific extension like
        /// .exe</param>
        /// <returns>A string to the executable path if it's found, null otherwise.</returns>
        public static string FindExecutable(string executable)
        {
            string which = (UnityEngine.RuntimePlatform.WindowsEditor ==
                            UnityEngine.Application.platform) ? "where" : "which";
            try
            {
                Result result = Run(which, executable, Environment.CurrentDirectory);
                if (result.exitCode == 0)
                {
                    string[] lines = System.Text.RegularExpressions.Regex.Split(result.stdout,
                                                                                "\r\n|\r|\n");
                    return lines[0];
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("'" + which + "' command is not on path.  " +
                                      "Unable to find executable '" + executable +
                                      "' (" + e.ToString() + ")");
            }
            return null;
        }
    }
}

#endif  // UNITY_ANDROID

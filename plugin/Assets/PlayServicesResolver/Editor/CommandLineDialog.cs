// <copyright file="CommandLineDialog.cs" company="Google Inc.">
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
    using System.Collections;
    using UnityEditor;

    internal class CommandLineDialog : TextAreaDialog
    {
        /// <summary>
        /// Forwards the output of the currently executing command to a CommandLineDialog window.
        /// </summary>
        public class ProgressReporter
        {
            /// <summary>
            /// Used to scale the progress bar by the number of lines reported by the command
            /// line tool.
            /// </summary>
            public int maxProgressLines;

            // Queue of command line output lines to send to the main / UI thread.
            private Queue textQueue = null;
            // Number of lines reported by the command line tool.
            private volatile int linesReported;
            // Command line tool result, set when command line execution is complete.
            private volatile CommandLine.Result result = null;

            /// <summary>
            /// Event called on the main / UI thread when the outstanding command line tool
            /// completes.
            /// </summary>
            public event CommandLine.CompletionHandler Complete;

            /// <summary>
            /// Construct a new reporter.
            /// </summary>
            public ProgressReporter()
            {
                textQueue = Queue.Synchronized(new Queue());
                maxProgressLines = 0;
                linesReported = 0;
                Complete = null;
            }

            /// <summary>
            /// Called from RunCommandLine() tool to report the output of the currently
            /// executing commmand.
            /// </summary>
            public void DataReceivedHandler(object unusedSender,
                                            System.Diagnostics.DataReceivedEventArgs args)
            {
                textQueue.Enqueue(args.Data + "\n");
                linesReported ++;
            }

            /// <summary>
            /// Called when the currently executing command completes.
            /// </summary>
            public void CommandLineToolCompletion(CommandLine.Result result)
            {
                this.result = result;
            }

            /// <summary>
            /// Called from CommandLineDialog in the context of the main / UI thread.
            /// </summary>
            public void Update(CommandLineDialog window) {
                if (textQueue.Count > 0)
                {
                    string data = (string)textQueue.Dequeue();
                    string bodyText = window.bodyText;
                    // Really weak handling carriage returns for progress style updates.
                    int carriageReturn = bodyText.LastIndexOf("\r");
                    string bodyTail = carriageReturn >= 0 && carriageReturn < bodyText.Length - 1 ?
                        bodyText.Substring(carriageReturn + 1) : "";
                    if (carriageReturn > 0) bodyText = bodyText.Substring(0, carriageReturn);
                    int tailLength = bodyTail.Length - data.Length;
                    bodyTail = tailLength > 0 ? bodyTail.Substring(data.Length) : "";
                    window.bodyText = bodyText + data + bodyTail;
                    window.Repaint();
                }
                if (maxProgressLines > 0)
                {
                    window.progress = (float)linesReported / (float)maxProgressLines;
                }
                if (result != null)
                {
                    window.progressTitle = "";
                    if (Complete != null)
                    {
                        Complete(result);
                        Complete = null;
                    }
                }
            }
        }

        public volatile float progress;
        public string progressTitle;
        public string progressSummary;

        /// <summary>
        /// Event delegate called from the Update() method of the window.
        /// </summary>
        public delegate void UpdateDelegate(CommandLineDialog window);

        public event UpdateDelegate UpdateEvent;

        private bool progressBarVisible;

        /// <summary>
        /// Create a dialog box which can display command line output.
        /// </summary>
        /// <returns>Reference to the new window.</returns>
        public static CommandLineDialog CreateCommandLineDialog(string title)
        {
            CommandLineDialog window = (CommandLineDialog)EditorWindow.GetWindow(
                typeof(CommandLineDialog), true, title);
            window.Initialize();
            return window;
        }

        /// <summary>
        /// Initialize all members of the window.
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();
            progress = 0.0f;
            progressTitle = "";
            progressSummary = "";
            UpdateEvent = null;
            progressBarVisible = false;
        }

        /// <summary>
        /// Asynchronously execute a command line tool in this window, showing progress
        /// and finally calling the specified delegate on completion from the main / UI thread.
        /// </summary>
        /// <param name="toolPath">Tool to execute.</param>
        /// <param name="arguments">String to pass to the tools' command line.</param>
        /// <param name="workingDirectory">Directory to execute the tool from.</param>
        /// <param name="completionDelegate">Called when the tool completes.</param>
        /// <param name="windowTitle">Title of the window used to display the command line
        /// output.</param>
        /// <param name="stdin">List of lines to write to the standard input.</param>
        /// <param name="stdoutHandler">Additional handler for the standard output stream.</param>
        /// <param name="stderrHandler">Additional handler for the standard error stream.</param>
        /// <param name="maxProgressLines">Specifies the number of lines output by the
        /// command line that results in a 100% value on a progress bar.</param>
        /// <returns>Reference to the new window.</returns>
        public void RunAsync(
            string toolPath, string arguments, string workingDirectory,
            CommandLine.CompletionHandler completionDelegate,
            string[] stdin = null,
            System.Diagnostics.DataReceivedEventHandler stderrHandler = null,
            int maxProgressLines = 0)
        {
            CommandLineDialog.ProgressReporter reporter =
                new CommandLineDialog.ProgressReporter();
            reporter.maxProgressLines = maxProgressLines;
            // Call the reporter from the UI thread from this window.
            UpdateEvent += reporter.Update;
            // Connect the user's delegate to the reporter's completion method.
            reporter.Complete += completionDelegate;
            // Disconnect the reporter when the command completes.
            CommandLine.CompletionHandler reporterUpdateDisable =
                (CommandLine.Result unusedResult) => { this.UpdateEvent -= reporter.Update; };
            reporter.Complete += reporterUpdateDisable;
            CommandLine.RunAsync(toolPath, arguments, workingDirectory,
                                 reporter.CommandLineToolCompletion,
                                 stdin: stdin, stdoutHandler: reporter.DataReceivedHandler,
                                 stderrHandler: stderrHandler);
        }

        /// <summary>
        /// Call the update event from the UI thread, optionally display / hide the progress bar.
        /// </summary>
        protected virtual void Update()
        {
            if (UpdateEvent != null) UpdateEvent(this);
            if (progressTitle != "")
            {
                progressBarVisible = true;
                EditorUtility.DisplayProgressBar(progressTitle, progressSummary,
                                                 progress);
            }
            else if (progressBarVisible)
            {
                progressBarVisible = false;
                EditorUtility.ClearProgressBar();
            }
        }
    }
}

#endif  // UNITY_ANDROID

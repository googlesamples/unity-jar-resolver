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

namespace GooglePlayServices
{
    using System.Collections.Generic;
    using System.Collections;
    using System.Diagnostics;
    using System.IO;
    using System;
    using UnityEditor;
    using UnityEngine;

    using Google;

    internal class CommandLineDialog : TextAreaDialog
    {
        /// <summary>
        /// Forwards the output of the currently executing command to a CommandLineDialog window.
        /// </summary>
        public class ProgressReporter : CommandLine.LineReader
        {
            /// <summary>
            /// Used to scale the progress bar by the number of lines reported by the command
            /// line tool.
            /// </summary>
            public int maxProgressLines;

            // Queue of command line output lines to send to the main / UI thread.
            private System.Collections.Queue textQueue = null;
            // Number of lines reported by the command line tool.
            private volatile int linesReported;
            // Command line tool result, set when command line execution is complete.
            private volatile CommandLine.Result result = null;
            // Logger for messages.
            private Google.Logger logger = null;

            /// <summary>
            /// Event called on the main / UI thread when the outstanding command line tool
            /// completes.
            /// </summary>
            public event CommandLine.CompletionHandler Complete;

            /// <summary>
            /// Construct a new reporter.
            /// </summary>
            /// <param name="logger">Logger to log command output.</param>
            public ProgressReporter(Google.Logger logger) {
                textQueue = System.Collections.Queue.Synchronized(new System.Collections.Queue());
                maxProgressLines = 0;
                linesReported = 0;
                LineHandler += CommandLineIOHandler;
                this.logger = logger;
                Complete = null;
            }

            // Count the number of newlines and carriage returns in a string.
            private int CountLines(string str)
            {
                return str.Split(new char[] { '\n', '\r' }).Length - 1;
            }

            /// <summary>
            /// Called from RunCommandLine() tool to report the output of the currently
            /// executing command.
            /// </summary>
            /// <param name="process">Executing process.</param>
            /// <param name="stdin">Standard input stream.</param>
            /// <param name="data">Data read from the standard output or error streams.</param>
            private void CommandLineIOHandler(Process process, StreamWriter stdin,
                                              CommandLine.StreamData data)
            {
                if (process.HasExited || data.data == null) return;
                // Count lines in stdout.
                if (data.handle == 0) linesReported += CountLines(data.text);
                // Enqueue data for the text view.
                var newLines = System.Text.Encoding.UTF8.GetString(data.data);
                textQueue.Enqueue(newLines);
                // Write to the logger.
                foreach (var line in CommandLine.SplitLines(newLines)) {
                    logger.Log(line, level: LogLevel.Verbose);
                }
            }

            /// <summary>
            /// Called when the currently executing command completes.
            /// </summary>
            public void CommandLineToolCompletion(CommandLine.Result result)
            {
                logger.Log(
                    String.Format("Command completed: {0}", result.message),
                    level: LogLevel.Verbose);
                this.result = result;
                SignalComplete();
            }

            /// <summary>
            /// Signal the completion event handler.
            /// This method *must* be called from the main thread.
            /// </summary>
            private void SignalComplete() {
                if (Complete != null) {
                    Complete(result);
                    Complete = null;
                }
            }

            /// <summary>
            /// Called from CommandLineDialog in the context of the main / UI thread.
            /// </summary>
            public void Update(CommandLineDialog window)
            {
                if (textQueue.Count > 0)
                {
                    List<string> textList = new List<string>();
                    while (textQueue.Count > 0) textList.Add((string)textQueue.Dequeue());
                    string bodyText = window.bodyText + String.Join("", textList.ToArray());
                    // Really weak handling of carriage returns.  Truncates to the previous
                    // line for each newline detected.
                    while (true)
                    {
                        // Really weak handling carriage returns for progress style updates.
                        int carriageReturn = bodyText.LastIndexOf("\r");
                        if (carriageReturn < 0 || bodyText.Substring(carriageReturn, 1) == "\n")
                        {
                            break;
                        }
                        string bodyTextHead = "";
                        int previousNewline = bodyText.LastIndexOf("\n", carriageReturn,
                                                                   carriageReturn);
                        if (previousNewline >= 0)
                        {
                            bodyTextHead = bodyText.Substring(0, previousNewline + 1);
                        }
                        bodyText = bodyTextHead + bodyText.Substring(carriageReturn + 1);
                    }
                    window.bodyText = bodyText;
                    window.Repaint();
                }
                if (maxProgressLines > 0)
                {
                    window.progress = (float)linesReported / (float)maxProgressLines;
                }
                if (result != null)
                {
                    window.progressTitle = "";
                    SignalComplete();
                }
            }
        }

        public volatile float progress;
        public string progressTitle;
        public string progressSummary;
        public Google.Logger logger = new Google.Logger();

        /// <summary>
        /// Whether a command is currently being executed.
        /// </summary>
        public bool RunningCommand { protected set; get; }

        /// <summary>
        /// Event delegate called from the Update() method of the window.
        /// </summary>
        public delegate void UpdateDelegate(CommandLineDialog window);

        public event UpdateDelegate UpdateEvent;

        /// <summary>
        /// Create a dialog box which can display command line output.
        /// </summary>
        /// <returns>Reference to the new window.</returns>
        public static CommandLineDialog CreateCommandLineDialog(string title)
        {
            // In batch mode we simply create the class without instancing the visible window.
            CommandLineDialog window = ExecutionEnvironment.InBatchMode ?
                new CommandLineDialog() : (CommandLineDialog)EditorWindow.GetWindow(
                    typeof(CommandLineDialog), true, title);
            window.Initialize();
            return window;
        }

        /// <summary>
        /// Alternative the Show() method that does not crash in batch mode.
        /// </summary>
        public new void Show() {
            Show(false);
        }

        /// <summary>
        /// Alternative Show() method that does not crash in batch mode.
        /// </summary>
        /// <param name="immediateDisplay">Display the window now.</param>
        public new void Show(bool immediateDisplay) {
            if (!ExecutionEnvironment.InBatchMode) {
                base.Show(immediateDisplay);
            }
        }

        /// <summary>
        /// Alternative Close() method that does not crash in batch mode.
        /// </summary>
        public new void Close() {
            if (!ExecutionEnvironment.InBatchMode) {
                base.Close();
            }
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
            autoScrollToBottom = false;
            logRedirector.ShouldLogDelegate = () => { return !RunningCommand; };
        }

        /// <summary>
        /// Set the progress bar status.
        /// </summary>
        /// <param name="title">Text to display before the progress bar.</param>
        /// <param name="value">Progress bar value 0..1.</param>
        /// <param name="summary">Text to display in the progress bar.</param>
        public void SetProgress(string title, float value, string summary) {
            progressTitle = title;
            progress = value;
            progressSummary = summary;
            Repaint();
        }

        // Draw the GUI with an optional status bar.
        protected override void OnGUI() {
            summaryTextDisplay = true;
            if (!String.IsNullOrEmpty(progressTitle)) {
                summaryTextDisplay = false;
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(progressTitle, EditorStyles.boldLabel);
                var progressBarRect = EditorGUILayout.BeginVertical();
                float progressValue = Math.Min(progress, 1.0f);
                EditorGUILayout.LabelField(""); // Creates vertical space for the progress bar.
                EditorGUI.ProgressBar(
                    progressBarRect, progressValue,
                    String.IsNullOrEmpty(progressSummary) ?
                        String.Format("{0}%... ", (int)(progressValue * 100.0f)) : progressSummary);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
                EditorGUILayout.EndVertical();
            }
            base.OnGUI();
        }

        /// <summary>
        /// Asynchronously execute a command line tool in this window, showing progress
        /// and finally calling the specified delegate on completion from the main / UI thread.
        /// </summary>
        /// <param name="toolPath">Tool to execute.</param>
        /// <param name="arguments">String to pass to the tools' command line.</param>
        /// <param name="completionDelegate">Called when the tool completes.</param>
        /// <param name="workingDirectory">Directory to execute the tool from.</param>
        /// <param name="ioHandler">Allows a caller to provide interactive input and also handle
        /// both output and error streams from a single delegate.</param>
        /// <param name="maxProgressLines">Specifies the number of lines output by the
        /// command line that results in a 100% value on a progress bar.</param>
        /// <returns>Reference to the new window.</returns>
        public void RunAsync(
            string toolPath, string arguments,
            CommandLine.CompletionHandler completionDelegate,
            string workingDirectory = null, Dictionary<string, string> envVars = null,
            CommandLine.IOHandler ioHandler = null, int maxProgressLines = 0)
        {
            CommandLineDialog.ProgressReporter reporter =
                new CommandLineDialog.ProgressReporter(logger);
            reporter.maxProgressLines = maxProgressLines;
            // Call the reporter from the UI thread from this window.
            UpdateEvent += reporter.Update;
            // Connect the user's delegate to the reporter's completion method.
            reporter.Complete += completionDelegate;
            // Connect the caller's IoHandler delegate to the reporter.
            reporter.DataHandler += ioHandler;
            // Disconnect the reporter when the command completes.
            CommandLine.CompletionHandler reporterUpdateDisable = (unusedResult) => {
                RunningCommand = false;
                this.UpdateEvent -= reporter.Update;
            };
            reporter.Complete += reporterUpdateDisable;
            logger.Log(String.Format(
                "Executing command: {0} {1}", toolPath, arguments), level: LogLevel.Verbose);
            RunningCommand = true;
            CommandLine.RunAsync(toolPath, arguments, reporter.CommandLineToolCompletion,
                                 workingDirectory: workingDirectory, envVars: envVars,
                                 ioHandler: reporter.AggregateLine);
        }

        /// <summary>
        /// Call the update event from the UI thread.
        /// </summary>
        protected virtual void Update()
        {
            if (UpdateEvent != null) UpdateEvent(this);
        }

        // Hide the progress bar if the window is closed.
        protected override void OnDestroy() {
            base.OnDestroy();
        }
    }
}

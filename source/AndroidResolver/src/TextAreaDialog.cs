// <copyright file="TextAreaDialog.cs" company="Google Inc.">
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
    using System;
    using UnityEditor;
    using UnityEngine;

    using Google;

    /// <summary>
    /// Window which displays a scrollable text area and two buttons at the bottom.
    /// </summary>
    public class TextAreaDialog : EditorWindow
    {
        /// <summary>
        /// Delegate type, called when a button is clicked.
        /// </summary>
        public delegate void ButtonClicked(TextAreaDialog dialog);

        /// <summary>
        /// Delegate called when a button is clicked.
        /// </summary>
        public ButtonClicked buttonClicked;

        /// <summary>
        /// Whether this window should be modal.
        /// NOTE: This emulates modal behavior by re-acquiring focus when it's lost.
        /// </summary>
        public bool modal = true;

        /// <summary>
        /// Set the text to display in the summary area of the window.
        /// </summary>
        public string summaryText = "";

        /// <summary>
        /// Whether to display summary text.
        /// </summary>
        public bool summaryTextDisplay = true;

        /// <summary>
        /// Set the text to display on the "yes" (left-most) button.
        /// </summary>
        public string yesText = "";

        /// <summary>
        /// Set the text to display on the "no" (right-most) button.
        /// </summary>
        public string noText = "";

        /// <summary>
        /// Set the text to display in the scrollable text area.
        /// </summary>
        public string bodyText = "";

        /// <summary>
        /// Result of yes / no button press.  true if the "yes" button was pressed, false if the
        /// "no" button was pressed.  Defaults to "false".
        /// </summary>
        public bool result = false;

        /// <summary>
        /// Whether either button was clicked.
        /// </summary>
        private bool yesNoClicked = false;

        /// <summary>
        /// Current position of the scrollbar.
        /// </summary>
        public Vector2 scrollPosition;

        /// <summary>
        /// Whether to automatically scroll to the bottom of the window.
        /// </summary>
        public volatile bool autoScrollToBottom;

        /// <summary>
        /// Last time the window was repainted.
        /// </summary>
        private long lastRepaintTimeInMilliseconds = 0;

        /// <summary>
        /// Minimum repaint period.
        /// </summary>
        private const long REPAINT_PERIOD_IN_MILLISECONDS = 33; // ~30Hz

        // Backing store for the Redirector property.
        internal LogRedirector logRedirector;

        /// <summary>
        /// Get the existing text area window or create a new one.
        /// </summary>
        /// <param name="title">Title to display on the window.</param>
        /// <returns>Reference to this class</returns>
        public static TextAreaDialog CreateTextAreaDialog(string title)
        {
            TextAreaDialog window = (TextAreaDialog)EditorWindow.GetWindow(typeof(TextAreaDialog),
                                                                           true, title, true);
            window.Initialize();
            return window;
        }

        public virtual void Initialize()
        {
            yesText = "";
            noText = "";
            summaryText = "";
            bodyText = "";
            result = false;
            yesNoClicked = false;
            scrollPosition = new Vector2(0, 0);
            minSize = new Vector2(300, 200);
            position = new Rect(UnityEngine.Screen.width / 3, UnityEngine.Screen.height / 3,
                                minSize.x * 2, minSize.y * 2);
            logRedirector = new LogRedirector(this);
        }

        // Add to body text.
        public void AddBodyText(string text) {
            RunOnMainThread.Run(() => {
                    bodyText += text;
                    Repaint();
                });
        }

        /// <summary>
        /// Alternative Repaint() method that does not crash in batch mode and throttles repaint
        /// rate to REPAINT_PERIOD_IN_MILLISECONDS.
        /// </summary>
        public new void Repaint() {
            if (!ExecutionEnvironment.InBatchMode) {
                // Throttle repaint to REPAINT_PERIOD_IN_MILLISECONDS.
                var timeInMilliseconds = (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
                var timeElapsedInMilliseconds = timeInMilliseconds - lastRepaintTimeInMilliseconds;
                if (timeElapsedInMilliseconds >= REPAINT_PERIOD_IN_MILLISECONDS) {
                    lastRepaintTimeInMilliseconds = timeInMilliseconds;
                    if (autoScrollToBottom) scrollPosition.y = Mathf.Infinity;
                    base.Repaint();
                }
            }
        }

        // Draw the GUI.
        protected virtual void OnGUI()
        {
            EditorGUILayout.BeginVertical();

            if (!String.IsNullOrEmpty(summaryText) && summaryTextDisplay) {
                EditorGUILayout.LabelField(summaryText, EditorStyles.boldLabel);
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            // Unity text elements can only display up to a small X number of characters (rumors
            // are ~65k) so generate a set of labels one for each subset of the text being
            // displayed.
            int bodyTextOffset = 0;
            System.Collections.Generic.List<string> bodyTextList =
                new System.Collections.Generic.List<string>();
            const int chunkSize = 5000;  // Conservative chunk size < 65k characters.
            while (bodyTextOffset < bodyText.Length)
            {
                int searchSize = Math.Min(bodyText.Length - bodyTextOffset, chunkSize);
                int readSize = bodyText.LastIndexOf("\n", bodyTextOffset + searchSize, searchSize);
                readSize = readSize >= 0 ? readSize - bodyTextOffset + 1 : searchSize;
                bodyTextList.Add(bodyText.Substring(bodyTextOffset, readSize).TrimEnd());
                bodyTextOffset += readSize;
            }
            foreach (string bodyTextChunk in bodyTextList)
            {
                float pixelHeight = EditorStyles.wordWrappedLabel.CalcHeight(
                        new GUIContent(bodyTextChunk), position.width);
                EditorGUILayout.SelectableLabel(bodyTextChunk,
                                                EditorStyles.wordWrappedLabel,
                                                GUILayout.Height(pixelHeight));
            }
            EditorGUILayout.EndScrollView();

            bool yesPressed = false;
            bool noPressed = false;
            EditorGUILayout.BeginHorizontal();
            if (yesText != "") yesPressed = GUILayout.Button(yesText);
            if (noText != "") noPressed = GUILayout.Button(noText);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            // If yes or no buttons were pressed, call the buttonClicked delegate.
            if (yesPressed || noPressed)
            {
                yesNoClicked = true;
                if (yesPressed)
                {
                    result = true;
                }
                else if (noPressed)
                {
                    result = false;
                }
                if (buttonClicked != null) buttonClicked(this);
            }
        }

        // Optionally make the dialog modal.
        protected virtual void OnLostFocus()
        {
            if (modal) Focus();
        }

        // If the window is destroyed click the no button if a listener is attached.
        protected virtual void OnDestroy() {
            if (!yesNoClicked) {
                result = false;
                if (buttonClicked != null) buttonClicked(this);
            }
        }

        /// <summary>
        /// Redirects logs to a TextAreaDialog window.
        /// </summary>
        internal class LogRedirector {

            /// <summary>
            /// Window to redirect logs to.
            /// </summary>
            private TextAreaDialog window;

            /// <summary>
            /// Create a log redirector associated with this window.
            /// </summary>
            public LogRedirector(TextAreaDialog window) {
                this.window = window;
                LogToWindow = (string message, LogLevel level) => LogMessage(message, level);
                ErrorLogged = false;
                WarningLogged = false;
                ShouldLogDelegate = () => { return true; };
            }

            /// <summary>
            /// Delegate that logs to the window associated with this object.
            /// </summary>
            public Google.Logger.LogMessageDelegate LogToWindow { get; private set; }

            /// <summary>
            /// Whether an error was logged.
            /// </summary>
            public bool ErrorLogged { get; private set; }


            /// <summary>
            /// Whether a warning was logged.
            /// </summary>
            public bool WarningLogged { get; private set; }

            /// <summary>
            /// Delegate that determines whether a message should be logged.
            /// </summary>
            public Func<bool> ShouldLogDelegate { get; set; }

            /// <summary>
            /// Log a message to the window associated with this object.
            /// </summary>
            private void LogMessage(string message, LogLevel level) {
                string messagePrefix;
                switch (level) {
                    case LogLevel.Error:
                        messagePrefix = "ERROR: ";
                        ErrorLogged = true;
                        break;
                    case LogLevel.Warning:
                        messagePrefix = "WARNING: ";
                        WarningLogged = true;
                        break;
                    default:
                        messagePrefix = "";
                        break;
                }
                if (ShouldLogDelegate()) window.AddBodyText(messagePrefix + message + "\n");

            }
        }

        /// <summary>
        /// Get an object that can redirect log messages to this window.
        /// </summary>
        internal LogRedirector Redirector { get { return logRedirector; } }
    }

}

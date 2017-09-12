// <copyright file="Logger.cs" company="Google Inc.">
// Copyright (C) 2017 Google Inc. All Rights Reserved.
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

namespace Google {

    /// <summary>
    /// Log severity.
    /// </summary>
    internal enum LogLevel {
        Debug,
        Verbose,
        Info,
        Warning,
        Error,
    };

    /// <summary>
    /// Writes filtered logs to the Unity log.
    /// </summary>
    internal class Logger {

        /// <summary>
        /// Filter the log level.
        /// </summary>
        internal LogLevel Level { get; set; }

        /// <summary>
        /// Enable / disable verbose logging.
        /// This toggles between Info vs. Verbose levels.
        /// </summary>
        internal bool Verbose {
            set { Level = value ? LogLevel.Verbose : LogLevel.Info; }
            get { return Level <= LogLevel.Verbose; }
        }

        /// <summary>
        /// Name of the file to log to, if this is null this will not log to a file.
        /// </summary>
        internal string LogFilename { get; set; }

        /// <summary>
        /// Construct a logger.
        /// </summary>
        internal Logger() {}

        /// <summary>
        /// Write a message to the log file.
        /// </summary>
        /// <param name="message">Message to log.</param>
        private void LogToFile(string message) {
            if (LogFilename != null) {
                using (var file = new System.IO.StreamWriter(LogFilename, true)) {
                    file.WriteLine(message);
                }
            }
        }

        /// <summary>
        /// Log a filtered message to Unity log and optionally to a file specified by LogFilename.
        /// </summary>
        /// <param name="message">String to write to the log.</param>
        /// <param name="level">Severity of the message, if this is below the currently selected
        /// Level property the message will not be logged.</param>
        internal virtual void Log(string message, LogLevel level = LogLevel.Info) {
            if (level >= Level) {
                switch (level) {
                    case LogLevel.Debug:
                    case LogLevel.Verbose:
                    case LogLevel.Info:
                        UnityEngine.Debug.Log(message);
                        LogToFile(message);
                        break;
                    case LogLevel.Warning:
                        UnityEngine.Debug.LogWarning(message);
                        LogToFile("WARNING: " + message);
                        break;
                    case LogLevel.Error:
                        UnityEngine.Debug.LogError(message);
                        LogToFile("ERROR: " + message);
                        break;
                }
            }
        }
    }
}

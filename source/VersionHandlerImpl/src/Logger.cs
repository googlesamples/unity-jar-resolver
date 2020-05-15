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

using System;

namespace Google {

    /// <summary>
    /// Log severity.
    /// </summary>
    public enum LogLevel {
        Debug,
        Verbose,
        Info,
        Warning,
        Error,
    };

    /// <summary>
    /// Where to log.
    /// </summary>
    [Flags]
    public enum LogTarget {
        Console = 1,
        Unity = 2,
        File = 4,
    };

    /// <summary>
    /// Writes filtered logs to the Unity log.
    /// </summary>
    public class Logger {

        /// <summary>
        /// Whether all log messages should be display.
        /// </summary>
        internal static bool DebugLoggingEnabled {
            get {
                return Environment.CommandLine.ToLower().Contains("-gvh_log_debug");
            }
        }

        /// <summary>
        /// Filter the log level.
        /// </summary>
        public LogLevel Level { get; set; }

        /// <summary>
        /// Filter the log targets.
        /// </summary>
        public LogTarget Target { get; set; }

        /// <summary>
        /// Enable / disable verbose logging.
        /// This toggles between Info vs. Verbose levels.
        /// </summary>
        public bool Verbose {
            set { Level = value ? LogLevel.Verbose : LogLevel.Info; }
            get { return Level <= LogLevel.Verbose; }
        }

        /// <summary>
        /// Name of the file to log to, if this is null this will not log to a file.
        /// </summary>
        public string LogFilename { get; set; }

        /// <summary>
        /// Delegate function used to log messages.
        /// </summary>
        /// <param name="message">Message to log.</param>
        /// <param name="level">Log level of the message.</param>
        public delegate void LogMessageDelegate(string message, LogLevel level);

        /// <summary>
        /// Event that is called for each logged message.
        /// </summary>
        public event LogMessageDelegate LogMessage;

        /// <summary>
        /// Construct a logger.
        /// </summary>
        public Logger() {
            Level = LogLevel.Info;
            Target = LogTarget.Unity | LogTarget.File;
        }

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
        public virtual void Log(string message, LogLevel level = LogLevel.Info) {
            if (level >= Level || DebugLoggingEnabled) {
                switch (level) {
                    case LogLevel.Debug:
                    case LogLevel.Verbose:
                    case LogLevel.Info:
                        if ((Target & LogTarget.Unity) != 0) UnityEngine.Debug.Log(message);
                        if ((Target & LogTarget.File) != 0) LogToFile(message);
                        if ((Target & LogTarget.Console) != 0) System.Console.WriteLine(message);
                        break;
                    case LogLevel.Warning:
                        if ((Target & LogTarget.Unity) != 0) UnityEngine.Debug.LogWarning(message);
                        if ((Target & LogTarget.File) != 0) LogToFile("WARNING: " + message);
                        if ((Target & LogTarget.Console) != 0) {
                            System.Console.WriteLine("WARNING: " + message);
                        }
                        break;
                    case LogLevel.Error:
                        if ((Target & LogTarget.Unity) != 0) UnityEngine.Debug.LogError(message);
                        if ((Target & LogTarget.File) != 0) LogToFile("ERROR: " + message);
                        if ((Target & LogTarget.Console) != 0) {
                            System.Console.WriteLine("ERROR: " + message);
                        }
                        break;
                }
            }
            if (LogMessage != null) LogMessage(message, level);
        }
    }
}

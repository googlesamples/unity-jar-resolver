// <copyright file="EditorInitializer.cs" company="Google Inc.">
// Copyright (C) 2020 Google Inc. All Rights Reserved.
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

using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEditor;

/// <summary>
/// Utility to initialize classes used only in Unity Editor.
/// </summary>
static public class EditorInitializer {
    /// <summary>
    /// Call initialization function on the main thread only when the condition is met.
    /// </summary>
    /// <param name="condition">When it returns true, call the initializer once.
    /// If null, initializer will be called in the next editor update.</param>
    /// <param name="initializer">Initialization function to be called when condition is met.
    /// </params>
    /// <param name="name">Name of the component to be initialized, for debug purpose.</param>
    /// <param name="logger">Logger to be used to report when initialization is finished.</param>
    public static void InitializeOnMainThread(
            Func<bool> condition, Func<bool> initializer, string name, Logger logger = null) {
        if (initializer == null) return;

        // Cache the flag to prevent string comparison in every frame during
        // PollOnUpdateUntilComplete()
        bool isExecuteMethodEnabled =  ExecutionEnvironment.ExecuteMethodEnabled;

        // Delay initialization until condition is met.
        RunOnMainThread.PollOnUpdateUntilComplete(() => {
            if (condition != null && !condition()) {
                // If Unity is launched with -executeMethod, in some Unity versions, editor
                // update will never be called. As a result, PollOnUpdateUntilComplete() will
                // attempt to call this poll function repeating on current thread until it returns
                // true.  Therefore, return true immediately and stop the polling in executeMethod
                // mode.
                return isExecuteMethodEnabled;
            }
            bool result = false;

            try {
                result = initializer();
            } catch (Exception e) {
                string errorMsg = String.Format("Exception thrown when initializing {0}: {1}",
                                    name, e.ToString());
                if (logger != null) {
                    logger.Log(errorMsg, level: LogLevel.Error);
                } else {
                    Debug.LogError(errorMsg);
                }
            }

            if (logger != null) {
                logger.Log(String.Format("{0} initialization {1}", name,
                    result ? "succeeded." : "failed." ),
                    level: result ? LogLevel.Verbose : LogLevel.Error);
            }

            return true;
        });
    }
}
}

// <copyright file="Dialog.cs" company="Google Inc.">
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

using System;

namespace Google {

/// <summary>
/// Interface for EditorUtility.DisplayDialog() and EditorUtility.DisplayDialogComplex() that
/// provides a global way to mock dialog methods and skip dialogs if
/// ExecutionEnvironment.InteractiveMode is false.
/// </summary>
internal class Dialog {

    /// <summary>
    /// Option selected by the dialog.
    /// </summary>
    public enum Option {

        /// <summary>
        /// Value that can be used as a default to provide different behavior in a non-interactive
        /// mode of operation.
        /// </summary>
        SelectedNone = -1,

        /// <summary>
        /// Option0 was selected by the user.
        /// </summary>
        Selected0 = 0,

        /// <summary>
        /// Option1 was selected by the user.
        /// </summary>
        Selected1 = 1,

        /// <summary>
        /// Option2 was selected by the user.
        /// </summary>
        Selected2 = 2,
    };

    /// <summary>
    /// Delegate that displays a modal dialog with up to 3 options.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <param name="message">Message to display in the dialog.</param>
    /// <param name="defaultOption">Option selected if interactivity is disabled.</param>
    /// <param name="option0">Text for the first option.</param>
    /// <param name="option1">Text for the second option or null to disable.</param>
    /// <param name="option2">Text for the third option or null to disable.</param>
    /// <returns>The selected option.</returns>
    public delegate Option DisplayDelegate(string title, string message, Option defaultOption,
                                           string option0, string option1, string option2);

    /// <summary>
    /// Delegate that displays a dialog requesting consent to report analytics.
    /// This is only exposed for testing purposes.
    /// </summary>
    internal static Dialog.DisplayDelegate displayDialogMethod = DisplayDefault;

    /// <summary>
    /// Displays a modal dialog with up to 3 options.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <param name="message">Message to display in the dialog.</param>
    /// <param name="option0">Text for the first option.</param>
    /// <param name="option1">Text for the second option.</param>
    /// <param name="option2">Text for the third option.</param>
    /// <param name="defaultOption">Option selected if interactivity is disabled.</param>
    /// <returns>When ExecutionEnvironment.InteractiveMode, the selected option, the
    /// defaultOption otherwise.</returns>
    internal static Option DisplayDefault(string title, string message, Option defaultOption,
                                          string option0, string option1, string option2) {
        if (ExecutionEnvironment.InteractiveMode) {
            if (String.IsNullOrEmpty(option1)) {
                UnityEditor.EditorUtility.DisplayDialog(title, message, option0, cancel: "");
                return Option.Selected0;
            } else if (String.IsNullOrEmpty(option2)) {
                return UnityEditor.EditorUtility.DisplayDialog(title, message, option0,
                                                               cancel: option1) ?
                    Option.Selected0 : Option.Selected1;
            } else {
                return (Option)UnityEditor.EditorUtility.DisplayDialogComplex(title, message,
                                                                              option0, option1,
                                                                              option2);
            }
        }
        return defaultOption;
    }

    /// <summary>
    /// Displays a modal dialog with up to 3 options.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <param name="message">Message to display in the dialog.</param>
    /// <param name="defaultOption">Option selected if interactivity is disabled.</param>
    /// <param name="option0">Text for the first option.</param>
    /// <param name="option1">Text for the second option.</param>
    /// <param name="option2">Text for the third option.</param>
    /// <returns>When ExecutionEnvironment.InteractiveMode, the selected option, the defaultOption
    /// otherwise.</returns>
    public static Option Display(string title, string message, Option defaultOption,
                                 string option0, string option1, string option2) {
        return displayDialogMethod(title, message, defaultOption, option0, option1, option2);
    }

    /// <summary>
    /// Displays a modal dialog with up to 2 options.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <param name="message">Message to display in the dialog.</param>
    /// <param name="defaultOption">Option selected if interactivity is disabled.</param>
    /// <param name="option0">Text for the first option.</param>
    /// <param name="option1">Text for the second option.</param>
    /// <returns>When ExecutionEnvironment.InteractiveMode, the selected option, the defaultOption
    /// otherwise.</returns>
    public static Option Display(string title, string message, Option defaultOption,
                                 string option0, string option1) {
        return displayDialogMethod(title, message, defaultOption, option0, option1, null);
    }

    /// <summary>
    /// Displays a modal dialog with up to 2 options.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <param name="message">Message to display in the dialog.</param>
    /// <param name="defaultOption">Option selected if interactivity is disabled.</param>
    /// <param name="option0">Text for the first option.</param>
    /// <param name="option1">Text for the second option.</param>
    /// <returns>When ExecutionEnvironment.InteractiveMode, the selected option, the defaultOption
    /// otherwise.</returns>
    public static Option Display(string title, string message, Option defaultOption,
                                 string option0) {
        return displayDialogMethod(title, message, defaultOption, option0, null, null);
    }
}

}

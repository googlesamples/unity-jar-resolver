// <copyright file="DialogWindow.cs" company="Google LLC">
// Copyright (C) 2020 Google LLC. All Rights Reserved.
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
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Google {

/// <summary>
/// Non-blocking version of dialog implemeted from EditorWindows.
/// The dialog will not block the editor. When multiple dialogs are triggered, they will be queued
/// and only be shown one at a time based on the triggering order.
/// </summary>
public class DialogWindow : EditorWindow {
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

    // Default width of the dialog.
    private const float DEFAULT_WINDOWS_WIDTH = 400.0f;

    /// <summary>
    /// All the data to render the content of the dialog and react to the user interaction.
    /// All the context should be serialable/deserialable so that all the context can be reloaded
    /// after Unity hot reloads.
    /// </summary>
    private class DialogContext {
        // Title of the dialog.
        internal string Title;

        // Message to display in the dialog.
        internal string Message;

        // Option selected if interactivity is disabled.
        internal Option DefaultOption = Option.SelectedNone;

        // Text for the first option.
        internal string Option0String;

        // Text for the second option or null to disable.
        internal string Option1String;

        // Text for the third option or null to disable.
        internal string Option2String;

        // Width of the dialog window.
        internal float WindowWidth = DEFAULT_WINDOWS_WIDTH;

        // Option selected if the dialog is closed.
        internal Option WindowCloseOption = Option.SelectedNone;

        // Callback to trigger once a selection is made.
        internal Action<Option> CompleteAction;

        // Callback to render additional content after dialog message.
        internal Action<DialogWindow> RenderContentAction;

        // Callback to render additional content before option buttons in the same row.
        internal Action<DialogWindow> RenderButtonsAction;

        // Callback for additional initialization after the dialog is created and before it is
        // displayed.
        internal Action<DialogWindow> InitAction;
    }

    /// <summary>
    /// Delegate that displays a non-blocking modal dialog with up to 3 options.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <param name="message">Message to display in the dialog.</param>
    /// <param name="defaultOption">Option selected if interactivity is disabled.</param>
    /// <param name="option0">Text for the first option.</param>
    /// <param name="option1">Text for the second option or null to disable.</param>
    /// <param name="option2">Text for the third option or null to disable.</param>
    /// <param name="windowWidth">(Optional) Width of the dialog window.</param>
    /// <param name="windowCloseOption">(Optional) Option selected if the dialog is closed.</param>
    /// <param name="complete">(Optional) Callback to trigger once a selection is made.</param>
    /// <param name="renderContent">(Optional) Callback to render additional content after
    /// dialog message.</param>
    /// <param name="renderButtons">(Optional) Callback to render additional content before option
    /// buttons in the same row.</param>
    /// <param name="init">(Optional) Callback for additional initialization after the dialog
    /// is created and before it is displayed.</param>
    public delegate void DisplayDelegate(
        string title, string message, Option defaultOption,
        string option0, string option1 = null, string option2 = null,
        float windowWidth = DEFAULT_WINDOWS_WIDTH, Option windowCloseOption = Option.SelectedNone,
        Action<Option> complete = null,
        Action<DialogWindow> renderContent = null,
        Action<DialogWindow> renderButtons = null,
        Action<DialogWindow> init = null);

    /// <summary>
    /// Delegate that displays a non-blocking dialog.
    /// This is only exposed for testing purposes.
    /// </summary>
    internal static DisplayDelegate displayDialogMethod = DisplayDefault;

    // Job queue for all the requests to display a dialog.
    private static RunOnMainThread.JobQueue dialogJobQueue = new RunOnMainThread.JobQueue();

    /// <summary>
    /// GUIStyle to render label with word-wrap.
    /// </summary>
    static public GUIStyle DefaultLabelStyle {
        get; private set;
    }

    /// <summary>
    /// GUIStyle to render dialog title.
    /// </summary>
    static private GUIStyle DefaultTitleStyle;

    /// <summary>
    /// GUIStyle to render dialog message.
    /// </summary>
    static private GUIStyle DefaultMessageStyle;

    // Context for this dialog window.
    private DialogContext dialogContext = new DialogContext();

    // The option selected by the user.
    private Option selectedOption = Option.SelectedNone;

    // Whether this window is terminating due to Unity hot reload.
    private bool terminating = false;

    /// <summary>
    /// Displays a non-blocking modal dialog with up to 3 options.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <param name="message">Message to display in the dialog.</param>
    /// <param name="defaultOption">Option selected if interactivity is disabled.</param>
    /// <param name="option0">Text for the first option.</param>
    /// <param name="option1">Text for the second option or null to disable.</param>
    /// <param name="option2">Text for the third option or null to disable.</param>
    /// <param name="windowWidth">Width of the dialog window.</param>
    /// <param name="windowCloseOption">Option selected if the dialog is closed.</param>
    /// <param name="complete">Callback to trigger once a selection is made.</param>
    /// <param name="renderContent">Callback to render additional content after dialog message.
    /// </param>
    /// <param name="renderButtons">Callback to render additional content before option buttons in
    /// the same row.</param>
    /// <param name="init">Callback for additional initialization after the dialog is created and
    /// before it is displayed.</param>
    internal static void DisplayDefault(string title, string message, Option defaultOption,
            string option0, string option1, string option2, float windowWidth,
            Option windowCloseOption, Action<Option> complete,
            Action<DialogWindow> renderContent, Action<DialogWindow> renderButtons,
            Action<DialogWindow> init) {
        if (ExecutionEnvironment.InteractiveMode) {
            var context = new DialogContext() {
                Title = title,
                Message = message,
                DefaultOption = defaultOption,
                Option0String = option0,
                Option1String = option1,
                Option2String = option2,
                WindowWidth = windowWidth,
                WindowCloseOption = windowCloseOption,
                CompleteAction = complete,
                RenderContentAction = renderContent,
                RenderButtonsAction = renderButtons,
                InitAction = init,
            };

            dialogJobQueue.Schedule(() => {
                var window = (DialogWindow) EditorWindow.GetWindow(
                        typeof(DialogWindow), true, context.Title, true);
                window.dialogContext = context;

                if (context.InitAction != null) {
                    context.InitAction(window);
                }

                window.Show();
            });
        } else {
            if (complete != null) {
                complete(defaultOption);
            }
        }
    }

    /// <summary>
    /// Displays a non-blocking modal dialog with up to 3 options.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <param name="message">Message to display in the dialog.</param>
    /// <param name="defaultOption">Option selected if interactivity is disabled.</param>
    /// <param name="option0">Text for the first option.</param>
    /// <param name="option1">Text for the second option or null to disable.</param>
    /// <param name="option2">Text for the third option or null to disable.</param>
    /// <param name="windowWidth">(Optional) Width of the dialog window.</param>
    /// <param name="windowCloseOption">(Optional) Option selected if the dialog is closed.</param>
    /// <param name="complete">(Optional) Callback to trigger once a selection is made.</param>
    /// <param name="renderContent">(Optional) Callback to render additional content after
    /// dialog message.</param>
    /// <param name="renderButtons">(Optional) Callback to render additional content before option
    /// buttons in the same row.</param>
    /// <param name="init">(Optional) Callback for additional initialization after the dialog
    /// is created and before it is displayed.</param>
    public static void Display(string title, string message, Option defaultOption,
            string option0, string option1 = null, string option2 = null,
            float windowWidth = DEFAULT_WINDOWS_WIDTH,
            Option windowCloseOption = Option.SelectedNone,
            Action<Option> complete = null, Action<DialogWindow> renderContent = null,
            Action<DialogWindow> renderButtons = null, Action<DialogWindow> init = null) {
        displayDialogMethod(title, message, defaultOption, option0, option1, option2, windowWidth,
                windowCloseOption, complete, renderContent, renderButtons, init);
    }

    /// <summary>
    /// Initialize GUIStyles used by this dialog.
    /// </summary>
    void InitializeStyles() {
        if (DefaultLabelStyle == null) {
            DefaultLabelStyle = new GUIStyle(EditorStyles.label);
            DefaultLabelStyle.wordWrap = true;
        }

        if (DefaultTitleStyle == null) {
            DefaultTitleStyle = new GUIStyle(EditorStyles.largeLabel);
            DefaultTitleStyle.fontStyle = FontStyle.Bold;
            DefaultTitleStyle.wordWrap = true;
        }

        if (DefaultMessageStyle == null) {
            DefaultMessageStyle = new GUIStyle(EditorStyles.boldLabel);
            DefaultMessageStyle.wordWrap = true;
        }

    }

    /// <summary>
    /// Render the dialog according to the context.
    /// </summary>
    void OnGUI() {
        // Close the window if Option0String is empty.
        // After Unity reload assemblies, the EditorWindow will remain open but all the content
        // in the dialog will be cleared because dialogContext is not serializable. Therefore,
        // close the dialog after assembly reload. Close in the next editor frame or it may
        // generate error message like "OpenGL Context became invalid during rendering".
        // This is for Unity 5.
        if (String.IsNullOrEmpty(dialogContext.Option0String) && !terminating) {
            terminating = true;
            RunOnMainThread.Run(() => {
                Close();
            }, runNow: false);
        }

        InitializeStyles();

        Rect rect = EditorGUILayout.BeginVertical();

        if (!String.IsNullOrEmpty(dialogContext.Title)) {
            GUILayout.Label(dialogContext.Title, EditorStyles.boldLabel);
            EditorGUILayout.Space();
        }

        // Render the dialog message.
        GUILayout.Label(dialogContext.Message, DefaultMessageStyle);
        EditorGUILayout.Space();

        // Render the additional context.
        if (dialogContext.RenderContentAction != null) {
            dialogContext.RenderContentAction(this);
            EditorGUILayout.Space();
        }

        EditorGUILayout.BeginHorizontal();
        // Render additional buttons before the option buttons.
        if (dialogContext.RenderButtonsAction != null) {
            dialogContext.RenderButtonsAction(this);
        }
        // Render option buttons.
        RenderOptionButtons();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        // Adjust the dialog window size according to the rendered content.
        // Rect returned by BeginVertical() can be zeroes for a couple of frames, therefore
        // ignoring resizing for those frames.
        if (rect.width != 0.0f && rect.height != 0.0f) {
            // Additional space at the bottom of the window.
            const float FILLER_WINDOWS_HEIGHT = 15.0f;
            float windowHeight = rect.height + FILLER_WINDOWS_HEIGHT;
            minSize = new Vector2(dialogContext.WindowWidth, windowHeight);
            maxSize = new Vector2(dialogContext.WindowWidth, windowHeight);
        }
    }
    /// <summary>
    /// Get a list of pairs of the text and enum of each non-empty option.
    /// </summary>
    /// <returns>A list of key-value pair where key is text and value is enum.</returns>
    private List<KeyValuePair<string, Option>> GetOptionList() {
        List<KeyValuePair<string, Option>> options = new List<KeyValuePair<string, Option>>();

        // Order the buttons similar to Unity dialog.
        // [ Option 1 (Cancel) ] [ Option 2 (Alt) ] [ Option 0 (Ok) ]
        if (!String.IsNullOrEmpty (dialogContext.Option1String)) {
            options.Add(new KeyValuePair<string, Option>(
                    dialogContext.Option1String, Option.Selected1));
        }
        if (!String.IsNullOrEmpty (dialogContext.Option2String)) {
            options.Add(new KeyValuePair<string, Option>(
                    dialogContext.Option2String, Option.Selected2));
        }
        if (!String.IsNullOrEmpty (dialogContext.Option0String)) {
            options.Add(new KeyValuePair<string, Option>(
                    dialogContext.Option0String, Option.Selected0));
        }
        return options;
    }

    /// <summary>
    /// Render all options with non-empty text.
    /// </summary>
    private void RenderOptionButtons () {
        Option selected = Option.SelectedNone;

        // Render options with non-empty text.
        var options = GetOptionList();

        // Space the button similar to Unity dialog. Ex.
        // | [ Option 1 (Cancel) ]                         [ Option 2 (Alt) ] [ Option 0 (Ok) ] |
        // |                                            [ Option 1 (Cancel) ] [ Option 0 (Ok) ] |
        // |                                                                  [ Option 0 (Ok) ] |
        List<int> spacesBeforeButtons = new List<int>(options.Count == 3 ?
                new int[3] { 0, 2, 0 } :
                new int[3] { 2, 0, 0 } );

        for (int i = 0; i < options.Count; ++i) {
            // Place spaces before the button.
            if (i < spacesBeforeButtons.Count ) {
                for (int j = 0; j < spacesBeforeButtons[i]; ++j) {
                    EditorGUILayout.Space();
                }
            }

            var pair = options [i];
            if (GUILayout.Button(pair.Key)) {
                selected = pair.Value;
            }
        }

        if (selected != Option.SelectedNone) {
            selectedOption = selected;
            if (dialogContext.CompleteAction != null) {
                dialogContext.CompleteAction(selectedOption);
            }
            Close();
        }
    }

    // Function called when the dialog window is closed.
    void OnDestroy() {
        // If no selection was made when the dialog is closed, trigger complete callback with
        // WindowCloseOption.
        if (dialogContext.WindowCloseOption != Option.SelectedNone &&
            selectedOption == Option.SelectedNone &&
            dialogContext.CompleteAction != null) {
            selectedOption = dialogContext.WindowCloseOption;
            dialogContext.CompleteAction(selectedOption);
        }

        // Complete the current dialog and display the next one if there is any in the queue.
        dialogJobQueue.Complete();
    }

    // Function called when the dialog window is enabled.
    void OnEnable() {
        VersionHandler.RegisterBeforeAssemblyReloadEvent(OnBeforeAssemblyReload);
    }

    // Function called when the dialog window is disabled.
    void OnDisable() {
        VersionHandler.UnregisterBeforeAssemblyReloadEvent(OnBeforeAssemblyReload);
    }

    // Function called before assemblies will be reloaded.
    private void OnBeforeAssemblyReload() {
        // Close the window before assembly will be reloaded.
        // After Unity reload assemblies, the EditorWindow will remain open but all the content
        // in the dialog will be cleared because all Action are not serialiable.  Therefore,
        // close the dialog before assembly reload.
        // Note that this only works from Unity 2017 since any version below does not have
        // the event API.
        Close();
    }
}  // class NonBlockingDialog
}  // namespace Google

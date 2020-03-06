using System.Runtime.Remoting.Messaging;
using UnityEngine;

namespace GooglePlayServices {
    using System;
    using UnityEditor;

    /// <summary>
    /// A fluid wrapper around the EditorUtility.DisplayDialogue
    /// interface.
    /// </summary>
    public class AlertModal {
        private const string DEFAULT_EMPTY = "";
        private const string DEFAULT_OK = "Yes";
        private const string DEFAULT_CANCEL = "No";
        private static Action DefaultEmptyAction = () => { };

        public class LabeledAction {
            public string Label { get; set; }
            public Action DelegateAction { get; set; }
        }

        /// <summary>
        /// Add a title to your Dialog box
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Add a message to your Dialog box
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The text and action to associate with the "ok" button.
        /// </summary>
        public LabeledAction Ok { get; set; }

        /// <summary>
        /// The text and action to associate with the "cancel" button.
        /// </summary>
        public LabeledAction Cancel { get; set; }

        /// <summary>
        /// The text and action to associate with the "alt" button.
        /// If this property is not specified, a two button display
        /// will be used.
        /// </summary>
        public LabeledAction Alt { get; set; }

        /// <summary>
        /// Constructor for the DialogBuilder sets defaults
        /// for required fields.
        /// </summary>
        public AlertModal() {
            Title = DEFAULT_EMPTY;
            Message = DEFAULT_EMPTY;
            Ok = new LabeledAction {
                Label = DEFAULT_OK,
                DelegateAction = DefaultEmptyAction
            };
            Cancel = new LabeledAction {
                Label = DEFAULT_CANCEL,
                DelegateAction = DefaultEmptyAction
            };
        }

        /// <summary>
        /// Display the window for the user's input. If no "alt" button is
        /// specified, display a normal DisplayDialog, otherwise use a
        /// DisplayDialogComplex
        /// </summary>
        public void Display() {
            if (Alt == null) {
                DisplaySimple();
            }
            else {
                DisplayComplex();
            }
        }

        /// <summary>
        /// Display a ComplexDialog with title, message,
        /// and 3 buttons - ok, cancel, and alt.
        /// </summary>
        private void DisplayComplex() {
            int option = EditorUtility.DisplayDialogComplex(Title, Message, Ok.Label,
                Cancel.Label, Alt.Label);

            switch (option) {
                // Ok option (perform action in the affirmative)
                case 0:
                    Ok.DelegateAction();
                    break;
                // Cancel option (whatever the negative is)
                case 1:
                    Cancel.DelegateAction();
                    break;
                // Alt option (whatever the third option you intended is)
                case 2:
                    Alt.DelegateAction();
                    break;
            }
        }

        /// <summary>
        /// Display a simple Dialog with a title, message, and
        /// two buttons - ok and cancel.
        /// </summary>
        private void DisplaySimple() {
            bool option = EditorUtility.DisplayDialog(Title, Message, Ok.Label, Cancel.Label);

            if (option) {
                Ok.DelegateAction();
            }
            else {
                Cancel.DelegateAction();
            }
        }
    }
}
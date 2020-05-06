// <copyright file="MultiSelectWindow.cs" company="Google Inc.">
// Copyright (C) 2019 Google Inc. All Rights Reserved.
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
    /// Window that displays a list of optionally selected items.
    /// </summary>
    public class MultiSelectWindow : EditorWindow {

        /// <summary>
        /// Position of the window.
        /// </summary>
        private Vector2 scrollPosition = new Vector2(0, 0);

        /// <summary>
        /// Index of each item to select mapped to the sorted set of items to display.
        /// </summary>
        private List<KeyValuePair<int, KeyValuePair<string, string>>> sortedItems;

        /// <summary>
        /// Items to display for selection, the Key of each item is stored in the selection
        /// the Value is rendered in the view.
        /// </summary>
        public List<KeyValuePair<string, string>> AvailableItems { get; set; }

        /// <summary>
        /// Set of items that have been selected.
        /// </summary>
        public HashSet<string> SelectedItems { get; set; }

        /// <summary>
        /// Caption for the top of the window.
        /// </summary>
        public string Caption { get; set; }

        /// <summary>
        /// Caption for the button to apply the selection.
        /// </summary>
        public string ApplyLabel { get; set; }

        /// <summary>
        /// Action to execute when the selection is applied.
        /// </summary>
        public Action OnApply;

        /// <summary>
        /// Caption for the button to cancel the selection.
        /// </summary>
        public string CancelLabel { get; set; }

        /// <summary>
        /// Action to execute when the selection is canceled.
        /// </summary>
        public Action OnCancel;

        /// <summary>
        /// Delegate that allows the rendering of each item to be customized.
        /// </summary>
        /// <param name="item">Item being rendered.</param>
        public delegate void RenderItemDelegate(KeyValuePair<string, string> item);

        /// <summary>
        /// Delegate which can be used to customize item rendering.
        /// </summary>
        public RenderItemDelegate RenderItem;

        /// <summary>
        /// Action to render additional contents after the listed items.
        /// </summary>
        public Action RenderAfterItems;

        /// <summary>
        /// Action to render additional items in the button area of the window before the
        /// cancel and apply buttons.
        /// </summary>
        public Action RenderBeforeCancelApply;

        /// <summary>
        /// Styles for unselected items in the list.
        /// </summary>
        private GUIStyle[] unselectedItemStyles;

        /// <summary>
        /// Styles for selected items in the list.
        /// </summary>
        private GUIStyle[] selectedItemStyles;

        /// <summary>
        /// Style for wrapped labels.
        /// </summary>
        private GUIStyle wrappedLabel;

        /// <summary>
        /// Initialize the window.
        /// </summary>
        protected MultiSelectWindow() {
            Initialize();
        }

        /// <summary>
        /// Reset the window to its default state.
        /// </summary>
        public virtual void Initialize() {
            AvailableItems = new List<KeyValuePair<string, string>>();
            InitializeSortedItems();
            SelectedItems = new HashSet<string>();
            ApplyLabel = "Apply";
            CancelLabel = "Cancel";
            scrollPosition = new Vector2(0, 0);
            minSize = new Vector2(300, 200);
            unselectedItemStyles = null;
            selectedItemStyles = null;
            wrappedLabel = null;
        }

        /// <summary>
        /// Select all items.
        /// </summary>
        public virtual void SelectAll() {
            if (AvailableItems != null) {
                SelectedItems = new HashSet<string>();
                foreach (var item in AvailableItems) SelectedItems.Add(item.Key);
            } else {
                SelectNone();
            }
        }

        /// <summary>
        /// Select no items.
        /// </summary>
        public virtual void SelectNone() {
            SelectedItems = new HashSet<string>();
        }

        /// <summary>
        /// Initialize the sortedItems array from the available items.
        /// </summary>
        private void InitializeSortedItems() {
            sortedItems = new List<KeyValuePair<int, KeyValuePair<string, string>>>();
            if (AvailableItems != null) {
                for (int i = 0; i < AvailableItems.Count; ++i) {
                    sortedItems.Add(new KeyValuePair<int, KeyValuePair<string, string>>(
                        i, new KeyValuePair<string, string>(AvailableItems[i].Key,
                                                            AvailableItems[i].Value)));
                }
            }
        }

        /// <summary>
        /// Sort the set of items.
        /// </summary>
        /// <param name="direction">1 to sort ascending, -1 to sort descending.</param>
        public virtual void Sort(int direction) {
            if (AvailableItems != null) {
                InitializeSortedItems();
                sortedItems.Sort((lhs, rhs) => {
                        return direction > 0 ?
                            String.Compare(lhs.Value.Value, rhs.Value.Value) :
                            String.Compare(rhs.Value.Value, lhs.Value.Value);
                    });
            }
        }

        /// <summary>
        /// Lazily configure the styles used to draw items in the window.
        /// Default styles in some versions of Unity are not accessible until OnGUI().
        /// </summary>
        protected virtual void InitializeStyles() {
            // Configure unselected styles with alternating normal states.
            if (unselectedItemStyles == null) {
                unselectedItemStyles = new GUIStyle[2];
                unselectedItemStyles[0] = EditorStyles.label;
                unselectedItemStyles[1] = EditorStyles.textField;
            }
            if (selectedItemStyles == null && unselectedItemStyles != null) {
                // Configure selected styles by overriding the font style.
                selectedItemStyles = new GUIStyle[unselectedItemStyles.Length];
                for (int i = 0; i < unselectedItemStyles.Length; ++i) {
                    var style = new GUIStyle(unselectedItemStyles[i]);
                    style.font = EditorStyles.boldLabel.font;
                    selectedItemStyles[i] = style;
                }
            }
            if (wrappedLabel == null) {
                wrappedLabel = new GUIStyle(EditorStyles.label);
                wrappedLabel.wordWrap = true;
            }
            if (AvailableItems != null && sortedItems.Count != AvailableItems.Count) {
                InitializeSortedItems();
            }
        }

        /// <summary>
        /// Draw the window.
        /// </summary>
        protected virtual void OnGUI() {
            InitializeStyles();
            if (!String.IsNullOrEmpty(Caption)) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(Caption, wrappedLabel);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginVertical(EditorStyles.textField);
                EditorGUILayout.Space();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.BeginVertical();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            int displayIndex = 0;
            foreach (var indexAndItem in sortedItems) {
                var item = indexAndItem.Value.Key;
                var display = indexAndItem.Value.Value;
                bool selected = SelectedItems.Contains(item);
                EditorGUILayout.BeginHorizontal(
                    selected ? selectedItemStyles[displayIndex % selectedItemStyles.Length] :
                        unselectedItemStyles[displayIndex % unselectedItemStyles.Length]);
                bool currentlySelected = EditorGUILayout.ToggleLeft(display, selected);
                if (currentlySelected != selected) {
                    if (currentlySelected) {
                        SelectedItems.Add(item);
                    } else {
                        SelectedItems.Remove(item);
                    }
                }
                if (RenderItem != null) RenderItem(indexAndItem.Value);
                EditorGUILayout.EndHorizontal();
                displayIndex++;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            if (RenderAfterItems != null) {
                EditorGUILayout.BeginVertical(EditorStyles.textField);
                EditorGUILayout.Space();
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                RenderAfterItems();
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All")) SelectAll();
            if (GUILayout.Button("None")) SelectNone();
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            if (RenderBeforeCancelApply != null) RenderBeforeCancelApply();
            bool cancel = GUILayout.Button(CancelLabel);
            bool apply = GUILayout.Button(ApplyLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            if (cancel || apply) {
                if (cancel && OnCancel != null) OnCancel();
                if (apply && OnApply != null) OnApply();
                Close();
            }
        }

        /// <summary>
        /// Get the existing multi-select window or create a new one.
        /// </summary>
        /// <param name="title">Title to display on the window.</param>
        /// <returns>Reference to this class</returns>
        [Obsolete("This method deprecated. Please use CreateMultiSelectWindow<T>() instead.")]
        public static MultiSelectWindow CreateMultiSelectWindow(string title) {
            MultiSelectWindow window = (MultiSelectWindow)EditorWindow.GetWindow(
                typeof(MultiSelectWindow), true, title, true);
            window.Initialize();
            return window;
        }

        /// <summary>
        /// Get the existing multi-select window or create a new one.
        /// To create an unique MultiSelectWindow, pass a type derived from MultiSelectWindow
        /// as the type parameter.
        /// </summary>
        /// <typeparam name="T">A type that inherits from the MultiSelectWindow.</typeparam>
        /// <param name="title">Title to display on the window.</param>
        /// <returns>Reference to this class</returns>
        public static T CreateMultiSelectWindow<T>(string title) where T : MultiSelectWindow {
            T window = (T)EditorWindow.GetWindow(typeof(T), true, title, true);
            window.Initialize();
            return window;
        }
    }
}

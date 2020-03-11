// <copyright file="XmlUtilities.cs" company="Google Inc.">
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
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Xml;

    internal class XmlUtilities {
        /// <summary>
        /// Parse an element in a XML file.
        /// </summary>
        /// <param name="reader">XML file reader.</param>
        /// <param name="elementName">Current element.</param>
        /// <param name="isStart">Whether this is the start of an element.</param>
        /// <param name="parentElementName">Name of the parent element.  If no parent element is
        /// present this will be an empty string.</param>
        /// <param name="elementNameStack">Stack of element names excluding the current element
        /// name. Returning true from this method pushes the current element name onto the
        /// stack.</param>
        /// <returns>true if the element is parsed successfully and should be pushed onto the
        /// stack, false otherwise. </returns>
        internal delegate bool ParseElement(XmlTextReader reader, string elementName,
                                            bool isStart, string parentElementName,
                                            List<string> elementNameStack);

        /// <summary>
        /// Used to advance through nodes in a XmlTextReader.
        /// </summary>
        private class Reader {

            /// <summary>
            /// Whether a file is still being read.
            /// </summary>
            public bool Reading { private set; get; }

            /// <summary>
            /// Determine whether the XmlTextReader has moved ahead of the position this object
            /// last observed.
            /// </summary>
            public bool XmlReaderIsAhead {
                get {
                    return lineNumber != reader.LineNumber ||
                        linePosition != reader.LinePosition;
                }
            }

            private XmlTextReader reader;
            private int lineNumber = -1;
            private int linePosition = -1;

            public Reader(XmlTextReader xmlReader) {
                reader = xmlReader;
                Reading = reader.Read();
                lineNumber = reader.LineNumber;
                linePosition = reader.LinePosition;
            }

            /// <summary>
            /// If no data has been read since the last call to this method, read the next node
            /// from the XML file.
            /// </summary>
            /// <returns>true if another node was read, false otherwise.</returns>
            public bool Read() {
                bool readData = false;
                if (Reading && !XmlReaderIsAhead) {
                    Reading = reader.Read();
                    readData = true;
                }
                lineNumber = reader.LineNumber;
                linePosition = reader.LinePosition;
                return readData;
            }
        }

        /// <summary>
        /// Utility method to simplify parsing a XML file.
        /// </summary>
        /// <param name="filename">Name of the file to read.</param>
        /// <param name="logger">Log messages associated with parsing the file.</param>
        /// <param name="parseElement">Delegate which attempts to parse an element in the file.
        /// </param>
        /// <returns>true if the file is successfully parsed, false otherwise.</returns>
        internal static bool ParseXmlTextFileElements(
                string filename, Logger logger, ParseElement parseElement) {
            if (!File.Exists(filename)) return false;
            bool successful = true;
            try {
                using (var xmlReader = new XmlTextReader(new StreamReader(filename))) {
                    var elementNameStack = new List<string>();
                    System.Func<string> getParentElement = () => elementNameStack.Count > 0 ?
                        elementNameStack[0] : "";
                    var reader = new Reader(xmlReader);
                    while (reader.Reading) {
                        var elementName = xmlReader.Name;
                        var parentElementName = getParentElement();
                        if (xmlReader.NodeType == XmlNodeType.Element) {
                            bool parsedElement = parseElement(xmlReader, elementName, true,
                                                              parentElementName, elementNameStack);
                            if (parsedElement) {
                                elementNameStack.Insert(0, elementName);
                            } else {
                                successful = false;
                            }

                            // If the parse delegate read data, move to the next XML node.
                            if (reader.XmlReaderIsAhead) {
                                reader.Read();
                                continue;
                            }
                        }
                        // <tag></tag> results in XmlNodeType.EndElement and
                        // <tag/> results in XmlNodeType.Element and IsEmptyElement = true.
                        if ((xmlReader.NodeType == XmlNodeType.EndElement ||
                             (xmlReader.NodeType == XmlNodeType.Element &&
                              xmlReader.IsEmptyElement)) &&
                            !String.IsNullOrEmpty(parentElementName)) {
                            if (elementNameStack[0] == elementName) {
                                elementNameStack.RemoveAt(0);
                            } else {
                                // If the stack is out of sync, the file must be poorly formatted
                                // so clear the stack.
                                elementNameStack.Clear();
                            }
                            if (!parseElement(xmlReader, elementName, false,
                                              getParentElement(), elementNameStack)) {
                                successful = false;
                            }
                        }
                        reader.Read();
                    }
                }
            } catch (XmlException exception) {
                logger.Log(String.Format("Failed while parsing XML file {0}\n{1}\n",
                                         filename, exception.ToString()), level: LogLevel.Error);
                return false;
            }
            return successful;
        }
    }
}

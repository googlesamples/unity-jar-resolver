// <copyright file="AndroidAbi.cs" company="Google Inc.">
// Copyright (C) 2018 Google Inc. All Rights Reserved.
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
using System.Reflection;

namespace GooglePlayServices {

/// <summary>
/// Provides access to Android ABI settings across different Unity versions.
/// </summary>
internal class AndroidAbis {

    /// <summary>
    /// Set of selected ABIs.
    /// </summary>
    private HashSet<string> abis;

    /// <summary>
    /// Create the default ABI set.
    /// </summary>
    public AndroidAbis() {
        abis = new HashSet<string>(Supported);
    }

    /// <summary>
    /// Create a selected set of ABIs from a set.
    /// </summary>
    /// <param name="abisSet">Set of ABI strings.</param>
    public AndroidAbis(IEnumerable<string> abisSet) {
        abis = new HashSet<string>(abisSet);
    }

    /// <summary>
    /// Create a set of ABIs from a comma separated set of ABI strings.
    /// </summary>
    /// <param name="abisSet">Set of ABI strings.</param>
    public AndroidAbis(string abiString) {
        if (String.IsNullOrEmpty(abiString)) {
            abis = new HashSet<string>(Supported);
        } else {
            abis = new HashSet<string>();
            foreach (var abi in abiString.Split(new [] { ',' })) {
                abis.Add(abi.Trim());
            }
        }
    }

    /// <summary>
    /// Convert the set of ABIs to a string.
    /// </summary>
    public override string ToString() {
        var abiList = (new List<string>(abis));
        abiList.Sort();
        return String.Join(",", abiList.ToArray());
    }

    /// <summary>
    /// Get the set of ABIs as a set.
    /// </summary>
    /// <returns>Set of ABIs.</returns>
    public HashSet<string> ToSet() { return new HashSet<string>(abis); }

    /// <summary>
    /// Compare with this object.
    /// </summary>
    /// <param name="obj">Object to compare with.</param>
    /// <returns>true if both objects have the same contents, false otherwise.</returns>
    public override bool Equals(System.Object obj) {
        var otherObj = obj as AndroidAbis;
        return otherObj != null && abis.SetEquals(otherObj.abis);
    }

    /// <summary>
    /// Generate a hash of this object.
    /// </summary>
    /// <returns>Hash of this object.</returns>
    public override int GetHashCode() { return abis.GetHashCode(); }

    /// <summary>
    /// Get the supported set of Android ABIs for the current Unity version.
    /// The dictionary maps the official Android ABI name (i.e the directory name looked up by the
    /// operating system) to the UnityEditor.AndroidTargetDevice (Unity 5.x & 2017.x) or
    // UnityEditor.AndroidArchitecture (Unity 2018.x) enumeration value name.
    /// </summary>
    private static Dictionary<string, string> SupportedAbiToAbiEnumValue {
        get {
            float unityVersion = Google.VersionHandler.GetUnityVersionMajorMinor();
            if (unityVersion >= 2018.0f) {
                return new Dictionary<string, string>() {
                    {"armeabi-v7a", "ARMv7"},
                    {"arm64-v8a", "ARM64"},
                    {"x86", "X86"},
                };
            } else if (unityVersion >= 5.0f) {
                return new Dictionary<string, string>() {
                    {"armeabi-v7a", "ARMv7"},
                    {"x86", "x86"},
                };
            }
            return new Dictionary<string, string>() {
                {"armeabi-v7a", ""},
                {"x86", ""},
            };
        }
    }

    /// <summary>
    /// Get the supported set of Android ABIs for the current Unity version.
    /// Each ABI string is an official Android ABI name and corresponds to the supported ABI
    /// directory name under the "jni" folder in an APK or AAR.
    /// </summary>
    public static IEnumerable<string> Supported {
        get { return SupportedAbiToAbiEnumValue.Keys; }
    }

    /// <summary>
    /// Get the supported set of all possible Android ABIs, ignoring what Unity supports.
    /// </summary>
    public static IEnumerable<string> AllSupported {
        get {
            return new [] {
                "armeabi", "armeabi-v7a", "arm64-v8a", "x86", "x86_64", "mips", "mips64"
            };
        }
    }

    /// <summary>
    /// Get the Android ABI type in the UnityEditor.PlayerSettings.Android structure and
    /// the name of the enumeration type used by the member for the current Unity version.
    /// </summary>
    private static KeyValuePair<PropertyInfo, Type> AbiPropertyAndEnumType {
        get {
            float unityVersion = Google.VersionHandler.GetUnityVersionMajorMinor();
            if (unityVersion >= 2018.0f) {
                return new KeyValuePair<PropertyInfo, Type>(
                    typeof(UnityEditor.PlayerSettings.Android).GetProperty("targetArchitectures"),
                    Google.VersionHandler.FindClass("UnityEditor",
                                                    "UnityEditor.AndroidArchitecture"));
            } else if (unityVersion >= 5.0f) {
                return new KeyValuePair<PropertyInfo, Type>(
                    typeof(UnityEditor.PlayerSettings.Android).GetProperty("targetDevice"),
                    Google.VersionHandler.FindClass("UnityEditor",
                                                    "UnityEditor.AndroidTargetDevice"));
            }
            return new KeyValuePair<PropertyInfo, Type>(null, null);
        }
    }

    /// <summary>
    /// Convert an enum value object to a ulong.
    /// </summary>
    /// <param name="enumValueObject">Enum object to convert.</param>
    private static ulong EnumValueObjectToULong(object enumValueObject) {
        /// Flags enum values can't be cast directly to an integral type, however it is possible to
        /// print the value as an integer string so convert to a string and then parse as an int.
        /// Enums are considered unsigned by the formatter, so if an enum is defined as -1 it will
        /// be formatted as UInt32.MaxValue, i.e. 4294967295.
        return UInt64.Parse(String.Format("{0:D}", enumValueObject));
    }

    /// <summary>
    /// Convert an enum value string to a ulong.
    /// </summary>
    /// <param name="enumType">Enum type to use to parse the string.</param>
    /// <param name="enumValueString">Enum string to convert.</param>
    private static ulong EnumValueStringToULong(Type enumType, string enumValueString) {
        return EnumValueObjectToULong(Enum.Parse(enumType, enumValueString));
    }

    /// <summary>
    /// Get / set the target device ABI (Unity >= 5.0.x)
    /// Unity >= 2018.x supports armeabi-v7a, arm64-v8a, x86 & fat (i.e armeabi-v7a, arm64, x86)
    /// Unity >= 5.0.x & <= 2017.x only support armeabi-v7a, x86 & fat (i.e armeabi-v7a & x86)
    /// </summary>
    public static AndroidAbis Current {
        set {
            float unityVersion = Google.VersionHandler.GetUnityVersionMajorMinor();
            var propertyAndType = AbiPropertyAndEnumType;
            var property = propertyAndType.Key;
            var enumType = propertyAndType.Value;
            var supportedAbis = SupportedAbiToAbiEnumValue;
            var abiSet = value.ToSet();
            if (unityVersion >= 2018.0f) {
                // Convert selected ABIs to a flags enum.
                ulong enumValue = 0;
                foreach (var abi in supportedAbis) {
                    if (abiSet.Contains(abi.Key)) {
                        // It's not possible to trivially cast a flag enum value to an int
                        // so perform the conversion via a string.
                        ulong enumValueInt = EnumValueStringToULong(enumType, abi.Value);
                        enumValue |= enumValueInt;
                    }
                }
                property.SetValue(null, Enum.ToObject(enumType, enumValue), null);
            } else if (unityVersion >= 5.0f) {
                // Filter the requested ABIs by those supported.
                abiSet.IntersectWith(supportedAbis.Keys);
                if (abiSet.Count == 0) return;
                // If more than one ABI is specified, select all.
                property.SetValue(
                    null,
                    Enum.ToObject(
                        enumType,
                        EnumValueStringToULong(
                            enumType,
                            abiSet.Count > 1 ? "FAT" :
                                supportedAbis[(new List<string>(abiSet))[0]])), null);
            } else {
                UnityEngine.Debug.LogWarning(
                    String.Format("Unity {0} does not support targeting a " +
                                  "specific set of Android ABIs.", unityVersion));
            }
        }

        get {
            float unityVersion = Google.VersionHandler.GetUnityVersionMajorMinor();
            var propertyAndType = AbiPropertyAndEnumType;
            var property = propertyAndType.Key;
            var enumType = propertyAndType.Value;
            var supportedAbis = SupportedAbiToAbiEnumValue;
            var selectedAbis = new HashSet<string>();
            if (unityVersion >= 2018.0f) {
                // Convert flags enum value to a set of ABI names.
                ulong enumValueInt = EnumValueObjectToULong(property.GetValue(null, null));
                foreach (var abi in supportedAbis) {
                    if ((enumValueInt & EnumValueStringToULong(enumType, abi.Value)) != 0) {
                        selectedAbis.Add(abi.Key);
                    }
                }
            } else if (unityVersion >= 5.0f) {
                // Convert enum value to an ABI name.
                var abiName = Enum.GetName(enumType, property.GetValue(null, null));
                foreach (var abi in supportedAbis) {
                    if (abi.Value == abiName) {
                        selectedAbis.Add(abi.Key);
                        break;
                    }
                }
            }
            return selectedAbis.Count == 0 ? new AndroidAbis() : new AndroidAbis(selectedAbis);
        }
    }

    /// <summary>
    /// Get / set the target device ABIs using a string.
    /// </summary>
    public static string CurrentString {
        set { Current = new AndroidAbis(value); }
        get { return Current.ToString(); }
    }
}

}



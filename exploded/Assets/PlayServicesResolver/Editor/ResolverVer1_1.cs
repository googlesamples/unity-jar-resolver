// <copyright file="ResolverVer1_1.cs" company="Google Inc.">
// Copyright (C) 2015 Google Inc. All Rights Reserved.
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
#if UNITY_ANDROID

namespace GooglePlayServices
{
    using UnityEditor;
    using System.Collections.Generic;
    using Google.JarResolver;
    using System.IO;
    using System.Xml;

    [InitializeOnLoad]
    public class ResolverVer1_1 : DefaultResolver
    {
        // Caches data associated with an aar so that it doesn't need to be queried to determine
        // whether it should be expanded / exploded if it hasn't changed.
        private class AarExplodeData
        {
            // Time the file was modified the last time it was inspected.
            public System.DateTime modificationTime;
            // Whether the AAR file should be expanded / exploded.
            public bool explode;
        }

        private Dictionary<string, AarExplodeData> aarExplodeData =
            new Dictionary<string, AarExplodeData>();
        // File used to to serialize aarExplodeData.  This is required as Unity will reload classes
        // in the editor when C# files are modified.
        private string aarExplodeDataFile = Path.Combine("ProjectSettings",
                                                         "GoogleAarExplodeCache.xml");

        private const int MajorVersion = 1;
        private const int MinorVersion = 1;
        private const int PointVersion = 0;

        static ResolverVer1_1()
        {
            ResolverVer1_1 resolver = new ResolverVer1_1();
            resolver.LoadAarExplodeCache();
            PlayServicesResolver.RegisterResolver(resolver);
        }

        /// <summary>
        /// Load data cached in aarExplodeDataFile into aarExplodeData.
        /// </summary>
        private void LoadAarExplodeCache()
        {
            if (!File.Exists(aarExplodeDataFile)) return;

            XmlTextReader reader = new XmlTextReader(new StreamReader(aarExplodeDataFile));
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "aars")
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.Name == "explodeData")
                        {
                            string aar = "";
                            AarExplodeData aarData = new AarExplodeData();
                            do
                            {
                                if (!reader.Read()) break;
                                if (reader.NodeType == XmlNodeType.Element)
                                {
                                    string elementName = reader.Name;
                                    if (reader.Read() && reader.NodeType == XmlNodeType.Text)
                                    {
                                        if (elementName == "aar")
                                        {
                                            aar = reader.ReadContentAsString();
                                        }
                                        else if (elementName == "modificationTime")
                                        {
                                            aarData.modificationTime =
                                                reader.ReadContentAsDateTime();
                                        }
                                        else if (elementName == "explode")
                                        {
                                            aarData.explode = reader.ReadContentAsBoolean();
                                        }
                                    }
                                }
                            } while (!(reader.Name == "explodeData" &&
                                       reader.NodeType == XmlNodeType.EndElement));
                            if (aar != "") aarExplodeData[aar] = aarData;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Save data from aarExplodeData into aarExplodeDataFile.
        /// </summary>
        private void SaveAarExplodeCache()
        {
            if (File.Exists(aarExplodeDataFile))
            {
                File.Delete(aarExplodeDataFile);
            }
            XmlTextWriter writer = new XmlTextWriter(new StreamWriter(aarExplodeDataFile));
            writer.WriteStartElement("aars");
            foreach (KeyValuePair<string, AarExplodeData> kv in aarExplodeData)
            {
                writer.WriteStartElement("explodeData");
                writer.WriteStartElement("aar");
                writer.WriteValue(kv.Key);
                writer.WriteEndElement();
                writer.WriteStartElement("modificationTime");
                writer.WriteValue(kv.Value.modificationTime);
                writer.WriteEndElement();
                writer.WriteStartElement("explode");
                writer.WriteValue(kv.Value.explode);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.Flush();
            writer.Close();
        }

        #region IResolver implementation

        /// <summary>
        /// Version of the resolver. - 1.1.0
        /// </summary>
        /// <remarks>The resolver with the greatest version is used when resolving.
        /// The value of the verison is calcuated using MakeVersion in DefaultResolver</remarks>
        /// <seealso cref="DefaultResolver.MakeVersionNumber"></seealso>
        public override int Version()
        {
            return MakeVersionNumber(MajorVersion, MinorVersion, PointVersion);
        }

        /// <summary>
        /// Perform the resolution and the exploding/cleanup as needed.
        /// </summary>
        public override void DoResolution(PlayServicesSupport svcSupport,
                                          string destinationDirectory,
                                          PlayServicesSupport.OverwriteConfirmation handleOverwriteConfirmation)
        {
            // Get the collection of dependencies that need to be copied.
            Dictionary<string, Dependency> deps =
                svcSupport.ResolveDependencies(true);

            // Copy the list
            svcSupport.CopyDependencies(deps,
                destinationDirectory,
                handleOverwriteConfirmation);

            // we want to look at all the .aars to decide to explode or not.
            // Some aars have variables in their AndroidManifest.xml file,
            // e.g. ${applicationId}.  Unity does not understand how to process
            // these, so we handle it here.
            ProcessAars(destinationDirectory);

            SaveAarExplodeCache();
        }

        #endregion

        /// <summary>
        /// Processes the aars.
        /// </summary>
        /// <remarks>Each aar copied is inspected and determined if it should be
        /// exploded into a directory or not. Unneeded exploded directories are
        /// removed.
        /// <para>
        /// Exploding is needed if the version of Unity is old, or if the artifact
        /// has been explicitly flagged for exploding.  This allows the subsequent
        /// processing of variables in the AndroidManifest.xml file which is not
        /// supported by the current versions of the manifest merging process that
        /// Unity uses.
        /// </para>
        /// <param name="dir">The directory to process.</param>
        void ProcessAars(string dir)
        {
            string[] files = Directory.GetFiles(dir, "*.aar");
            foreach (string f in files)
            {
                if (ShouldExplode(f))
                {
                    string exploded = ProcessAar(Path.GetFullPath(dir), f);
                    ReplaceVariables(exploded);
                }
                else
                {
                    string baseName = Path.GetFileNameWithoutExtension(f);
                    if (Directory.Exists(Path.Combine(dir, baseName)))
                    {
                        DeleteFully(Path.Combine(dir, baseName));
                    }
                }
            }
        }

        /// <summary>
        /// Determined whether an aar file should be exploded (extracted).
        ///
        /// This is required for some aars so that the Unity Jar Resolver can perform variable
        /// expansion on manifests in the package before they're merged by aapt.
        /// </summary>
        /// <returns><c>true</c>, if the aar should be exploded, <c>false</c> otherwise.</returns>
        /// <param name="aarFile">The aar file.</param>
        internal virtual bool ShouldExplode(string aarFile)
        {
            AarExplodeData aarData = new AarExplodeData();
            aarData.explode = !SupportsAarFiles;
            if (!aarData.explode)
            {
                AarExplodeData retrievedAarData = null;
                if (aarExplodeData.TryGetValue(aarFile, out retrievedAarData))
                {
                    System.DateTime modificationTime = File.GetLastWriteTime(aarFile);
                    if (modificationTime.CompareTo(aarData.modificationTime) <= 0)
                    {
                        aarData = retrievedAarData;
                    }
                }
            }
            if (!aarData.explode)
            {
                string temporaryDirectory = CreateTemporaryDirectory();
                if (temporaryDirectory == null) return false;
                string manifestFilename = "AndroidManifest.xml";
                try
                {
                    if (ExtractAar(aarFile, new string[] {manifestFilename},
                                   temporaryDirectory))
                    {
                        string manifestPath = Path.Combine(temporaryDirectory,
                                                           manifestFilename);
                        if (File.Exists(manifestPath))
                        {
                            string manifest = File.ReadAllText(manifestPath);
                            aarData.explode = manifest.IndexOf("${applicationId}") >= 0;
                        }
                        aarData.modificationTime = File.GetLastWriteTime(aarFile);
                    }
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.Log("Unable to examine AAR file " + aarFile + ", err: " + e);
                    throw e;
                }
                finally
                {
                    DefaultResolver.DeleteFully(temporaryDirectory);
                }
            }
            aarExplodeData[aarFile] = aarData;
            return aarData.explode;
        }

        /// <summary>
        /// Replaces the variables in the AndroidManifest file.
        /// </summary>
        /// <param name="exploded">Exploded.</param>
        void ReplaceVariables(string exploded)
        {
            string manifest = Path.Combine(exploded, "AndroidManifest.xml");
            if (File.Exists(manifest))
            {
                StreamReader sr = new StreamReader(manifest);
                string body = sr.ReadToEnd();
                sr.Close();

                body = body.Replace("${applicationId}", PlayerSettings.bundleIdentifier);

                using (var wr = new StreamWriter(manifest, false))
                {
                    wr.Write(body);
                }
            }
        }
    }
}
#endif

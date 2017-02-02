// <copyright file="Models.cs" company="Google Inc.">
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
namespace Google.PackageManager {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Xml.Serialization;

    /// <summary>
    /// Abstract base class for XML serializable model classes. Derived model
    /// classes get implementations of common methods for load and save of model
    /// data.
    /// </summary>
    public abstract class PackageManagerModel<T> {
        /// <summary>
        /// The xml model version. Used to detect and handle future model
        /// changes.
        /// </summary>
        [XmlElement("xmlModelVersion")]
        public string xmlModelVersion;

        // TODO: b/34936401 add xmlModelVersion validation.

        /// <summary>
        /// Deserializes a model from a provided stream containing XML data for the model.
        /// </summary>
        /// <returns>The from stream.</returns>
        /// <param name="reader">Reader.</param>
        public static T LoadFromStream(StreamReader reader) {
            return (T)((new XmlSerializer(typeof(T)).Deserialize(reader)));
        }

        /// <summary>
        /// Deserializes a model from a specified XML model file.
        /// </summary>
        /// <returns>The inflated model object. Will throw an exception if the
        /// file was not found.</returns>
        /// <param name="file">The XML file path to read from.</param>
        public static T LoadFromFile(string file) {
            return LoadFromStream(new StreamReader(file, Encoding.UTF8, true));
        }

        /// <summary>
        /// Builds model tree from string containing valid XML.
        /// </summary>
        /// <returns>The model built from the provided utf-8 string.</returns>
        /// <param name="xmlData">Xml data encoded in utf-8.</param>
        public static T LoadFromString(string xmlData) {
            return LoadFromStream(
                new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(xmlData))));
        }

        /// <summary>
        /// Serializes the model to an XML string.
        /// </summary>
        /// <returns>An XML formatted string representing the model state.
        /// </returns>
        public string SerializeToXMLString() {
            var serializer = new XmlSerializer(typeof(T));
            var memoryStream = new MemoryStream();
            var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8);
            serializer.Serialize(streamWriter, this);
            byte[] utf8EncodedXml = memoryStream.ToArray();
            return Encoding.UTF8.GetString(utf8EncodedXml, 0, utf8EncodedXml.Length);
        }
    }

    /// <summary>
    /// Abstract base class for XML serializable model classes. Derived model
    /// classes get implementations of common methods for asset labels and keys.
    /// </summary>
    public abstract class PackageManagerLabeledModel<T> : PackageManagerModel<T> {
        /// <summary>
        /// The group identifier based on Maven POM model.
        /// https://maven.apache.org/guides/mini/guide-naming-conventions.html
        /// </summary>
        [XmlElement("groupId")]
        public string groupId;

        /// <summary>
        /// The artifact identifier based on Maven POM model.
        /// https://maven.apache.org/guides/mini/guide-naming-conventions.html
        /// </summary>
        [XmlElement("artifactId")]
        public string artifactId;

        /// <summary>
        /// The version based on Maven POM model.
        /// https://maven.apache.org/guides/mini/guide-naming-conventions.html
        /// </summary>
        [XmlElement("version")]
        public string version;

        /// <summary>
        /// Meta information for the plugin encoded as a string.
        /// If groupId, artifactId or version are not set then this value will be null.
        /// </summary>
        /// <returns>The asset label.</returns>
        public string GenerateAssetLabel() {
            if (groupId == null || artifactId == null || version == null) {
                return null;
            }
            return string.Join(
                Constants.STRING_KEY_BINDER,
                new string[]{
                    Constants.GPM_LABEL_MARKER,
                    Constants.GPM_LABEL_KEY,
                    GenerateUniqueKey()
                });
        }

        /// <summary>
        /// Generates the unique key basd on the current groupId, artifactId and version.
        /// </summary>
        /// <returns>The unique key.</returns>
        public string GenerateUniqueKey() {
            if (groupId == null || artifactId == null || version == null) {
                throw new Exception(string.Format("Attempted to generate unique " +
                                                  "key on object {0} without setting " +
                                                  "object state. [groupId = {1}, artifactId = " +
                                                  "{2}, verison = {3}]", this, ""+groupId,
                                                  ""+artifactId, ""+version));
            }
            return string.Join(Constants.STRING_KEY_BINDER,
                               new string[] { groupId, artifactId, version });
        }
    }

    /// <summary>
    /// Registry model class contains details about registered plugins which are
    /// part of a specific registry instance.
    ///
    /// A registry is a URI addressable XML model that references a set of
    /// PackageManager compatable packaged plugins. A registry encapsulates the
    /// concept of a lookup group.
    /// </summary>
    [XmlRoot("registry")]
    public class Registry : PackageManagerLabeledModel<Registry> {
        /// <summary>
        /// When was this registry last updated in epoc time.
        /// </summary>
        [XmlElement("lastUpdated")]
        public long lastUpdated;
        /// <summary>
        /// The modules (packaged plugin references) contained within this registry.
        /// </summary>
        [XmlElement("modules")]
        public Modules modules = new Modules();
    }

    /// <summary>
    /// Modules is a container that holds the list of registered plugins. It is
    /// used to keep the XML model clean and readable.
    /// </summary>
    public class Modules : PackageManagerModel<Modules> {
        /// <summary>
        /// A module as represented in this model can be one of the following:
        /// 1) A groupId of a packaged plugin, if that packaged plugin is relative to the registry.
        ///    (meaning that it has a path that is a decendent of the registry Uri path)
        /// 2) An absolute Uri to a location of a packaged plugin's package-manifest.xml
        ///
        /// Note: It is permitted to mix representations in a registry model. Meaning you can have
        /// a mix of groupId representations and absolute Uri repesentations in the same registry.
        /// </summary>
        [XmlElement("module")]
        public List<string> module = new List<string>();
    }

    /// <summary>
    /// Plugin meta data associated with a plugin for use in package management.
    /// This model contains the bulk of the packaged plugin details used for
    /// managing plugin installation and deletion.
    /// </summary>
    [XmlRoot("metadata")]
    public class PluginMetaData : PackageManagerLabeledModel<PluginMetaData> {
        /// <summary>
        /// Format eg. 1.2.3
        /// </summary>
        [XmlElement("modelVersion")]
        public string modelVersion;

        /// <summary>
        /// What format the binary package is in. Will usually be "unitypackage".
        /// </summary>
        [XmlElement("packaging")]
        public string packaging;

        /// <summary>
        /// A container element that holds a list of all versions the plugin has.
        /// </summary>
        [XmlElement("versioning")]
        public Versioning versioning = new Versioning();

        /// <summary>
        /// The ISO 8601 date this plugin was last updated.
        /// </summary>
        [XmlElement("lastUpdated")]
        public long lastUpdated;

        /// <summary>
        /// UniqueKey format: groupId:artifactId:version
        /// </summary>
        /// <value>The unique key.</value>
        [XmlIgnore]
        public string UniqueKey {
            get {
                return GenerateUniqueKey();
            }
        }
    }

    /// <summary>
    /// Versioning is a container class for release information and other available
    /// versions of the plugin.
    /// </summary>
    public class Versioning : PackageManagerModel<Versioning> {
        /// <summary>
        /// The currently released version of the plugin package. This value is
        /// used to help select the best version to install in a project.
        /// </summary>
        [XmlElement("release")]
        public string release;

        /// <summary>
        /// The available and published versions of the packaged plugin.
        /// </summary>
        [XmlArray("versions")]
        [XmlArrayItem("version")]
        public HashSet<string> versions = new HashSet<string>();
    }

    /// <summary>
    /// A description of the packaged plugin in terms of a specific language code.
    ///
    /// TODO(krispy): add support for additional languages
    /// </summary>
    [XmlRoot("language")]
    public class Language : PackageManagerModel<Language> {
        /// <summary>
        /// The lang code is used to indicate the language used in the description.
        /// Currently only a value of 'en' is supported by the UI.
        /// </summary>
        [XmlAttribute("type")]
        public string langCode;

        /// <summary>
        /// The name of the plugin in a human readable form.
        /// </summary>
        [XmlElement("name")]
        public string name;

        /// <summary>
        /// A short description of the plugin. Usually a single marketing sentance.
        /// </summary>
        [XmlElement("short")]
        public string shortDesc;

        /// <summary>
        /// A long form description of the packaged plugin. Used to describe features
        /// and bug fixes.
        /// </summary>
        [XmlElement("full")]
        public string fullDesc;
    }

    /// <summary>
    /// Plugin description containing the text details about a plugin as will be
    /// presented to the user in the Package Manager UI.
    /// </summary>
    [XmlRoot("description")]
    public class PluginDescription : PackageManagerModel<PluginDescription> {
        /// <summary>
        /// The set of descriptions for this packaged plugin.
        /// </summary>
        [XmlArray("languages")]
        [XmlArrayItem("language")]
        public List<Language> languages = new List<Language>();
    }

    /// <summary>
    /// Package dependencies model representing the JarResolver dependencies a package plugin has.
    /// </summary>
    [XmlRoot("package-dependencies")]
    public class PackageDependencies : PackageManagerLabeledModel<PackageDependencies> {
        /// <summary>
        /// The root dependencies that the Unity plugin package has.
        /// </summary>
        [XmlArray("android-dependencies")]
        [XmlArrayItem("android-dependency")]
        public List<AndroidPackageDependency> androidDependencies =
            new List<AndroidPackageDependency>();
        /// <summary>
        /// The iOS Pod dependencies.
        /// </summary>
        [XmlArray("ios-pod-dependencies")]
        [XmlArrayItem("ios-pod-dependency")]
        public List<IOSPodDependency> iOSDependencies = new List<IOSPodDependency>();
    }

    /// <summary>
    /// IOS Package dependency model representing a specific iOS dependency.
    /// </summary>
    public class IOSPodDependency : PackageManagerModel<IOSPodDependency> {
        /// <summary>
        /// The name of the POD.
        /// </summary>
        [XmlElement("name")]
        public string name;
        /// <summary>
        /// The version of the POD.
        /// </summary>
        [XmlElement("version")]
        public string version;
    }

    /// <summary>
    /// Package dependency model representing a specific android dependency.
    /// </summary>
    public class AndroidPackageDependency : PackageManagerModel<AndroidPackageDependency> {
        /// <summary>
        /// The group identifier for the Android dependency. eg "com.google.android.gms"
        /// </summary>
        [XmlElement("group")]
        public string group;
        /// <summary>
        /// The artifact identifier for the Android dependency. eg. "play-services-ads"
        /// </summary>
        [XmlElement("artifact")]
        public string artifact;
        /// <summary>
        /// The flexible version identifier for the Android dependency. eg. "LATEST", "23.1+" etc.
        /// </summary>
        [XmlElement("version")]
        public string version;
        /// <summary>
        /// The arguments to support where to find details about the dependency.
        /// </summary>
        [XmlElement("args")]
        public DependencyArgument args = new DependencyArgument();
    }

    /// <summary>
    /// Dependency argument set containing sets of android packages and repositories
    /// </summary>
    public class DependencyArgument : PackageManagerModel<DependencyArgument> {
        /// <summary>
        /// Maps to the concept of PlayServicesSupport/Dependency packageIds.
        /// Eg. {"extra-google-m2repository","extra-android-m2repository"}
        /// </summary>
        [XmlArray("android-packages")]
        [XmlArrayItem("android-package")]
        public List<string> packageIds = new List<string>();
        /// <summary>
        /// Maps to the concept of PlayServicesSupport/Dependency repositories
        /// </summary>
        [XmlArray("repositories")]
        [XmlArrayItem("repository")]
        public List<string> repositories = new List<string>();
    }

    /// <summary>
    /// Project packages model used to represent a manifest of what packaged plugins have been added
    /// to the project. This model supports the ability to cleanly remove packaged plugins without
    /// relying on the JarResolver.
    /// </summary>
    [XmlRoot("gpm-project")]
    public class ProjectPackages : PackageManagerModel<ProjectPackages> {
        [XmlArray("clients")]
        [XmlArrayItem("client")]
        public List<ProjectClient> clients = new List<ProjectClient>();
    }

    /// <summary>
    /// Project client represents an installed packaged plugin with all known
    /// data associated with its installation into a project.
    /// </summary>
    public class ProjectClient : PackageManagerLabeledModel<ProjectClient> {
        /// <summary>
        /// The client dependencies declared in the gpm.dep.xml for the package
        /// </summary>
        [XmlElement("package-dependencies")]
        public PackageDependencies clientDependencies;
        /// <summary>
        /// The assets - all known that belong to this package
        /// </summary>
        [XmlArray("assets")]
        [XmlArrayItem("asset")]
        public List<string> assets = new List<string>();
        /// <summary>
        /// List of versionless asset names from resolution
        /// </summary>
        [XmlArray("resolved-dep-names")]
        [XmlArrayItem("dep-name")]
        public List<string> depNames = new List<string>();
        /// <summary>
        /// Has this package resolved its android deps?
        /// </summary>
        [XmlElement("android-resolved")]
        public bool resolvedForAndroid = false;
        /// <summary>
        /// Has this package resolved its ios deps?
        /// </summary>
        [XmlElement("ios-resolved")]
        public bool resolvedForIOS = false;

        [XmlIgnore]
        public string Name {
            get {
                return string.Format("{0}{1}{2}",groupId, Constants.STRING_KEY_BINDER, artifactId);
            }
        }
    }
}
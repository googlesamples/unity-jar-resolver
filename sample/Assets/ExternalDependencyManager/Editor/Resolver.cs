// <copyright file="Resolver.cs" company="Google Inc.">
// Copyright (C) 2017 Google Inc. All Rights Reserved.
//
//	Licensed under the Apache License, Version 2.0 (the "License");
//	you may not use this file except in compliance with the License.
//	You may obtain a copy of the License at
//
//	http://www.apache.org/licenses/LICENSE-2.0
//
//	Unless required by applicable law or agreed to in writing, software
//	distributed under the License is distributed on an "AS IS" BASIS,
//	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//	See the License for the specific language governing permissions and
//	  limitations under the License.
// </copyright>

namespace Google.JarResolver
{
	using System;
	using System.Collections.Generic;
	using UnityEditor;

	/// <summary>
	/// Resolver provides an interface to hide the reflection calls required to
	/// setup the dependency tree. It is safe to use Resolver on any platform
	/// since it will noop if Unity is not currently set to the Android platform.
	/// </summary>
	public static class Resolver
	{
		/// <summary>
		/// When Resolver.CreateSupportInstance is called it will return a
		/// ResolverImpl instance. You can then chain calls to the DependOn
		/// method to setup your dependencies. DependOn will noop when not
		/// on the Android platform.
		/// </summary>
		public class ResolverImpl
		{
			object _svcSupport;

			public ResolverImpl(object svcSupport)
			{
				_svcSupport = svcSupport;
			}


			/// <summary>
			/// Adds a dependency to the project.
			/// </summary>
			/// <remarks>This method should be called for
			/// each library that is required.	Transitive dependencies are processed
			/// so only directly referenced libraries need to be added.
			/// <para>
			/// The version string can be contain a trailing + to indicate " or greater".
			/// Trailing 0s are implied.  For example:
			/// </para>
			/// <para>	1.0 means only version 1.0, but
			/// also matches 1.0.0.
			/// </para>
			/// <para>1.2.3+ means version 1.2.3 or 1.2.4, etc. but not 1.3.
			/// </para>
			/// <para>
			/// 0+ means any version.
			/// </para>
			/// <para>
			/// LATEST means the only the latest version.
			/// </para>
			/// </remarks>
			/// <param name="group">Group - the Group Id of the artifact</param>
			/// <param name="artifact">Artifact - Artifact Id</param>
			/// <param name="version">Version - the version constraint</param>
			/// <param name="packageIds">Optional list of Android SDK package identifiers.</param>
			/// <param name="repositories">List of additional repository directories to search for
			/// this artifact.</param>
			public ResolverImpl DependOn(string group, string artifact, string version, string[] packageIds = null, string[] repositories = null)
			{
				if (_svcSupport != null) {
					Google.VersionHandler.InvokeInstanceMethod(_svcSupport, "DependOn",
						new object[] { group, artifact, version },
						namedArgs: new Dictionary<string, object>()
						{
							{ "packageIds", packageIds },
							{ "repositories", repositories }
						});
				}

				return this;
			}
		}


		/// <summary>
		/// Creates an instance of PlayServicesSupport wrapped in a ResolverImpl instance.
		/// This instance is used to add dependencies for the calling client.
		/// </summary>
		/// <returns>The instance.</returns>
		/// <param name="clientName">Client name.  Must be a valid filename.
		/// This is used to uniquely identify
		/// the calling client so that dependencies can be associated with a specific
		/// client to help in resetting dependencies.</param>
		/// <param name="assemblyName">optional. Specifies which assembly the PlayServicesSupport
		/// class should be loaded from.</param>
		public static ResolverImpl CreateSupportInstance(string clientName, string assemblyName = "Google.JarResolver")
		{
			// if we aren't on Android default to an empty shim
			if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
				return new ResolverImpl(null);

			// bail out with an empty instance if the PlayServicesSupport class isn't available
			var playServicesSupport = Google.VersionHandler.FindClass(assemblyName, "Google.JarResolver.PlayServicesSupport");
			if (playServicesSupport == null)
				return new ResolverImpl(null);

			// create a live instance of the PlayServicesSupport class and return a live ResolverImpl wrapping it
			var svcSupport = Google.VersionHandler.InvokeStaticMethod(playServicesSupport, "CreateInstance",
				new object[] {
				clientName,
				EditorPrefs.GetString( "AndroidSdkRoot" ),
				"ProjectSettings"
			} );

			return new ResolverImpl(svcSupport);
		}
	}
}
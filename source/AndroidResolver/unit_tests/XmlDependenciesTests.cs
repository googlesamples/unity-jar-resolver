using System.Text.RegularExpressions;

namespace GooglePlayServices.Tests {
	using System;
	using NUnit.Framework;

	[TestFixture]
	public class XmlDependenciesTests
	{
		[TestCase("Assets/Editor/Dependencies.xml")]
		[TestCase("Assets/Editor/MyFolder/Dependencies.xml")]
		[TestCase("Editor/Dependencies.xml")]
		[TestCase("Editor/MyFolder/Dependencies.xml")]
		[TestCase("Assets/Editor/SomeDependencies.xml")]
		[TestCase("Assets/MyEditorCode/Dependencies.xml")]
		[TestCase("Assets/MyEditorCode/SomeDependencies.xml")]
		[TestCase("Assets/Editor/")]
		[TestCase("Assets/Editor/Dependendencies")]
		public void IsDependenciesFileReturnsExpected(string path) {
			bool actualResult = XmlDependencies.IsDependenciesFile(path);

			// This was the previous implementation before the optimization attempt and acts as a test reference.
			bool expectedResult = Regex.IsMatch(input: path, pattern: @".*[/\\]Editor[/\\].*Dependencies\.xml$");

			Assert.AreEqual(expectedResult, actualResult);
		}
	}
}
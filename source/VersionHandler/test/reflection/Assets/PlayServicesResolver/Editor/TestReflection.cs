// <copyright file="TestReflection.cs" company="Google Inc.">
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
using System.Collections;
using System.IO;

using Google;

// Class used to test method invocation.
public class Greeter {
    private string name;

    public event Action instanceEvent;

    static public event Action staticEvent;

    public Greeter(string name) {
        this.name = name;
    }

    public static string GenericHello() {
        return "Hello";
    }

    public static string GenericHelloWithCustomerName(string customerName) {
        return String.Format("{0} {1}", GenericHello(), customerName);
    }

    public static string GenericHelloWithCustomerName(int customerId) {
        return String.Format("{0} customer #{1}", GenericHello(), customerId);
    }

    public static string GenericHelloWithPronoun(string pronoun = "There") {
        return String.Format("{0} {1}", GenericHello(), pronoun);
    }

    public static string GenericHelloWithCustomerNameAndPronoun(string customerName,
                                                                string pronoun = "There") {
        return String.Format("{0} {1}", GenericHelloWithPronoun(pronoun: pronoun), customerName);
    }

    public static string GenericHelloWithCustomerNameAndSuffixes(
            string customerName, IEnumerable<string> suffixes = null) {
        string fullName = Greeter.GenericHelloWithCustomerName(customerName);
        if (suffixes != null) {
            foreach (var suffix in suffixes) fullName += " " + suffix;
        }
        return fullName;
    }


    private string MyNameIs() {
        return String.Format(", my name is {0}", name);
    }

    public string Hello() {
        return Greeter.GenericHello() + MyNameIs();
    }

    public string HelloWithCustomerName(string customerName) {
        return Greeter.GenericHelloWithCustomerName(customerName) + MyNameIs();
    }

    public string HelloWithCustomerNameAndPronoun(string customerName,
                                                  string pronoun = "There") {
        return Greeter.GenericHelloWithCustomerNameAndPronoun(customerName,
                                                              pronoun: pronoun) + MyNameIs();
    }

    public static void InvokeStaticEvent() {
        if (staticEvent != null) {
            staticEvent.Invoke();
        }
    }

    public void InvokeInstanceEvent() {
        if (instanceEvent != null) {
            instanceEvent.Invoke();
        }
    }
}

[UnityEditor.InitializeOnLoad]
public class TestReflection {

    /// <summary>
    /// Test all reflection methods.
    /// </summary>
    static TestReflection() {
        // Disable stack traces for more condensed logs.
        UnityEngine.Application.stackTraceLogType = UnityEngine.StackTraceLogType.None;

        // Run tests.
        var failures = new List<string>();
        foreach (var test in new Func<bool>[] {
                    TestFindClassWithAssemblyName,
                    TestFindClassWithoutAssemblyName,
                    TestInvokeStaticMethodWithNoArgs,
                    TestInvokeStaticMethodWithStringArg,
                    TestInvokeStaticMethodWithIntArg,
                    TestInvokeStaticMethodWithNamedArgDefault,
                    TestInvokeStaticMethodWithNamedArg,
                    TestInvokeStaticMethodWithArgAndNamedArgDefault,
                    TestInvokeStaticMethodWithArgAndNamedArg,
                    TestInvokeStaticMethodWithArgAndNamedInterfaceArg,
                    TestInvokeInstanceMethodWithNoArgs,
                    TestInvokeInstanceMethodWithNamedArgDefault,
                    TestInvokeInstanceMethodWithNamedArg,
                    TestInvokeStaticEventMethod,
                 }) {
            var testName = test.Method.Name;
            Exception exception = null;
            bool succeeded = false;
            try {
                UnityEngine.Debug.Log(String.Format("Running test {0}", testName));
                succeeded = test();
            } catch (Exception ex) {
                exception = ex;
                succeeded = false;
            }
            if (succeeded) {
                UnityEngine.Debug.Log(String.Format("{0}: PASSED", testName));
            } else {
                UnityEngine.Debug.LogError(String.Format("{0} ({1}): FAILED", testName, exception));
                failures.Add(testName);
            }
        }

        if (failures.Count > 0) {
            UnityEngine.Debug.Log("Test failed");
            foreach (var testName in failures) {
                UnityEngine.Debug.Log(String.Format("{0}: FAILED", testName));
            }
            UnityEditor.EditorApplication.Exit(1);
        }
        UnityEngine.Debug.Log("Test passed");
        UnityEditor.EditorApplication.Exit(0);
    }

    // Test searching for a class when specifying the assembly name.
    static bool TestFindClassWithAssemblyName() {
        var expectedType = typeof(UnityEditor.EditorApplication);
        var foundType = VersionHandler.FindClass("UnityEditor", "UnityEditor.EditorApplication");
        if (expectedType != foundType) {
            UnityEngine.Debug.LogError(String.Format("Unexpected type {0} vs {1}", foundType,
                                                     expectedType));
            return false;
        }
        return true;
    }

    // Test searching for a class without specifying the assembly name.
    static bool TestFindClassWithoutAssemblyName() {
        var expectedType = typeof(UnityEditor.EditorApplication);
        var foundType = VersionHandler.FindClass(null, "UnityEditor.EditorApplication");
        if (expectedType != foundType) {
            UnityEngine.Debug.LogError(String.Format("Unexpected type {0} vs {1}", foundType,
                                                     expectedType));
            return false;
        }
        return true;
    }

    static bool CheckValue(string expected, string value) {
        if (value != expected) {
            UnityEngine.Debug.LogError(String.Format("Unexpected value {0} vs {1}", value,
                                                     expected));
            return false;
        }
        return true;
    }

    // Invoke a static method with no arguments.
    static bool TestInvokeStaticMethodWithNoArgs() {
        return CheckValue("Hello",
                          (string)VersionHandler.InvokeStaticMethod(typeof(Greeter), "GenericHello",
                                                                    null, null));
    }

    // Invoke an overloaded static method with a string arg.
    static bool TestInvokeStaticMethodWithStringArg() {
        return CheckValue("Hello Jane",
                          (string)VersionHandler.InvokeStaticMethod(typeof(Greeter),
                                                                    "GenericHelloWithCustomerName",
                                                                    new object[] { "Jane" }, null));
    }

    // Invoke an overloaded static method with an int arg.
    static bool TestInvokeStaticMethodWithIntArg() {
        return CheckValue("Hello customer #1337",
                          (string)VersionHandler.InvokeStaticMethod(typeof(Greeter),
                                                                    "GenericHelloWithCustomerName",
                                                                    new object[] { 1337 }, null));
    }

    // Invoke a static method with a default value of a named arg.
    static bool TestInvokeStaticMethodWithNamedArgDefault() {
        return CheckValue("Hello There",
                          (string)VersionHandler.InvokeStaticMethod(typeof(Greeter),
                                                                    "GenericHelloWithPronoun",
                                                                    null, null));
    }

    // Invoke a static method with a named arg.
    static bool TestInvokeStaticMethodWithNamedArg() {
        return CheckValue("Hello Miss",
                          (string)VersionHandler.InvokeStaticMethod(
                              typeof(Greeter), "GenericHelloWithPronoun",
                              null, new Dictionary<string, object> { { "pronoun", "Miss" } }));
    }

    // Invoke a static method with a positional and default value for a named arg.
    static bool TestInvokeStaticMethodWithArgAndNamedArgDefault() {
        return CheckValue("Hello There Bob",
                          (string)VersionHandler.InvokeStaticMethod(
                              typeof(Greeter), "GenericHelloWithCustomerNameAndPronoun",
                              new object[] { "Bob" }, null));
    }

    // Invoke a static method with a positional and named arg.
    static bool TestInvokeStaticMethodWithArgAndNamedArg() {
        return CheckValue("Hello Mrs Smith",
                          (string)VersionHandler.InvokeStaticMethod(
                              typeof(Greeter), "GenericHelloWithCustomerNameAndPronoun",
                              new object[] { "Smith" },
                              new Dictionary<string, object> { { "pronoun", "Mrs" } } ));
    }

    // Invoke a static method with a positional and named interface arg.
    static bool TestInvokeStaticMethodWithArgAndNamedInterfaceArg() {
        IEnumerable<string> suffixes = new string[] { "BSc", "Hons", "PhD", "Kt", "MPerf" };
        return CheckValue("Hello Angie BSc Hons PhD Kt MPerf",
                          (string)VersionHandler.InvokeStaticMethod(
                              typeof(Greeter), "GenericHelloWithCustomerNameAndSuffixes",
                              new object[] { "Angie" },
                              new Dictionary<string, object> { { "suffixes", suffixes } }));
    }

    // Invoke an instance method with no args.
    static bool TestInvokeInstanceMethodWithNoArgs() {
        return CheckValue("Hello, my name is Sam",
                          (string)VersionHandler.InvokeInstanceMethod(new Greeter("Sam"), "Hello",
                                                                      null, null));
    }

    // Invoke an instance method with an default value for a named argument.
    static bool TestInvokeInstanceMethodWithNamedArgDefault() {
        return CheckValue("Hello There Helen, my name is Sam",
                          (string)VersionHandler.InvokeInstanceMethod(
                              new Greeter("Sam"), "HelloWithCustomerNameAndPronoun",
                              new object[] { "Helen" }, null));
    }

    // Invoke an instance method with a named argument.
    static bool TestInvokeInstanceMethodWithNamedArg() {
        return CheckValue("Hello Mrs Smith, my name is Sam",
                          (string)VersionHandler.InvokeInstanceMethod(
                              new Greeter("Sam"), "HelloWithCustomerNameAndPronoun",
                              new object[] { "Smith" },
                              new Dictionary<string, object> { { "pronoun", "Mrs" } }));
    }

    // Check if the delegate is properly added/removed from the given event using the function to
    // test.
    static bool CheckEvent(Func<Action, bool> funcToTest, Action invokeEvent,
                           bool expectSuccess, bool expectInvoked) {

        bool invoked = false;

        Action actionToInvoke = () => {
            invoked = true;
        };

        bool result = funcToTest(actionToInvoke);
        if (result != expectSuccess){
            throw new Exception(
                    String.Format("Expect funcToTest returns '{0}', but actually returned '{1}'",
                            expectSuccess, result));
        }
        invokeEvent();
        if ( invoked != expectInvoked) {
            throw new Exception(String.Format(
                    "Expect event invoked: {0}, but actually event invoked: {1}",
                            expectInvoked, invoked));
        }

        return true;
    }

    // Test adding/removing delegate to a static event.
    static bool TestInvokeStaticEventMethod() {
        CheckEvent( funcToTest: (action) =>
                VersionHandler.InvokeStaticEventAddMethod(typeof(Greeter), "staticEvent", action),
                invokeEvent: Greeter.InvokeStaticEvent,
                expectSuccess: true,
                expectInvoked: true);

        CheckEvent( funcToTest: (action) =>
                VersionHandler.InvokeStaticEventRemoveMethod(typeof(Greeter), "staticEvent",
                        action),
                invokeEvent: Greeter.InvokeStaticEvent,
                expectSuccess: true,
                expectInvoked: false);

        CheckEvent( funcToTest: (action) =>
                VersionHandler.InvokeStaticEventAddMethod(typeof(Greeter), "foo", action),
                invokeEvent: Greeter.InvokeStaticEvent,
                expectSuccess: false,
                expectInvoked: false);

        CheckEvent( funcToTest: (action) =>
                VersionHandler.InvokeStaticEventRemoveMethod(typeof(Greeter), "foo", action),
                invokeEvent: Greeter.InvokeStaticEvent,
                expectSuccess: false,
                expectInvoked: false);

        return true;
    }
}

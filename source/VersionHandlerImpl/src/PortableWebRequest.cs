// <copyright file="PortableWebRequest.cs" company="Google Inc.">
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

namespace Google {

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Net;
using System.Text;
using System.Web;

using UnityEngine;

/// <summary>
/// Minimal interface to retrieve the status and result of a web request.
/// </summary>
public interface IPortableWebRequestStatus {
    /// <summary>
    /// Determine whether the request is complete
    /// </summary>
    bool Complete { get; }

    /// <summary>
    /// Get the response / payload of the request.
    /// </summary>
    byte[] Result { get; }

    /// <summary>
    /// Get the response headers.
    /// </summary>
    IDictionary<string, string> Headers { get; }

    /// <summary>
    /// Get the status code from the response headers.
    /// </summary>
    HttpStatusCode Status { get; }
}

/// <summary>
/// Interface for an object that starts a web request.
/// </summary>
public interface IPortableWebRequest {
    /// <summary>
    /// Post to a URL.
    /// </summary>
    /// <param name="url">URL to send data to.</param>
    /// <param name="headers">Headers to use when performing the request.</param>
    /// <param name="formFields">Form fields to URL encode and send.</param>
    /// <returns>Web request if successfully started, null otherwise.</returns>
    IPortableWebRequestStatus Post(string url, IDictionary<string, string> headers,
                                   IEnumerable<KeyValuePair<string, string>> formFields);

    /// <summary>
    /// Post to a URL.
    /// </summary>
    /// <param name="path">URL path to send data to.</param>
    /// <param name="queryParams">Query parameters to be appended to the URL.</param>
    /// <param name="headers">Headers to use when performing the request.</param>
    /// <param name="formFields">Form fields to URL encode and send.</param>
    /// <returns>Web request if successfully started, null otherwise.</returns>
    IPortableWebRequestStatus Post(string path,
                                   IEnumerable<KeyValuePair<string, string>> queryParams,
                                   IDictionary<string, string> headers,
                                   IEnumerable<KeyValuePair<string, string>> formFields);

    /// <summary>
    /// Get the contents of a URL.
    /// </summary>
    /// <param name="url">URL to retrieve data from.</param>
    /// <param name="headers">Headers to use when performing the request.</param>
    IPortableWebRequestStatus Get(string url,
                                  IDictionary<string, string> headers);
}

/// <summary>
/// Extension methods for the IPortableWebRequestStatus interface.
/// </summary>
internal static class IPortableWebRequestStatusExtension {

    /// <summary>
    /// Get the status code from the response headers.
    /// </summary>
    public static HttpStatusCode GetStatus(this IPortableWebRequestStatus requestStatus) {
        string headerValue = null;
        string code = null;
        if (requestStatus.Headers.TryGetValue("Status-Code", out headerValue)) {
            code = headerValue;
        } else if (requestStatus.Headers.TryGetValue("STATUS", out headerValue)) {
            // Unity puts the status-line (see RFC7230) into the STATUS header field.
            // Parse the string "http-version status-code reason-phrase"
            var tokens = headerValue.Split(' ');
            if (tokens.Length >= 3) code = tokens[1];
        } else {
            UnityEngine.Debug.Log("Status code not found");
            foreach (var kv in requestStatus.Headers) {
                UnityEngine.Debug.Log(String.Format("{0}={1}", kv.Key, kv.Value));
            }
        }
        if (!String.IsNullOrEmpty(code)) {
            try {
                return (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), code, false);
            } catch (ArgumentException) {
                // Ignored
            }
        }
        return (HttpStatusCode)0;
    }
}

/// <summary>
/// *Very* basic web request that works across Unity 4.x and newer.
/// </summary>
public class PortableWebRequest : IPortableWebRequest {

    /// <summary>
    /// Minimal interface to retrieve the status and result of a web request.
    /// </summary>
    protected class RequestStatus : IPortableWebRequestStatus {

        /// <summary>
        /// Backing store for the Request property.
        /// </summary>
        private object request = null;

        /// <summary>
        /// Whether an invalid request was assigned to this object.
        /// </summary>
        private bool requestError = false;

        /// <summary>
        /// Lock for the request property.
        /// </summary>
        private object requestLock = new object();

        /// <summary>
        /// Unity web request object.
        /// </summary>
        public object Request {
            set {
                lock (requestLock) {
                    request = value;
                    requestError = request == null;
                }
            }

            get {
                lock (requestLock) {
                    return request;
                }
            }
        }

        /// <summary>
        /// Determine whether the request is complete
        /// </summary>
        public bool Complete {
            get {
                if (requestError || PortableWebRequest.isDoneProperty == null) return true;
                var requestObj = Request;
                if (requestObj == null ||
                    !(bool)PortableWebRequest.isDoneProperty.GetValue(requestObj, null)) {
                    return false;
                }
                bool isWww = requestObj.GetType().Name == "WWW";
                // Populate the response.
                if (Result == null) {
                    if (isWww) {
                        Result = (byte[])PortableWebRequest.bytesProperty.GetValue(requestObj,
                                                                                   null);
                    } else {
                        var handler =
                            PortableWebRequest.downloadHandlerProperty.GetValue(requestObj, null);
                        Result = handler != null ?
                            (byte[])PortableWebRequest.downloadHandlerDataProperty.GetValue(
                                handler, null) : null;
                    }
                }
                // Populate the response headers.
                if (Headers.Count == 0) {
                    if (isWww) {
                        Headers = (IDictionary<string, string>)
                            PortableWebRequest.responseHeadersProperty.GetValue(requestObj, null);
                    } else {
                        var headers = (IDictionary<string, string>)
                                PortableWebRequest.getResponseHeadersMethod.Invoke(
                                    requestObj, null);
                        if (headers != null) {
                            var headersWithStatus = new Dictionary<string, string>(headers);
                            headersWithStatus["Status-Code"] =
                                responseCodeProperty.GetValue(requestObj, null).ToString();
                            headers = headersWithStatus;
                        }
                        Headers = headers;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Get the response / payload of the request.
        /// </summary>
        public byte[] Result { private set; get; }

        /// <summary>
        /// Get the response headers.
        /// </summary>
        public IDictionary<string, string> Headers { private set; get; }

        /// <summary>
        /// Get the status code from the response headers.
        /// </summary>
        public HttpStatusCode Status { get { return this.GetStatus(); } }

        /// <summary>
        /// Construct an empty request status object.
        /// </summary>
        public RequestStatus() {
            Headers = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Logger for this object.
    /// Exposed for testing purposes.
    /// </summary>
    internal static Logger logger = new Logger();

    /// <summary>
    /// Singleton instance of this class.
    /// This can only be set by tests.
    /// </summary>
    public static IPortableWebRequest DefaultInstance { get; internal set; }

    /// <summary>
    /// Whether an attempt has already been made to cache the web request type.
    /// </summary>
    private static bool attempedToCacheRequestType = false;

    /// <summary>
    /// Type of "request".
    /// </summary>
    private static Type requestType;

    /// <summary>
    /// Method used to construct a POST UnityWebRequest.
    /// </summary>
    private static MethodInfo postMethod;

    /// <summary>
    /// Method used to send a UnityWebRequest after it has been initialized.
    /// </summary>
    private static MethodInfo sendMethod;

    /// <summary>
    /// Method used to construct a GET UnityWebRequest.
    /// </summary>
    private static MethodInfo getMethod;

    /// <summary>
    /// Instance method used to set headers on a UnityWebRequest.
    /// </summary>
    private static MethodInfo setRequestHeaderMethod;

    /// <summary>
    /// Instance method used to get all headers from a UnityWebRequest.
    /// </summary>
    private static MethodInfo getResponseHeadersMethod;

    /// <summary>
    /// Instance method used to get the status / response code from a UnityWebRequest.
    /// </summary>
    private static PropertyInfo responseCodeProperty;

    /// <summary>
    /// WWW property that contains the bytes returned by the server.
    /// </summary>
    private static PropertyInfo bytesProperty;

    /// <summary>
    /// WWW property that contains the headers returned by the server.
    /// </summary>
    private static PropertyInfo responseHeadersProperty;

    /// <summary>
    /// UnityWebRequest property that contains the download handler.
    /// By default this is DownloadHandlerBuffer.
    /// </summary>
    private static PropertyInfo downloadHandlerProperty;

    /// <summary>
    /// Field of DownloadHandler that contains the downloaded data.
    /// </summary>
    private static PropertyInfo downloadHandlerDataProperty;

    /// <summary>
    /// Property that indicates whether a request is complete.
    /// </summary>
    private static PropertyInfo isDoneProperty;

    /// <summary>
    /// Initialize the singleton.
    /// </summary>
    static PortableWebRequest() {
        DefaultInstance = new PortableWebRequest();
    }

    /// <summary>
    /// Find and cache the WWW type and it's properties.
    /// </summary>
    /// <returns>true if successful, false otherwise.</returns>
    private static bool FindAndCacheRequestTypeWww() {
        requestType = Type.GetType("UnityEngine.WWW, UnityEngine");
        if (requestType != null) {
            bytesProperty = requestType.GetProperty("bytes");
            responseHeadersProperty = requestType.GetProperty("responseHeaders");
            if (bytesProperty == null || responseHeadersProperty == null) {
                logger.Log(String.Format("Unable to get bytes='{0}' or responseHeaders='{1}' " +
                                         "property from '{2}'",
                                         bytesProperty, responseHeadersProperty, requestType),
                           level: LogLevel.Warning);
                requestType = null;
            }
        }
        return requestType != null;
    }

    /// <summary>
    /// Try to find a type in a list of assemblies.
    /// </summary>
    /// <param name="typeName">Fully qualified type name.</param>
    /// <param name="assemblyNames">Names of assemblies to search.</param>
    /// <returns>Type if successful, null otherwise.</returns>
    private static Type FindTypeInAssemblies(string typeName, IEnumerable<string> assemblyNames) {
        Type type = null;
        foreach (var assemblyName in assemblyNames) {
            type = Type.GetType(String.Format("{0}, {1}", typeName, assemblyName));
            if (type != null) break;
        }
        return type;
    }

    /// <summary>
    /// Find and cache the UnityWebRequest type and associated classes.
    /// </summary>
    /// <returns>true if successful, false otherwise.</returns>
    private static bool FindAndCacheRequestTypeUnityWebRequest() {
        // These types moved between assemblies between Unity 5.x and Unity 2017.x
        var networkingAssemblyNames = new [] { "UnityEngine.UnityWebRequestModule", "UnityEngine" };
        requestType = FindTypeInAssemblies("UnityEngine.Networking.UnityWebRequest",
                                           networkingAssemblyNames);
        if (requestType != null) {
            postMethod = requestType.GetMethod("Post",
                                               new [] { typeof(String), typeof(WWWForm) });
            getMethod = requestType.GetMethod("Get", new [] { typeof(String) });
            downloadHandlerProperty = requestType.GetProperty("downloadHandler");
            sendMethod = requestType.GetMethod("SendWebRequest");
            sendMethod = sendMethod == null ? requestType.GetMethod("Send") : sendMethod;
            setRequestHeaderMethod = requestType.GetMethod("SetRequestHeader");
            getResponseHeadersMethod = requestType.GetMethod("GetResponseHeaders");
            responseCodeProperty = requestType.GetProperty("responseCode");
            if (postMethod == null || getMethod == null || downloadHandlerProperty == null ||
                sendMethod == null || setRequestHeaderMethod == null ||
                getResponseHeadersMethod == null || responseCodeProperty == null) {
                logger.Log(
                    String.Format(
                        "Failed to get postMethod='{0}', getMethod='{1}', " +
                        "sendMethod='{2}', downloadHandlerProperty='{3}', " +
                        "setRequestHeaderMethod='{4}', getResponseHeadersMethod='{5}' " +
                        "responseCodeProperty='{6}' from '{7}'",
                        postMethod, getMethod, sendMethod, downloadHandlerProperty,
                        setRequestHeaderMethod, getResponseHeadersMethod, responseCodeProperty,
                        requestType),
                    level: LogLevel.Warning);
                requestType = null;
            }
        }

        var downloadHandlerType = FindTypeInAssemblies("UnityEngine.Networking.DownloadHandler",
                                                       networkingAssemblyNames);
        if (downloadHandlerType != null) {
            downloadHandlerDataProperty = downloadHandlerType.GetProperty("data");
            if (downloadHandlerDataProperty == null) {
                logger.Log(String.Format("Failed to get data property for {0}.",
                                         downloadHandlerType), level: LogLevel.Warning);
                requestType = null;
            }
        } else {
            logger.Log("DownloadHandler type not found.", level: LogLevel.Warning);
            requestType = null;
        }
        return requestType != null;
    }

    /// <summary>
    /// Find the request type supported by the current version of Unity.
    /// </summary>
    /// <returns>true if a request type is found, false otherwise.</returns>
    private static bool FindAndCacheRequestType() {
        lock (logger) {
            if (attempedToCacheRequestType) return requestType != null;
            attempedToCacheRequestType = true;

            logger.Log("Find web request type", level: LogLevel.Debug);
            bool cachedRequestType = FindAndCacheRequestTypeUnityWebRequest() ||
                FindAndCacheRequestTypeWww();
            logger.Log(cachedRequestType ?
                       String.Format("PortableWebRequest using '{0}'",
                                     requestType.AssemblyQualifiedName) :
                       "PortableWebRequest unable to find a supported web request type.",
                       level: LogLevel.Debug);
            if (cachedRequestType) {
                isDoneProperty = requestType.GetProperty("isDone");
                if (isDoneProperty == null) {
                    logger.Log(String.Format("Failed to get isDone field / property from {0}",
                                             requestType), level: LogLevel.Warning);
                    requestType = null;
                }
            }
            return requestType != null;
        }
    }

    /// <summary>
    /// HTTP method to use in the request.
    /// </summary>
    private enum HttpMethod {
        Get,
        Post,
    };

    /// <summary>
    /// Start a web request on the main thread.
    /// </summary>
    /// <param name="method">Method to use.</param>
    /// <param name="url">Target URL.</param>
    /// <param name="headers">Headers to use when performing the request.</param>
    /// <param name="payload">Payload to send if this is a Post request, ignored otherwise.</param>
    /// <returns>PortableWebRequest instance that provides the status of the request.</returns>
    private static IPortableWebRequestStatus StartRequestOnMainThread(
            HttpMethod method, string url, IDictionary<string, string> headers, WWWForm form) {
        var requestStatus = new RequestStatus();
        RunOnMainThread.Run(() => {
                                requestStatus.Request = StartRequest(method, url, headers, form);
                            });
        return requestStatus;
    }

    /// <summary>
    /// Start a web request.
    /// </summary>
    /// <param name="method">Method to use.</param>
    /// <param name="url">Target URL.</param>
    /// <param name="headers">Headers to use when performing the request.</param>
    /// <param name="payload">Payload to send if this is a Post request, ignored otherwise.</param>
    /// <returns>Request object that provides the status of the request.</returns>
    private static object StartRequest(HttpMethod method, string url,
                                       IDictionary<string, string> headers,
                                       WWWForm form) {
        object unityRequest = null;
        if (FindAndCacheRequestType()) {
            if (requestType.Name == "WWW") {
                var uploadHeaders = new Dictionary<string, string>();
                if (headers != null) {
                    foreach (var kv in headers) {
                        uploadHeaders[kv.Key] = kv.Value;
                    }
                }
                // Need to manually add the Content-Type header when sending a form as the
                // constructor that takes a WWWForm that doesn't allow the specification of
                // headers.
                if (method == HttpMethod.Post) {
                    uploadHeaders["Content-Type"] = "application/x-www-form-urlencoded";
                }
                object[] args = method == HttpMethod.Get ?
                    new object[] { url, null, uploadHeaders } :
                    new object[] { url, form.data, uploadHeaders };
                unityRequest = Activator.CreateInstance(requestType, args);
            } else if (requestType.Name == "UnityWebRequest") {
                switch (method) {
                    case HttpMethod.Post:
                        unityRequest = postMethod.Invoke(null, new object[] { url, form });
                        break;
                    case HttpMethod.Get:
                        unityRequest = getMethod.Invoke(null, new object[] { url });
                        break;
                }
                if (headers != null) {
                    foreach (var kv in headers) {
                        setRequestHeaderMethod.Invoke(unityRequest,
                                                      new object[] { kv.Key, kv.Value });
                    }
                }
                if (unityRequest != null) sendMethod.Invoke(unityRequest, null);
            }
        }
        return unityRequest;

    }

    /// <summary>
    /// Post to a URL.
    /// </summary>
    /// <param name="url">URL to send data to.</param>
    /// <param name="headers">Headers to use when performing the request.</param>
    /// <param name="formFields">Form fields to URL encode and send.</param>
    /// <returns>Web request if successfully started, null otherwise.</returns>
    public IPortableWebRequestStatus Post(
            string url, IDictionary<string, string> headers,
            IEnumerable<KeyValuePair<string, string>> formFields) {
        var form = new WWWForm();
        if (formFields != null) {
            foreach (var formField in formFields) form.AddField(formField.Key, formField.Value);
        }

        try {
            return StartRequestOnMainThread(HttpMethod.Post, url, headers, form);
        } catch (Exception ex) {
            logger.Log(String.Format("Failed to send post request {0}", ex),
                       level: LogLevel.Verbose);
            return null;
        }
    }

    /// <summary>
    /// Post to a URL.
    /// </summary>
    /// <param name="path">URL path to send data to.</param>
    /// <param name="queryParams">Query parameters to be appended to the URL.</param>
    /// <param name="headers">Headers to use when performing the request.</param>
    /// <param name="formFields">Form fields to URL encode and send.</param>
    /// <returns>Web request if successfully started, null otherwise.</returns>
    public IPortableWebRequestStatus Post(string path,
                                   IEnumerable<KeyValuePair<string, string>> queryParams,
                                   IDictionary<string, string> headers,
                                   IEnumerable<KeyValuePair<string, string>> formFields) {
        var url = new StringBuilder(256);
        foreach (var param in queryParams) {
            url.AppendFormat("{0}{1}={2}",
                                url.Length == 0 ? "?" : "&",
                                Uri.EscapeDataString(param.Key).Trim(),
                                Uri.EscapeDataString(param.Value).Trim());
        }
        url.Insert(0, path);
        return Post(url.ToString(), headers, formFields);
    }

    /// <summary>
    /// Get the contents of a URL.
    /// </summary>
    /// <param name="url">URL to retrieve data from.</param>
    /// <param name="headers">Headers to use when performing the request.</param>
    public IPortableWebRequestStatus Get(string url, IDictionary<string, string> headers) {
        try {
            return StartRequestOnMainThread(HttpMethod.Get, url, headers, null);
        } catch (Exception ex) {
            logger.Log(String.Format("Failed to send get request {0}", ex),
                       level: LogLevel.Verbose);
            return null;
        }
    }
}

}

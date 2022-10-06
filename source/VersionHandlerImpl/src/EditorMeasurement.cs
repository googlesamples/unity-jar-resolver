// <copyright file="EditorMeasurement.cs" company="Google Inc.">
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
using System.IO;

using UnityEngine;
using UnityEditor;

/// <summary>
/// Object that reports events via Google Analytics.
/// </summary>
public class EditorMeasurement {

    /// <summary>
    /// Settings store for the analytics module.
    /// </summary>
    public class Settings {
        // Reference to the analytics object being configured.
        private EditorMeasurement analytics;

        // Cache of settings.
        private bool enabled;

        // Strings used to render the analytics reporting option.
        internal static string EnableAnalyticsReporting = "Enable Analytics Reporting";
        internal static string ReportUsageToDevelopers = "Report {0} usage to the developers. {1}";

        /// <summary>
        /// Initialize settings cache.
        /// </summary>
        public Settings(EditorMeasurement analytics) {
            this.analytics = analytics;
            enabled = analytics.Enabled;
        }

        /// <summary>
        /// Save settings.
        /// </summary>
        public void Save() {
            analytics.Enabled = enabled;
        }

        /// <summary>
        /// Render an option in a settings menu.
        /// </summary>
        public void RenderGui() {
            if (!EditorMeasurement.GloballyEnabled) return;

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(EnableAnalyticsReporting, EditorStyles.boldLabel);
            enabled = EditorGUILayout.Toggle(enabled);
            GUILayout.EndHorizontal();

            GUILayout.Label(String.Format(ReportUsageToDevelopers,
                                          analytics.PluginName,
                                          analytics.DataCollectionDescription));
            GUILayout.EndVertical();
        }
    }

    // Key used to store whether analytics is enabled.
    private string enabledPreferencesKey;
    // Key used to store whether the user has consented to enable analytics.
    private string consentRequestedPreferencesKey;
    // Key used to store cookie in project preferences.
    private string cookiePreferencesKey;
    // Key used to store cookie in system preferences.
    private string systemCookiePreferencesKey;
    // Settings used to enable / disable analytics and store cookies.
    private ProjectSettings settings;
    // ID used to report analytics data.
    private string trackingId;
    // Privacy policy URL.
    private string privacyPolicy;
    // Generates random numbers for each report.
    private System.Random random = new System.Random();
    // Logger used to report what is being logged by the module.
    private Logger logger;

    /// <summary>
    /// Name of the plugin that owns this object.
    /// </summary>
    public string PluginName { get; private set; }

    /// <summary>
    /// Description of collected data.
    /// </summary>
    internal string DataCollectionDescription { get; private set; }

    // Strings for the consent request dialog.
    internal static string EnableAnalytics = "Enable Analytics for {0}";
    internal static string RequestConsentMessage =
        "Would you like to share usage info about the {0} plugin with Google?";
    internal static string RequestConsentDataCollection =
        "This data can be used to improve this product.";
    internal static string RequestConsentLearnMore =
        "To learn more about this product, click the “{0}” button.";
    internal static string RequestConsentPrivacyPolicy =
        "For more information, see {0} by clicking the “{1}” button.";
    internal static string Yes = "Yes";
    internal static string No = "No";
    internal static string LearnMore = "Learn More";
    internal static string PrivacyPolicy = "Privacy Policy";

    // Global flag that controls whether this class is enabled and should report
    // data.
    internal static bool GloballyEnabled = true;

    /// <summary>
    /// Enable / disable analytics
    /// </summary>
    public bool Enabled {
        get { return settings.GetBool(enabledPreferencesKey, true); }
        set {
            settings.SetBool(enabledPreferencesKey, value);
            if (value) {
                if (String.IsNullOrEmpty(Cookie)) Cookie = GenerateCookie();
                if (String.IsNullOrEmpty(SystemCookie)) SystemCookie = GenerateCookie();
            } else {
                Cookie = "";
                if (!settings.UseProjectSettings) SystemCookie = "";
            }
        }
    }

    /// <summary>
    /// Whether consent has been requested to enable analytics.
    /// </summary>
    public bool ConsentRequested {
        get {
            return settings.GetBool(consentRequestedPreferencesKey, false,
                                    SettingsLocation.System);
        }
        private set {
            settings.SetBool(consentRequestedPreferencesKey, value, SettingsLocation.System);
        }
    }

    // Whether consent dialog has been displayed.
    private bool ConsentRequesting = false;

    /// <summary>
    /// Project level cookie which enables reporting of unique projects.
    /// Empty string if analytics is disabled.
    /// </summary>
    internal string Cookie {
        get {
            return Enabled ?
                settings.GetString(cookiePreferencesKey, "", SettingsLocation.Project) : "";
        }
        set {
            settings.SetString(cookiePreferencesKey, value, SettingsLocation.Project);
        }
    }

    /// <summary>
    /// System level cookie which enables reporting of unique users.
    /// Empty string if analytics is disabled.
    /// </summary>
    internal string SystemCookie {
        get { return settings.GetString(systemCookiePreferencesKey, "", SettingsLocation.System); }
        set { settings.SetString(systemCookiePreferencesKey, value, SettingsLocation.System); }
    }

    /// <summary>
    /// Delegate that displays a dialog requesting consent to report analytics.
    /// This is only exposed for testing purposes.
    /// </summary>
    internal DialogWindow.DisplayDelegate displayDialog = DialogWindow.Display;

    /// <summary>
    /// Delegate that opens a URL in an external application.
    /// </summary>
    /// <param name="url">URL to open.</param>
    internal delegate void OpenUrlDelegate(string url);

    /// <summary>
    /// Delegate that opens a URL in an external application.
    /// This is only exposed for testing purposes.
    /// </summary>
    internal OpenUrlDelegate openUrl = OpenUrl;

    /// <summary>
    /// Prefix for all events reported by this instance.
    /// </summary>
    public string BasePath { get; set; }

    /// <summary>
    /// Query added to all reports by this instance.
    /// </summary>
    public string BaseQuery { get; set; }

    /// <summary>
    /// Prefix for all human readable report names (page titles) by this instance.
    /// </summary>
    public string BaseReportName { get; set; }

    /// <summary>
    /// Whether to log the Unity version as a query parameter unityVersion=VERSION.
    /// This is enabled by default.
    /// </summary>
    public bool ReportUnityVersion { get; set; }

    /// <summary>
    /// Whether to log the Unity host platform as a query parameter unityPlatform=PLATFORM.
    /// This is enabled by default.
    /// </summary>
    public bool ReportUnityPlatform { get; set; }

    /// <summary>
    /// The plugin installation source.
    /// </summary>
    /// Typical sources are:
    /// <ul>
    ///   <li><b>unitypackage</b> - installed manually in the project</li>
    ///   <li><b>assetstore</b> - installed via the Unity Asset Store</li>
    ///   <li><b>upm</b> - installed via the Unity Package Manager</li>
    /// </ul>
    public string InstallSource { get; set; }

    /// <summary>
    /// Set the installation source from the location of an assembly in the plugin relative to the
    /// project directory. If InstallSource is not null it takes precedence over this property.
    /// </summary>
    /// <note>This path must use the system directory separator.</note>
    public string InstallSourceFilename { get; set; }

    /// <summary>
    /// Url to page about the data usage for this measurement.
    /// </summary>
    public string DataUsageUrl { get; set; }

    /// <summary>
    /// Generate common query parameters.
    /// </summary>
    /// <returns>Query string with common parameters.</returns>
    internal string CommonQuery {
        get {
            var query = "";
            if (ReportUnityVersion) query = "unityVersion=" + GetAndCacheUnityVersion();
            if (ReportUnityPlatform) {
                query = ConcatenateQueryStrings(
                    query, "unityPlatform=" + GetAndCacheUnityRuntimePlatform());
            }
            string installSource = null;
            if (!String.IsNullOrEmpty(InstallSource)) {
                installSource = InstallSource;
            } else if (!String.IsNullOrEmpty(InstallSourceFilename)) {
                var currentDir = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar;
                var installSourceFilename = InstallSourceFilename;
                if (installSourceFilename.StartsWith(currentDir)) {
                    installSourceFilename = installSourceFilename.Substring(currentDir.Length);
                }
                var rootDir = installSourceFilename.Substring(
                    0, installSourceFilename.IndexOf(Path.DirectorySeparatorChar));
                installSource = rootDir == "Assets" ? "unitypackage" : rootDir == "Library" ?
                    "upm" : "";
            }
            if (!String.IsNullOrEmpty(installSource)) {
                query = ConcatenateQueryStrings(query, "installSource=" + installSource);
            }
            return query;
        }
    }

    /// <summary>
    /// Construct an object to report usage of a plugin.
    /// </summary>
    /// <param name="projectSettings">Settings to store preferences.</param>
    /// <param name="logger">Logger used to display reported events.</param>
    /// <param name="analyticsTrackingId">ID used to report analytics data.</param>
    /// <param name="settingsNamespace">Prefix applied to settings stored by this instance.
    /// This should be a globally unique ID. To minimize the chances of collision developers should
    /// use reverse domain name notation (e.g com.foo.bar).</param>
    /// <param name="pluginName">Name of the plugin to display.</param>
    /// <param name="dataCollectionDescription">Data collection description.</param>
    /// <param name="privacyPolicy">Privacy policy URL.</param>
    public EditorMeasurement(ProjectSettings projectSettings, Logger logger,
                             string analyticsTrackingId, string settingsNamespace,
                             string pluginName, string dataCollectionDescription,
                             string privacyPolicy) {
        ReportUnityVersion = true;
        ReportUnityPlatform = true;
        enabledPreferencesKey = settingsNamespace + "AnalyticsEnabled";
        cookiePreferencesKey = settingsNamespace + "AnalyticsCookie";
        consentRequestedPreferencesKey = settingsNamespace + "AnalyticsConsentRequested";
        systemCookiePreferencesKey = settingsNamespace + "AnalyticsSystemCookie";
        settings = projectSettings;
        trackingId = analyticsTrackingId;
        this.logger = logger;
        PluginName = pluginName;
        DataCollectionDescription = dataCollectionDescription;
        this.privacyPolicy = privacyPolicy;
    }

    /// <summary>
    /// Open a URL in an external application.
    /// </summary>
    /// <param name="url">URL to open.</param>
    private static void OpenUrl(string url) {
        Application.OpenURL(url);
    }

    /// <summary>
    /// Exposed so that it can be set by tests.
    /// </summary>
    internal static string unityVersion = null;

    /// <summary>
    /// Get and cache the Unity version.
    /// </summary>
    /// <returns>Unity version string.</returns>
    private static string GetAndCacheUnityVersion() {
        if (String.IsNullOrEmpty(unityVersion)) {
            unityVersion = Application.unityVersion;
        }
        return unityVersion;
    }

    /// <summary>
    /// Exposed so that it can be set by tests.
    /// </summary>
    internal static string unityRuntimePlatform = null;

    /// <summary>
    /// Get and cache the Unity runtime platform string.
    /// </summary>
    /// <returns>Unity runtime platform string.</returns>
    private static string GetAndCacheUnityRuntimePlatform() {
        if (String.IsNullOrEmpty(unityRuntimePlatform)) {
            unityRuntimePlatform = Application.platform.ToString();
        }
        return unityRuntimePlatform;
    }

    /// <summary>
    /// Ask user to enable analytics.
    /// </summary>
    public void PromptToEnable(Action complete) {
        if (ConsentRequesting || ConsentRequested) {
            complete();
        } else {
            ConsentRequesting = true;
            displayDialog(
                String.Format(EnableAnalytics, PluginName),
                String.Format(RequestConsentMessage, PluginName),
                DialogWindow.Option.Selected1, Yes, No,
                windowWidth: 500.0f,
                complete: option => {
                    switch (option) {
                        case DialogWindow.Option.Selected0: // Yes
                            Enabled = true;
                            break;
                        case DialogWindow.Option.Selected1: // No
                            Enabled = false;
                            break;
                    }
                    ConsentRequesting = false;
                    ConsentRequested = true;
                    complete();
                }, renderContent: dialog => {
                    GUILayout.Label(RequestConsentDataCollection,
                            DialogWindow.DefaultLabelStyle);
                    EditorGUILayout.Space();

                    if (!String.IsNullOrEmpty(DataCollectionDescription)) {
                        GUILayout.Label(DataCollectionDescription,
                                DialogWindow.DefaultLabelStyle);
                        EditorGUILayout.Space();
                    }

                    if (!String.IsNullOrEmpty(DataUsageUrl)) {
                        GUILayout.Label(String.Format(RequestConsentLearnMore, LearnMore),
                                DialogWindow.DefaultLabelStyle);
                        EditorGUILayout.Space();
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button (LearnMore)) {
                            OpenUrl(DataUsageUrl);
                        }
                        EditorGUILayout.Space();
                        EditorGUILayout.Space();
                        EditorGUILayout.Space();
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.Space();
                        EditorGUILayout.Space();
                    }

                    GUILayout.Label(
                        String.Format(RequestConsentPrivacyPolicy, privacyPolicy, PrivacyPolicy),
                        DialogWindow.DefaultLabelStyle);
                }, renderButtons: dialog => {
                    if (GUILayout.Button(PrivacyPolicy)) {
                        OpenUrl(privacyPolicy);
                    }
                    EditorGUILayout.Space();
                });
        }
    }

    /// <summary>
    /// Restore default analytics settings.  This revokes consent for event reporting.
    /// </summary>
    public void RestoreDefaultSettings() {
        if (!Enabled) settings.DeleteKey(consentRequestedPreferencesKey);
        settings.DeleteKeys(new [] { enabledPreferencesKey });
    }

    /// <summary>
    /// Generate an analytics "cookie" / UUID / GUID.
    /// </summary>
    /// <returns>A cookie string.</returns>
    private static string GenerateCookie() {
        return Guid.NewGuid().ToString().Replace("-", "").ToLower();
    }

    /// <summary>
    /// Open a URL and report an analytics event.
    /// </summary>
    /// <param name="url">URL to open and report</param>
    /// <param name="documentTitle">Title of the URL being opened.</param>
    public void OpenUrl(string url, string documentTitle) {
        // The reported path can't include a scheme and host, so this flattens the reported URL
        // into a path.
        var uri = new Uri(url);
        Report("/" + uri.Host + "/" + uri.PathAndQuery + uri.Fragment, documentTitle);
        openUrl(url);
    }


    /// <summary>
    /// Concatenate two query strings.
    /// </summary>
    /// <param name="baseQuery">URL path or query to append to. If this is null or empty
    /// the returned value will be the supplied query argument.</param>
    /// <param name="query">Query to append.</param>
    /// <returns>Path concatenated with the supplied query string.</returns>
    internal static string ConcatenateQueryStrings(string baseQuery, string query) {
        if (String.IsNullOrEmpty(query)) return baseQuery;
        if (String.IsNullOrEmpty(baseQuery)) return query;
        if (baseQuery.StartsWith("?")) baseQuery = baseQuery.Substring(1);
        if (!baseQuery.EndsWith("&")) baseQuery = baseQuery + "&";
        if (query.StartsWith("?")) query = query.Substring(1);
        return baseQuery + query;
    }

    /// <summary>
    /// Report an event with parameters.
    /// </summary>
    /// <param name="reportUrl">URL to send with the report, this must exclude the scheme and host
    /// which can be anything starting with "/" e.g /a/b/c</param>
    /// <param name="parameters">Key value pairs to add as a query string to the reportUrl.</param>
    /// <param name="reportName">Human readable name to report with the URL.</param>
    public void Report(string reportUrl, ICollection<KeyValuePair<string, string>> parameters,
                       string reportName) {
        if (parameters.Count > 0) {
            var queryComponents = new List<string>();
            foreach (var kv in parameters) {
                // URL escape keys and values.
                queryComponents.Add(String.Format("{0}={1}", Uri.EscapeDataString(kv.Key).Trim(),
                                                  Uri.EscapeDataString(kv.Value).Trim()));
            }
            reportUrl = reportUrl + "?" + String.Join("&", queryComponents.ToArray());
        }
        Report(reportUrl, reportName);
    }

    /// <summary>
    /// Report an event.
    /// </summary>
    /// <param name="reportUrl">URL to send with the report, this must exclude the scheme and host
    /// which can be anything starting with "/" e.g /a/b/c</param>
    /// <param name="reportName">Human readable name to report with the URL.</param>
    public void Report(string reportUrl, string reportName) {
        if (!GloballyEnabled) return;

        PromptToEnable(() => {
            if (!Enabled) return;
            try {
                var uri = new Uri("http://ignore.host/" + reportUrl);
                bool reported = false;
                var path = String.Join("", uri.Segments);
                var queryPrefix =
                    ConcatenateQueryStrings(
                        ConcatenateQueryStrings(uri.Query,
                            ConcatenateQueryStrings(CommonQuery, BaseQuery)), "scope=");
                var fragment = uri.Fragment;
                if (!String.IsNullOrEmpty(BasePath)) path = BasePath + path;
                if (!String.IsNullOrEmpty(BaseReportName)) reportName = BaseReportName + reportName;
                // Strip all extraneous path separators.
                while (path.Contains("//")) path = path.Replace("//", "/");
                foreach (var cookie in
                        new KeyValuePair<string, string>[] {
                            new KeyValuePair<string, string>(Cookie, queryPrefix + "project"),
                            new KeyValuePair<string, string>(SystemCookie, queryPrefix + "system")
                        }) {
                    if (String.IsNullOrEmpty(cookie.Key)) continue;
                    // See https://developers.google.com/analytics/devguides/collection/protocol/v1
                    var status = PortableWebRequest.DefaultInstance.Post(
                        "https://www.google-analytics.com/collect",
                        new[] {
                            // Version
                            new KeyValuePair<string, string>("v", "1"),
                            // Tracking ID.
                            new KeyValuePair<string, string>("tid", trackingId),
                            // Client ID.
                            new KeyValuePair<string, string>("cid", cookie.Key),
                            // Hit type.
                            new KeyValuePair<string, string>("t", "pageview"),
                            // "URL" / string to report.
                            new KeyValuePair<string, string>(
                                "dl", path + "?" + cookie.Value + fragment),
                            // Document title.
                            new KeyValuePair<string, string>("dt", reportName),
                            // Cache buster
                            new KeyValuePair<string, string>("z", random.Next().ToString())
                        },
                        null, null);
                    if (status != null) reported = true;
                }
                if (reported) {
                    logger.Log(String.Format("Reporting analytics data: {0}{1}{2} '{3}'", path,
                                            String.IsNullOrEmpty(queryPrefix) ? "" : "?" + queryPrefix,
                                            fragment, reportName),
                            level: LogLevel.Verbose);
                }
            } catch (Exception e) {
                // Make sure no exception thrown during analytics reporting will be raised to
                // the main thread and interupt the process.
                logger.Log(String.Format(
                    "Failed to reporting analytics data due to exception: {0}", e),
                    level: LogLevel.Verbose);
            }
        });
    }
}
}

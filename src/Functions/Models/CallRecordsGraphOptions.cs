using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CallRecordInsights.Models
{
    public class CallRecordsGraphOptions
    {
        public int LifetimeMinutes { get; set; }

        public string Resource { get; set; }

        public string ChangeType { get; set; }

        public Uri NotificationUrl { get; set; }

        public IEnumerable<string> Tenants { get; init; }

        public string Endpoint { get; set; }

        public CallRecordsGraphOptions() {}

        public CallRecordsGraphOptions(IConfigurationSection configurationSection)
        {
            LifetimeMinutes = configurationSection.GetValue(nameof(LifetimeMinutes), 4230);
            Resource = configurationSection.GetValue(nameof(Resource), "communications/callRecords");
            ChangeType = configurationSection.GetValue(nameof(ChangeType), "created,updated");
            NotificationUrl = GetNotificationUrl(configurationSection);
            Tenants = configurationSection.GetValue<string>(nameof(Tenants))?
                            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        ?? Enumerable.Empty<string>();
            Endpoint = configurationSection.GetValue(nameof(Endpoint), GLOBAL_ENDPOINT)?.ToLowerInvariant();
            if (!ValidEndpoints.Contains(Endpoint))
                throw new ArgumentException($"Invalid Endpoint {Endpoint}", nameof(configurationSection));
        }
        
        /// <summary>
        /// Gets the notification URL from the configuration section for the <see cref="CallRecordsGraphOptions"/>.
        /// </summary>
        /// <param name="configurationSection"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static Uri GetNotificationUrl(IConfigurationSection configurationSection)
        {
            var configuredUrl = configurationSection.GetValue<string>(nameof(NotificationUrl));
            if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var notificationUri))
                throw new ArgumentException($"Invalid NotificationUrl '{configuredUrl}'", nameof(configurationSection));

            var query = notificationUri.GetComponents(UriComponents.Query, UriFormat.SafeUnescaped);
            if (!string.IsNullOrEmpty(query))
            {
                var queryParams = query
                    .Split(
                        '&',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToDictionary(
                        q => q.Split('=', 2)[0],
                        q => q.Contains('=') ? Uri.UnescapeDataString(q.Split('=', 2)[1]) : null,
                        StringComparer.OrdinalIgnoreCase);
                if (queryParams != null && queryParams.Remove("tenantId"))
                {
                    var queryBuilder = new StringBuilder();
                    var first = true;
                    foreach (var queryParam in queryParams)
                    {
                        if (!first)
                            queryBuilder.Append('&');
                        else
                            first = false;

                        queryBuilder.Append(queryParam.Key);

                        if (queryParam.Value != null)
                        {
                            queryBuilder.Append('=')
                                .Append(Uri.EscapeDataString(queryParam.Value));
                        }
                    }
                    // update the uri with the new query
                    var builder = new UriBuilder(notificationUri)
                    {
                        Query = queryBuilder.ToString()
                    };
                    notificationUri = builder.Uri;
                }
            }

            return notificationUri;
        }

        private const string GLOBAL_ENDPOINT = "graph.microsoft.com";
        private const string USGOV_ENDPOINT = "graph.microsoft.us";
        private const string USGOV_DOD_ENDPOINT = "dod-graph.microsoft.us";
        private const string CHINA_ENDPOINT = "microsoftgraph.chinacloudapi.cn";

        private static readonly string[] ValidEndpoints = new[] { GLOBAL_ENDPOINT, USGOV_ENDPOINT, USGOV_DOD_ENDPOINT, CHINA_ENDPOINT };
    }
}

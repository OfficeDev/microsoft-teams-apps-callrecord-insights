using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CallRecordInsights.Extensions
{
    public static class TenantIdValidator
    {
        public class OpenIdConfiguration
        {
            [JsonPropertyName("token_endpoint")]
            public string TokenEndpoint { get; set; }

            public Guid GetTenantIdGuid()
            {
                if (string.IsNullOrEmpty(TokenEndpoint)
                    || !Uri.TryCreate(TokenEndpoint, UriKind.Absolute, out var uri)
                    || uri.Segments.Length < 2)
                    return Guid.Empty;

                var idSegment = uri.Segments[1][..^1]; // get the 2nd segment and remove the trailing slash

                if (Guid.TryParse(idSegment, out var id))
                    return id;

                return Guid.Empty;
            }
        }

        private static readonly Dictionary<string, Guid> cachedDomainLookups = new();
        private static readonly object cachedDomainLookupsLock = new();

        private static readonly HttpClient httpClient = new(
            new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15)
            });

        /// <summary>
        /// Checks if the tenantId is a valid GUID or a valid domain name.
        /// </summary>
        /// <param name="tenantId"></param>
        /// <returns></returns>
        public static bool IsValidTenantId(this string tenantId) => string.IsNullOrEmpty(tenantId) || Guid.TryParse(tenantId, out var _) || Uri.CheckHostName(tenantId) == UriHostNameType.Dns;
        /// <summary>
        /// Gets the tenant id GUID from the tenant id string if it is a valid Tenant Id.
        /// </summary>
        /// <param name="tenantIdString"></param>
        /// <param name="tenantIdGuid"></param>
        /// <returns></returns>
        public static bool TryGetValidTenantIdGuid(this string tenantIdString, out Guid tenantIdGuid)
        {
            tenantIdGuid = Guid.Empty;
            if (string.IsNullOrEmpty(tenantIdString))
                return false;

            if (!IsValidTenantId(tenantIdString))
                return false;

            lock (cachedDomainLookupsLock)
                if (cachedDomainLookups.TryGetValue(tenantIdString, out tenantIdGuid) && tenantIdGuid != Guid.Empty)
                    return true;

            // lookup the domain name to get the tenant id GUID
            try
            {
                var data = GetOpenIdConfiguration(tenantIdString);
                tenantIdGuid = data.GetTenantIdGuid();
            }
            catch (Exception _) when (_ is ArgumentNullException or HttpRequestException or NullReferenceException or JsonException)
            {
                return false;
            }

            lock (cachedDomainLookupsLock)
                cachedDomainLookups[tenantIdString] = tenantIdGuid;

            return tenantIdGuid != Guid.Empty;
        }

        /// <summary>
        /// Get the OpenIdConfiguration for the tenantId in a synchronous manner.
        /// </summary>
        /// <param name="tenantId"></param>
        /// <returns></returns>
        private static OpenIdConfiguration GetOpenIdConfiguration(string tenantId) => GetOpenIdConfigurationAsync(tenantId).GetAwaiter().GetResult();

        /// <summary>
        /// Get the OpenIdConfiguration for the tenantId.
        /// </summary>
        /// <param name="tenantId"></param>
        /// <returns></returns>
        private static async Task<OpenIdConfiguration> GetOpenIdConfigurationAsync(string tenantId)
        {
            ArgumentNullException.ThrowIfNull(tenantId, nameof(tenantId));
            var response = await httpClient.GetAsync($"https://login.microsoftonline.com/{tenantId}/.well-known/openid-configuration");
            using var contentStream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<OpenIdConfiguration>(contentStream);
        }
    }
}

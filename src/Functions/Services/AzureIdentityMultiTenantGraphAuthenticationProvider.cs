using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace CallRecordInsights.Services
{
    public class AzureIdentityMultiTenantGraphAuthenticationProvider : IAuthenticationProvider
    {
        private const string AUTHORIZATION_HEADER_KEY = "Authorization";
        private const string PROTOCOL_SCHEME = "Bearer";
        private const string DEFAULT_TENANT_LOOKUP = "DEFAULT";

        private static readonly ConcurrentDictionary<string, AccessToken> _tokenCache = new();
        
        private readonly GraphServiceClientOptions _defaultAuthenticationOptions;
        private readonly TokenCredential _credential;
        private readonly ILogger<AzureIdentityMultiTenantGraphAuthenticationProvider> logger;
        private static readonly string[] AppOnlyScopes = new[] { "00000003-0000-0000-c000-000000000000/.default" };

        public AzureIdentityMultiTenantGraphAuthenticationProvider(
            TokenCredential credential,
            ILogger<AzureIdentityMultiTenantGraphAuthenticationProvider> logger,
            GraphServiceClientOptions graphServiceClientOptions = null)
        {
            _credential = credential ?? throw new ArgumentNullException(nameof(credential));
            this.logger = logger;
            if (graphServiceClientOptions?.RequestAppToken == false)
                throw new InvalidOperationException($"The {nameof(AzureIdentityMultiTenantGraphAuthenticationProvider)} only supports app only authentication.");
            _defaultAuthenticationOptions = graphServiceClientOptions ?? new GraphServiceClientOptions();
            _defaultAuthenticationOptions.RequestAppToken = true;
        }

        /// <summary>
        /// This method will authenticate the request using the <see cref="TokenCredential"/> provided in the constructor.
        /// Override the default TenantId by setting the <see cref="GraphServiceClientOptions.AcquireTokenOptions"/> property on the <see cref="RequestInformation.RequestOptions"/> parameter.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="additionalAuthenticationContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task AuthenticateRequestAsync(RequestInformation request, Dictionary<string, object> additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
        {
            var graphServiceClientOptions = request.RequestOptions.OfType<GraphAuthenticationOptions>().FirstOrDefault() ?? _defaultAuthenticationOptions;

            if (!PROTOCOL_SCHEME.Equals(graphServiceClientOptions?.ProtocolScheme, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The {nameof(AzureIdentityMultiTenantGraphAuthenticationProvider)} only supports the {PROTOCOL_SCHEME} protocol scheme.");
            if (graphServiceClientOptions?.RequestAppToken == false)
                throw new InvalidOperationException($"The {nameof(AzureIdentityMultiTenantGraphAuthenticationProvider)} only supports app only authentication.");

            request.Headers.Remove(AUTHORIZATION_HEADER_KEY);

            var result = await GetTokenAsync(
                    graphServiceClientOptions.AcquireTokenOptions?.Tenant,
                    cancellationToken)
                .ConfigureAwait(false);

            request.Headers.Add(AUTHORIZATION_HEADER_KEY, GetBearerToken(result));
        }

        /// <summary>
        /// Gets a token for the specified tenant. If no tenant is specified, the default tenant will be used.
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="cancellationToken"></param>
        /// <returns><see cref="ValueTask{AccessToken}"/></returns>
        private async ValueTask<AccessToken> GetTokenAsync(string tenant, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(tenant))
                tenant = DEFAULT_TENANT_LOOKUP;

            if (_tokenCache.TryGetValue(tenant, out var accessToken) && accessToken.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                logger?.LogInformation("Using cached token for tenant '{Tenant}'", tenant);
                return accessToken;
            }

            logger?.LogInformation("Getting new token for tenant '{Tenant}'", tenant);
            var newToken = await _credential.GetTokenAsync(
                    new TokenRequestContext(AppOnlyScopes, tenantId:tenant),
                    cancellationToken)
                .ConfigureAwait(false);

            _tokenCache[tenant] = newToken;
            return newToken;
        }

        /// <summary>
        /// Gets the Bearer token from the <see cref="AccessToken"/>.
        /// </summary>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        private static string GetBearerToken(AccessToken accessToken) => string.Join(' ', PROTOCOL_SCHEME, accessToken.Token);
    }
}

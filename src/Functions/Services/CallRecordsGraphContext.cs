using Azure.Core;
using CallRecordInsights.Extensions;
using CallRecordInsights.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.CallRecords;
using Microsoft.Kiota.Abstractions;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Services
{
    /// <summary>
    /// A context for managing <see cref="Subscription"/> and <see cref="CallRecord"/> for the configured <see cref="CallRecordsGraphOptions"/>
    /// </summary>
    public class CallRecordsGraphContext : ICallRecordsGraphContext
    {
        private readonly Lazy<List<string>> lazyTenantList;
        private readonly Lazy<string> lazyNotificationUrlString;
        private readonly Lazy<string> lazyDefaultTenant;

        private readonly TokenCredential credential;
        private readonly GraphServiceClient graphClient;
        private readonly CallRecordsGraphOptions graphOptions;
        private readonly ILogger<CallRecordsGraphContext> logger;

        public CallRecordsGraphContext(
            TokenCredential credential,
            GraphServiceClient graphClient,
            CallRecordsGraphOptions graphOptions,
            ILogger<CallRecordsGraphContext> logger)
        {
            this.credential = credential;
            this.graphClient = graphClient;
            this.graphOptions = graphOptions;
            this.logger = logger;

            lazyDefaultTenant = new(DefaultTenantFactory);
            lazyTenantList = new(TenantListFactory);
            lazyNotificationUrlString = new(NotificationUrlStringFactory);
        }
        public string DefaultTenant { get => lazyDefaultTenant.Value; }

        /// <summary>
        /// The configured Tenants to use for callRecord notifications found in the <see cref="CallRecordsGraphOptions"/>
        /// If the <see cref="CallRecordsGraphOptions.Tenants"/> is empty, then it will be set to a list of 1 tenant that is the default tenant
        /// The default tenant is the tenantId associated with the current Azure Subscription
        /// </summary>
        public IReadOnlyList<string> Tenants { get => lazyTenantList.Value; }

        /// <summary>
        /// Adds or renews a <see cref="Subscription"/> for the configured <see cref="CallRecordsGraphOptions"/> for all configured tenants
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="AggregateException"></exception>
        public async Task<IDictionary<string, Subscription>> AddOrRenewSubscriptionsForConfiguredTenantsAsync(CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, Subscription>();
            var errors = new List<Exception>();

            logger?.LogInformation("Adding or renewing subscriptions for all configured tenants");

            foreach (var tenant in Tenants)
            {
                try
                {
                    var subscription = await AddOrRenewSubscriptionsForTenantAsync(
                            tenant,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (subscription is not default(Subscription))
                        results.Add(tenant, subscription);
                }
                catch (Exception ex) when (ex is ApiException or TaskCanceledException or OperationCanceledException)
                {
                    errors.Add(ex);
                }
            }

            if (errors.Any())
                throw new AggregateException("Errors adding or renewing subscriptions", errors);

            return results;
        }

        /// <summary>
        /// Gets all existing <see cref="Subscription"/> for the configured <see cref="CallRecordsGraphOptions"/> from all configured tenants
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns><see cref="IDictionary{string, Subscription}"/> where the key is the tenantId in which the subscription was found,
        /// and the value is the configured <see cref="Subscription"/></returns>
        /// <exception cref="AggregateException"></exception>
        public async Task<IDictionary<string, Subscription>> GetSubscriptionsFromConfiguredTenantsAsync(CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, Subscription>();
            var errors = new List<Exception>();

            logger?.LogInformation("Looking for subscriptions in all configured tenants");

            foreach (var tenant in Tenants)
            {
                try
                {
                    var subscription = await GetSubscriptionForTenantAsync(
                            tenant,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (subscription is not default(Subscription))
                        results.Add(tenant, subscription);
                }
                catch (Exception ex) when (ex is ApiException or TaskCanceledException or OperationCanceledException)
                {
                    errors.Add(ex);
                }
            }

            if (errors.Any())
                throw new AggregateException("Errors getting subscriptions", errors);

            return results;
        }

        /// <summary>
        /// Looks for a callRecord in all configured tenants
        /// </summary>
        /// <param name="callId">The id of the callRecord to retrieve</param>
        /// <param name="cancellationToken"></param>
        /// <returns><see cref="IDictionary{string, CallRecord}"/> where the key is the tenantId in which the callRecord was found,
        /// and the value is the requested <see cref="CallRecord"/></returns>
        /// <exception cref="AggregateException"></exception>
        public async Task<IDictionary<string, CallRecord>> GetCallRecordFromConfiguredTenantsAsync(string callId, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, CallRecord>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<Exception>();

            logger?.LogInformation(
                "Looking for callRecord {CallId} in all configured tenants",
                callId?.Sanitize());

            foreach (var tenant in Tenants)
            {
                try
                {
                    var callRecord = await GetCallRecordFromTenantAsync(
                            callId,
                            tenant,
                            cancellationToken,
                            throwNotFoundException: false)
                        .ConfigureAwait(false);

                    if (callRecord is not default(CallRecord))
                        results.Add(tenant,callRecord);
                }
                catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or ApiException)
                {
                    errors.Add(ex);
                }
            }

            if (errors.Any())
                throw new AggregateException("Errors getting callRecord", errors);

            if (results.Count == 0)
                logger?.LogWarning(
                    "CallRecord {CallId} not found in any configured tenants",
                    callId?.Sanitize());

            return results;
        }

        /// <summary>
        /// Adds or renews a <see cref="Subscription"/> for the configured <see cref="CallRecordsGraphOptions"/> for a specific tenant
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns><see cref="Subscription"/> that was added or renewed</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="tenant"/> is not found in the configuration after normalization</exception>
        /// <exception cref="ApiException">If the Graph call fails</exception>
        /// <exception cref="TaskCanceledException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task<Subscription> AddOrRenewSubscriptionsForTenantAsync(string tenant, CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "Adding or renewing subscriptions for '{Tenant}'",
                tenant);

            var currentSubscription = await GetSubscriptionForTenantAsync(
                    tenant,
                    cancellationToken)
                .ConfigureAwait(false);

            if (currentSubscription is default(Subscription))
            {
                logger?.LogInformation(
                    "Subscription does not exist for '{Tenant}'",
                    tenant);
                return await AddSubscriptionForTenantAsync(
                        tenant,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            logger?.LogInformation(
                "Subscription {id} already exists for '{Tenant}'",
                currentSubscription.Id,
                tenant);

            return await RenewSubscriptionForTenantAsync(
                    currentSubscription,
                    tenant,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets an existing <see cref="Subscription"/> for the configured <see cref="CallRecordsGraphOptions"/> from a specific tenant
        /// </summary>
        /// <param name="tenant">The tenantId to get the subscription from</param>
        /// <param name="cancellationToken"></param>
        /// <returns><see cref="Subscription"/> that was found, or null if missing.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="tenant"/> is not found in the configuration after normalization</exception>
        /// <exception cref="ApiException">If the Graph call fails</exception>
        /// <exception cref="TaskCanceledException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task<Subscription> GetSubscriptionForTenantAsync(string tenant, CancellationToken cancellationToken = default)
        {
            tenant = GetNormalizedTenantFromConfiguration(tenant);

            try
            {
                logger?.LogInformation(
                    "Getting subscription for '{Tenant}'",
                    tenant);

                var currentSubs = await graphClient.Subscriptions
                    .GetAsync(
                        r => r.Options.AsAppForTenant(tenant),
                        cancellationToken)
                    .ConfigureAwait(false);

                return currentSubs?.Value?
                    .FirstOrDefault(subscription =>
                        subscription?.Resource == graphOptions.Resource
                        && subscription?.ChangeType == graphOptions.ChangeType
                        && Uri.TryCreate(subscription?.NotificationUrl, UriKind.Absolute, out var subscriptionUri)
                        && graphOptions.NotificationUrl.IsBaseOf(subscriptionUri));
            }
            catch (Exception ex) when (ex is ApiException or TaskCanceledException or OperationCanceledException)
            {
                logger?.LogError(
                    ex,
                    "Error getting subscription for '{Tenant}'",
                    tenant);

                throw;
            }
        }

        /// <summary>
        /// Renews an existing <see cref="Subscription"/> for the configured <see cref="CallRecordsGraphOptions"/> to a specific tenant
        /// </summary>
        /// <param name="existing">The exisiting <see cref="Subscription"/> to be renewed</param>
        /// <param name="tenant">The tenantId in which the subscription exists</param>
        /// <param name="cancellationToken"></param>
        /// <returns><see cref="Subscription"/> that was renewed</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="tenant"/> is not found in the configuration after normalization</exception>
        /// <exception cref="ApiException">If the Graph call fails</exception>
        /// <exception cref="TaskCanceledException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task<Subscription> RenewSubscriptionForTenantAsync(Subscription existing, string tenant, CancellationToken cancellationToken = default)
        {
            tenant = GetNormalizedTenantFromConfiguration(tenant);

            try
            {
                logger?.LogInformation(
                    "Renewing subscription {id} for '{Tenant}'",
                    existing.Id,
                    tenant);
                
                var newSubscription = await graphClient.Subscriptions[existing.Id]
                    .PatchAsync(
                        GetSubscriptionRequest(),
                        r => r.Options.AsAppForTenant(tenant),
                        cancellationToken)
                    .ConfigureAwait(false);
                
                logger?.LogInformation(
                    "Subscription {id} renewed for '{Tenant}'",
                    newSubscription?.Id,
                    tenant);
                
                return newSubscription;
            }
            catch (Exception ex) when (ex is ApiException or TaskCanceledException or OperationCanceledException)
            {
                logger?.LogError(
                    ex,
                    "Error renewing subscription for '{Tenant}'",
                    tenant);
                
                throw;
            }
        }

        /// <summary>
        /// Adds a new <see cref="Subscription"/> for the configured <see cref="CallRecordsGraphOptions"/> to a specific tenant
        /// </summary>
        /// <param name="tenant">The tenantId to add the subscription</param>
        /// <param name="cancellationToken"></param>
        /// <returns><see cref="Subscription"/> that was added</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="tenant"/> is not found in the configuration after normalization</exception>
        /// <exception cref="ApiException">If the Graph call fails</exception>
        /// <exception cref="TaskCanceledException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task<Subscription> AddSubscriptionForTenantAsync(string tenant, CancellationToken cancellationToken = default)
        {
            tenant = GetNormalizedTenantFromConfiguration(tenant);

            try
            {
                logger?.LogInformation(
                    "Adding subscription for '{Tenant}'",
                    tenant);

                var newSubscription = await graphClient.Subscriptions
                    .PostAsync(
                        GetSubscriptionRequest(),
                        r => r.Options.AsAppForTenant(tenant),
                        cancellationToken)
                    .ConfigureAwait(false);

                logger?.LogInformation(
                    "Subscription {id} added for '{Tenant}'",
                    newSubscription?.Id,
                    tenant);

                return newSubscription;
            }
            catch (Exception ex) when (ex is ApiException or TaskCanceledException or OperationCanceledException)
            {
                logger?.LogError(
                    ex,
                    "Error adding subscription for '{Tenant}'",
                    tenant);

                throw;
            }
        }

        /// <summary>
        /// Gets a specific callRecord from a specific tenant
        /// </summary>
        /// <param name="callId">The id of the specific callRecord to retrieve</param>
        /// <param name="tenant">The tenantId context to use to request the callReocrd</param>
        /// <param name="cancellationToken"></param>
        /// <param name="throwNotFoundException">Should this method throw <see cref="ApiException"/> <see cref="HttpStatusCode.NotFound"/> exceptions or return null</param>
        /// <returns><see cref="CallRecord"/> that was requested</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="tenant"/> is not found in the configuration after normalization</exception>
        /// <exception cref="ApiException">If the Graph call fails</exception>
        /// <exception cref="TaskCanceledException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task<CallRecord> GetCallRecordFromTenantAsync(string callId, string tenant, CancellationToken cancellationToken = default, bool throwNotFoundException = true)
        {
            tenant = GetNormalizedTenantFromConfiguration(tenant);

            try
            {
                logger?.LogInformation(
                    "Looking for callRecord {CallId} in '{Tenant}'",
                    callId?.Sanitize(),
                    tenant);

                var callRecord = await graphClient.Communications.CallRecords[callId]
                    .GetAsync(
                        r =>
                        {
                            r.QueryParameters.Expand = new[] { "sessions($expand=segments)" };
                            r.Options.AsAppForTenant(tenant);
                        },
                    cancellationToken)
                    .ConfigureAwait(false);

                logger?.LogInformation(
                    "Found callRecord {CallId} with {SessionCount} sessions in '{Tenant}'",
                    callId?.Sanitize(),
                    callRecord?.Sessions?.Count,
                    tenant);
                
                return callRecord;
            }
            catch (Exception ex) when (ex is ApiException or TaskCanceledException or OperationCanceledException)
            {
                if (!throwNotFoundException && ex is ApiException apiEx && apiEx.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                {
                    // Ignore Not Found errors
                    logger?.LogWarning(
                        "CallRecord {CallId} was not found in tenant {Tenant}",
                        callId?.Sanitize(),
                        tenant);

                    return default;
                }

                logger?.LogError(
                    ex,
                    "Errors getting CallRecord {CallId} from '{Tenant}'",
                    callId?.Sanitize(),
                    tenant);

                throw;
            }
        }

        /// <summary>
        /// Get a <see cref="Subscription"/> request object for the configured <see cref="CallRecordsGraphOptions"/>
        /// </summary>
        /// <returns><see cref="Subscription"/></returns>
        private Subscription GetSubscriptionRequest()
        {
            var subscription =  new Subscription()
            {
                ChangeType = graphOptions.ChangeType,
                NotificationUrl = lazyNotificationUrlString.Value,
                Resource = graphOptions.Resource,
                ExpirationDateTime = DateTime.UtcNow.AddMinutes(graphOptions.LifetimeMinutes)
            };

            logger?.LogInformation(
                "Created Subscription request for {Resource} {ChangeType} notifications with url {NotificationUrl} expiring at {ExpirationDateTime}",
                subscription.Resource,
                subscription.ChangeType,
                subscription.NotificationUrl,
                subscription.ExpirationDateTime);

            return subscription;
        }

        /// <summary>
        /// Gets the Guid string representation of the <paramref name="tenantIdentifier"/> from the configuration.
        /// Throws <see cref="ArgumentException"/> if <paramref name="tenantIdentifier"/> is not found in the configuration after normalization
        /// </summary>
        /// <param name="tenantIdentifier"></param>
        /// <returns>The normalized Guid string representation of <paramref name="tenantIdentifier"/></returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="tenantIdentifier"/> is not found in the configuration after normalization</exception>
        private string GetNormalizedTenantFromConfiguration(string tenantIdentifier)
        {
            tenantIdentifier = tenantIdentifier?.TryGetValidTenantIdGuid(out var tenantIdGuid) == true
                ? tenantIdGuid.ToString()
                : Tenants[0];

            if (!Tenants.Contains(tenantIdentifier, StringComparer.OrdinalIgnoreCase))
            {
                logger?.LogError(
                    "Tenant '{Tenant}' is not configured",
                    tenantIdentifier);

                throw new ArgumentException("{Tenant} is not configured", nameof(tenantIdentifier));
            }

            return tenantIdentifier;
        }

        /// <summary>
        /// Gets the notification url string for the configured <see cref="CallRecordsGraphOptions"/>
        /// </summary>
        /// <returns></returns>
        private string NotificationUrlStringFactory()
        {
            var queryBuilder = new StringBuilder(graphOptions.NotificationUrl.Query);
            if (queryBuilder.Length > 0)
                queryBuilder.Append('&');
            var url = new UriBuilder(graphOptions.NotificationUrl) { Query = queryBuilder.Append("tenantId=").Append(DefaultTenant).ToString() }.Uri.ToString();
            if (url.StartsWith("eventhub:"))
                url = $"EventHub:{url[9..]}";
            return url;
        }

        /// <summary>
        /// Gets the list of tenants from the configured <see cref="CallRecordsGraphOptions"/>
        /// </summary>
        /// <returns></returns>
        private List<string> TenantListFactory()
        {
            var tenants = graphOptions
                .Tenants?
                .Select(t => t.TryGetValidTenantIdGuid(out var tid) ? tid.ToString() : null)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToList();

            // If graphOptions.Tenants is not empty, then it will be used as it was passed
            // if the default tenant was not included in the list, then it will not be configured for callRecord notifications
            if (tenants?.Count > 0)
                return tenants;

            // If graphOptions.Tenants list is empty, then it will be set to a list of 1 tenant that is the default tenant
            return new List<string> { DefaultTenant };
        }

        /// <summary>
        /// Gets the default tenantId associated with the current Azure Subscription
        /// </summary>
        /// <returns></returns>
        private string DefaultTenantFactory()
        {
            var token = credential.GetToken(
                    new TokenRequestContext(new[] { "00000003-0000-0000-c000-000000000000/.default" }),
                    default)
                .Token;
            var tenantId = new JwtSecurityToken(token)
                        .Claims
                        .FirstOrDefault(c => c.Type == "tid")?
                        .Value?
                        .TryGetValidTenantIdGuid(out var tenantIdGuid) == true
                            ? tenantIdGuid.ToString()
                            : null;
            return tenantId;
        }
    }
}

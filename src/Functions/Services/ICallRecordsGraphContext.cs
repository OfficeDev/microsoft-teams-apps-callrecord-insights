using Microsoft.Graph.Models;
using Microsoft.Graph.Models.CallRecords;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Services
{
    /// <summary>
    /// An interface for the context for managing <see cref="Subscription"/> and <see cref="CallRecord"/> objects.
    /// </summary>
    public interface ICallRecordsGraphContext
    {
        IReadOnlyList<string> Tenants { get; }
        Task<IDictionary<string, Subscription>> AddOrRenewSubscriptionsForConfiguredTenantsAsync(CancellationToken cancellationToken = default);
        Task<IDictionary<string, Subscription>> GetSubscriptionsFromConfiguredTenantsAsync(CancellationToken cancellationToken = default);
        Task<IDictionary<string, CallRecord>> GetCallRecordFromConfiguredTenantsAsync(string callId, CancellationToken cancellationToken = default);
        Task<Subscription> AddOrRenewSubscriptionsForTenantAsync(string tenant, CancellationToken cancellationToken = default);
        Task<Subscription> AddSubscriptionForTenantAsync(string tenant, CancellationToken cancellationToken = default);
        Task<CallRecord> GetCallRecordFromTenantAsync(string callId, string tenant, CancellationToken cancellationToken = default, bool logNotFoundErrors = true);
        Task<Subscription> GetSubscriptionForTenantAsync(string tenant, CancellationToken cancellationToken = default);
        Task<Subscription> RenewSubscriptionForTenantAsync(Subscription existing, string tenant, CancellationToken cancellationToken = default);
    }
}
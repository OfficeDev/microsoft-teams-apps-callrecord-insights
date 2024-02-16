using CallRecordInsights.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Services
{
    public interface ICallRecordsDataContext
    {
        string Endpoint { get; }

        Task CreateOrUpsertProcessedRecordsAsync(IReadOnlyList<CosmosCallRecord> cosmosCallRecords, string tenantIdContext, Guid callIdGuid, CallRecordsDataContext.Operation operation, CancellationToken cancellationToken = default);
        Task<CallRecordsDataContext.Operation> GetNeededProcessedRecordsOperationAsync(string tenantIdContext, Guid callId, DateTimeOffset? lastModified, int count, CancellationToken cancellationToken = default);
        Task<bool> IsAccessible(CancellationToken cancellationToken = default);
    }
}
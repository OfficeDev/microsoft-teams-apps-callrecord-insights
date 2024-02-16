using Azure;
using CallRecordInsights.Extensions;
using CallRecordInsights.Flattener;
using CallRecordInsights.Models;
using CallRecordInsights.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.CallRecords;
using Microsoft.Kiota.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Functions
{
    public class ProcessCallRecordFunction
    {
        private readonly ICallRecordsGraphContext callRecordsGraphContext;
        private readonly ICallRecordsDataContext callRecordsDataContext;
        private readonly IJsonProcessor jsonFlattener;
        private readonly ILogger<ProcessCallRecordFunction> logger;

        public ProcessCallRecordFunction(
            ICallRecordsGraphContext callRecordsGraphContext,
            ICallRecordsDataContext callRecordsDataContext,
            IJsonProcessor jsonFlattener,
            ILogger<ProcessCallRecordFunction> logger)
        {
            this.callRecordsGraphContext = callRecordsGraphContext;
            this.callRecordsDataContext = callRecordsDataContext;
            this.jsonFlattener = jsonFlattener;
            this.logger = logger;
        }

        [FunctionName(nameof(ProcessCallRecordFunction))]
        public async Task RunAsync(
            [QueueTrigger("%CallRecordsToDownloadQueueName%", Connection = "CallRecordsQueueConnection")]
            string queuedCallId,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(ProcessCallRecordFunction),
                nameof(RunAsync),
                DateTime.UtcNow);

            var (tenantIdString, callIdString) = GetTenantIdAndCallId(queuedCallId);

            if (!Guid.TryParse(callIdString, out var callIdGuid))
            {
                logger?.LogWarning(
                    "CallId '{QueuedCallId}' => '{CallId}' was not a valid Guid",
                    queuedCallId?.Sanitize(),
                    callIdString?.Sanitize());
                return;
            }

            if (tenantIdString.TryGetValidTenantIdGuid(out var tenantIdGuid))
            {
                try
                {
                    var callRecord = await callRecordsGraphContext.GetCallRecordFromTenantAsync(
                            callIdGuid.ToString(),
                            tenantIdGuid.ToString(),
                            cancellationToken,
                            logNotFoundErrors: true)
                        .ConfigureAwait(false);

                    await ProcessRecordAsync(
                            callRecord,
                            tenantIdString,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (
                    ex is AggregateException or RequestFailedException
                    or ApiException 
                    or TaskCanceledException or OperationCanceledException 
                    or CosmosException or InvalidOperationException)
                {
                    logger?.LogError(
                        ex,
                        "Error Processing Record: {TenantId} {CallId}",
                        tenantIdGuid,
                        callIdGuid);
                }
                return;
            }

            IDictionary<string, CallRecord> allRecords;
            try
            {
                allRecords = await callRecordsGraphContext.GetCallRecordFromConfiguredTenantsAsync(
                        callIdGuid.ToString(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is AggregateException or RequestFailedException
                or ApiException 
                or TaskCanceledException or OperationCanceledException)
            {
                logger?.LogError(
                    ex,
                    "Error Getting Record: {TenantId} {CallId}",
                    tenantIdGuid,
                    callIdGuid);

                throw;
            }

            var allExceptions = new List<Exception>();
            foreach (var record in allRecords)
            {
                try
                {
                    await ProcessRecordAsync(record.Value, record.Key, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (
                    ex is AggregateException or RequestFailedException
                    or TaskCanceledException or OperationCanceledException
                    or CosmosException or InvalidOperationException)
                {
                    logger?.LogError(
                        ex,
                        "Error Processing Record: {TenantId} {CallId}",
                        record.Key,
                        callIdGuid);

                    allExceptions.Add(ex);
                }
            }

            if (allExceptions.Count > 0)
                throw new AggregateException(allExceptions);
        }

        /// <summary>
        /// Gets the tenantId and callId from the queuedCallId which is in the format of "tenantId|callId"
        /// </summary>
        /// <param name="queuedCallId"></param>
        /// <returns></returns>
        private static (string tenantId, string callId) GetTenantIdAndCallId(string queuedCallId)
        {
            var tenantId = string.Empty;
            var callId = queuedCallId;
            if (queuedCallId.Contains('|'))
            {
                var callIdParts = queuedCallId.Split('|', StringSplitOptions.TrimEntries);
                tenantId = callIdParts[0];
                callId = callIdParts[1];
            }
            return (tenantId, callId);
        }

        /// <summary>
        /// Processes the <paramref name="callRecord"/> in the <paramref name="tenantId"/> context and upserts the records into Cosmos
        /// </summary>
        /// <param name="callRecord">The <see cref="CallRecord"/> to process</param>
        /// <param name="tenantId">The TenantId context</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        private async Task ProcessRecordAsync(
            CallRecord callRecord,
            string tenantId,
            CancellationToken cancellationToken = default)
        {
            if (callRecord == null)
                throw new ArgumentNullException(nameof(callRecord));
            if (tenantId == null)
                throw new ArgumentNullException(nameof(tenantId));

            try
            {
                var cosmosCallRecords = callRecord.AsKustoCallRecords(
                        jsonFlattener,
                        tenantId)
                    .Select(r => CosmosCallRecord.Create(r))
                    .ToList();

                if (cosmosCallRecords.Count == 0)
                {
                    logger?.LogInformation(
                        "Skipping {CallId} {TenantId} as no records were generated",
                        callRecord.Id,
                        tenantId?.Sanitize());

                    return;
                }

                var tenantIdContext = cosmosCallRecords.First().CallRecordTenantIdContext;

                var callId = cosmosCallRecords.First().CallId ?? Guid.Empty;

                var lastModified = cosmosCallRecords.Max(r => r.LastModifiedDateTimeOffset);

                var cosmosOperation = await callRecordsDataContext.GetNeededProcessedRecordsOperationAsync(
                        tenantIdContext,
                        callId,
                        lastModified,
                        cosmosCallRecords.Count,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (cosmosOperation == CallRecordsDataContext.Operation.Skip)
                {
                    logger?.LogInformation(
                        "Skipping {CallId} {TenantId}",
                        callId,
                        tenantIdContext,
                        lastModified);
                    return;
                }

                await callRecordsDataContext.CreateOrUpsertProcessedRecordsAsync(
                        cosmosCallRecords,
                        tenantIdContext,
                        callId,
                        cosmosOperation,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is AggregateException or RequestFailedException
                or TaskCanceledException or OperationCanceledException
                or CosmosException or InvalidOperationException)
            {
                logger?.LogError(
                    ex,
                    "Upsert Failure for {CallId}",
                    callRecord.Id);

                throw;
            }
        }
    }
}

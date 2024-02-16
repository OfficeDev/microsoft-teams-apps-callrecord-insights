using Azure.Core;
using CallRecordInsights.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Services
{
    public class CallRecordsDataContext : ICallRecordsDataContext
    {
        public enum Operation
        {
            Create,
            Upsert,
            Skip,
            Delete
        }

        private const int MAX_TRANSACTION_BATCH_SIZE = 100;

        private const string DEFAULT_DATABASE_ID = "callrecordinsights";
        private const string DEFAULT_PROCESSED_CONTAINER_ID = "records";

        private readonly static string[] PROCESSED_CONTAINER_PARTITION_KEY_PATHS = new[] { $"/{nameof(CosmosCallRecord.CallRecordTenantIdContext)}", $"/{nameof(CosmosCallRecord.CallId)}" };

        private readonly ILogger<CallRecordsDataContext> logger;
        private readonly Database callRecordsDatabase;
        private readonly string processedContainerId;
        private Container processedContainer;

        public CallRecordsDataContext(
            TokenCredential tokenCredential,
            IConfiguration configuration,
            ILogger<CallRecordsDataContext> logger)
        {
            this.logger = logger;
            var endpointUri = configuration.GetValue<string>("EndpointUri");

            if (string.IsNullOrEmpty(endpointUri))
                throw new ArgumentException("EndpointUri must be configured", nameof(configuration));


            var callRecordsDatabaseId = configuration.GetValue("Database", DEFAULT_DATABASE_ID);
            processedContainerId = configuration.GetValue("ProcessedContainer", DEFAULT_PROCESSED_CONTAINER_ID);

            var cosmosClient = new CosmosClient(endpointUri, tokenCredential);

            callRecordsDatabase = cosmosClient.GetDatabase(callRecordsDatabaseId);
            Endpoint = $"{endpointUri.TrimEnd('/')}/dbs/{callRecordsDatabase.Id}/colls/{processedContainerId}";
        }
        
        public string Endpoint { get; private set; }

        /// <summary>
        /// Tests if the CosmosDB Processed Container is accessible
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsAccessible(CancellationToken cancellationToken = default)
        {
            try
            {
                var processedContainer = await GetProcessedContainerAsync(cancellationToken)
                    .ConfigureAwait(false);

                using var queryResult = processedContainer.GetItemQueryIterator<CosmosCallRecord>(new QueryDefinition("SELECT TOP 1 *  FROM c"));
                
                if (queryResult.HasMoreResults)
                    _ = await queryResult.ReadNextAsync(cancellationToken);
                
                return true;
            }
            catch (Exception ex) when (ex is CosmosException or InvalidOperationException or TaskCanceledException or OperationCanceledException)
            {
                logger?.LogError(ex, "CosmosDB is not accessible");
                return false;
            }
        }

        private class ProcessedOperationQueryResult
        {
            public int Count { get; set; }
            public DateTimeOffset? LastModifiedDateTimeOffset { get; set; }
        }

        /// <summary>
        /// Determines what operation should be performed for the <see cref="CosmosCallRecord"/>s generated for a given callId and tenantIdContext against the Processed Container
        /// If no record exists, the operation is <see cref="Operation.Create"/>
        /// If the record(s) exist but are older or have a different row count, the operation is <see cref="Operation.Upsert"/>
        /// If the record(s) exist and are not older and have the same row count, the operation is <see cref="Operation.Skip"/>
        /// </summary>
        /// <param name="tenantIdContext">The <see cref="CosmosCallRecord.CallRecordTenantIdContext"/></param>
        /// <param name="callId">The <see cref="CosmosCallRecord.CallId"/></param>
        /// <param name="lastModified">The <see cref="CosmosCallRecord.LastModifiedDateTimeOffset"/></param>
        /// <param name="count">The number of <see cref="CosmosCallRecord"/>s generated for the <param name="callId"/></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The required <see cref="Task{Operation}"/></returns>
        public async Task<Operation> GetNeededProcessedRecordsOperationAsync(
            string tenantIdContext,
            Guid callId,
            DateTimeOffset? lastModified,
            int count,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "Checking if {CallId} of {TenantId} with {LastModified} exists or is older",
                callId,
                tenantIdContext,
                lastModified);

            var processedContainer = await GetProcessedContainerAsync(cancellationToken)
                    .ConfigureAwait(false);

            using var queryResult = processedContainer.GetItemQueryIterator<ProcessedOperationQueryResult>(
                queryDefinition: new QueryDefinition("SELECT COUNT(1) AS Count, MAX(c.LastModifiedDateTimeOffset) AS LastModifiedDateTimeOffset FROM c WHERE c.CallRecordTenantIdContext = @tenantIdContext AND c.CallId = @callId")
                    .WithParameter("@tenantIdContext", tenantIdContext)
                    .WithParameter("@callId",callId),
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKeyBuilder().Add(tenantIdContext).Add(callId.ToString()).Build() });

            var cost = 0.0;
            if (queryResult.HasMoreResults)
            {
                var resultsPage = await queryResult.ReadNextAsync(cancellationToken);
                cost = resultsPage.RequestCharge;
                if (resultsPage.Any())
                {
                    var existingLastModified = resultsPage.First().LastModifiedDateTimeOffset;
                    var existingCount = resultsPage.First().Count;
                    if (existingCount == count && existingLastModified >= lastModified)
                    {
                        logger?.LogInformation(
                            "Should Create {CallId} of {TenantId} last modified {LastModified} with {Count} rows, as existing record is not older {ExistingLastModified} and has the same row count {ExistingCount}, RU: {RU_Cost}",
                            callId,
                            tenantIdContext,
                            lastModified,
                            count,
                            existingLastModified,
                            existingCount,
                            cost);

                        return Operation.Skip;
                    }
                    if (existingCount > 0)
                    {
                        logger?.LogInformation(
                            "Should Update {CallId} of {TenantId} last modified {LastModified} with {Count} rows, as existing record is older {ExistingLastModified} or has a different row count {ExistingCount}, RU: {RU_Cost}",
                            callId,
                            tenantIdContext,
                            lastModified,
                            count,
                            existingLastModified,
                            existingCount,
                            cost);

                        return Operation.Upsert;
                    }
                }
            }

            logger?.LogInformation(
                "Should Create {CallId} of {TenantId} as no record exists, RU: {RU_Cost}",
                callId,
                tenantIdContext,
                cost);

            return Operation.Create;
        }

        /// <summary>
        /// Creates or upserts the <see cref="CosmosCallRecord"/>s generated for a given callId and tenantIdContext in the Processed Container
        /// It is performed in batches of <see cref="MAX_TRANSACTION_BATCH_SIZE"/>, and if any batch fails, a rollback is attempted
        /// </summary>
        /// <param name="cosmosCallRecords">The <see cref="IReadOnlyList{CosmosCallRecord}"/> of records to create or upsert. 
        /// Must all be from the same <param name="callIdGuid"/> and <param name="tenantIdContext"/>
        /// </param>
        /// <param name="tenantIdContext">The <see cref="CosmosCallRecord.CallRecordTenantIdContext"/></param>
        /// <param name="callIdGuid">The <see cref="CosmosCallRecord.CallId"/></param>
        /// <param name="operation"><see cref="Operation"/> to perform, must be <see cref="Operation.Create"> or <see cref="Operation.Upsert"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="Exception">If any operation fails and the subsequent rollback fails, the <see cref="Exception"/> from the failed operation is thrown</exception>
        /// <exception cref="ArgumentException">If <param name="operation"/> is not <see cref="Operation.Create"> or <see cref="Operation.Upsert"></exception>
        public async Task CreateOrUpsertProcessedRecordsAsync(
            IReadOnlyList<CosmosCallRecord> cosmosCallRecords,
            string tenantIdContext,
            Guid callIdGuid,
            Operation operation,
            CancellationToken cancellationToken = default)
        {
            var callId = callIdGuid.ToString();

            if (operation != Operation.Create && operation != Operation.Upsert)
                throw new ArgumentException($"Operation must be {nameof(Operation.Create)} or {nameof(Operation.Upsert)}", nameof(operation));

            var partitionKey = new PartitionKeyBuilder()
                .Add(tenantIdContext)
                .Add(callId)
                .Build();

            var (processed, exception) = await ProcessBatchOperationsAsync(
                    operation,
                    partitionKey,
                    callId,
                    cosmosCallRecords,
                    cosmosCallRecords.Count,
                    cancellationToken)
                .ConfigureAwait(false);

            if (exception != null)
            {
                var (_, deleteException) = await ProcessBatchOperationsAsync(
                            Operation.Delete,
                            partitionKey,
                            callId,
                            cosmosCallRecords,
                            processed,
                            cancellationToken)
                    .ConfigureAwait(false);

                if (deleteException != null)
                {
                    var aggregateException = new AggregateException(exception, deleteException);
                    logger?.LogError(
                        aggregateException,
                        "Error processing {Operation} for {CallId} of {Count} rows with {Count} processed and error rolling back. PartitionKey {PartitionKey}",
                        operation,
                        callId,
                        cosmosCallRecords.Count,
                        processed,
                        partitionKey.ToString());
                    throw aggregateException;
                }

                logger?.LogError(
                    exception,
                    "Error processing {Operation} for {CallId} of {Count} rows with {Count} processed, successfully rolled-back. PartitionKey {PartitionKey}",
                    operation,
                    callId,
                    cosmosCallRecords.Count,
                    processed,
                    partitionKey.ToString());

                throw exception;
            }
        }

        /// <summary>
        /// Processes CosmosCallRecords in batches of <see cref="MAX_TRANSACTION_BATCH_SIZE"/> for the given <param name="operation"/>
        /// If any batch fails, the remaining items are skipped and the exception is returned
        /// </summary>
        /// <param name="operation">The <see cref="Operation"/> to perform</param>
        /// <param name="partitionKey">The <see cref="PartitionKey"/> for the operation</param>
        /// <param name="callId">The <see cref="CosmosCallRecord.CallId"/> of the records</param>
        /// <param name="cosmosCallRecords">The full <see cref="IReadOnlyList{CosmosCallRecord}"/> to process</param>
        /// <param name="numberToProcess">The limit of records to process</param>
        /// <param name="cancellationToken"></param>
        /// <returns>A <see cref="Task{(int processed,Exception exception)}"/> with the number of successfully processed records 
        /// and the <see cref="Exception"/>, if any, thrown by the batch</returns>
        private async Task<(int processed, Exception exception)> ProcessBatchOperationsAsync(
            Operation operation,
            PartitionKey partitionKey,
            string callId,
            IReadOnlyList<CosmosCallRecord> cosmosCallRecords,
            int numberToProcess,
            CancellationToken cancellationToken = default)
        {
            var processed = 0;
            Exception exception = default;
            Container container = default;

            try
            {
                container = await GetProcessedContainerAsync(cancellationToken)
                     .ConfigureAwait(false);

                logger?.LogInformation(
                    "{Operation} for {CallId} of {Count} rows in {Container} in {Database}. PartitionKey {PartitionKey}",
                    operation,
                    callId,
                    cosmosCallRecords.Count,
                    container.Id,
                    container.Database.Id,
                    partitionKey.ToString());

                for (var i = 0; i <= numberToProcess; i += MAX_TRANSACTION_BATCH_SIZE)
                {
                    var batch = cosmosCallRecords
                        .Skip(i)
                        .Take(MAX_TRANSACTION_BATCH_SIZE);

                    var currentBatchSize = batch.Count();

                    logger?.LogInformation(
                        "{Operation} batch for {CallId} of {Count} rows of {Total} in {Container} in {Database}. PartitionKey {PartitionKey}",
                        operation,
                        callId,
                        currentBatchSize,
                        numberToProcess,
                        container.Id,
                        container.Database.Id,
                        partitionKey.ToString());

                    var transaction = container.CreateTransactionalBatch(partitionKey);

                    foreach (var cosmosCallRecord in batch)
                    {
                        switch (operation)
                        {
                            case Operation.Create:
                                transaction.CreateItem(cosmosCallRecord);
                                break;
                            case Operation.Upsert:
                                transaction.UpsertItem(cosmosCallRecord);
                                break;
                            case Operation.Delete:
                                transaction.DeleteItem(cosmosCallRecord.id);
                                break;
                        }
                    }

                    var batchResponse = await transaction.ExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var cost = batchResponse.RequestCharge;

                    if (!batchResponse.IsSuccessStatusCode)
                    {
                        logger?.LogError(
                            "{Operation} failure for {CallId} after {Count} rows of {Total} in {Container} in {Database}: {Status} {ErrorMessage} (retry:{RetryAfter}). PartitionKey {PartitionKey}, RU: {RU_Cost}",
                            operation,
                            callId,
                            processed,
                            numberToProcess,
                            container.Id,
                            container.Database.Id,
                            batchResponse.StatusCode,
                            batchResponse.ErrorMessage,
                            batchResponse.RetryAfter,
                            partitionKey.ToString(),
                            cost);

                        logger?.LogError("Detail: {Diagnostic}", batchResponse.Diagnostics.ToString());

                        var substatus = 0;
                        for (var j = 0; j < batchResponse.Count(); j++)
                        {
                            var itemResponse = batchResponse[j];
                            if (itemResponse.StatusCode == HttpStatusCode.FailedDependency)
                                continue;

                            substatus = (int)itemResponse.StatusCode;
                            logger?.LogError(
                                "{Operation} failure for {CallId} after {Count} rows of {Total} in {Container} in {Database}: . PartitionKey {PartitionKey}",
                                operation,
                                callId,
                                processed + j,
                                numberToProcess,
                                container.Id,
                                container.Database.Id,
                                itemResponse.StatusCode,
                                itemResponse.RetryAfter,
                                partitionKey.ToString());
                        }

                        exception = new CosmosException(batchResponse.ErrorMessage, batchResponse.StatusCode, substatus, batchResponse.ActivityId, batchResponse.RequestCharge);

                        return (processed, exception);
                    }

                    processed += currentBatchSize;

                    logger?.LogInformation(
                        "{Operation} success for {CallId} for {Count} rows of {Total} in {Container} in {Database}. PartitionKey {PartitionKey}, RU: {RU_Cost}",
                        operation,
                        callId,
                        processed,
                        numberToProcess,
                        container.Id,
                        container.Database.Id, 
                        partitionKey.ToString(),
                        cost);
                }
            }
            catch (Exception ex) when (ex is CosmosException or InvalidOperationException or TaskCanceledException or OperationCanceledException)
            {
                logger?.LogError(
                    ex,
                    "{Operation} failure for {CallId} after {Count} rows of {Total} in {Container} in {Database}. PartitionKey {PartitionKey}",
                    operation,
                    callId,
                    processed,
                    numberToProcess,
                    container?.Id,
                    container?.Database.Id, 
                    partitionKey.ToString());
                exception = ex;
            }
            return (processed, exception);
        }

        /// <summary>
        /// Gets or creates the Processed Container in the CallRecords Database
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask<Container> GetProcessedContainerAsync(CancellationToken cancellationToken = default)
        {
            if (processedContainer is not null)
                return processedContainer;

            if (string.IsNullOrEmpty(processedContainerId))
                return null;

            try
            {
                logger?.LogInformation(
                    "Getting or creating {Container} in {Database}",
                    processedContainerId,
                    callRecordsDatabase.Id);

                var indexingPolicy = new IndexingPolicy
                {
                    IndexingMode = IndexingMode.Consistent,
                };
                indexingPolicy.IncludedPaths.Add(new IncludedPath { Path = @"/CallRecordTenantIdContext/?" });
                indexingPolicy.IncludedPaths.Add(new IncludedPath { Path = @"/CallId/?" });
                indexingPolicy.IncludedPaths.Add(new IncludedPath { Path = @"/LastModifiedDateTimeOffset/?" });
                indexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = @"/*" });

                var containerDefinition = new ContainerProperties
                {
                    Id = processedContainerId,
                    PartitionKeyPaths = PROCESSED_CONTAINER_PARTITION_KEY_PATHS,
                    IndexingPolicy = indexingPolicy,
                };

                var throughputProperties = ThroughputProperties.CreateAutoscaleThroughput(1000);

                processedContainer = await callRecordsDatabase
                    .CreateContainerIfNotExistsAsync(containerDefinition, throughputProperties, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                logger?.LogInformation(
                    "{Container} in {Database} is created",
                    processedContainer.Id,
                    callRecordsDatabase.Id);
            }
            catch (Exception ex) when (ex is CosmosException or InvalidOperationException or TaskCanceledException or OperationCanceledException)
            {
                logger?.LogError(
                    ex,
                    "Error getting or creating {Container} in {Database}",
                    processedContainerId,
                    callRecordsDatabase.Id);

                throw;
            }
            return processedContainer;
        }
    }
}

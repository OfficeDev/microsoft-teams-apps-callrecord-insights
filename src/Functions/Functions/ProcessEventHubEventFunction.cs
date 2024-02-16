using Azure;
using Azure.Core.Serialization;
using Azure.Messaging.EventHubs;
using Azure.Storage.Queues;
using CallRecordInsights.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Functions
{
    public class ProcessEventHubEventFunction
    {
        private readonly ILogger<ProcessEventHubEventFunction> logger;

        public ProcessEventHubEventFunction(
            ILogger<ProcessEventHubEventFunction> logger)
        {
            this.logger = logger;
        }

        [FunctionName(nameof(ProcessEventHubEventFunction))]
        public async Task RunAsync(
            [EventHubTrigger("%GraphNotificationEventHubName%", Connection = "EventHubConnection")]
            EventData[] eventDataBatch,
            [Queue("%CallRecordsToDownloadQueueName%", Connection = "CallRecordsQueueConnection")]
            QueueClient callRecordsToDownloadQueue,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(ProcessEventHubEventFunction),
                nameof(RunAsync),
                DateTime.UtcNow);

            logger?.LogInformation(
                "Trigger contains {EventCount} events",
                eventDataBatch.Length);

            var exceptions = new List<Exception>();
            foreach (var eventDataEntry in eventDataBatch)
            {
                try
                {
                    logger?.LogInformation(
                        "Processing event {PartitionKey} {SequenceNumber} {EnqueuedTime}",
                        eventDataEntry.PartitionKey,
                        eventDataEntry.SequenceNumber,
                        eventDataEntry.EnqueuedTime);

                    var notificationBatch = await eventDataEntry.EventBody
                        .ToObjectAsync<GraphNotificationBatch>(
                            JsonObjectSerializer.Default,
                            cancellationToken)
                        .ConfigureAwait(false);

                    logger?.LogInformation(
                        "Event contains {NotificationCount} notifications",
                        notificationBatch?.Value?.Count());

                    foreach (var notification in notificationBatch?.Value)
                    {
                        if (notification.ResourceData is null || !notification.ResourceData.IsCallRecordEvent())
                        {
                            logger?.LogInformation(
                                "Unwanted notification of type {ODataType}",
                                notification.ResourceData.ODataType);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(notification.ResourceData.Id))
                        {
                            logger?.LogWarning(
                                "Invalid notification of type {ODataType}: id '{id}' was null or whitespace",
                                notification.ResourceData.ODataType,
                                notification.ResourceData.Id);
                            continue;
                        }

                        await QueueNotificationForProcessingAsync(
                                notification,
                                callRecordsToDownloadQueue,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is FormatException or JsonException)
                {
                    logger?.LogWarning(
                        ex,
                        "{PartitionKey} {SequenceNumber} {EnqueuedTime} is not a valid event",
                        eventDataEntry.PartitionKey,
                        eventDataEntry.SequenceNumber,
                        eventDataEntry.EnqueuedTime);
                }
                catch (Exception ex) when (
                    ex is RequestFailedException
                    or NullReferenceException
                    or TaskCanceledException or OperationCanceledException)
                {
                    logger?.LogError(
                        ex,
                        "Error processing event {PartitionKey} {SequenceNumber} {EnqueuedTime}",
                        eventDataEntry.PartitionKey,
                        eventDataEntry.SequenceNumber,
                        eventDataEntry.EnqueuedTime);
                    exceptions.Add(ex);
                }
            }

            if (exceptions.Count > 0)
                throw new AggregateException("Error(s) processing Event Hub Data", exceptions);
        }

        /// <summary>
        /// Adds the notification to the queue for processing
        /// </summary>
        /// <param name="changeNotification">The <see cref="GraphEventNotification"/> to queue</param>
        /// <param name="callRecordsToDownloadQueue">The destination <see cref="QueueClient"/></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task QueueNotificationForProcessingAsync(
            GraphEventNotification changeNotification,
            QueueClient callRecordsToDownloadQueue,
            CancellationToken cancellationToken = default)
        {
            var newEntity = new CosmosEventNotification(changeNotification);
            try
            {
                logger?.LogInformation(
                    "Try Adding {id} to {Destination}",
                    newEntity.GetQueueString(),
                    callRecordsToDownloadQueue.Name);

                await callRecordsToDownloadQueue.SendMessageAsync(
                        newEntity.GetQueueString(),
                        cancellationToken)
                    .ConfigureAwait(false);

                logger?.LogInformation(
                    "Success Adding {id} to {Destination}",
                    newEntity.GetQueueString(),
                    callRecordsToDownloadQueue.Name);
            }
            catch (Exception ex) when (ex is RequestFailedException or TaskCanceledException or OperationCanceledException)
            {
                logger?.LogError(
                    ex,
                    "Error Adding {id} to {Destination}",
                    newEntity.GetQueueString(),
                    callRecordsToDownloadQueue.Name);

                throw;
            }
        }
    }
}

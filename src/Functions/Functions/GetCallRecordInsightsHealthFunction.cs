using Azure;
using Azure.Storage.Queues;
using CallRecordInsights.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Functions
{
    public class GetCallRecordInsightsHealthFunction
    {
        private readonly ILogger<GetCallRecordInsightsHealthFunction> logger;
        private readonly IConfiguration configuration;
        private readonly ICallRecordsGraphContext callRecordsGraphContext;
        private readonly ICallRecordsDataContext callRecordsDataContext;
        private readonly QueueServiceClient queueServiceClient;
        private readonly string eventHubName;

        public GetCallRecordInsightsHealthFunction(
                    ICallRecordsGraphContext callRecordsGraphContext,
                    ICallRecordsDataContext callRecordsDataContext,
                    IConfiguration configuration,
                    QueueServiceClient queueServiceClient,
                    ILogger<GetCallRecordInsightsHealthFunction> logger)
        {
            this.callRecordsGraphContext = callRecordsGraphContext;
            this.callRecordsDataContext = callRecordsDataContext;
            this.configuration = configuration;
            this.queueServiceClient = queueServiceClient;
            this.logger = logger;
            eventHubName = configuration.GetValue<string>("GraphNotificationEventHubName");
        }

        [Function(nameof(GetCallRecordInsightsHealthFunction))]
        public async Task<HttpResponseData> RunAsync(
            [HttpTrigger(AuthorizationLevel.Admin, "get", Route = "health")]
            HttpRequestData request,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(GetCallRecordInsightsHealthFunction),
                nameof(RunAsync),
                DateTime.UtcNow);

            // Get QueueClient from QueueServiceClient for isolated worker model
            var queueName = configuration.GetValue<string>("CallRecordsToDownloadQueueName");
            var callRecordsToDownloadQueue = queueServiceClient.GetQueueClient(queueName);

            try
            {
                var EventHub = GetEventHubHealth();
                var Cosmos = await GetCosmosHealthAsync(cancellationToken).ConfigureAwait(false);
                var DownloadQueue = await GetQueueHealthAsync(callRecordsToDownloadQueue, cancellationToken).ConfigureAwait(false);

                IDictionary<string, Subscription> subs = default;
                try
                {
                    subs = await callRecordsGraphContext.GetSubscriptionsFromConfiguredTenantsAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (
                    ex is AggregateException or RequestFailedException
                    or ApiException or HttpRequestException
                    or SocketException or TimeoutException)
                {
                    logger?.LogError(ex, "Error querying Subscriptions");
                }

                var Subscriptions = subs?
                    .Select(s => new
                        {
                            s.Value.Id,
                            s.Value.ExpirationDateTime,
                            TenantId = s.Key,
                        })
                    .ToList();

                var UnhealthyServices = new List<string>();
                if (!EventHub.Healthy)
                    UnhealthyServices.Add(nameof(EventHub));
                if (!Cosmos.Healthy)
                    UnhealthyServices.Add(nameof(Cosmos));
                if (!DownloadQueue.Healthy)
                    UnhealthyServices.Add(nameof(DownloadQueue));
                if (Subscriptions is null || Subscriptions.Count != callRecordsGraphContext.Tenants.Count)
                    UnhealthyServices.Add(nameof(Subscriptions));

                var response = request.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    Healthy = !UnhealthyServices.Any(),
                    EventHub,
                    Cosmos,
                    DownloadQueue,
                    Subscriptions,
                    UnhealthyServices,
                });
                return response;
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
            {
                return request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }
        }

        /// <summary>
        /// Gets the value of a key from a connection string.
        /// </summary>
        /// <param name="connectionString">The connection string to parse</param>
        /// <param name="key">The key to retrieve</param>
        /// <returns>The value of the <paramref name="key"/> in the <paramref name="connectionString"/> if present</returns>
        private static string GetValueFromConnectionString(string connectionString, string key)
        {
            const StringSplitOptions splitOptions = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;
            return connectionString?.Split(';', splitOptions)?.FirstOrDefault(s => s.StartsWith(key))?.Split('=', 2, splitOptions)[1];
        }

        internal record HealthState
        {
            public bool Healthy;
            public string Status;
            public string Url;
        }

        /// <summary>
        /// Retrieves the configuration of the Event Hub, no connection is made.
        /// </summary>
        /// <returns></returns>
        private HealthState GetEventHubHealth()
        {
            var EventHub = new HealthState
            {
                Healthy = false,
                Status = string.Empty,
                Url = string.Empty,
            };

            if (string.IsNullOrEmpty(eventHubName))
            {
                EventHub.Status = "No EventHub name found in configuration.";
                return EventHub;
            }

            // In isolated worker model, connection configuration is accessed differently
            var eventHubConnectionString = configuration.GetValue<string>("EventHubConnection");
            var eventHubNamespace = configuration.GetValue<string>("EventHubConnection__fullyQualifiedNamespace");

            if (string.IsNullOrEmpty(eventHubConnectionString) && string.IsNullOrEmpty(eventHubNamespace))
            {
                EventHub.Status = "No EventHub connection string or fullyQualifiedNamespace found in configuration.";
                return EventHub;
            }

            EventHub.Healthy = true;
            if (!string.IsNullOrEmpty(eventHubConnectionString))
            {
                EventHub.Url = $"{GetValueFromConnectionString(eventHubConnectionString, "Endpoint").TrimEnd('/')}/{eventHubName}";
                return EventHub;
            }

            EventHub.Url = $"sb://{eventHubNamespace}/{eventHubName}";
            return EventHub;
        }

        /// <summary>
        /// Gets the health of the Cosmos DB container by verifying it is accessible.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<HealthState> GetCosmosHealthAsync(CancellationToken token)
        {
            if (callRecordsGraphContext is null)
            {
                return new HealthState
                {
                    Healthy = false,
                    Status = "No CallRecordGraphContext found.",
                };
            }
            var isAccessible = await callRecordsDataContext.IsAccessible(token).ConfigureAwait(false);
            if (!isAccessible)
            {
                return new HealthState
                {
                    Healthy = false,
                    Status = "Cosmos DB is not accessible.",
                    Url = callRecordsDataContext.Endpoint,
                };
            }
            return new HealthState
            {
                Healthy = true,
                Url = callRecordsDataContext.Endpoint,
            };
        }

        /// <summary>
        /// Gets the health of the Queue by peeking the next message.
        /// </summary>
        /// <param name="queueClient">The <see cref="QueueClient"/> to test</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<HealthState> GetQueueHealthAsync(QueueClient queueClient, CancellationToken cancellationToken = default)
        {
            var Queue = new HealthState
            {
                Healthy = false,
                Status = string.Empty,
                Url = queueClient?.Uri?.ToString(),
            };
            try
            {
                if (queueClient != null)
                {
                    // Just make sure we don't throw an exception
                    _ = await queueClient.PeekMessagesAsync(1, cancellationToken).ConfigureAwait(false);
                    Queue.Healthy = true;
                }
            }
            catch (Exception ex) when (
                ex is RequestFailedException
                or SocketException or TimeoutException
                or HttpRequestException)
            {
                logger?.LogError(ex, "Error querying Queue");
                Queue.Status = ex.Message;
            }
            return Queue;
        }
    }
}

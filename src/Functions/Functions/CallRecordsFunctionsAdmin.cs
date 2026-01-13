using Azure;
using Azure.Storage.Queues;
using CallRecordInsights.Extensions;
using CallRecordInsights.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Functions
{
    public class CallRecordsFunctionsAdmin
    {
        private readonly ICallRecordsGraphContext callRecordsGraphContext;
        private readonly ILogger<CallRecordsFunctionsAdmin> logger;
        private readonly QueueServiceClient queueServiceClient;
        private readonly IConfiguration configuration;

        public CallRecordsFunctionsAdmin(
            ICallRecordsGraphContext callRecordsGraphContext,
            ILogger<CallRecordsFunctionsAdmin> logger,
            QueueServiceClient queueServiceClient,
            IConfiguration configuration)
        {
            this.callRecordsGraphContext = callRecordsGraphContext;
            this.logger = logger;
            this.queueServiceClient = queueServiceClient;
            this.configuration = configuration;
        }

        [Function(nameof(GetSubscriptionIdFunction))]
        public async Task<HttpResponseData> GetSubscriptionIdFunction(
            [HttpTrigger(AuthorizationLevel.Admin, "get", Route = "subscription/{tenantId?}")]
            HttpRequestData request,
            string tenantId = null,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(CallRecordsFunctionsAdmin),
                nameof(GetSubscriptionIdFunction),
                DateTime.UtcNow);

            try
            {
                var currentSubscription = await callRecordsGraphContext.GetSubscriptionForTenantAsync(
                        tenantId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (currentSubscription is null)
                {
                    var notFoundResponse = request.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteAsJsonAsync(new
                    {
                        Title = "Subscription not found.",
                        Detail = "The subscription for call records for this function was not found."
                    });
                    return notFoundResponse;
                }
                return await GetSubscriptionResultAsync(request, currentSubscription, tenantId);
            }
            catch (ArgumentException ex) when (ex.Message?.Contains("not configured", StringComparison.OrdinalIgnoreCase) == true)
            {
                var badRequestResponse = request.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new
                {
                    Title = "Tenant not configured.",
                    Detail = $"Could not find tenant \"{tenantId}\" in configuration."
                });
                return badRequestResponse;
            }
        }

        [Function(nameof(AddSubscriptionOrRenewIfExpiredFunction))]
        public async Task<HttpResponseData> AddSubscriptionOrRenewIfExpiredFunction(
            [HttpTrigger(AuthorizationLevel.Admin, "put", "post", Route = "subscription/{tenantId?}")]
            HttpRequestData request,
            string tenantId = null,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(CallRecordsFunctionsAdmin),
                nameof(AddSubscriptionOrRenewIfExpiredFunction),
                DateTime.UtcNow);

            logger?.LogInformation(
                "Request received for {Tenant}",
                string.IsNullOrEmpty(tenantId) ? "default tenant" : tenantId?.Sanitize());

            try
            {
                var subscription = await callRecordsGraphContext.AddOrRenewSubscriptionsForTenantAsync(
                        tenantId,
                        cancellationToken)
                    .ConfigureAwait(false);

                return await GetSubscriptionResultAsync(request, subscription, tenantId);
            }
            catch (ArgumentException ex) when (ex.Message?.Contains("not configured", StringComparison.OrdinalIgnoreCase) == true)
            {
                var badRequestResponse = request.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new
                {
                    Title = "Tenant not configured.",
                    Detail = $"Could not find tenant \"{tenantId}\" in configuration."
                });
                return badRequestResponse;
            }
            catch (ArgumentException ex)
            {
                var badRequestResponse = request.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new
                {
                    Title = "Invalid request.",
                    Detail = ex.Message
                });
                return badRequestResponse;
            }
            catch (ODataError ex) when (ex.Error?.Code?.Equals(GraphErrorCode.AccessDenied.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                var unauthorizedResponse = request.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new
                {
                    Title = "Access denied.",
                    Detail = "The application does not have the required permissions to create a subscription for this tenant."
                });
                return unauthorizedResponse;
            }
        }

        [Function(nameof(ManuallyProcessCallIdsFunction))]
        public async Task<HttpResponseData> ManuallyProcessCallIdsFunction(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "callRecords")]
            HttpRequestData request,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(CallRecordsFunctionsAdmin),
                nameof(ManuallyProcessCallIdsFunction),
                DateTime.UtcNow);

            // Get QueueClient from QueueServiceClient for isolated worker model
            var queueName = configuration.GetValue<string>("CallRecordsToDownloadQueueName");
            var callRecordsToDownloadQueue = queueServiceClient.GetQueueClient(queueName);

            IEnumerable<string> callIds;
            try
            {
                callIds = await request.ReadFromJsonAsync<IEnumerable<string>>(cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentNullException or NotSupportedException)
            {
                var badRequestResponse = request.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new
                {
                    Title = "Invalid request body.",
                    Detail = "The request body was not a valid JSON array of callId Guids."
                });
                return badRequestResponse;
            }

            var results = new MultipleCallIdRequestResultObject();
            results.ProcessingSkippedDueToFailure.UnionWith(callIds);
            foreach (var callId in results.ProcessingSkippedDueToFailure)
            {
                logger?.LogInformation(
                    "Manually process callRecord request received for callId: {callId}",
                    callId?.Sanitize());

                results.ProcessingSkippedDueToFailure.Remove(callId);
                if (!Guid.TryParse(callId, out var callIdGuid))
                {
                    results.QueuingFailure.Add(callId);
                    continue;
                }
                try
                {
                    var reciept = await callRecordsToDownloadQueue.SendMessageAsync(
                            callIdGuid.ToString(),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (reciept?.Value is not null)
                    {
                        results.Queued.Add(callId);
                        continue;
                    }

                    results.QueuingFailure.Add(callId);
                }
                catch(Exception ex) when (ex is RequestFailedException or TaskCanceledException or OperationCanceledException)
                {
                    results.QueuingFailure.Add(callId);
                }
            }
            return await MultipleCallIdRequestResultObject.ResultAsync(request, results);
        }

        /// <summary>
        /// Converts a <see cref="Subscription"/> object to an <see cref="HttpResponseData"/> object with appropriate fields.
        /// </summary>
        /// <param name="request">The HTTP request</param>
        /// <param name="subscription">The <see cref="Subscription"/> to return</param>
        /// <param name="TenantId">The TenantId requested</param>
        /// <returns></returns>
        private static async Task<HttpResponseData> GetSubscriptionResultAsync(HttpRequestData request, Subscription subscription, string TenantId = null)
        {
            TenantId = string.IsNullOrEmpty(TenantId) ? "Default" : TenantId?.TryGetValidTenantIdGuid(out var tenantIdGuid) == true ? tenantIdGuid.ToString() : TenantId;
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                subscription.Id,
                subscription.ExpirationDateTime,
                TenantId,
                subscription.Resource,
                subscription.ChangeType,
                subscription.NotificationUrl,
            });
            return response;
        }

        public class MultipleCallIdRequestResultObject
        {
            public HashSet<string> Queued { get; set; } = new();
            public HashSet<string> QueuingFailure { get; set; } = new();
            public HashSet<string> ProcessingSkippedDueToFailure { get; set; } = new();

            /// <summary>
            /// Wraps the <see cref="MultipleCallIdRequestResultObject"/> in an <see cref="HttpResponseData"/> with appropriate status code.
            /// </summary>
            /// <param name="request">The HTTP request</param>
            /// <param name="results"></param>
            /// <returns></returns>
            public static async Task<HttpResponseData> ResultAsync(HttpRequestData request, MultipleCallIdRequestResultObject results)
            {
                var statusCode = results.QueuingFailure.Count + results.ProcessingSkippedDueToFailure.Count > 0
                    ? results.Queued.Count == 0
                        ? HttpStatusCode.InternalServerError
                        : (HttpStatusCode)207 // Multi-Status
                    : HttpStatusCode.OK;

                var response = request.CreateResponse(statusCode);
                await response.WriteAsJsonAsync(results);
                return response;
            }
        }
    }
}

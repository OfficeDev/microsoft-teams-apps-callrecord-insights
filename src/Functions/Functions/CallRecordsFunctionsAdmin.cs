using Azure;
using Azure.Storage.Queues;
using CallRecordInsights.Extensions;
using CallRecordInsights.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Functions
{
    public class CallRecordsFunctionsAdmin
    {
        private readonly ICallRecordsGraphContext callRecordsGraphContext;
        private readonly ILogger<CallRecordsFunctionsAdmin> logger;

        public CallRecordsFunctionsAdmin(
            ICallRecordsGraphContext callRecordsGraphContext,
            ILogger<CallRecordsFunctionsAdmin> logger)
        {
            this.callRecordsGraphContext = callRecordsGraphContext;
            this.logger = logger;
        }

        [FunctionName(nameof(GetSubscriptionIdFunction))]
        public async Task<IActionResult> GetSubscriptionIdFunction(
            [HttpTrigger(AuthorizationLevel.Admin, "get", Route = "subscription/{tenantId?}")]
            HttpRequest request,
            string tenantId = null,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(CallRecordsFunctionsAdmin),
                nameof(GetSubscriptionIdFunction),
                DateTime.UtcNow);

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, request.HttpContext.RequestAborted);

            try
            {
                var currentSubscription = await callRecordsGraphContext.GetSubscriptionForTenantAsync(
                        tenantId,
                        cancellationSource.Token)
                    .ConfigureAwait(false);

                if (currentSubscription is null)
                {
                    return new NotFoundObjectResult(new ProblemDetails
                    {
                        Title = "Subscription not found.",
                        Detail = "The subscription for call records for this function was not found."
                    });
                }
                return GetSubscriptionResult(currentSubscription, tenantId);
            }
            catch (ArgumentException ex) when (ex.Message?.Contains("not configured",StringComparison.OrdinalIgnoreCase) == true)
            {
                return new BadRequestObjectResult(new ProblemDetails
                {
                    Title = "Tenant not configured.",
                    Detail = $"Could not find tenant \"{tenantId}\" in configuration."
                });
            }
        }

        [FunctionName(nameof(AddSubscriptionOrRenewIfExpiredFunction))]
        public async Task<IActionResult> AddSubscriptionOrRenewIfExpiredFunction(
            [HttpTrigger(AuthorizationLevel.Admin, "put", "post", Route = "subscription/{tenantId?}")]
            HttpRequest request,
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

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, request.HttpContext.RequestAborted);

            try
            {
                var subscription = await callRecordsGraphContext.AddOrRenewSubscriptionsForTenantAsync(
                        tenantId,
                        cancellationSource.Token)
                    .ConfigureAwait(false);

                return GetSubscriptionResult(subscription, tenantId);
            }
            catch (ArgumentException ex) when (ex.Message?.Contains("not configured", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new BadRequestObjectResult(new ProblemDetails
                {
                    Title = "Tenant not configured.",
                    Detail = $"Could not find tenant \"{tenantId}\" in configuration."
                });
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(new ProblemDetails
                {
                    Title = "Invalid request.",
                    Detail = ex.Message
                });
            }
            catch (ODataError ex) when (ex.Error?.Code?.Equals(GraphErrorCode.AccessDenied.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                return new UnauthorizedObjectResult(new ProblemDetails
                {
                    Title = "Access denied.",
                    Detail = "The application does not have the required permissions to create a subscription for this tenant."
                });
            }
        }

        [FunctionName(nameof(ManuallyProcessCallIdsFunction))]
        public async Task<IActionResult> ManuallyProcessCallIdsFunction(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "callRecords")]      
            HttpRequest request,
            [Queue("%CallRecordsToDownloadQueueName%", Connection = "CallRecordsQueueConnection")]
            QueueClient callRecordsToDownloadQueue,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(CallRecordsFunctionsAdmin),
                nameof(ManuallyProcessCallIdsFunction),
                DateTime.UtcNow);

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, request.HttpContext.RequestAborted);

            IEnumerable<string> callIds;
            try
            {
                callIds = await request.ReadFromJsonAsync<IEnumerable<string>>(cancellationSource.Token);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentNullException or NotSupportedException)
            {
                return new BadRequestObjectResult(new ProblemDetails
                {
                    Title = "Invalid request body.",
                    Detail = "The request body was not a valid JSON array of callId Guids."
                });
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
                            cancellationSource.Token)
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
            return MultipleCallIdRequestResultObject.Result(results);
        }

        /// <summary>
        /// Converts a <see cref="Subscription"/> object to an <see cref="OkObjectResult"/> object with appropriate fields.
        /// </summary>
        /// <param name="subscription">The <see cref="Subscription"/> to return</param>
        /// <param name="TenantId">The TenantId requested</param>
        /// <returns></returns>
        private static IActionResult GetSubscriptionResult(Subscription subscription, string TenantId = null)
        {
            TenantId = string.IsNullOrEmpty(TenantId) ? "Default" : TenantId?.TryGetValidTenantIdGuid(out var tenantIdGuid) == true ? tenantIdGuid.ToString() : TenantId;
            return new OkObjectResult(
                new
                {
                    subscription.Id,
                    subscription.ExpirationDateTime,
                    TenantId,
                    subscription.Resource,
                    subscription.ChangeType,
                    subscription.NotificationUrl,
                });
        }

        public class MultipleCallIdRequestResultObject
        {
            public HashSet<string> Queued { get; set; } = new();
            public HashSet<string> QueuingFailure { get; set; } = new();
            public HashSet<string> ProcessingSkippedDueToFailure { get; set; } = new();

            /// <summary>
            /// Wraps the <see cref="MultipleCallIdRequestResultObject"/> in an <see cref="ObjectResult"/> with appropriate status code.
            /// </summary>
            /// <param name="results"></param>
            /// <returns></returns>
            public static ObjectResult Result(MultipleCallIdRequestResultObject results)
            {
                var code = results.QueuingFailure.Count + results.ProcessingSkippedDueToFailure.Count > 0
                    ? results.Queued.Count == 0
                        ? StatusCodes.Status500InternalServerError
                        : StatusCodes.Status207MultiStatus
                    : StatusCodes.Status200OK;
                return new ObjectResult(results)
                {
                    StatusCode = code
                };
            }
        }
    }
}

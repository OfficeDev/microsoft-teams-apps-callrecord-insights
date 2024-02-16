using CallRecordInsights.Extensions;
using CallRecordInsights.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Functions.Functions
{
    public class GetCallRecordAdminFunction
    {
        private readonly ICallRecordsGraphContext callRecordsGraphContext;
        private readonly ILogger<GetCallRecordAdminFunction> logger;

        public GetCallRecordAdminFunction(
            ICallRecordsGraphContext callRecordsGraphContext,
            ILogger<GetCallRecordAdminFunction> logger)
        {
            this.callRecordsGraphContext = callRecordsGraphContext;
            this.logger = logger;
        }

        [FunctionName(nameof(GetCallRecordAdminFunction))]
        public async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Admin, "get", Route = "callRecords/{callId}/{tenantId?}")]
            HttpRequest request,
            Guid callId,
            string tenantId = null,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(GetCallRecordAdminFunction),
                nameof(RunAsync),
                DateTime.UtcNow);

            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, request.HttpContext.RequestAborted);
            if (!string.IsNullOrEmpty(tenantId))
            {
                try
                {
                    var record = await callRecordsGraphContext.GetCallRecordFromTenantAsync(
                            callId.ToString(),
                            tenantId,
                            cancellationSource.Token,
                            logNotFoundErrors: true)
                        .ConfigureAwait(false);

                    var callRecordString = await record.SerializeAsStringAsync().ConfigureAwait(false);

                    return new ContentResult()
                    {
                        Content = callRecordString,
                        ContentType = "application/json",
                        StatusCode = StatusCodes.Status200OK,
                    };
                }
                catch (ApiException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
                {
                    return new NotFoundObjectResult(new { error = new { message = $"Call Record Not Found for {callId} in tenant {tenantId}" } });
                }
                catch (ApiException ex)
                {
                    logger.LogError(
                        ex,
                        "Error getting call record from tenant {tenantId}",
                        tenantId);

                    var result = new ObjectResult(new { error = new { message = $"Error getting call record {callId} from tenant {tenantId}" } });
                    result.StatusCode = StatusCodes.Status500InternalServerError;

                    return result;
                }
                catch (ArgumentException ex) when (ex.Message?.Contains("not configured", StringComparison.OrdinalIgnoreCase) == true)
                {
                    logger.LogError(
                        ex,
                        "Error getting call record from tenant {tenantId}",
                        tenantId);
                    return new BadRequestObjectResult(new { error = new { message = $"Tenant {tenantId} is not configured" } });
                }
            }
            try
            {
                var allRecords = await callRecordsGraphContext.GetCallRecordFromConfiguredTenantsAsync(
                        callId.ToString(),
                        cancellationSource.Token)
                    .ConfigureAwait(false);

                var resultString = await allRecords.SerializeAsStringAsync().ConfigureAwait(false);

                return new ContentResult()
                {
                    Content = resultString,
                    ContentType = "application/json",
                    StatusCode = StatusCodes.Status200OK,
                };
            }
            catch (Exception ex) when (ex is AggregateException or ApiException)
            {
                logger.LogError(ex, "Error getting call record from configured tenants");
                var result = new ObjectResult(new { error = new { message = $"Error getting call record from configured tenants" } });
                result.StatusCode = StatusCodes.Status500InternalServerError;
                return result;
            }
        }
    }
}

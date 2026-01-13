using CallRecordInsights.Extensions;
using CallRecordInsights.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using System;
using System.Net;
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

        [Function(nameof(GetCallRecordAdminFunction))]
        public async Task<HttpResponseData> RunAsync(
            [HttpTrigger(AuthorizationLevel.Admin, "get", Route = "callRecords/{callId}/{tenantId?}")]
            HttpRequestData request,
            Guid callId,
            string tenantId = null,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(GetCallRecordAdminFunction),
                nameof(RunAsync),
                DateTime.UtcNow);

            if (!string.IsNullOrEmpty(tenantId))
            {
                try
                {
                    var record = await callRecordsGraphContext.GetCallRecordFromTenantAsync(
                            callId.ToString(),
                            tenantId,
                            cancellationToken,
                            logNotFoundErrors: true)
                        .ConfigureAwait(false);

                    var callRecordString = await record.SerializeAsStringAsync().ConfigureAwait(false);

                    var response = request.CreateResponse(HttpStatusCode.OK);
                    response.Headers.Add("Content-Type", "application/json");
                    await response.WriteStringAsync(callRecordString);
                    return response;
                }
                catch (ApiException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                {
                    var response = request.CreateResponse(HttpStatusCode.NotFound);
                    await response.WriteAsJsonAsync(new { error = new { message = $"Call Record Not Found for {callId} in tenant {tenantId}" } });
                    return response;
                }
                catch (ApiException ex)
                {
                    logger.LogError(
                        ex,
                        "Error getting call record from tenant {tenantId}",
                        tenantId);

                    var response = request.CreateResponse(HttpStatusCode.InternalServerError);
                    await response.WriteAsJsonAsync(new { error = new { message = $"Error getting call record {callId} from tenant {tenantId}" } });
                    return response;
                }
                catch (ArgumentException ex) when (ex.Message?.Contains("not configured", StringComparison.OrdinalIgnoreCase) == true)
                {
                    logger.LogError(
                        ex,
                        "Error getting call record from tenant {tenantId}",
                        tenantId);
                    var response = request.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { error = new { message = $"Tenant {tenantId} is not configured" } });
                    return response;
                }
            }
            try
            {
                var allRecords = await callRecordsGraphContext.GetCallRecordFromConfiguredTenantsAsync(
                        callId.ToString(),
                        cancellationToken)
                    .ConfigureAwait(false);

                var resultString = await allRecords.SerializeAsStringAsync().ConfigureAwait(false);

                var response = request.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(resultString);
                return response;
            }
            catch (Exception ex) when (ex is AggregateException or ApiException)
            {
                logger.LogError(ex, "Error getting call record from configured tenants");
                var response = request.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = new { message = $"Error getting call record from configured tenants" } });
                return response;
            }
        }
    }
}

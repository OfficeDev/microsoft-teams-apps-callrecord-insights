using CallRecordInsights.Services;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CallRecordInsights.Functions
{
    public class AddOrRenewCallRecordsSubscriptionFunction
    {
        private readonly ICallRecordsGraphContext callRecordsGraphContext;
        private readonly ILogger<AddOrRenewCallRecordsSubscriptionFunction> logger;

        public AddOrRenewCallRecordsSubscriptionFunction(
            ICallRecordsGraphContext callRecordsGraphContext,
            ILogger<AddOrRenewCallRecordsSubscriptionFunction> logger)
        {
            this.callRecordsGraphContext = callRecordsGraphContext;
            this.logger = logger;
        }

        [FunctionName(nameof(AddOrRenewCallRecordsSubscriptionFunction))]
        public async Task RunAsync(
            [TimerTrigger("%RenewSubscriptionScheduleCron%")]
            TimerInfo timerInfo,
            CancellationToken cancellationToken = default)
        {
            logger?.LogInformation(
                "{Function}.{Method} triggered at {DateTime}",
                nameof(AddOrRenewCallRecordsSubscriptionFunction),
                nameof(RunAsync),
                DateTime.UtcNow);

            _ = await callRecordsGraphContext.AddOrRenewSubscriptionsForConfiguredTenantsAsync(cancellationToken);
        }
    }
}

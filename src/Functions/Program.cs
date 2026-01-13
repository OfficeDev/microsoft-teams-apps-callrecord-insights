using CallRecordInsights.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, config) =>
    {
        config
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
    })
    .ConfigureServices(services =>
    {
        services
            .AddJsonToKustoFlattener()
            .AddCallRecordsGraphContext()
            .AddCallRecordsDataContext()
            .AddCallRecordsQueueContext();
    })
    .Build();

host.Run();

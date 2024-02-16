# Configuration Settings

> [!NOTE]
> This application was designed to use Identity-Based connections wherever possible, and as such, some of the configuration entries look different than other examples of Azure Function implementations.
> See [this](https://learn.microsoft.com/en-us/azure/azure-functions/functions-reference#configure-an-identity-based-connection) for more information

## `RenewSubscriptionScheduleCron`
This is Cron string for the frequency of renewing the Call Records Notification Subscription from Graph.

This defaults to `0 0 */2 * * *` or every 2 hours.

## `CallRecordsQueueConnection__queueServiceUri`
This is the Queue Service Uri of the storage account which contains the download/processing queue.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to the primary queue endpoint of the storage account the same template creates.

## `CallRecordsQueueConnection__credential`
This is the identity used to connect to the [download/processing queue](#callrecordsqueueconnection__queueserviceuri)

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to `managedidentity`. This should not be changed.

## `CallRecordsToDownloadQueueName`
This is the name of the storage queue to be used as the download/processing queue.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to [KustoCallRecordsDatabaseName](#kustocallrecordsdatabasename-string) + `download`

## `GraphSubscription__NotificationUrl`
This is the `notificationUrl` used in the Call Records [Subscription](https://learn.microsoft.com/en-us/graph/api/resources/subscription#properties) used by the deployment

This will be in the form of the Uri for the Key Vault secret holding the Event Hub connection string for use by the Graph Event Notification Service.

See [Receive change notifications through Azure Event Hubs](https://learn.microsoft.com/en-us/graph/change-notifications-delivery-event-hubs#creating-the-subscription) for more info

## `GraphSubscription__Tenants`
This is a list of tenants the application is configured to monitor. It can be either a tenantId GUID or configured tenant domain. 

By default, this is set to [TenantDomain](deployment.md#tenantdomain-string-required)

If [Multi/Cross Tenant](./multi-tenant-deployment.md) deployment is desired, this will be all the monitored tenants, separated by a semi-colon (`;`)

## `AzureAd`
This section will not be present unless configured manually.
This section is for configuring the app service principal to be used when calling Graph API.
This is only required for [Multi/Cross Tenant](./multi-tenant-deployment.md) deployments, otherwise the managed identity of the function app will be used for all Graph API calls.

This section should be a valid [MicrosoftIdentityOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.identity.web.microsoftidentityoptions) configuration.

The fields `TenantId`, `ClientId`, `Instance`, and either `ClientCredentials` or `ClientCertificates` are required

*Example*:
```
AzureAd__Instance:                                      https://login.microsoftonline.com
AzureAd__TenantId:                                      c0ffee60-0dde-cafc-0ffe-ebadcaffe14e
AzureAd__ClientId:                                      c1d71d1d-ca11-ca11-ca11-defa177ca115
AzureAd__ClientCredentials__0__SourceType:              KeyVault
AzureAd__ClientCredentials__0__KeyVaultUrl:             https://kvcridemo1.vault.azure.net
AzureAd__ClientCredentials__0__KeyVaultCertificateName: CRI-DEMO-1-SPN-CERT
```

## `CallRecordInsightsDb__EndpointUri`
This is the Document Endpoint of the Cosmos DB Account used to store the flattened call records.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to the documentEndpoint of the Cosmos DB Account that was created in [deployCosmos.bicep](../deploy/bicep/deployCosmos.bicep)

## `CallRecordInsightsDb__DatabaseName`
This is the name of the Cosmos DB database used to store the flattened call records.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to the name of the database that was created in [deployCosmos.bicep](../deploy/bicep/deployCosmos.bicep)

## `CallRecordInsightsDb__ProcessedContainerName`
This is the container in the Cosmos DB database used to store the flattened call records.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to the name of the container in the Cosmos DB database that was created in [deployCosmos.bicep](../deploy/bicep/deployCosmos.bicep)

## `GraphNotificationEventHubName`
This is the Event Hub to which the Graph Event Notifications are sent.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to the name of the Event Hub configured in the same template.

## `EventHubConnection__fullyQualifiedNamespace`
This is the namespace of the Event Hub associated with [GraphNotificationEventHubName](#graphnotificationeventhubname)

This is used for the identity-based connection from the function app.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) from the underlying serviceBusEndpoint of the Event Hub configured in the same template.

## `EventHubConnection__credential`
This is the identity used to connect to the [Event Hub](#eventhubconnection__fullyqualifiednamespace)

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to `managedidentity`. This should not be changed.

## `AzureWebJobsStorage__accountName`
This is the name of the storage account used for the function app.

This is used for the identity-based connection from the function app.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) and should not be changed.

## `AzureWebJobsSecretStorageType`
This is the secret storage entry to allow secrets management to occur in Key Vault instead of in the underlying storage account.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to `keyvault`. This should not be changed.

## `AzureWebJobsSecretStorageKeyVaultUri`
This is the secret storage entry to allow secrets management to occur in Key Vault instead of in the underlying storage account.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to the Key Vault Uri created for internal use in the same template.

## `FUNCTIONS_EXTENSION_VERSION`
This is a mandatory Azure Functions configuration entry.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to `~4`. This should not be changed.

## `FUNCTIONS_WORKER_RUNTIME`
This is a mandatory Azure Functions configuration entry.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to `dotnet`. This should not be changed.

## `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING`
This is a mandatory Azure Functions configuration entry.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to a key vault reference to the secret containing the storage account connection string, both of which were created in the same template.

## `WEBSITE_CONTENTSHARE`
This is a mandatory Azure Functions configuration entry.

This is set by [deployFunction.bicep](../deploy/bicep/deployFunction.bicep) to the name of the function app.
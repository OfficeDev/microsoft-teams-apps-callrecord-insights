// Parameters
@description('The object id of the Service Principal for the Graph Change Tracking Application.')
@minLength(36)
@maxLength(36)
param graphChangeTrackingAppObjectId string

@description('The name of the tenant to monitor')
param teamsTenantDomainName string

@description('The base name to use for the resources that will be provisioned. It must start with a letter and contain only letters and numbers. It must be globally unique.')
@minLength(3)
@maxLength(35)
param baseResourceName string = resourceGroup().name

@description('Location for all resources.')
param location string = resourceGroup().location

@description('The type of deployment. Production is a standard deployment. DevTest is a smaller deployment with no SLA.')
@allowed([
  'Production'
  'RestrictedProduction'
  'DevTest'
])
param deploymentType string = 'Production'

@description('The name of the Cosmos DB account to create. Must be between 3 and 44 characters long, and contain only lowercase letters, numbers, and hyphens and start with a letter or number.')
@minLength(3)
@maxLength(44)
param cosmosAccountName string = '${baseResourceName}cdb'

@description('The name of the cosmos database that contains the processed call records table.')
param callRecordsDatabaseName string = 'callrecordinsights'

@description('The name of the cosmos container that contains the processed call records.')
param callRecordsContainerName string = 'records'

param useSeparateKeyVaultForGraph bool = deploymentType != 'DevTest'

param useGraphEventHubManagedIdentity bool = false

// Variables
var tenantId = subscription().tenantId
var tenantDomain = teamsTenantDomainName

var storageAccountName = 'callq${uniqueString(toLower(baseResourceName), toLower(tenantId))}'

var downloadQueueName = '${toLower(callRecordsDatabaseName)}download'

var keyvaultName = length('kv${baseResourceName}') > 24 ? substring('kv${baseResourceName}', 0, 24) : 'kv${baseResourceName}'

var graphKeyVaultName = 'g${length(keyvaultName) > 23 ? substring(keyvaultName, 0, 23) : keyvaultName}'

// T-Shirt sizing
var configurations = {
  DevTest: {
    serverfarm: {
      sku: {
        name: 'Y1'
        // tier: 'Dynamic'
      }
    }
    storage: {
      sku: {
        name: 'Standard_LRS'
      }
      properties: {
        supportsHttpsTrafficOnly: true
        allowBlobPublicAccess: false
        minimumTlsVersion: 'TLS1_2'
      }
    }
    eventHub: {
      sku: {
        name: 'Basic'
      }
      properties: {
        disableLocalAuth: useGraphEventHubManagedIdentity
      }
      eventhubs: {
        properties: {
          messageRetentionInDays: 1
        }
      }
    }
    functionApp: {
      identity: {
        type: 'SystemAssigned'
      }
      properties: {
        clientAffinityEnabled: false
        httpsOnly: true
        siteConfig: {
          alwaysOn: false
          netFrameworkVersion: 'v6.0'
          ftpsState: 'Disabled'
        }
      }
    }
    keyvault: {
      properties: {
        tenantId: subscription().tenantId
        publicNetworkAccess: 'Enabled'
        enableRbacAuthorization: true
        sku: {
          name: 'standard'
          family: 'A'
        }
      }
    }
  }
  Production: {
    serverfarm: {
      sku: {
        name: 'Y1'
        // tier: 'Dynamic'
      }
    }
    storage: {
      sku: {
        name: 'Standard_GRS'
      }
      properties: {
        supportsHttpsTrafficOnly: true
        allowBlobPublicAccess: false
        minimumTlsVersion: 'TLS1_2'
      }
    }
    eventHub: {
      sku: {
        name: 'Basic'
      }
      properties: {
        disableLocalAuth: useGraphEventHubManagedIdentity
      }
      eventhubs: {
        properties: {
          messageRetentionInDays: 1
        }
      }
    }
    functionApp: {
      identity: {
        type: 'SystemAssigned'
      }
      properties: {
        clientAffinityEnabled: false
        httpsOnly: true
        siteConfig: {
          alwaysOn: false
          netFrameworkVersion: 'v6.0'
          ftpsState: 'Disabled'
        }
      }
    }
    keyvault: {
      properties: {
        tenantId: subscription().tenantId
        publicNetworkAccess: 'Enabled'
        enableRbacAuthorization: true
        sku: {
          name: 'standard'
          family: 'A'
        }
      }
    }
  }
  RestrictedProduction: {
    serverfarm: {
      sku: {
        name: 'EP1'
        // tier: 'Premium'
      }
    }
    storage: {
      sku: {
        name: 'Standard_GRS'
      }
      properties: {
        supportsHttpsTrafficOnly: true
        allowBlobPublicAccess: false
        minimumTlsVersion: 'TLS1_2'
        networkAcls: {
          bypass: 'AzureServices'
          defaultAction: 'Deny'
          ipRules: []
          virtualNetworkRules: [
            {
              id: virtualNetwork::fnappSubnet.id
              action: 'Allow'
            }
          ]
        }
      }
    }
    eventHub: {
      sku: {
        name: 'Standard'
      }
      properties: {
        disableLocalAuth: useGraphEventHubManagedIdentity
        // privateEndpointConnections: [{ privateEndpoint: { id: '' } }]
      }
    }
    functionApp: {
      identity: {
        type: 'SystemAssigned'
      }
      properties: {
        clientAffinityEnabled: false
        httpsOnly: true
        siteConfig: {
          alwaysOn: false
          netFrameworkVersion: 'v6.0'
          ftpsState: 'Disabled'
        }
        virtualNetworkSubnetId: virtualNetwork::fnappSubnet.id
      }
    }
    keyvault: {
      properties: {
        tenantId: subscription().tenantId
        // This is set to Enabled because the function app needs to be able to access the key vault, it is restricted by the networkAcls
        publicNetworkAccess: 'Enabled'
        enableRbacAuthorization: true
        sku: {
          name: 'standard'
          family: 'A'
        }
        networkAcls: {
          bypass: 'AzureServices'
          defaultAction: 'Deny'
          virtualNetworkRules: [ { id: virtualNetwork::fnappSubnet.id } ]
          ipRules: useGraphEventHubManagedIdentity ? [] : map(graphChangeNotificationSubnets[environment().name], s => { value: s })
        }
      }
    }
  }
  Custom: {
    // document the requirements to create your own template
  }
}

// Virtual Network
resource virtualNetwork 'Microsoft.Network/virtualNetworks@2022-11-01' = if (startsWith(deploymentType, 'Restricted')) {
  name: 'vnet-${resourceGroup().name}'
  location: location
  properties: { addressSpace: { addressPrefixes: [ '10.0.0.0/16' ] } }
  resource fnappSubnet 'subnets' = {
    name: 'fnapp-subnet'
    properties: {
      addressPrefix: '10.0.1.0/24'
      serviceEndpoints: [
        {
          service: 'Microsoft.KeyVault'
          locations: [ location ]
        }
        {
          service: 'Microsoft.Storage'
          locations: [ location ]
        }
        {
          service: 'Microsoft.Web'
          locations: [ location ]
        }
        {
          service: 'Microsoft.EventHub'
          locations: [ location ]
        }
      ]
      delegations: [
        {
          name: 'fnapp-serverFarms'
          properties: {
            serviceName: 'Microsoft.Web/serverFarms'
          }
        }
      ]
    }
  }
}

// Storage Account
resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: storageAccountName
  location: location
  kind: 'Storage'
  sku: configurations[deploymentType].storage.sku
  properties: configurations[deploymentType].storage.properties
  resource queues 'queueServices' = {
    name: 'default'
    resource download 'queues' = { name: downloadQueueName }
  }
}

var graphChangeNotificationSubnets = loadJsonContent('GraphChangeNotificationSubnets.jsonc')

// Event Hub
resource eventHubNamespace 'Microsoft.EventHub/namespaces@2022-10-01-preview' = {
  name: length(baseResourceName) >= 6 ? baseResourceName : '${baseResourceName}${substring(uniqueString(baseResourceName),0, 6 - length(baseResourceName))}'
  location: location
  sku: configurations[deploymentType].eventHub.sku
  properties: configurations[deploymentType].eventHub.properties
  resource graphEventHub 'eventhubs' = {
    name: 'graphevents'
    properties: configurations[deploymentType].eventHub.eventhubs.properties
    resource senderAuthorizationRule 'authorizationRules' = {
      name: 'sender'
      properties: { rights: [ 'Send' ] }
    }
  }
  resource networkAcl 'networkRuleSets' = if (startsWith(deploymentType, 'Restricted')) {
    name: 'default'
    properties: {
      publicNetworkAccess: 'SecuredByPerimeter'
      trustedServiceAccessEnabled: true
      defaultAction: 'Deny'
      virtualNetworkRules: [ { subnet: { id: virtualNetwork::fnappSubnet.id } } ]
      ipRules: map(graphChangeNotificationSubnets[environment().name], s => { action: 'Allow', ipMask: s })
    }
  }
}

// Cosmos DB account
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-09-15' existing = {
  name: cosmosAccountName
  resource database 'sqlDatabases' existing = {
    name: callRecordsDatabaseName
    resource container 'containers' existing = {
      name: callRecordsContainerName
    }
  }
}

// Function App
resource serverfarm 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: baseResourceName
  location: location
  sku: configurations[deploymentType].serverfarm.sku
}

var eventHubFQDN = split(split(eventHubNamespace.properties.serviceBusEndpoint,'://')[1],':')[0]

var GraphNotificationUrl = useGraphEventHubManagedIdentity /*
*/ ? 'EventHub:https://${eventHubFQDN}/eventhubname/${eventHubNamespace::graphEventHub.name}' /*
*/ : useSeparateKeyVaultForGraph /*
*/    ? 'EventHub:${graphKeyVault::graphEventHubConnectionString.properties.secretUri}' /*
*/    : 'EventHub:${keyvault::graphEventHubConnectionString.properties.secretUri}'

var GraphEndpoints = {
  usgovvirginia : 'graph.microsoft.us'
  usgovarizona : 'graph.microsoft.us'
  usgovtexas : 'graph.microsoft.us'
  usdodcentral : 'dod-graph.microsoft.us'
  usdodeast : 'dod-graph.microsoft.us'
}

resource functionApp 'Microsoft.Web/sites@2022-09-01' = {
  name: '${baseResourceName}-function'
  location: location
  kind: 'functionapp'
  identity: configurations[deploymentType].functionApp.identity
  properties: configurations[deploymentType].functionApp.properties
  resource appSettings 'config' = {
    name: 'appsettings'
    properties: toObject(flatten([
      [
        { key: 'RenewSubscriptionScheduleCron', value: '0 0 */2 * * *' }
        // CallRecords Queue Configuration
        { key: 'CallRecordsQueueConnection__queueServiceUri', value: storageAccount.properties.primaryEndpoints.queue }
        { key: 'CallRecordsQueueConnection__credential', value: 'managedidentity' }
        { key: 'CallRecordsToDownloadQueueName', value: storageAccount::queues::download.name }
    
        // Graph Subscription Manager Configuration
        { key: 'GraphSubscription__NotificationUrl', value: GraphNotificationUrl }
        { key: 'GraphSubscription__Tenants', value: tenantDomain }
    
        { key: 'CallRecordInsightsDb__EndpointUri', value: cosmosAccount.properties.documentEndpoint }
        { key: 'CallRecordInsightsDb__DatabaseName', value: cosmosAccount::database.properties.resource.id }
        { key: 'CallRecordInsightsDb__ProcessedContainerName', value: cosmosAccount::database::container.properties.resource.id }
        
        { key: 'GraphNotificationEventHubName', value: eventHubNamespace::graphEventHub.name }
        
        { key: 'EventHubConnection__fullyQualifiedNamespace', value: eventHubFQDN }
        { key: 'EventHubConnection__credential', value: 'managedidentity' }

        { key: 'AzureWebJobsSecretStorageType', value: 'keyvault' }
        { key: 'AzureWebJobsSecretStorageKeyVaultUri', value: keyvault.properties.vaultUri }
        { key: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { key: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet' }
        { key: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: '@Microsoft.KeyVault(VaultName=${keyvault.name};SecretName=${keyvault::storageAccountConnectionString.name})' }
        { key: 'WEBSITE_CONTENTSHARE', value: toLower(functionApp.name) }
        { key: 'SCM_COMMAND_IDLE_TIMEOUT', value: '1800' }
      ]
      contains(GraphEndpoints, location) ? [ // GCCH/DoD Configuration
        { key: 'GraphSubscription__Endpoint', value: GraphEndpoints[location] }
        { key: 'AzureAd__Instance', value: environment().authentication.loginEndpoint }  
        { key: 'AzureWebJobsStorage__blobServiceUri', value: storageAccount.properties.primaryEndpoints.blob }
        { key: 'AzureWebJobsStorage__queueServiceUri', value: storageAccount.properties.primaryEndpoints.queue }
        { key: 'AzureWebJobsStorage__tableServiceUri', value: storageAccount.properties.primaryEndpoints.table }
      ] : [ // Non-GCCH/DoD Configuration      
        { key: 'AzureWebJobsStorage__accountName', value: storageAccount.name }
      ]]), o => o.key, o => o.value)
    dependsOn: [
        functionAppKeyVaultRoleAssignment // Ensure the function app has access to the key vault before reading referenced secrets
        functionAppEventHubsRoleAssignment
        functionAppStorageRoleAssignment
      ]
  }
}

// Key Vault
resource keyvault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: keyvaultName
  location: location
  properties: configurations[deploymentType].keyvault.properties
  resource storageAccountConnectionString 'secrets' = {
    name: 'StorageAccountConnectionString'
    properties: {
      value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listkeys().keys[0].value}'
      attributes: { enabled: true }
      contentType: 'text/plain'
    }
  }

  resource graphEventHubConnectionString 'secrets' = if (!useGraphEventHubManagedIdentity && !useSeparateKeyVaultForGraph) {
    name: 'GraphEventHubConnectionString'
    properties: {
      value: eventHubNamespace::graphEventHub::senderAuthorizationRule.listkeys().primaryConnectionString
      attributes: { enabled: true }
      contentType: 'text/plain'
    }
  }
}

// If using a separate key vault for the graph, create it here
resource graphKeyVault 'Microsoft.KeyVault/vaults@2023-02-01' = if (!useGraphEventHubManagedIdentity && useSeparateKeyVaultForGraph) {
  name: graphKeyVaultName
  location: location
  properties: configurations[deploymentType].keyvault.properties

  resource graphEventHubConnectionString 'secrets' = {
    name: 'GraphEventHubConnectionString'
    properties: {
      value: eventHubNamespace::graphEventHub::senderAuthorizationRule.listkeys().primaryConnectionString
      attributes: { enabled: true }
      contentType: 'text/plain'
    }
  }
}

// Role Assignments
var roleDefinitionId = {
  StorageAccount: {
    // This is the built-in Storage Account Contributor role. See https://docs.microsoft.com/azure/role-based-access-control/built-in-roles#storage-account-contributor
    Contributor: '17d1049b-9a84-46fb-8f53-869881c3d3ab'
    Queue: {
      Data: {
        // This is the built-in Storage Queue Data Contributor role. See https://docs.microsoft.com/azure/role-based-access-control/built-in-roles#storage-queue-data-contributor
        Contributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
      }
    }
    Blob: {
      Data: {
        // This is the built-in Storage Blob Data Owner role. See https://docs.microsoft.com/azure/role-based-access-control/built-in-roles#storage-blob-data-owner
        Owner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
      }
    }
  }
  EventHubs: {
    Data: {
      // This is the built-in Azure Event Hubs Data Sender role. See https://docs.microsoft.com/azure/role-based-access-control/built-in-roles#event-hubs-data-sender
      Sender: '2b629674-e913-4c01-ae53-ef4638d8f975'
      // This is the built-in Azure Event Hubs Data Receiver role. See https://docs.microsoft.com/azure/role-based-access-control/built-in-roles#event-hubs-data-receiver
      Receiver: 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde'
    }
  }
  KeyVault: {
    Secrets: {
      // This is the built-in Key Vault Secrets Officer role. See https://docs.microsoft.com/azure/role-based-access-control/built-in-roles#key-vault-secrets-officer
      Officer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
      // This is the built-in Key Vault Secrets User role. See https://docs.microsoft.com/azure/role-based-access-control/built-in-roles#key-vault-secrets-user
      User: '4633458b-17de-408a-b874-0445c86b69e6'
    }
  }
  CosmosDB: {
    Data: {
      Contributor: '00000000-0000-0000-0000-000000000002'
    }
  }
}

var neededRoles = {
  functionApp: {
    StorageAccount: [
      roleDefinitionId.StorageAccount.Contributor
      roleDefinitionId.StorageAccount.Queue.Data.Contributor
      roleDefinitionId.StorageAccount.Blob.Data.Owner
    ]
    EventHubs: [
      roleDefinitionId.EventHubs.Data.Receiver
    ]
    KeyVault: [
      roleDefinitionId.KeyVault.Secrets.Officer
    ]
    CosmosDB: [
      roleDefinitionId.CosmosDB.Data.Contributor
    ]
  }
  graphChangeTrackingApp: {
    EventHubs: (useGraphEventHubManagedIdentity) ? [ roleDefinitionId.EventHubs.Data.Sender ] : []
    KeyVault: (!useGraphEventHubManagedIdentity) ? [ roleDefinitionId.KeyVault.Secrets.User ] : []
  }
}

resource functionAppStorageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for role in neededRoles.functionApp.StorageAccount: {
  scope: storageAccount
  name: guid(functionApp.id, role, resourceGroup().id)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}]

resource functionAppEventHubsRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for role in neededRoles.functionApp.EventHubs: {
  scope: eventHubNamespace::graphEventHub
  name: guid(functionApp.id, role, resourceGroup().id)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}]

resource functionAppKeyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for role in neededRoles.functionApp.KeyVault: {
  scope: keyvault
  name: guid(functionApp.id, role, resourceGroup().id)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}]

resource functionAppCosmosDBRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-09-15' = [for role in neededRoles.functionApp.CosmosDB: {
  name: guid(functionApp.id, role, resourceGroup().id)
  parent: cosmosAccount
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', cosmosAccountName, role)
    principalId: functionApp.identity.principalId
    scope: replace(cosmosAccount::database.id, '/sqlDatabases/', '/dbs/')
  }
}]

resource graphChangeTrackingAppRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for role in neededRoles.graphChangeTrackingApp.EventHubs: {
  scope: eventHubNamespace::graphEventHub
  name: guid(graphChangeTrackingAppObjectId, role, resourceGroup().id)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
    principalId: graphChangeTrackingAppObjectId
    principalType: 'ServicePrincipal'
  }
}]

resource graphChangeTrackingAppKeyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for role in neededRoles.graphChangeTrackingApp.KeyVault: {
  scope: useSeparateKeyVaultForGraph ? graphKeyVault::graphEventHubConnectionString : keyvault::graphEventHubConnectionString
  name: guid(graphChangeTrackingAppObjectId, role, resourceGroup().id)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
    principalId: graphChangeTrackingAppObjectId
    principalType: 'ServicePrincipal'
  }
}]

// Outputs
output keyVaultName string = keyvaultName
output appDomain string = functionApp.properties.defaultHostName
output functionName string = functionApp.name
output functionAppIdentity object = functionApp.identity
output functionAppSubnetId string = deploymentType == 'RestrictedProduction' ? functionApp.properties.virtualNetworkSubnetId : ''

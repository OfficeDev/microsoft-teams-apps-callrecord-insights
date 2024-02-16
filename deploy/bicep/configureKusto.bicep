// Parameters
@description('The name of the Kusto cluster.')
param clusterName string

@description('The name of the Cosmos DB account that will be used to store the call records.')
param cosmosAccountName string

@description('The name of the Cosmos DB database that will be used to store the call records.')
param callRecordsDatabaseName string = 'callrecordinsights'

@description('The name of the Cosmos DB container that will be used to store the call records.')
param callRecordsContainerName string = 'records'

@description('The name of the Kusto database.')
param databaseName string = 'CallRecordInsights'

@description('The name of the table to create.')
param tableName string = 'CallRecords'

@description('The name of the view to create.')
param viewName string = '${tableName}View'

@description('The name of the get call records function to create.')
param callRecordsFunctionName string = '${tableName}Func'

param location string = resourceGroup().location

param updateTag string = newGuid()

// Existing Resources
resource kustoCluster 'Microsoft.Kusto/clusters@2022-12-29' existing = {
  name: clusterName
  resource database 'databases' existing = {
    name: databaseName
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-09-15' existing = {
  name: cosmosAccountName
  resource database 'sqlDatabases' existing = {
    name: callRecordsDatabaseName
    resource container 'containers' existing = {
      name: callRecordsContainerName
    }
  }
}

// Deploy the table, view, and functions
resource table 'Microsoft.Kusto/clusters/databases/scripts@2022-12-29' = {
  name: guid(tableName,'createtable')
  parent: kustoCluster::database
  properties: {
    continueOnErrors: false
    forceUpdateTag: updateTag
    scriptContent: replace(replace(replace(loadTextContent('kustoScripts/CreateTable.kql'),'##TABLE_NAME##',tableName),'##VIEW_NAME##',viewName),'##FUNCTION_NAME##',callRecordsFunctionName)
  }
}

resource configure 'Microsoft.Kusto/clusters/databases/scripts@2022-12-29' = {
  name: guid(tableName,'configuretable')
  parent: kustoCluster::database
  properties: {
    continueOnErrors: false
    forceUpdateTag: updateTag
    scriptContent: replace(replace(replace(loadTextContent('kustoScripts/Configure.kql'),'##TABLE_NAME##',tableName),'##VIEW_NAME##',viewName),'##FUNCTION_NAME##',callRecordsFunctionName)
  }
  dependsOn: [
    table // Ensure the table is created before creating the view and function
  ]
}

// Assign the Cosmos DB Account Reader Role
resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(clusterName, tableName, 'fbdf93bf-df7d-467e-a4d2-9458aa1360c8')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'fbdf93bf-df7d-467e-a4d2-9458aa1360c8')
    principalId: kustoCluster.identity.principalId
  }
}

// Assign the Cosmos DB Data Reader Role
resource comsosAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-09-15' = {
  name: guid(clusterName, tableName, '00000000-0000-0000-0000-000000000001')
  parent: cosmosAccount
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', cosmosAccount.name, '00000000-0000-0000-0000-000000000001')
    principalId: kustoCluster.identity.principalId
    scope: replace(replace(cosmosAccount::database::container.id,'/sqlDatabases/','/dbs/'),'/containers/','/colls/')
  }
}

resource kustoIngestion 'Microsoft.Kusto/clusters/databases/dataConnections@2023-08-15' = {
  name: '${tableName}Ingestion'
  location: location
  parent: kustoCluster::database
  kind: 'CosmosDb'
  properties: {
    cosmosDbAccountResourceId: cosmosAccount.id
    cosmosDbDatabase: cosmosAccount::database.properties.resource.id
    cosmosDbContainer: cosmosAccount::database::container.properties.resource.id
    managedIdentityResourceId: kustoCluster.id
    tableName: tableName
    mappingRuleName: 'CallRecordMappingJsonMapping'
  }
  dependsOn: [
    assignment        // Ensure the assignments are done before creating the ingestion
    comsosAssignment
    configure   // Ensure the table and mapping are created before creating the ingestion
  ]
}

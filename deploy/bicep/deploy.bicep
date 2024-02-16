@minLength(3)
@maxLength(24)
param baseResourceName string = resourceGroup().name

@allowed(['DevTest','Production','RestrictedProduction'])
param deploymentSize string = 'Production'

@description('The Azure region that\'s right for you. Not every resource is available in every region.')
param location string = resourceGroup().location

@description('The URL to the git repository to deploy.')
param gitRepoUrl string = 'https://github.com/Microsoft/CallRecordInsights.git'

@description('The branch of the git repository to deploy.')
param gitBranch string = 'main'

@description('The domain of the tenant that will be monitored for Call Records.')
param tenantDomain string

@description('The name of the cosmos account to use.')
param cosmosAccountName string = '${baseResourceName}cdb'

@description('The name of the database to store call records in the cosmos account.')
param cosmosCallRecordsDatabaseName string = 'callrecordinsights'

@description('The name of the container to store call records in the cosmos account.')
param cosmosCallRecordsContainerName string = 'records'

@description('The name of the existing Kusto cluster to use.')
param existingKustoClusterName string = 'NOEXISTINGKUSTOCLUSTER'

@description('The name of the database in the Kusto cluster.')
param kustoCallRecordsDatabaseName string = 'CallRecordInsights'

@description('The name of the table to store processed call records in the Kusto cluster.')
param kustoCallRecordsTableName string = 'CallRecords'

@description('The GUID of the Graph Change Tracking Service Principal for the Subscription\'s tenant.')
@minLength(36)
@maxLength(36)
param graphChangeTrackingSPNObjectId string

param useEventHubManagedIdentity bool = false

module kustoDeploy 'deployKusto.bicep' = {
  name: 'kustoDeploy'
  params: {
    deploymentType: deploymentSize
    location: location
    databaseName: kustoCallRecordsDatabaseName
    existingKustoClusterName: existingKustoClusterName
  }
}

module cosmosDeploy 'deployCosmos.bicep' = {
  name: 'cosmosDeploy'
  params: {
    baseResourceName: baseResourceName
    location: location
    daysToRetainData: 30
    accountName: cosmosAccountName
    databaseName: cosmosCallRecordsDatabaseName
    containerName: cosmosCallRecordsContainerName
  }
}

module functionDeploy 'deployFunction.bicep' = {
  name: 'functionDeploy'
  params: {
    graphChangeTrackingAppObjectId: graphChangeTrackingSPNObjectId
    teamsTenantDomainName: tenantDomain
    baseResourceName: baseResourceName
    location: location
    deploymentType: deploymentSize
    cosmosAccountName: cosmosDeploy.outputs.cosmosDbAccountName
    callRecordsDatabaseName: cosmosDeploy.outputs.databaseName
    callRecordsContainerName: cosmosDeploy.outputs.containerName
    useGraphEventHubManagedIdentity: useEventHubManagedIdentity
  }
}

module kustoConfigure 'configureKusto.bicep' = {
  name: 'kustoConfigure'
  params: {
    clusterName: kustoDeploy.outputs.Name
    cosmosAccountName: cosmosDeploy.outputs.cosmosDbAccountName
    callRecordsDatabaseName: cosmosDeploy.outputs.databaseName
    callRecordsContainerName: cosmosDeploy.outputs.containerName
    databaseName: kustoCallRecordsDatabaseName
    tableName: kustoCallRecordsTableName
    viewName: '${kustoCallRecordsTableName}View'
    callRecordsFunctionName: '${kustoCallRecordsTableName}Func'
    location: location
  }
}

module codeDeploy 'deployAppSource.bicep' = {
  name: 'codeDeploy'
  params: {
    functionAppName: functionDeploy.outputs.functionName
    gitRepoUrl: gitRepoUrl
    gitBranch: gitBranch
  }
}

output appPrincipalprincipalId string = functionDeploy.outputs.functionAppIdentity.principalId
output functionName string = functionDeploy.outputs.functionName
output appDomain string = functionDeploy.outputs.appDomain

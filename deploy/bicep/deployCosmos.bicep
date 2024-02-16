@description('The base name to use for the resources that will be provisioned.')
@minLength(3)
@maxLength(35)
param baseResourceName string = resourceGroup().name

@description('The location to create the resources in.')
param location string = resourceGroup().location

@description('The number of days to retain data in the Cosmos DB container.')
@minValue(1)
@maxValue(365)
param daysToRetainData int = 30

@description('The name of the Cosmos DB account to create. Must be between 3 and 44 characters long, and contain only lowercase letters, numbers, and hyphens and start with a letter or number.')
@minLength(3)
@maxLength(44)
param accountName string = '${baseResourceName}cdb'

@description('The name of the Cosmos DB database to create.')
param databaseName string = 'callrecordinsights'

@description('The name of the Cosmos DB container to create.')
param containerName string = 'records'

resource cosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-09-15' = {
    name: toLower(accountName)
    location: location
    properties: {
        databaseAccountOfferType: 'Standard'
        locations: [
            {
                locationName: location
                failoverPriority: 0
                isZoneRedundant: false
            }
        ]
    }

    resource database 'sqlDatabases' = {
        name: databaseName
        properties: {
            resource: {
                id: databaseName
            }
            options: {
                autoscaleSettings: {
                    maxThroughput: 1000
                }
            }
        }

        resource container 'containers' = {
            name: containerName
            properties: {
                resource: {
                    id: containerName
                    partitionKey: {
                        paths: [
                            '/CallRecordTenantIdContext'
                            '/CallId'
                        ]
                        kind: 'MultiHash'
                        version: 2
                    }
                    indexingPolicy: {
                        automatic: true
                        indexingMode: 'consistent'
                        includedPaths: [
                            { path: '/CallRecordTenantIdContext/?' }
                            { path: '/CallId/?' }
                            { path: '/LastModifiedDateTimeOffset/?' }
                        ]
                        excludedPaths: [
                            { path: '/*' }
                        ]
                        compositeIndexes: [
                            [
                                {
                                    path: '/CallRecordTenantIdContext'
                                    order: 'descending'
                                }
                                {
                                    path: '/CallId'
                                    order: 'descending'
                                }
                                {
                                    path:'/LastModifiedDateTimeOffset'
                                    order: 'descending'
                                }
                            ]
                        ]
                    }
                    defaultTtl: daysToRetainData * 24 * 60 * 60
                }
            }
        }
    }
}

output cosmosDbAccountName string = cosmosDbAccount.name
output databaseName string = cosmosDbAccount::database.name
output containerName string = cosmosDbAccount::database::container.name

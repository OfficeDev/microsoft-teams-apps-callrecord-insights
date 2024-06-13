// Parameters
@description('The base name to use for the resources that will be provisioned.')
param clusterNamePrefix string = 'crinsights'

@description('Location for all resources.')
param location string = resourceGroup().location

@description('The type of deployment. Production is a standard deployment. DevTest is a smaller deployment with no SLA.')
@allowed([
  'Production'
  'RestrictedProduction'
  'DevTest'
])
param deploymentType string = 'Production'

@description('The name of the Kusto database.')
param databaseName string = 'CallRecordInsights'

@description('The name of the existing Kusto cluster to use.')
param existingKustoClusterName string = 'NOEXISTINGKUSTOCLUSTER'

@description('The type of identity to assign to the cluster. SystemAssigned is the default. None will not assign an identity. UserAssigned will assign the identity specified in the identity parameter.')
param identity string = 'SystemAssigned'

var isGovCloud = startsWith(location, 'usgov') || startsWith(location, 'usdod')

// T-Shirt sizing
var clusterConfigurations = {
  DevTest: {
    cluster: {
      sku: {
        name: isGovCloud ? 'Dev(No SLA)_Standard_D11_v2' : 'Dev(No SLA)_Standard_E2a_v4'
        tier: 'Basic'
        capacity: 1
      }
      properties: {
        enableAutoStop: true
      }
    }
    database: {
      properties: {
        softDeletePeriod: 'P180D'
        hotCachePeriod: 'P7D'
      }
    }
  }
  Production: {
    cluster: {
      sku: {
        name: isGovCloud ? 'Standard_D11_v2' : 'Standard_E2ads_v5'
        tier: 'Standard'
        capacity: 2
      }
      properties: {}
    }
    database: {
      properties: {
        softDeletePeriod: 'P365D'
        hotCachePeriod: 'P31D'
      }
    }
  }
  // this will eventually be locked down network wise, but for now, just using the same as production
  RestrictedProduction: {
    cluster: {
      sku: {
        name: isGovCloud ? 'Standard_D11_v2' : 'Standard_E2ads_v5'
        tier: 'Standard'
        capacity: 2
      }
      properties: {}
    }
    database: {
      properties: {
        softDeletePeriod: 'P365D'
        hotCachePeriod: 'P31D'
      }
    }
  }
}

var clusterIdentity = identity == 'SystemAssigned' || identity == 'None'/*
                */? {
                      type: identity
                    } /*
                */: {
                      type: 'UserAssigned'
                      userAssignedIdentities: {
                        '${identity}': {}
                      }
                    }

                    
// clean up the resource name
// I would prefer this to be in a function/module, but the current limitations of func and module do not allow for this easily
var input = clusterNamePrefix
var validChars = ['alpha','numeric']
var validFirstChars = ['alpha','numeric'] // assumed to be subset of validChars
var validEndChars = ['alpha','numeric'] // assumed to be subset of validChars
var maxLength = 24
var upper = ['A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z']
var lower = ['a','b','c','d','e','f','g','h','i','j','k','l','m','n','o','p','q','r','s','t','u','v','w','x','y','z']
var charSets = {
  upper: upper
  lower: lower
  alpha: concat(upper,lower)
  numeric: ['0','1','2','3','4','5','6','7','8','9']
  hyphen: ['-']
  underscore: ['_']
  period: ['.']
}
var validCharSet = flatten(map(validChars , s => charSets[s]))
var validFirstCharSet = flatten(map(validFirstChars , s => charSets[s]))
var validEndCharSet = flatten(map(validEndChars , s => charSets[s]))

// get a unique string based on the given seed
var caseShifted = toLower('${input}${(uniqueString(resourceGroup().id))}')
var cleanedUnique = reduce(map(range(0, length(caseShifted)-1), i => substring(caseShifted,i,1)), '', (c,n) => '${c}${(c == '-' && n == '-' ? '' : n)}')
var indexedChars = items(toObject(range(0,length(cleanedUnique)),i => '${i}', i => substring(cleanedUnique,i,1)))
var firstIndexedChar = sort(filter(indexedChars, c => contains(validFirstCharSet, c.value)), (c,n) => int(c.key) < int(n.key))[0]
var endIndexedChar = sort(filter(indexedChars, c => int(c.key) > int(firstIndexedChar.key) && (maxLength > -1 && int(c.key) < (int(firstIndexedChar.key) + maxLength)) && contains(validEndCharSet, c.value)), (c,n) => int(c.key) > int(n.key))[0]
var validIndexedChars = sort(filter(indexedChars, c => int(c.key) > int(firstIndexedChar.key) && int(c.key) < int(endIndexedChar.key) && contains(validCharSet, c.value)), (c,n) => int(c.key) < int(n.key))
var clusterResourceName = '${firstIndexedChar.value}${reduce(validIndexedChars, '', (c,n) => '${c}${n.value}')}${endIndexedChar.value}'

var existing = !empty(trim(existingKustoClusterName)) && existingKustoClusterName != 'NOEXISTINGKUSTOCLUSTER'

resource existingCluster 'Microsoft.Kusto/clusters@2023-08-15' existing = if (existing) {
  name: existingKustoClusterName
}

resource existingDatabase 'Microsoft.Kusto/clusters/databases@2023-08-15' = if (existing) {
  name: '${existing ? existingKustoClusterName : 'bogus'}/${databaseName}'
  location: location
  properties: clusterConfigurations[deploymentType].database.properties
  kind: 'ReadWrite'
}

resource newCluster 'Microsoft.Kusto/clusters@2023-08-15' = if (!existing) {
  name: clusterResourceName
  location: location
  sku: clusterConfigurations[deploymentType].cluster.sku
  identity: clusterIdentity
  properties: clusterConfigurations[deploymentType].cluster.properties
  resource database 'databases' = {
    name: databaseName
    location: location
    properties: clusterConfigurations[deploymentType].database.properties
    kind: 'ReadWrite'
  }
}

// Outputs
output Name string = existing ? existingCluster.name : newCluster.name
output Uri string = existing ? existingCluster.properties.uri : newCluster.properties.uri
output DatabaseName string = existing ? existingDatabase.name : newCluster::database.name

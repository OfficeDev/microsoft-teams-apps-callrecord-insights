# Deployment
Follow the steps below to deploy the application:

## Retrieve Deployment Scripts

### Fork Repo (optional)

> [!WARNING]
> This step is required if you want to make any customizations to the solution being deployed
  
![](./media/github-fork.png)

### Download the zip file containing the source and deployment scripts

> [!NOTE]
> If you performed the [step above](#fork-repo-optional), this should be done from your new repository

![](./media/github-download-code.png)

### Unblock the zip file
This is required to prevent issues with script ps1 script executions.

![](./media/unblock-file.png)

### Extract the zip file
The deployment script(s) will be executed from this directory. Make note of where this was stored.

## Define deployment parameters

### `[-ResourceGroupName]` \<string\> **Required**
This is the name that is used for the Azure Resource Group that Call Record Insights resources will be deployed to. If this does not exist it will be created.

### `[-BaseResourceName]` \<string\>
This is the name of which all resources are based upon. If BaseResourceName is not specified ResourceGroupName is used.

**Default**: [ResourceGroupName](#resourcegroupname-string-required)

### `[-SubscriptionId]` \<string\> **Required**
This is the subscription id of the subscription to which the application will be deployed.

### `[-DeploymentSize]` \<string\>
This defines the SKUs of each component (Kusto, Function App, Storage Account).

Options are:
- DevTest
- Production
- ProductionRestricted

**Default**: `Production`

### `[-Location]` \<string\>
This is the Azure region location where the application (and all associated resources) will be deployed.

[Choose the Right Azure Region for You \| Microsoft Azure](https://azure.microsoft.com/en-us/explore/global-infrastructure/geographies/#overview)

*Example*: `centralus`

**Default**: `westus`

### `[-GitRepoUrl]` \<string\>
This is the GIT Repo URL that you are deploying.

If you [forked](#fork-repo-optional) the repo, then this is **Required** and must be set to the url hosting your repository

*Example*: `https://github.com/{organization}/callrecord-insights.git`

If you are using a private repository, then a Personal Access Token must be created and passed in this parameter

*Example*: `https://{username}:{PAT}@github.com/{organization}/callrecord-insights.git`

**Default**: `https://github.com/OfficeDev/microsoft-teams-apps-callrecord-insights.git`

### `[-GitBranch]` \<string\>
This is the name of the branch of the repository you wish to deploy

**Default**: `main`

### `[-TenantDomain]` \<string\> **Required**
This is the domain name associated with your Tenant.

*Example*: `contoso.com`

### `[-CosmosAccountName]` \<string\>
The account name used for Cosmos DB.

**Default**: [BaseResourceName](#baseresourcename-string) + `cdb`

### `[-CosmosCallRecordsDatabaseName]` \<string\>
The database name used for the Call Records Database in Cosmos DB.

**Default**: `CallRecordInsights`

### `[-CosmosCallRecordsContainerName]` \<string\>
The CallRecords container name in Cosmos DB.

**Default**: `records`

### `[-ExistingKustoClusterName]` \<string\>
The Kusto cluster name if deploying to an existing KustoCluster

### `[-KustoCallRecordsDatabaseName]` \<string\>
The database name inside of the Kusto cluster of the Call Records database

### `[-KustoCallRecordsTableName]` \<string\>
The table name inside of the Kusto database

### `[-KustoCallRecordsViewName]` \<string\>
The name of the view that de-duplicates records in case of duplication due to updated call record ingestion.

### `[-UseEventHubManagedIdentity]` \<switch\>
Reserved for future use, currently breaks all functionality.

> [!CAUTION]
> Do not use this parameter

## Execute deploy.ps1
Using the parameters defined [above](#define-deployment-parameters)

*Example:* 
```powershell
$DeploymentParameters = @{
  ResourceGroupName = 'cridemo1rg'
  BaseResourceName  = 'cridemo1'
  SubscriptionId    = 'ba5eba11-beef-ba5e-ba11-beefba5eba11'
  TenantDomain      = 'contoso.com'
}
.\deploy.ps1 @DeploymentParameters
```

Wait for deployment script to complete

# Deployment Validation & Verification

Once deployment steps complete (successfully or unsuccessfully) the deployment script calls the admin function to get the health state of the deployment. The output indicates whether all required services are up and healthy.

## Deployment Validation
Once the deployment script is completed, the final output will be the deployment health.

If the output of `$HealthState` is healthy, then you will receive the following message:  
**`App deployment is healthy.`**

### Deployment Health Details
`$HealthState` is the variable which will contain details regarding the health state of the deployment

![](./media/deploy-ps1-health-state.png)

#### healthy
This indicates if the deployment is overall healthy or unhealthy.

#### eventHub
This provides the health status of the EventHub and the EventHub Url

#### cosmos
This provides the health status of the Cosmos DB and the Cosmos DB Url for the call records db.

#### downloadQueue
This provides the health status of the Azure Storage Queue and the Azure Storage Queue Url for the download queue.

#### subscriptions
This provides the health status of the subscription to the Call Records Graph API endpoint and the expiration date of the subscription.

#### unhealthyServices
Lists all services above that were found to be unhealthy.

### Check Cosmos DB Ingestion/Data Connection

From portal.azure.com, 
Navigate to Resource Group -> Kusto Cluster -> Databases -> Database -> CosmosDB

![](./media/data-explorer-portal-database.png)
![](./media/data-explorer-portal-data-connections.png)

### Check Subscription Status in Graph
![](./media/graph-explorer-list-subscriptions.png)

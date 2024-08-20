using namespace System.Text
using namespace System.Collections
using namespace System.Collections.Generic
using namespace System.Management.Automation
using namespace System.Runtime.InteropServices

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]
    $ResourceGroupName,

    [Parameter()]
    [ValidatePattern('^[a-z][a-z\d-]{1,20}[a-z\d]$')]
    [string]
    $BaseResourceName = $ResourceGroupName,

    [Parameter(Mandatory)]
    [string]
    $SubscriptionId,

    [ValidateSet('DevTest','Production','RestrictedProduction')]
    $DeploymentSize = 'Production',

    [Parameter(HelpMessage = 'The Azure region that''s right for you. Not every resource is available in every region.')]
    [ValidateSet('australiaeast', 'brazilsouth', 'canadacentral', 'centralindia', 'centralus', 'eastasia', 'eastus', 'eastus2',
        'francecentral', 'germanywestcentral', 'japaneast', 'koreacentral', 'northcentralus', 'northeurope', 'norwayeast',
        'polandcentral', 'southafricanorth', 'southcentralus', 'southeastasia', 'swedencentral', 'switzerlandnorth',
        'uaenorth', 'uksouth', 'westcentralus', 'westeurope', 'westus', 'westus2', 'westus3',
        'usgovvirginia', 'usgovarizona', 'usgovtexas', 'usdodcentral')]
    [string]
    $Location = 'westus',

    [Parameter(HelpMessage = 'The URL to the git repository to deploy.')]
    [AllowEmptyString()]
    [string]
    $GitRepoUrl = 'https://github.com/OfficeDev/microsoft-teams-apps-callrecord-insights.git',

    [Parameter(HelpMessage = 'The branch of the git repository to deploy.')]
    [AllowEmptyString()]
    [string]
    $GitBranch = 'main',

    [Parameter(HelpMessage = 'The domain of the tenant that will be monitored for Call Records.')]
    [string]
    $TenantDomain,

    [Parameter(HelpMessage = 'The name of the cosmos account to use.')]
    [string]
    $CosmosAccountName = "${BaseResourceName}cdb",

    [Parameter(HelpMessage = 'The name of the database to store call records in the cosmos account.')]
    [string]
    $CosmosCallRecordsDatabaseName = 'callrecordinsights',

    [Parameter(HelpMessage = 'The name of the container to store call records in the cosmos account.')]
    [string]
    $CosmosCallRecordsContainerName = 'records',

    [Parameter(HelpMessage = 'The name of the existing Kusto cluster to use.')]
    [string]
    $ExistingKustoClusterName,

    [Parameter(HelpMessage = 'The name of the database in the Kusto cluster.')]
    [string]
    $KustoCallRecordsDatabaseName = 'CallRecordInsights',

    [Parameter(HelpMessage = 'The name of the table to store processed call records in the Kusto cluster.')]
    [string]
    $KustoCallRecordsTableName = 'CallRecords',

    [Parameter(HelpMessage = 'The name of the view to store processed call records in the Kusto cluster.')]
    [string]
    $KustoCallRecordsViewName = $KustoCallRecordsTableName + 'View',

    [Parameter()]
    [bool]
    $UseEventHubManagedIdentity = $false
)

if (!(Get-Command -Name az -CommandType Application -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI is not installed. Please install Azure CLI from https://aka.ms/az-cli." -ErrorAction Stop
    return
}

$GraphEndpoint = switch ($Location) {
    { $_ -in @('usgovvirginia', 'usgovarizona', 'usgovtexas', 'usgoviowa') } { 'graph.microsoft.us' }
    { $_ -in @('usdodcentral','usdodeast') } { 'dod-graph.microsoft.us' }
    default { 'graph.microsoft.com' }
}

$AzureCloud = switch ($GraphEndpoint) {
    { $_ -in @('usgovvirginia', 'usgovarizona', 'usgovtexas', 'usgoviowa') } { 'AzureUSGovernment' }
    { $_ -in @('usdodcentral','usdodeast') } { 'AzureUSGovernment' }
    default { 'AzureCloud' }
}

$AzCommands = @{
    getdeployment                  = { az deployment group show --resource-group $args[0] --name $args[1] --query properties 2>&1 }
    deploybicep                    = {
        param(
            [string]
            $ResourceGroupName,
            [string]
            $TemplateFile,
            [Hashtable]
            $TemplateParameterObject
        )
        $parameters = & {
            'deployment'
            'group'
            'create'
            '--resource-group'
            $ResourceGroupName
            '--mode'
            'Incremental'
            '--template-file'
            $TemplateFile
            '--no-prompt'
            'true'
            '--no-wait'
            '--query'
            'properties'
            '--parameters'
            $TemplateParameterObject.Keys.ForEach({
                $value = $TemplateParameterObject[$_]
                if($value -isnot [string] -and $value -isnot [ValueType] -and ($value -is [Collections.IDictionary] -or $value -is [PSObject])) {
                    $value = ($value | ConvertTo-Json -Compress -Depth 99).Replace('"','\"')
                }
                '{0}={1}' -f $_,$value
            })
        }
        az @parameters 2>&1
    }  
    getdeploymentoperations        = { az deployment operation group list --resource-group $args[0] --name $args[1] --query "[].{provisioningState:properties.provisioningState,targetResource:properties.targetResource.id,statusMessage:properties.statusMessage.error.message}" 2>&1 }
    getenvironment                 = { az cloud show --query name 2>&1 }
    setenvironment                 = { az cloud set --name $args[0] 2>&1 }
    getazsubscription              = { az account show --query id 2>&1 }
    connect                        = { az login 2>&1; if ($LASTEXITCODE -eq 0) { az account set --subscription $args[0] 2>&1 } }
    getazusername                  = { az ad signed-in-user show --query userPrincipalName 2>&1 }
    getazoid                       = { az ad signed-in-user show --query id 2>&1 }
    getaztenant                    = { az account show --query tenantId 2>&1 }
    getspnobjectid                 = { az ad sp list --spn $args[0] --query "[].id" 2>&1 }
    createspn                      = { az ad sp create --id $args[0] --query "[].id" 2>&1 }
    getresourcegroup               = { az group show --name $args[0] 2>&1 }
    createresourcegroup            = { az group create --name $args[0] --location $args[1] 2>&1 }
    getaadapproleassignments       = { az rest --method get --url "https://${GraphEndpoint}/v1.0/servicePrincipals/$($args[0])/appRoleAssignments" 2>&1 }
    addaadapproleassignment        = {
        $request = @{
            principalId = $args[0]
            resourceId  = $args[1]
            appRoleId   = $args[2]
        }
        $body = ($request | ConvertTo-Json -Compress).Replace('"', '\"')
        az rest --method post --url "https://${GraphEndpoint}/v1.0/servicePrincipals/$($args[0])/appRoleAssignments" --body "$body" 2>&1
    }
    getwebappdeployment            = { az webapp log deployment show --resource-group $args[0] --name $args[1] 2>&1 }
    getmasterkey                   = { az functionapp keys list --resource-group $args[0] --name $args[1] --query masterKey 2>&1 }
    getwebappdeploymentlogs        = { az webapp log deployment show --resource-group $args[0] --name $args[1] --query "[].{time:log_time,message:message,url:details_url}" 2>&1 }
    getwebapppublishingcredentials = { az webapp deployment list-publishing-credentials --resource-group $args[0] --name $args[1] --query "{pwd:publishingPassword,un:publishingUserName}" 2>&1 }
    getwebappdeploymentlogdetails  = { az rest --uri ($args[0] -replace '(?<=://)',"$($args[1].un):$($args[1].pwd)@") --skip-authorization-header --query "[].{time:log_time,message:message,url:details_url}" 2>&1 }
}

function FormatDictionary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [IDictionary]
        $Hashtable
    )
    $sb = [StringBuilder]::new('@{')
    $first = $true
    foreach ($key in $Hashtable.psbase.Keys) {
        if (!$first) { $null = $sb.Append(';') }
        $first = $false
        $null = $sb.AppendFormat('{0}=', (FormatItem $key -UnquoteIfValid)).Append((FormatItem $Hashtable[$key]))
    }
    return $sb.Append('}').ToString()
}

function FormatItem {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [AllowNull()]
        [object]
        $Item,

        [switch]
        $UnquoteIfValid
    )
    if ($null -eq $Item) {
        return '$null'
    }
    if ($Item -is [string]) {
        if (!$UnquoteIfValid -or [Regex]::IsMatch($key, '[^\dA-Za-z_]')) {
            return "'{0}'" -f $Item.Replace("'", "''")
        }
        return $Item
    }
    if ($Item -is [Guid]) {
        return "[Guid]::Parse('{0}')" -f $Item
    }
    if ($Item -is [bool]) {
        return '${0}' -f $Item.ToString().ToLowerInvariant()
    }
    if ($Item -is [IDictionary]) {
        return FormatDictionary $Item
    }
    if ($Item -is [ICollection]) {
        $sb = [StringBuilder]::new('@(')
        $first = $true
        foreach ($value in $Item) {
            if (!$first) { $null = $sb.Append(',') }
            $first = $false
            $null = $sb.Append((FormatItem $value))
        }
        return $sb.Append(')').ToString()
    }
    return $Item.ToString()
}

function TryExecuteMethod {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [string]
        $MethodName,

        [Parameter(ValueFromRemainingArguments)]
        [object[]]
        $Replacements
    )
    $Result = $null
    $GeneratedErrors = $null
    try {
        $Expression = $AzCommands[$MethodName]
        $DebugString = "{$($Expression.ToString().Trim())}.Invoke($(FormatItem $Replacements))"
        Write-Verbose $DebugString
        $Result = $Expression.Invoke($Replacements) | Where-Object {
            if ($_ -isnot [ErrorRecord]) { 
                return $true
            }
            $er = if($_.Exception -is [RemoteException]) { $_ } else { $_.ErrorRecord }
            $message = $er.Exception.Message
            Write-Verbose "stderr: $message"
            if (![string]::IsNullOrEmpty($message) -and !$message.StartsWith('WARNING:') -and !$message.Contains('You are using cryptography on a 32-bit Python on a 64-bit Windows Operating System.')) {
                if ($null -eq $GeneratedErrors) { $GeneratedErrors = [List[ErrorRecord]]@() }
                $GeneratedErrors.Add($_)
            }
            return $false
        } | ConvertFrom-Json
    }
    catch {
        if ($null -eq $GeneratedErrors) { $GeneratedErrors = [List[ErrorRecord]]@() }
        $GeneratedErrors.Add($_)
        $Result = $null
    }
    if($GeneratedErrors.Count -eq 0) { $GeneratedErrors = $null }
    return @($GeneratedErrors, $Result)
}

function deployifneeded {
    param (
        [Parameter(Mandatory, Position = 0)]
        [string]
        $DeploymentType,
        [Parameter(Mandatory, Position = 1)]
        [string]
        $DeploymentName,
        [Parameter(Mandatory, Position = 2)]
        [hashtable]
        $DeploymentParams,

        [switch]
        $SkipDeployment
    )
    $StatusMessages = [HashSet[string]]@()
    Write-Host "Checking $DeploymentType deployment in resource group '$ResourceGroupName'."
    
    $Errors, $Deployment = TryExecuteMethod getdeployment $ResourceGroupName $DeploymentName
    if (!$SkipDeployment -and (!$Deployment -or $Errors -or ($Deployment.provisioningState -in @('cancelled','failed')))) {
        Write-Host "Deploying $DeploymentType to resource group '$ResourceGroupName'. This may take several minutes."
        $bicepFile = Join-Path $PSScriptRoot (Join-Path bicep "${DeploymentName}.bicep")
        $Errors, $Deployment = TryExecuteMethod deploybicep $ResourceGroupName $bicepFile $DeploymentParams
        if ($Errors) {
            Write-Error "Failed to create $DeploymentType in resource group '$ResourceGroupName'. Please ensure you have access to the subscription and try again."
            $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
            return $null
        }
        Start-Sleep -Seconds (Get-Random -Minimum 10 -Maximum 30)
    }
    $First = $true
    while ($Deployment.provisioningState -ne 'succeeded') {
        if (!$First) { Start-Sleep -Seconds (Get-Random -Minimum 10 -Maximum 30) }
        $First = $false
        $Errors, $Deployment = TryExecuteMethod getdeployment $ResourceGroupName $DeploymentName
        if ($Errors) {
            Write-Error "Could not get $DeploymentType deployment in resource group '$ResourceGroupName'. Please ensure you have access to the subscription and try again."
            $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
            return $null
        }

        $nestedDeployments = [Stack[string]]@($DeploymentName)
        $processedDeployments = [HashSet[string]]@()

        while ($nestedDeployments.Count -gt 0) {
            $nestedDeploymentName = $nestedDeployments.Pop()
            if (!$processedDeployments.Add($nestedDeploymentName)) { continue }
            $Errors, $OperationState = TryExecuteMethod getdeploymentoperations $ResourceGroupName $nestedDeploymentName
            if (!$Errors) {
                $OperationState | ForEach-Object {
                    if (!$_.targetResource) { return }
                    if ($_.targetResource.Split('/')[-2] -eq 'deployments') {
                        $nestedDeployments.Push($_.targetResource.Split('/')[-1])
                    }
                    $verb = if ($_.provisioningState.EndsWith('ed')) { 'has' } else { 'is' }
                    $Message = "Deploy Resource: $($_.targetResource.Split('/providers/',2)[1]) $verb $($_.provisioningState)."
                    if ($StatusMessages.Add($Message)) {
                        $Color = if ($Message -match 'has Succeeded') {
                            'Green'
                        } elseif ($Message -match 'has Failed') {
                            'Red'
                        } else {
                            'White'
                        }
                        Write-Host $Message -ForegroundColor $Color
                        if ($_.provisioningState -eq 'Failed') {
                            Write-Warning "$($_.statusMessage)"                           
                        }
                    }
                }
            }
        }

        # $state = $Deployment.provisioningState.ToLower()
        $verb = if ($Deployment.provisioningState.EndsWith('ed')) { 'has' } else { 'is' }

        $Message = "$DeploymentType deployment $verb $($Deployment.provisioningState)."
        if ($StatusMessages.Add($Message)) {
            $Color = if ($Message -match 'has Succeeded') {
                'Green'
            } elseif ($Message -match 'has Failed') {
                'Red'
            } else {
                'White'
            }
            Write-Host $Message -ForegroundColor $Color
        }

        $Errors = $null
        switch ($Deployment.provisioningState) {
            'cancelled' {
                Write-Warning "$DeploymentType in resource group '$ResourceGroupName' deployment was cancelled! Please try again."
                return $null
            }
            'failed' {
                Write-Error "$DeploymentType deployment in resource group '$ResourceGroupName' failed. Please ensure you have access to the subscription and try again."
                return $null
            }
        }
    }
    Write-Host "$DeploymentType has been deployed successfully." -ForegroundColor Green
    Write-Host
    return $Deployment
}

# Try to get current signed-in subscription id, we'll use this to determine if we need to connect all together or connect to a different subscription
Write-Host "Ensuring we are connected to subscription '$SubscriptionId'."
$Errors, $ConnectedSubscriptionId = TryExecuteMethod getazsubscription

if ($Errors -or $null -eq $ConnectedSubscriptionId -or $ConnectedSubscriptionId -ne $SubscriptionId) {
    $Errors, $CurrentAzureCloud = TryExecuteMethod getenvironment $AzureCloud
    if ($Errors) {
        Write-Error "Failed to get the current environment. Please ensure you have access to the subscription and try again."
        $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
        return
    }
    if ($CurrentAzureCloud -ne $AzureCloud) {
        Write-Host "Setting the environment to '$AzureCloud'."
        $Errors, $null = TryExecuteMethod setenvironment $AzureCloud
        if ($Errors) {
            Write-Error "Failed to set the environment to '$AzureCloud'. Please ensure you have access to the subscription and try again."
            $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
            return
        }
    }

    Write-Host "Connecting to subscription '$SubscriptionId'. Please login if prompted."
    $Errors, $null = TryExecuteMethod connect $SubscriptionId
    if ($Errors) {
        Write-Error "Failed to connect to subscription '$SubscriptionId'. Please ensure you have access to the subscription and try again."
        return
    }
}
# Get Logged In User
$Errors, $AdminUser = TryExecuteMethod getazusername
if ($Errors) {
    Write-Host "Connecting to subscription '$SubscriptionId'. Please login if prompted."
    $Errors, $null = TryExecuteMethod connect $SubscriptionId
    if ($Errors) {
        Write-Error "Failed to get identity of signed in user! Please ensure you are signed in and try again."
        $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
        return
    }
    $Errors, $AdminUser = TryExecuteMethod getazusername
    if ($Errors) {
        Write-Error "Failed to get identity of signed in user! Please ensure you are signed in and try again."
        $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
        return
    }
}
$Errors, $AdminOID = TryExecuteMethod getazoid
if ($Errors) {
    Write-Error "Failed to get identity of signed in user! Please ensure you are signed in and try again."
    $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
    return
}
$Errors, $TenantId = TryExecuteMethod getaztenant
if ($Errors) {
    Write-Error "Failed to get tenant id for subscription '$SubscriptionId'. Please ensure you have access to the subscription and try again."
    $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
    return
}
Write-Host "Connected to subscription '$SubscriptionId' in tenant '$TenantId' as '$AdminUser'." -ForegroundColor Green
Write-Host

if ([string]::IsNullOrEmpty($TenantDomain)) {
    $TenantDomain = $AdminUser.Split('@')[1]
}

# Try to get the SPN OID for the Graph Events Change Tracking app
Write-Host "Getting SPN object id for the Microsoft Graph Change Tracking app."
$Errors, $GraphChangeTrackingSPNObjectId = TryExecuteMethod getspnobjectid '0bf30f3b-4a52-48df-9a82-234910c4a086'
if ($Errors) {
    Write-Error "Failed to get the SPN object id for the Microsoft Graph Change Tracking app. Please ensure you have access to the tenant and try again."
    $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
    return
}
if ($null -eq $GraphChangeTrackingSPNObjectId) { 
    Write-Host "Creating SPN for the Microsoft Graph Change Tracking app."
    $Errors, $GraphChangeTrackingSPNObjectId = TryExecuteMethod createspn '0bf30f3b-4a52-48df-9a82-234910c4a086'
    if ($Errors) {
        Write-Error "Failed to create the SPN object id for the Microsoft Graph Change Tracking app. Please ensure you have access to the tenant and try again."
        $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
        return
    }
}
if ($GraphChangeTrackingSPNObjectId -isnot [string]) { $GraphChangeTrackingSPNObjectId = $GraphChangeTrackingSPNObjectId[0] }
Write-Host "SPN object id for the Microsoft Graph Change Tracking app is '$GraphChangeTrackingSPNObjectId'." -ForegroundColor Green
Write-Host

# Try to get the resource group, if it doesn't exist, create it
Write-Host "Checking if resource group '$ResourceGroupName' exists."
$null, $ResourceGroup = TryExecuteMethod getresourcegroup $ResourceGroupName
if ($null -eq $ResourceGroup) {
    Write-Host "Creating resource group '$ResourceGroupName' in location '$Location'."
    $Errors, $null = TryExecuteMethod createresourcegroup $ResourceGroupName $Location
    if ($Errors) {
        Write-Error "Failed to create resource group '$ResourceGroupName' in location '$Location'. Please ensure you have access to the subscription and try again."
        $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
        return
    }
}
Write-Host "Resource group '$ResourceGroupName' exists." -ForegroundColor Green
Write-Host

$Deployment = deployifneeded 'Solution' deploy (@{
    baseResourceName               = $BaseResourceName
    deploymentSize                 = $DeploymentSize
    location                       = $Location
    gitRepoUrl                     = $GitRepoUrl
    gitBranch                      = $GitBranch
    tenantDomain                   = $TenantDomain
    cosmosAccountName              = $CosmosAccountName
    cosmosCallRecordsDatabaseName  = $CosmosCallRecordsDatabaseName
    cosmosCallRecordsContainerName = $CosmosCallRecordsContainerName
    existingKustoClusterName       = $ExistingKustoClusterName
    kustoCallRecordsDatabaseName   = $KustoCallRecordsDatabaseName
    kustoCallRecordsTableName      = $KustoCallRecordsTableName
    graphChangeTrackingSPNObjectId = $GraphChangeTrackingSPNObjectId
    useEventHubManagedIdentity     = $UseEventHubManagedIdentity
})
if (!$Deployment) { return }

$appPrincipalprincipalId = $Deployment.outputs.appPrincipalprincipalId.value
$functionName = $Deployment.outputs.functionName.value
$appDomain = $Deployment.outputs.appDomain.value

Write-Host "Getting SPN object id for Microsoft Graph."
$Errors, $MicrosoftGraphSpn = TryExecuteMethod getspnobjectid '00000003-0000-0000-c000-000000000000'
if ($Errors) {
    Write-Error "Failed to get the SPN object id for Microsoft Graph. Please ensure you have access to the tenant and try again."
    $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
    return
}
if ($MicrosoftGraphSpn -isnot [string]) { $MicrosoftGraphSpn = $MicrosoftGraphSpn[0] }
Write-Host "SPN object id for Microsoft Graph is '$MicrosoftGraphSpn'." -ForegroundColor Green

Write-Host "Configuring Function App Identity with required permissions."
$Errors, $CurrentPermissions = TryExecuteMethod getaadapproleassignments $appPrincipalprincipalId
$GraphPerms = $null
if ($CurrentPermissions.value) {
    $GraphPerms = $CurrentPermissions.value.Where({ $_.resourceId -eq $MicrosoftGraphSpn }).appRoleId
}
$Permissions = @('df021288-bdef-4463-88db-98f22de89214', '45bbb07e-7321-4fd7-a8f6-3ff27e6a81c8')
foreach ($perm in $Permissions) {
    if ($GraphPerms -and $GraphPerms.Contains($perm)) {
        Write-Host "App role '$perm' is already assigned to Service Principal '$appPrincipalprincipalId'."
        Write-Verbose "Existing Perms: $(ConvertTo-Json $GraphPerms -Compress)"
        continue
    }
    Write-Host "Adding app role '$perm' to Service Principal '$appPrincipalprincipalId'."
    $Errors, $null = TryExecuteMethod addaadapproleassignment $appPrincipalprincipalId $MicrosoftGraphSpn $perm
    if ($Errors) {
        Write-Error "Failed to add app role '$perm' to Service Principal '$appPrincipalprincipalId'. Please ensure you have access to the tenant and try again."
        $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
        return
    }
}
Write-Host "Function App Identity has been configured with required permissions." -ForegroundColor Green
Write-Host

Write-Host "Waiting for function app '$functionName' to finish deployment."
$deployed = $false
while (!$deployed) {
    $Errors, $CurrentDeploymentLogs = TryExecuteMethod getwebappdeployment $ResourceGroupName $functionName
    if ($Errors) {
        Write-Error "Failed to get deployment logs for function app '$functionName'. Please ensure you have access to the subscription and try again."
        $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
        return
    }
    $latestmessage = if ($null -ne $CurrentDeploymentLogs) { $CurrentDeploymentLogs[-1].message } else { '' }
    if ($latestmessage -cmatch 'Deployment Failed.') {
        Write-Error "Failed to deploy function app '$functionName'. Getting deployment logs..."

        $Errors, $MainDeploymentLogs = TryExecuteMethod getwebappdeploymentlogs $ResourceGroupName $functionName
        if ($Errors.Count -gt 0) {
            $MainDeploymentLogs = $null
            Write-Error "Failed to get deployment logs for function app '$functionName'."
            $Errors | ForEach-Object { Write-Error $_.Exception.Message }
        }
        $NeedDetail = @($MainDeploymentLogs | Where-Object { $_.url })
        $DeploymentLogDetails = if ($NeedDetail.Count -gt 0) {
            $Errors, $PublishingCredentials = TryExecuteMethod getwebapppublishingcredentials $ResourceGroupName $functionName
            if ($Errors.Count -gt 0) {
                Write-Error "Failed to get publishing credentials for function app '$functionName'."
                $PublishingCredentials = $null
                $Errors | ForEach-Object { Write-Error $_.Exception.Message }
                return
            }
            @($NeedDetail | ForEach-Object {
                if ($null -eq $PublishingCredentials.un -or $null -eq $PublishingCredentials.pwd) { return }
                $Errors, $Details = TryExecuteMethod getwebappdeploymentlogdetails $_.url $PublishingCredentials
                if ($Errors.Count -gt 0) {
                    Write-Error "Failed to get deployment log details for function app '$functionName'."
                    $Details = $null
                    $Errors | ForEach-Object { Write-Error $_.Exception.Message }
                    return
                }
                $Details
            })
        } else {
            @()
        }

        $DeploymentLogs = $MainDeploymentLogs + $DeploymentLogDetails | Where-Object { $_.time } |
            Sort-Object time | ForEach-Object {'{0:o}: {1}' -f $_.time, $_.message }

        if ($DeploymentLogs.Count -gt 0) {
            Write-Warning "Deployment logs for function app '$functionName':`n$($DeploymentLogs -join "`n")"
        }

        return
    }
    $deployed = $latestmessage -match '^\s*deployment\s+successful\.\s*$'
    if (!$deployed) { Start-Sleep -Seconds 10 }
}

Write-Host "Function app '$functionName' has been deployed successfully." -ForegroundColor Green
Write-Host

Write-Host 'Getting the master function key to bootstrap the function.'
$Errors, $Key = TryExecuteMethod getmasterkey $ResourceGroupName $functionName
if ($Errors) {
    Write-Error "Failed to get master key for function app '$functionName'."
    $Errors | ForEach-Object { Write-Error -ErrorRecord $_ }
    return
}

$SubscriptionFunctionUrl = "https://${appDomain}/api/subscription"
$retryInterval = 15
$RetryCount = 0
$MaxRetries = 30
$RetriableErrors = @(500)
try {
    Write-Host "Triggering function to add the Call Records Graph Subscription (${SubscriptionFunctionUrl})."
    while ($true) {
        try {
            $null = Invoke-RestMethod -Uri $SubscriptionFunctionUrl -Method Post -Headers @{ 'x-functions-key' = $Key } -Verbose:$false -ErrorAction Stop
            break
        }
        catch {
            if ($_.Exception.Response.StatusCode -notin $RetriableErrors -or ++$RetryCount -gt $MaxRetries) {
                throw
            }
            Write-Host "Application is not yet authorized to add the Call Records Graph Subscription, awaiting app role propagation, retrying in $retryInterval seconds." -ForegroundColor Yellow
            Start-Sleep -Seconds $retryInterval
            $retryInterval = [Math]::Min(($retryInterval * 2), (2 * 60))
        }
    }
    Write-Host "Triggering function to add the Call Records Graph Subscription completed successfully." -ForegroundColor Green
}
catch {
    if ($_.Exception.Response.StatusCode -notin $RetriableErrors) {
        Write-Error "Failed to trigger function to add the Call Records Graph Subscription."
        Write-Error -ErrorRecord $_
        return
    }
    Write-Host "Failed to trigger function to add the Call Records Graph Subscription. The application is running but is not yet authorized to add the Call Records Graph Subscription. Try again later." -ForegroundColor Yellow
}

$HealthFunctionUrl = "https://${appDomain}/api/health"
try {
    Write-Host "Getting Health of deployed function (${HealthFunctionUrl})."
    $HealthState = Invoke-RestMethod -Uri $HealthFunctionUrl -Headers @{ 'x-functions-key' = $Key } -Verbose:$false -ErrorAction Stop
    Set-Variable -Name 'HealthState' -Value $HealthState -Scope Global
    Write-Host '$HealthState contains the health of the deployed function.'
    if ($HealthState.healthy) {
        Write-Host "App deployment is healthy" -ForegroundColor Green
    }
    else {
        Write-Host "App deployment is not healthy" -ForegroundColor Red
        Write-Host "HealthState: `n$(ConvertTo-Json -InputObject $HealthState -ErrorAction SilentlyContinue -Depth 10)"
        return
    }
}
catch {
    Write-Error "Failed to get health of deployed function."
    Write-Error -ErrorRecord $_
    return
}
Write-Host

Write-Host "Function deployed to https://${appDomain}/"

Write-Host "Deployment complete." -ForegroundColor Green
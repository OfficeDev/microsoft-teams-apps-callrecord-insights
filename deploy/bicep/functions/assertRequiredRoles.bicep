param location string = resourceGroup().location

param principalId string

param roleIds array

param graphEndpoint string = 'graph.microsoft.com'

param forceUpdateTag string = newGuid()

resource assertRequiredRoles 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
    name: 'assertRequiredRoles-${guid(principalId,'${roleIds}')}'
    kind: 'AzureCLI'
    location: location
    properties: {
        arguments: '${principalId} ${graphEndpoint} ${roleIds}'
        scriptContent: '''
            PRINCIPAL_ID=$1; shift
            GRAPH_ENDPOINT=$1; shift
            NEEDED_ROLES=( "$@" )
            REQUEST_URL="https://$GRAPH_ENDPOINT/v1.0/servicePrincipals/$PRINCIPAL_ID/appRoleAssignments"
            RESPONSES=()

            GRAPH_SPN=$(az ad sp list --spn '00000003-0000-0000-c000-000000000000' --query '[].id' --output tsv)
            CURRENT_ASSIGNMENTS=$(az rest --method get --url $REQUEST_URL | jq -r ".value[] | select(.resourceId==\"$GRAPH_SPN\") | .appRoleId")
            for role in "${NEEDED_ROLES[@]}"; do
                if [[ ! " ${CURRENT_ASSIGNMENTS[@]} " =~ " $role " ]]; then
                    BODY="{\"principalId\":\"$PRINCIPAL_ID\",\"resourceId\":\"$GRAPH_SPN\",\"appRoleId\":\"$role\"}"
                    RESPONSE=$(az rest --method post --url $REQUEST_URL --body $BODY)
                    RESPONSES+=("$RESPONSE")
                fi
            done

            echo ${RESPONSES[@]} | jq -n '{responses:[inputs]}' > $AZ_SCRIPTS_OUTPUT_PATH
        '''
        azCliVersion: '2.55.0'
        retentionInterval: 'P1D'
        cleanupPreference: 'OnSuccess'
        forceUpdateTag: forceUpdateTag
    }
}

output assertRequiredRoles object = assertRequiredRoles.properties.outputs

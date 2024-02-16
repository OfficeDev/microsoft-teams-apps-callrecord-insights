# Troubleshooting

## Deployment

### Error - No SPN for Graph Change Tracking
The required Service Principal for the First Party Application 'Microsoft Graph Change Tracking' does not exist in the tenant where Call Record Insights is deployed

[More Information](https://learn.microsoft.com/en-us/graph/change-notifications-delivery-event-hubs#what-if-the-microsoft-graph-change-tracking-application-is-missing)

#### Resolution
Register the Service Principal for the First Party Application in the tenant where Call Record Insights is deployed

*Example*:
```powershell
Connect-MgGraph -Scopes 'Application.ReadWrite.All'
$GraphChangeTrackingAppId = '0bf30f3b-4a52-48df-9a82-234910c4a086'
$Existing = Get-MgServicePrincipal -Filter "appId eq '$GraphChangeTrackingAppId'" -ErrorAction SilentlyContinue
if (!$Existing) {
   Write-Warning "SPN not found, creating new..."
   New-MgServicePrincipal -AppId \$GraphChangeTrackingAppId
}
else {
   Write-Information "SPN already created..."
   $Existing
}
```

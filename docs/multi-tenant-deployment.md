# Multi/Cross Tenant Deployment

For the default (and most common) deployment, Call Record Insights is deployed to an Azure Subscription associated with the same Microsoft Entra ID Tenant that is being monitored.

However, when that shared tenant context is not possible, it is possible to deploy Call Record Insights to work in either a Cross-Tenant or Multi-Tenant configuration.

Example:

- Development deployment with Production data being monitored

- Single deployment with multiple subsidiary tenants being monitored

Since the default deployment uses Managed Identity for all Microsoft Graph authentication, this will not work, as the identity would only have permissions to access data from the tenant which owns the Identity.

By using Microsoft Entra ID App authentication, we can authenticate against external tenants that have been properly configured and consented.

## Create Microsoft Entra ID App Registration (In Deployed Tenant)
### Create a New registration

<img src="./media/multi-tenant-create-application-1.png" style="width:75%; align:left" />

> [!Note]
> Be sure to select *Accounts in any organizational directory (Any Microsoft Entra ID - Multitenant)*

<img src="./media/multi-tenant-create-application-2.png" style="width:75%; align:left" />

<img src="./media/multi-tenant-create-application-3.png" style="width:75%; align:left" />

### Configure Microsoft Graph API Permissions

<img src="./media/multi-tenant-create-application-4.png" style="width:75%; align:left" />
<img src="./media/multi-tenant-create-application-5.png" style="width:75%; align:left" />
<img src="./media/multi-tenant-create-application-6.png" style="width:75%; align:left" />
<img src="./media/multi-tenant-create-application-7.png" style="width:75%; align:left" />
<img src="./media/multi-tenant-create-application-8.png" style="width:75%; align:left" />

### Grant admin consent (Optional)

> [!NOTE]
> This is only necessary if the Microsoft Entra ID Tenant where Call Record Insights is deployed is being monitored
> 
> This is unneeded if Call Records for this tenant are unwanted

<img src="./media/multi-tenant-create-application-9.png" style="width:75%; align:left" />
<img src="./media/multi-tenant-create-application-10.png" style="width:75%; align:left" />
<img src="./media/multi-tenant-create-application-11.png" style="width:75%; align:left" />

### Create Client Secret

> [!NOTE]
> Client Certificates can also be used
> 
> It is recommended that this is not done via the portal but instead managed and stored within the configured Azure Key Vault used by Call Record Insights

> [!NOTE]
> If this Secret or Certificate Expires or is revoked, Call Record Insights will no longer be able to monitor Call Records until renewed.

<img src="./media/multi-tenant-create-application-12.png" style="width:75%; align:left" />
<img src="./media/multi-tenant-create-application-13.png" style="width:75%; align:left" />


## Register Service Principal (In All Monitored Tenants)

### Create New Service Principal for the newly created App Registration

```powershell
$MultiTenantAppClientId = 'c0ffee60-0dde-cafc-0ffe-ebadcaffe14e' # This should be the Application (client) ID for the app registration

Connect-MgGraph -Scopes Application.ReadWrite.All
$NewSPN = New-MgServicePrincipal -AppId $MultiTenantAppClientId

Write-Host "Go To https://portal.azure.com/#view/Microsoft_AAD_IAM/ManagedAppMenuBlade/~/Permissions/objectId/$($NewSPN.Id)/appId/$MultiTenantAppClientId to grant consent to the application for your tenant."
```

<img src="./media/multi-tenant-register-service-principal-1.png" style="width:75%; align:left" />

### Grant consent for the new Service Principal

<img src="./media/multi-tenant-grant-consent-1.png" style="width:75%; align:left" />

#### Sign as a Global Administrator
<img src="./media/multi-tenant-grant-consent-2.png" style="width:75%; align:left" />

#### Verify the Application and Permissions are correct
<img src="./media/multi-tenant-grant-consent-3.png" style="width:75%; align:left" />

#### After Accepting the Permissions requested
<img src="./media/multi-tenant-grant-consent-4.png" style="width:75%; align:left" />

#### Refresh To Verify the permissions are consented
<img src="./media/multi-tenant-grant-consent-5.png" style="width:75%; align:left" />

## Update Call Record Insights Configuration

### Ensure all monitored domains are present in the [Monitored Tenant List](./function-configuration-settings.md#graphsubscription__tenants).

If a domain is not listed in this configuration, it **will not be monitored** even if consent has been granted

If the local tenant is also monitored, be sure to include it in the configuration in addition to any external Tenants.

### Enable Call Record Insights to use the [App Registration](#create-microsoft-entra-id-app-registration-in-deployed-tenant)

This is done in the [Graph App Configuration](./function-configuration-settings.md#azuread), refer there for more information and example(s)

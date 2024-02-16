# Admin Functions

Add info here about how to disable, and notes that none are required post deployment if wanting to eliminate all HTTP Triggers

Call Record Insights provides several HTTP Triggered functions solely for administrative purposes. 

Each admin function requires the use of the master/host key for authentication.

---

## GetCallRecordAdminFunction
This admin function retrieves a raw call record from Graph

### URL
`https://cridemo1-function.azurewebsites.net/api/callRecords/ca111dca-111d-ca11-1dca-11ca111dca11/contoso.com?code=<MASTER_KEY>`

### Method
`GET`

---

## AddSubscriptionOrRenewIfExpiredFunction
This admin function creates/renews the subscription to Graph

### URL
`https://cridemo1-function.azurewebsites.net/api/subscription/contoso.com?code=<MASTER_KEY>`

### Method
`POST`

---

## ManuallyProcessCallIdsFunction
This admin function process a call record with a provided Call-Id.

### URL
`https://cridemo1-function.azurewebsites.net/api/callRecords?code=<MASTER_KEY>`

### Method
`POST`

### Body
`["ca111dca-111d-ca11-1dca-11ca111dca11"]`

### Content-Type
`application/json`

---

## GetCallRecordInsightsHealthFunction
Gets the health state of each component

### URL
`https://cridemo1-function.azurewebsites.net/api/health?code=<MASTER_KEY>`

### Method
`GET`

---

## GetSubscriptionIdFunction
Gets the Azure subscription Id the deployment is in

### URL
`https://cridemo1-function.azurewebsites.net/api/subscription/contoso.com?code=<MASTER_KEY>`

### Method
`GET`

### Result
```json 
{
    "id": "0ddba11c-a11a-b1ec-ab00-5eca55e77e1d",
    "expirationDateTime": "1/20/2024 10:30:03 AM",
    "tenantId": "c0ffee60-0dde-cafc-0ffe-ebadcaffe14e",
    "resource": "communications/callRecords",
    "changeType": "created,updated",
    "notificationUrl": "EventHub:https://kvcridemo1.vault.azure.net/secrets/GraphEventHubConnectionString?tenantId=c0ffee60-0dde-cafc-0ffe-ebadcaffe14e"
}
```

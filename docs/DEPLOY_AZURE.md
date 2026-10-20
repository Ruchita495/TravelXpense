# Deploying to Azure (optional, untested template)

You need an Azure subscription (free tier works) and the `az` CLI.

```bash
RG=travelxpense-rg ; LOC=canadacentral ; SUFFIX=$RANDOM
az group create -n $RG -l $LOC

# Azure SQL
az sql server create -g $RG -n txsql$SUFFIX -l $LOC -u txadmin -p '<StrongPassword!>'
az sql db create -g $RG -s txsql$SUFFIX -n TravelXpense --service-objective Basic
az sql server firewall-rule create -g $RG -s txsql$SUFFIX -n AllowAzure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Service Bus queue
az servicebus namespace create -g $RG -n txbus$SUFFIX -l $LOC --sku Basic
az servicebus queue create -g $RG --namespace-name txbus$SUFFIX -n expense-events

# App Service (Linux)
az appservice plan create -g $RG -n tx-plan --is-linux --sku B1
az webapp create -g $RG -p tx-plan -n txapi$SUFFIX --runtime "DOTNETCORE:8.0"
```

Then set app settings (Portal > Configuration, or `az webapp config appsettings set`):

| Setting | Value |
|---|---|
| `ConnectionStrings__Default` | Azure SQL connection string |
| `Jwt__Key` | long random secret (store in Key Vault and reference it) |
| `Messaging__Provider` | `ServiceBus` |
| `ServiceBus__ConnectionString` | from the namespace's shared access policy |
| `Cors__Origins__0` | your frontend URL |

For the GitHub Actions deploy job: add repo variable `AZURE_WEBAPP_NAME` and secret `AZURE_WEBAPP_PUBLISH_PROFILE`
(download the publish profile from the App Service page).
Host the built React app (`frontend/dist`) on Azure Static Web Apps, and point it at the API URL.

// Azure infrastructure for the starter: three Linux App Services running the container images, Azure SQL with
// Microsoft Entra authentication only, Key Vault for secrets, a container registry and Application Insights.
//
// Nothing is created until you run the deployment yourself (see docs/azure-deployment.md). Review SKUs and
// names first: App Service, SQL and the registry are billed.
//
//   az deployment group create -g <resource-group> -f deploy/azure/main.bicep -p deploy/azure/main.parameters.example.json

targetScope = 'resourceGroup'

@description('Short name used as a prefix for every resource (letters and digits).')
@minLength(3)
@maxLength(12)
param appName string = 'starter'

@description('Environment suffix, e.g. prod or test.')
param environmentName string = 'prod'

param location string = resourceGroup().location

@description('Image tag to run (for example the git commit SHA pushed by your pipeline).')
param imageTag string

@description('Object id of the Microsoft Entra group that administers the SQL server (the deployment team).')
param sqlAdminGroupObjectId string

@description('Display name of that Microsoft Entra group.')
param sqlAdminGroupName string

@description('App Service plan SKU. P0v3 is the smallest SKU with features suitable for production; B1 is cheaper for tests.')
param appServiceSku string = 'P0v3'

@description('Number of instances. More than one requires the scale-out notes in docs/azure-deployment.md.')
@minValue(1)
param instanceCount int = 1

@description('Custom domain names, if any. Without them the default *.azurewebsites.net names are used.')
param identityHostName string = ''
param webHostName string = ''
param apiHostName string = ''

@description('Thumbprints of the token signing and encryption certificates imported into the App Service (see the docs).')
param signingCertificateThumbprint string = ''
param encryptionCertificateThumbprint string = ''

@description('Set to true only after the google-client-id and google-client-secret secrets exist in Key Vault. While false, Google sign-in is not configured and its button is not shown.')
param enableGoogleSignIn bool = false

@description('Sender address for account emails.')
param emailFromAddress string

@description('SMTP relay used for account emails (e.g. smtp.azurecomm.net for Azure Communication Services).')
param smtpHost string

param smtpPort int = 587
param smtpUserName string = ''

var suffix = '${appName}-${environmentName}'
var identityHost = empty(identityHostName) ? 'app-${suffix}-identity.azurewebsites.net' : identityHostName
var webHost = empty(webHostName) ? 'app-${suffix}-web.azurewebsites.net' : webHostName
var apiHost = empty(apiHostName) ? 'app-${suffix}-api.azurewebsites.net' : apiHostName
var databaseName = 'sqldb-${suffix}'
var registryName = toLower(replace('cr${appName}${environmentName}${uniqueString(resourceGroup().id)}', '-', ''))
var keyVaultName = take('kv-${suffix}-${uniqueString(resourceGroup().id)}', 24)

// Built-in role definition ids.
var acrPullRole = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var keyVaultSecretsUserRole = '4633458b-17de-408a-b874-0445c86b69e6'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${suffix}'
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true // no SQL passwords at all
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      principalType: 'Group'
      tenantId: subscription().tenantId
    }
  }
}

// Lets Azure services (the App Services) reach the server. For stricter isolation use a private endpoint
// and VNet integration instead (see docs/azure-deployment.md).
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: { name: 'S0', tier: 'Standard' }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'asp-${suffix}'
  location: location
  kind: 'linux'
  sku: {
    name: appServiceSku
    capacity: instanceCount
  }
  properties: {
    reserved: true
  }
}

var sqlConnection = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Encrypt=True;Authentication=Active Directory Managed Identity'

// An unresolved Key Vault reference is passed to the app as literal text, so optional secrets are only
// referenced when the feature is switched on.
var googleSettings = enableGoogleSignIn ? [
  { name: 'Authentication__Google__ClientId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=google-client-id)' }
  { name: 'Authentication__Google__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=google-client-secret)' }
] : []

var smtpCredentialSettings = empty(smtpUserName) ? [] : [
  { name: 'Email__UserName', value: smtpUserName }
  { name: 'Email__Password', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=smtp-password)' }
]

// Certificates imported into App Service are exposed as files once their thumbprints are listed in
// WEBSITE_LOAD_CERTIFICATES. Until both are set the identity server refuses to start with a clear message.
var certificateSettings = empty(signingCertificateThumbprint) || empty(encryptionCertificateThumbprint) ? [] : [
  { name: 'WEBSITE_LOAD_CERTIFICATES', value: '${signingCertificateThumbprint},${encryptionCertificateThumbprint}' }
  { name: 'OpenIddict__SigningCertificate__Path', value: '/var/ssl/private/${signingCertificateThumbprint}.p12' }
  { name: 'OpenIddict__EncryptionCertificate__Path', value: '/var/ssl/private/${encryptionCertificateThumbprint}.p12' }
]

var commonSettings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  // App Service terminates TLS and forwards the original scheme and client IP in X-Forwarded-* headers.
  { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
  { name: 'WEBSITES_PORT', value: '8080' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
]

var apps = [
  {
    key: 'identity'
    image: 'starter-identity'
    affinity: false
    webSockets: false
    settings: concat(googleSettings, smtpCredentialSettings, certificateSettings, [
      { name: 'Authentication__Issuer', value: 'https://${identityHost}/' }
      { name: 'Authentication__DefaultReturnUrl', value: 'https://${webHost}/' }
      { name: 'OpenIddict__Clients__starter-web__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=web-client-secret)' }
      { name: 'OpenIddict__Clients__starter-web__RedirectUris__0', value: 'https://${webHost}/signin-oidc' }
      { name: 'OpenIddict__Clients__starter-web__PostLogoutRedirectUris__0', value: 'https://${webHost}/signout-callback-oidc' }
      { name: 'Email__FromAddress', value: emailFromAddress }
      { name: 'Email__Host', value: smtpHost }
      { name: 'Email__Port', value: string(smtpPort) }
      { name: 'Email__Security', value: 'StartTls' }
    ])
  }
  {
    key: 'api'
    image: 'starter-api'
    affinity: false
    webSockets: false
    settings: [
      { name: 'Authentication__Issuer', value: 'https://${identityHost}/' }
    ]
  }
  {
    key: 'web'
    image: 'starter-web'
    // Blazor Server keeps per-user state in memory on one instance: requests and the WebSocket of a circuit
    // must reach the same instance (ARR affinity), and WebSockets must be on.
    affinity: true
    webSockets: true
    settings: [
      { name: 'Authentication__Authority', value: 'https://${identityHost}/' }
      { name: 'Authentication__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=web-client-secret)' }
      { name: 'Api__BaseUrl', value: 'https://${apiHost}/' }
    ]
  }
]

resource sites 'Microsoft.Web/sites@2023-12-01' = [for app in apps: {
  name: 'app-${suffix}-${app.key}'
  location: location
  kind: 'app,linux,container'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: app.affinity
    keyVaultReferenceIdentity: 'SystemAssigned'
    siteConfig: {
      linuxFxVersion: 'DOCKER|${registry.properties.loginServer}/${app.image}:${imageTag}'
      acrUseManagedIdentityCreds: true
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      webSocketsEnabled: app.webSockets
      healthCheckPath: '/health/ready'
      appSettings: concat(commonSettings, app.settings)
      connectionStrings: [
        { name: 'Default', connectionString: sqlConnection, type: 'SQLAzure' }
      ]
    }
  }
}]

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (app, i) in apps: {
  name: guid(registry.id, app.key, acrPullRole)
  scope: registry
  properties: {
    principalId: sites[i].identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRole)
  }
}]

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (app, i) in apps: {
  name: guid(keyVault.id, app.key, keyVaultSecretsUserRole)
  scope: keyVault
  properties: {
    principalId: sites[i].identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRole)
  }
}]

output registryLoginServer string = registry.properties.loginServer
output keyVaultName string = keyVault.name
output sqlServerName string = sqlServer.name
output databaseName string = databaseName
output identityUrl string = 'https://${identityHost}/'
output webUrl string = 'https://${webHost}/'
output apiUrl string = 'https://${apiHost}/'
output appIdentityNames array = [for (app, i) in apps: sites[i].name]

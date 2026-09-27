targetScope = 'resourceGroup'

@description('Globally unique Azure SQL logical server name.')
param sqlServerName string

@description('Name of the Microsoft Entra user administering Azure SQL.')
param sqlAdministratorName string

@description('Object ID of the Microsoft Entra user administering Azure SQL.')
param sqlAdministratorObjectId string

@description('Azure region for the deployment.')
param location string = resourceGroup().location

@description('Azure region for SQL, which can differ when the app region blocks new SQL servers.')
param sqlLocation string = 'westeurope'

@description('Resource name prefix, unique within the resource group.')
param namePrefix string = 'oakshore-coach'

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-app'
  location: location
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: sqlLocation
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdministratorName
      principalType: 'User'
      sid: sqlAdministratorObjectId
      tenantId: subscription().tenantId
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource azureServicesFirewall 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'coach'
  location: sqlLocation
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    requestedBackupStorageRedundancy: 'Local'
    maxSizeBytes: 34359738368
  }
}

resource appEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: ''
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

output appIdentityId string = appIdentity.id
output appIdentityClientId string = appIdentity.properties.clientId
output appIdentityPrincipalId string = appIdentity.properties.principalId
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output appEnvironmentId string = appEnvironment.id
output appEnvironmentDomain string = appEnvironment.properties.defaultDomain

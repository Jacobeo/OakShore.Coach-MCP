targetScope = 'resourceGroup'

@description('Resource name prefix used by foundation.bicep.')
param namePrefix string = 'oakshore-coach'

@description('Azure SQL server FQDN output by foundation.bicep.')
param sqlServerFqdn string

@description('Immutable GHCR image reference, preferably a commit SHA or digest.')
param image string

@description('GitHub account with read access to the GHCR package.')
param registryUsername string

@secure()
@description('GitHub package read token. Stored as a Container Apps secret.')
param registryToken string

@description('HTTPS OIDC issuer used to validate bearer tokens.')
param authenticationAuthority string

@description('Azure region for the deployment.')
param location string = resourceGroup().location

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: '${namePrefix}-app'
}

resource appEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: '${namePrefix}-env'
}

var appName = '${namePrefix}-api'
var publicUrl = 'https://${appName}.${appEnvironment.properties.defaultDomain}'
var audience = '${publicUrl}/mcp'

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: appEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: 'ghcr.io'
          username: registryUsername
          passwordSecretRef: 'ghcr-token'
        }
      ]
      secrets: [
        {
          name: 'ghcr-token'
          value: registryToken
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'Authentication__Authority'
              value: authenticationAuthority
            }
            {
              name: 'Authentication__Audience'
              value: audience
            }
            {
              name: 'Ingest__BaseUrl'
              value: publicUrl
            }
            {
              name: 'Server__PublicUrl'
              value: publicUrl
            }
            {
              name: 'Persistence__ConnectionString'
              value: 'Server=tcp:${sqlServerFqdn},1433;Database=coach;Authentication=Active Directory Managed Identity;User Id=${appIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
            }
          ]
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 10
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '10'
              }
            }
          }
        ]
      }
    }
  }
}

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'

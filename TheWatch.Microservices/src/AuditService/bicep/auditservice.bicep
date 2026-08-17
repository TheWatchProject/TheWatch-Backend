param location string = resourceGroup().location
param environmentId string
param containerImage string

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'auditservice'
  location: location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      ingress: {
        external: false
        targetPort: 8006
      }
      dapr: {
        enabled: true
        appId: 'auditservice'
        appPort: 8006
      }
    }
    template: {
      containers: [
        {
          name: 'auditservice'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1.0Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 20
      }
    }
  }
}
param location string = resourceGroup().location
param environmentId string
param containerImage string

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'authservice'
  location: location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      ingress: {
        external: false
        targetPort: 8007
      }
      dapr: {
        enabled: true
        appId: 'authservice'
        appPort: 8007
      }
    }
    template: {
      containers: [
        {
          name: 'authservice'
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
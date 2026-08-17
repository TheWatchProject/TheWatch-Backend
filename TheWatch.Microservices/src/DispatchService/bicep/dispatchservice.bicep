param location string = resourceGroup().location
param environmentId string
param containerImage string

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'dispatchservice'
  location: location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      ingress: {
        external: false
        targetPort: 8002
      }
      dapr: {
        enabled: true
        appId: 'dispatchservice'
        appPort: 8002
      }
    }
    template: {
      containers: [
        {
          name: 'dispatchservice'
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
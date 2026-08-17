param location string = resourceGroup().location
param environmentId string
param containerImage string

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'incidentservice'
  location: location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      ingress: {
        external: false
        targetPort: 8001
      }
      dapr: {
        enabled: true
        appId: 'incidentservice'
        appPort: 8001
      }
    }
    template: {
      containers: [
        {
          name: 'incidentservice'
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
using Aspire.Hosting;

namespace Projects;

public class TheWatch_EmergencyService : IProjectMetadata
{
    public string ProjectPath => "../Services/TheWatch.EmergencyService/TheWatch.EmergencyService.csproj";
}

public class TheWatch_MobileBff : IProjectMetadata
{
    public string ProjectPath => "../Services/TheWatch.MobileBff/TheWatch.MobileBff.csproj";
}

public class TheWatch_ApiGateway : IProjectMetadata
{
    public string ProjectPath => "../Gateways/TheWatch.ApiGateway/TheWatch.ApiGateway.csproj";
}

public class TheWatch_Functions : IProjectMetadata
{
    public string ProjectPath => "../Serverless/TheWatch.Functions/TheWatch.Functions.csproj";
}

public class TheWatch_Web_Admin : IProjectMetadata
{
    public string ProjectPath => "../Clients/TheWatch.Web.Admin/TheWatch.Web.Admin.csproj";
}

public class IncidentService : IProjectMetadata
{
    public string ProjectPath => "../../../../TheWatch.Microservices/src/IncidentService/IncidentService.csproj";
}

public class DispatchService : IProjectMetadata
{
    public string ProjectPath => "../../../../TheWatch.Microservices/src/DispatchService/DispatchService.csproj";
}

public class LocationService : IProjectMetadata
{
    public string ProjectPath => "../../../../TheWatch.Microservices/src/LocationService/LocationService.csproj";
}

public class NotificationService : IProjectMetadata
{
    public string ProjectPath => "../../../../TheWatch.Microservices/src/NotificationService/NotificationService.csproj";
}

public class TriageService : IProjectMetadata
{
    public string ProjectPath => "../../../../TheWatch.Microservices/src/TriageService/TriageService.csproj";
}

public class AuditService : IProjectMetadata
{
    public string ProjectPath => "../../../../TheWatch.Microservices/src/AuditService/AuditService.csproj";
}

public class AuthService : IProjectMetadata
{
    public string ProjectPath => "../../../../TheWatch.Microservices/src/AuthService/AuthService.csproj";
}

public class AiInferenceService : IProjectMetadata
{
    public string ProjectPath => "../../../../TheWatch.Microservices/src/AiInferenceService/AiInferenceService.csproj";
}

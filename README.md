# ⚡ TheWatch-Backend

> Distributed Cloud-Native Backend, .NET Aspire 9.0 AppHost Orchestrator, Serverless Azure Functions, Geospatial A* Database, and Enterprise Microservices for The Watch.

---

## 📦 Projects in this Repository

| Project / Service | Description |
| :--- | :--- |
| **`TheWatch.AppHost`** | .NET Aspire 9.0 orchestration entrypoint coordinating Postgres PostGIS, Redis, RabbitMQ, and microservices. |
| **`TheWatch.ServiceDefaults`** | Standardized OpenTelemetry metrics, structured logging, distributed tracing, and health check endpoints. |
| **`TheWatch.MobileBff`** | Mobile Backend-For-Frontend gateway, real-time SignalR hubs, and JWT/OAuth2 authentication. |
| **`TheWatch.EmergencyService`** | CAD 911 dispatch, mutual aid routing, and trauma severity triage. |
| **`TheWatch.Functions`** | 38 serverless HTTP, timer, queue, and blob triggers (Azure Functions / Google Cloud Functions). |
| **`TheWatch.Infrastructure`** | Dual-channel transactional outbox, quartz scheduling, webhook dispatch, and IoT adapters. |
| **`TheWatch.Geospatial.Db`** | PostGIS entity framework context, A* pathfinding, QuadTree spatial indexing, and geofence evaluators. |
| **`TheWatch.Security`** | NIST SP 800-53 FedRAMP High evaluator, DISA STIG scanner, HIPAA ePHI sanitizer, and FIPS crypto. |
| **`TheWatch.Core.Messaging`** | High-throughput Reactive Emergency Event Stream Hub & Complex Event Processing (CEP). |
| **`TheWatch.Microservices/*`** | Autonomous microservices (Incident, Dispatch, Location, Triage, Audit, Auth, AI Inference). |

---

## 🛠️ Local Development & Running

```bash
# Run the entire distributed system via .NET Aspire
dotnet run --project TheWatch.DotNet/src/Orchestration/TheWatch.AppHost/TheWatch.AppHost.csproj
```

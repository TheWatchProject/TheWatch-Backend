using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MediatR;
using OpenTelemetry.Trace;
using TheWatch.Microservices.Evidence.AuditService.Services;
using TheWatch.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Microservice: AuditService (Tamper-Evident Immutable Audit Logging & Cryptographic Verification)
builder.Services.AddControllers().AddDapr();
builder.Services.AddTheWatchAuthentication(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "TheWatch AuditService API",
        Version = "v1",
        Description = "Cryptographic SHA-256 Tamper-Evident Audit Trail & Regulatory Compliance Log Service"
    });
});

builder.Services.AddSingleton<IAuditLogEngine, InMemoryAuditLogEngine>();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddHealthChecks();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracer => tracer
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "TheWatch AuditService v1"));
}

app.UseRouting();
app.UseTheWatchAuthentication();
app.UseCloudEvents();
app.MapControllers();
app.MapSubscribeHandler();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions()
{
    Predicate = _ => true,
}).AllowAnonymous();

app.Run();
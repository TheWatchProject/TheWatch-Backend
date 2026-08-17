using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MediatR;
using OpenTelemetry.Trace;
using TheWatch.Microservices.Dispatch.DispatchService.Services;
using TheWatch.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Microservice: DispatchService (Unit allocation, routing, readiness scoring and dispatch assignment)
builder.Services.AddControllers().AddDapr();
builder.Services.AddTheWatchAuthentication(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "TheWatch DispatchService API",
        Version = "v1",
        Description = "Automated Dispatch Routing, Responder Readiness & Proximity Engine"
    });
});

builder.Services.AddSingleton<IDispatchStore, InMemoryDispatchStore>();
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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "TheWatch DispatchService v1"));
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
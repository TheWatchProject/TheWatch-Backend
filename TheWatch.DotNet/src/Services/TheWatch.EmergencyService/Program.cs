using TheWatch.ServiceDefaults;
using MediatR;
using TheWatch.Application.Commands;
using TheWatch.Application;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateIncidentCommand).Assembly));
builder.Services.AddTheWatchApplicationPipeline();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/api/v1/emergency/incidents", async (CreateIncidentCommand command, IMediator mediator) =>
{
    var id = await mediator.Send(command);
    return Results.Created($"/api/v1/emergency/incidents/{id}", new { id });
});

app.Run();

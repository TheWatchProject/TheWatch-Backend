using TheWatch.Infrastructure.Security;
using TheWatch.Microservices.Mesh.MeshGatewayService.Services;
using TheWatch.Security;
using TheWatch.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers().AddDapr();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "TheWatch MeshGatewayService API",
        Version = "v1",
        Description = "LoRaWAN, BLE, and Serial RF Mesh Packet Gateway with FIPS-140-3 Decryption"
    });
});

builder.Services.AddSingleton<IFipsCryptoProvider, FipsAesGcmCryptoProvider>();
builder.Services.AddSingleton<IMeshDecoderService, MeshDecoderService>();
builder.Services.AddTheWatchAuthentication(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "TheWatch MeshGatewayService v1"));
}

app.UseTheWatchAuthentication();
app.MapControllers();
app.MapDefaultEndpoints();

app.Run();

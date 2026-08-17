using Microsoft.AspNetCore.Mvc;
using TheWatch.Contracts;
using TheWatch.Microservices.Mesh.MeshGatewayService.Services;

namespace TheWatch.Microservices.Mesh.MeshGatewayService.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class MeshGatewayController : ControllerBase
{
    private readonly ILogger<MeshGatewayController> _logger;
    private readonly IMeshDecoderService _decoder;

    public MeshGatewayController(ILogger<MeshGatewayController> logger, IMeshDecoderService decoder)
    {
        _logger = logger;
        _decoder = decoder;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "MeshGatewayService", domain = "Mesh", status = "Healthy", timestamp = DateTime.UtcNow });
    }

    [HttpPost("packet")]
    public async Task<IActionResult> IngestPacket([FromBody] MeshContracts.MeshPacket packet)
    {
        var report = await _decoder.IngestPacketAsync(packet);
        return Ok(report);
    }

    [HttpGet("nodes")]
    public async Task<IActionResult> GetNodes()
    {
        var nodes = await _decoder.GetActiveNodesAsync();
        return Ok(nodes);
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> UpdateHeartbeat([FromBody] MeshContracts.MeshNodeStatus status)
    {
        await _decoder.UpdateNodeStatusAsync(status);
        return Ok(new { status = "Acknowledged" });
    }
}

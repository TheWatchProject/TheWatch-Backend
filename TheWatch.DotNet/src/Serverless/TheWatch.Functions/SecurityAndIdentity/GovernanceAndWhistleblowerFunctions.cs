using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Contracts;
using TheWatch.Geospatial.Db;
using static TheWatch.Contracts.WhistleblowerAndTipsContracts;

namespace TheWatch.Functions.SecurityAndIdentity;

public sealed class GovernanceAndWhistleblowerFunctions
{
    private readonly ILogger<GovernanceAndWhistleblowerFunctions> _logger;
    private readonly IWhistleblowerAndTipsEngine _engine;

    public GovernanceAndWhistleblowerFunctions(
        ILogger<GovernanceAndWhistleblowerFunctions> logger,
        IWhistleblowerAndTipsEngine engine)
    {
        _logger = logger;
        _engine = engine;
    }

    [Function("SubmitCorporateWhistleblower")]
    public async Task<HttpResponseData> SubmitWhistleblowerAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/governance/whistleblower")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body ?? "{}");
        var root = doc.RootElement;

        string ticker = root.GetProperty("ticker").GetString() ?? "NASDAQ: THEWATCH";
        int catInt = root.GetProperty("category").GetInt32();
        string payload = root.GetProperty("encryptedPayload").GetString() ?? "";
        string token = root.GetProperty("claimantSecretToken").GetString() ?? "";
        bool isAnon = root.GetProperty("isAnonymous").GetBoolean();

        var report = _engine.SubmitWhistleblowerReport(ticker, (WhistleblowerCategory)catInt, payload, token, isAnon);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(report);
        return res;
    }

    [Function("SubmitCommunitySafetyTip")]
    public async Task<HttpResponseData> SubmitTipAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/governance/tips")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body ?? "{}");
        var root = doc.RootElement;

        int catInt = root.GetProperty("category").GetInt32();
        string desc = root.GetProperty("description").GetString() ?? "";
        double lat = root.GetProperty("latitude").GetDouble();
        double lon = root.GetProperty("longitude").GetDouble();
        string landmark = root.GetProperty("landmark").GetString() ?? "";
        bool isAnon = root.GetProperty("isAnonymous").GetBoolean();
        string alias = root.TryGetProperty("submitterAlias", out var a) ? a.GetString() ?? "" : "";
        bool reward = root.GetProperty("rewardRequested").GetBoolean();

        var tip = _engine.SubmitCommunityTip((CommunityTipCategory)catInt, desc, lat, lon, landmark, isAnon, alias, reward);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(tip);
        return res;
    }
}

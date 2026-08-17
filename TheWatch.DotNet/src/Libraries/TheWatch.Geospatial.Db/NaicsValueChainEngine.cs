using System.Collections.Concurrent;
using TheWatch.Contracts;
using static TheWatch.Contracts.NaicsValueChainContracts;

namespace TheWatch.Geospatial.Db;

public interface INaicsValueChainEngine
{
    void RegisterClassification(NaicsClassification classification);
    void RegisterNode(ValueChainNode node);
    void RegisterEdge(ValueChainEdge edge);
    ValueChainTraceResult TraceSupplyDependencies(ValueChainTraceRequest request);
    IReadOnlyList<NaicsClassification> GetAllClassifications();
}

public sealed class NaicsValueChainEngine : INaicsValueChainEngine
{
    private readonly ConcurrentDictionary<string, NaicsClassification> _classifications = new();
    private readonly ConcurrentDictionary<string, ValueChainNode> _nodes = new();
    private readonly ConcurrentBag<ValueChainEdge> _edges = new();

    public NaicsValueChainEngine()
    {
        SeedStandardTaxonomies();
    }

    private void SeedStandardTaxonomies()
    {
        var defaults = new List<NaicsClassification>
        {
            new("621910", "Ambulance Services", "Emergency and non-emergency medical transportation and field life support", NaicsSector.HealthCareSocialAssistance, ValueChainStage.Operations, true),
            new("922160", "Fire Protection", "Fire fighting, fire prevention, and hazardous material mitigation", NaicsSector.PublicAdministration, ValueChainStage.Operations, true),
            new("922120", "Police Protection", "Law enforcement, traffic control, and criminal interdiction", NaicsSector.PublicAdministration, ValueChainStage.Operations, true),
            new("622110", "General Medical and Surgical Hospitals", "Comprehensive tertiary medical and surgical hospitalization care", NaicsSector.HealthCareSocialAssistance, ValueChainStage.Operations, true),
            new("488190", "Other Support Activities for Air Transportation", "Autonomous drone logistics, helipad operations, and aerial reconnaissance", NaicsSector.TransportationWarehousing, ValueChainStage.OutboundLogistics, true),
            new("517111", "Wired Telecommunications Carriers", "Core fiber-optic and broadband emergency communications infrastructure", NaicsSector.Information, ValueChainStage.FirmInfrastructure, true),
            new("221122", "Electric Power Distribution", "Electrical grid transmission and substation power distribution", NaicsSector.Utilities, ValueChainStage.InboundLogistics, true),
            new("423450", "Medical, Dental, and Hospital Equipment Merchant Wholesalers", "Medical equipment, PPE, and emergency medical device supply chain", NaicsSector.WholesaleTrade, ValueChainStage.Procurement, true),
            new("541512", "Computer Systems Design Services", "CAD dispatch software, telemetry algorithms, and cryptographic verification", NaicsSector.ProfessionalScientificTechnicalServices, ValueChainStage.TechnologyDevelopment, false)
        };

        foreach (var c in defaults)
        {
            _classifications.TryAdd(c.Code, c);
        }
    }

    public void RegisterClassification(NaicsClassification classification)
    {
        _classifications[classification.Code] = classification;
    }

    public void RegisterNode(ValueChainNode node)
    {
        _nodes[node.NodeId] = node;
    }

    public void RegisterEdge(ValueChainEdge edge)
    {
        _edges.Add(edge);
    }

    public ValueChainTraceResult TraceSupplyDependencies(ValueChainTraceRequest request)
    {
        var matched = new List<ValueChainNode>();
        var targetCoord = new GeoBoundingBox(
            request.Latitude - (request.SearchRadiusKm / 111.0),
            request.Latitude + (request.SearchRadiusKm / 111.0),
            request.Longitude - (request.SearchRadiusKm / (111.0 * Math.Cos(request.Latitude * Math.PI / 180.0))),
            request.Longitude + (request.SearchRadiusKm / (111.0 * Math.Cos(request.Latitude * Math.PI / 180.0)))
        );

        foreach (var node in _nodes.Values)
        {
            if (request.RequiredNaicsCodes.Contains(node.NaicsCode) || request.RequiredNaicsCodes.Count == 0)
            {
                if (targetCoord.Contains(node.Latitude, node.Longitude))
                {
                    matched.Add(node);
                }
            }
        }

        var matchedIds = matched.Select(m => m.NodeId).ToHashSet();
        var relevantEdges = _edges
            .Where(e => matchedIds.Contains(e.SourceNodeId) || matchedIds.Contains(e.TargetNodeId))
            .ToList();

        double resilienceScore = matched.Count > 0 ? Math.Min(1.0, matched.Count / 5.0) : 0.0;

        return new ValueChainTraceResult(
            request.IncidentId,
            matched,
            relevantEdges,
            resilienceScore,
            DateTime.UtcNow
        );
    }

    public IReadOnlyList<NaicsClassification> GetAllClassifications() => _classifications.Values.ToList();
}

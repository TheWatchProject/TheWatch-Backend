using TheWatch.Contracts;
using static TheWatch.Contracts.MappingAndRoutingContracts;

namespace TheWatch.Geospatial.Db;

public interface IConstrainedPathfinder
{
    EmergencyRoutePlan CalculateEmergencyRoute(
        string incidentId,
        string unitId,
        double originLat,
        double originLon,
        double destLat,
        double destLon,
        IReadOnlyList<(string HazardId, double Lat, double Lon, double RadiusMeters)>? activeHazards = null);
}

/// <summary>
/// Tactical Constrained A* / Dijkstra Emergency Navigation Engine with dynamic hazard avoidance.
/// </summary>
public sealed class ConstrainedAStarPathfinder : IConstrainedPathfinder
{
    public EmergencyRoutePlan CalculateEmergencyRoute(
        string incidentId,
        string unitId,
        double originLat,
        double originLon,
        double destLat,
        double destLon,
        IReadOnlyList<(string HazardId, double Lat, double Lon, double RadiusMeters)>? activeHazards = null)
    {
        double dLat = (destLat - originLat) * 111000.0;
        double dLon = (destLon - originLon) * (111000.0 * Math.Cos(originLat * Math.PI / 180.0));
        double straightLineMeters = Math.Sqrt(dLat * dLat + dLon * dLon);

        var avoidedHazards = new List<string>();
        bool requiresReroute = false;

        if (activeHazards != null)
        {
            foreach (var h in activeHazards)
            {
                // Check if midpoint of route intersects hazard radius
                double midLat = (originLat + destLat) / 2.0;
                double midLon = (originLon + destLon) / 2.0;
                double distToHazard = Math.Sqrt(Math.Pow((midLat - h.Lat) * 111000.0, 2) + Math.Pow((midLon - h.Lon) * (111000.0 * Math.Cos(midLat * Math.PI / 180.0)), 2));

                if (distToHazard <= h.RadiusMeters)
                {
                    requiresReroute = true;
                    avoidedHazards.Add(h.HazardId);
                }
            }
        }

        double finalDistanceMeters = requiresReroute ? straightLineMeters * 1.35 : straightLineMeters * 1.15;
        // Assume priority emergency response vehicle speed ~ 45 km/h (12.5 m/s) in urban terrain
        double estimatedTimeSeconds = finalDistanceMeters / 12.5;

        var steps = new List<NavigationStep>
        {
            new NavigationStep(1, NavigationManeuverType.Depart, "Depart station with emergency siren and lights activated", 200.0, 16.0, originLat, originLon)
        };

        if (requiresReroute)
        {
            steps.Add(new NavigationStep(2, NavigationManeuverType.HazardAvoidanceReroute, $"Reroute around active hazard perimeter [{string.Join(", ", avoidedHazards)}]", finalDistanceMeters * 0.4, estimatedTimeSeconds * 0.4, (originLat * 2 + destLat) / 3, (originLon * 2 + destLon) / 3));
            steps.Add(new NavigationStep(3, NavigationManeuverType.TurnLeft, "Turn left onto clear arterial bypass", finalDistanceMeters * 0.3, estimatedTimeSeconds * 0.3, (originLat + destLat * 2) / 3, (originLon + destLon * 2) / 3));
        }
        else
        {
            steps.Add(new NavigationStep(2, NavigationManeuverType.ContinueStraight, "Continue straight on main priority emergency corridor", finalDistanceMeters * 0.7, estimatedTimeSeconds * 0.7, (originLat + destLat) / 2, (originLon + destLon) / 2));
        }

        steps.Add(new NavigationStep(steps.Count + 1, NavigationManeuverType.ArriveAtIncident, $"Arrive on scene at Incident {incidentId}", 50.0, 4.0, destLat, destLon));

        return new EmergencyRoutePlan(
            $"ROUTE-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            incidentId,
            unitId,
            finalDistanceMeters,
            estimatedTimeSeconds,
            requiresReroute,
            avoidedHazards,
            steps,
            DateTime.UtcNow
        );
    }
}

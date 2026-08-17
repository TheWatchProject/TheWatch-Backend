using System;
using System.Collections.Generic;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Webhooks;

/// <summary>
/// Comprehensive Catalog of Webhook Payload Templates (CAD 911, FEMA CAP, Ring, Alexa, SCADA, PagerDuty).
/// </summary>
public static class WebhookTemplatesCatalog
{
    public static string RenderCadIncidentTemplate(string incidentId, string category, string priority, string address, double lat, double lon) =>
        $$"""
        {
          "specversion": "1.0",
          "type": "thewatch.cad.incident.{{priority.ToLowerInvariant()}}",
          "source": "https://thewatch.gov/cad",
          "id": "{{incidentId}}",
          "time": "{{DateTime.UtcNow:O}}",
          "datacontenttype": "application/json",
          "data": {
            "incidentId": "{{incidentId}}",
            "category": "{{category}}",
            "priority": "{{priority}}",
            "location": {
              "address": "{{address}}",
              "latitude": {{lat}},
              "longitude": {{lon}}
            },
            "status": "DISPATCHED",
            "mutualAidRequired": true
          }
        }
        """;

    public static string RenderFemaCapAlertTemplate(string alertId, string headline, string urgency, string severity, string areaPolygon) =>
        $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
          <identifier>{{alertId}}</identifier>
          <sender>dispatch@thewatch.gov</sender>
          <sent>{{DateTime.UtcNow:O}}</sent>
          <status>Actual</status>
          <msgType>Alert</msgType>
          <scope>Public</scope>
          <info>
            <category>Safety</category>
            <event>{{headline}}</event>
            <urgency>{{urgency}}</urgency>
            <severity>{{severity}}</severity>
            <certainty>Observed</certainty>
            <headline>{{headline}}</headline>
            <area>
              <areaDesc>Affected Evacuation Perimeter</areaDesc>
              <polygon>{{areaPolygon}}</polygon>
            </area>
          </info>
        </alert>
        """;

    public static string RenderSmartHomeAlarmTemplate(string deviceId, string deviceType, string zone, string threatType) =>
        $$"""
        {
          "deviceId": "{{deviceId}}",
          "deviceType": "{{deviceType}}",
          "event": "ALARM_TRIGGERED",
          "zone": "{{zone}}",
          "threatType": "{{threatType}}",
          "timestamp": "{{DateTime.UtcNow:O}}",
          "verificationStatus": "CONFIRMED_ALARM"
        }
        """;

    public static string RenderScadaAnomalyTemplate(string sensorId, string plantSector, double readingValue, double threshold, string unit) =>
        $$"""
        {
          "sensorId": "{{sensorId}}",
          "plantSector": "{{plantSector}}",
          "metric": "GAS_CONCENTRATION_EXCEEDANCE",
          "readingValue": {{readingValue}},
          "thresholdLimit": {{threshold}},
          "unit": "{{unit}}",
          "status": "CRITICAL_SHUTDOWN_REQUIRED",
          "timestamp": "{{DateTime.UtcNow:O}}"
        }
        """;

    public static string RenderPagerDutyIncidentTemplate(string incidentId, string title, string severity, string summary) =>
        $$"""
        {
          "routing_key": "thewatch-sre-service-key",
          "event_action": "trigger",
          "dedup_key": "{{incidentId}}",
          "payload": {
            "summary": "{{summary}}",
            "severity": "{{severity}}",
            "source": "TheWatch-Aspire-Cluster",
            "custom_details": {
              "incidentId": "{{incidentId}}",
              "title": "{{title}}",
              "reportedAt": "{{DateTime.UtcNow:O}}"
            }
          }
        }
        """;
}

using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.Infrastructure.RealTime;

public class AlertService
{
    private readonly MyDbContextDxy _db;
    private readonly IHubContext<LiveTelemetryHub> _hub;
    private readonly ILogger<AlertService> _logger;
    private readonly IConfiguration _config;

    public AlertService(MyDbContextDxy db, IHubContext<LiveTelemetryHub> hub, ILogger<AlertService> logger, IConfiguration config)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
        _config = config;
    }

    public async Task EvaluateReadingAsync(string deviceExternalId, string deviceName, string metric, double value, CancellationToken ct = default)
    {
        try
        {
            var rules = await _db.AlertRules
                .Where(r => r.IsEnabled && (r.DeviceExternalId == "ALL" || r.DeviceExternalId == deviceExternalId))
                .ToListAsync(ct);

            foreach (var rule in rules)
            {
                if (!IsMetricMatch(rule.Metric, metric))
                    continue;

                bool triggered = false;
                if (rule.Condition == "GreaterThan" && value > rule.ThresholdValue) triggered = true;
                else if (rule.Condition == "LessThan" && value < rule.ThresholdValue) triggered = true;
                else if (rule.Condition == "Equals" && Math.Abs(value - rule.ThresholdValue) < 0.001) triggered = true;

                if (triggered)
                {
                    // Debounce: check if there's an active (unresolved) incident in the last 2 minutes for this device+metric
                    var recentUnresolved = await _db.AlertIncidents
                        .AnyAsync(i => i.DeviceExternalId == deviceExternalId && i.Metric == metric && !i.IsResolved && i.TriggeredAt > DateTime.UtcNow.AddMinutes(-2), ct);

                    if (!recentUnresolved)
                    {
                        var displayMetric = GetDisplayMetric(metric);
                        var incident = new AlertIncident
                        {
                            Id = Guid.NewGuid(),
                            AlertRuleId = rule.Id,
                            DeviceExternalId = deviceExternalId,
                            DeviceName = deviceName,
                            Metric = metric,
                            TriggerValue = value,
                            Severity = rule.Severity,
                            Message = $"{deviceName} {displayMetric} is {value:F2} (threshold: {rule.ThresholdValue:F2})",
                            TriggeredAt = DateTime.UtcNow,
                            IsResolved = false
                        };

                        _db.AlertIncidents.Add(incident);
                        await _db.SaveChangesAsync(ct);

                        // Broadcast to all connected clients via SignalR
                        await _hub.Clients.All.SendAsync("ReceiveAlertIncident", incident, cancellationToken: ct);

                        await SendEmailAsync(rule, incident, ct);

                        _logger.LogWarning("ALERT TRIGGERED: {Message} [{Severity}]", incident.Message, incident.Severity);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating alerts for {Device} {Metric}", deviceExternalId, metric);
        }
    }

    private static bool IsMetricMatch(string ruleMetric, string incomingMetric)
    {
        if (string.Equals(ruleMetric, incomingMetric, StringComparison.OrdinalIgnoreCase))
            return true;

        var r = NormalizeMetricName(ruleMetric);
        var i = NormalizeMetricName(incomingMetric);
        return string.Equals(r, i, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMetricName(string m)
    {
        if (string.IsNullOrWhiteSpace(m)) return string.Empty;
        var s = m.Trim().ToLowerInvariant();
        return s switch
        {
            "flowrate" or "flow" or "instantaneousflowrate" or "instantaneous_flow_rate" => "flowrate",
            "totalizer" or "accumulatedtotalizer" or "total" or "accumulated_totalizer" => "totalizer",
            "temp" or "temperature" or "temperaturec" or "temperature_c" => "temperature",
            "humidity" or "humidityrh" or "humidity_rh" or "rh" => "humidity",
            "activepower" or "power" or "active_power" or "kw" => "activepower",
            "totalenergy" or "energy" or "total_energy" or "kwh" => "totalenergy",
            "voltage" or "volt" => "voltage",
            "current" or "amp" or "ampere" => "current",
            "frequency" or "freq" or "hz" => "frequency",
            "pressure" or "bar" or "psi" => "pressure",
            _ => s
        };
    }

    private static string GetDisplayMetric(string metric)
    {
        return NormalizeMetricName(metric) switch
        {
            "flowrate" => "Flow Rate",
            "totalizer" => "Totalizer",
            "temperature" => "Temperature",
            "humidity" => "Humidity",
            "activepower" => "Active Power",
            "totalenergy" => "Total Energy",
            "voltage" => "Voltage",
            "current" => "Current",
            "frequency" => "Frequency",
            "pressure" => "Pressure",
            _ => metric
        };
    }

    private async Task SendEmailAsync(AlertRule rule, AlertIncident incident, CancellationToken ct)
    {
        var host = _config["Smtp:Host"];
        var smtpUser = _config["Smtp:Username"];
        var smtpPass = _config["Smtp:Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(smtpPass))
        {
            _logger.LogInformation("SMTP not configured (host or password missing) — email skipped for incident {Id}", incident.Id);
            return;
        }

        var from = _config["Smtp:From"] ?? smtpUser;
        var fromName = _config["Smtp:FromName"] ?? "SCADA Alarm";
        var recipient = string.IsNullOrWhiteSpace(rule.NotificationEmail)
            ? _config["Smtp:DefaultRecipient"]
            : rule.NotificationEmail;
        if (string.IsNullOrWhiteSpace(recipient)) return;

        try
        {
            var mail = new MailMessage
            {
                From = new MailAddress(from!, fromName),
                Subject = $"[{incident.Severity.ToUpperInvariant()}] SCADA Alert: {incident.DeviceName}",
                Body = $@"
Alert triggered at {incident.TriggeredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}

Device:   {incident.DeviceName} ({incident.DeviceExternalId})
Metric:   {incident.Metric}
Value:    {incident.TriggerValue:F2}
Severity: {incident.Severity}

{incident.Message}
",
                IsBodyHtml = false
            };
            mail.To.Add(recipient);

            using var smtp = new SmtpClient(host)
            {
                Port = int.TryParse(_config["Smtp:Port"], out var port) ? port : 587,
                EnableSsl = _config["Smtp:EnableSsl"] != "false",
                Credentials = new NetworkCredential(smtpUser, smtpPass)
            };
            await smtp.SendMailAsync(mail, ct);
            _logger.LogInformation("Alert email sent to {Recipient} for incident {Id}", recipient, incident.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert email for incident {Id}", incident.Id);
        }
    }
}

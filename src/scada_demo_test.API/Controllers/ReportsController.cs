using Microsoft.AspNetCore.Mvc;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IDeviceRepository _devices;
    private readonly ISensorReadingRepository _readings;
    private readonly IReportQueryService _history;

    public ReportsController(IDeviceRepository devices, ISensorReadingRepository readings, IReportQueryService history)
    {
        _devices = devices;
        _readings = readings;
        _history = history;
    }

    // GET /api/reports/fm-water-01/history?metric=FlowRate&from=2026-08-01T00:00:00Z&to=2026-08-29T00:00:00Z
    // Any range from a minute to a year - automatically reads whichever storage
    // tier (raw/hourly/daily/monthly) still covers it. Used by the Reports page's
    // custom date-range chart/table.
    [HttpGet("{externalId}/history")]
    public async Task<IActionResult> GetHistory(string externalId, [FromQuery] string metric, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (to <= from) return BadRequest("'to' must be after 'from'.");

        var device = await _devices.GetByExternalIdAsync(externalId);
        if (device is null) return NotFound();

        var series = await _history.GetHistoryAsync(device.Id, metric, from.ToUniversalTime(), to.ToUniversalTime());
        series.DeviceExternalId = externalId;
        return Ok(series);
    }

    // GET /api/reports/fm-water-01/range-pdf?metric=FlowRate&from=...&to=...
    // Same custom date-range data as /history, rendered as a downloadable PDF.
    [HttpGet("{externalId}/range-pdf")]
    public async Task<IActionResult> GenerateRangeReport(string externalId, [FromQuery] string metric, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (to <= from) return BadRequest("'to' must be after 'from'.");

        var device = await _devices.GetByExternalIdAsync(externalId);
        if (device is null) return NotFound();

        var series = await _history.GetHistoryAsync(device.Id, metric, from.ToUniversalTime(), to.ToUniversalTime());

        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text("ALAM IOT Enterprises").FontSize(20).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"SCADA - {metric} Report ({series.Resolution} resolution)").FontSize(13).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingTop(20).Column(col =>
                {
                    col.Item().Text($"Device: {device.Name} ({device.ExternalId})").FontSize(14).Bold();
                    col.Item().Text($"Range: {from:yyyy-MM-dd HH:mm} - {to:yyyy-MM-dd HH:mm} (UTC)");
                    col.Item().Text($"Report generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                    col.Item().PaddingTop(16).Text("Summary").FontSize(13).Bold();
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        if (series.IsCumulative)
                        {
                            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                            {
                                c.Item().Text("Total consumed").FontColor(Colors.Grey.Darken1).FontSize(10);
                                c.Item().Text($"{series.Total?.ToString("0.0") ?? "-"} {series.Unit}").FontSize(18).Bold();
                            });
                        }
                        else
                        {
                            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                            {
                                c.Item().Text("Average").FontColor(Colors.Grey.Darken1).FontSize(10);
                                c.Item().Text($"{series.Average?.ToString("0.0") ?? "-"} {series.Unit}").FontSize(18).Bold();
                            });
                            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                            {
                                c.Item().Text("Min / Max").FontColor(Colors.Grey.Darken1).FontSize(10);
                                c.Item().Text($"{series.Min?.ToString("0.0") ?? "-"} / {series.Max?.ToString("0.0") ?? "-"} {series.Unit}").FontSize(18).Bold();
                            });
                        }
                    });

                    col.Item().PaddingTop(20).Text($"Data points ({series.Points.Count})").FontSize(13).Bold();
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        table.Header(h =>
                        {
                            h.Cell().Text("Time").Bold();
                            h.Cell().Text($"{metric}").Bold();
                        });

                        foreach (var p in series.Points.Take(200))
                        {
                            table.Cell().Text(p.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                            table.Cell().Text($"{p.Value:0.00} {series.Unit}");
                        }
                    });
                });

                page.Footer().AlignCenter().Text("ALAM IOT Enterprises - SCADA Platform").FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf();

        return File(pdfBytes, "application/pdf", $"{device.ExternalId}-{metric}-report.pdf");
    }

    // GET /api/reports/fm-water-01 or GET /api/reports/fm-water-01/pdf -> downloads a PDF summary for that meter
    [HttpGet("{externalId}")]
    [HttpGet("{externalId}/pdf")]
    public async Task<IActionResult> GenerateReport(string externalId)
    {
        var device = await _devices.GetByExternalIdAsync(externalId);
        if (device is null) return NotFound();

        var flowReadings = await _readings.GetRecentAsync(device.Id, "FlowRate", count: 50);
        var totalizerReadings = await _readings.GetRecentAsync(device.Id, "Totalizer", count: 1);

        var latestFlow = flowReadings.FirstOrDefault();
        var latestTotalizer = totalizerReadings.FirstOrDefault();

        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text("ALAM IOT Enterprises").FontSize(20).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Text("SCADA - Flowmeter Report").FontSize(13).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingTop(20).Column(col =>
                {
                    col.Item().Text($"Device: {device.Name}").FontSize(14).Bold();
                    col.Item().Text($"Device ID: {device.ExternalId}");
                    col.Item().Text($"Status: {device.Status}");
                    col.Item().Text($"Report generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                    col.Item().PaddingTop(16).Text("Current Readings").FontSize(13).Bold();
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                        {
                            c.Item().Text("Flow Rate").FontColor(Colors.Grey.Darken1).FontSize(10);
                            c.Item().Text($"{latestFlow?.Value.ToString("0.0") ?? "-"} {latestFlow?.Unit}").FontSize(18).Bold();
                        });
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(c =>
                        {
                            c.Item().Text("Totalizer").FontColor(Colors.Grey.Darken1).FontSize(10);
                            c.Item().Text($"{latestTotalizer?.Value.ToString("0.0") ?? "-"} {latestTotalizer?.Unit}").FontSize(18).Bold();
                        });
                    });

                    col.Item().PaddingTop(20).Text("Recent Flow Rate History (last 50 readings)").FontSize(13).Bold();
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        table.Header(h =>
                        {
                            h.Cell().Text("Time").Bold();
                            h.Cell().Text("Flow Rate").Bold();
                        });

                        foreach (var r in flowReadings.Take(20))
                        {
                            table.Cell().Text(r.Timestamp.ToLocalTime().ToString("HH:mm:ss"));
                            table.Cell().Text($"{r.Value:0.0} {r.Unit}");
                        }
                    });
                });

                page.Footer().AlignCenter().Text("ALAM IOT Enterprises - SCADA Platform").FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf();

        return File(pdfBytes, "application/pdf", $"{device.ExternalId}-report.pdf");
    }

    [HttpGet("{externalId}/export-csv")]
    public async Task<IActionResult> ExportHistoryCsv(string externalId, [FromQuery] string metric, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (to <= from) return BadRequest("'to' must be after 'from'.");

        var device = await _devices.GetByExternalIdAsync(externalId);
        if (device is null) return NotFound();

        var series = await _history.GetHistoryAsync(device.Id, metric, from.ToUniversalTime(), to.ToUniversalTime());

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# SCADA Export - {device.Name} ({device.ExternalId})");
        sb.AppendLine($"# Metric: {metric}, Unit: {series.Unit}, Resolution: {series.Resolution}");
        sb.AppendLine($"# Time Range: {from:yyyy-MM-dd HH:mm} to {to:yyyy-MM-dd HH:mm} (UTC)");
        sb.AppendLine("TimestampUtc,Value,Unit,SampleCount,Min,Max");

        foreach (var pt in series.Points)
        {
            sb.AppendLine($"{pt.Timestamp:yyyy-MM-dd HH:mm:ss},{pt.Value},{series.Unit},{pt.SampleCount},{pt.Min},{pt.Max}");
        }

        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"{device.ExternalId}_{metric}_{from:yyyyMMdd}_{to:yyyyMMdd}.csv");
    }
}


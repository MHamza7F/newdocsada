using scada_demo_test.Maui.Models;

namespace scada_demo_test.Maui.Controls;

public partial class FlowMeterCard : ContentView
{
    public event EventHandler<FlowMeterModel>? AuditReportRequested;

    public FlowMeterCard()
    {
        InitializeComponent();
    }

    private async void OnAuditReportClicked(object? sender, EventArgs e)
    {
        if (BindingContext is FlowMeterModel meter)
        {
            AuditReportRequested?.Invoke(this, meter);

            string reportText = $"INDUSTRIAL FLOW METER AUDIT REPORT\n" +
                               $"----------------------------------------\n" +
                               $"Device Name: {meter.DeviceName}\n" +
                               $"External ID: {meter.DeviceExternalId}\n" +
                               $"Modbus Slave: {meter.ModbusSlaveId}\n" +
                               $"Flow Rate: {meter.FormattedFlowRate}\n" +
                               $"Cumulative Total: {meter.FormattedTotalizer}\n" +
                               $"Status: {meter.Status}\n" +
                               $"Last Seen: {meter.FormattedLastSeen}\n" +
                               $"----------------------------------------\n" +
                               $"Data certified accurate from telemetry stream.";

            var currentPage = Application.Current?.Windows[0].Page;
            if (currentPage != null)
            {
                await currentPage.DisplayAlert("📄 Meter Audit Report", reportText, "Dismiss");
            }
        }
    }
}
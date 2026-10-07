using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Services;

namespace scada_demo_test.Maui.ViewModels;

public partial class AlertsViewModel : BaseViewModel
{
    private readonly FirebaseScadaService _firebase;

    [ObservableProperty]
    private ObservableCollection<AlertIncident> _incidents = new();

    [ObservableProperty]
    private ObservableCollection<AlertRule> _rules = new();

    [ObservableProperty]
    private bool _showIncidentsView = true;

    [ObservableProperty]
    private int _criticalCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _activeCount;

    public AlertsViewModel(FirebaseScadaService firebase)
    {
        _firebase = firebase;
        Title = "Alarm Center";
    }

    [RelayCommand]
    public async Task LoadAlertsAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var incidents = await _firebase.GetAlertIncidentsAsync(50);
            var rules = await _firebase.GetAlertRulesAsync();

            Incidents.Clear();
            foreach (var inc in incidents)
            {
                Incidents.Add(inc);
            }

            Rules.Clear();
            foreach (var r in rules)
            {
                Rules.Add(r);
            }

            ActiveCount = Incidents.Count(i => !i.IsResolved && i.AcknowledgedAt == null);
            CriticalCount = Incidents.Count(i => i.Severity == "Critical");
            WarningCount = Incidents.Count(i => i.Severity == "Warning");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Could not load alarms: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void SwitchView(string viewMode)
    {
        ShowIncidentsView = viewMode == "Incidents";
    }

    [RelayCommand]
    public async Task AcknowledgeIncidentAsync(AlertIncident incident)
    {
        if (incident == null || incident.AcknowledgedAt != null) return;

        try
        {
            var (success, error) = await _firebase.AcknowledgeAlertIncidentAsync(incident.Id, "Operator");
            if (success)
            {
                incident.AcknowledgedAt = DateTime.UtcNow;
                incident.AcknowledgedBy = "Operator";
                ActiveCount = Incidents.Count(i => !i.IsResolved && i.AcknowledgedAt == null);
                await Shell.Current.DisplayAlert("Alarm Acknowledged", $"Alarm for {incident.DeviceExternalId} acknowledged.", "OK");
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", error ?? "Could not acknowledge alarm.", "OK");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", ex.Message, "OK");
        }
    }
}

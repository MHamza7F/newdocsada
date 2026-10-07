using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Services;

namespace scada_demo_test.Maui.ViewModels;

public partial class AuditLogsViewModel : BaseViewModel
{
    private readonly FirebaseScadaService _firebase;
    private List<AuditLog> _rawLogs = new();

    [ObservableProperty]
    private ObservableCollection<AuditLog> _logs = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalLogsCount;

    public AuditLogsViewModel(FirebaseScadaService firebase)
    {
        _firebase = firebase;
        Title = "Audit Logs & Security";
    }

    [RelayCommand]
    public async Task LoadLogsAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            _rawLogs = await _firebase.GetAuditLogsAsync(100);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Could not load audit logs: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _rawLogs
            : _rawLogs.Where(l =>
                (l.Action != null && l.Action.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                (l.EntityName != null && l.EntityName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                (l.UserName != null && l.UserName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                (l.Details != null && l.Details.Contains(SearchText, StringComparison.OrdinalIgnoreCase))).ToList();

        Logs.Clear();
        foreach (var log in filtered)
        {
            Logs.Add(log);
        }
        TotalLogsCount = _rawLogs.Count;
    }
}

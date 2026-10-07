using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Services;

namespace scada_demo_test.Maui.ViewModels;

public partial class SitesViewModel : BaseViewModel
{
    private readonly FirebaseScadaService _firebase;
    private List<Site> _allSites = new();

    [ObservableProperty]
    private ObservableCollection<Site> _sites = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalSitesCount;

    public SitesViewModel(FirebaseScadaService firebase)
    {
        _firebase = firebase;
        Title = "Plant Facilities & Sites";
    }

    [RelayCommand]
    public async Task LoadSitesAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            _allSites = await _firebase.GetSitesAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Could not load sites: {ex.Message}", "OK");
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
            ? _allSites
            : _allSites.Where(s =>
                s.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                s.Code.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                s.Location.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        Sites.Clear();
        foreach (var site in filtered)
        {
            Sites.Add(site);
        }
        TotalSitesCount = _allSites.Count;
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Services;

namespace scada_demo_test.Maui.ViewModels;

public partial class StorageTanksViewModel : BaseViewModel
{
    private readonly FirebaseScadaService _firebase;
    private IDisposable? _tankSubscription;
    private List<StorageTank> _allTanks = new();

    [ObservableProperty]
    private ObservableCollection<StorageTank> _tanks = new();

    [ObservableProperty]
    private double _averageLevel;

    [ObservableProperty]
    private double _totalVolumeLiters;

    [ObservableProperty]
    private double _totalCapacityLiters;

    [ObservableProperty]
    private int _alertTanksCount;

    [ObservableProperty]
    private string _selectedLiquidFilter = "All";

    public StorageTanksViewModel(FirebaseScadaService firebase)
    {
        _firebase = firebase;
        Title = "Storage Tanks";
    }

    [RelayCommand]
    public async Task LoadTanksAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            _allTanks = await _firebase.GetStorageTanksAsync();
            ApplyFilterAndStats();

            // Real-time updates subscription
            _tankSubscription?.Dispose();
            _tankSubscription = _firebase.SubscribeToStorageTanks(updatedTank =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var index = _allTanks.FindIndex(t => t.Id == updatedTank.Id || t.TankCode == updatedTank.TankCode);
                    if (index >= 0)
                    {
                        _allTanks[index] = updatedTank;
                    }
                    else
                    {
                        _allTanks.Add(updatedTank);
                    }
                    ApplyFilterAndStats();
                });
            });
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Could not load storage tanks: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void SetLiquidFilter(string liquid)
    {
        SelectedLiquidFilter = liquid;
        ApplyFilterAndStats();
    }

    private void ApplyFilterAndStats()
    {
        var filtered = SelectedLiquidFilter == "All"
            ? _allTanks
            : _allTanks.Where(t => string.Equals(t.LiquidType, SelectedLiquidFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        Tanks.Clear();
        foreach (var tank in filtered)
        {
            Tanks.Add(tank);
        }

        if (_allTanks.Count > 0)
        {
            AverageLevel = Math.Round(_allTanks.Average(t => t.LevelPercentage), 1);
            TotalVolumeLiters = Math.Round(_allTanks.Sum(t => t.CurrentVolumeLiters), 0);
            TotalCapacityLiters = Math.Round(_allTanks.Sum(t => t.CapacityLiters), 0);
            AlertTanksCount = _allTanks.Count(t => t.Status.Contains("Alert", StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Unsubscribe()
    {
        _tankSubscription?.Dispose();
        _tankSubscription = null;
    }
}

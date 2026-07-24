using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretMirrlyGUI.Services;
using ZapretMirrlyGUI.Pages;

namespace ZapretMirrlyGUI.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isRunning = false;

    [ObservableProperty]
    private string _progressStatusText = "Готов к запуску диагностики";

    [ObservableProperty]
    private double _progressPercent = 0;

    [ObservableProperty]
    private string _recommendedPresetTitle = "Диагностика не проводилась";

    [ObservableProperty]
    private string _recommendedPresetDescription = "Запустите автоматическое тестирование для определения оптимальной стратегии обхода для вашего провайдера.";

    public ObservableCollection<StrategyScoreItem> StrategyScores { get; } = new();

    public DiagnosticsViewModel()
    {
    }

    [RelayCommand]
    private void StartDiagnostics()
    {
        if (IsRunning) return;
        IsRunning = true;
        ProgressStatusText = "Инициализация тестирования пресетов...";
        ProgressPercent = 5;
    }

    [RelayCommand]
    private void StopDiagnostics()
    {
        IsRunning = false;
        ProgressStatusText = "Тестирование остановлено пользователем.";
    }
}

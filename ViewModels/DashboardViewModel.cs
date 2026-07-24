using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretMirrlyGUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ZapretMirrlyGUI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly DispatcherTimer _autoMetricsTimer;
    private readonly DispatcherTimer _autoApplyCountdownTimer;
    private int _countdownSeconds = 5;
    private bool _isMeasuring = false;

    [ObservableProperty]
    private List<string> _presets = new();

    [ObservableProperty]
    private string _selectedPreset = "";

    [ObservableProperty]
    private string _presetArgumentsText = "";

    [ObservableProperty]
    private string _launchLogText = "";

    [ObservableProperty]
    private bool _isRunning = false;

    [ObservableProperty]
    private string _statusTitle = "Обход не запущен";

    [ObservableProperty]
    private string _serviceNote = "";

    [ObservableProperty]
    private string _actionButtonText = "Запустить обход";

    [ObservableProperty]
    private bool _isActionButtonEnabled = true;

    [ObservableProperty]
    private bool _isProgressRingActive = false;

    [ObservableProperty]
    private Visibility _progressRingVisibility = Visibility.Collapsed;

    // Orb status brushes & glyph
    [ObservableProperty]
    private SolidColorBrush _statusOrbBg = new(Windows.UI.Color.FromArgb(255, 40, 12, 12));

    [ObservableProperty]
    private SolidColorBrush _statusOrbInnerBg = new(Windows.UI.Color.FromArgb(255, 90, 22, 22));

    [ObservableProperty]
    private SolidColorBrush _statusIconFg = new(Windows.UI.Color.FromArgb(255, 240, 80, 80));

    [ObservableProperty]
    private string _statusIndicatorIconGlyph = "\uE71A";

    // Latency metrics
    [ObservableProperty]
    private string _ytPingText = "-- мс";

    [ObservableProperty]
    private SolidColorBrush _ytPingBrush = new(Windows.UI.Color.FromArgb(255, 161, 161, 170));

    [ObservableProperty]
    private string _dcPingText = "-- мс";

    [ObservableProperty]
    private SolidColorBrush _dcPingBrush = new(Windows.UI.Color.FromArgb(255, 161, 161, 170));

    [ObservableProperty]
    private string _lossText = "--%";

    [ObservableProperty]
    private SolidColorBrush _lossBrush = new(Windows.UI.Color.FromArgb(255, 161, 161, 170));

    // Diagnostic recommendation overlay
    [ObservableProperty]
    private Visibility _overlayVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _winnerPresetName = "";

    [ObservableProperty]
    private string _winnerStatsText = "";

    [ObservableProperty]
    private string _applyWinnerButtonText = "Отлично (15 с)";

    public DashboardViewModel()
    {
        _autoMetricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoMetricsTimer.Tick += (s, e) => _ = MeasureDashboardMetricsAsync();

        _autoApplyCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoApplyCountdownTimer.Tick += AutoApplyCountdownTimer_Tick;
    }

    public void OnLoaded()
    {
        ZapretService.OnStatusChanged += OnZapretStatusChanged;
        ZapretService.OnLogReceived += OnZapretLogReceived;

        LoadPresets();
        UpdateUIStatus();
        _ = MeasureDashboardMetricsAsync();
        _autoMetricsTimer.Start();

        CheckAndShowDiagnosticRecommendations();
    }

    public void OnUnloaded()
    {
        ZapretService.OnStatusChanged -= OnZapretStatusChanged;
        ZapretService.OnLogReceived -= OnZapretLogReceived;

        _autoMetricsTimer.Stop();
        _autoApplyCountdownTimer.Stop();
    }

    public void LoadPresets()
    {
        Presets = ZapretService.GetPresets();

        if (Presets.Count > 0)
        {
            var savedPreset = SettingsManager.Instance.LastSelectedPreset;
            int savedIndex = Presets.FindIndex(p => p.Equals(savedPreset, StringComparison.OrdinalIgnoreCase));
            if (savedIndex != -1)
            {
                SelectedPreset = Presets[savedIndex];
            }
            else
            {
                int generalIndex = Presets.FindIndex(p => p.Equals("general.bat", StringComparison.OrdinalIgnoreCase));
                SelectedPreset = generalIndex != -1 ? Presets[generalIndex] : Presets[0];
            }
        }
        else
        {
            PresetArgumentsText = "Предупреждение: пресеты (.bat файлы) не обнаружены в папке zapret.";
        }
    }

    partial void OnSelectedPresetChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        SettingsManager.Instance.LastSelectedPreset = value;
        SettingsManager.Save();

        var mode = GetActiveGameFilterMode();
        var rawArgs = ZapretService.ParseArgumentsFromBatch(value, mode);
        PresetArgumentsText = rawArgs.Replace(" --", "\n--").Trim();
    }

    public void UpdateUIStatus()
    {
        bool isRunning = ZapretService.IsRunning;
        string serviceStatus = ZapretService.GetServiceStatus();
        bool diagRunning = ZapretService.IsDiagnosticsRunning;

        IsRunning = isRunning;

        if (isRunning)
        {
            StatusOrbBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 40, 15));
            StatusOrbInnerBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 90, 22));
            StatusIconFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 144, 238, 144));
            StatusIndicatorIconGlyph = "\uE73E";
            StatusTitle = "Обход активен";
            ServiceNote = "Запущен напрямую через GUI";
            ActionButtonText = "Остановить обход";
        }
        else if (serviceStatus == "RUNNING")
        {
            StatusOrbBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 40, 15));
            StatusOrbInnerBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 90, 22));
            StatusIconFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 144, 238, 144));
            StatusIndicatorIconGlyph = "\uE73E";
            StatusTitle = "Обход активен";
            ServiceNote = "Работает через службу Windows";
            ActionButtonText = "Запустить обход";
        }
        else if (serviceStatus == "START_PENDING")
        {
            StatusOrbBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 30, 0));
            StatusOrbInnerBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 90, 65, 0));
            StatusIconFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 190, 60));
            StatusIndicatorIconGlyph = "\uE72C";
            StatusTitle = "Запуск службы...";
            ServiceNote = "Служба Windows инициализируется";
            ActionButtonText = "Запустить обход";
        }
        else
        {
            StatusOrbBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 12, 12));
            StatusOrbInnerBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 90, 22, 22));
            StatusIconFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 80, 80));
            StatusIndicatorIconGlyph = "\uE71A";
            StatusTitle = "Обход не запущен";
            ServiceNote = $"Автозапуск через службу: {serviceStatus}";
            ActionButtonText = "Запустить обход";
        }

        IsActionButtonEnabled = !diagRunning;
    }

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    private void OnZapretStatusChanged(bool running)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateUIStatus());
        }
        else
        {
            UpdateUIStatus();
        }
    }

    private void OnZapretLogReceived(string line)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => LaunchLogText += line + "\n");
        }
        else
        {
            LaunchLogText += line + "\n";
        }
    }

    [RelayCommand]
    private async Task ToggleBypassAsync()
    {
        if (ZapretService.IsDiagnosticsRunning) return;

        IsActionButtonEnabled = false;
        IsProgressRingActive = true;
        ProgressRingVisibility = Visibility.Visible;

        try
        {
            await Task.Run(() =>
            {
                if (ZapretService.IsRunning)
                {
                    ZapretService.StopBypass();
                }
                else
                {
                    string presetToRun = SelectedPreset;
                    if (string.IsNullOrEmpty(presetToRun))
                        presetToRun = SettingsManager.Instance.LastSelectedPreset;
                    if (string.IsNullOrEmpty(presetToRun) && Presets.Count > 0)
                        presetToRun = Presets[0];

                    if (string.IsNullOrEmpty(presetToRun)) return;

                    string serviceStatus = ZapretService.GetServiceStatus();
                    if (serviceStatus == "RUNNING")
                        ZapretService.RemoveService();

                    var mode = GetActiveGameFilterMode();
                    ZapretService.StartBypass(presetToRun, mode);
                }
            });

            if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    UpdateUIStatus();
                    _ = MeasureDashboardMetricsAsync();
                });
            }
            else
            {
                UpdateUIStatus();
                _ = MeasureDashboardMetricsAsync();
            }
        }
        finally
        {
            IsActionButtonEnabled = true;
            IsProgressRingActive = false;
            ProgressRingVisibility = Visibility.Collapsed;
        }
    }

    [RelayCommand]
    private void CopyArgs()
    {
        if (!string.IsNullOrEmpty(PresetArgumentsText))
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(PresetArgumentsText);
            Clipboard.SetContent(dataPackage);
        }
    }

    [RelayCommand]
    private void CopyLog()
    {
        if (!string.IsNullOrEmpty(LaunchLogText))
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(LaunchLogText);
            Clipboard.SetContent(dataPackage);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LaunchLogText = "";
    }

    [RelayCommand]
    private void ApplyWinnerNow()
    {
        if (DiagnosticResultManager.BestPresets.Count > 0)
        {
            SelectAndApplyPreset(DiagnosticResultManager.BestPresets[0].PresetName);
        }
        CloseRecommendationOverlay();
    }

    [RelayCommand]
    private void CloseOverlay()
    {
        CloseRecommendationOverlay();
    }

    public void SelectAndApplyPreset(string presetName)
    {
        int idx = Presets.FindIndex(p => p.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (idx != -1)
        {
            SelectedPreset = Presets[idx];
        }

        if (!ZapretService.IsRunning)
        {
            _ = ToggleBypassAsync();
        }
    }

    private async Task MeasureDashboardMetricsAsync()
    {
        if (_isMeasuring) return;
        _isMeasuring = true;

        try
        {
            var ytTask = NetworkLatencyService.MeasureHttpSniLatencyAsync("YouTube", "https://www.youtube.com/generate_204", 1500);
            var dcTask = NetworkLatencyService.MeasureHttpSniLatencyAsync("Discord", "https://discord.com/api/v9/gateway", 1500);
            var lossTask = NetworkLatencyService.MeasurePacketLossAsync("1.1.1.1", 2, 500);

            await Task.WhenAll(ytTask, dcTask, lossTask);

            var resYt = ytTask.Result;
            var resDc = dcTask.Result;
            var lossVal = lossTask.Result;

            YtPingText = resYt.FormattedText;
            YtPingBrush = GetPingColorBrush(resYt);

            DcPingText = resDc.FormattedText;
            DcPingBrush = GetPingColorBrush(resDc);

            LossText = $"{lossVal}%";
            LossBrush = lossVal == 0
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));
        }
        finally
        {
            _isMeasuring = false;
        }
    }

    private static SolidColorBrush GetPingColorBrush(LatencyResult res)
    {
        if (res.IsBlocked || !res.IsSuccess)
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));
        if (res.LatencyMs <= 120)
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
        if (res.LatencyMs <= 250)
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11));

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 161, 161, 170));
    }

    private void AutoApplyCountdownTimer_Tick(object? sender, object e)
    {
        _countdownSeconds--;
        if (_countdownSeconds <= 0)
        {
            _autoApplyCountdownTimer.Stop();
            CloseRecommendationOverlay();
        }
        else
        {
            ApplyWinnerButtonText = $"Отлично ({_countdownSeconds} с)";
        }
    }

    private void CheckAndShowDiagnosticRecommendations()
    {
        if (!DiagnosticResultManager.HasUnseenResults || DiagnosticResultManager.BestPresets.Count == 0)
            return;

        var winner = DiagnosticResultManager.BestPresets[0];
        WinnerPresetName = winner.PresetName;

        string isp = IspService.CachedIspName != "Не определен" ? $" Провайдер: {IspService.CachedIspName}" : "";
        WinnerStatsText = $"{winner.SuccessText} •{isp}";

        SelectAndApplyPreset(winner.PresetName);

        OverlayVisibility = Visibility.Visible;
        DiagnosticResultManager.MarkAsSeen();

        _countdownSeconds = 15;
        ApplyWinnerButtonText = $"Отлично ({_countdownSeconds} с)";
        _autoApplyCountdownTimer.Start();
    }

    private void CloseRecommendationOverlay()
    {
        _autoApplyCountdownTimer.Stop();
        OverlayVisibility = Visibility.Collapsed;
    }

    private static string GetActiveGameFilterMode()
    {
        try
        {
            var root = ZapretService.FindZapretRoot();
            var file = Path.Combine(root, "utils", "game_filter.enabled");
            if (!File.Exists(file)) return "disabled";
            return File.ReadAllText(file).Trim().ToLower();
        }
        catch
        {
            return "disabled";
        }
    }
}

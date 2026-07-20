using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class DashboardPage : Page
{
    private string _selectedPreset = "";
    private readonly DispatcherTimer _autoMetricsTimer;
    private bool _isMeasuring = false;

    private readonly DispatcherTimer _autoApplyCountdownTimer;
    private int _countdownSeconds = 5;

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += DashboardPage_Loaded;
        Unloaded += DashboardPage_Unloaded;

        _autoMetricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoMetricsTimer.Tick += (s, e) => _ = MeasureDashboardMetricsAsync();

        _autoApplyCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoApplyCountdownTimer.Tick += AutoApplyCountdownTimer_Tick;
    }

    private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        ZapretService.OnStatusChanged += OnZapretStatusChanged;
        ZapretService.OnLogReceived += OnLogReceived;

        LoadPresets();
        UpdateUIStatus();
        _ = MeasureDashboardMetricsAsync();
        _autoMetricsTimer.Start();

        CheckAndShowDiagnosticRecommendations();
    }

    private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ZapretService.OnStatusChanged -= OnZapretStatusChanged;
        ZapretService.OnLogReceived -= OnLogReceived;
        _autoMetricsTimer.Stop();
        _autoApplyCountdownTimer.Stop();
    }

    // ── Presets ─────────────────────────────────────────────────────────────────

    private void LoadPresets()
    {
        var presets = ZapretService.GetPresets();
        PresetComboBox.ItemsSource = presets;

        if (presets.Count > 0)
        {
            var savedPreset = SettingsManager.Instance.LastSelectedPreset;
            int savedIndex = presets.FindIndex(p => p.Equals(savedPreset, StringComparison.OrdinalIgnoreCase));
            if (savedIndex != -1)
                PresetComboBox.SelectedIndex = savedIndex;
            else
            {
                int generalIndex = presets.FindIndex(p => p.Equals("general.bat", StringComparison.OrdinalIgnoreCase));
                PresetComboBox.SelectedIndex = generalIndex != -1 ? generalIndex : 0;
            }
        }
        else
        {
            PresetArgumentsTextBox.Text = "Предупреждение: пресеты (.bat файлы) не обнаружены в папке zapret.";
        }
    }

    private string GetActiveGameFilterMode()
    {
        var root = ZapretService.FindZapretRoot();
        var file = Path.Combine(root, "utils", "game_filter.enabled");
        if (!File.Exists(file)) return "disabled";
        try { return File.ReadAllText(file).Trim().ToLower(); }
        catch { return "disabled"; }
    }

    // ── Status display ──────────────────────────────────────────────────────────

    private void UpdateUIStatus()
    {
        bool isRunning = ZapretService.IsRunning;
        string serviceStatus = ZapretService.GetServiceStatus();
        bool isServiceInstalled = ZapretService.IsServiceInstalled();
        bool diagRunning = ZapretService.IsDiagnosticsRunning;

        // Orb colors
        Windows.UI.Color orbOuter, orbInner, iconColor;
        string iconGlyph;
        string statusTitle, serviceNote;
        string btnText;

        if (isRunning)
        {
            orbOuter  = Windows.UI.Color.FromArgb(255, 15, 40, 15);
            orbInner  = Windows.UI.Color.FromArgb(255, 22, 90, 22);
            iconColor = Windows.UI.Color.FromArgb(255, 144, 238, 144);
            iconGlyph = "\uE73E";
            statusTitle = "Обход активен";
            serviceNote = "Запущен напрямую через GUI";
            btnText  = "Остановить обход";
        }
        else if (serviceStatus == "RUNNING")
        {
            orbOuter  = Windows.UI.Color.FromArgb(255, 15, 40, 15);
            orbInner  = Windows.UI.Color.FromArgb(255, 22, 90, 22);
            iconColor = Windows.UI.Color.FromArgb(255, 144, 238, 144);
            iconGlyph = "\uE73E";
            statusTitle = "Обход активен";
            serviceNote = "Работает через службу Windows";
            btnText  = "Запустить обход";
        }
        else if (serviceStatus == "START_PENDING")
        {
            orbOuter  = Windows.UI.Color.FromArgb(255, 40, 30, 0);
            orbInner  = Windows.UI.Color.FromArgb(255, 90, 65, 0);
            iconColor = Windows.UI.Color.FromArgb(255, 255, 190, 60);
            iconGlyph = "\uE72C";
            statusTitle = "Запуск службы...";
            serviceNote = "Служба Windows инициализируется";
            btnText  = "Запустить обход";
        }
        else
        {
            orbOuter  = Windows.UI.Color.FromArgb(255, 40, 12, 12);
            orbInner  = Windows.UI.Color.FromArgb(255, 90, 22, 22);
            iconColor = Windows.UI.Color.FromArgb(255, 240, 80, 80);
            iconGlyph = "\uE71A";
            statusTitle = "Обход не запущен";
            serviceNote = $"Автозапуск через службу: {serviceStatus}";
            btnText  = "Запустить обход";
        }

        StatusOrbBg.Color = orbOuter;
        StatusOrbInnerBg.Color = orbInner;
        StatusIconFg.Color = iconColor;
        StatusIndicatorIcon.Glyph = iconGlyph;
        StatusText.Text = statusTitle;
        ServiceStatusText.Text = serviceNote;

        ActionBypassText.Text = btnText;

        // Disable button during diagnostics
        ActionBypassButton.IsEnabled = !diagRunning;
    }

    private void OnZapretStatusChanged(bool running)
    {
        DispatcherQueue.TryEnqueue(UpdateUIStatus);
    }

    // ── Log feed ────────────────────────────────────────────────────────────────

    private void OnLogReceived(string line)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LaunchLogTextBlock.Text += line + "\n";
            // Scroll to bottom
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null, true);
        });
    }

    private void CopyArgsButton_Click(object sender, RoutedEventArgs e)
    {
        var text = PresetArgumentsTextBox.Text;
        if (!string.IsNullOrEmpty(text))
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
    }

    private void CopyLogButton_Click(object sender, RoutedEventArgs e)
    {
        var text = LaunchLogTextBlock.Text;
        if (!string.IsNullOrEmpty(text))
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchLogTextBlock.Text = "";
    }

    // ── Preset selection ────────────────────────────────────────────────────────

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is string preset)
        {
            _selectedPreset = preset;
            SettingsManager.Instance.LastSelectedPreset = preset;
            SettingsManager.Save();

            var mode = GetActiveGameFilterMode();
            var rawArgs = ZapretService.ParseArgumentsFromBatch(preset, mode);

            // Split each --flag onto its own line for readability
            var formatted = rawArgs
                .Replace(" --", "\n--")
                .Trim();
            PresetArgumentsTextBox.Text = formatted;
        }
    }

    // ── Bypass control ──────────────────────────────────────────────────────────

    private async void ActionBypassButton_Click(object sender, RoutedEventArgs e)
    {
        if (ZapretService.IsDiagnosticsRunning) return;

        ActionBypassButton.IsEnabled = false;
        ActionBypassProgressRing.IsActive = true;
        ActionBypassProgressRing.Visibility = Visibility.Visible;

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
                    if (string.IsNullOrEmpty(_selectedPreset)) return;

                    string serviceStatus = ZapretService.GetServiceStatus();
                    if (serviceStatus == "RUNNING")
                        ZapretService.RemoveService();

                    var mode = GetActiveGameFilterMode();
                    ZapretService.StartBypass(_selectedPreset, mode);
                }
            });
            
            DispatcherQueue.TryEnqueue(UpdateUIStatus);
            _ = MeasureDashboardMetricsAsync();
        }
        finally
        {
            ActionBypassButton.IsEnabled = true;
            ActionBypassProgressRing.IsActive = false;
            ActionBypassProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private async Task MeasureDashboardMetricsAsync()
    {
        if (_isMeasuring) return;
        _isMeasuring = true;

        try
        {
            // Perform REAL TLS SNI Handshake & HTTP Probes (DPI resets TLS Client Hello on YouTube/Discord)
            var ytTask = NetworkLatencyService.MeasureHttpSniLatencyAsync("YouTube", "https://www.youtube.com/generate_204", 1500);
            var dcTask = NetworkLatencyService.MeasureHttpSniLatencyAsync("Discord", "https://discord.com/api/v9/gateway", 1500);
            var lossTask = NetworkLatencyService.MeasurePacketLossAsync("1.1.1.1", 2, 500);

            await Task.WhenAll(ytTask, dcTask, lossTask);

            var resYt = ytTask.Result;
            var resDc = dcTask.Result;
            var lossVal = lossTask.Result;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (YtPingText != null)
                {
                    YtPingText.Text = resYt.FormattedText;
                    YtPingText.Foreground = GetPingColorBrush(resYt);
                }
                if (DcPingText != null)
                {
                    DcPingText.Text = resDc.FormattedText;
                    DcPingText.Foreground = GetPingColorBrush(resDc);
                }
                if (LossText != null)
                {
                    LossText.Text = $"{lossVal}%";
                    LossText.Foreground = lossVal == 0
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129))  // #10B981 Green
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));   // #EF4444 Red
                }
            });
        }
        finally
        {
            _isMeasuring = false;
        }
    }

    private static SolidColorBrush GetPingColorBrush(LatencyResult res)
    {
        if (res.IsBlocked || !res.IsSuccess)
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)); // Red (#EF4444)
        if (res.LatencyMs <= 120)
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)); // Green (#10B981)
        if (res.LatencyMs <= 250)
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)); // Yellow (#F59E0B)

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 161, 161, 170));   // Grey (#A1A1AA)
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
            if (ApplyWinnerNowButtonText != null)
            {
                ApplyWinnerNowButtonText.Text = $"Отлично ({_countdownSeconds} с)";
            }
        }
    }

    private void CheckAndShowDiagnosticRecommendations()
    {
        if (!DiagnosticResultManager.HasUnseenResults || DiagnosticResultManager.BestPresets.Count == 0)
            return;

        var winner = DiagnosticResultManager.BestPresets[0];
        OverlayWinnerPresetName.Text = winner.PresetName;

        string isp = IspService.CachedIspName != "Не определен" ? $" Провайдер: {IspService.CachedIspName}" : "";
        OverlayWinnerStatsText.Text = $"{winner.SuccessText} •{isp}";

        // Automatically apply the winning strategy
        SelectAndApplyPreset(winner.PresetName);

        DiagnosticRecommendationOverlay.Visibility = Visibility.Visible;
        DiagnosticResultManager.MarkAsSeen();

        _countdownSeconds = 15;
        if (ApplyWinnerNowButtonText != null)
        {
            ApplyWinnerNowButtonText.Text = $"Отлично ({_countdownSeconds} с)";
        }
        _autoApplyCountdownTimer.Start();
    }

    private Button CreateStrategyRecommendationCard(DiagnosticPresetScore score, bool isRecommended)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(isRecommended ? Windows.UI.Color.FromArgb(255, 24, 24, 28) : Windows.UI.Color.FromArgb(255, 18, 18, 20)),
            BorderBrush = new SolidColorBrush(isRecommended ? Windows.UI.Color.FromArgb(255, 56, 189, 248) : Windows.UI.Color.FromArgb(255, 39, 39, 42)),
            BorderThickness = new Thickness(isRecommended ? 1.5 : 1),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sp = new StackPanel { Spacing = 2 };
        var nameText = new TextBlock
        {
            Text = score.PresetName,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(score.IsWinner ? Windows.UI.Color.FromArgb(255, 56, 189, 248) : Microsoft.UI.Colors.White)
        };
        var statsText = new TextBlock
        {
            Text = $"{score.SuccessText}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 161, 161, 170))
        };

        sp.Children.Add(nameText);
        sp.Children.Add(statsText);
        Grid.SetColumn(sp, 0);
        grid.Children.Add(sp);

        var applyBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(isRecommended ? Windows.UI.Color.FromArgb(255, 30, 41, 59) : Windows.UI.Color.FromArgb(255, 30, 30, 32))
        };
        var applyText = new TextBlock
        {
            Text = "Выбрать",
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(isRecommended ? Windows.UI.Color.FromArgb(255, 56, 189, 248) : Windows.UI.Color.FromArgb(255, 161, 161, 170))
        };
        applyBadge.Child = applyText;
        Grid.SetColumn(applyBadge, 1);
        grid.Children.Add(applyBadge);

        btn.Content = grid;
        btn.Click += (s, e) =>
        {
            SelectAndApplyPreset(score.PresetName);
            CloseRecommendationOverlay();
        };

        return btn;
    }

    private void SelectAndApplyPreset(string presetName)
    {
        var presets = ZapretService.GetPresets();
        int idx = presets.FindIndex(p => p.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (idx != -1)
        {
            PresetComboBox.SelectedIndex = idx;
        }

        if (!ZapretService.IsRunning)
        {
            ActionBypassButton_Click(this, new RoutedEventArgs());
        }
    }

    private void ApplyWinnerNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (DiagnosticResultManager.BestPresets.Count > 0)
        {
            SelectAndApplyPreset(DiagnosticResultManager.BestPresets[0].PresetName);
        }
        CloseRecommendationOverlay();
    }

    private void CloseOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRecommendationOverlay();
    }

    private void CloseRecommendationOverlay()
    {
        _autoApplyCountdownTimer.Stop();
        DiagnosticRecommendationOverlay.Visibility = Visibility.Collapsed;
    }
}

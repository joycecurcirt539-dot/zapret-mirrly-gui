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

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += DashboardPage_Loaded;
        Unloaded += DashboardPage_Unloaded;
    }

    private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        ZapretService.OnStatusChanged += OnZapretStatusChanged;
        ZapretService.OnLogReceived += OnLogReceived;

        LoadPresets();
        UpdateUIStatus();
    }

    private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ZapretService.OnStatusChanged -= OnZapretStatusChanged;
        ZapretService.OnLogReceived -= OnLogReceived;
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
        string btnGlyph;

        if (isRunning)
        {
            orbOuter  = Windows.UI.Color.FromArgb(255, 15, 40, 15);
            orbInner  = Windows.UI.Color.FromArgb(255, 22, 90, 22);
            iconColor = Windows.UI.Color.FromArgb(255, 144, 238, 144);
            iconGlyph = "\uE73E";
            statusTitle = "Обход активен";
            serviceNote = "Запущен напрямую через GUI";
            btnText  = "Остановить обход";
            btnGlyph = "\uE71A";
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
            btnGlyph = "\uE768";
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
            btnGlyph = "\uE768";
        }
        else
        {
            orbOuter  = Windows.UI.Color.FromArgb(255, 40, 12, 12);
            orbInner  = Windows.UI.Color.FromArgb(255, 90, 22, 22);
            iconColor = Windows.UI.Color.FromArgb(255, 240, 80, 80);
            iconGlyph = "\uE71A";
            statusTitle = "Обход не запущен";
            serviceNote = isServiceInstalled
                ? $"Автозапуск через службу: {serviceStatus}"
                : "Автозапуск через службу: не настроен";
            btnText  = "Запустить обход";
            btnGlyph = "\uE768";
        }

        StatusOrbBg.Color = orbOuter;
        StatusOrbInnerBg.Color = orbInner;
        StatusIconFg.Color = iconColor;
        StatusIndicatorIcon.Glyph = iconGlyph;
        StatusText.Text = statusTitle;
        ServiceStatusText.Text = serviceNote;

        ActionBypassIcon.Glyph = btnGlyph;
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

    private void ActionBypassButton_Click(object sender, RoutedEventArgs e)
    {
        if (ZapretService.IsDiagnosticsRunning) return;

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
        UpdateUIStatus();
    }

    private void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateUIStatus();
    }
}

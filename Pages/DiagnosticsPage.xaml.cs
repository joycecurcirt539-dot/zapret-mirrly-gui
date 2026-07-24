using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Documents;
using Windows.ApplicationModel.DataTransfer;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.Pages;

public class StrategyScoreItem : INotifyPropertyChanged
{
    private string _configName = "";
    private string _details = "";
    private string _scoreText = "";
    private int _okCount;
    private int _failCount;
    private int _unsupCount;
    private int _blockedCount;
    private Brush _badgeBackground = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private Brush _badgeForeground = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private string _rank = "";
    private Brush _rankColor = new SolidColorBrush(Microsoft.UI.Colors.Gray);
    private string _rankGlyph = "";
    private double _successPercent;

    public string ConfigName
    {
        get => _configName;
        set { _configName = value; OnPropertyChanged(); }
    }

    public string Details
    {
        get => _details;
        set { _details = value; OnPropertyChanged(); }
    }

    public string ScoreText
    {
        get => _scoreText;
        set { _scoreText = value; OnPropertyChanged(); }
    }

    public int OkCount
    {
        get => _okCount;
        set { _okCount = value; OnPropertyChanged(); }
    }

    public int FailCount
    {
        get => _failCount;
        set { _failCount = value; OnPropertyChanged(); }
    }

    public int UnsupCount
    {
        get => _unsupCount;
        set { _unsupCount = value; OnPropertyChanged(); }
    }

    public int BlockedCount
    {
        get => _blockedCount;
        set { _blockedCount = value; OnPropertyChanged(); }
    }

    public Brush BadgeBackground
    {
        get => _badgeBackground;
        set { _badgeBackground = value; OnPropertyChanged(); }
    }

    public Brush BadgeForeground
    {
        get => _badgeForeground;
        set { _badgeForeground = value; OnPropertyChanged(); }
    }

    public string Rank
    {
        get => _rank;
        set { _rank = value; OnPropertyChanged(); }
    }

    public Brush RankColor
    {
        get => _rankColor;
        set { _rankColor = value; OnPropertyChanged(); }
    }

    public string RankGlyph
    {
        get => _rankGlyph;
        set { _rankGlyph = value; OnPropertyChanged(); }
    }

    public double SuccessPercent
    {
        get => _successPercent;
        set { _successPercent = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed partial class DiagnosticsPage : Page
{
    private CancellationTokenSource? _diagnosticCts; // new native engine cancellation
    private readonly ObservableCollection<StrategyScoreItem> _strategyScores = new();
    private string _bestPresetFound = "";
    private readonly List<string> _allPresets = new();
    private readonly List<string> _selectedPresets = new();
    private readonly StringBuilder _logBuffer = new();
    private readonly List<(string Text, bool IsError)> _pendingLogLines = new();
    private DateTime _lastLogFlush = DateTime.MinValue;
    private bool _isLogFlushScheduled = false;
    private DateTime _diagStartTime = DateTime.MinValue;
    private int _totalDiagPresets = 0;
    private int _completedDiagPresets = 0;
    private DispatcherTimer? _realtimeEtaTimer;

    public DiagnosticsPage()
    {
        InitializeComponent();
        
        StrategyScoresListView.ItemsSource = _strategyScores;
        Loaded += DiagnosticsPage_Loaded;
        Unloaded += DiagnosticsPage_Unloaded;

        _realtimeEtaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _realtimeEtaTimer.Tick += RealtimeEtaTimer_Tick;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string param && param == "start")
        {
            TriggerAutoStart();
        }
    }

    public void TriggerAutoStart()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            PlayButton_Click(this, new RoutedEventArgs());
        });
    }

    private bool _isFirstLoad = true;

    private void DiagnosticsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            AnimationHelper.AnimateElementEntrance(DiagnosticsTabView, 0, -40, 0.98, 220, 0);
            AnimationHelper.AnimateElementEntrance(ControlDeckBorder, 0, -20, 1.0, 240, 40);
            AnimationHelper.AnimateElementEntrance(DiagnosticsRightColumn, 50, 0, 0.96, 270, 120);
        }

        _allPresets.Clear();
        var presets = ZapretService.GetPresets();
        _allPresets.AddRange(presets);

        _selectedPresets.Clear();
        var savedSelected = SettingsManager.Instance.DiagnosticsSelectedPresets;
        if (savedSelected != null)
        {
            foreach (var preset in savedSelected)
            {
                if (_allPresets.Contains(preset))
                {
                    _selectedPresets.Add(preset);
                }
            }
        }

        UpdateSelectConfigsButtonText();

        SetComboBoxByTag(TestTypeComboBox, SettingsManager.Instance.DiagnosticsTestType);
        SetComboBoxByTag(RunModeComboBox, SettingsManager.Instance.DiagnosticsRunMode);
        SetComboBoxByTag(TimeoutComboBox, SettingsManager.Instance.DiagnosticsTimeout);

        TimeoutComboBox.SelectionChanged += (s, ev) =>
        {
            if (TimeoutComboBox.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
            {
                SettingsManager.Instance.DiagnosticsTimeout = tag;
                SettingsManager.Save();
            }
        };

        _ = Task.Run(async () =>
        {
            var ispName = await IspService.GetIspNameAsync();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (IspStatusTextBlock != null)
                    IspStatusTextBlock.Text = $"Провайдер: {ispName}";
            });
        });
    }

    private void SetComboBoxByTag(ComboBox comboBox, string tag)
    {
        if (comboBox == null || string.IsNullOrEmpty(tag)) return;
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag as string == tag)
            {
                comboBox.SelectedItem = cbi;
                break;
            }
        }
    }

    private void DiagnosticsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Do not stop the diagnostics script when switching tabs since the page is cached.
    }

    private void ConfigsFlyout_Opened(object sender, object e)
    {
        if (ConfigsListView == null) return;
        if (ConfigsListView.Items.Count > 0) return; // Only populate once

        foreach (var preset in _allPresets)
        {
            var checkBox = new CheckBox
            {
                Content = preset,
                Tag = preset,
                Margin = new Thickness(0, 4, 0, 4),
                IsChecked = _selectedPresets.Contains(preset)
            };
            checkBox.Checked += ConfigCheckBox_Changed;
            checkBox.Unchecked += ConfigCheckBox_Changed;
            ConfigsListView.Items.Add(checkBox);
        }
    }

    private void ConfigCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (ConfigsListView == null) return;
        _selectedPresets.Clear();
        foreach (var item in ConfigsListView.Items)
        {
            if (item is CheckBox cb && cb.IsChecked == true && cb.Tag is string preset)
            {
                _selectedPresets.Add(preset);
            }
        }
        UpdateSelectConfigsButtonText();

        SettingsManager.Instance.DiagnosticsSelectedPresets = new List<string>(_selectedPresets);
        SettingsManager.Save();
    }

    private void UpdateSelectConfigsButtonText()
    {
        if (SelectConfigsButton != null)
        {
            SelectConfigsButton.Content = _selectedPresets.Count > 0 ? $"Выбрать ({_selectedPresets.Count})" : "Выбрать";
        }
    }

    private void TestTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TestTypeComboBox != null && TestTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            SettingsManager.Instance.DiagnosticsTestType = tag;
            SettingsManager.Save();
        }
    }

    private void RunModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectConfigsButton != null && RunModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            SelectConfigsButton.IsEnabled = tag == "select";
            SettingsManager.Instance.DiagnosticsRunMode = tag;
            SettingsManager.Save();
        }
    }

    private void ConfigsFlyout_Closed(object sender, object e)
    {
        // Save choices
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (ZapretService.IsDiagnosticsRunning) return;

        PlayButton.IsEnabled = false;
        PlayProgressRing.IsActive = true;
        PlayProgressRing.Visibility = Visibility.Visible;

        try
        {
            // 1. Service Conflict Handling (Y/N prompt)
            if (ZapretService.IsServiceInstalled())
            {
                var serviceStatus = ZapretService.GetServiceStatus();
                var dialog = new ContentDialog
                {
                    Title = "Конфликт со службой zapret",
                    Content = $"Обнаружена установленная служба Windows (Статус: {serviceStatus}). Для проведения диагностики службу необходимо временно удалить (настройки сохранятся).\n\nВы согласны удалить службу и продолжить?",
                    PrimaryButtonText = "Да (Рекомендуется)",
                    CloseButtonText = "Нет",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    StatusStripTextBlock.Text = "Удаление службы перед диагностикой...";
                    await Task.Run(() => ZapretService.RemoveService());
                }
                else
                {
                    StatusStripTextBlock.Text = "Диагностика отменена из-за конфликта службы.";
                    PlayButton.IsEnabled = true;
                    PlayProgressRing.IsActive = false;
                    PlayProgressRing.Visibility = Visibility.Collapsed;
                    return;
                }
            }

            // 2. GUI Process check
            if (ZapretService.IsRunning)
            {
                await Task.Run(() => ZapretService.StopBypass());
            }

            StartDiagnostics();
        }
        catch (Exception ex)
        {
            StatusStripTextBlock.Text = $"Ошибка инициализации: {ex.Message}";
            PlayButton.IsEnabled = true;
            PlayProgressRing.IsActive = false;
            PlayProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void StartDiagnostics()
    {
        ZapretService.IsDiagnosticsRunning = true;

        // Prepare UI state
        PlayButton.IsEnabled = false;
        PlayProgressRing.IsActive = true;
        PlayProgressRing.Visibility = Visibility.Visible;
        StopButton.IsEnabled = true;
        TestTypeComboBox.IsEnabled = false;
        RunModeComboBox.IsEnabled = false;
        SelectConfigsButton.IsEnabled = false;
        TimeoutComboBox.IsEnabled = false;
        StatusProgressRing.IsActive = true;
        StatusStripTextBlock.Text = "Выполнение диагностики...";
        
        _strategyScores.Clear();
        BestStrategyCard.Visibility = Visibility.Collapsed;
        _bestPresetFound = "";
        lock (_logBuffer)
        {
            _logBuffer.Clear();
            _pendingLogLines.Clear();
        }
        ConsoleLogRichTextBlock.Blocks.Clear();

        // Collect parameters
        string testType = (TestTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "standard";
        string runMode  = (RunModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        string timeout  = (TimeoutComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "5";
        int timeoutSec  = int.TryParse(timeout, out var t) ? t : 5;

        // Build preset list directly — no env-var juggling
        var presetsToRun = runMode == "select" && _selectedPresets.Count > 0
            ? _selectedPresets.ToList()
            : _allPresets.ToList();

        _diagnosticCts?.Cancel();
        _diagnosticCts = new CancellationTokenSource();

        _diagStartTime = DateTime.Now;
        _totalDiagPresets = presetsToRun.Count;
        _completedDiagPresets = 0;
        _realtimeEtaTimer?.Start();

        if (StatusStripTextBlock != null)
        {
            StatusStripTextBlock.Text = $"Диагностика запущена • 1 из {_totalDiagPresets} (расчёт времени...)";
        }

        var options = new DiagnosticOptions
        {
            Presets        = presetsToRun,
            TimeoutSeconds = timeoutSec,
            TestType       = testType,
            InitDelaySeconds = 0
        };

        var engineProgress = new Progress<DiagnosticProgressEvent>(OnDiagnosticProgress);
        _ = Task.Run(() => DiagnosticEngine.RunAsync(options, engineProgress, _diagnosticCts.Token));
    }

    private void AddStrategyScore(string config, int ok, int fail, int unsup, int blocked, bool isDpi)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Remove from list if already exists (updating score)
            for (int i = 0; i < _strategyScores.Count; i++)
            {
                if (_strategyScores[i].ConfigName.Equals(config, StringComparison.OrdinalIgnoreCase))
                {
                    _strategyScores.RemoveAt(i);
                    break;
                }
            }

            var item = new StrategyScoreItem
            {
                ConfigName = config,
                OkCount = ok,
                FailCount = fail,
                UnsupCount = unsup,
                BlockedCount = blocked
            };

            int total = ok + fail + blocked;
            double ratio = total > 0 ? (double)ok / total : 0;
            item.SuccessPercent = ratio * 100.0;

            Windows.UI.Color bgColor;
            Windows.UI.Color fgColor = Microsoft.UI.Colors.White;

            if (isDpi)
            {
                item.Details = $"ОК: {ok}  |  Фейл: {fail}  |  Блок: {blocked}";
                item.ScoreText = $"{ok} OK";
            }
            else
            {
                item.Details = $"ОК: {ok}  |  Ошибки: {fail}  |  Неподд.: {unsup}";
                item.ScoreText = $"{ok} OK";
            }

            if (ok > 0 && fail == 0 && blocked == 0)
            {
                // 100% Success - Emerald Green
                bgColor = Windows.UI.Color.FromArgb(255, 16, 185, 129); // #10B981
            }
            else if (ratio >= 0.7)
            {
                // Mostly Working (>70%) - Lime Green
                bgColor = Windows.UI.Color.FromArgb(255, 132, 204, 22); // #84CC16
            }
            else if (ratio >= 0.3)
            {
                // Partially Working (30%-70%) - Amber
                bgColor = Windows.UI.Color.FromArgb(255, 245, 158, 11); // #F59E0B
            }
            else if (ok > 0)
            {
                // Barely Working (1%-30%) - Orange
                bgColor = Windows.UI.Color.FromArgb(255, 249, 115, 22); // #F97316
            }
            else
            {
                // 0% Success - Crimson Red
                bgColor = Windows.UI.Color.FromArgb(255, 220, 38, 38); // #DC2626
            }

            item.BadgeBackground = new SolidColorBrush(bgColor);
            item.BadgeForeground = new SolidColorBrush(fgColor);

            _strategyScores.Add(item);

            // Real-time sorting: best (highest OK) at top, worst at bottom
            var sorted = _strategyScores
                .OrderByDescending(s => s.OkCount)
                .ThenBy(s => s.FailCount)
                .ThenBy(s => s.BlockedCount)
                .ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var currentItem = sorted[i];
                currentItem.Rank = (i + 1).ToString();
                if (i == 0)
                {
                    currentItem.RankColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 189, 248)); // Blue/Accent
                    currentItem.RankGlyph = "1";
                }
                else if (i == 1)
                {
                    currentItem.RankColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 161, 161, 170)); // Zinc
                    currentItem.RankGlyph = "2";
                }
                else if (i == 2)
                {
                    currentItem.RankColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 161, 161, 170)); // Zinc
                    currentItem.RankGlyph = "3";
                }
                else
                {
                    currentItem.RankColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 113, 113, 122)); // Gray
                    currentItem.RankGlyph = (i + 1).ToString();
                }

                int oldIndex = _strategyScores.IndexOf(sorted[i]);
                if (oldIndex != i)
                {
                    _strategyScores.Move(oldIndex, i);
                }
            }
        });
    }

    private void AppendToConsole(string text, bool isError = false)
    {
        lock (_logBuffer)
        {
            _logBuffer.AppendLine(text);
            
            // Limit buffer size to prevent memory exhaustion (keep last ~100k chars)
            if (_logBuffer.Length > 150000)
            {
                _logBuffer.Remove(0, 50000);
            }

            _pendingLogLines.Add((text, isError));

            if (_isLogFlushScheduled) return;

            _isLogFlushScheduled = true;
        }

        var now = DateTime.UtcNow;
        var elapsed = (now - _lastLogFlush).TotalMilliseconds;

        if (elapsed >= 100)
        {
            DispatcherQueue.TryEnqueue(() => FlushLogToUI());
        }
        else
        {
            int delayMs = (int)(100 - elapsed);
            if (delayMs < 10) delayMs = 10;
            Task.Delay(delayMs).ContinueWith(_ =>
            {
                DispatcherQueue.TryEnqueue(() => FlushLogToUI());
            });
        }
    }

    private void FlushLogToUI()
    {
        List<(string Text, bool IsError)> linesToRender;
        lock (_logBuffer)
        {
            _isLogFlushScheduled = false;
            linesToRender = new List<(string Text, bool IsError)>(_pendingLogLines);
            _pendingLogLines.Clear();
        }

        if (linesToRender.Count == 0) return;

        try
        {
            if (ConsoleLogRichTextBlock != null)
            {
                foreach (var item in linesToRender)
                {
                    if (string.IsNullOrEmpty(item.Text)) continue;

                    var parts = item.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (var part in parts)
                    {
                        if (string.IsNullOrEmpty(part) && parts.Length > 1 && part == parts.Last())
                            continue; // Skip trailing empty split element

                        var paragraph = new Paragraph();
                        var run = new Run { Text = part };

                        // 1. Red - Error / Fail
                        bool isErrorLine = item.IsError || 
                                           part.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                                           part.Contains("[winws ERROR]", StringComparison.OrdinalIgnoreCase) ||
                                           part.Contains("[SERVICE ERROR]", StringComparison.OrdinalIgnoreCase) ||
                                           part.Contains("[Системная ошибка]", StringComparison.OrdinalIgnoreCase) ||
                                           part.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                                           part.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
                                           part.Contains("ERR", StringComparison.Ordinal) ||
                                           part.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                                           part.Contains("blocked", StringComparison.OrdinalIgnoreCase);

                        // 2. Orange - Warning / Timeout
                        bool isWarningLine = part.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("leftover", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("skipped", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("Внимание", StringComparison.OrdinalIgnoreCase);

                        // 3. Green - Success / OK
                        bool isSuccessLine = part.Contains("OK", StringComparison.Ordinal) ||
                                             part.Contains("available", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("успешно", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("найден", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("found", StringComparison.OrdinalIgnoreCase) ||
                                             part.Contains("detected", StringComparison.OrdinalIgnoreCase);

                        // 4. Blue - Operations / Cmd
                        bool isActionLine = part.Contains("[CMD]", StringComparison.OrdinalIgnoreCase) ||
                                            part.Contains("[SERVICE]", StringComparison.OrdinalIgnoreCase) ||
                                            part.Contains("Checking strategy", StringComparison.OrdinalIgnoreCase) ||
                                            part.Contains("Testing:", StringComparison.OrdinalIgnoreCase) ||
                                            part.Contains("Запуск", StringComparison.OrdinalIgnoreCase) ||
                                            part.Contains("Остановка", StringComparison.OrdinalIgnoreCase) ||
                                            part.Contains("Удаление", StringComparison.OrdinalIgnoreCase) ||
                                            part.Contains("Восстановление", StringComparison.OrdinalIgnoreCase);

                        if (isErrorLine)
                        {
                            run.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 107, 107)); // Hex #FF6B6B
                        }
                        else if (isWarningLine)
                        {
                            run.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 146, 43)); // Hex #FF922B
                        }
                        else if (isSuccessLine)
                        {
                            run.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 81, 207, 102)); // Hex #51CF66
                        }
                        else if (isActionLine)
                        {
                            run.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 154, 240)); // Hex #339AF0
                        }
                        else
                        {
                            run.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 173, 181, 189)); // Hex #ADB5BD
                        }

                        paragraph.Inlines.Add(run);
                        ConsoleLogRichTextBlock.Blocks.Add(paragraph);
                    }
                }

                while (ConsoleLogRichTextBlock.Blocks.Count > 1000)
                {
                    ConsoleLogRichTextBlock.Blocks.RemoveAt(0);
                }

                ScrollRichTextBlockToBottom();
            }
            _lastLogFlush = DateTime.UtcNow;
        }
        catch { }
    }

    private void ScrollRichTextBlockToBottom()
    {
        if (ConsoleLogScrollViewer != null)
        {
            ConsoleLogScrollViewer.ChangeView(null, ConsoleLogScrollViewer.ScrollableHeight, null, false);
        }
    }



    private void ScrollTextBoxToBottom(TextBox textBox)
    {
        if (VisualTreeHelper.GetChildrenCount(textBox) > 0)
        {
            var grid = VisualTreeHelper.GetChild(textBox, 0) as Grid;
            if (grid != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(grid); i++)
                {
                    var child = VisualTreeHelper.GetChild(grid, i);
                    if (child is ScrollViewer sv)
                    {
                        sv.ChangeView(null, sv.ScrollableHeight, null, false);
                        break;
                    }
                }
            }
        }
    }

    private void ResetUIState(string statusText)
    {
        ZapretService.IsDiagnosticsRunning = false;
        _realtimeEtaTimer?.Stop();

        DispatcherQueue.TryEnqueue(() =>
        {
            PlayButton.IsEnabled = true;
            PlayProgressRing.IsActive = false;
            PlayProgressRing.Visibility = Visibility.Collapsed;
            StopButton.IsEnabled = false;
            
            string runMode = (RunModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
            SelectConfigsButton.IsEnabled = runMode == "select";
            
            TestTypeComboBox.IsEnabled = true;
            RunModeComboBox.IsEnabled = true;
            TimeoutComboBox.IsEnabled = true;
            StatusProgressRing.IsActive = false;
            StatusStripTextBlock.Text = statusText;
        });
    }

    private void StopDiagnosticsProcess()
    {
        // Cancel the native engine
        _diagnosticCts?.Cancel();

        // Kill any running winws immediately
        ZapretService.KillAllWinwsProcesses();

        // Save whatever scores we've already collected
        if (_strategyScores != null && _strategyScores.Count > 0)
        {
            var scores = _strategyScores.Select(s => new DiagnosticPresetScore
            {
                PresetName   = s.ConfigName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ? s.ConfigName : s.ConfigName + ".bat",
                SuccessCount = s.OkCount,
                TotalCount   = s.OkCount + s.FailCount + s.BlockedCount,
                AvgPingMs    = s.OkCount > 0 ? 45 : -1
            }).ToList();
            DiagnosticResultManager.SaveResults(scores);
        }

        ResetUIState("Диагностика остановлена");
    }

    // ── Native engine progress handler ─────────────────────────────────────────

    private void OnDiagnosticProgress(DiagnosticProgressEvent evt)
    {
        // Log everything to console (Progress<T> already marshals to UI thread)
        if (!string.IsNullOrEmpty(evt.Message) && evt.Message != "done")
            AppendToConsole(evt.Message, evt.IsError);

        // Update ETA status header
        if (evt.Type == DiagnosticEventType.PresetFinished && evt.Result != null)
        {
            var r = evt.Result;
            AddStrategyScore(r.PresetName, r.Ok, r.Fail, r.Unsup, r.Blocked, r.IsDpi);
            _completedDiagPresets++;
        }

        // When everything is done, save results and reset UI
        if (evt.Type == DiagnosticEventType.AllDone && ZapretService.IsDiagnosticsRunning)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_strategyScores.Count > 0)
                {
                    var scores = _strategyScores.Select(s => new DiagnosticPresetScore
                    {
                        PresetName   = s.ConfigName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ? s.ConfigName : s.ConfigName + ".bat",
                        SuccessCount = s.OkCount,
                        TotalCount   = s.OkCount + s.FailCount + s.BlockedCount,
                        AvgPingMs    = s.OkCount > 0 ? 45 : -1
                    }).ToList();
                    DiagnosticResultManager.SaveResults(scores);

                    var winner = DiagnosticResultManager.BestPresets.FirstOrDefault(p => p.IsWinner);
                    if (winner != null)
                    {
                        _bestPresetFound = winner.PresetName;
                        BestStrategyTextBlock.Text = winner.PresetName;
                        BestStrategyCard.Visibility = Visibility.Visible;
                    }
                }
            });

            var totalElapsed = DateTime.Now - _diagStartTime;
            ResetUIState($"Завершено за {totalElapsed:mm\\:ss} • Протестировано {_completedDiagPresets} пресетов");
        }
    }

    private void RealtimeEtaTimer_Tick(object? sender, object e)
    {
        if (!ZapretService.IsDiagnosticsRunning || _diagStartTime == DateTime.MinValue)
            return;

        var elapsed = DateTime.Now - _diagStartTime;

        double avgSecPerPreset = _completedDiagPresets > 0
            ? elapsed.TotalSeconds / _completedDiagPresets
            : 2.0;

        int remainingPresets = Math.Max(0, _totalDiagPresets - _completedDiagPresets);
        int etaSec = (int)Math.Max(0, Math.Ceiling(remainingPresets * avgSecPerPreset));

        string etaStr = remainingPresets > 0 ? $" • Осталось ~{etaSec} сек" : "";
        string elapsedStr = $"прошло {elapsed:mm\\:ss}";
        int currentNumber = Math.Min(_totalDiagPresets, _completedDiagPresets + 1);

        if (StatusStripTextBlock != null)
        {
            StatusStripTextBlock.Text = $"Тест {currentNumber} из {_totalDiagPresets}{etaStr} ({elapsedStr})";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopDiagnosticsProcess();
        ResetUIState("Диагностика остановлена пользователем.");
    }

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to logs tab programmatically
        if (this.Frame.Parent is NavigationView nav)
        {
            // Find logs item
            foreach (var item in nav.MenuItems)
            {
                if (item is NavigationViewItem nvi && nvi.Tag as string == "logs")
                {
                    nav.SelectedItem = nvi;
                    break;
                }
            }
        }
    }

    private void CopyConsoleButton_Click(object sender, RoutedEventArgs e)
    {
        string fullLog;
        lock (_logBuffer)
        {
            fullLog = _logBuffer.ToString();
        }
        if (string.IsNullOrEmpty(fullLog)) return;
        var dataPackage = new DataPackage();
        dataPackage.SetText(fullLog);
        Clipboard.SetContent(dataPackage);
    }

    private void ApplyBestStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_bestPresetFound)) return;

        // Query active game filter from settings
        var root = ZapretService.FindZapretRoot();
        var gameFilterFile = Path.Combine(root, "utils", "game_filter.enabled");
        string mode = "disabled";
        if (File.Exists(gameFilterFile))
        {
            try { mode = File.ReadAllText(gameFilterFile).Trim().ToLower(); } catch { }
        }

        // Install service quietly without popping annoying dialogs
        ZapretService.InstallService(_bestPresetFound, mode);

        if (StatusStripTextBlock != null)
        {
            StatusStripTextBlock.Text = $"Стратегия '{_bestPresetFound}' успешно примена в системе.";
        }
    }

    // ─── System Diagnostics & Parity Actions ──────────────────────────────────

    // ─── System Diagnostics & Parity Actions ──────────────────────────────────

    private async void RunSystemDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        RunSystemDiagnosticsBtn.IsEnabled = false;
        FixSystemConflictsBtn.IsEnabled = false;
        ClearDiscordCacheBtn.IsEnabled = false;
        SystemDiagnosticsLogTextBox.Text = "Запуск системной проверки...\n";

        // 1. BFE Check (Native Win32 Service Query)
        var bfeRunning = await Task.Run(() => Win32ServiceManager.GetServiceStatusName("BFE") == "RUNNING");
        UpdateStatusCard(BfeStatusIcon, BfeStatusDetails, bfeRunning, "Служба BFE активна и работает", "Служба BFE отключена! Zapret не сможет работать.");
        LogDiag(bfeRunning ? "[PASS] Служба BFE активна." : "[FAIL] Служба BFE отключена.");

        // 2. Proxy Check
        var proxyActive = await Task.Run(() => {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                if (key != null)
                {
                    var val = key.GetValue("ProxyEnable");
                    return val != null && (int)val == 1;
                }
            }
            catch { }
            return false;
        });
        UpdateStatusCard(ProxyStatusIcon, ProxyStatusDetails, !proxyActive, "Системный прокси не используется", "Включен системный прокси! Может мешать обходу.");
        LogDiag(!proxyActive ? "[PASS] Системный прокси отключен." : "[WARN] Включен системный прокси.");

        // 3. TCP Timestamps Check (Native Registry Query)
        var timestampsActive = await Task.Run(() => CheckTcpTimestampsInRegistry());
        UpdateStatusCard(TimestampsStatusIcon, TimestampsStatusDetails, timestampsActive, "TCP timestamps включены", "TCP timestamps отключены! Рекомендуется включить.");
        LogDiag(timestampsActive ? "[PASS] TCP Timestamps включены." : "[WARN] TCP Timestamps отключены.");

        // 4. Conflicts Check (Native Service Manager)
        var conflictingServices = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2", "SmartByte", "EPWD", "TracSrvWrapper", "KNetworkService", "Killer Network Service", "iclsClient" };
        var foundConflicts = new List<string>();
        await Task.Run(() => {
            foreach (var svc in conflictingServices)
            {
                if (Win32ServiceManager.IsServiceInstalled(svc))
                {
                    foundConflicts.Add(svc);
                }
            }
            // Check AdGuard process
            var procs = Process.GetProcessesByName("AdguardSvc");
            if (procs.Length > 0) foundConflicts.Add("AdGuard (процесс)");
        });
        bool hasConflicts = foundConflicts.Count > 0;
        UpdateStatusCard(ConflictsStatusIcon, ConflictsStatusDetails, !hasConflicts, 
            "Конфликты служб и процессов не обнаружены", 
            $"Обнаружены конфликты: {string.Join(", ", foundConflicts)}");
        LogDiag(!hasConflicts ? "[PASS] Конфликтующие службы не обнаружены." : $"[FAIL] Обнаружены конфликты: {string.Join(", ", foundConflicts)}");

        // 5. VPN Check
        var activeVpns = new List<string>();
        await Task.Run(() => {
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    if (proc.ProcessName.Contains("vpn", StringComparison.OrdinalIgnoreCase) || 
                        proc.ProcessName.Contains("wireguard", StringComparison.OrdinalIgnoreCase) ||
                        proc.ProcessName.Contains("openvpn", StringComparison.OrdinalIgnoreCase))
                    {
                        activeVpns.Add(proc.ProcessName);
                    }
                }
            }
            catch { }
        });
        bool hasVpn = activeVpns.Count > 0;
        UpdateStatusCard(VpnStatusIcon, VpnStatusDetails, !hasVpn, "Активные VPN процессы не обнаружены", $"Обнаружены VPN процессы: {string.Join(", ", activeVpns)}");
        LogDiag(!hasVpn ? "[PASS] Активные VPN не запущены." : $"[WARN] Запущены VPN процессы: {string.Join(", ", activeVpns)}");

        // 6. Secure DNS (DoH) Check
        var dohEnabled = await Task.Run(() => {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters");
                if (key != null)
                {
                    foreach (var subkeyName in key.GetSubKeyNames())
                    {
                        using var subkey = key.OpenSubKey(subkeyName);
                        var val = subkey?.GetValue("DohFlags");
                        if (val != null && Convert.ToInt32(val) > 0) return true;
                    }
                }
            }
            catch { }
            return false;
        });
        UpdateStatusCard(DnsStatusIcon, DnsStatusDetails, dohEnabled, "Шифрование DNS (DoH) настроено в системе", "DoH не настроен на уровне Windows (рекомендуется настроить в браузере)");
        LogDiag(dohEnabled ? "[PASS] Безопасный DNS (DoH) настроен в Windows." : "[WARN] DoH не настроен на уровне Windows.");

        // 7. Hosts File Check
        var (hostsNeedsUpdate, hostsStatus, _) = await ZapretService.CheckHostsStatusAsync();
        UpdateStatusCard(HostsStatusIcon, HostsStatusDetails, !hostsNeedsUpdate, "В файле hosts присутствуют записи обхода", hostsStatus);
        LogDiag(!hostsNeedsUpdate ? "[PASS] Файл hosts содержит нужные записи обхода." : $"[WARN] {hostsStatus}");

        // 8. WinDivert Check (Native Service Query)
        var divertConflict = await Task.Run(() => {
            bool divertRunning = Win32ServiceManager.GetServiceStatusName("WinDivert") == "RUNNING" || 
                                 Win32ServiceManager.GetServiceStatusName("WinDivert14") == "RUNNING";
            return divertRunning && !ZapretService.IsRunning;
        });
        UpdateStatusCard(DivertStatusIcon, DivertStatusDetails, !divertConflict, "Драйвер WinDivert чист и готов к работе", "Обнаружена зависшая служба драйвера WinDivert!");
        LogDiag(!divertConflict ? "[PASS] Драйвер WinDivert в порядке." : "[FAIL] Обнаружен зависший драйвер WinDivert без активного процесса обхода.");

        SystemDiagnosticsLogTextBox.Text += "\nСистемная проверка завершена.\n";
        
        RunSystemDiagnosticsBtn.IsEnabled = true;
        FixSystemConflictsBtn.IsEnabled = true;
        ClearDiscordCacheBtn.IsEnabled = true;
    }

    private static bool CheckTcpTimestampsInRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\Tcpip\Parameters");
            if (key != null)
            {
                var val = key.GetValue("Tcp1323Opts");
                if (val != null)
                {
                    int opts = Convert.ToInt32(val);
                    return opts == 1 || opts == 3;
                }
            }
        }
        catch { }
        return false;
    }

    private void UpdateStatusCard(FontIcon icon, TextBlock details, bool success, string passText, string failText)
    {
        icon.Glyph = success ? "\uE73E" : "\uE711";
        icon.Foreground = success ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 144, 238, 144)) : new SolidColorBrush(Microsoft.UI.Colors.Orange);
        details.Text = success ? passText : failText;
    }

    private void LogDiag(string message)
    {
        SystemDiagnosticsLogTextBox.Text += message + Environment.NewLine;
    }

    private async void FixSystemConflictsBtn_Click(object sender, RoutedEventArgs e)
    {
        FixSystemConflictsBtn.IsEnabled = false;
        SystemDiagnosticsLogTextBox.Text += "\nУстранение конфликтов служб через Win32 API...\n";

        await Task.Run(() => {
            var conflictingServices = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2", "SmartByte", "EPWD", "TracSrvWrapper", "KNetworkService", "Killer Network Service", "iclsClient" };
            foreach (var svc in conflictingServices)
            {
                if (Win32ServiceManager.IsServiceInstalled(svc))
                {
                    DispatcherQueue.TryEnqueue(() => SystemDiagnosticsLogTextBox.Text += $"[FIX] Нативное удаление конфликтующей службы: {svc}...\n");
                    Win32ServiceManager.RemoveWin32Service(svc);
                }
            }

            // Cleanup WinDivert driver if process not running
            if (!ZapretService.IsRunning)
            {
                DispatcherQueue.TryEnqueue(() => SystemDiagnosticsLogTextBox.Text += "[FIX] Нативное удаление зависшей службы драйвера WinDivert...\n");
                Win32ServiceManager.RemoveWin32Service("WinDivert");
                Win32ServiceManager.RemoveWin32Service("WinDivert14");
            }
        });

        SystemDiagnosticsLogTextBox.Text += "[FIX] Конфликты устранены. Запустите повторную проверку.\n";
        FixSystemConflictsBtn.IsEnabled = true;
    }

    private async void ClearDiscordCacheBtn_Click(object sender, RoutedEventArgs e)
    {
        ClearDiscordCacheBtn.IsEnabled = false;
        SystemDiagnosticsLogTextBox.Text += "\nОчистка кэша Discord...\n";

        bool success = await Task.Run(() => {
            try
            {
                // Kill Discord
                foreach (var proc in Process.GetProcessesByName("Discord"))
                {
                    try {
                        proc.Kill(true);
                        DispatcherQueue.TryEnqueue(() => SystemDiagnosticsLogTextBox.Text += "[INFO] Процесс Discord.exe завершен.\n");
                    } catch { }
                }

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var discordDir = Path.Combine(appData, "discord");

                var cacheFolders = new[] { "Cache", "Code Cache", "GPUCache" };
                foreach (var folder in cacheFolders)
                {
                    var path = Path.Combine(discordDir, folder);
                    if (Directory.Exists(path))
                    {
                        try {
                            Directory.Delete(path, true);
                            DispatcherQueue.TryEnqueue(() => SystemDiagnosticsLogTextBox.Text += $"[FIX] Папка кэша удалена: {folder}\n");
                        }
                        catch (Exception ex) {
                            DispatcherQueue.TryEnqueue(() => SystemDiagnosticsLogTextBox.Text += $"[WARN] Не удалось удалить {folder}: {ex.Message}\n");
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => SystemDiagnosticsLogTextBox.Text += $"[ERROR] Сбой очистки кэша: {ex.Message}\n");
                return false;
            }
        });

        SystemDiagnosticsLogTextBox.Text += success ? "[INFO] Кэш Discord успешно очищен.\n" : "[WARN] Очистка кэша завершена с ошибками.\n";
        ClearDiscordCacheBtn.IsEnabled = true;
    }
}

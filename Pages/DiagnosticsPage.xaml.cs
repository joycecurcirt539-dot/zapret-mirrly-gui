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
    private Process? _powershellProcess;
    private readonly ObservableCollection<StrategyScoreItem> _strategyScores = new();
    private string _bestPresetFound = "";
    private readonly List<string> _allPresets = new();
    private readonly List<string> _selectedPresets = new();
    private readonly StringBuilder _logBuffer = new();
    private readonly List<(string Text, bool IsError)> _pendingLogLines = new();
    private DateTime _lastLogFlush = DateTime.MinValue;
    private bool _isLogFlushScheduled = false;

    public DiagnosticsPage()
    {
        InitializeComponent();
        
        StrategyScoresListView.ItemsSource = _strategyScores;
        Loaded += DiagnosticsPage_Loaded;
        Unloaded += DiagnosticsPage_Unloaded;
    }

    private void DiagnosticsPage_Loaded(object sender, RoutedEventArgs e)
    {
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
            SelectConfigsButton.Content = $"Выбрать ({_selectedPresets.Count})...";
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
        string runMode = (RunModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        string timeout = (TimeoutComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "5";

        // Determine selected config indices (1-indexed based on _allPresets order)
        var indicesList = new List<int>();
        if (runMode == "select")
        {
            foreach (var sel in _selectedPresets)
            {
                int idx = _allPresets.FindIndex(p => p.Equals(sel, StringComparison.OrdinalIgnoreCase));
                if (idx != -1)
                {
                    indicesList.Add(idx + 1); // 1-indexed
                }
            }
        }

        Task.Run(() => RunPowerShellDiagnostics(testType, runMode, timeout, indicesList));
    }

    private void RunPowerShellDiagnostics(string testType, string runMode, string timeout, List<int> selectedIndices)
    {
        var root = ZapretService.FindZapretRoot();
        var scriptPath = Path.Combine(root, "utils", "test zapret.ps1");

        if (!File.Exists(scriptPath))
        {
            AppendToConsole($"[ERROR] Скрипт диагностики не найден по пути: {scriptPath}\n");
            ResetUIState("Ошибка: скрипт не найден");
            return;
        }

        AppendToConsole($"[INFO] Запуск оригинального скрипта диагностики...\n");

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            WorkingDirectory = Path.Combine(root, "utils"),
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Pass timeout and GUI mode via Environment Variables
        startInfo.EnvironmentVariables["MONITOR_TIMEOUT"] = timeout;
        startInfo.EnvironmentVariables["GUI_MODE"] = "1";
        startInfo.EnvironmentVariables["TEST_TYPE"] = testType;
        startInfo.EnvironmentVariables["RUN_MODE"] = runMode;
        if (runMode == "select")
        {
            var indicesStr = string.Join(",", selectedIndices);
            if (string.IsNullOrEmpty(indicesStr)) indicesStr = "0"; 
            startInfo.EnvironmentVariables["SELECTED_INDICES"] = indicesStr;
        }

        try
        {
            _powershellProcess = new Process { StartInfo = startInfo };
            
            _powershellProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    var translated = TranslatePowerShellLine(e.Data);
                    AppendToConsole(translated, isError: false);
                    ParseStdoutLine(e.Data);
                }
            };
            
            _powershellProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    var translated = TranslatePowerShellLine(e.Data);
                    AppendToConsole($"[Системная ошибка] {translated}", isError: true);
                }
            };

            _powershellProcess.Start();
            _powershellProcess.BeginOutputReadLine();
            _powershellProcess.BeginErrorReadLine();

            _powershellProcess.WaitForExit();
            
            var exitCode = _powershellProcess.ExitCode;
            ResetUIState(exitCode == 0 ? "Проверка завершена" : "Проверка прервана");
        }
        catch (Exception ex)
        {
            AppendToConsole($"[ERROR] Ошибка запуска процесса: {ex.Message}\n");
            ResetUIState("Ошибка запуска");
        }
    }

    private readonly Regex _scoreRegex = new(@"^(?<config>general.*\.bat)\s*:\s*(?:HTTP\s*)?OK:\s*(?<ok>\d+),\s*(?:ERR|FAIL):\s*(?<fail>\d+),\s*UNSUP:\s*(?<unsup>\d+)", RegexOptions.IgnoreCase);
    private readonly Regex _dpiScoreRegex = new(@"^(?<config>general.*\.bat)\s*:\s*OK:\s*(?<ok>\d+),\s*FAIL:\s*(?<fail>\d+),\s*UNSUP:\s*(?<unsup>\d+),\s*BLOCKED:\s*(?<blocked>\d+)", RegexOptions.IgnoreCase);
    private readonly Regex _bestRegex = new(@"Best strategy:\s*(?<best>general.*)", RegexOptions.IgnoreCase);

    private void ParseStdoutLine(string line)
    {
        // 1. Check Standard Results
        var match = _scoreRegex.Match(line);
        if (match.Success)
        {
            var config = match.Groups["config"].Value.Trim();
            var ok = int.Parse(match.Groups["ok"].Value);
            var fail = int.Parse(match.Groups["fail"].Value);
            var unsup = int.Parse(match.Groups["unsup"].Value);

            AddStrategyScore(config, ok, fail, unsup, 0, false);
            return;
        }

        // 2. Check DPI Results
        var dpiMatch = _dpiScoreRegex.Match(line);
        if (dpiMatch.Success)
        {
            var config = dpiMatch.Groups["config"].Value.Trim();
            var ok = int.Parse(dpiMatch.Groups["ok"].Value);
            var fail = int.Parse(dpiMatch.Groups["fail"].Value);
            var unsup = int.Parse(dpiMatch.Groups["unsup"].Value);
            var blocked = int.Parse(dpiMatch.Groups["blocked"].Value);

            AddStrategyScore(config, ok, fail, unsup, blocked, true);
            return;
        }

        // 3. Check Best Strategy
        var bestMatch = _bestRegex.Match(line);
        if (bestMatch.Success)
        {
            var best = bestMatch.Groups["best"].Value.Trim();
            // Append .bat extension if missing
            if (!best.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                best += ".bat";
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                _bestPresetFound = best;
                BestStrategyTextBlock.Text = best;
                BestStrategyCard.Visibility = Visibility.Visible;
            });

            // Automatically stop PowerShell since results are declared
            StopDiagnosticsProcess();
        }
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
                    currentItem.RankColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 215, 0)); // Gold
                    currentItem.RankGlyph = "👑";
                }
                else if (i == 1)
                {
                    currentItem.RankColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 192, 192, 192)); // Silver
                    currentItem.RankGlyph = "🥈";
                }
                else if (i == 2)
                {
                    currentItem.RankColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 205, 127, 50)); // Bronze
                    currentItem.RankGlyph = "🥉";
                }
                else
                {
                    currentItem.RankColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128)); // Gray
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

    private string TranslatePowerShellLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;

        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Run as Administrator to execute tests", "Запустите от имени Администратора для выполнения тестов" },
            { "curl.exe found", "Файл curl.exe найден" },
            { "Current ipset status:", "Текущий статус ipset:" },
            { "Ipset will be switched to 'any' for accurate DPI tests.", "База ipset будет переключена на 'any' для точных тестов DPI." },
            { "If you close the window with the X button, ipset will NOT restore immediately.", "Если вы закроете окно кнопкой X, ipset НЕ восстановится мгновенно." },
            { "It will be restored automatically on the next script run.", "Он будет автоматически восстановлен при следующем запуске." },
            { "Fix the errors above and rerun.", "Исправьте ошибки выше и запустите снова." },
            { "Press any key to exit...", "Нажмите любую клавишу для выхода..." },
            { "Script interrupted. Restoring ipset...", "Скрипт прерван. Восстановление исходного состояния ipset..." },
            { "Best strategy:", "Лучшая стратегия:" },
            { "Checking strategy", "Проверка стратегии" },
            { "unsupported", "не поддерживается" },
            { "blocked", "заблокировано" },
            { "available", "доступно" },
            { "none", "отсутствует" }
        };

        foreach (var pair in translations)
        {
            line = System.Text.RegularExpressions.Regex.Replace(line, System.Text.RegularExpressions.Regex.Escape(pair.Key), pair.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        line = System.Text.RegularExpressions.Regex.Replace(line, @"Checking strategy\s+(?<name>general.*\.bat)\s*\.\.\.", "Проверка стратегии ${name}...", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        line = System.Text.RegularExpressions.Regex.Replace(line, @"Testing:\s+(?<name>general.*\.bat)\s*\((?<sec>\d+)s timeout\)\s*\.\.\.", "Тестирование: ${name} (таймаут ${sec} сек)...", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return line;
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
        if (_powershellProcess != null)
        {
            try
            {
                if (!_powershellProcess.HasExited)
                {
                    _powershellProcess.Kill(true);
                }
            }
            catch { }
            finally
            {
                _powershellProcess.Dispose();
                _powershellProcess = null;
            }
        }
        
        ZapretService.KillAllWinwsProcesses();
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

        // Install service
        ZapretService.InstallService(_bestPresetFound, mode);

        var infoDialog = new ContentDialog
        {
            Title = "Успех",
            Content = $"Пресет '{_bestPresetFound}' успешно установлен и запущен как служба автозапуска Windows.",
            CloseButtonText = "ОК",
            XamlRoot = this.XamlRoot
        };
        _ = infoDialog.ShowAsync();
    }

    // ─── System Diagnostics & Parity Actions ──────────────────────────────────

    private async void RunSystemDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        RunSystemDiagnosticsBtn.IsEnabled = false;
        FixSystemConflictsBtn.IsEnabled = false;
        ClearDiscordCacheBtn.IsEnabled = false;
        SystemDiagnosticsLogTextBox.Text = "Запуск системной проверки...\n";

        // 1. BFE Check
        var bfeRunning = await Task.Run(() => {
            var output = ZapretService.ExecuteCommand("sc.exe", "query BFE");
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        });
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

        // 3. TCP Timestamps Check
        var timestampsActive = await Task.Run(() => {
            var output = ZapretService.ExecuteCommand("netsh.exe", "interface tcp show global");
            return output.Contains("timestamps", StringComparison.OrdinalIgnoreCase) && 
                   output.Contains("enabled", StringComparison.OrdinalIgnoreCase);
        });
        UpdateStatusCard(TimestampsStatusIcon, TimestampsStatusDetails, timestampsActive, "TCP timestamps включены", "TCP timestamps отключены! Рекомендуется включить.");
        LogDiag(timestampsActive ? "[PASS] TCP Timestamps включены." : "[WARN] TCP Timestamps отключены.");

        // 4. Conflicts Check
        var conflictingServices = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2", "SmartByte", "EPWD", "TracSrvWrapper" };
        var foundConflicts = new List<string>();
        await Task.Run(() => {
            foreach (var svc in conflictingServices)
            {
                var output = ZapretService.ExecuteCommand("sc.exe", $"query {svc}");
                if (!output.Contains("FAILED 1060") && !output.Contains("не установлена") && !output.Contains("не существует"))
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

        // 8. WinDivert Check
        var divertConflict = await Task.Run(() => {
            var output = ZapretService.ExecuteCommand("sc.exe", "query WinDivert");
            var output14 = ZapretService.ExecuteCommand("sc.exe", "query WinDivert14");
            bool divertRunning = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) || 
                                 output14.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            // If winws is not running but windivert is, it is a conflict
            return divertRunning && !ZapretService.IsRunning;
        });
        UpdateStatusCard(DivertStatusIcon, DivertStatusDetails, !divertConflict, "Драйвер WinDivert чист и готов к работе", "Обнаружена зависшая служба драйвера WinDivert!");
        LogDiag(!divertConflict ? "[PASS] Драйвер WinDivert в порядке." : "[FAIL] Обнаружен зависший драйвер WinDivert без активного процесса обхода.");

        SystemDiagnosticsLogTextBox.Text += "\nСистемная проверка завершена.\n";
        
        RunSystemDiagnosticsBtn.IsEnabled = true;
        FixSystemConflictsBtn.IsEnabled = true;
        ClearDiscordCacheBtn.IsEnabled = true;
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
        SystemDiagnosticsLogTextBox.Text += "\nУстранение конфликтов служб...\n";

        await Task.Run(() => {
            var conflictingServices = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2", "SmartByte", "EPWD", "TracSrvWrapper" };
            foreach (var svc in conflictingServices)
            {
                var check = ZapretService.ExecuteCommand("sc.exe", $"query {svc}");
                if (!check.Contains("FAILED 1060") && !check.Contains("не установлена") && !check.Contains("не существует"))
                {
                    DispatcherQueue.TryEnqueue(() => SystemDiagnosticsLogTextBox.Text += $"[FIX] Удаление конфликтующей службы: {svc}...\n");
                    ZapretService.ExecuteCommand("sc.exe", $"stop {svc}");
                    ZapretService.ExecuteCommand("sc.exe", $"delete {svc}");
                }
            }

            // Cleanup WinDivert driver if process not running
            if (!ZapretService.IsRunning)
            {
                DispatcherQueue.TryEnqueue(() => SystemDiagnosticsLogTextBox.Text += "[FIX] Удаление зависшей службы драйвера WinDivert...\n");
                ZapretService.ExecuteCommand("sc.exe", "stop WinDivert");
                ZapretService.ExecuteCommand("sc.exe", "delete WinDivert");
                ZapretService.ExecuteCommand("sc.exe", "stop WinDivert14");
                ZapretService.ExecuteCommand("sc.exe", "delete WinDivert14");
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

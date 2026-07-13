using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class LogsPage : Page
{
    // Capped static log line history to survive page navigation
    private static readonly List<string> _allLogLines = new();
    private const int MaxLogLinesCount = 5000;
    
    private bool _isLoaded = false;

    public LogsPage()
    {
        InitializeComponent();
        Loaded += LogsPage_Loaded;
        Unloaded += LogsPage_Unloaded;
    }

    private void LogsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;

        lock (_allLogLines)
        {
            _allLogLines.Clear();
            _allLogLines.AddRange(ZapretService.GetLogHistory());
        }

        ApplyFilters();
        
        // Subscribe to real-time logs
        ZapretService.OnLogReceived += OnZapretLogReceived;
    }

    private void LogsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        ZapretService.OnLogReceived -= OnZapretLogReceived;
    }

    private void OnZapretLogReceived(string log)
    {
        // Enqueue to list
        lock (_allLogLines)
        {
            _allLogLines.Add(log);
            if (_allLogLines.Count > MaxLogLinesCount)
            {
                _allLogLines.RemoveRange(0, _allLogLines.Count - MaxLogLinesCount + 500); // Batch remove oldest 500
            }
        }

        if (!_isLoaded) return;

        // Render log in UI
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsLogPassingFilters(log))
            {
                var paragraph = new Paragraph();
                var run = new Run { Text = log };

                // 1. Red - Error / Fail
                bool isErrorLine = log.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                                   log.Contains("[winws ERROR]", StringComparison.OrdinalIgnoreCase) ||
                                   log.Contains("[SERVICE ERROR]", StringComparison.OrdinalIgnoreCase) ||
                                   log.Contains("[Системная ошибка]", StringComparison.OrdinalIgnoreCase) ||
                                   log.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                                   log.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
                                   log.Contains("ERR", StringComparison.Ordinal) ||
                                   log.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                                   log.Contains("blocked", StringComparison.OrdinalIgnoreCase);

                // 2. Orange - Warning / Timeout
                bool isWarningLine = log.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("leftover", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("skipped", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("Внимание", StringComparison.OrdinalIgnoreCase);

                // 3. Green - Success / OK
                bool isSuccessLine = log.Contains("OK", StringComparison.Ordinal) ||
                                     log.Contains("available", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("успешно", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("найден", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("found", StringComparison.OrdinalIgnoreCase) ||
                                     log.Contains("detected", StringComparison.OrdinalIgnoreCase);

                // 4. Blue - Operations / Cmd
                bool isActionLine = log.Contains("[CMD]", StringComparison.OrdinalIgnoreCase) ||
                                    log.Contains("[SERVICE]", StringComparison.OrdinalIgnoreCase) ||
                                    log.Contains("Checking strategy", StringComparison.OrdinalIgnoreCase) ||
                                    log.Contains("Testing:", StringComparison.OrdinalIgnoreCase) ||
                                    log.Contains("Запуск", StringComparison.OrdinalIgnoreCase) ||
                                    log.Contains("Остановка", StringComparison.OrdinalIgnoreCase) ||
                                    log.Contains("Удаление", StringComparison.OrdinalIgnoreCase) ||
                                    log.Contains("Восстановление", StringComparison.OrdinalIgnoreCase);

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
                LogRichTextBlock.Blocks.Add(paragraph);

                // Keep only last 1000 blocks to prevent memory/UI lag
                while (LogRichTextBlock.Blocks.Count > 1000)
                {
                    LogRichTextBlock.Blocks.RemoveAt(0);
                }

                ScrollLogsRichTextBlockToBottom();
            }
        });
    }

    private void ScrollLogsRichTextBlockToBottom()
    {
        if (LogScrollViewer != null)
        {
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null, false);
        }
    }

    private void FilterInputs_Changed(object sender, object e)
    {
        if (!_isLoaded) return;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        string searchQuery = SearchTextBox.Text.Trim();
        string filterTag = (LevelFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        var filteredLines = new List<string>();
        lock (_allLogLines)
        {
            foreach (var line in _allLogLines)
            {
                if (IsLogPassingFilters(line, searchQuery, filterTag))
                {
                    filteredLines.Add(line);
                }
            }
        }

        // Limit to last 1000 lines to prevent UI freeze
        var displayLines = filteredLines.Skip(Math.Max(0, filteredLines.Count - 1000)).ToList();

        LogRichTextBlock.Blocks.Clear();
        foreach (var line in displayLines)
        {
            var paragraph = new Paragraph();
            var run = new Run { Text = line };

            // 1. Red - Error / Fail
            bool isErrorLine = line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("[winws ERROR]", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("[SERVICE ERROR]", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("[Системная ошибка]", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("ERR", StringComparison.Ordinal) ||
                               line.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("blocked", StringComparison.OrdinalIgnoreCase);

            // 2. Orange - Warning / Timeout
            bool isWarningLine = line.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("leftover", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("skipped", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("Внимание", StringComparison.OrdinalIgnoreCase);

            // 3. Green - Success / OK
            bool isSuccessLine = line.Contains("OK", StringComparison.Ordinal) ||
                                 line.Contains("available", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("успешно", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("найден", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("found", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("detected", StringComparison.OrdinalIgnoreCase);

            // 4. Blue - Operations / Cmd
            bool isActionLine = line.Contains("[CMD]", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("[SERVICE]", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Checking strategy", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Testing:", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Запуск", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Остановка", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Удаление", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Восстановление", StringComparison.OrdinalIgnoreCase);

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
            LogRichTextBlock.Blocks.Add(paragraph);
        }

        ScrollLogsRichTextBlockToBottom();
    }

    private string GetFilteredText()
    {
        string searchQuery = SearchTextBox.Text.Trim();
        string filterTag = (LevelFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        var filteredLines = new List<string>();
        lock (_allLogLines)
        {
            foreach (var line in _allLogLines)
            {
                if (IsLogPassingFilters(line, searchQuery, filterTag))
                {
                    filteredLines.Add(line);
                }
            }
        }
        return string.Join(Environment.NewLine, filteredLines);
    }

    private bool IsLogPassingFilters(string logLine, string? query = null, string? filterTag = null)
    {
        query ??= SearchTextBox.Text.Trim();
        filterTag ??= (LevelFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        // 1. Text Search Filter
        if (!string.IsNullOrEmpty(query) && logLine.IndexOf(query, StringComparison.OrdinalIgnoreCase) == -1)
        {
            return false;
        }

        // 2. Level Filter tag
        if (filterTag == "errors")
        {
            return logLine.Contains("[winws ERROR]", StringComparison.OrdinalIgnoreCase) ||
                   logLine.Contains("[SERVICE ERROR]", StringComparison.OrdinalIgnoreCase) ||
                   logLine.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                   logLine.Contains("ошибка", StringComparison.OrdinalIgnoreCase);
        }
        if (filterTag == "service")
        {
            return logLine.Contains("[SERVICE]", StringComparison.OrdinalIgnoreCase) ||
                   logLine.Contains("[CMD]", StringComparison.OrdinalIgnoreCase);
        }
        if (filterTag == "winws")
        {
            // Raw winws outputs don't start with service control prefixes
            return !logLine.Contains("[SERVICE]", StringComparison.OrdinalIgnoreCase) &&
                   !logLine.Contains("[CMD]", StringComparison.OrdinalIgnoreCase);
        }

        return true; // "all"
    }

    private void CopyLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var text = GetFilteredText();
        if (string.IsNullOrEmpty(text)) return;

        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        lock (_allLogLines)
        {
            _allLogLines.Clear();
        }
        LogRichTextBlock.Blocks.Clear();
    }

    private void OpenInNotepadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = GetFilteredText();
            var tempPath = Path.Combine(Path.GetTempPath(), "zapret_mirrly_logs.txt");
            File.WriteAllText(tempPath, text, Encoding.UTF8);
            System.Diagnostics.Process.Start("notepad.exe", tempPath);
        }
        catch { }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = GetFilteredText();
            var root = ZapretService.FindZapretRoot();
            var exportPath = Path.Combine(root, "logs_export.txt");
            File.WriteAllText(exportPath, text, Encoding.UTF8);
            
            // Open explorer and select the file
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{exportPath}\"");
        }
        catch { }
    }
}

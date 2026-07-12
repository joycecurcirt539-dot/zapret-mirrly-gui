using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
                LogTextBox.Text += log + Environment.NewLine;
                
                // Keep cursor at bottom
                LogTextBox.SelectionStart = LogTextBox.Text.Length;
                LogTextBox.SelectionLength = 0;
            }
        });
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

        LogTextBox.Text = string.Join(Environment.NewLine, filteredLines) + Environment.NewLine;
        LogTextBox.SelectionStart = LogTextBox.Text.Length;
        LogTextBox.SelectionLength = 0;
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
        if (string.IsNullOrEmpty(LogTextBox.Text)) return;

        var dataPackage = new DataPackage();
        dataPackage.SetText(LogTextBox.Text);
        Clipboard.SetContent(dataPackage);
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        lock (_allLogLines)
        {
            _allLogLines.Clear();
        }
        LogTextBox.Text = "";
    }

    private void OpenInNotepadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "zapret_mirrly_logs.txt");
            File.WriteAllText(tempPath, LogTextBox.Text, Encoding.UTF8);
            System.Diagnostics.Process.Start("notepad.exe", tempPath);
        }
        catch { }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = ZapretService.FindZapretRoot();
            var exportPath = Path.Combine(root, "logs_export.txt");
            File.WriteAllText(exportPath, LogTextBox.Text, Encoding.UTF8);
            
            // Open explorer and select the file
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{exportPath}\"");
        }
        catch { }
    }
}

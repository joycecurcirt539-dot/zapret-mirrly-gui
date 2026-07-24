using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretMirrlyGUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ZapretMirrlyGUI.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    private static readonly List<string> _allLogLines = new();
    private const int MaxLogLinesCount = 5000;

    [ObservableProperty]
    private string _filterQueryText = "";

    [ObservableProperty]
    private int _selectedFilterIndex = 0;

    [ObservableProperty]
    private string _logText = "";

    public void OnLoaded()
    {
        lock (_allLogLines)
        {
            _allLogLines.Clear();
            _allLogLines.AddRange(ZapretService.GetLogHistory());
        }

        ApplyFilters();
        ZapretService.OnLogReceived += OnZapretLogReceived;
    }

    public void OnUnloaded()
    {
        ZapretService.OnLogReceived -= OnZapretLogReceived;
    }

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    private void OnZapretLogReceived(string log)
    {
        lock (_allLogLines)
        {
            _allLogLines.Add(log);
            if (_allLogLines.Count > MaxLogLinesCount)
            {
                _allLogLines.RemoveRange(0, _allLogLines.Count - MaxLogLinesCount + 500);
            }
        }

        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ApplyFilters());
        }
        else
        {
            ApplyFilters();
        }
    }

    partial void OnFilterQueryTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedFilterIndexChanged(int value)
    {
        ApplyFilters();
    }

    public void ApplyFilters()
    {
        var sb = new StringBuilder();
        lock (_allLogLines)
        {
            foreach (var log in _allLogLines)
            {
                if (IsLogPassingFilters(log))
                {
                    sb.AppendLine(log);
                }
            }
        }
        LogText = sb.ToString();
    }

    private bool IsLogPassingFilters(string log)
    {
        if (!string.IsNullOrWhiteSpace(FilterQueryText))
        {
            if (!log.Contains(FilterQueryText, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return SelectedFilterIndex switch
        {
            1 => log.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || log.Contains("FAIL", StringComparison.OrdinalIgnoreCase),
            2 => log.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase) || log.Contains("[WARN]", StringComparison.OrdinalIgnoreCase),
            3 => log.Contains("OK", StringComparison.Ordinal) || log.Contains("success", StringComparison.OrdinalIgnoreCase),
            4 => log.Contains("[winws]", StringComparison.OrdinalIgnoreCase),
            5 => log.Contains("[TgWsProxy]", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    [RelayCommand]
    private void CopyLogs()
    {
        var package = new DataPackage();
        package.SetText(LogText);
        Clipboard.SetContent(package);
    }

    [RelayCommand]
    private void ClearLogs()
    {
        lock (_allLogLines)
        {
            _allLogLines.Clear();
        }
        LogText = "";
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretMirrlyGUI.Services;
using ZapretMirrlyGUI.Pages;

public class ListMetadataItem
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconData { get; set; } = "";
    public string DefaultValue { get; set; } = "";
}

public partial class ListsViewModel : ObservableObject
{
    private readonly List<ListMetadataItem> _listMetadata = new();
    private readonly Dictionary<string, string> _bufferedContent = new();
    private readonly Dictionary<string, string> _originalContent = new();

    [ObservableProperty]
    private ListMetadataItem? _activeItem;

    [ObservableProperty]
    private string _activeFileName = "";

    [ObservableProperty]
    private string _activeDescription = "";

    [ObservableProperty]
    private string _editorText = "";

    [ObservableProperty]
    private string _statusInfo = "Загрузка списков...";

    [ObservableProperty]
    private bool _isModified = false;

    public List<ListMetadataItem> ListMetadata => _listMetadata;

    public ListsViewModel()
    {
        InitializeListMetadata();
        LoadAllFilesFromDisk();

        if (_listMetadata.Count > 0)
        {
            ActiveItem = _listMetadata[0];
        }
    }

    private void InitializeListMetadata()
    {
        _listMetadata.Add(new ListMetadataItem
        {
            Id = "system_bypass",
            DisplayName = "Обход (Системный)",
            FileName = "list-general.txt",
            Description = "Общий системный список доменов для обхода блокировок и замедлений (системный).",
            IconData = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zM11 19.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z",
            DefaultValue = "youtube.com\ngoogle.com"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "google_bypass",
            DisplayName = "Google/YouTube",
            FileName = "list-google.txt",
            Description = "Системный список доменов Google, YouTube и связанных с ними CDN служб (CDN, видеохостинг).",
            IconData = "M10 15l5.19-3L10 9v6m11.56-7.83c.13.47.22 1.1.28 1.9.07.8.1 1.49.1 2.09L22 12c0 2.19-.16 3.8-.44 4.83-.25.9-.83 1.48-1.73 1.73-.47.13-1.33.22-2.65.28-1.3.07-2.49.1-3.59.1L12 19c-4.19 0-6.8-.16-7.83-.44-.9-.25-1.48-.83-1.73-1.73-.13-.47-.22-1.1-.28-1.9-.07-.8-.1-1.49-.1-2.09L2 12c0-2.19.16-3.8.44-4.83.25-.9.83-1.48 1.73-1.73.47-.13 1.33-.22 2.65-.28 1.3-.07 2.49-.1 3.59-.1L12 5c4.19 0 6.8.16 7.83.44.9.25 1.48.83 1.73 1.73z",
            DefaultValue = "youtube.com\ngooglevideo.com\nytimg.com"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "system_exclude",
            DisplayName = "Исключения (Системный)",
            FileName = "list-exclude.txt",
            Description = "Системный список исключений. Трафик к этим доменам всегда идет напрямую без модификации DPI.",
            IconData = "M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm5 11H7v-2h10v2z",
            DefaultValue = "gosuslugi.ru\nnalog.ru"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "ipset_all",
            DisplayName = "IPSet-список",
            FileName = "ipset-all.txt",
            Description = "Список IP-адресов или подсетей CIDR для блокировок по IP (используется при включенном режиме IPSet).",
            IconData = "M2 20h20v-4H2v4zm2-3h2v2H4v-2zM2 4v4h20V4H2zm4 3H4V5h2v2zm-4 7h20v-4H2v4zm2-3h2v2H4v-2z",
            DefaultValue = "203.0.113.113/32"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "system_ipset_exclude",
            DisplayName = "IP-исключения (Системный)",
            FileName = "ipset-exclude.txt",
            Description = "Системные исключения подсетей IP. Этот трафик всегда пускается напрямую.",
            IconData = "M22 13h-6v-2h6v2zm-2-9v4H2V4h18zm-2 3V5H4v2h14zm-16 7h10v-4H2v4zm2-3h2v2H4v-2zm-2 9h20v-4H2v4zm2-3h2v2H4v-2z",
            DefaultValue = "192.168.0.0/16\n10.0.0.0/8"
        });
    }

    private void LoadAllFilesFromDisk()
    {
        var listsDir = ZapretService.FindListsDirectory();
        Directory.CreateDirectory(listsDir);

        foreach (var item in _listMetadata)
        {
            var filePath = Path.Combine(listsDir, item.FileName);
            string content = "";

            try
            {
                if (File.Exists(filePath))
                {
                    content = File.ReadAllText(filePath, Encoding.UTF8);
                }
                else
                {
                    content = item.DefaultValue + Environment.NewLine;
                    File.WriteAllText(filePath, content, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                content = $"Ошибка загрузки файла:\n{ex.Message}";
            }

            content = content.Replace("\r\n", "\n");
            _originalContent[item.FileName] = content;
            _bufferedContent[item.FileName] = content;
        }
    }

    partial void OnActiveItemChanged(ListMetadataItem? value)
    {
        if (value == null) return;

        ActiveFileName = value.FileName;
        ActiveDescription = value.Description;

        if (_bufferedContent.TryGetValue(value.FileName, out var content))
        {
            EditorText = content;
        }
        else
        {
            EditorText = "";
        }

        UpdateStatusInfo();
    }

    partial void OnEditorTextChanged(string value)
    {
        if (ActiveItem == null) return;

        string normalized = value.Replace("\r\n", "\n");
        _bufferedContent[ActiveItem.FileName] = normalized;

        string original = _originalContent.GetValueOrDefault(ActiveItem.FileName, "");
        IsModified = normalized != original;

        UpdateStatusInfo();
    }

    private void UpdateStatusInfo()
    {
        if (ActiveItem == null) return;

        var lines = EditorText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int lineCount = lines.Length;

        var listsDir = ZapretService.FindListsDirectory();
        var filePath = Path.Combine(listsDir, ActiveItem.FileName);

        long sizeKb = 0;
        if (File.Exists(filePath))
        {
            sizeKb = new FileInfo(filePath).Length / 1024;
        }

        StatusInfo = $"Записей: {lineCount} | Размер: {sizeKb} КБ {(IsModified ? "• [Не сохранено]" : "• [Сохранено]")}";
    }

    [RelayCommand]
    private void SaveCurrentFile()
    {
        if (ActiveItem == null) return;

        var listsDir = ZapretService.FindListsDirectory();
        var filePath = Path.Combine(listsDir, ActiveItem.FileName);

        try
        {
            string normalized = EditorText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            File.WriteAllText(filePath, normalized, Encoding.UTF8);

            _originalContent[ActiveItem.FileName] = EditorText.Replace("\r\n", "\n");
            IsModified = false;
            UpdateStatusInfo();
        }
        catch (Exception ex)
        {
            StatusInfo = $"Ошибка сохранения: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetCurrentFile()
    {
        if (ActiveItem == null) return;

        if (_originalContent.TryGetValue(ActiveItem.FileName, out var original))
        {
            EditorText = original;
            IsModified = false;
            UpdateStatusInfo();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.Pages;

public class ListMetadataItem
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconGlyph { get; set; } = "";
    public string DefaultValue { get; set; } = "";
}

public sealed partial class ListsPage : Page
{
    private readonly List<ListMetadataItem> _listMetadata = new();
    private readonly Dictionary<string, string> _bufferedContent = new();
    private readonly Dictionary<string, string> _originalContent = new();
    
    private ListMetadataItem? _activeItem = null;
    private bool _isUpdatingFromSidebar = false;
    private int _lastSearchIndex = 0;

    public ListsPage()
    {
        InitializeComponent();
        Loaded += ListsPage_Loaded;
    }

    private void ListsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_listMetadata.Count > 0) return; // Already loaded

        InitializeListMetadata();
        LoadAllFilesFromDisk();

        // Bind and select first item
        ListsSidebarListView.ItemsSource = _listMetadata;
        ListsSidebarListView.SelectedIndex = 0;
    }

    private void InitializeListMetadata()
    {
        _listMetadata.Add(new ListMetadataItem
        {
            Id = "system_bypass",
            DisplayName = "Обход (Системный)",
            FileName = "list-general.txt",
            Description = "Общий системный список доменов для обхода блокировок и замедлений (системный).",
            IconGlyph = "",
            DefaultValue = "youtube.com\ngoogle.com"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "user_bypass",
            DisplayName = "Обход (Пользователь)",
            FileName = "list-general-user.txt",
            Description = "Ваш личный список доменов для обхода. Добавляйте сюда сайты, которые хотите разблокировать.",
            IconGlyph = "",
            DefaultValue = "domain.example.abc"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "google_bypass",
            DisplayName = "Google/YouTube",
            FileName = "list-google.txt",
            Description = "Системный список доменов Google, YouTube и связанных с ними CDN служб (CDN, видеохостинг).",
            IconGlyph = "",
            DefaultValue = "youtube.com\ngooglevideo.com\nytimg.com"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "system_exclude",
            DisplayName = "Исключения (Системный)",
            FileName = "list-exclude.txt",
            Description = "Системный список исключений. Трафик к этим доменам всегда идет напрямую без модификации DPI.",
            IconGlyph = "",
            DefaultValue = "gosuslugi.ru\nnalog.ru"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "user_exclude",
            DisplayName = "Исключения (Пользователь)",
            FileName = "list-exclude-user.txt",
            Description = "Ваши личные домены-исключения. Внесите сюда сайты банков или госструктур, если они работают нестабильно.",
            IconGlyph = "",
            DefaultValue = "domain.example.abc"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "ipset_all",
            DisplayName = "IPSet-список",
            FileName = "ipset-all.txt",
            Description = "Список IP-адресов или подсетей CIDR для блокировок по IP (используется при включенном режиме IPSet).",
            IconGlyph = "",
            DefaultValue = "203.0.113.113/32"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "system_ipset_exclude",
            DisplayName = "IP-исключения (Системный)",
            FileName = "ipset-exclude.txt",
            Description = "Системные исключения подсетей IP. Этот трафик всегда пускается напрямую.",
            IconGlyph = "",
            DefaultValue = "192.168.0.0/16\n10.0.0.0/8"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "user_ipset_exclude",
            DisplayName = "IP-исключения (Пользователь)",
            FileName = "ipset-exclude-user.txt",
            Description = "Ваши личные IP-исключения подсетей. Защищает доверенные локальные или внешние IP от перехвата.",
            IconGlyph = "",
            DefaultValue = "203.0.113.113/32"
        });
    }

    private void LoadAllFilesFromDisk()
    {
        var root = ZapretService.FindZapretRoot();
        var listsDir = Path.Combine(root, "lists");
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

            // Normalize line endings so comparison with TextBox.Text doesn't produce false positives
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");

            _originalContent[item.Id] = content;
            _bufferedContent[item.Id] = content;
        }
    }

    private void ListsSidebarListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListsSidebarListView.SelectedItem is not ListMetadataItem selectedItem) return;

        // 1. Buffer current editor changes before switching
        if (_activeItem != null && !_isUpdatingFromSidebar)
        {
            _bufferedContent[_activeItem.Id] = ListEditorTextBox.Text;
        }

        // 2. Switch active item
        _isUpdatingFromSidebar = true;
        try
        {
            _activeItem = selectedItem;
            ActiveListTitleTextBlock.Text = selectedItem.DisplayName;
            ActiveListSubtitleTextBlock.Text = selectedItem.Description;
            ListEditorTextBox.Text = _bufferedContent[selectedItem.Id];
            
            // Reset search parameters
            SearchBox.Text = "";
            _lastSearchIndex = 0;

            UpdateSaveStatus();
        }
        finally
        {
            _isUpdatingFromSidebar = false;
        }
    }

    private void ListEditorTextBox_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingFromSidebar || _activeItem == null) return;

        _bufferedContent[_activeItem.Id] = ListEditorTextBox.Text;
        UpdateSaveStatus();
    }

    private void UpdateSaveStatus()
    {
        if (_activeItem == null) return;

        // Normalize line endings before comparing to avoid false "unsaved" from TextBox internals
        static string Norm(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

        bool isModified = Norm(_bufferedContent[_activeItem.Id]) != Norm(_originalContent[_activeItem.Id]);
        if (isModified)
        {
            ListSaveStatusTextBlock.Text = "⚠️ Есть несохранённые изменения";
            ListSaveStatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
        }
        else
        {
            ListSaveStatusTextBlock.Text = "";
            ListSaveStatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 100, 100));
        }
    }


    private void SortAlphabeticallyButton_Click(object sender, RoutedEventArgs e)
    {
        string text = ListEditorTextBox.Text;

        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).ToList();

        var comments = lines.Where(l => l.TrimStart().StartsWith("#")).ToList();
        var domains  = lines.Where(l => !l.TrimStart().StartsWith("#") && !string.IsNullOrWhiteSpace(l)).ToList();

        domains.Sort(StringComparer.OrdinalIgnoreCase);

        // Use \n so the result matches normalized originals — avoids false "unsaved" after sort
        var parts = new List<string>();
        parts.AddRange(comments);
        if (comments.Count > 0 && domains.Count > 0)
            parts.Add("");
        parts.AddRange(domains);

        ListEditorTextBox.Text = string.Join("\n", parts);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string query = sender.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        string text = ListEditorTextBox.Text;
        int index = text.IndexOf(query, _lastSearchIndex, StringComparison.OrdinalIgnoreCase);
        
        if (index == -1) // Wrap search to beginning
        {
            index = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
        }

        if (index != -1)
        {
            ListEditorTextBox.Focus(FocusState.Programmatic);
            ListEditorTextBox.Select(index, query.Length);
            _lastSearchIndex = index + query.Length;
        }
        else
        {
            // Reset index if not found
            _lastSearchIndex = 0;
        }
    }

    private void RevertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeItem == null) return;

        ListEditorTextBox.Text = _originalContent[_activeItem.Id];
        _bufferedContent[_activeItem.Id] = _originalContent[_activeItem.Id];
        UpdateSaveStatus();
    }

    private void SaveChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeItem == null) return;

        var root = ZapretService.FindZapretRoot();
        var filePath = Path.Combine(root, "lists", _activeItem.FileName);

        try
        {
            var content = ListEditorTextBox.Text;
            File.WriteAllText(filePath, content, Encoding.UTF8);
            
            // Sync states
            _originalContent[_activeItem.Id] = content;
            UpdateSaveStatus();

            ListSaveStatusTextBlock.Text = $"✓ Сохранено в {DateTime.Now:HH:mm:ss}";
            ListSaveStatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGreen);
        }
        catch (Exception ex)
        {
            ListSaveStatusTextBlock.Text = $"Ошибка записи: {ex.Message}";
            ListSaveStatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
        }
    }
}

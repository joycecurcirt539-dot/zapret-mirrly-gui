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
    public string IconData { get; set; } = "";
    public string DefaultValue { get; set; } = "";
}

public class StringToGeometryConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string pathData && !string.IsNullOrWhiteSpace(pathData))
        {
            try
            {
                var xaml = $"<Geometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">{pathData}</Geometry>";
                return Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
            }
            catch { }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
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
            IconData = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zM11 19.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z",
            DefaultValue = "youtube.com\ngoogle.com"
        });

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "user_bypass",
            DisplayName = "Обход (Пользователь)",
            FileName = "list-general-user.txt",
            Description = "Ваш личный список доменов для обхода. Добавляйте сюда сайты, которые хотите разблокировать.",
            IconData = "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z",
            DefaultValue = "domain.example.abc"
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
            Id = "user_exclude",
            DisplayName = "Исключения (Пользователь)",
            FileName = "list-exclude-user.txt",
            Description = "Ваши личные домены-исключения. Внесите сюда сайты банков или госструктур, если они работают нестабильно.",
            IconData = "M15 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm-9-2V8h6v2H6zm9 4c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z",
            DefaultValue = "domain.example.abc"
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

        _listMetadata.Add(new ListMetadataItem
        {
            Id = "user_ipset_exclude",
            DisplayName = "IP-исключения (Пользователь)",
            FileName = "ipset-exclude-user.txt",
            Description = "Ваши личные IP-исключения подсетей. Защищает доверенные локальные или внешние IP от перехвата.",
            IconData = "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z M22 13h-6v-2h6v2z",
            DefaultValue = "203.0.113.113/32"
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

        var filePath = Path.Combine(ZapretService.FindListsDirectory(), _activeItem.FileName);

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

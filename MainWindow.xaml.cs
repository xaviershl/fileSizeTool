using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace FileSizeTool;

public partial class MainWindow : Window
{
    public ObservableCollection<FileItem> Items { get; } = new();
    private bool _isWorking;
    private string? _currentSortMemberPath;
    private ListSortDirection? _currentSortDirection;
    private static readonly Dictionary<string, List<FileItem>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<MainWindow> _instances = new();
    private static bool _sharedNewWindowEnabled;
    private static bool _sharedUseCacheEnabled;
    private static readonly string SettingsDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.ini");

    static MainWindow()
    {
        InitializeSettings();
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _instances.Add(this);
        MenuItemNewWindow.IsChecked = _sharedNewWindowEnabled;
        MenuItemUseCache.IsChecked = _sharedUseCacheEnabled;
        Closed += (_, _) => _instances.Remove(this);
        AppendConsole("准备就绪。请选择目录后点击“扫描”进行统计。");
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "请选择目录",
            UseDescriptionForTitle = true,
            SelectedPath = TxtPath.Text,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            TxtPath.Text = dialog.SelectedPath;
            AppendConsole($"已选择目录：{dialog.SelectedPath}");
        }
    }

    private async void BtnUp_Click(object sender, RoutedEventArgs e)
    {
        var path = TxtPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendConsole("请输入目录后再点击上一级。");
            return;
        }

        if (File.Exists(path))
        {
            path = Path.GetDirectoryName(path) ?? path;
        }

        if (!Directory.Exists(path))
        {
            System.Windows.MessageBox.Show("目录不存在，请检查路径。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var parent = Directory.GetParent(path);
        if (parent == null)
        {
            AppendConsole("当前目录已是根目录，无法上移。");
            return;
        }

        var targetPath = parent.FullName;
        TxtPath.Text = targetPath;

        if (IsUseCacheEnabled && TryLoadFromCache(targetPath))
        {
            return;
        }

        await ScanPathAsync(targetPath);
    }

    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        await ScanPathAsync(TxtPath.Text.Trim());
    }

    private async void BtnScanItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is FileItem item)
        {
            if (IsNewWindowEnabled)
            {
                OpenNewWindowAndScan(item.FullPath);
                return;
            }

            TxtPath.Text = item.FullPath;
            await ScanPathAsync(item.FullPath);
        }
    }

    private bool IsNewWindowEnabled => MenuItemNewWindow?.IsChecked == true;

    private bool IsUseCacheEnabled => MenuItemUseCache?.IsChecked == true;

    private void OpenNewWindowAndScan(string path)
    {
        var window = new MainWindow();
        window.Show();
        window.TxtPath.Text = path;
        _ = window.ScanPathAsync(path);
    }

    private void MenuItemSettings_Checked(object sender, RoutedEventArgs e)
    {
        if (sender == MenuItemNewWindow)
        {
            _sharedNewWindowEnabled = true;
        }
        else if (sender == MenuItemUseCache)
        {
            _sharedUseCacheEnabled = true;
        }

        SaveSettings();
        SyncSettingsToAllWindows();
    }

    private void MenuItemSettings_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender == MenuItemNewWindow)
        {
            _sharedNewWindowEnabled = false;
        }
        else if (sender == MenuItemUseCache)
        {
            _sharedUseCacheEnabled = false;
        }

        SaveSettings();
        SyncSettingsToAllWindows();
    }

    private static void SyncSettingsToAllWindows()
    {
        foreach (var window in _instances)
        {
            if (window.MenuItemNewWindow != null)
            {
                window.MenuItemNewWindow.IsChecked = _sharedNewWindowEnabled;
            }

            if (window.MenuItemUseCache != null)
            {
                window.MenuItemUseCache.IsChecked = _sharedUseCacheEnabled;
            }
        }
    }

    private static void InitializeSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            if (!File.Exists(SettingsFilePath))
            {
                SaveSettings();
                return;
            }

            LoadSettings();
        }
        catch
        {
            _sharedNewWindowEnabled = false;
            _sharedUseCacheEnabled = false;
        }
    }

    private static void LoadSettings()
    {
        foreach (var line in File.ReadAllLines(SettingsFilePath, Encoding.UTF8))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
            {
                continue;
            }

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            if (string.Equals(key, "NewWindow", StringComparison.OrdinalIgnoreCase))
            {
                _sharedNewWindowEnabled = bool.TryParse(value, out var flag) && flag;
            }
            else if (string.Equals(key, "UseCache", StringComparison.OrdinalIgnoreCase))
            {
                _sharedUseCacheEnabled = bool.TryParse(value, out var flag) && flag;
            }
        }
    }

    private static void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllLines(SettingsFilePath, new[]
            {
                $"NewWindow={_sharedNewWindowEnabled}",
                $"UseCache={_sharedUseCacheEnabled}"
            }, Encoding.UTF8);
        }
        catch
        {
            // 忽略设置保存失败。
        }
    }

    private void MenuItemAbout_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "文件大小查看器\n\n这是一个用于扫描目录大小、查看文件和文件夹信息的简易工具。\n\n支持：\n- 目录扫描\n- 新窗口扫描\n- 使用缓存快速跳转\n- 文件路径复制和强制删除",
            "关于 文件大小查看器",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private bool TryLoadFromCache(string path)
    {
        path = NormalizePath(path);
        if (!_cache.TryGetValue(path, out var cachedItems))
        {
            return false;
        }

        Items.Clear();
        foreach (var item in cachedItems.Select(CloneItem))
        {
            Items.Add(item);
        }

        ApplyCurrentSort();
        AppendConsole($"从缓存加载：{path}，共 {Items.Count} 条目。");
        return true;
    }

    private void CacheScanResult(string path, List<FileItem> items)
    {
        path = NormalizePath(path);
        _cache[path] = items.Select(CloneItem).ToList();
    }

    private static FileItem CloneItem(FileItem item)
    {
        return new FileItem
        {
            Index = item.Index,
            FullPath = item.FullPath,
            Size = item.Size,
            SizeText = item.SizeText,
            Type = item.Type,
            IsDirectory = item.IsDirectory
        };
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is FileItem item)
        {
            OpenPath(item.FullPath);
        }
    }

    private async void MenuCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.CommandParameter is FileItem item)
        {
            var result = await TrySetClipboardTextAsync(item.FullPath);
            if (result)
            {
                AppendConsole($"已复制路径：{item.FullPath}");
            }
        }
    }

    private Task<bool> TrySetClipboardTextAsync(string text)
    {
        return Task.Run(() =>
        {
            const int maxRetries = 10;
            const int delayMs = 50;
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                var tcs = new TaskCompletionSource<bool>();
                var thread = new Thread(() =>
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(text);
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();

                try
                {
                    if (tcs.Task.Wait(delayMs + 100))
                    {
                        return true;
                    }
                }
                catch (AggregateException ae)
                {
                    var ex = ae.InnerException ?? ae;
                    if (IsClipboardBusyException(ex))
                    {
                        Thread.Sleep(delayMs);
                        continue;
                    }
                    Dispatcher.Invoke(() => AppendConsole($"复制路径失败：{ex.Message}"));
                    return false;
                }
                catch (Exception ex)
                {
                    if (IsClipboardBusyException(ex))
                    {
                        Thread.Sleep(delayMs);
                        continue;
                    }
                    Dispatcher.Invoke(() => AppendConsole($"复制路径失败：{ex.Message}"));
                    return false;
                }
            }

            Dispatcher.Invoke(() => AppendConsole("复制路径失败：剪贴板被占用，请稍后再试。"));
            return false;
        });
    }

    private bool IsClipboardBusyException(Exception ex)
    {
        if (ex is ExternalException)
        {
            return true;
        }

        return ex.Message.Contains("剪贴板", StringComparison.CurrentCultureIgnoreCase) ||
               ex.Message.Contains("Clipboard", StringComparison.CurrentCultureIgnoreCase);
    }

    private async void MenuForceDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.CommandParameter is FileItem item)
        {
            var message = item.IsDirectory
                ? $"确认强制删除文件夹：{item.FullPath} ? 将递归删除全部内容。"
                : $"确认强制删除文件：{item.FullPath} ?";

            var result = System.Windows.MessageBox.Show(message, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await DeleteItemAsync(item);
            }
        }
    }

    private async Task ScanPathAsync(string path)
    {
        if (_isWorking)
        {
            AppendConsole("当前正在执行其它操作，请稍后再试。");
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            System.Windows.MessageBox.Show("请输入有效的目录路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (File.Exists(path))
        {
            path = Path.GetDirectoryName(path) ?? path;
        }

        if (!Directory.Exists(path))
        {
            System.Windows.MessageBox.Show("目录不存在，请检查路径。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetWorking(true);
        AppendConsole($"开始扫描：{path}");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var rootInfo = new DirectoryInfo(path);
            var items = await Task.Run(() => ScanDirectory(rootInfo));
            stopwatch.Stop();

            Items.Clear();
            for (var index = 0; index < items.Count; index++)
            {
                items[index].Index = index + 1;
                Items.Add(items[index]);
            }
            ApplyCurrentSort();
            AppendConsole($"扫描完成：{Items.Count} 条目，耗时 {stopwatch.Elapsed:mm\\:ss}。{Environment.NewLine}");

            if (IsUseCacheEnabled)
            {
                CacheScanResult(path, items);
            }
        }
        catch (Exception ex)
        {
            AppendConsole($"扫描失败：{ex.Message}");
            System.Windows.MessageBox.Show($"扫描过程中发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetWorking(false);
        }
    }

    private List<FileItem> ScanDirectory(DirectoryInfo directory)
    {
        var items = new List<FileItem>();

        DirectoryInfo[] subdirectories = Array.Empty<DirectoryInfo>();
        try
        {
            subdirectories = directory.GetDirectories();
        }
        catch (Exception ex)
        {
            AppendConsole($"无法读取子目录：{directory.FullName}，{ex.Message}");
        }

        foreach (var subdirectory in subdirectories)
        {
            AppendConsole($"开始读取子目录：{subdirectory.FullName}");
            try
            {
                var childSize = ComputeDirectorySize(subdirectory);
                items.Add(CreateFileItem(subdirectory.FullName, childSize, true));
            }
            catch (Exception ex)
            {
                AppendConsole($"读取目录失败：{subdirectory.FullName}，{ex.Message}");
            }
        }

        FileInfo[] files = Array.Empty<FileInfo>();
        try
        {
            files = directory.GetFiles();
        }
        catch (Exception ex)
        {
            AppendConsole($"无法读取文件：{directory.FullName}，{ex.Message}");
        }

        foreach (var file in files)
        {
            try
            {
                items.Add(CreateFileItem(file.FullName, file.Length, false));
            }
            catch (Exception ex)
            {
                AppendConsole($"读取文件失败：{file.FullName}，{ex.Message}");
            }
        }

        return items;
    }

    private long ComputeDirectorySize(DirectoryInfo directory)
    {
        long size = 0;

        FileInfo[] files = Array.Empty<FileInfo>();
        DirectoryInfo[] subdirectories = Array.Empty<DirectoryInfo>();

        try
        {
            files = directory.GetFiles();
        }
        catch (Exception ex)
        {
            AppendConsole($"无法读取文件：{directory.FullName}，{ex.Message}");
        }

        foreach (var file in files)
        {
            try
            {
                size += file.Length;
            }
            catch (Exception ex)
            {
                AppendConsole($"读取文件失败：{file.FullName}，{ex.Message}");
            }
        }

        try
        {
            subdirectories = directory.GetDirectories();
        }
        catch (Exception ex)
        {
            AppendConsole($"无法读取子目录：{directory.FullName}，{ex.Message}");
        }

        foreach (var subdirectory in subdirectories)
        {
            try
            {
                size += ComputeDirectorySize(subdirectory);
            }
            catch (Exception ex)
            {
                AppendConsole($"读取子目录失败：{subdirectory.FullName}，{ex.Message}");
            }
        }

        return size;
    }

    private async Task DeleteItemAsync(FileItem item)
    {
        if (_isWorking)
        {
            AppendConsole("当前正在执行其它操作，请稍后再试。");
            return;
        }

        try
        {
            if (item.IsDirectory)
            {
                Directory.Delete(item.FullPath, true);
            }
            else
            {
                File.Delete(item.FullPath);
            }

            AppendConsole($"已删除：{item.FullPath}");
            RemoveDeletedItems(item.FullPath);
        }
        catch (Exception ex)
        {
            AppendConsole($"删除失败：{item.FullPath}，{ex.Message}");
            System.Windows.MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveDeletedItems(string path)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = normalizedPath + Path.DirectorySeparatorChar;
        var removed = Items.Where(item => string.Equals(item.FullPath, normalizedPath, StringComparison.OrdinalIgnoreCase) || item.FullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var item in removed)
        {
            Items.Remove(item);
        }

        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].Index = index + 1;
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
            AppendConsole($"打开：{path}");
        }
        catch (Exception ex)
        {
            AppendConsole($"打开失败：{path}，{ex.Message}");
            System.Windows.MessageBox.Show($"打开失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private FileItem CreateFileItem(string fullPath, long size, bool isDirectory)
    {
        return new FileItem
        {
            FullPath = fullPath,
            Size = size,
            SizeText = FormatSize(size),
            Type = isDirectory ? "文件夹" : "文件",
            IsDirectory = isDirectory
        };
    }

    private void DataGridItems_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var column = e.Column;
        var direction = column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;

        foreach (var col in DataGridItems.Columns)
        {
            if (col != column)
            {
                col.SortDirection = null;
            }
        }

        column.SortDirection = direction;
        _currentSortMemberPath = column.SortMemberPath;
        _currentSortDirection = direction;

        ApplyCurrentSort();
    }

    private void ApplyCurrentSort()
    {
        if (string.IsNullOrWhiteSpace(_currentSortMemberPath) || !_currentSortDirection.HasValue)
        {
            return;
        }

        var column = DataGridItems.Columns.FirstOrDefault(c => c.SortMemberPath == _currentSortMemberPath);
        if (column == null)
        {
            return;
        }

        foreach (var col in DataGridItems.Columns)
        {
            col.SortDirection = col == column ? _currentSortDirection : null;
        }

        var sortedItems = _currentSortMemberPath switch
        {
            "FullPath" => _currentSortDirection == ListSortDirection.Ascending
                ? Items.OrderBy(i => i.FullPath, StringComparer.CurrentCultureIgnoreCase)
                : Items.OrderByDescending(i => i.FullPath, StringComparer.CurrentCultureIgnoreCase),
            "Size" => _currentSortDirection == ListSortDirection.Ascending
                ? Items.OrderBy(i => i.Size)
                : Items.OrderByDescending(i => i.Size),
            "Type" => _currentSortDirection == ListSortDirection.Ascending
                ? Items.OrderBy(i => i.Type, StringComparer.CurrentCultureIgnoreCase)
                : Items.OrderByDescending(i => i.Type, StringComparer.CurrentCultureIgnoreCase),
            _ => Items.OrderBy(i => i.Index)
        };

        var sortedList = sortedItems.ToList();
        Items.Clear();
        for (var index = 0; index < sortedList.Count; index++)
        {
            sortedList[index].Index = index + 1;
            Items.Add(sortedList[index]);
        }
    }

    private string FormatSize(long size)
    {
        if (size < 1024) return $"{size} B";
        if (size < 1024 * 1024) return $"{size / 1024.0:F2} KB";
        if (size < 1024 * 1024 * 1024) return $"{size / (1024.0 * 1024):F2} MB";
        return $"{size / (1024.0 * 1024 * 1024):F2} GB";
    }

    private void AppendConsole(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            TxtConsole.ScrollToEnd();
        });
    }

    private void MenuClearConsole_Click(object sender, RoutedEventArgs e)
    {
        TxtConsole.Clear();
    }

    private void SetWorking(bool working)
    {
        _isWorking = working;
        BtnBrowse.IsEnabled = !working;
        BtnScan.IsEnabled = !working;
        DataGridItems.IsEnabled = !working;
    }
}

public sealed class FileItem : INotifyPropertyChanged
{
    private int _index;

    public int Index
    {
        get => _index;
        set
        {
            if (_index == value) return;
            _index = value;
            OnPropertyChanged(nameof(Index));
        }
    }

    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string SizeText { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

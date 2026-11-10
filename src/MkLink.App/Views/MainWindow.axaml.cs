using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MkLink.Models;
using MkLink.Services;
using MkLink.ViewModels;

namespace MkLink.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel? ViewModel => DataContext as MainViewModel;
        private string _currentMarkdownFilePath = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            TitleTextBlock.Text = "MkLink-GMTPC Path Mapper | Build: " + GetBuildTimeText();
            BuildInfoTextBlock.Text = ".NET 8.0 Windows x64 Portable | " + GetBuildTimeText();

            AddHandler(KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel);
            AddHandler(DragDrop.DropEvent, MainWindow_Drop);
            AddHandler(DragDrop.DragOverEvent, MainWindow_DragOver);

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                CheckMklinkControl.BindViewModel(ViewModel);
            }
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            ElevatedCmdRunner.CleanupProcesses();
        }

        private static string GetBuildTimeText()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                DateTime buildTime = File.GetLastWriteTime(location);
                return buildTime.ToString("yyyy-MM-dd HH:mm:ss dddd");
            }
            catch
            {
                return DateTime.Now.ToString("yyyy-MM-dd");
            }
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled) return;

            bool ctrl = (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;

            if (ctrl && e.Key == Key.S)
            {
                SaveMarkdownButton_Click(null, null);
                e.Handled = true;
                return;
            }

            if (ctrl && e.Key == Key.O)
            {
                LoadMarkdownButton_Click(null, null);
                e.Handled = true;
                return;
            }

            if (ctrl && e.Key == Key.Z)
            {
                if (ViewModel != null && ViewModel.CanUndo)
                {
                    ViewModel.Undo();
                    e.Handled = true;
                    return;
                }
            }

            if (ctrl && e.Key == Key.Y)
            {
                if (ViewModel != null && ViewModel.CanRedo)
                {
                    ViewModel.Redo();
                    e.Handled = true;
                    return;
                }
            }

            if (ctrl && e.Key == Key.V)
            {
                if (e.Source is not TextBox)
                {
                    _ = PasteClipboardIntoCopyTabAsync();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Delete)
            {
                if (e.Source is not TextBox)
                {
                    DeleteSelectedRows_Click(null, null);
                    e.Handled = true;
                }
            }
        }

        private async Task PasteClipboardIntoCopyTabAsync()
        {
            if (ViewModel?.CopyTab == null) return;

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            string? text = await clipboard.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                ViewModel.CopyTab.PasteSourceText(text);
                SetStatus("Đã dán dữ liệu từ Clipboard vào Tab COPY.");
            }
        }

        private void MainWindow_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files) || e.Data.Contains(DataFormats.Text))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void MainWindow_Drop(object? sender, DragEventArgs e)
        {
            if (ViewModel?.CopyTab == null) return;

            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles();
                if (files != null)
                {
                    var paths = files.Select(f => f.Path.LocalPath).Where(p => !string.IsNullOrWhiteSpace(p));
                    string combined = string.Join(Environment.NewLine, paths);
                    if (!string.IsNullOrWhiteSpace(combined))
                    {
                        ViewModel.CopyTab.PasteSourceText(combined);
                        SetStatus($"Đã thêm {paths.Count()} đường dẫn kéo thả vào Tab COPY.");
                        e.Handled = true;
                        return;
                    }
                }
            }

            if (e.Data.Contains(DataFormats.Text))
            {
                string? text = e.Data.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ViewModel.CopyTab.PasteSourceText(text);
                    SetStatus("Đã thêm nội dung văn bản kéo thả vào Tab COPY.");
                    e.Handled = true;
                }
            }
        }

        private async void BrowseRootFolderButton_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel?.CopyTab == null) return;

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var initialFolder = await storage.TryGetFolderFromPathAsync(ViewModel.CopyTab.SelectedRootFolder);
            var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Chọn thư mục Root cho Symlink Mapping",
                SuggestedStartLocation = initialFolder,
                AllowMultiple = false
            });

            if (result != null && result.Count > 0)
            {
                string picked = result[0].Path.LocalPath;
                ViewModel.CopyTab.SetSelectedRootFolder(picked);
                ViewModel.CopyTab.ApplyTargetCommand.Execute(null);
                SetStatus("Đã chọn Root folder: " + picked);
            }
        }

        private async void SaveMarkdownButton_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;

            bool showSaveDialog = false;

            if (!string.IsNullOrWhiteSpace(_currentMarkdownFilePath))
            {
                var confirmDialog = new SaveConfirmDialog(_currentMarkdownFilePath);
                var result = await confirmDialog.ShowDialog<bool>(this);
                if (result)
                {
                    if (confirmDialog.SelectedAction == SaveDialogAction.Overwrite)
                    {
                        try
                        {
                            File.WriteAllText(_currentMarkdownFilePath, ViewModel.ExportMarkdown());
                            PortableSettingsService.SaveLastSavedMdFolder(_currentMarkdownFilePath);
                            SetStatus("Đã lưu đè file Markdown: " + _currentMarkdownFilePath);
                            return;
                        }
                        catch (Exception ex)
                        {
                            SetStatus("Lỗi lưu file: " + ex.Message);
                            return;
                        }
                    }
                    else if (confirmDialog.SelectedAction == SaveDialogAction.SaveAs)
                    {
                        showSaveDialog = true;
                    }
                }
                else
                {
                    return;
                }
            }
            else
            {
                showSaveDialog = true;
            }

            if (showSaveDialog)
            {
                var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storage == null) return;

                string initialDir = PortableSettingsService.GetLastSavedMdFolder();
                IStorageFolder? startLocation = null;
                if (!string.IsNullOrWhiteSpace(initialDir))
                {
                    startLocation = await storage.TryGetFolderFromPathAsync(initialDir);
                }

                var saveFile = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Lưu cấu hình Markdown",
                    DefaultExtension = "md",
                    SuggestedFileName = string.IsNullOrWhiteSpace(_currentMarkdownFilePath)
                        ? "mklink.md"
                        : Path.GetFileName(_currentMarkdownFilePath),
                    SuggestedStartLocation = startLocation,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Markdown files") { Patterns = new[] { "*.md" } },
                        new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
                    }
                });

                if (saveFile != null)
                {
                    string localPath = saveFile.Path.LocalPath;
                    try
                    {
                        File.WriteAllText(localPath, ViewModel.ExportMarkdown());
                        _currentMarkdownFilePath = localPath;
                        PortableSettingsService.SaveLastSavedMdFolder(localPath);
                        SetStatus("Đã lưu cấu hình Markdown: " + localPath);
                    }
                    catch (Exception ex)
                    {
                        SetStatus("Lỗi lưu file: " + ex.Message);
                    }
                }
            }
        }

        private async void LoadMarkdownButton_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            string initialDir = PortableSettingsService.GetLastSavedMdFolder();
            IStorageFolder? startLocation = null;
            if (!string.IsNullOrWhiteSpace(initialDir))
            {
                startLocation = await storage.TryGetFolderFromPathAsync(initialDir);
            }

            var openFiles = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Mở file cấu hình Markdown",
                AllowMultiple = false,
                SuggestedStartLocation = startLocation,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Markdown files") { Patterns = new[] { "*.md" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
                }
            });

            if (openFiles != null && openFiles.Count > 0)
            {
                string localPath = openFiles[0].Path.LocalPath;
                try
                {
                    string markdown = File.ReadAllText(localPath);
                    ViewModel.ImportMarkdown(markdown);
                    _currentMarkdownFilePath = localPath;
                    PortableSettingsService.SaveLastSavedMdFolder(localPath);
                    SetStatus("Đã nạp file Markdown: " + localPath);
                }
                catch (Exception ex)
                {
                    SetStatus("Lỗi nạp file: " + ex.Message);
                }
            }
        }

        private void DonateAuthorButton_Click(object? sender, RoutedEventArgs? e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://tinyurl.com/gmtpcdonate",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetStatus("Lỗi mở trình duyệt: " + ex.Message);
            }
        }

        private void DeleteSelectedRows_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel?.CopyTab == null) return;

            var selected = CopyInputGrid.SelectedItems.Cast<PathItem>().ToList();
            if (selected.Count == 0 && CopyOutputGrid != null)
            {
                selected = CopyOutputGrid.SelectedItems.Cast<PathItem>().ToList();
            }

            if (selected.Count > 0)
            {
                ViewModel.CopyTab.RemoveSourceItems(selected);
                SetStatus($"Đã xóa {selected.Count} dòng được chọn.");
            }
        }

        private void ClearAllRows_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel?.CopyTab == null) return;
            ViewModel.CopyTab.ClearAllSourceItems();
            SetStatus("Đã xóa toàn bộ danh sách nguồn.");
        }

        private void SortInputButton_Click(object? sender, RoutedEventArgs? e)
        {
            ViewModel?.CopyTab?.SortSourceItemsByInput(false, false);
            SetStatus("Đã sắp xếp theo Input.");
        }

        private void SortNormalizeButton_Click(object? sender, RoutedEventArgs? e)
        {
            ViewModel?.CopyTab?.SortSourceItemsByInput(true, false);
            SetStatus("Đã sắp xếp theo Normalize Input.");
        }

        private void SortEditedButton_Click(object? sender, RoutedEventArgs? e)
        {
            ViewModel?.CopyTab?.SortSourceItemsByEdited(true);
            SetStatus("Đã sắp xếp theo Edit Output (Edited lên đầu).");
        }

        private async void EditOutputPathMenuItem_Click(object? sender, RoutedEventArgs? e)
        {
            var selected = CopyOutputGrid?.SelectedItem as PathItem;
            if (selected == null || selected.IsPlaceholder) return;

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            string initial = !string.IsNullOrWhiteSpace(selected.EditOutputPath)
                ? selected.EditOutputPath
                : selected.MappedTarget;

            IStorageFolder? startLocation = null;
            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            {
                startLocation = await storage.TryGetFolderFromPathAsync(initial);
            }

            var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Chọn thư mục Output đã chỉnh sửa cho dòng này",
                SuggestedStartLocation = startLocation,
                AllowMultiple = false
            });

            if (picked != null && picked.Count > 0)
            {
                ViewModel?.CaptureUndoSnapshot();
                selected.EditOutputPath = picked[0].Path.LocalPath;
                SetStatus("Đã cập nhật Edit Output cho dòng đã chọn.");
            }
        }

        private void RestoreDefaultPathMenuItem_Click(object? sender, RoutedEventArgs? e)
        {
            var selected = CopyOutputGrid?.SelectedItem as PathItem;
            if (selected == null || selected.IsPlaceholder) return;

            ViewModel?.CaptureUndoSnapshot();
            selected.EditOutputPath = string.Empty;
            SetStatus("Đã khôi phục Default Output cho dòng đã chọn.");
        }

        private async void CopyCurrentResult_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;

            var btn = sender as Button;
            string tag = btn?.Tag?.ToString() ?? "CopyTab";
            string text = tag switch
            {
                "CopyTab" => ViewModel.CopyTab.ScriptResult,
                "DeleteTab" => ViewModel.DeleteTab.ScriptResult,
                "MklinkTab" => ViewModel.MklinkTab.ScriptResult,
                "CopyReverseTab" => ViewModel.CopyReverseTab.ScriptResult,
                "DeleteReverseTab" => ViewModel.DeleteReverseTab.ScriptResult,
                _ => string.Empty
            };

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null && !string.IsNullOrEmpty(text))
            {
                await clipboard.SetTextAsync(text);
                SetStatus("Đã copy script vào Clipboard.");
            }
        }

        private async void CopyCombinedDirect_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(ViewModel.CombinedCopyDeleteMklinkScript);
                SetStatus("Đã copy Combined Direct script vào Clipboard.");
            }
        }

        private async void CopyCombinedReverse_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(ViewModel.CombinedDeleteAndCopyReverseScript);
                SetStatus("Đã copy Combined Reverse script vào Clipboard.");
            }
        }

        private void CopyDeleteMklinkDirect_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;

            ElevatedCmdRunner.Run(
                new[] { "COPY", "DELETE", "MKLINK D" },
                () => Avalonia.Threading.Dispatcher.UIThread.Post(() => CheckMklinkControl.RefreshFromSource()),
                out string err,
                ViewModel.CopyTab.ScriptResult,
                ViewModel.DeleteTab.ScriptResult,
                ViewModel.MklinkTab.ScriptResult);

            if (!string.IsNullOrEmpty(err)) SetStatus(err);
        }

        private void DeleteMklinkDirect_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;

            ElevatedCmdRunner.Run(
                new[] { "DELETE", "MKLINK D" },
                () => Avalonia.Threading.Dispatcher.UIThread.Post(() => CheckMklinkControl.RefreshFromSource()),
                out string err,
                ViewModel.DeleteTab.ScriptResult,
                ViewModel.MklinkTab.ScriptResult);

            if (!string.IsNullOrEmpty(err)) SetStatus(err);
        }

        private void RunCopyReverse_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;

            ElevatedCmdRunner.Run(
                new[] { "COPY REVERSE" },
                () => Avalonia.Threading.Dispatcher.UIThread.Post(() => CheckMklinkControl.RefreshFromSource()),
                out string err,
                ViewModel.CopyReverseTab.ScriptResult);

            if (!string.IsNullOrEmpty(err)) SetStatus(err);
        }

        private void RunDeleteReverse_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;

            ElevatedCmdRunner.Run(
                new[] { "DELETE REVERSE" },
                () => Avalonia.Threading.Dispatcher.UIThread.Post(() => CheckMklinkControl.RefreshFromSource()),
                out string err,
                ViewModel.DeleteReverseTab.ScriptResult);

            if (!string.IsNullOrEmpty(err)) SetStatus(err);
        }

        private void RunDeleteAndCopyReverse_Click(object? sender, RoutedEventArgs? e)
        {
            if (ViewModel == null) return;

            ElevatedCmdRunner.Run(
                new[] { "DELETE REVERSE", "COPY REVERSE", "DELETE REVERSE" },
                () => Avalonia.Threading.Dispatcher.UIThread.Post(() => CheckMklinkControl.RefreshFromSource()),
                out string err,
                ViewModel.DeleteReverseTab.ScriptResult,
                ViewModel.CopyReverseTab.ScriptResult,
                ViewModel.DeleteReverseTab.ScriptResult);

            if (!string.IsNullOrEmpty(err)) SetStatus(err);
        }

        private void SetStatus(string message)
        {
            StatusMessageTextBlock.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
        }
    }
}

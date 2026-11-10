using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MkLink.Models;
using MkLink.Services;
using MkLink.ViewModels;

namespace MkLink.Views
{
    public partial class CheckMklinkView : UserControl
    {
        private MainViewModel? _viewModel;
        private readonly List<MklinkCheckEntry> _allEntries = new List<MklinkCheckEntry>();
        private readonly ObservableCollection<MklinkCheckEntry> _displayedEntries = new ObservableCollection<MklinkCheckEntry>();

        public CheckMklinkView()
        {
            InitializeComponent();
            CheckGrid.ItemsSource = _displayedEntries;
        }

        public void BindViewModel(MainViewModel viewModel)
        {
            _viewModel = viewModel;
            if (_viewModel?.CopyTab != null)
            {
                _viewModel.CopyTab.DataChanged += (s, e) => RefreshFromSource();
            }
            RefreshFromSource();
        }

        public void RefreshFromSource()
        {
            if (_viewModel?.CopyTab == null) return;

            var scanned = SymlinkScanner.BuildEntries(_viewModel.CopyTab.SourceItems);
            _allEntries.Clear();
            _allEntries.AddRange(scanned);

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            string keyword = SearchBox?.Text?.Trim() ?? string.Empty;
            int statusIndex = StatusFilterComboBox?.SelectedIndex ?? 0;
            int matchIndex = MatchFilterComboBox?.SelectedIndex ?? 0;

            var filtered = _allEntries.AsEnumerable();

            if (!string.IsNullOrEmpty(keyword))
            {
                filtered = filtered.Where(x =>
                    x.NormalizedInput.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.OutputPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.AlreadyMklinkTo.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Status filter: 0: All, 1: MKLINK, 2: Real folder, 3: Missing
            if (statusIndex == 1)
            {
                filtered = filtered.Where(x => x.IsLink);
            }
            else if (statusIndex == 2)
            {
                filtered = filtered.Where(x => x.Exists && !x.IsLink);
            }
            else if (statusIndex == 3)
            {
                filtered = filtered.Where(x => !x.Exists);
            }

            // Match filter: 0: All, 1: Matched, 2: Not matched
            if (matchIndex == 1)
            {
                filtered = filtered.Where(x => x.IsMatched);
            }
            else if (matchIndex == 2)
            {
                filtered = filtered.Where(x => !x.IsMatched && x.IsLink);
            }

            _displayedEntries.Clear();
            foreach (var item in filtered)
            {
                _displayedEntries.Add(item);
            }

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int linkCount = 0;
            int missingCount = 0;
            for (int i = 0; i < _allEntries.Count; i++)
            {
                if (_allEntries[i].IsLink) linkCount++;
                else if (!_allEntries[i].Exists) missingCount++;
            }

            int realCount = _allEntries.Count - linkCount - missingCount;
            SummaryTextBlock.Text = $"Tổng: {_allEntries.Count} | Folder thật: {realCount} | Shortcut mklink: {linkCount} | Chưa tồn tại: {missingCount} (Hiển thị: {_displayedEntries.Count})";
        }

        private void RefreshButton_Click(object? sender, RoutedEventArgs e) => RefreshFromSource();

        private void FilterComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilters();

        private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => ApplyFilters();

        private void ClearFilterButton_Click(object? sender, RoutedEventArgs e)
        {
            if (SearchBox != null) SearchBox.Text = string.Empty;
            if (StatusFilterComboBox != null) StatusFilterComboBox.SelectedIndex = 0;
            if (MatchFilterComboBox != null) MatchFilterComboBox.SelectedIndex = 0;
            ApplyFilters();
        }

        private void SelectAllButton_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedEntries) item.IsSelected = true;
        }

        private void SelectNoneButton_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedEntries) item.IsSelected = false;
        }

        private void InvertSelectButton_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedEntries) item.IsSelected = !item.IsSelected;
        }

        private void SelectMklinkButton_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedEntries) item.IsSelected = item.IsLink;
        }

        private void SelectRealFolderButton_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedEntries) item.IsSelected = item.Exists && !item.IsLink;
        }

        private void SelectMissingButton_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedEntries) item.IsSelected = !item.Exists;
        }

        private List<MklinkCheckEntry> GetSelectedEntries() => _displayedEntries.Where(x => x.IsSelected).ToList();

        private void MakeMklinkCopyButton_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedEntries();
            if (selected.Count == 0) return;

            var copyScripts = new List<string>();
            var delScripts = new List<string>();
            var mklinkScripts = new List<string>();

            foreach (var entry in selected)
            {
                copyScripts.Add($"if not exist \"{entry.OutputPath}\" mkdir \"{entry.OutputPath}\"\nxcopy \"{entry.NormalizedInput}\" \"{entry.OutputPath}\" /E /I /Y");
                delScripts.Add($"rmdir /s /q \"{entry.NormalizedInput}\"");
                mklinkScripts.Add($"mklink /D \"{entry.NormalizedInput}\" \"{entry.OutputPath}\"");
            }

            ElevatedCmdRunner.Run(
                new[] { "COPY", "DELETE", "MKLINK D" },
                () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshFromSource),
                out _,
                string.Join("\n", copyScripts),
                string.Join("\n", delScripts),
                string.Join("\n", mklinkScripts));
        }

        private void MakeMklinkNoCopyButton_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedEntries();
            if (selected.Count == 0) return;

            var delScripts = new List<string>();
            var mklinkScripts = new List<string>();

            foreach (var entry in selected)
            {
                delScripts.Add($"rmdir /s /q \"{entry.NormalizedInput}\"");
                mklinkScripts.Add($"mklink /D \"{entry.NormalizedInput}\" \"{entry.OutputPath}\"");
            }

            ElevatedCmdRunner.Run(
                new[] { "DELETE", "MKLINK D" },
                () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshFromSource),
                out _,
                string.Join("\n", delScripts),
                string.Join("\n", mklinkScripts));
        }

        private void MakeReverseButton_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedEntries();
            if (selected.Count == 0) return;

            var delReverseScripts = new List<string>();
            var copyReverseScripts = new List<string>();

            foreach (var entry in selected)
            {
                delReverseScripts.Add($"rmdir /s /q \"{entry.NormalizedInput}\"");
                copyReverseScripts.Add($"xcopy \"{entry.OutputPath}\" \"{entry.NormalizedInput}\" /E /I /Y");
            }

            ElevatedCmdRunner.Run(
                new[] { "DELETE REVERSE", "COPY REVERSE", "DELETE REVERSE" },
                () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshFromSource),
                out _,
                string.Join("\n", delReverseScripts),
                string.Join("\n", copyReverseScripts),
                string.Join("\n", delReverseScripts));
        }

        private void MoveToDestinationButton_Click(object? sender, RoutedEventArgs e)
        {
            var selected = GetSelectedEntries();
            if (selected.Count == 0) return;

            var moveScripts = new List<string>();
            foreach (var entry in selected)
            {
                moveScripts.Add($"robocopy \"{entry.NormalizedInput}\" \"{entry.OutputPath}\" /E /MOVE");
            }

            ElevatedCmdRunner.Run(
                new[] { "MOVE TO DESTINATION" },
                () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshFromSource),
                out _,
                string.Join("\n", moveScripts));
        }

        private void DeleteSelectedLinesButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_viewModel?.CopyTab == null) return;

            var selected = GetSelectedEntries();
            if (selected.Count == 0) return;

            var indices = selected.Select(x => x.SourceIndex).Distinct().OrderByDescending(x => x).ToList();
            var itemsToDelete = new List<PathItem>();
            foreach (int index in indices)
            {
                if (index >= 0 && index < _viewModel.CopyTab.SourceItems.Count)
                {
                    var item = _viewModel.CopyTab.SourceItems[index];
                    if (!item.IsPlaceholder && !item.IsEmpty)
                    {
                        itemsToDelete.Add(item);
                    }
                }
            }

            _viewModel.CopyTab.RemoveSourceItems(itemsToDelete);
            RefreshFromSource();
        }
    }
}

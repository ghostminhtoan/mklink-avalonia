using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Avalonia.Threading;
using MkLink.Models;

namespace MkLink.ViewModels
{
    public enum ScriptMode
    {
        Copy,
        Delete,
        MklinkD,
        ReverseCopy,
        ReverseDelete
    }

    public enum EditOutputFilterKind
    {
        All,
        Default,
        Edited
    }

    public class MappingInfo
    {
        public MappingInfo(string normalizedSource, string mappedTarget)
        {
            NormalizedSource = normalizedSource;
            MappedTarget = mappedTarget;
        }

        public string NormalizedSource { get; }
        public string MappedTarget { get; }
    }

    public class PathTabViewModel : ViewModelBase
    {
        private const string DefaultRootFolder = @"D:\";
        private const string DefaultSubfolderName = "USERS";

        private readonly MainViewModel _owner;
        private readonly ScriptMode _scriptMode;
        private readonly PathTabViewModel? _masterTab;
        private string _selectedRootFolder;
        private string _subfolderName;
        private string _rootTargetPath;
        private string _scriptResult = string.Empty;
        private EditOutputFilterKind _editOutputFilterKind = EditOutputFilterKind.All;
        private bool _isRefreshing;
        private bool _placeholderRefreshPending;

        public PathTabViewModel(
            MainViewModel owner,
            string title,
            ScriptMode scriptMode,
            ObservableCollection<PathItem> sourceItems,
            bool isMirrorTab,
            PathTabViewModel? masterTab)
        {
            _owner = owner;
            Title = title;
            _scriptMode = scriptMode;
            _masterTab = masterTab;
            IsMirrorTab = isMirrorTab;
            _selectedRootFolder = DefaultRootFolder;
            _subfolderName = DefaultSubfolderName;
            _rootTargetPath = Path.Combine(_selectedRootFolder, _subfolderName);
            SourceItems = sourceItems;
            SourceItems.CollectionChanged += SourceItems_CollectionChanged;

            ApplyTargetCommand = new RelayCommand(ApplyTarget, CanEditSource);
            CopyScriptCommand = new RelayCommand(CopyScript);
            UndoCommand = new RelayCommand(Undo, CanUndo);
            RedoCommand = new RelayCommand(Redo, CanRedo);

            for (int i = 0; i < SourceItems.Count; i++)
            {
                SourceItems[i].PropertyChanged += PathItem_PropertyChanged;
            }

            if (!IsMirrorTab)
            {
                EnsureInputPlaceholder();
                RefreshAll();
            }

            RefreshScript();
        }

        public event EventHandler? DataChanged;

        public string Title { get; }

        public ScriptMode ScriptMode => _scriptMode;

        public bool IsMirrorTab { get; }

        public bool CanEditRows => !IsMirrorTab;

        public ObservableCollection<PathItem> SourceItems { get; }

        public string SelectedRootFolder => _selectedRootFolder;

        public string SubfolderName
        {
            get => (IsMirrorTab && _masterTab != null) ? _masterTab.SubfolderName : _subfolderName;
            set
            {
                if (_subfolderName != value)
                {
                    _subfolderName = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public string RootTargetPath
        {
            get => (IsMirrorTab && _masterTab != null) ? _masterTab.RootTargetPath : _rootTargetPath;
            private set
            {
                if (_rootTargetPath != value)
                {
                    _rootTargetPath = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public string ScriptResult
        {
            get => _scriptResult;
            private set
            {
                if (_scriptResult != value)
                {
                    _scriptResult = value;
                    OnPropertyChanged();
                }
            }
        }

        public EditOutputFilterKind EditOutputFilterKind
        {
            get => _editOutputFilterKind;
            set
            {
                if (_editOutputFilterKind != value)
                {
                    _editOutputFilterKind = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand ApplyTargetCommand { get; }
        public ICommand CopyScriptCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        public void NotifyMirrorStateChanged()
        {
            if (!IsMirrorTab) return;
            OnPropertyChanged(nameof(SubfolderName));
            OnPropertyChanged(nameof(RootTargetPath));
        }

        public void NotifyDisplayOptionsChanged()
        {
            OnPropertyChanged("IsWordWrapEnabled");
        }

        public void PasteSourceText(string text)
        {
            if (IsMirrorTab && _masterTab != null)
            {
                _masterTab.PasteSourceText(text);
                return;
            }

            if (string.IsNullOrWhiteSpace(text)) return;

            RecordHistory();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = CleanInput(lines[i]);
                if (line.Length > 0)
                {
                    AddSourceLine(line);
                }
            }

            RefreshAll();
            RaiseDataChanged();
        }

        public void LoadDocumentState(MarkdownDocumentState state)
        {
            if (IsMirrorTab)
            {
                _masterTab?.LoadDocumentState(state);
                return;
            }

            if (state == null) return;

            if (_owner != null && !_owner.IsRestoringHistory)
            {
                _owner.CaptureUndoSnapshot();
            }

            _isRefreshing = true;
            try
            {
                _selectedRootFolder = string.IsNullOrWhiteSpace(state.SelectedRootFolder) ? DefaultRootFolder : state.SelectedRootFolder;
                _subfolderName = string.IsNullOrWhiteSpace(state.SubfolderName) ? DefaultSubfolderName : state.SubfolderName;
                RootTargetPath = string.IsNullOrWhiteSpace(state.RootTargetPath)
                    ? Path.Combine(_selectedRootFolder, _subfolderName)
                    : state.RootTargetPath;

                SourceItems.Clear();
                if (state.PathRows != null && state.PathRows.Count > 0)
                {
                    for (int i = 0; i < state.PathRows.Count; i++)
                    {
                        var row = state.PathRows[i];
                        if (row == null) continue;

                        string originalInput = CleanInput(row.OriginalInput);
                        if (string.IsNullOrWhiteSpace(originalInput)) continue;

                        var item = new PathItem { OriginalInput = originalInput };
                        SourceItems.Add(item);
                        RefreshItem(item);

                        if (!string.IsNullOrWhiteSpace(row.NormalizedInput))
                        {
                            item.NormalizedInput = CleanInput(row.NormalizedInput);
                        }

                        if (!string.IsNullOrWhiteSpace(row.OutputPath))
                        {
                            item.MappedTarget = CleanInput(row.OutputPath);
                        }

                        item.EditOutputPath = CleanInput(row.EditOutputPath);
                    }
                }
                else if (state.InputLines != null)
                {
                    for (int i = 0; i < state.InputLines.Count; i++)
                    {
                        string input = CleanInput(state.InputLines[i]);
                        if (input.Length > 0)
                        {
                            SourceItems.Add(new PathItem { OriginalInput = input });
                        }
                    }
                }
            }
            finally
            {
                _isRefreshing = false;
            }

            EnsureInputPlaceholder();
            RefreshAll();

            if (state.PathRows != null && state.PathRows.Count > 0)
            {
                _isRefreshing = true;
                try
                {
                    int limit = Math.Min(state.PathRows.Count, SourceItems.Count);
                    for (int i = 0; i < limit; i++)
                    {
                        var row = state.PathRows[i];
                        var item = SourceItems[i];
                        if (row == null || item == null || item.IsPlaceholder) continue;

                        item.NormalizedInput = CleanInput(row.NormalizedInput);
                        item.MappedTarget = CleanInput(row.OutputPath);
                        item.EditOutputPath = CleanInput(row.EditOutputPath);
                    }
                }
                finally
                {
                    _isRefreshing = false;
                }

                RefreshScript();
            }

            OnPropertyChanged(nameof(SelectedRootFolder));
            OnPropertyChanged(nameof(SubfolderName));
            OnPropertyChanged(nameof(RootTargetPath));
            RaiseDataChanged();
        }

        public void RefreshScript()
        {
            var builder = new StringBuilder();

            for (int i = 0; i < SourceItems.Count; i++)
            {
                PathItem item = SourceItems[i];
                if (!CanGenerateCommand(item)) continue;

                if (builder.Length > 0) builder.AppendLine();
                builder.Append(BuildCommand(item));
            }

            ScriptResult = builder.ToString();
        }

        private bool CanEditSource() => !IsMirrorTab;

        public void SetSelectedRootFolder(string selectedRootFolder)
        {
            if (IsMirrorTab || string.IsNullOrWhiteSpace(selectedRootFolder)) return;

            RecordHistory();
            _selectedRootFolder = selectedRootFolder;
            OnPropertyChanged(nameof(SelectedRootFolder));
        }

        private void ApplyTarget()
        {
            if (IsMirrorTab) return;

            RecordHistory();
            string root = CleanInput(_selectedRootFolder);
            string subfolder = CleanInput(SubfolderName);

            if (root.Length == 0) root = DefaultRootFolder;

            RootTargetPath = subfolder.Length > 0 ? Path.Combine(root, subfolder) : root;
            try
            {
                if (!string.IsNullOrWhiteSpace(RootTargetPath))
                {
                    Directory.CreateDirectory(RootTargetPath);
                }
            }
            catch
            {
            }

            RefreshAll();
            RaiseDataChanged();
        }

        private void CopyScript()
        {
            // Được gọi từ UI hoặc helper
        }

        public MarkdownDocumentState CaptureDocumentState()
        {
            var state = new MarkdownDocumentState
            {
                SelectedRootFolder = _selectedRootFolder,
                SubfolderName = _subfolderName,
                RootTargetPath = _rootTargetPath,
                InputLines = new List<string>(),
                PathRows = new List<MarkdownPathRowState>()
            };

            for (int i = 0; i < SourceItems.Count; i++)
            {
                PathItem item = SourceItems[i];
                if (item != null && !item.IsPlaceholder && !string.IsNullOrWhiteSpace(item.OriginalInput))
                {
                    state.InputLines.Add(item.OriginalInput);
                    state.PathRows.Add(new MarkdownPathRowState
                    {
                        OriginalInput = item.OriginalInput,
                        NormalizedInput = item.NormalizedInput,
                        OutputPath = item.MappedTarget,
                        EditOutputPath = item.EditOutputPath
                    });
                }
            }

            return state;
        }

        public void RemoveSourceItem(PathItem item)
        {
            if (IsMirrorTab || item == null) return;
            RemoveSourceItems(new[] { item });
        }

        public void RemoveSourceItems(IEnumerable<PathItem> items)
        {
            if (IsMirrorTab || items == null) return;

            var deleteList = new List<PathItem>();
            foreach (var item in items)
            {
                if (item != null && !item.IsPlaceholder && !item.IsEmpty && SourceItems.Contains(item))
                {
                    deleteList.Add(item);
                }
            }

            if (deleteList.Count == 0) return;

            RecordHistory();
            for (int i = 0; i < deleteList.Count; i++)
            {
                SourceItems.Remove(deleteList[i]);
            }
        }

        public void MoveSourceItems(IEnumerable<PathItem> items, PathItem? targetItem)
        {
            if (IsMirrorTab || items == null) return;

            var moveList = new List<PathItem>();
            foreach (var item in items)
            {
                if (item != null && !item.IsPlaceholder && SourceItems.Contains(item) && !moveList.Contains(item))
                {
                    moveList.Add(item);
                }
            }

            if (moveList.Count == 0) return;
            if (targetItem != null && moveList.Contains(targetItem)) return;

            moveList.Sort((left, right) => SourceItems.IndexOf(left).CompareTo(SourceItems.IndexOf(right)));

            RecordHistory();
            _isRefreshing = true;
            try
            {
                var originalIndices = new List<int>();
                for (int i = 0; i < moveList.Count; i++)
                {
                    originalIndices.Add(SourceItems.IndexOf(moveList[i]));
                }
                originalIndices.Sort();

                int targetIndex = targetItem != null ? SourceItems.IndexOf(targetItem) : GetPlaceholderIndex();
                if (targetIndex < 0) targetIndex = SourceItems.Count;

                for (int i = originalIndices.Count - 1; i >= 0; i--)
                {
                    SourceItems.RemoveAt(originalIndices[i]);
                }

                int removedBeforeTarget = 0;
                for (int i = 0; i < originalIndices.Count; i++)
                {
                    if (originalIndices[i] < targetIndex)
                    {
                        removedBeforeTarget++;
                    }
                }

                targetIndex -= removedBeforeTarget;
                int placeholderIndex = GetPlaceholderIndex();
                if (placeholderIndex >= 0 && targetIndex > placeholderIndex)
                {
                    targetIndex = placeholderIndex;
                }

                if (targetIndex < 0) targetIndex = 0;

                for (int i = 0; i < moveList.Count; i++)
                {
                    SourceItems.Insert(targetIndex + i, moveList[i]);
                }
            }
            finally
            {
                _isRefreshing = false;
            }

            RefreshAll();
            RaiseDataChanged();
        }

        public void SortSourceItemsByEdited(bool descending)
        {
            if (SourceItems == null || SourceItems.Count <= 1) return;

            RecordHistory();
            _isRefreshing = true;
            try
            {
                var itemsWithIndex = SourceItems
                    .Select((item, index) => new { Item = item, OriginalIndex = index })
                    .ToList();

                var placeholders = itemsWithIndex.Where(x => x.Item.IsPlaceholder).ToList();
                var normalItems = itemsWithIndex.Where(x => !x.Item.IsPlaceholder).ToList();

                normalItems.Sort((a, b) =>
                {
                    bool aEdited = a.Item.IsEdited;
                    bool bEdited = b.Item.IsEdited;

                    if (aEdited != bEdited)
                    {
                        return descending ? (aEdited ? -1 : 1) : (aEdited ? 1 : -1);
                    }

                    return a.OriginalIndex.CompareTo(b.OriginalIndex);
                });

                var sortedList = normalItems.Concat(placeholders).Select(x => x.Item).ToList();
                for (int i = 0; i < sortedList.Count; i++)
                {
                    var targetItem = sortedList[i];
                    int currentIndex = SourceItems.IndexOf(targetItem);
                    if (currentIndex != i && currentIndex >= 0)
                    {
                        SourceItems.Move(currentIndex, i);
                    }
                }
            }
            finally
            {
                _isRefreshing = false;
            }

            RefreshAll();
            RaiseDataChanged();
        }

        public void SortSourceItemsByInput(bool sortByNormalize, bool descending)
        {
            if (SourceItems == null || SourceItems.Count <= 1) return;

            RecordHistory();
            _isRefreshing = true;
            try
            {
                var itemsWithIndex = SourceItems
                    .Select((item, index) => new { Item = item, OriginalIndex = index })
                    .ToList();

                var placeholders = itemsWithIndex.Where(x => x.Item.IsPlaceholder).ToList();
                var normalItems = itemsWithIndex.Where(x => !x.Item.IsPlaceholder).ToList();

                normalItems.Sort((a, b) =>
                {
                    string valA = sortByNormalize ? a.Item.NormalizedInput : a.Item.OriginalInput;
                    string valB = sortByNormalize ? b.Item.NormalizedInput : b.Item.OriginalInput;

                    int cmp = string.Compare(valA, valB, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0)
                    {
                        return descending ? -cmp : cmp;
                    }

                    return a.OriginalIndex.CompareTo(b.OriginalIndex);
                });

                var sortedList = normalItems.Concat(placeholders).Select(x => x.Item).ToList();
                for (int i = 0; i < sortedList.Count; i++)
                {
                    var targetItem = sortedList[i];
                    int currentIndex = SourceItems.IndexOf(targetItem);
                    if (currentIndex != i && currentIndex >= 0)
                    {
                        SourceItems.Move(currentIndex, i);
                    }
                }
            }
            finally
            {
                _isRefreshing = false;
            }

            RefreshAll();
            RaiseDataChanged();
        }

        public void ClearAllSourceItems()
        {
            if (IsMirrorTab || SourceItems.Count == 0) return;

            RecordHistory();
            SourceItems.Clear();
        }

        private void AddSourceLine(string line)
        {
            PathItem? placeholder = FindPlaceholderItem();
            if (placeholder != null)
            {
                placeholder.IsPlaceholder = false;
                placeholder.OriginalInput = line;
                return;
            }

            SourceItems.Add(new PathItem { OriginalInput = line });
        }

        private PathItem? FindPlaceholderItem()
        {
            for (int i = 0; i < SourceItems.Count; i++)
            {
                if (SourceItems[i].IsPlaceholder && SourceItems[i].IsEmpty)
                {
                    return SourceItems[i];
                }
            }
            return null;
        }

        private int GetPlaceholderIndex()
        {
            for (int i = 0; i < SourceItems.Count; i++)
            {
                if (SourceItems[i].IsPlaceholder)
                {
                    return i;
                }
            }
            return -1;
        }

        private void SourceItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    if (e.NewItems[i] is PathItem item)
                    {
                        item.PropertyChanged += PathItem_PropertyChanged;
                        if (!IsMirrorTab)
                        {
                            RefreshItem(item);
                        }
                    }
                }
            }

            if (e.OldItems != null)
            {
                for (int i = 0; i < e.OldItems.Count; i++)
                {
                    if (e.OldItems[i] is PathItem item)
                    {
                        item.PropertyChanged -= PathItem_PropertyChanged;
                    }
                }
            }

            if (_isRefreshing) return;

            if (!IsMirrorTab)
            {
                QueueInputPlaceholderRefresh();
                RefreshScript();
                RaiseDataChanged();
            }
            else
            {
                RefreshScript();
            }
        }

        private void PathItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isRefreshing) return;

            if (!IsMirrorTab && e.PropertyName == nameof(PathItem.OriginalInput))
            {
                if (sender is PathItem item)
                {
                    item.IsPlaceholder = false;
                    RefreshItem(item);
                    QueueInputPlaceholderRefresh();
                    RefreshScript();
                    RaiseDataChanged();
                }
                return;
            }

            if (e.PropertyName == nameof(PathItem.NormalizedInput) ||
                e.PropertyName == nameof(PathItem.MappedTarget) ||
                e.PropertyName == nameof(PathItem.EditOutputPath) ||
                e.PropertyName == nameof(PathItem.IsValid) ||
                e.PropertyName == nameof(PathItem.IsPlaceholder))
            {
                RefreshScript();
                if (e.PropertyName == nameof(PathItem.EditOutputPath))
                {
                    RaiseDataChanged();
                }
            }
        }

        public void RefreshAll()
        {
            if (IsMirrorTab && _masterTab != null)
            {
                _masterTab.RefreshAll();
                return;
            }

            _isRefreshing = true;
            try
            {
                for (int i = 0; i < SourceItems.Count; i++)
                {
                    RefreshItem(SourceItems[i]);
                }
            }
            finally
            {
                _isRefreshing = false;
            }

            EnsureInputPlaceholder();
            RefreshScript();
        }

        private void EnsureInputPlaceholder() => EnsureInputPlaceholderCore();

        private void QueueInputPlaceholderRefresh()
        {
            if (IsMirrorTab) return;
            if (_placeholderRefreshPending) return;

            _placeholderRefreshPending = true;

            Dispatcher.UIThread.Post(() =>
            {
                _placeholderRefreshPending = false;
                EnsureInputPlaceholderCore();
                RefreshScript();
                RaiseDataChanged();
            }, DispatcherPriority.Background);
        }

        private void EnsureInputPlaceholderCore()
        {
            if (IsMirrorTab) return;

            int nonEmptyCount = 0;
            for (int i = 0; i < SourceItems.Count; i++)
            {
                if (!SourceItems[i].IsEmpty) nonEmptyCount++;
            }

            if (nonEmptyCount >= 1)
            {
                for (int i = SourceItems.Count - 1; i >= 0; i--)
                {
                    if (SourceItems[i].IsEmpty)
                    {
                        SourceItems.RemoveAt(i);
                    }
                }
            }
            else
            {
                PathItem? lastEmpty = null;
                for (int i = 0; i < SourceItems.Count; i++)
                {
                    if (SourceItems[i].IsEmpty)
                    {
                        lastEmpty = SourceItems[i];
                    }
                }

                for (int i = SourceItems.Count - 1; i >= 0; i--)
                {
                    if (SourceItems[i].IsEmpty && !ReferenceEquals(SourceItems[i], lastEmpty))
                    {
                        SourceItems.RemoveAt(i);
                    }
                }

                if (lastEmpty == null)
                {
                    SourceItems.Add(new PathItem { IsPlaceholder = true });
                }
                else
                {
                    lastEmpty.IsPlaceholder = true;
                    RefreshItem(lastEmpty);
                }
            }
        }

        private void RefreshItem(PathItem item)
        {
            string original = CleanInput(item.OriginalInput);
            if (original.Length == 0)
            {
                item.NormalizedInput = string.Empty;
                item.MappedTarget = string.Empty;
                item.IsValid = false;
                item.ErrorMessage = "Path trống.";
                return;
            }

            try
            {
                MappingInfo info = BuildMappingInfo(original);
                item.NormalizedInput = info.NormalizedSource;
                item.MappedTarget = info.MappedTarget;
                item.IsValid = true;
                item.ErrorMessage = string.Empty;
            }
            catch (Exception ex)
            {
                item.NormalizedInput = original;
                item.MappedTarget = string.Empty;
                item.IsValid = false;
                item.ErrorMessage = "Path lỗi format: " + ex.Message;
            }
        }

        private MappingInfo BuildMappingInfo(string original)
        {
            string normalized = NormalizeSource(original);
            string expanded = Environment.ExpandEnvironmentVariables(normalized);

            string fullPath = Path.GetFullPath(expanded);
            string relative = GetRelativeForTarget(normalized, expanded, fullPath);
            string mappedTarget = CombineTarget(RootTargetPath, relative);

            return new MappingInfo(normalized, mappedTarget);
        }

        private string NormalizeSource(string original)
        {
            string path = CleanInput(original);
            if (path.Length == 0) return path;

            string programFilesX86 = @"C:\Program Files (x86)\";
            string programFiles = @"C:\Program Files\";
            string programData = @"C:\ProgramData\";
            string? systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrWhiteSpace(systemDrive)) systemDrive = "C:";

            if (TryNormalizeUserProfile(path, out var remainder))
            {
                return remainder;
            }

            if (TryNormalizeWithPrefix(path, programFilesX86, "%programfiles(x86)%", out remainder))
            {
                return remainder;
            }

            if (TryNormalizeWithPrefix(path, programFiles, "%programw6432%", out remainder))
            {
                return remainder;
            }

            if (TryNormalizeWithPrefix(path, programData, "%programdata%", out remainder))
            {
                return remainder;
            }

            if (path.Length >= 3 &&
                path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/') &&
                string.Equals(path.Substring(0, 2), systemDrive, StringComparison.OrdinalIgnoreCase))
            {
                remainder = path.Substring(3);
                if (remainder.Length == 0)
                {
                    return "%systemdrive%\\";
                }
                return "%systemdrive%\\" + remainder;
            }

            return path;
        }

        private static bool TryNormalizeUserProfile(string path, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return false;

            string prefix = @"C:\Users\";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            int nextSeparator = path.IndexOf('\\', prefix.Length);
            if (nextSeparator < 0)
            {
                normalized = "%userprofile%";
                return true;
            }

            string remainder = path.Substring(nextSeparator + 1);
            if (remainder.Length == 0)
            {
                normalized = "%userprofile%";
                return true;
            }

            normalized = "%userprofile%\\" + remainder;
            return true;
        }

        private string GetRelativeForTarget(string normalized, string expanded, string fullPath)
        {
            if (TryStripNormalizedPrefix(normalized, "%userprofile%", out var userRelative))
            {
                return userRelative;
            }

            if (TryStripNormalizedPrefix(normalized, "%programfiles(x86)%", out var pf86Relative))
            {
                return Path.Combine("Program Files (x86)", pf86Relative);
            }

            if (TryStripNormalizedPrefix(normalized, "%programw6432%", out var pfRelative))
            {
                return Path.Combine("Program Files", pfRelative);
            }

            if (TryStripNormalizedPrefix(normalized, "%programdata%", out var programDataRelative))
            {
                return Path.Combine("ProgramData", programDataRelative);
            }

            if (TryStripNormalizedPrefix(normalized, "%systemdrive%", out var systemRelative))
            {
                return systemRelative;
            }

            if (!Path.IsPathRooted(expanded))
            {
                return normalized;
            }

            string? root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) && fullPath.Length > root.Length)
            {
                return fullPath.Substring(root.Length);
            }

            return normalized;
        }

        private string CombineTarget(string root, string relative)
        {
            string cleanRelative = CleanInput(relative).TrimStart('\\', '/');
            if (cleanRelative.Length == 0) return root;
            return Path.Combine(root, cleanRelative);
        }

        private static bool TryNormalizeWithPrefix(string path, string prefix, string normalizedPrefix, out string normalized)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string remainder = path.Substring(prefix.Length);
                normalized = string.IsNullOrEmpty(remainder)
                    ? normalizedPrefix + "\\"
                    : normalizedPrefix + "\\" + remainder;
                return true;
            }

            normalized = string.Empty;
            return false;
        }

        private static bool TryStripNormalizedPrefix(string path, string normalizedPrefix, out string remainder)
        {
            string prefixWithSlash = normalizedPrefix.EndsWith("\\", StringComparison.Ordinal)
                ? normalizedPrefix
                : normalizedPrefix + "\\";

            if (path.StartsWith(prefixWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                remainder = path.Substring(prefixWithSlash.Length);
                return true;
            }

            if (string.Equals(path, normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                remainder = string.Empty;
                return true;
            }

            remainder = string.Empty;
            return false;
        }

        private bool CanGenerateCommand(PathItem item)
        {
            if (item == null || item.IsPlaceholder || !item.IsValid) return false;
            if (string.IsNullOrWhiteSpace(item.NormalizedInput)) return false;

            if (_scriptMode != ScriptMode.Delete &&
                _scriptMode != ScriptMode.ReverseDelete &&
                string.IsNullOrWhiteSpace(item.EffectiveOutputPath))
            {
                return false;
            }

            return true;
        }

        private string BuildCommand(PathItem item)
        {
            if (_scriptMode == ScriptMode.Copy)
            {
                string outputPath = item.EffectiveOutputPath;
                string command = "if not exist " + Quote(outputPath) + " mkdir " + Quote(outputPath) + Environment.NewLine +
                                 "xcopy " + Quote(item.NormalizedInput) + " " + Quote(outputPath) + " /E /I /Y";
                return command;
            }

            if (_scriptMode == ScriptMode.ReverseCopy)
            {
                return "xcopy " + Quote(item.EffectiveOutputPath) + " " + Quote(item.NormalizedInput) + " /E /I /Y";
            }

            if (_scriptMode == ScriptMode.Delete)
            {
                return "rmdir /s /q " + Quote(item.NormalizedInput);
            }

            if (_scriptMode == ScriptMode.ReverseDelete)
            {
                return "rmdir /s /q " + Quote(item.NormalizedInput);
            }

            // MKLINK D
            return "mklink /D " + Quote(item.NormalizedInput) + " " + Quote(item.EffectiveOutputPath);
        }

        private static string Quote(string value) => "\"" + (value ?? string.Empty) + "\"";

        private void RaiseDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);

        private static string CleanInput(string? value)
        {
            if (value == null) return string.Empty;
            return value.Trim().Trim('"');
        }

        private void RecordHistory()
        {
            if (_owner != null && !_owner.IsRestoringHistory)
            {
                _owner.CaptureUndoSnapshot();
            }
        }

        private bool CanUndo() => _owner != null && _owner.CanUndo;

        private bool CanRedo() => _owner != null && _owner.CanRedo;

        private void Undo() => _owner?.Undo();

        private void Redo() => _owner?.Redo();
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using MkLink.Models;

namespace MkLink.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private bool _isWordWrapEnabled = true;
        private readonly Stack<MarkdownDocumentState> _undoStack = new Stack<MarkdownDocumentState>();
        private readonly Stack<MarkdownDocumentState> _redoStack = new Stack<MarkdownDocumentState>();
        private bool _isRestoringHistory;

        public MainViewModel()
        {
            var copyItems = new ObservableCollection<PathItem>();

            CopyTab = new PathTabViewModel(this, "COPY", ScriptMode.Copy, copyItems, false, null);
            DeleteTab = new PathTabViewModel(this, "DELETE", ScriptMode.Delete, copyItems, true, CopyTab);
            MklinkTab = new PathTabViewModel(this, "MKLINK D", ScriptMode.MklinkD, copyItems, true, CopyTab);
            CopyReverseTab = new PathTabViewModel(this, "COPY REVERSE", ScriptMode.ReverseCopy, copyItems, true, CopyTab);
            DeleteReverseTab = new PathTabViewModel(this, "DELETE REVERSE", ScriptMode.ReverseDelete, copyItems, true, CopyTab);

            CopyTab.DataChanged += CopyTab_DataChanged;
            DeleteTab.RefreshScript();
            MklinkTab.RefreshScript();
            CopyReverseTab.RefreshScript();
            DeleteReverseTab.RefreshScript();
            NotifyCombinedScriptsChanged();
        }

        public PathTabViewModel CopyTab { get; }
        public PathTabViewModel DeleteTab { get; }
        public PathTabViewModel MklinkTab { get; }
        public PathTabViewModel CopyReverseTab { get; }
        public PathTabViewModel DeleteReverseTab { get; }

        public string CombinedCopyDeleteMklinkScript
        {
            get
            {
                var builder = new StringBuilder();
                if (CopyTab != null && !string.IsNullOrWhiteSpace(CopyTab.ScriptResult))
                {
                    builder.AppendLine("echo ===== [COPY] =====");
                    builder.AppendLine(CopyTab.ScriptResult);
                }
                if (DeleteTab != null && !string.IsNullOrWhiteSpace(DeleteTab.ScriptResult))
                {
                    if (builder.Length > 0) builder.AppendLine();
                    builder.AppendLine("echo ===== [DELETE] =====");
                    builder.AppendLine(DeleteTab.ScriptResult);
                }
                if (MklinkTab != null && !string.IsNullOrWhiteSpace(MklinkTab.ScriptResult))
                {
                    if (builder.Length > 0) builder.AppendLine();
                    builder.AppendLine("echo ===== [MKLINK D] =====");
                    builder.AppendLine(MklinkTab.ScriptResult);
                }
                return builder.ToString();
            }
        }

        public string CombinedDeleteAndCopyReverseScript
        {
            get
            {
                var builder = new StringBuilder();
                if (DeleteReverseTab != null && !string.IsNullOrWhiteSpace(DeleteReverseTab.ScriptResult))
                {
                    builder.AppendLine("echo ===== [DELETE REVERSE] =====");
                    builder.AppendLine(DeleteReverseTab.ScriptResult);
                }
                if (CopyReverseTab != null && !string.IsNullOrWhiteSpace(CopyReverseTab.ScriptResult))
                {
                    if (builder.Length > 0) builder.AppendLine();
                    builder.AppendLine("echo ===== [COPY REVERSE] =====");
                    builder.AppendLine(CopyReverseTab.ScriptResult);
                }
                if (DeleteReverseTab != null && !string.IsNullOrWhiteSpace(DeleteReverseTab.ScriptResult))
                {
                    if (builder.Length > 0) builder.AppendLine();
                    builder.AppendLine("echo ===== [DELETE REVERSE] =====");
                    builder.AppendLine(DeleteReverseTab.ScriptResult);
                }
                return builder.ToString();
            }
        }

        private void NotifyCombinedScriptsChanged()
        {
            OnPropertyChanged(nameof(CombinedCopyDeleteMklinkScript));
            OnPropertyChanged(nameof(CombinedDeleteAndCopyReverseScript));
        }

        public bool CanUndo => _undoStack.Count > 0;

        public bool CanRedo => _redoStack.Count > 0;

        public bool IsWordWrapEnabled
        {
            get => _isWordWrapEnabled;
            set
            {
                if (_isWordWrapEnabled != value)
                {
                    _isWordWrapEnabled = value;
                    OnPropertyChanged();
                    CopyTab.NotifyDisplayOptionsChanged();
                    DeleteTab.NotifyDisplayOptionsChanged();
                    MklinkTab.NotifyDisplayOptionsChanged();
                    CopyReverseTab.NotifyDisplayOptionsChanged();
                    DeleteReverseTab.NotifyDisplayOptionsChanged();
                }
            }
        }

        private void CopyTab_DataChanged(object? sender, EventArgs e)
        {
            DeleteTab.NotifyMirrorStateChanged();
            MklinkTab.NotifyMirrorStateChanged();
            CopyReverseTab.NotifyMirrorStateChanged();
            DeleteReverseTab.NotifyMirrorStateChanged();
            DeleteTab.RefreshScript();
            MklinkTab.RefreshScript();
            CopyReverseTab.RefreshScript();
            DeleteReverseTab.RefreshScript();
            NotifyCombinedScriptsChanged();
        }

        public string ExportMarkdown() => MarkdownStateSerializer.Serialize(this);

        public void ImportMarkdown(string markdown)
        {
            var state = MarkdownStateSerializer.Deserialize(markdown);
            CopyTab.LoadDocumentState(state);
            IsWordWrapEnabled = state.IsWordWrapEnabled;
            DeleteTab.NotifyMirrorStateChanged();
            MklinkTab.NotifyMirrorStateChanged();
            CopyReverseTab.NotifyMirrorStateChanged();
            DeleteReverseTab.NotifyMirrorStateChanged();
            DeleteTab.RefreshScript();
            MklinkTab.RefreshScript();
            CopyReverseTab.RefreshScript();
            DeleteReverseTab.RefreshScript();
            NotifyCombinedScriptsChanged();
        }

        public void CaptureUndoSnapshot()
        {
            if (_isRestoringHistory) return;

            _undoStack.Push(CaptureCurrentState());
            _redoStack.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;

            _redoStack.Push(CaptureCurrentState());
            RestoreState(_undoStack.Pop());
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;

            _undoStack.Push(CaptureCurrentState());
            RestoreState(_redoStack.Pop());
        }

        private MarkdownDocumentState CaptureCurrentState()
        {
            var state = CopyTab.CaptureDocumentState();
            state.IsWordWrapEnabled = _isWordWrapEnabled;
            return state;
        }

        private void RestoreState(MarkdownDocumentState state)
        {
            if (state == null) return;

            _isRestoringHistory = true;
            try
            {
                CopyTab.LoadDocumentState(state);
                IsWordWrapEnabled = state.IsWordWrapEnabled;
            }
            finally
            {
                _isRestoringHistory = false;
            }

            DeleteTab.NotifyMirrorStateChanged();
            MklinkTab.NotifyMirrorStateChanged();
            CopyReverseTab.NotifyMirrorStateChanged();
            DeleteReverseTab.NotifyMirrorStateChanged();
            DeleteTab.RefreshScript();
            MklinkTab.RefreshScript();
            CopyReverseTab.RefreshScript();
            DeleteReverseTab.RefreshScript();
            NotifyCombinedScriptsChanged();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        internal bool IsRestoringHistory => _isRestoringHistory;
    }

    public static class MarkdownStateSerializer
    {
        private const string Heading = "# MKLink Path Mapper";

        private static string EscapePipe(string? text)
        {
            if (text == null) return string.Empty;
            return text.Replace("|", "\\|");
        }

        private static string FormatPathForTable(string? path)
        {
            if (path == null) return string.Empty;
            string result = path.Replace("\\", "\\\\<wbr>").Replace("/", "/<wbr>");
            return EscapePipe(result);
        }

        private static string CleanTablePath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string cleaned = path
                .Replace("<wbr>", "")
                .Replace("<wbr />", "")
                .Replace("<WBR>", "")
                .Replace("<WBR />", "")
                .Replace("&#8203;", "");

            if (cleaned.StartsWith("\\\\"))
            {
                return "\\\\" + cleaned.Substring(2).Replace("\\\\", "\\");
            }
            return cleaned.Replace("\\\\", "\\");
        }

        public static string Serialize(MainViewModel viewModel)
        {
            if (viewModel == null) return string.Empty;

            var builder = new StringBuilder();
            builder.AppendLine(Heading);
            builder.AppendLine();
            builder.AppendLine("<style>");
            builder.AppendLine("  table {");
            builder.AppendLine("    width: 100%;");
            builder.AppendLine("    table-layout: fixed;");
            builder.AppendLine("  }");
            builder.AppendLine("  td, th {");
            builder.AppendLine("    word-break: break-all;");
            builder.AppendLine("    word-wrap: break-word;");
            builder.AppendLine("  }");
            builder.AppendLine("</style>");
            builder.AppendLine();
            builder.AppendLine("- SelectedRootFolder: " + viewModel.CopyTab.SelectedRootFolder);
            builder.AppendLine("- SubfolderName: " + viewModel.CopyTab.SubfolderName);
            builder.AppendLine("- RootTargetPath: " + viewModel.CopyTab.RootTargetPath);
            builder.AppendLine("- IsWordWrapEnabled: " + viewModel.IsWordWrapEnabled);
            builder.AppendLine();

            var validItems = new List<PathItem>();
            for (int i = 0; i < viewModel.CopyTab.SourceItems.Count; i++)
            {
                PathItem item = viewModel.CopyTab.SourceItems[i];
                if (item != null && !item.IsPlaceholder && !string.IsNullOrWhiteSpace(item.OriginalInput))
                {
                    validItems.Add(item);
                }
            }

            var sortedItems = validItems.OrderByDescending(item => !string.IsNullOrWhiteSpace(item.EditOutputPath)).ToList();

            builder.AppendLine("## INPUT");
            builder.AppendLine();
            builder.AppendLine("| Input | Normalize Input |");
            builder.AppendLine("| --- | --- |");
            for (int i = 0; i < sortedItems.Count; i++)
            {
                PathItem item = sortedItems[i];
                builder.AppendLine(string.Format("| {0} | {1} |",
                    FormatPathForTable(item.OriginalInput),
                    FormatPathForTable(item.NormalizedInput)));
            }
            builder.AppendLine();

            builder.AppendLine("## OUTPUT");
            builder.AppendLine();
            builder.AppendLine("| Output | Edit Output |");
            builder.AppendLine("| --- | --- |");
            for (int i = 0; i < sortedItems.Count; i++)
            {
                PathItem item = sortedItems[i];
                builder.AppendLine(string.Format("| {0} | {1} |",
                    FormatPathForTable(item.MappedTarget),
                    FormatPathForTable(item.EditOutputPath)));
            }

            return builder.ToString();
        }

        public static MarkdownDocumentState Deserialize(string markdown)
        {
            var state = new MarkdownDocumentState
            {
                InputLines = new List<string>(),
                PathRows = new List<MarkdownPathRowState>()
            };

            if (string.IsNullOrWhiteSpace(markdown)) return state;

            var inputRows = new List<Tuple<string, string>>();
            var outputRows = new List<Tuple<string, string>>();

            using (var reader = new StringReader(markdown))
            {
                string? line;
                bool inCodeBlock = false;
                bool inRowBlock = false;
                bool inInputBlock = false;
                bool inOutputBlock = false;

                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("- SelectedRootFolder:", StringComparison.OrdinalIgnoreCase))
                    {
                        state.SelectedRootFolder = UnwrapValue(trimmed.Substring("- SelectedRootFolder:".Length));
                        continue;
                    }

                    if (trimmed.StartsWith("- SubfolderName:", StringComparison.OrdinalIgnoreCase))
                    {
                        state.SubfolderName = UnwrapValue(trimmed.Substring("- SubfolderName:".Length));
                        continue;
                    }

                    if (trimmed.StartsWith("- RootTargetPath:", StringComparison.OrdinalIgnoreCase))
                    {
                        state.RootTargetPath = UnwrapValue(trimmed.Substring("- RootTargetPath:".Length));
                        continue;
                    }

                    if (trimmed.StartsWith("- IsWordWrapEnabled:", StringComparison.OrdinalIgnoreCase))
                    {
                        state.IsWordWrapEnabled = ParseBool(UnwrapValue(trimmed.Substring("- IsWordWrapEnabled:".Length)));
                        continue;
                    }

                    if (trimmed == "## COPY ROWS")
                    {
                        inRowBlock = true;
                        inInputBlock = false;
                        inOutputBlock = false;
                        continue;
                    }

                    if (trimmed == "## INPUT")
                    {
                        inRowBlock = false;
                        inInputBlock = true;
                        inOutputBlock = false;
                        continue;
                    }

                    if (trimmed == "## OUTPUT")
                    {
                        inRowBlock = false;
                        inInputBlock = false;
                        inOutputBlock = true;
                        continue;
                    }

                    if (trimmed == "```paths")
                    {
                        inCodeBlock = true;
                        continue;
                    }

                    if (trimmed == "```")
                    {
                        if (inCodeBlock) inCodeBlock = false;
                        continue;
                    }

                    if (inCodeBlock && trimmed.Length > 0)
                    {
                        if (inRowBlock)
                        {
                            string[] parts = line.Split(new[] { '\t' }, 4);
                            state.PathRows.Add(new MarkdownPathRowState
                            {
                                OriginalInput = parts.Length > 0 ? parts[0] : string.Empty,
                                NormalizedInput = parts.Length > 1 ? parts[1] : string.Empty,
                                OutputPath = parts.Length > 2 ? parts[2] : string.Empty,
                                EditOutputPath = parts.Length > 3 ? parts[3] : string.Empty
                            });
                        }
                        else
                        {
                            state.InputLines.Add(line);
                        }
                        continue;
                    }

                    if (trimmed.StartsWith("|") && trimmed.EndsWith("|"))
                    {
                        if (trimmed.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            trimmed.IndexOf("Output", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            trimmed.IndexOf("Original Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            trimmed.IndexOf("---") >= 0)
                        {
                            continue;
                        }

                        string temp = trimmed.Replace("\\|", "\x01");
                        string[] rawParts = temp.Split('|');

                        if (inRowBlock)
                        {
                            if (rawParts.Length >= 5)
                            {
                                var parts = new List<string>();
                                for (int i = 1; i < rawParts.Length - 1; i++)
                                {
                                    string val = rawParts[i].Trim().Replace("\x01", "|");
                                    parts.Add(CleanTablePath(val));
                                }

                                state.PathRows.Add(new MarkdownPathRowState
                                {
                                    OriginalInput = parts.Count > 0 ? parts[0] : string.Empty,
                                    NormalizedInput = parts.Count > 1 ? parts[1] : string.Empty,
                                    OutputPath = parts.Count > 2 ? parts[2] : string.Empty,
                                    EditOutputPath = parts.Count > 3 ? parts[3] : string.Empty
                                });
                            }
                        }
                        else if (inInputBlock)
                        {
                            if (rawParts.Length >= 3)
                            {
                                string originalInput = CleanTablePath(rawParts[1].Trim().Replace("\x01", "|"));
                                string normalizedInput = CleanTablePath(rawParts[2].Trim().Replace("\x01", "|"));
                                inputRows.Add(Tuple.Create(originalInput, normalizedInput));
                            }
                        }
                        else if (inOutputBlock)
                        {
                            if (rawParts.Length >= 3)
                            {
                                string outputPath = CleanTablePath(rawParts[1].Trim().Replace("\x01", "|"));
                                string editOutputPath = CleanTablePath(rawParts[2].Trim().Replace("\x01", "|"));
                                outputRows.Add(Tuple.Create(outputPath, editOutputPath));
                            }
                        }
                    }
                }
            }

            if (inputRows.Count > 0 || outputRows.Count > 0)
            {
                int count = Math.Max(inputRows.Count, outputRows.Count);
                for (int i = 0; i < count; i++)
                {
                    var inputRow = i < inputRows.Count ? inputRows[i] : Tuple.Create(string.Empty, string.Empty);
                    var outputRow = i < outputRows.Count ? outputRows[i] : Tuple.Create(string.Empty, string.Empty);

                    state.PathRows.Add(new MarkdownPathRowState
                    {
                        OriginalInput = inputRow.Item1,
                        NormalizedInput = inputRow.Item2,
                        OutputPath = outputRow.Item1,
                        EditOutputPath = outputRow.Item2
                    });
                }
            }

            return state;
        }

        private static string UnwrapValue(string? value)
        {
            if (value == null) return string.Empty;
            string result = value.Trim();
            if (result.StartsWith("`", StringComparison.Ordinal) && result.EndsWith("`", StringComparison.Ordinal) && result.Length >= 2)
            {
                result = result.Substring(1, result.Length - 2);
            }
            return result;
        }

        private static bool ParseBool(string value)
        {
            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }
            return true;
        }
    }
}

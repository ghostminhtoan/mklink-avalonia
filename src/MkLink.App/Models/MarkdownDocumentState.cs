using System.Collections.Generic;

namespace MkLink.Models
{
    public class MarkdownDocumentState
    {
        public string SelectedRootFolder { get; set; } = @"D:\";

        public string SubfolderName { get; set; } = "USERS";

        public string RootTargetPath { get; set; } = @"D:\USERS";

        public bool IsWordWrapEnabled { get; set; } = true;

        public List<string> InputLines { get; set; } = new List<string>();

        public List<MarkdownPathRowState> PathRows { get; set; } = new List<MarkdownPathRowState>();
    }

    public class MarkdownPathRowState
    {
        public string OriginalInput { get; set; } = string.Empty;

        public string NormalizedInput { get; set; } = string.Empty;

        public string OutputPath { get; set; } = string.Empty;

        public string EditOutputPath { get; set; } = string.Empty;
    }
}

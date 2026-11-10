using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace MkLink.Models
{
    public class MklinkCheckEntry : INotifyPropertyChanged
    {
        private bool _isSelected;

        public MklinkCheckEntry(
            int sourceIndex,
            string normalizedInput,
            string outputPath,
            bool exists,
            bool isLink,
            bool destinationCheck,
            string alreadyMklinkTo,
            string status)
        {
            SourceIndex = sourceIndex;
            NormalizedInput = normalizedInput ?? string.Empty;
            OutputPath = outputPath ?? string.Empty;
            Exists = exists;
            IsLink = isLink;
            DestinationCheck = destinationCheck;
            AlreadyMklinkTo = alreadyMklinkTo ?? string.Empty;
            Status = status ?? string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int SourceIndex { get; }

        public string NormalizedInput { get; }

        public string OutputPath { get; }

        public bool Exists { get; }

        public bool IsLink { get; }

        public bool DestinationCheck { get; }

        public string AlreadyMklinkTo { get; }

        public string Status { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusColorHex
        {
            get
            {
                if (string.Equals(Status, "MKLINK", StringComparison.OrdinalIgnoreCase))
                {
                    return "#00E5FF"; // Cyan
                }

                if (string.Equals(Status, "Real folder", StringComparison.OrdinalIgnoreCase))
                {
                    return "#FFD700"; // Gold
                }

                if (string.Equals(Status, "Missing", StringComparison.OrdinalIgnoreCase) || !Exists)
                {
                    return "#FF6B6B"; // Soft red
                }

                return "#E0E0E0"; // Normal text
            }
        }

        public bool IsMatched
        {
            get
            {
                if (string.IsNullOrEmpty(OutputPath) || string.IsNullOrEmpty(AlreadyMklinkTo))
                {
                    return false;
                }

                string p1 = OutputPath;
                try
                {
                    p1 = Environment.ExpandEnvironmentVariables(p1);
                    p1 = Path.GetFullPath(p1);
                }
                catch
                {
                }

                string p2 = AlreadyMklinkTo;
                try
                {
                    p2 = Environment.ExpandEnvironmentVariables(p2);
                    p2 = Path.GetFullPath(p2);
                }
                catch
                {
                }

                p1 = p1.Replace('/', '\\').TrimEnd('\\');
                p2 = p2.Replace('/', '\\').TrimEnd('\\');

                return string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase);
            }
        }

        public string MatchedText => IsMatched ? "Khớp" : (IsLink ? "Khác đích" : "-");

        public string DestinationCheckText => DestinationCheck ? "Tồn tại" : "Chưa có";

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MkLink.Models
{
    public class PathItem : INotifyPropertyChanged
    {
        private string _originalInput = string.Empty;
        private string _normalizedInput = string.Empty;
        private string _mappedTarget = string.Empty;
        private string _editOutputPath = string.Empty;
        private bool _isValid;
        private bool _isPlaceholder;
        private string _errorMessage = string.Empty;

        public string OriginalInput
        {
            get => _originalInput;
            set
            {
                if (_originalInput != value)
                {
                    _originalInput = value ?? string.Empty;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }

        public string NormalizedInput
        {
            get => _normalizedInput;
            set
            {
                if (_normalizedInput != value)
                {
                    _normalizedInput = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public string MappedTarget
        {
            get => _mappedTarget;
            set
            {
                if (_mappedTarget != value)
                {
                    _mappedTarget = value ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(_editOutputPath) &&
                        string.Equals(_editOutputPath, _mappedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        _editOutputPath = string.Empty;
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EditOutputPath));
                    OnPropertyChanged(nameof(IsEdited));
                    OnPropertyChanged(nameof(EditOutputStateText));
                    OnPropertyChanged(nameof(EffectiveOutputPath));
                }
            }
        }

        public string EditOutputPath
        {
            get => _editOutputPath;
            set
            {
                string normalized = NormalizeEditOutputPath(value);
                if (_editOutputPath != normalized)
                {
                    _editOutputPath = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsEdited));
                    OnPropertyChanged(nameof(EditOutputStateText));
                    OnPropertyChanged(nameof(EffectiveOutputPath));
                }
            }
        }

        public string EffectiveOutputPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_editOutputPath))
                {
                    return _editOutputPath;
                }
                return _mappedTarget;
            }
        }

        public bool IsEdited => !string.IsNullOrWhiteSpace(_editOutputPath);

        public string EditOutputStateText => IsEdited ? "Edited" : "Default";

        public bool IsValid
        {
            get => _isValid;
            set
            {
                if (_isValid != value)
                {
                    _isValid = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowValidationError));
                }
            }
        }

        public bool IsPlaceholder
        {
            get => _isPlaceholder;
            set
            {
                if (_isPlaceholder != value)
                {
                    _isPlaceholder = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowValidationError));
                }
            }
        }

        public bool IsEmpty => string.IsNullOrWhiteSpace(OriginalInput);

        public bool ShowValidationError => !IsPlaceholder && !IsValid;

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        private string NormalizeEditOutputPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string cleaned = value.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(_mappedTarget) &&
                string.Equals(cleaned, _mappedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return cleaned;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

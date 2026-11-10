using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MkLink.Views
{
    public enum SaveDialogAction
    {
        Overwrite,
        SaveAs,
        Cancel
    }

    public partial class SaveConfirmDialog : Window
    {
        public SaveDialogAction SelectedAction { get; private set; } = SaveDialogAction.Cancel;

        public SaveConfirmDialog()
        {
            InitializeComponent();
        }

        public SaveConfirmDialog(string filePath) : this()
        {
            FilePathTextBlock.Text = filePath;
        }

        private void OverwriteButton_Click(object? sender, RoutedEventArgs e)
        {
            SelectedAction = SaveDialogAction.Overwrite;
            Close(true);
        }

        private void SaveAsButton_Click(object? sender, RoutedEventArgs e)
        {
            SelectedAction = SaveDialogAction.SaveAs;
            Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            SelectedAction = SaveDialogAction.Cancel;
            Close(false);
        }
    }
}

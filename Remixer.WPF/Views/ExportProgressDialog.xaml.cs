using System;
using System.Windows;
using System.Windows.Threading;

namespace Remixer.WPF.Views;

public partial class ExportProgressDialog : Window
{
    public ExportProgressDialog()
    {
        InitializeComponent();
    }

    public void UpdateProgress(int percent, string message)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Value = Math.Max(0, Math.Min(100, percent));
            StatusText.Text = message ?? "Exporting...";
        });
    }

    public void SetComplete()
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Value = 100;
            StatusText.Text = "Export complete!";
        });
    }

    public void SetError(string errorMessage)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = $"Error: {errorMessage}";
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
        });
    }
}


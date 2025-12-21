using System.Windows;
using Microsoft.Win32;

namespace Remixer.WPF.Views;

public partial class ExportDialog : Window
{
    public int SampleRate { get; private set; } = 44100;
    public int Channels { get; private set; } = 2;
    public int BitsPerSample { get; private set; } = 16;
    public string OutputPath { get; private set; } = string.Empty;

    private readonly string _defaultFileName;

    public ExportDialog(string defaultFileName)
    {
        InitializeComponent();
        _defaultFileName = defaultFileName;
        OutputFileTextBox.Text = defaultFileName;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "WAV Files|*.wav|All Files|*.*",
            Title = "Export Remixed Audio",
            FileName = _defaultFileName
        };

        if (dialog.ShowDialog() == true)
        {
            OutputFileTextBox.Text = dialog.FileName;
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        OutputPath = OutputFileTextBox.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            MessageBox.Show("Please select an output file.", "Validation Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Parse sample rate
        SampleRate = SampleRateComboBox.SelectedIndex switch
        {
            0 => 22050,
            1 => 44100,
            2 => 48000,
            3 => 96000,
            _ => 44100
        };

        // Parse channels
        Channels = ChannelsComboBox.SelectedIndex switch
        {
            0 => 1,
            1 => 2,
            _ => 2
        };

        // Parse bit depth
        BitsPerSample = BitDepthComboBox.SelectedIndex switch
        {
            0 => 16,
            1 => 24,
            2 => 32,
            _ => 16
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}




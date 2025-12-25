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
        FormatComboBox.SelectionChanged += FormatComboBox_SelectionChanged;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        // Get selected format
        var selectedFormat = FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
        var extension = selectedFormat?.Tag?.ToString() ?? ".wav";
        
        var filter = extension switch
        {
            ".wav" => "WAV Files|*.wav|All Files|*.*",
            ".mp3" => "MP3 Files|*.mp3|All Files|*.*",
            ".m4a" => "AAC Files|*.m4a|All Files|*.*",
            ".wma" => "WMA Files|*.wma|All Files|*.*",
            _ => "All Files|*.*"
        };
        
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = "Export Remixed Audio",
            FileName = System.IO.Path.ChangeExtension(_defaultFileName, extension)
        };

        if (dialog.ShowDialog() == true)
        {
            var selectedPath = dialog.FileName;
            // Ensure correct extension
            if (!selectedPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                selectedPath = System.IO.Path.ChangeExtension(selectedPath, extension);
            }
            OutputFileTextBox.Text = selectedPath;
        }
    }
    
    private void FormatComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Update file extension when format changes
        if (FormatComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string extension)
        {
            var currentPath = OutputFileTextBox.Text;
            if (!string.IsNullOrEmpty(currentPath))
            {
                OutputFileTextBox.Text = System.IO.Path.ChangeExtension(currentPath, extension);
            }
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




using System.Windows;

namespace Remixer.WPF.Views;

public partial class SavePresetDialog : Window
{
    public string PresetName { get; private set; } = string.Empty;
    public string PresetDescription { get; private set; } = string.Empty;

    public SavePresetDialog()
    {
        InitializeComponent();
        PresetNameTextBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        PresetName = PresetNameTextBox.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(PresetName))
        {
            MessageBox.Show("Please enter a preset name.", "Validation Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            PresetNameTextBox.Focus();
            return;
        }

        PresetDescription = DescriptionTextBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}




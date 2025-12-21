using Remixer.WPF.ViewModels;
using System.Windows;

namespace Remixer.WPF.Views;

/// <summary>
/// Interaction logic for SettingsWindow.xaml
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    public SettingsWindow()
    {
        InitializeComponent();
        ViewModel.SettingsSaved += (s, e) => 
        {
            DialogResult = true;
            Close();
        };
        ViewModel.SettingsCancelled += (s, e) => 
        {
            DialogResult = false;
            Close();
        };
        
        // Load existing API key into password box if available
        if (!string.IsNullOrEmpty(ViewModel.ApiKey))
        {
            ApiKeyPasswordBox.Password = ViewModel.ApiKey;
        }
    }

    private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && sender is System.Windows.Controls.PasswordBox passwordBox)
        {
            ViewModel.ApiKey = passwordBox.Password;
        }
    }
}


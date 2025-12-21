using System;
using System.Windows;

namespace Remixer.WPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            // Ensure window is visible
            this.Visibility = Visibility.Visible;
            this.WindowState = WindowState.Normal;
            this.Show();
            this.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing window: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", 
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }
}

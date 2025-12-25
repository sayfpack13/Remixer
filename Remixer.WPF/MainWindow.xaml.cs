using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Remixer.WPF.ViewModels;

namespace Remixer.WPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _isDraggingSeekBar = false;
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            // Ensure window is visible and maximized
            this.Visibility = Visibility.Visible;
            this.WindowState = WindowState.Maximized;
            this.Show();
            this.Activate();

            // Handle window closing to dispose ViewModel
            this.Closed += MainWindow_Closed;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing window: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        // Dispose ViewModel when window closes
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }

    private void SeekBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider && DataContext is MainViewModel viewModel && viewModel.IsAudioLoaded)
        {
            _isDraggingSeekBar = true;
            viewModel.SetUserDraggingSlider(true);
            // Update visual position immediately when clicked
            var percentage = e.GetPosition(slider).X / slider.ActualWidth;
            var value = percentage * (slider.Maximum - slider.Minimum) + slider.Minimum;
            value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
            slider.Value = value;
            slider.CaptureMouse();
            e.Handled = true;
        }
    }

    private void SeekBar_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSeekBar && sender is Slider slider)
        {
            // Update visual position during drag, but don't seek yet
            var percentage = e.GetPosition(slider).X / slider.ActualWidth;
            var value = percentage * (slider.Maximum - slider.Minimum) + slider.Minimum;
            value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
            slider.Value = value;
            e.Handled = true;
        }
    }

    private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSeekBar && sender is Slider slider)
        {
            slider.ReleaseMouseCapture();
            _isDraggingSeekBar = false;
            // Final seek on mouse release
            if (DataContext is MainViewModel viewModel && viewModel.IsAudioLoaded)
            {
                viewModel.SetUserDraggingSlider(false);
                viewModel.SeekCommand.Execute(slider.Value);
            }
        }
    }
    
    private void SeekBar_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Handle case where mouse is released outside the slider
        if (_isDraggingSeekBar && sender is Slider slider)
        {
            _isDraggingSeekBar = false;
            if (DataContext is MainViewModel viewModel && viewModel.IsAudioLoaded)
            {
                viewModel.SetUserDraggingSlider(false);
                // Still perform seek with current slider value
                viewModel.SeekCommand.Execute(slider.Value);
            }
        }
    }

    private void SeekBar_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is Slider slider && DataContext is MainViewModel viewModel && viewModel.IsAudioLoaded)
        {
            double seekStep = 1.0; // 1% of track length
            double newValue = slider.Value;

            switch (e.Key)
            {
                case System.Windows.Input.Key.Left:
                    newValue = Math.Max(0, slider.Value - seekStep);
                    break;
                case System.Windows.Input.Key.Right:
                    newValue = Math.Min(100, slider.Value + seekStep);
                    break;
                case System.Windows.Input.Key.Up:
                    newValue = Math.Min(100, slider.Value + seekStep * 5); // Larger step
                    break;
                case System.Windows.Input.Key.Down:
                    newValue = Math.Max(0, slider.Value - seekStep * 5); // Larger step
                    break;
                default:
                    return; // Don't handle other keys
            }

            slider.Value = newValue;
            viewModel.SeekCommand.Execute(newValue);
            e.Handled = true;
        }
    }

    // Removed ValueChanged handler to avoid seeking during drag
    // We only seek on mouse up for smoother playback
}

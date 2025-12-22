using System.Windows;
using System.Windows.Input;
using Remixer.WPF.ViewModels;

namespace Remixer.WPF.Views;

public partial class AIChatWindow : Window
{
    public AIChatWindow(AIChatViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // Scroll to bottom when new messages are added
        viewModel.Messages.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null && e.NewItems.Count > 0)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    MessagesScrollViewer.ScrollToEnd();
                });
            }
        };
    }

    private void TextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+Enter to send
            if (DataContext is AIChatViewModel vm)
            {
                vm.SendMessageCommand.Execute(null);
            }
            e.Handled = true;
        }
    }
}


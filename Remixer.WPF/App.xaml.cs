using System;
using System.Windows;
using Remixer.Core.Logging;

namespace Remixer.WPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static bool _isHandlingError = false;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Handle unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (_isHandlingError) return; // Prevent infinite recursion
            _isHandlingError = true;
            
            try
            {
                var exception = args.ExceptionObject as Exception;
                Logger.Critical("Unhandled AppDomain exception", exception);
                
                // Try to write to a crash file
                System.IO.File.WriteAllText("crash.log", $"FATAL ERROR:\n{args.ExceptionObject}");
            }
            catch
            {
                // If logging fails, silently exit
            }
        };
        
        DispatcherUnhandledException += (sender, args) =>
        {
            if (_isHandlingError) return; // Prevent infinite recursion
            _isHandlingError = true;
            
            try
            {
                Logger.Critical("Unhandled Dispatcher exception", args.Exception);
                args.Handled = true;
                
                // Try to write to a crash file
                System.IO.File.WriteAllText("crash.log", $"ERROR:\n{args.Exception}");
            }
            catch
            {
                // If logging fails, silently mark as handled
                args.Handled = true;
            }
            finally
            {
                _isHandlingError = false;
            }
        };
    }
}


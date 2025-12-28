using System;
using System.Windows;
using System.Windows.Threading;

namespace AeroAI.UI;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log the exception (if possible) and show a message box
        // Using MessageBox directly as this is a UI crash
        MessageBox.Show($"Unhandled Exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}", 
                        "AeroAI Crash", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
        
        // Prevent default crash behavior if possible, but for serious errors it might still terminate
        e.Handled = true; 
        
        // Optionally shutdown if it's unrecoverable
        // Current.Shutdown(); 
    }
}

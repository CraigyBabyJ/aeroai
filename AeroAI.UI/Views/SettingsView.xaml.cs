using System;
using System.Windows.Controls;

namespace AeroAI.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}

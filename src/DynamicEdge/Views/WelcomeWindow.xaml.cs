using System.Windows;
using DynamicEdge.Core;

namespace DynamicEdge.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow() { InitializeComponent(); }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.Autostart = AutoBox.IsChecked == true;
        DialogResult = true;
    }
}

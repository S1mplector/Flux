using System;
using System.Reflection;
using System.Windows;

namespace Equalizer.Presentation.Splash;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {GetVersionString()}";
    }

    private static string GetVersionString()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var v = asm.GetName().Version;
            return v != null ? v.ToString(3) : "dev";
        }
        catch
        {
            return "dev";
        }
    }

    public void SetStatus(string message)
    {
        StatusText.Text = message;
    }
}

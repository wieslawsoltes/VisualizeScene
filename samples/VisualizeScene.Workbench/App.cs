using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VisualizeScene.Workbench;

public sealed class App : Application
{
    private Window? window;
    public App()
    {
        RequestedTheme = ApplicationTheme.Light;
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        UnhandledException += (_, args) => System.Diagnostics.Debug.WriteLine(args.Exception);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new Window { Title = "VisualizeScene · Graph Workbench", Content = new WorkbenchPage() };
        window.Activate();
    }
}

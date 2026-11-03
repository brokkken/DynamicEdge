using System;
using System.Threading;
using System.Windows;
using DynamicEdge.Core;
using DynamicEdge.Services;
using DynamicEdge.Views;
using WF = System.Windows.Forms;

namespace DynamicEdge;

public partial class App : Application
{
    Mutex? _mutex;
    IslandWindow? _island;
    SettingsWindow? _settings;
    WF.NotifyIcon? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Local\DynamicEdge.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }

        DispatcherUnhandledException += (_, a) => { SettingsService.Log(a.Exception.ToString()); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => SettingsService.Log("Fatal: " + a.ExceptionObject);

        SettingsService.Load();
        ThemeManager.Apply();

        bool first = !SettingsService.Current.FirstRunDone;
        if (first)
        {
            new WelcomeWindow().ShowDialog();
            SettingsService.Current.FirstRunDone = true;
            SettingsService.NotifyChanged();
            await Autostart.SetAsync(SettingsService.Current.Autostart);
        }

        AppServices.Msg = new MessageWindow();
        AppServices.Output = new AudioEndpoint(0);
        AppServices.Input = new AudioEndpoint(1);

        _island = new IslandWindow();
        _island.Show();
        CreateTray();

        await AppServices.Media.InitAsync();
        await AppServices.Notifications.InitAsync();
        if (first) _island.ShowToastPublic("Dynamic Edge запущен", "Нажмите на капсулу, чтобы развернуть");
    }

    void CreateTray()
    {
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Развернуть / свернуть", null, (_, _) => Dispatcher.Invoke(() => _island?.ToggleExpanded()));
        menu.Items.Add("Показать / скрыть капсулу", null, (_, _) => Dispatcher.Invoke(() => _island?.ToggleHidden()));
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Настройки…", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(ExitApp));

        System.Drawing.Icon icon;
        try { icon = new System.Drawing.Icon(GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream); }
        catch { icon = System.Drawing.SystemIcons.Application; }

        _tray = new WF.NotifyIcon { Icon = icon, Text = "Dynamic Edge", Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, a) => { if (a.Button == WF.MouseButtons.Left) Dispatcher.Invoke(() => _island?.ToggleExpanded()); };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettings);
    }

    void ShowSettings()
    {
        if (_settings != null) { _settings.Activate(); return; }
        _settings = new SettingsWindow();
        _settings.Closed += (_, _) => { _settings = null; SettingsService.Save(); };
        _settings.Show();
        _settings.Activate();
    }

    void ExitApp()
    {
        try
        {
            SettingsService.Save();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            AppServices.Msg?.Dispose();
            AppServices.Output?.Dispose();
            AppServices.Input?.Dispose();
        }
        catch { }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}

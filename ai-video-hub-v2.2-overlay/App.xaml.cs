using System.Windows;
using System.Windows.Threading;
using AI.VideoHub.Services;

namespace AI.VideoHub;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private readonly DiagnosticLog _log = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.Ensure();

        if (e.Args.Contains("--selftest-protocol", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var result = ProtocolSelfTest.Run();
                Console.WriteLine(result);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("protocol-selftest=FAIL " + ex);
                Shutdown(22);
            }
            return;
        }

        if (e.Args.Contains("--selftest-storage", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var result = await StorageSelfTest.RunAsync();
                Console.WriteLine(result);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("storage-selftest=FAIL " + ex);
                Shutdown(21);
            }
            return;
        }

        _singleInstance = new SingleInstanceGuard("Local\\AI.VideoHub.V2.SingleInstance");
        var singleInstanceProbe = e.Args.Contains("--probe-single-instance", StringComparer.OrdinalIgnoreCase);
        if (!_singleInstance.IsPrimaryInstance)
        {
            if (!singleInstanceProbe)
                MessageBox.Show("AI Video Hub 已经在运行。为保护账号 Profile 和本地数据，不允许同时启动两个实例。", "AI Video Hub", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(2);
            return;
        }
        if (singleInstanceProbe)
        {
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _log.Error("AppDomain unhandled: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => { _log.Error("Task unobserved: " + args.Exception); args.SetObserved(); };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Error("Dispatcher unhandled: " + e.Exception);
        MessageBox.Show("软件遇到未处理异常，已写入诊断日志。\n\n" + e.Exception.Message, "AI Video Hub", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

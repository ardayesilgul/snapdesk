using SnapDesk;
using SnapDesk.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = new SingleInstance();

        if (!instance.IsFirstInstance)
        {
            var command = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "capture";
            instance.SendCommand(command);
            return;
        }

        ApplicationConfiguration.Initialize();

        var settings = Settings.Load();
        var app = new SnapDeskApplication(settings, instance);

        instance.CommandReceived += command => app.HandleCommand(command);

        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

        app.Start();
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            app.HandleCommand(args[0]);
        }
        Application.Run(new ApplicationContext());
    }

    private static void ShowFatal(Exception? exception)
    {
        if (exception is null) return;

        string? logPath = null;
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "SnapDesk");
            Directory.CreateDirectory(folder);
            logPath = Path.Combine(folder, "error.log");
            File.AppendAllText(
                logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\r\n\r\n");
        }
        catch
        {
            // günlük yazılamadıysa sorun değil
        }

        MessageBox.Show(
            "SnapDesk beklenmeyen bir hatayla karşılaştı.\n\n" + exception.Message +
            (logPath is null ? string.Empty : "\n\nAyrıntılar: " + logPath),
            "SnapDesk",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

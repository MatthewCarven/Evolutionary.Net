using System.Windows;

namespace EvolutionaryStudio
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // headless self-test used for build verification: run a tiny evolution and exit
            if (e.Args != null && Array.Exists(e.Args, a => a == "--smoke"))
            {
                int exitCode = SmokeTest.Run();
                Shutdown(exitCode);
                return;
            }

            var window = new MainWindow();
            MainWindow = window;

            // demo-run the UI and capture PNGs of each tab, then exit
            int screenshotArg = e.Args != null ? Array.IndexOf(e.Args, "--screenshot") : -1;
            if (screenshotArg >= 0)
            {
                string dir = screenshotArg + 1 < e.Args.Length ? e.Args[screenshotArg + 1] : "screenshots";
                window.Loaded += async (_, _) => Shutdown(await window.RunScreenshotDemoAsync(dir) ? 0 : 1);
            }

            window.Show();
        }
    }
}

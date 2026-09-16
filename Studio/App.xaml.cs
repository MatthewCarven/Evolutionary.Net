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
            window.Show();
        }
    }
}

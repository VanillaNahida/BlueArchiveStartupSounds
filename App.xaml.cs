using System.Windows;

namespace BlueArchiveStartupSounds
{
    public partial class App : Application
    {
        private AudioPlayer? _audioPlayer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length > 0 && e.Args[0] == "--startup")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _audioPlayer = new AudioPlayer();
                _audioPlayer.PlaybackCompleted += OnPlaybackCompleted;
                var config = ConfigManager.LoadConfig();
                _audioPlayer.PlayWithConfig(config);
            }
            else
            {
                // 非--startup模式，显示主窗口
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }

        private void OnPlaybackCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                _audioPlayer?.Dispose();
                Shutdown();
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _audioPlayer?.Dispose();
            base.OnExit(e);
        }
    }
}

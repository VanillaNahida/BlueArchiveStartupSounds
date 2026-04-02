using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BlueArchiveStartupSounds
{
    public partial class MainWindow : Window
    {
        private AppConfig _config;
        private AudioPlayer _audioPlayer;

        public MainWindow()
        {
            InitializeComponent();
            _config = ConfigManager.LoadConfig();
            _audioPlayer = new AudioPlayer();
            _audioPlayer.PlaybackCompleted += OnPlaybackCompleted;
            LoadConfigToUi();
            CheckLockEngineFolder();
        }

        private void CheckLockEngineFolder()
        {
            var workDir = AppDomain.CurrentDomain.BaseDirectory;
            var lockEngineFolder = Path.Combine(workDir, "LockEngine");
            var lockEngineExe = Path.Combine(lockEngineFolder, "LockEngine.exe");
            
            if (!Directory.Exists(lockEngineFolder))
            {
                MessageBox.Show("未检测到 LockEngine 文件夹，开机自动播放影片功能不可用。\n\n如需使用此功能，请确保 LockEngine 文件夹存在于程序目录下。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                AutoStartVideoCheckBox.IsEnabled = false;
            }
            else if (!File.Exists(lockEngineExe))
            {
                MessageBox.Show("未检测到 LockEngine.exe 文件，开机自动播放影片功能不可用。\n\n如需使用此功能，请确保 LockEngine.exe 存在于 LockEngine 文件夹中。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                AutoStartVideoCheckBox.IsEnabled = false;
            }
        }

        private void LoadConfigToUi()
        {
            BgmPathTextBox.Text = _config.BgmPath;
            VoiceDirTextBox.Text = _config.VoiceDir;
            AronaVoiceDirTextBox.Text = _config.AronaVoiceDir;
            AronaEnterTextBox.Text = _config.AronaEnterVoice;
            AronaTtsTextBox.Text = _config.AronaTtsVoice;

            DelaySecondsNumericUpDown.Value = (decimal)_config.DelaySeconds;
            FadeDurationNumericUpDown.Value = (decimal)_config.FadeDuration;

            BgmVolumeSlider.Value = _config.BgmVolume * 100;
            VoiceVolumeSlider.Value = _config.VoiceVolume * 100;

            KillLockEngineCheckBox.IsChecked = _config.KillLockEngine;
            WaitForLockEngineCheckBox.IsChecked = _config.WaitForLockEngine;
            AutoStartVoiceCheckBox.IsChecked = ConfigManager.TaskExists(ConfigManager.TaskName);
            AutoStartVideoCheckBox.IsChecked = ConfigManager.RegistryRunExists(ConfigManager.RegValueName);
        }

        private void BgmVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BgmVolumeText != null)
            {
                BgmVolumeText.Text = $"{e.NewValue:F1}%";
            }
        }

        private void VoiceVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VoiceVolumeText != null)
            {
                VoiceVolumeText.Text = $"{e.NewValue:F1}%";
            }
        }

        private void BrowseBgmFile(object sender, RoutedEventArgs e)
        {
            BrowseFile(BgmPathTextBox);
        }

        private void BrowseVoiceDir(object sender, RoutedEventArgs e)
        {
            BrowseFolder(VoiceDirTextBox);
        }

        private void BrowseAronaVoiceDir(object sender, RoutedEventArgs e)
        {
            BrowseFolder(AronaVoiceDirTextBox);
        }

        private void BrowseAronaEnterFile(object sender, RoutedEventArgs e)
        {
            BrowseFile(AronaEnterTextBox);
        }

        private void BrowseAronaTtsFile(object sender, RoutedEventArgs e)
        {
            BrowseFile(AronaTtsTextBox);
        }

        private void BrowseFile(TextBox textBox)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "音频文件 (*.wav;*.ogg;*.mp3)|*.wav;*.ogg;*.mp3|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == true)
            {
                textBox.Text = dialog.FileName;
            }
        }

        private void BrowseFolder(TextBox textBox)
        {
            var dialog = new Forms.FolderBrowserDialog
            {
                ShowNewFolderButton = true
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                textBox.Text = dialog.SelectedPath;
            }
        }

        private void SaveConfig(object sender, RoutedEventArgs e)
        {
            var delaySeconds = (double)DelaySecondsNumericUpDown.Value;
            var fadeDuration = (double)FadeDurationNumericUpDown.Value;

            _config = new AppConfig
            {
                BgmPath = BgmPathTextBox.Text,
                VoiceDir = VoiceDirTextBox.Text,
                AronaVoiceDir = AronaVoiceDirTextBox.Text,
                AronaEnterVoice = AronaEnterTextBox.Text,
                AronaTtsVoice = AronaTtsTextBox.Text,
                DelaySeconds = Math.Round(delaySeconds, 1),
                FadeDuration = Math.Round(fadeDuration, 1),
                BgmVolume = Math.Round(BgmVolumeSlider.Value / 100.0, 2),
                VoiceVolume = Math.Round(VoiceVolumeSlider.Value / 100.0, 2),
                KillLockEngine = KillLockEngineCheckBox.IsChecked ?? false,
                WaitForLockEngine = WaitForLockEngineCheckBox.IsChecked ?? false
            };

            ConfigManager.SaveConfig(_config);

            var workDir = AppDomain.CurrentDomain.BaseDirectory;

            if (AutoStartVoiceCheckBox.IsChecked == true)
            {
                if (!ConfigManager.TaskExists(ConfigManager.TaskName))
                {
                    ConfigManager.CreateTask(ConfigManager.TaskName, workDir);
                }
            }
            else
            {
                ConfigManager.RemoveTask(ConfigManager.TaskName);
            }

            var lockEnginePath = Path.Combine(workDir, "LockEngine", "LockEngine.exe");
            if (AutoStartVideoCheckBox.IsChecked == true)
            {
                ConfigManager.AddRegistryRun(ConfigManager.RegValueName, lockEnginePath);
            }
            else
            {
                ConfigManager.RemoveRegistryRun(ConfigManager.RegValueName);
            }

            MessageBox.Show("配置已保存！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void TestPlay(object sender, RoutedEventArgs e)
        {
            if (_audioPlayer.IsPlaying)
            {
                _audioPlayer.Stop();
                TestButton.Content = "测试播放";
                TestButton.IsEnabled = true;
                return;
            }

            var delaySeconds = (double)DelaySecondsNumericUpDown.Value;
            var fadeDuration = (double)FadeDurationNumericUpDown.Value;

            var testConfig = new AppConfig
            {
                BgmPath = BgmPathTextBox.Text,
                VoiceDir = VoiceDirTextBox.Text,
                AronaVoiceDir = AronaVoiceDirTextBox.Text,
                AronaEnterVoice = AronaEnterTextBox.Text,
                AronaTtsVoice = AronaTtsTextBox.Text,
                DelaySeconds = Math.Round(delaySeconds, 1),
                FadeDuration = Math.Round(fadeDuration, 1),
                BgmVolume = Math.Round(BgmVolumeSlider.Value / 100.0, 2),
                VoiceVolume = Math.Round(VoiceVolumeSlider.Value / 100.0, 2),
                KillLockEngine = false,
                WaitForLockEngine = WaitForLockEngineCheckBox.IsChecked ?? false
            };

            TestButton.Content = "播放中...";
            TestButton.IsEnabled = false;
            _audioPlayer.PlayWithConfig(testConfig);
        }

        private void OnPlaybackCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                TestButton.Content = "测试播放";
                TestButton.IsEnabled = true;
            });
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.ShowDialog();
        }

        protected override void OnClosed(EventArgs e)
        {
            _audioPlayer.Dispose();
            base.OnClosed(e);
        }
    }
}

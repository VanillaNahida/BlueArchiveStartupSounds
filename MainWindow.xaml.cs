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
        private const string DefaultOutputDeviceText = "(系统默认设备)";

        public MainWindow()
        {
            InitializeComponent();
            // 初始高度按内容自适应，但不超过屏幕工作区；超出部分由 ScrollViewer 滚动显示，
            // 避免在高 DPI 缩放或小分辨率下窗口超出屏幕导致下方按钮看不到
            MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 20);
            // 首次布局完成后改为手动尺寸：窗口不再被内容高度锁死，用户可以缩小窗口，
            // 内容显示不全时即可通过 ScrollViewer 上下滚动查看
            Loaded += OnFirstLoaded;
            _config = ConfigManager.LoadConfig();
            _audioPlayer = new AudioPlayer();
            _audioPlayer.PlaybackCompleted += OnPlaybackCompleted;
            LoadConfigToUi();
            CheckLockEngineFolder();
        }

        /// <summary>
        /// 首次显示后把窗口从“按内容自适应高度”切换为可自由调整大小，
        /// 使窗口可以被缩小，内容显示不全时由 ScrollViewer 上下滚动；
        /// 切换时保持当前尺寸不变，不把高度固定为具体数值。
        /// </summary>
        private void OnFirstLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnFirstLoaded;
            SizeToContent = SizeToContent.Manual;
        }

        private void CheckLockEngineFolder()
        {
            var workDir = GetApplicationDirectory();
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

        /// <summary>
        /// 获取应用程序实际所在目录。
        /// 单文件发布时 AppDomain.CurrentDomain.BaseDirectory 可能指向临时解压目录，
        /// 改用 Environment.ProcessPath 获取真实 exe 路径。
        /// </summary>
        private static string GetApplicationDirectory()
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                return Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
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
            LogToFileCheckBox.IsChecked = _config.LogToFile;
            EnableLoginMusicCheckBox.IsChecked = _config.EnableLoginMusic;
            EnableDesktopVoiceCheckBox.IsChecked = _config.EnableDesktopVoice;
            AutoStartVoiceCheckBox.IsChecked = ConfigManager.TaskExists(ConfigManager.TaskName);
            AutoStartVideoCheckBox.IsChecked = ConfigManager.RegistryRunExists(ConfigManager.RegValueName);

            LoadOutputDevicesIntoCombo(_config.OutputDevice);
        }

        /// <summary>
        /// 将系统可用音频输出设备填入下拉列表，并选中配置中保存的设备。
        /// </summary>
        private void LoadOutputDevicesIntoCombo(string preferredDevice)
        {
            var devices = new List<string> { DefaultOutputDeviceText };
            foreach (var device in AudioPlayer.GetOutputDevices())
            {
                if (!devices.Contains(device, StringComparer.OrdinalIgnoreCase))
                {
                    devices.Add(device);
                }
            }

            // 若保存的设备名当前不在枚举列表中（例如声卡状态变化），仍加入列表以便用户查看并重新选择
            if (!string.IsNullOrEmpty(preferredDevice) &&
                !devices.Contains(preferredDevice, StringComparer.OrdinalIgnoreCase))
            {
                devices.Add(preferredDevice);
            }

            OutputDeviceComboBox.ItemsSource = devices;

            if (string.IsNullOrEmpty(preferredDevice))
            {
                OutputDeviceComboBox.SelectedIndex = 0;
            }
            else
            {
                var index = devices.FindIndex(d => d.Equals(preferredDevice, StringComparison.OrdinalIgnoreCase));
                OutputDeviceComboBox.SelectedIndex = index >= 0 ? index : 0;
            }
        }

        /// <summary>
        /// 获取下拉列表中当前选中的设备名；选择默认项时返回空字符串。
        /// </summary>
        private string GetSelectedOutputDevice()
        {
            var selected = OutputDeviceComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selected) ||
                string.Equals(selected, DefaultOutputDeviceText, StringComparison.Ordinal))
            {
                return "";
            }
            return selected;
        }

        private void RefreshOutputDevices(object sender, RoutedEventArgs e)
        {
            var current = GetSelectedOutputDevice();
            LoadOutputDevicesIntoCombo(current);
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
                WaitForLockEngine = WaitForLockEngineCheckBox.IsChecked ?? false,
                LogToFile = LogToFileCheckBox.IsChecked ?? false,
                EnableLoginMusic = EnableLoginMusicCheckBox.IsChecked ?? true,
                EnableDesktopVoice = EnableDesktopVoiceCheckBox.IsChecked ?? true,
                OutputDevice = GetSelectedOutputDevice()
            };

            ConfigManager.SaveConfig(_config);

            var workDir = GetApplicationDirectory();

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
                WaitForLockEngine = WaitForLockEngineCheckBox.IsChecked ?? false,
                LogToFile = LogToFileCheckBox.IsChecked ?? false,
                EnableLoginMusic = EnableLoginMusicCheckBox.IsChecked ?? true,
                EnableDesktopVoice = EnableDesktopVoiceCheckBox.IsChecked ?? true,
                OutputDevice = GetSelectedOutputDevice()
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

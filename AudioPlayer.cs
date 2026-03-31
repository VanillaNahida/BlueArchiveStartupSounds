using System.Diagnostics;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace BaStartupSounds
{
    public class AudioPlayer : IDisposable
    {
        private WaveOutEvent? _outputDevice;
        private MixingSampleProvider? _mixer;
        private FadeInOutSampleProvider? _bgmProvider;
        private VolumeSampleProvider? _bgmVolumeProvider;
        private WaveStream? _bgmReader;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _playTask;
        private bool _isPlaying;
        private readonly object _lockObject = new();

        public bool IsPlaying => _isPlaying;

        public event Action? PlaybackCompleted;

        public void PlayWithConfig(AppConfig config)
        {
            lock (_lockObject)
            {
                if (_isPlaying)
                {
                    Stop();
                }
                _cancellationTokenSource = new CancellationTokenSource();
                _playTask = Task.Run(() => PlayWithConfigAsync(config, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
            }
        }

        private async Task PlayWithConfigAsync(AppConfig config, CancellationToken cancellationToken)
        {
            try
            {
                _isPlaying = true;

                var bgmPath = ResolvePath(config.BgmPath);
                var voiceDir = ResolvePath(config.VoiceDir);
                var aronaVoiceDir = ResolvePath(config.AronaVoiceDir);
                var aronaEnterVoice = ResolvePath(config.AronaEnterVoice);
                var aronaTtsVoice = ResolvePath(config.AronaTtsVoice);

                // 如果配置了等待LockEngine启动，则等待
                if (config.WaitForLockEngine)
                {
                    Console.WriteLine("等待LockEngine启动...");
                    await WaitForLockEngineAsync(cancellationToken);
                    Console.WriteLine("LockEngine已启动");
                }

                // 初始化音频输出设备和混音器
                _outputDevice = new WaveOutEvent();
                _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
                _mixer.ReadFully = true;

                // 启动BGM播放
                if (File.Exists(bgmPath))
                {
                    // 创建BGM读取器
                    if (Path.GetExtension(bgmPath).ToLowerInvariant() == ".ogg")
                    {
                        _bgmReader = new VorbisWaveReader(bgmPath);
                    }
                    else
                    {
                        _bgmReader = new AudioFileReader(bgmPath);
                    }

                    // 创建BGM音量控制器
                    _bgmVolumeProvider = new VolumeSampleProvider(_bgmReader.ToSampleProvider());
                    _bgmVolumeProvider.Volume = 0; // 初始音量为0

                    // 创建BGM淡入淡出控制器
                    _bgmProvider = new FadeInOutSampleProvider(_bgmVolumeProvider);

                    // 将BGM添加到混音器
                    _mixer.AddMixerInput(_bgmProvider);

                    // 初始化输出设备
                    _outputDevice.Init(_mixer);
                    _outputDevice.Play();

                    // 淡入BGM
                    await FadeBgmAsync(config.FadeDuration, config.BgmVolume, cancellationToken, true);
                }
                else
                {
                    // 如果没有BGM，也要初始化输出设备
                    _outputDevice.Init(_mixer);
                    _outputDevice.Play();
                }

                // 延时后播放标题语音
                await Task.Delay(TimeSpan.FromSeconds(config.DelaySeconds), cancellationToken);

                var voiceFiles = GetVoiceFiles(voiceDir);
                if (voiceFiles.Count > 0)
                {
                    var selectedVoice = voiceFiles[Random.Shared.Next(voiceFiles.Count)];
                    // 同时播放BGM和标题语音，标题语音不需要淡入淡出
                    await PlayVoiceAsync(selectedVoice, config.VoiceVolume, false, config.FadeDuration, cancellationToken);
                }

                // 等待电脑解锁
                await WaitForUnlockAsync(cancellationToken);

                // 淡出并停止BGM
                if (_bgmProvider != null)
                {
                    await FadeBgmAsync(config.FadeDuration, config.BgmVolume, cancellationToken, false);
                    _mixer?.RemoveMixerInput(_bgmProvider);
                    _bgmReader?.Dispose();
                    _bgmReader = null;
                    _bgmProvider = null;
                    _bgmVolumeProvider = null;
                }

                // 关闭LockEngine.exe
                if (config.KillLockEngine)
                {
                    KillProcess("LockEngine.exe");
                }

                // 按顺序播放进入桌面的语音，进入桌面的语音不需要淡入淡出
                if (File.Exists(aronaEnterVoice))
                {
                    await PlayVoiceAsync(aronaEnterVoice, config.VoiceVolume, false, config.FadeDuration, cancellationToken);
                }

                if (File.Exists(aronaTtsVoice))
                {
                    await PlayVoiceAsync(aronaTtsVoice, config.VoiceVolume, false, config.FadeDuration, cancellationToken);
                }

                var aronaVoiceFiles = GetVoiceFiles(aronaVoiceDir);
                if (aronaVoiceFiles.Count > 0)
                {
                    var selectedAronaVoice = aronaVoiceFiles[Random.Shared.Next(aronaVoiceFiles.Count)];
                    await PlayVoiceAsync(selectedAronaVoice, config.VoiceVolume, false, config.FadeDuration, cancellationToken);
                }
                else
                {
                    // 调试：如果没有找到音频文件，输出目录路径
                    Console.WriteLine($"No audio files found in directory: {aronaVoiceDir}");
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("播放任务已取消。");
            }
            catch (Exception ex)
            {
                LogMessage($"播放任务异常: {ex}");
            }
            finally
            {
                Cleanup();
                _isPlaying = false;
                PlaybackCompleted?.Invoke();
            }
        }

        private async Task PlayVoiceAsync(string filePath, double volume, bool useFade, double fadeDuration, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath) || _mixer == null)
            {
                return;
            }

            try
            {
                // 调试：输出文件路径
                Console.WriteLine($"Playing file: {filePath}");
                
                // 创建语音读取器
                IWaveProvider waveReader;
                if (Path.GetExtension(filePath).ToLowerInvariant() == ".ogg")
                {
                    waveReader = new VorbisWaveReader(filePath);
                }
                else
                {
                    waveReader = new AudioFileReader(filePath);
                }

                var voiceReader = waveReader.ToSampleProvider();
                
                // 统一采样率和声道
                if (voiceReader.WaveFormat.SampleRate != _mixer.WaveFormat.SampleRate || 
                    voiceReader.WaveFormat.Channels != _mixer.WaveFormat.Channels)
                {
                    Console.WriteLine($"Resampling: {voiceReader.WaveFormat.SampleRate}Hz {voiceReader.WaveFormat.Channels}ch to {_mixer.WaveFormat.SampleRate}Hz {_mixer.WaveFormat.Channels}ch");
                    // 重采样
                    voiceReader = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(voiceReader, _mixer.WaveFormat.SampleRate);
                    // 处理声道不匹配
                    if (voiceReader.WaveFormat.Channels == 1 && _mixer.WaveFormat.Channels == 2)
                    {
                        voiceReader = new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(voiceReader);
                    }
                }

                // 创建语音音量控制器
                var voiceVolumeProvider = new VolumeSampleProvider(voiceReader)
                {
                    Volume = useFade ? 0 : (float)volume // 如果使用淡入，初始音量为0，否则直接设置目标音量
                };

                // 创建语音淡入淡出控制器
                var voiceFadeProvider = new FadeInOutSampleProvider(voiceVolumeProvider);

                // 将语音添加到混音器
                _mixer.AddMixerInput(voiceFadeProvider);
                Console.WriteLine("Added to mixer");

                if (useFade)
                {
                    // 淡入语音
                    voiceFadeProvider.BeginFadeIn((int)(fadeDuration * 1000)); // 淡入时长

                    // 同时设置目标音量
                    var steps = 50;
                    var stepTime = fadeDuration / steps;
                    for (int i = 0; i <= steps; i++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var voiceVolume = (i / (double)steps) * volume;
                        voiceVolumeProvider.Volume = (float)voiceVolume;
                        await Task.Delay(TimeSpan.FromSeconds(stepTime), cancellationToken);
                    }
                }

                // 等待语音播放完成
                var totalTime = GetAudioDuration(filePath);
                if (totalTime.HasValue)
                {
                    await Task.Delay(totalTime.Value, cancellationToken);
                }
                else
                {
                    // 如果无法获取时长，等待一段时间
                    await Task.Delay(3000, cancellationToken);
                }

                if (useFade)
                {
                    // 淡出语音
                    voiceFadeProvider.BeginFadeOut((int)(fadeDuration * 1000)); // 淡出时长

                    // 同时降低音量
                    var steps = 50;
                    var stepTime = fadeDuration / steps;
                    for (int i = steps; i >= 0; i--)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var voiceVolume = (i / (double)steps) * volume;
                        voiceVolumeProvider.Volume = (float)voiceVolume;
                        await Task.Delay(TimeSpan.FromSeconds(stepTime), cancellationToken);
                    }
                }

                // 从混音器中移除语音
                _mixer.RemoveMixerInput(voiceFadeProvider);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"播放语音失败: {ex.Message}");
                throw;
            }
        }

        private TimeSpan? GetAudioDuration(string filePath)
        {
            try
            {
                if (Path.GetExtension(filePath).ToLowerInvariant() == ".ogg")
                {
                    using var reader = new VorbisWaveReader(filePath);
                    return reader.TotalTime;
                }
                else
                {
                    using var reader = new AudioFileReader(filePath);
                    return reader.TotalTime;
                }
            }
            catch
            {
                return null;
            }
        }

        private async Task FadeBgmAsync(double duration, double targetVolume, CancellationToken cancellationToken, bool fadeIn)
        {
            if (_bgmProvider == null || _bgmVolumeProvider == null)
                return;

            if (fadeIn)
            {
                // 淡入
                _bgmProvider.BeginFadeIn((int)(duration * 1000));

                // 同时设置目标音量
                var steps = 50;
                var stepTime = duration / steps;
                for (int i = 0; i <= steps; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var volume = (i / (double)steps) * targetVolume;
                    _bgmVolumeProvider.Volume = (float)volume;
                    await Task.Delay(TimeSpan.FromSeconds(stepTime), cancellationToken);
                }
            }
            else
            {
                // 淡出
                _bgmProvider.BeginFadeOut((int)(duration * 1000));

                var steps = 50;
                var stepTime = duration / steps;
                for (int i = steps; i >= 0; i--)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var volume = (i / (double)steps) * targetVolume;
                    _bgmVolumeProvider.Volume = (float)volume;
                    await Task.Delay(TimeSpan.FromSeconds(stepTime), cancellationToken);
                }
            }
        }

        private void Cleanup()
        {
            _bgmReader?.Dispose();
            _bgmReader = null;

            _outputDevice?.Stop();
            _outputDevice?.Dispose();
            _outputDevice = null;

            _mixer?.RemoveAllMixerInputs();
            _mixer = null;

            _bgmProvider = null;
            _bgmVolumeProvider = null;
        }

        private string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        private List<string> GetVoiceFiles(string directory)
        {
            var files = new List<string>();
            if (!Directory.Exists(directory))
            {
                return files;
            }

            var extensions = new[] { ".wav", ".ogg", ".mp3", ".wma", ".aac", ".m4a" };
            foreach (var file in Directory.GetFiles(directory))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (extensions.Contains(ext))
                {
                    files.Add(file);
                }
            }
            return files;
        }

        private async Task WaitForUnlockAsync(CancellationToken cancellationToken)
        {
            while (IsWorkstationLocked() && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        private bool IsWorkstationLocked()
        {
            try
            {
                var processes = Process.GetProcessesByName("logonui");
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private void KillProcess(string processName)
        {
            string nameOnly = Path.GetFileNameWithoutExtension(processName);
            LogMessage($"开始尝试关闭进程: {processName}");

            var initialProcesses = Process.GetProcessesByName(nameOnly);
            if (initialProcesses.Length == 0)
            {
                LogMessage($"未找到进程: {nameOnly}");
                return;
            }

            try
            {
                foreach (var p in initialProcesses)
                {
                    try
                    {
                        LogMessage($"原生Kill尝试: {p.ProcessName} (PID: {p.Id})");
                        p.Kill(entireProcessTree: true);
                        var exited = p.WaitForExit(3000);
                        LogMessage(exited
                            ? $"原生Kill成功: PID {p.Id}"
                            : $"原生Kill超时: PID {p.Id}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"原生Kill失败: PID {p.Id}, 错误: {ex.Message}");
                    }
                }

                // 原生Kill后再次校验，如仍存在则使用taskkill兜底
                if (Process.GetProcessesByName(nameOnly).Length > 0)
                {
                    LogMessage($"原生Kill后仍存在 {nameOnly}，执行taskkill兜底");
                    RunTaskKill(processName);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"KillProcess流程异常: {ex.Message}");
            }

            var remaining = Process.GetProcessesByName(nameOnly);
            if (remaining.Length == 0)
            {
                LogMessage($"进程已成功关闭: {nameOnly}");
            }
            else
            {
                LogMessage($"进程仍在运行: {nameOnly}, 数量: {remaining.Length}");
            }
        }

        private void RunTaskKill(string processName)
        {
            try
            {
                var imageName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? processName
                    : $"{Path.GetFileNameWithoutExtension(processName)}.exe";
                LogMessage($"尝试使用taskkill命令关闭进程: {imageName}");
                
                var psi = new ProcessStartInfo
                {
                    // 使用绝对路径避免重定向问题
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"),
                    // 增加 /T 来杀掉子进程树
                    Arguments = $"/F /T /IM {imageName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                process?.WaitForExit(5000);
                
                var output = process?.StandardOutput.ReadToEnd();
                var error = process?.StandardError.ReadToEnd();
                
                if (process?.ExitCode == 0)
                {
                    LogMessage($"taskkill成功关闭进程: {imageName}");
                    if (!string.IsNullOrEmpty(output))
                    {
                        LogMessage($"taskkill输出: {output.Trim()}");
                    }
                }
                else
                {
                    LogMessage($"taskkill关闭进程失败，退出代码: {process?.ExitCode}");
                    if (!string.IsNullOrEmpty(error))
                    {
                        LogMessage($"taskkill错误: {error.Trim()}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"执行taskkill命令时出错: {ex.Message}");
            }
        }

        private void LogMessage(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Debug.WriteLine(line);
            Console.WriteLine(line);
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BaStartupSounds.log");
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
                // 日志写入失败不影响主流程
            }
        }

        private async Task WaitForLockEngineAsync(CancellationToken cancellationToken)
        {
            while (!IsLockEngineRunning() && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        private bool IsLockEngineRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName("LockEngine");
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public void Stop()
        {
            lock (_lockObject)
            {
                _cancellationTokenSource?.Cancel();
                Cleanup();
                _isPlaying = false;
            }
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }
}

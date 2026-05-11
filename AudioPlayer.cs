using System.Diagnostics;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace BlueArchiveStartupSounds
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
        private bool _logToFile;
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
                _logToFile = config.LogToFile;
                _cancellationTokenSource = new CancellationTokenSource();
                _playTask = Task.Run(() => PlayWithConfigAsync(config, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
            }
        }

        private async Task PlayWithConfigAsync(AppConfig config, CancellationToken cancellationToken)
        {
            try
            {
                _isPlaying = true;
                LogMessage("播放任务开始");

                var bgmPath = Path.GetFullPath(ResolvePath(config.BgmPath));
                var voiceDir = Path.GetFullPath(ResolvePath(config.VoiceDir));
                var aronaVoiceDir = Path.GetFullPath(ResolvePath(config.AronaVoiceDir));
                var aronaEnterVoice = Path.GetFullPath(ResolvePath(config.AronaEnterVoice));
                var aronaTtsVoice = Path.GetFullPath(ResolvePath(config.AronaTtsVoice));

                LogMessage($"配置信息 - BGM路径: {bgmPath}, 语音目录: {voiceDir}, Arona语音目录: {aronaVoiceDir}");
                LogMessage($"配置信息 - 等待LockEngine: {config.WaitForLockEngine}, 关闭LockEngine: {config.KillLockEngine}");
                LogMessage($"配置信息 - 延迟时间: {config.DelaySeconds}s, 淡入淡出时长: {config.FadeDuration}s");
                LogMessage($"配置信息 - BGM音量: {config.BgmVolume}, 语音音量: {config.VoiceVolume}");

                if (config.WaitForLockEngine)
                {
                    LogMessage("等待LockEngine启动...");
                    await WaitForLockEngineAsync(cancellationToken);
                    LogMessage("LockEngine已启动");
                }

                LogMessage("初始化音频输出设备和混音器");
                _outputDevice = new WaveOutEvent();
                _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
                _mixer.ReadFully = true;

                if (File.Exists(bgmPath))
                {
                    LogMessage($"启动BGM播放: {bgmPath}");
                    if (Path.GetExtension(bgmPath).ToLowerInvariant() == ".ogg")
                    {
                        _bgmReader = new VorbisWaveReader(bgmPath);
                    }
                    else
                    {
                        _bgmReader = new AudioFileReader(bgmPath);
                    }

                    _bgmVolumeProvider = new VolumeSampleProvider(_bgmReader.ToSampleProvider());
                    _bgmVolumeProvider.Volume = 0;
                    _bgmProvider = new FadeInOutSampleProvider(_bgmVolumeProvider);
                    _mixer.AddMixerInput(_bgmProvider);
                    _outputDevice.Init(_mixer);
                    _outputDevice.Play();
                    LogMessage("BGM开始淡入");
                    await FadeBgmAsync(config.FadeDuration, config.BgmVolume, cancellationToken, true);
                    LogMessage("BGM淡入完成");
                }
                else
                {
                    LogMessage($"BGM文件不存在: {bgmPath}，跳过BGM播放");
                    _outputDevice.Init(_mixer);
                    _outputDevice.Play();
                }

                LogMessage($"等待 {config.DelaySeconds} 秒后播放标题语音");
                await Task.Delay(TimeSpan.FromSeconds(config.DelaySeconds), cancellationToken);

                var voiceFiles = GetVoiceFiles(voiceDir);
                if (voiceFiles.Count > 0)
                {
                    var selectedVoice = voiceFiles[Random.Shared.Next(voiceFiles.Count)];
                    await PlayVoiceAsync(selectedVoice, config.VoiceVolume, false, config.FadeDuration, cancellationToken);
                    LogMessage("标题语音播放完成");
                }
                else
                {
                    LogMessage($"标题语音目录为空: {voiceDir}");
                }

                LogMessage("等待电脑解锁");
                await WaitForUnlockAsync(cancellationToken);
                LogMessage("电脑已解锁");

                if (_bgmProvider != null)
                {
                    LogMessage("BGM开始淡出");
                    await FadeBgmAsync(config.FadeDuration, config.BgmVolume, cancellationToken, false);
                    LogMessage("BGM淡出完成，停止BGM播放");
                    _mixer?.RemoveMixerInput(_bgmProvider);
                    _bgmReader?.Dispose();
                    _bgmReader = null;
                    _bgmProvider = null;
                    _bgmVolumeProvider = null;
                }

                if (config.KillLockEngine)
                {
                    LogMessage("开始关闭LockEngine进程");
                    KillProcess("LockEngine.exe");
                }

                if (File.Exists(aronaEnterVoice))
                {
                    await PlayVoiceAsync(aronaEnterVoice, config.VoiceVolume, false, config.FadeDuration, cancellationToken);
                    LogMessage("进入桌面语音播放完成");
                }

                if (File.Exists(aronaTtsVoice))
                {
                    await PlayVoiceAsync(aronaTtsVoice, config.VoiceVolume, false, config.FadeDuration, cancellationToken);
                    LogMessage("TTS语音播放完成");
                }

                var aronaVoiceFiles = GetVoiceFiles(aronaVoiceDir);
                if (aronaVoiceFiles.Count > 0)
                {
                    var selectedAronaVoice = aronaVoiceFiles[Random.Shared.Next(aronaVoiceFiles.Count)];
                    await PlayVoiceAsync(selectedAronaVoice, config.VoiceVolume, false, config.FadeDuration, cancellationToken);
                    LogMessage("Arona语音播放完成");
                }
                else
                {
                    LogMessage($"Arona语音目录为空: {aronaVoiceDir}");
                }

                LogMessage("播放任务完成");
            }
            catch (OperationCanceledException)
            {
                LogMessage("播放任务已取消");
            }
            catch (Exception ex)
            {
                LogMessage($"播放任务异常: {ex}");
            }
            finally
            {
                LogMessage("执行清理操作");
                Cleanup();
                _isPlaying = false;
                PlaybackCompleted?.Invoke();
                LogMessage("播放任务结束");
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
                var absolutePath = Path.GetFullPath(filePath);
                LogMessage($"开始播放语音文件: {absolutePath}, 音量: {volume}, 淡入淡出: {useFade}, 时长: {fadeDuration}s");
                
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
                var originalFormat = $"{voiceReader.WaveFormat.SampleRate}Hz {voiceReader.WaveFormat.Channels}ch";
                
                if (voiceReader.WaveFormat.SampleRate != _mixer.WaveFormat.SampleRate || 
                    voiceReader.WaveFormat.Channels != _mixer.WaveFormat.Channels)
                {
                    LogMessage($"音频格式转换: {originalFormat} -> {_mixer.WaveFormat.SampleRate}Hz {_mixer.WaveFormat.Channels}ch");
                    voiceReader = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(voiceReader, _mixer.WaveFormat.SampleRate);
                    if (voiceReader.WaveFormat.Channels == 1 && _mixer.WaveFormat.Channels == 2)
                    {
                        voiceReader = new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(voiceReader);
                    }
                }

                var voiceVolumeProvider = new VolumeSampleProvider(voiceReader)
                {
                    Volume = useFade ? 0 : (float)volume
                };

                var voiceFadeProvider = new FadeInOutSampleProvider(voiceVolumeProvider);
                _mixer.AddMixerInput(voiceFadeProvider);
                LogMessage("语音已添加到混音器");

                if (useFade)
                {
                    LogMessage("开始语音淡入");
                    voiceFadeProvider.BeginFadeIn((int)(fadeDuration * 1000));
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
                    LogMessage("语音淡入完成");
                }

                var totalTime = GetAudioDuration(filePath);
                if (totalTime.HasValue)
                {
                    LogMessage($"等待语音播放完成，时长: {totalTime.Value}");
                    await Task.Delay(totalTime.Value, cancellationToken);
                }
                else
                {
                    LogMessage("无法获取音频时长，使用默认等待时间 3 秒");
                    await Task.Delay(3000, cancellationToken);
                }

                if (useFade)
                {
                    LogMessage("开始语音淡出");
                    voiceFadeProvider.BeginFadeOut((int)(fadeDuration * 1000));
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
                    LogMessage("语音淡出完成");
                }

                _mixer.RemoveMixerInput(voiceFadeProvider);
                LogMessage("语音已从混音器移除");
            }
            catch (Exception ex)
            {
                LogMessage($"播放语音失败: {ex.Message}");
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
            if (_logToFile)
            {
                try
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlueArchiveStartupSounds.log");
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }
                catch
                {
                }
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

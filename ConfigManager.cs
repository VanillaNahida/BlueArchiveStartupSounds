using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace BaStartupSounds
{
    public static class ConfigManager
    {
        private const string ConfigFileName = "AppConfig.json";
        public const string TaskName = "BlueArchive开机音效";
        public const string RegValueName = "BlueArchiveLockEngine";
        private const string RegKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        private static string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
        }

        public static AppConfig LoadConfig()
        {
            var configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        return config;
                    }
                }
                catch
                {
                }
            }
            return AppConfig.GetDefault();
        }

        public static void SaveConfig(AppConfig config)
        {
            var configPath = GetConfigPath();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(configPath, json);
        }

        public static bool TaskExists(string taskName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/query /tn \"{taskName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(30000);
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool CreateTask(string taskName, string workDir)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                var xmlContent = GenerateTaskXml(taskName, exePath, workDir);
                var tempXmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
                // 使用UTF-16编码写入XML文件，与Python实现保持一致
                File.WriteAllText(tempXmlPath, xmlContent, Encoding.Unicode);

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/create /xml \"{tempXmlPath}\" /tn \"{taskName}\" /f",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(30000);
                var result = process?.ExitCode == 0;

                try
                {
                    File.Delete(tempXmlPath);
                }
                catch { }

                return result;
            }
            catch
            {
                return false;
            }
        }

        public static bool RemoveTask(string taskName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/delete /tn \"{taskName}\" /f",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(30000);
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string GenerateTaskXml(string taskName, string exePath, string workDir)
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>SYSTEM</Author>
    <Description>在系统启动后以高优先级播放BlueArchive开机音效</Description>
    <URI>\{taskName}</URI>
  </RegistrationInfo>
  <Triggers>
    <BootTrigger>
      <Enabled>true</Enabled>
      <Delay>PT30S</Delay>
    </BootTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
    <Priority>0</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{exePath}""</Command>
      <Arguments>--startup</Arguments>
      <WorkingDirectory>{workDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        public static bool RegistryRunExists(string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegKeyPath);
                return key?.GetValue(valueName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool AddRegistryRun(string valueName, string exePath)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegKeyPath, true);
                key?.SetValue(valueName, exePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool RemoveRegistryRun(string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegKeyPath, true);
                key?.DeleteValue(valueName, false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

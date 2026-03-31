using System.Text.Json.Serialization;

namespace BlueArchiveStartupSounds
{
    public class AppConfig
    {
        [JsonPropertyName("bgm_path")]
        public string BgmPath { get; set; } = "./voice/title_bgm.wav";

        [JsonPropertyName("voice_dir")]
        public string VoiceDir { get; set; } = "./voice/TitleVoice";

        [JsonPropertyName("arona_voice_dir")]
        public string AronaVoiceDir { get; set; } = "./voice/AronaVoice/daily";

        [JsonPropertyName("arona_enter_voice")]
        public string AronaEnterVoice { get; set; } = "./voice/AronaVoice/arona_attendance_enter_3.mp3";

        [JsonPropertyName("arona_tts_voice")]
        public string AronaTtsVoice { get; set; } = "./voice/AronaVoice/arona_default_tts.mp3";

        [JsonPropertyName("delay_seconds")]
        public double DelaySeconds { get; set; } = 7.0;

        [JsonPropertyName("fade_duration")]
        public double FadeDuration { get; set; } = 1.5;

        [JsonPropertyName("bgm_volume")]
        public double BgmVolume { get; set; } = 0.3;

        [JsonPropertyName("voice_volume")]
        public double VoiceVolume { get; set; } = 1.0;

        [JsonPropertyName("kill_lock_engine")]
        public bool KillLockEngine { get; set; } = false;

        [JsonPropertyName("wait_for_lockengine")]
        public bool WaitForLockEngine { get; set; } = false;

        public static AppConfig GetDefault()
        {
            return new AppConfig();
        }

        public AppConfig Clone()
        {
            return new AppConfig
            {
                BgmPath = BgmPath,
                VoiceDir = VoiceDir,
                AronaVoiceDir = AronaVoiceDir,
                AronaEnterVoice = AronaEnterVoice,
                AronaTtsVoice = AronaTtsVoice,
                DelaySeconds = DelaySeconds,
                FadeDuration = FadeDuration,
                BgmVolume = BgmVolume,
                VoiceVolume = VoiceVolume,
                KillLockEngine = KillLockEngine,
                WaitForLockEngine = WaitForLockEngine
            };
        }
    }
}

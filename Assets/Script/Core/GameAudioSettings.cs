using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 배경음/효과음/캐릭터 목소리 음량(0~1)을 보관하고 PlayerPrefs에 저장하는 설정 저장소.
    ///
    /// 주의: 이 프로젝트엔 아직 실제 오디오 재생(AudioSource/AudioMixer)이 하나도 없다.
    /// 그래서 지금 이 값들은 "저장되고 UI에 반영될 뿐, 실제로 들리는 소리를 바꾸지는 않는다".
    /// 나중에 사운드를 붙일 때 각 AudioSource가 OnVolumeChanged를 구독해서 자기 volume에
    /// 반영하거나(또는 AudioMixer 파라미터로 넘기거나) 하면, UI 쪽은 손대지 않아도 된다.
    ///
    /// MonoBehaviour가 아닌 정적 클래스인 이유: 씬에 배치할 오브젝트나 참조 연결 없이 어디서든
    /// 읽고 쓸 수 있어야 하고, 씬을 다시 로드해도(다시하기) 값이 유지돼야 하기 때문.
    /// </summary>
    public static class GameAudioSettings
    {
        private const string BgmKey = "audio.bgm";
        private const string SfxKey = "audio.sfx";
        private const string VoiceKey = "audio.voice";

        private const float DefaultVolume = 0.8f;

        /// <summary>음량 중 하나라도 바뀌면 발행. 나중에 실제 AudioSource들이 구독할 지점.</summary>
        public static event System.Action OnVolumeChanged;

        public static float Bgm { get; private set; }
        public static float Sfx { get; private set; }
        public static float Voice { get; private set; }

        // 정적 생성자 - 이 클래스를 처음 건드리는 순간 자동으로 저장된 값을 불러온다.
        static GameAudioSettings()
        {
            Bgm = PlayerPrefs.GetFloat(BgmKey, DefaultVolume);
            Sfx = PlayerPrefs.GetFloat(SfxKey, DefaultVolume);
            Voice = PlayerPrefs.GetFloat(VoiceKey, DefaultVolume);
        }

        public static void SetBgm(float value) => Set(BgmKey, value, v => Bgm = v);
        public static void SetSfx(float value) => Set(SfxKey, value, v => Sfx = v);
        public static void SetVoice(float value) => Set(VoiceKey, value, v => Voice = v);

        private static void Set(string key, float value, System.Action<float> assign)
        {
            float clamped = Mathf.Clamp01(value);
            assign(clamped);

            // SetFloat는 메모리에만 쓰므로 슬라이더를 드래그하는 동안 매 프레임 호출돼도 부담이 적다.
            // 실제 디스크 기록(Save)은 설정 창을 닫을 때 한 번만 - 모바일에서 잦은 디스크 쓰기를 피함.
            PlayerPrefs.SetFloat(key, clamped);
            OnVolumeChanged?.Invoke();
        }

        /// <summary>메모리에 있는 값을 실제로 디스크에 기록. 설정 창을 닫는 시점에 호출.</summary>
        public static void Save() => PlayerPrefs.Save();
    }
}

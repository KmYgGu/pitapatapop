using UnityEngine;
using JojoPuzzle.Core;

namespace JojoPuzzle.Audio
{
    /// <summary>
    /// 효과음 재생을 한 곳에서 담당한다. 어떤 소리를 쓸지는 여기 인스펙터에 모아두고,
    /// 게임 코드는 <c>PlayPiecePlaced()</c> 처럼 <b>상황</b>만 알린다 - 소리를 바꾸거나 늘릴 때
    /// 게임 코드를 건드리지 않아도 되고, 어떤 소리가 어디서 나는지 한눈에 보인다.
    /// (대사를 SpeechDirector 한 곳이 소유하는 것과 같은 이유다.)
    ///
    /// <b>AudioSource 를 여러 개 돌려쓴다.</b> 하나로 PlayOneShot 만 써도 겹쳐 나긴 하지만
    /// 소리마다 음정(pitch)을 따로 줄 수 없다. 접기 소리처럼 빠르게 반복되는 건 음정을 조금씩
    /// 올려줘야 "따다다닥"으로 들리고, 안 그러면 같은 소리가 뭉쳐서 웅웅거린다.
    /// 개수는 고정이고 Awake 에서 한 번만 만든다 - 재생할 때마다 만들고 버리지 않는다.
    ///
    /// 볼륨은 재생하는 순간 <see cref="GameAudioSettings.Sfx"/> 를 읽는다. 설정이 바뀌어도
    /// 구독·갱신 없이 다음 소리부터 바로 반영된다.
    /// </summary>
    public class SfxPlayer : MonoBehaviour
    {
        [Header("퍼즐 매치")]
        [Tooltip("플레이어가 조각을 놓았을 때(1.넣기).")]
        [SerializeField] private AudioClip piecePlaced;

        [Tooltip("매치된 조각이 한 칸 접혀 넘어갈 때마다(2.반복).")]
        [SerializeField] private AudioClip collectStep;

        [Tooltip("다 모인 조각이 마지막에 사라질 때(3.끝).")]
        [SerializeField] private AudioClip collectFinish;

        [Header("접기 소리 반복")]
        [Tooltip("접기 소리를 다시 트는 간격(초). <b>조각이 접히는 속도와는 무관하다</b> - " +
                 "접기 연출이 진행되는 동안 이 간격으로 계속 울릴 뿐이다.\n" +
                 "소리가 끝나고 잠깐 숨을 고를 만큼은 돼야 한다. 지금 클립은 0.47초쯤 들리므로 " +
                 "0.55면 약 0.08초 쉬고 다시 울린다. 클립을 짧은 것으로 바꾸면 이 값도 같이 줄일 것.")]
        [SerializeField] private float collectStepInterval = 0.55f;

        [Header("재생기")]
        [Tooltip("동시에 울릴 수 있는 소리의 개수. 이보다 많이 겹치면 가장 오래된 것부터 밀려난다.")]
        [Min(1)]
        [SerializeField] private int voiceCount = 8;

        private AudioSource[] voices;
        private int nextVoice;

        // 접기 연출이 몇 개 돌고 있는지. 캐스케이드로 여러 매치가 겹쳐도 소리는 하나의
        // 박자로만 울린다("전체 시간에 맞춰 튼다"는 뜻이 그것) - 그래서 bool 이 아니라 카운터다.
        private int collectLoopCount;
        private float nextCollectStepTime;

        private void Awake()
        {
            voices = new AudioSource[Mathf.Max(1, voiceCount)];
            for (int i = 0; i < voices.Length; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f; // 2D - 퍼즐 효과음은 위치감이 필요 없다
                voices[i] = source;
            }
        }

        /// <summary>플레이어가 조각을 놓았을 때.</summary>
        public void PlayPiecePlaced() => Play(piecePlaced, 1f);

        /// <summary>
        /// 접기 연출이 시작됐다 - 끝날 때까지 접기 소리를 일정한 간격으로 반복한다.
        ///
        /// <b>조각이 접히는 박자에 맞추지 않는다.</b> 큰 매치는 한 칸이 0.03초까지 짧아지는데
        /// 거기 맞추면 소리가 뭉개진다. 소리는 소리대로 편한 간격으로 울리고, 대신 연출이
        /// 오래 걸릴수록 그만큼 여러 번 울린다 - "많이 맞췄다"가 소리 횟수로 전해지는 건 그 덕이다.
        ///
        /// 반드시 <see cref="EndCollectLoop"/>와 짝으로 부를 것.
        /// </summary>
        public void BeginCollectLoop()
        {
            if (collectLoopCount <= 0)
                nextCollectStepTime = 0f; // 첫 소리는 기다리지 않고 바로

            collectLoopCount++;
        }

        /// <summary>접기 연출이 끝났다. 겹쳐 돌던 게 있으면 마지막 하나가 끝날 때 멈춘다.</summary>
        public void EndCollectLoop()
        {
            collectLoopCount = Mathf.Max(0, collectLoopCount - 1);
        }

        /// <summary>다 모인 조각이 마지막에 사라질 때.</summary>
        public void PlayCollectFinish() => Play(collectFinish, 1f);

        /// <summary>
        /// 캐릭터 음성. 효과음이 아니라 <b>음성 볼륨</b>(GameAudioSettings.Voice)을 따른다 -
        /// 설정에서 둘을 따로 조절할 수 있어야 하기 때문이다.
        /// 어떤 음성을 틀지는 부르는 쪽이 정한다(대사마다 녹음이 여러 개라 여기서 목록을 들 수 없다).
        /// </summary>
        public void PlayVoice(AudioClip clip) => Play(clip, 1f, 1f, GameAudioSettings.Voice);

        private void Update()
        {
            if (collectLoopCount <= 0 || Time.time < nextCollectStepTime)
                return;

            nextCollectStepTime = Time.time + Mathf.Max(0.02f, collectStepInterval);
            Play(collectStep, 1f);
        }

        /// <param name="channelVolume">
        /// 어느 볼륨 설정을 따를지. 음수면 효과음 볼륨(GameAudioSettings.Sfx).
        /// </param>
        private void Play(AudioClip clip, float volumeScale, float pitch = 1f, float channelVolume = -1f)
        {
            if (clip == null || voices == null)
                return;

            float volume = (channelVolume >= 0f ? channelVolume : GameAudioSettings.Sfx) * volumeScale;
            if (volume <= 0f)
                return;

            // 가장 오래전에 쓴 것부터 순서대로 돌려쓴다. 재생 중인 게 걸리면 그 소리는 잘리는데,
            // 그때는 이미 voiceCount 개가 동시에 울리고 있다는 뜻이라 하나쯤 잘려도 안 들린다.
            var source = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;

            source.pitch = pitch;
            source.volume = volume;
            source.clip = clip;
            source.Play();
        }
    }
}

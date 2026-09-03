using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 숫자 대신 원형 호가 점점 어두워지는 방식의 제한시간 타이머.
    /// 씬 세팅: 밝은 배경 원(Image) 위에, 이 컴포넌트가 붙은 "어두운 오버레이" Image를 겹쳐서 배치.
    /// 오버레이 Image 설정: Image Type = Filled, Fill Method = Radial 360, Fill Origin = Top,
    /// Clockwise 체크. 시간이 지날수록 fillAmount가 0→1로 커지면서 어두운 부채꼴이 시계방향으로
    /// 자라나고, 1이 되면(=완전히 어두워지면) 타임오버.
    /// </summary>
    public class RadialTimerUI : MonoBehaviour
    {
        [SerializeField] private Image darkOverlay; // 비워두면 이 오브젝트의 Image를 자동으로 사용

        private float totalDuration = 60f;
        private float elapsed;
        private bool isRunning;

        /// <summary>타임오버가 됐을 때 발행. BattleManager 등에서 구독해서 패배 처리하면 됨.</summary>
        public event System.Action OnTimeUp;

        /// <summary>0(시작)~1(타임오버) 사이 진행도.</summary>
        public float Progress => totalDuration > 0f ? Mathf.Clamp01(elapsed / totalDuration) : 1f;

        /// <summary>남은 시간 비율 (1=가득 남음, 0=타임오버).</summary>
        public float RemainingFraction => 1f - Progress;

        /// <summary>남은 시간(초). 초읽기 연출(<see cref="TimerCountdownUI"/>)이 이걸 본다.</summary>
        public float RemainingSeconds => Mathf.Max(0f, totalDuration - elapsed);

        /// <summary>지금 시계가 돌고 있는지. 멈춰 있는 동안은 초읽기도 멈춰야 한다.</summary>
        public bool IsRunning => isRunning;

        private void Awake()
        {
            if (darkOverlay == null)
                darkOverlay = GetComponent<Image>();
        }

        /// <summary>제한시간(초)을 지정해서 타이머 시작. 진행 중이던 타이머는 초기화됨.</summary>
        public void StartTimer(float durationSeconds)
        {
            totalDuration = Mathf.Max(0.01f, durationSeconds);
            elapsed = 0f;
            isRunning = true;
            UpdateVisual();
        }

        /// <summary>시간이 다 차서 끝난 상태인지. 끝난 타이머는 Resume으로 다시 돌지 않는다.</summary>
        public bool IsFinished => elapsed >= totalDuration;

        public void Pause() => isRunning = false;

        /// <summary>
        /// 멈춰둔 타이머를 다시 굴린다. 이미 끝난 타이머는 무시한다 - 그러지 않으면 다음 Update에서
        /// 곧바로 다시 만료 판정에 걸려 OnTimeUp이 두 번 발행된다.
        /// </summary>
        public void Resume()
        {
            if (IsFinished)
                return;

            isRunning = true;
        }

        private void Update()
        {
            if (!isRunning)
                return;

            elapsed += Time.deltaTime;

            if (elapsed >= totalDuration)
            {
                elapsed = totalDuration;
                isRunning = false;
                UpdateVisual();
                OnTimeUp?.Invoke();
                return;
            }

            UpdateVisual();
        }

        private void UpdateVisual()
        {
            if (darkOverlay != null)
                darkOverlay.fillAmount = Progress;
        }
    }
}

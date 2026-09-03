using UnityEngine;
using Spine.Unity;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 리더 캐릭터(PlayerCharImage2)의 Spine 애니메이션을 배틀 진행에 맞춰 전환한다.
    ///
    /// 흐름:
    ///   평소                      → 1.idle (반복)
    ///   스탠드업 배너가 뜨는 순간  → 4.readyattack (반복)  "공격 준비 자세로 버틴다"
    ///   불꽃이 전부 도착한 순간    → 5.attackdone (1회)     "때린다"
    ///   그대로 마지막 프레임 유지  → 스탠드업 종료까지
    ///   스탠드업 종료             → 1.idle (반복)
    ///
    /// 마지막 프레임 유지는 따로 처리할 게 없다. Spine은 loop=false로 재생하면 끝난 뒤
    /// 그 자세를 그대로 붙잡고 있으므로, 다음 SetAnimation이 올 때까지 자연히 정지 상태가 된다.
    ///
    /// 이벤트로만 동작하고 Update를 돌지 않는다 - 이 프로젝트가 계속 지켜온 방식이다.
    /// </summary>
    public class LeaderBattleAnimator : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("리더 초상화 안의 SpineChar에 붙은 SkeletonAnimation. " +
                 "비워두면 자식에서 찾는다.")]
        [SerializeField] private SkeletonAnimation skeletonAnimation;

        [Header("이벤트 출처")]
        [Tooltip("스탠드업 배너. 배너가 뜨는 순간 공격 준비 자세로 바뀐다.")]
        [SerializeField] private StandUpTimeUI standUpTimeUI;

        [Tooltip("불꽃 도착과 스탠드업 종료 알림을 받을 대상.")]
        [SerializeField] private BoardInputController boardInput;

        [Header("애니메이션 이름")]
        [Tooltip("Spine 애니메이션 이름은 대소문자와 공백까지 정확히 맞아야 한다. " +
                 "재작업 후 이름이 바뀌면 여기도 같이 고칠 것.")]
        [SerializeField] private string idleAnimation = "1.idle";
        [SerializeField] private string readyAttackAnimation = "4.readyattack";
        [SerializeField] private string attackDoneAnimation = "5.attackdone";

        [Header("눈 깜빡임 (별도 트랙)")]
        [Tooltip("눈 깜빡임 애니메이션. 몸통 애니메이션과 겹치지 않는 별도 트랙에서 재생되므로 " +
                 "idle/공격 중 어느 때나 자연스럽게 깜빡인다. 비워두면 깜빡임을 끈다.")]
        [SerializeField] private string blinkAnimation = "closeeye";

        [Tooltip("깜빡임 사이 간격(초)의 최소/최대. 이 범위에서 무작위로 뽑아 사람처럼 불규칙하게 만든다.")]
        [SerializeField] private float blinkIntervalMin = 2.5f;
        [SerializeField] private float blinkIntervalMax = 6f;

        [Header("전환")]
        [Tooltip("애니메이션이 서로 섞이는 시간(초). 0이면 딱 끊어서 바뀐다.\n" +
                 "0으로 둔 이유: 도끼(weaponhand)를 1.idle이 전혀 키하지 않아서, 섞는 동안 도끼가 " +
                 "셋업 포즈 쪽으로 되돌아가며 눈에 띄게 움직인다. Spine에서 idle에도 도끼 키를 " +
                 "넣어 잡아주면 그때 다시 올려도 된다.")]
        [SerializeField] private float mixDuration;

        /// <summary>
        /// 몸통 애니메이션이 쓰는 트랙. 이쪽에 idle/readyattack/attackdone이 올라간다.
        /// </summary>
        private const int BodyTrack = 0;

        /// <summary>
        /// 눈 깜빡임 전용 트랙. 몸통과 분리해두면 idle이든 공격 중이든 상관없이 깜빡일 수 있고,
        /// 몸통 애니메이션마다 눈 키를 넣어줄 필요도 없어진다(높은 트랙이 우선 적용됨).
        /// </summary>
        private const int BlinkTrack = 1;

        private float nextBlinkTime;

        private void OnEnable()
        {
            if (standUpTimeUI != null)
                standUpTimeUI.OnBannerShown += HandleBannerShown;

            if (boardInput != null)
            {
                boardInput.OnStandUpAttackStart += HandleAttackStart;
                boardInput.OnStandUpTimeEnd += HandleStandUpEnd;
            }
        }

        private void OnDisable()
        {
            if (standUpTimeUI != null)
                standUpTimeUI.OnBannerShown -= HandleBannerShown;

            if (boardInput != null)
            {
                boardInput.OnStandUpAttackStart -= HandleAttackStart;
                boardInput.OnStandUpTimeEnd -= HandleStandUpEnd;
            }
        }

        private void Start()
        {
            // Spine의 skeleton/AnimationState는 Awake 시점엔 아직 없을 수 있어서 Start에서 잡는다
            // (SkeletonAnimation 주석에 명시된 주의사항).
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

            Play(idleAnimation, true);
            ScheduleNextBlink();
        }

        private void Update()
        {
            if (string.IsNullOrEmpty(blinkAnimation) || skeletonAnimation == null)
                return;

            // Time.time을 쓰므로 일시정지(timeScale=0)에도 시간은 흐른다. 멈춘 화면에서 눈만
            // 깜빡이는 게 어색하면 Time.time 대신 직접 deltaTime을 누적하도록 바꾸면 된다.
            if (Time.time < nextBlinkTime)
                return;

            Blink();
            ScheduleNextBlink();
        }

        private void ScheduleNextBlink()
        {
            float min = Mathf.Max(0.1f, blinkIntervalMin);
            float max = Mathf.Max(min, blinkIntervalMax);
            nextBlinkTime = Time.time + Random.Range(min, max);
        }

        /// <summary>
        /// 눈 깜빡임을 별도 트랙에서 한 번 재생한다.
        ///
        /// 재생 뒤에 <b>빈 애니메이션을 이어 붙이는 게 핵심</b>이다. 그냥 두면 깜빡임이 끝난 자세
        /// (눈 감김일 수도 있는 마지막 프레임)를 트랙이 계속 붙잡고 있어서 눈이 감긴 채로 굳는다.
        /// 빈 애니메이션을 넣으면 트랙이 셋업 포즈로 돌아가며 눈 제어를 놓아, 다음 깜빡임 전까지
        /// 몸통 트랙이 눈을 그대로 두게 된다.
        /// </summary>
        private void Blink()
        {
            var state = skeletonAnimation.AnimationState;
            if (state == null || !HasAnimation(blinkAnimation))
                return;

            state.SetAnimation(BlinkTrack, blinkAnimation, false);
            state.AddEmptyAnimation(BlinkTrack, 0.1f, 0f);
        }

        private void HandleBannerShown()
        {
            Play(readyAttackAnimation, true);
        }

        /// <summary>
        /// 불꽃을 다 흡수하고 한 박자 버틴 뒤에 온다(종료 시퀀스당 한 번).
        ///
        /// loop=false라 재생이 끝나면 마지막 프레임 자세로 그대로 멈춘다.
        /// 스탠드업 종료 알림이 올 때까지 그 자세를 유지하는 게 의도한 동작.
        /// </summary>
        private void HandleAttackStart()
        {
            Play(attackDoneAnimation, false);
        }

        private void HandleStandUpEnd()
        {
            Play(idleAnimation, true);
        }

        private void Play(string animationName, bool loop)
        {
            if (skeletonAnimation == null || string.IsNullOrEmpty(animationName))
                return;

            // 없는 동작은 SpinePlayback 이 그 캐릭터의 idle 로 메운다(2026-08-30).
            SpinePlayback.Play(skeletonAnimation.AnimationState, skeletonAnimation.Skeleton?.Data,
                               animationName, loop, BodyTrack, mixDuration);
        }

        /// <summary>
        /// 없는 이름을 넣으면 Spine이 예외를 던지므로 미리 확인한다. 애니메이션을 재작업하면서
        /// 이름이 바뀌면 여기서 걸러지고, 조용히 실패하는 대신 원인이 로그에 남는다.
        /// </summary>
        private bool HasAnimation(string animationName)
        {
            var skeletonData = skeletonAnimation.Skeleton?.Data;
            if (skeletonData == null)
                return true; // 아직 초기화 전 - 판단할 수 없으니 그냥 통과시킨다

            if (skeletonData.FindAnimation(animationName) != null)
                return true;

            Debug.LogWarning($"[LeaderBattleAnimator] '{animationName}' 애니메이션이 없습니다. " +
                             $"Spine에서 이름이 바뀌었는지 확인하세요.", this);
            return false;
        }
    }
}

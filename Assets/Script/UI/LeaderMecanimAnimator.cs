using Spine.Unity;
using UnityEngine;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 리더 초상화의 Spine 동작을 스탠드업 진행에 맞춰 바꿔주는 다리.
    ///
    ///   배너가 뜸        -> 4.readyattack (기를 모은다)
    ///   불꽃을 다 흡수함 -> 5.attackdone  (내려찍는다)
    ///   스탠드업 종료    -> 1.idle        (돌아온다)
    ///
    /// <b>이름이 Mecanim 인데 Mecanim 을 쓰지 않는다.</b> 2026-08-25 사용자 지시로 <b>모든 Spine 을
    /// 코드 제어(SkeletonAnimation)로 되돌렸다</b> - 클래스 이름만 그대로 두는 이유는 씬의 참조
    /// guid 가 파일에 매여 있어서, 이름을 바꾸면 배틀 씬의 연결을 전부 다시 이어야 하기 때문이다.
    /// (기능이 더 붙을 때 한꺼번에 정리할 것.)
    ///
    /// 재생은 <see cref="SpinePlayback"/> 한 곳을 지난다 - 그 캐릭터에게 없는 동작이면
    /// <b>자기 idle</b> 로 메워준다.
    /// </summary>
    public class LeaderMecanimAnimator : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("리더 초상화 안 SpineChar 의 SkeletonAnimation. 비워두면 자식에서 찾는다.")]
        [SerializeField] private SkeletonAnimation player;

        [Header("구독")]
        [SerializeField] private StandUpTimeUI standUpTimeUI;
        [SerializeField] private BoardInputController boardInput;

        [Header("동작 이름")]
        [Tooltip("스탠드업 배너가 뜰 때. 기를 모으는 자세.")]
        [SerializeField] private string readyAttackAnimation = SpinePlayback.ReadyAttack;

        [Tooltip("불꽃을 다 흡수하고 내려찍을 때.")]
        [SerializeField] private string attackDoneAnimation = SpinePlayback.AttackDone;

        [Tooltip("스탠드업이 끝나고 돌아갈 대기 동작.")]
        [SerializeField] private string idleAnimation = SpinePlayback.Idle;

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
            if (player == null)
                player = GetComponentInChildren<SkeletonAnimation>(true);

            // 시작 자세를 여기서 한 번 정한다 - 애셋에 어떤 값이 저장돼 있든 같은 자세로 시작한다.
            PlayIdle();
        }

        /// <summary>대기 자세로. 다른 화면(승리 연출 등)에서도 부를 수 있게 열어둔다.</summary>
        public void PlayIdle() => Play(idleAnimation, true);

        /// <summary>이겼을 때의 자세. 승리 화면이 대사에 맞춰 부른다.</summary>
        public void PlayWin() => Play(SpinePlayback.Win, true);

        /// <summary>
        /// 스탠드업 배너가 떴다. <b>이 자세는 반복한다</b>(2026-08-27 사용자 지시) -
        /// 스탠드업 타임 10초 내내 기를 모으고 있어야 하는데, 한 번만 재생하면 마지막 프레임에
        /// 굳은 채로 남아 정지 화면처럼 보인다.
        /// </summary>
        private void HandleBannerShown() => Play(readyAttackAnimation, true);

        /// <summary>
        /// 불꽃을 다 흡수하고 한 박자 버틴 뒤에 온다. 몇 번 발행될지 신경 쓸 필요가 없다 -
        /// BoardInputController 가 종료 시퀀스당 한 번만 보낸다.
        /// </summary>
        private void HandleAttackStart() => Play(attackDoneAnimation, false);

        private void HandleStandUpEnd() => PlayIdle();

        private void Play(string animationName, bool loop)
        {
            if (player == null)
                return;

            SpinePlayback.Play(player, animationName, loop);
        }
    }
}

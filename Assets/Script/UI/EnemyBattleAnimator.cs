using Spine.Unity;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 적 초상화의 Spine 동작을 <b>필요할 때 한 번씩</b> 재생시키는 다리 역할.
    ///
    /// <see cref="LeaderMecanimAnimator"/>와 하는 일은 같지만 방향이 다르다. 그쪽은 스탠드업
    /// 이벤트를 구독해서 스스로 반응하는데, 적은 "언제 공격하는지"를 <b>EnemyHarassment 가
    /// 정하고 시키는</b> 구조라 이벤트를 구독하지 않고 <see cref="PlayAttack"/> 을 노출하기만 한다.
    ///
    /// 공격이 끝나면 <b>스스로 대기 자세로 돌아간다</b>. 리더는 스탠드업이 끝날 때까지
    /// 공격 자세로 버텨야 해서 바깥에서 돌려보내지만, 적의 가벼운 방해는 짧게 한 번 하고 마는
    /// 동작이라 돌아가는 것까지 여기서 책임지는 게 맞다.
    ///
    /// <b>Animator(Mecanim) 를 걷어냈다</b>(2026-08-25 사용자 지시로 전부 코드 제어로 되돌림).
    /// 예전에는 상태 기계가 <c>1_idle -> 4_readyattack -> 5_attackdone -> 1_idle</c> 한 줄로만
    /// 이어져 있어서, 대기 자세에서 곧바로 공격 상태를 트리거하면 <b>아무 일도 일어나지 않았다</b>.
    /// 그래서 상태 기계를 타지 않고 <c>Animator.Play</c> 로 목표 상태를 직접 재생하는 우회를
    /// 쓰고 있었는데, 이름으로 재생하는 지금 방식에서는 그 우회 자체가 필요 없다.
    ///
    /// 재생은 <see cref="SpinePlayback"/> 한 곳을 지난다 - 그 캐릭터에게 없는 동작이면
    /// Rabrith 것으로 메워준다.
    /// </summary>
    public class EnemyBattleAnimator : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("적 초상화 안 SpineChar 의 SkeletonAnimation. 비워두면 자식에서 찾는다.")]
        [SerializeField] private SkeletonAnimation player;

        [Header("동작 이름")]
        [Tooltip("공격 동작. <b>Spine 애니메이션 이름 그대로</b>다 - Animator 를 쓸 때처럼 점을 " +
                 "밑줄로 바꿀 필요가 없다.")]
        [SerializeField] private string attackAnimation = SpinePlayback.AttackDone;

        [Tooltip("공격이 끝나고 돌아갈 대기 동작.")]
        [SerializeField] private string idleAnimation = SpinePlayback.Idle;

        [Header("타이밍")]
        [Tooltip("공격 모션 길이(초). 이 시간이 지나면 대기 자세로 돌아간다. " +
                 "5.attackdone 클립 길이가 0.667초라 그 값을 기본으로 둔다.")]
        [SerializeField] private float attackMotionDuration = 0.667f;

        [Tooltip("공격 모션이 시작되고 <b>실제로 내려찍기까지</b> 걸리는 시간(초). " +
                 "맞는 쪽 연출(아군이 놀라는 타이밍)을 여기에 맞춘다.")]
        [SerializeField] private float attackImpactTime = 0.2f;

        /// <summary>
        /// PlayAttack() 을 부르고 나서 <b>실제로 내려찍기까지</b> 걸리는 시간(초).
        /// 방해 연출이 "맞는 타이밍"을 여기에 맞출 수 있게 노출한다.
        /// </summary>
        public float AttackImpactDelay => Mathf.Max(0f, attackImpactTime);

        /// <summary>지금 공격 동작 중인지. 방해가 겹칠 때 참고할 수 있다.</summary>
        public bool IsPlayingAttack => returnToIdleAt >= 0f;

        // 대기 자세로 돌아갈 시각. 음수면 예약 없음 - 그때만 Update가 일을 한다.
        private float returnToIdleAt = -1f;

        private void Start()
        {
            if (player == null)
                player = GetComponentInChildren<SkeletonAnimation>(true);

            PlayIdle();
        }

        /// <summary>공격 모션을 한 번 재생하고, 끝나면 스스로 대기 자세로 돌아간다.</summary>
        public void PlayAttack()
        {
            if (!SpinePlayback.Play(player, attackAnimation, false))
                return;

            returnToIdleAt = Time.time + Mathf.Max(0.05f, attackMotionDuration);
        }

        /// <summary>대기 자세로. 패배 화면처럼 바깥에서 자세를 정해야 할 때도 쓴다.</summary>
        public void PlayIdle()
        {
            returnToIdleAt = -1f;
            SpinePlayback.Play(player, idleAnimation, true);
        }

        /// <summary>이겼을 때의 자세. 패배 화면이 적에게 시킨다.</summary>
        public void PlayWin()
        {
            returnToIdleAt = -1f;
            SpinePlayback.Play(player, SpinePlayback.Win, true);
        }

        private void Update()
        {
            if (returnToIdleAt < 0f || Time.time < returnToIdleAt)
                return;

            PlayIdle();
        }
    }
}

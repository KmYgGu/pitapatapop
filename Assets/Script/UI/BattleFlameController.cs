using UnityEngine;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// HUD 초상화 뒤에서 타오르는 불꽃 오라를 켜고 끄는 컨트롤러.
    ///
    /// 켜지는 조건 두 가지:
    /// 1) 리더 캐릭터 - 스탠드업 타임 동안에만. 시작/종료는 BoardInputController가 발행하는
    ///    OnStandUpTimeStart / OnStandUpTimeEnd에 맞춘다.
    /// 2) 적 - 보스일 때만, 배틀이 시작되는 순간부터 계속 켜져 있음.
    ///
    /// 불꽃 오브젝트 자체는 초상화 Image 뒤에 깔린 Image이고(같은 FlameAura 셰이더 사용),
    /// 여기서는 SetActive만 토글한다. 색은 각 Image의 color로 지정하므로 머티리얼은 공유 하나면 된다.
    /// </summary>
    public class BattleFlameController : MonoBehaviour
    {
        [SerializeField] private BoardInputController boardInput;

        [Header("불꽃 오브젝트 (초상화 뒤에 배치)")]
        [SerializeField] private GameObject leaderFlame;
        [SerializeField] private GameObject enemyFlame;

        [Header("적 설정")]
        [Tooltip("이 적이 보스인지. 보스면 배틀 시작과 동시에 불꽃이 켜진 채로 시작한다. " +
                 "나중에 스테이지/적 데이터가 생기면 그쪽에서 SetEnemyIsBoss로 넘겨주면 됨.")]
        [SerializeField] private bool enemyIsBoss;

        [Header("리더 불꽃 성장 (스탠드업 종료 시 흡수)")]
        [Tooltip("불꽃을 전부 흡수했을 때의 최대 배율.")]
        [SerializeField] private float leaderFlameMaxScale = 2f;

        [Header("보스 불꽃")]
        [Tooltip("보스 불꽃 크기를 <b>리더가 불꽃을 다 모았을 때의 몇 할</b>로 할지" +
                 "(2026-08-28 사용자 지시: '살짝 작을 정도'). " +
                 "0.9면 리더 최대의 90% - 기본 크기 그대로면 보스가 약해 보인다는 지적이었다. " +
                 "리더 최대 배율을 바꾸면 보스도 <b>같이 따라온다</b> - 두 값을 따로 맞추면 " +
                 "한쪽만 고쳤을 때 크기 관계가 어긋난다.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float bossFlameScaleOfLeaderMax = 0.9f;

        private Vector3 leaderFlameBaseScale = Vector3.one;
        private Vector3 enemyFlameBaseScale = Vector3.one;

        private void Awake()
        {
            // 원래 크기를 기억해둔다 - 스탠드업마다 커졌다가 여기로 되돌아온다.
            if (leaderFlame != null)
                leaderFlameBaseScale = leaderFlame.transform.localScale;

            if (enemyFlame != null)
                enemyFlameBaseScale = enemyFlame.transform.localScale;

            // 시작 상태를 여기서 확정 - 씬에 켜둔 채로 저장돼 있어도 규칙대로 맞춰진다.
            SetLeaderFlame(false);
            SetEnemyIsBoss(enemyIsBoss);
        }

        private void OnEnable()
        {
            if (boardInput == null)
                return;

            // 이벤트 구독은 BoardInputController.Initialize와 무관하게 언제든 가능하다
            // (이벤트 자체는 컴포넌트가 살아있는 한 존재하므로 실행 순서를 신경 쓰지 않아도 됨).
            boardInput.OnStandUpTimeStart += HandleStandUpStart;
            boardInput.OnStandUpTimeEnd += HandleStandUpEnd;
            boardInput.OnStandUpFlameArrived += HandleFlameArrived;
        }

        private void OnDisable()
        {
            if (boardInput == null)
                return;

            boardInput.OnStandUpTimeStart -= HandleStandUpStart;
            boardInput.OnStandUpTimeEnd -= HandleStandUpEnd;
            boardInput.OnStandUpFlameArrived -= HandleFlameArrived;
        }

        private void HandleStandUpStart() => SetLeaderFlame(true);

        private void HandleStandUpEnd() => SetLeaderFlame(false);

        /// <summary>
        /// 흡수한 불꽃 진행도(0~1)에 맞춰 리더 불꽃을 1배에서 최대 배율까지 키운다.
        /// 보드 쪽이 "몇 개 중 몇 번째"를 이미 비율로 계산해서 넘겨주므로, 불꽃이 하나면
        /// 첫 도착에 1.0이 와서 즉시 최대가 되고 여러 개면 그 수만큼 나눠서 커진다.
        /// </summary>
        private void HandleFlameArrived(float progress01)
        {
            if (leaderFlame == null)
                return;

            float scale = Mathf.Lerp(1f, leaderFlameMaxScale, Mathf.Clamp01(progress01));
            leaderFlame.transform.localScale = leaderFlameBaseScale * scale;
        }

        /// <summary>적이 보스인지 설정하고 불꽃을 즉시 반영. 배틀 시작 시 스테이지 데이터로 호출할 지점.</summary>
        public void SetEnemyIsBoss(bool isBoss)
        {
            enemyIsBoss = isBoss;

            if (enemyFlame == null)
                return;

            // 리더가 다 모았을 때보다 <b>살짝 작게</b>. 둘의 기본 크기가 같아서 리더 최대 배율에
            // 비율만 곱하면 된다 - 보스 쪽 배율을 따로 적어두면 리더 값을 바꿨을 때 어긋난다.
            enemyFlame.transform.localScale =
                enemyFlameBaseScale * (leaderFlameMaxScale * bossFlameScaleOfLeaderMax);

            enemyFlame.SetActive(isBoss);
        }

        private void SetLeaderFlame(bool active)
        {
            if (leaderFlame == null)
                return;

            // 켜고 끌 때마다 크기를 원래대로 되돌린다 - 안 그러면 지난 스탠드업에서 커진 채로
            // 다음 스탠드업이 시작돼서 회를 거듭할수록 계속 커진다.
            leaderFlame.transform.localScale = leaderFlameBaseScale;
            leaderFlame.SetActive(active);
        }
    }
}

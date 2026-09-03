using UnityEngine;
using JojoPuzzle.Battle;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 배틀이 끝난 뒤 화면들이 <b>어떤 순서로 오는지 아는 유일한 곳</b>
    /// (<see cref="StageSelect.StageSelectFlow"/> 와 같은 방침).
    ///
    /// <code>
    ///   배틀 종료 → 승리 연출(BattleResultPanel)
    ///             → 1. 결과 화면(BattleRewardPanel)        - 최종 점수 + 획득 골드
    ///             → 2. 스테이지 클리어 화면                - <b>건너뛴다</b>(사용자 지시,
    ///                                                        스테이지 선택 UI 를 다시 만든 뒤에)
    ///             → 3. 캐릭터 결과 화면(BattleCharacterPanel)
    ///             → 확인 -> 아파트 (그 화면이 직접 간다)
    ///
    ///   패배     → 패배 화면(BattleDefeatPanel) → 1·3번 화면(승리와 같음) -> 아파트
    ///              경험치만 4분의 1이고 적 레벨 골드 보너스는 안 붙는다.
    /// </code>
    ///
    /// <b>승패는 마무리 처리가 끝난 뒤에 정해진다</b>(BattleManager.ResolveRoutine) -
    /// 타임오버로 끝났어도 마무리에서 적을 눕히면 승리로 들어온다.
    ///
    /// <b>패널들은 다음 화면을 모른다.</b> "플레이어가 넘기겠다고 했다"만 알리고, 그 다음에
    /// 무엇이 오는지는 전부 여기서 정한다. 그래서 2·3번이 생겨도 패널 코드는 안 고친다.
    ///
    /// <b>이 흐름의 끝은 캐릭터 결과 화면이다.</b> 거기서 확인을 누르면 아파트로 나가므로
    /// 여기서 더 이어붙일 게 없다. 2번이 생기면 결과 화면과 캐릭터 화면 사이에 끼우면 된다.
    /// </summary>
    public class BattleResultFlow : MonoBehaviour
    {
        [SerializeField] private BattleManager battleManager;

        [Tooltip("승리 연출. 적이 날아가고 승리 대사가 오가는 화면.")]
        [SerializeField] private BattleResultPanel victoryPanel;

        [Tooltip("1. 결과 화면 - 최종 점수와 획득 골드 정산.")]
        [SerializeField] private BattleRewardPanel rewardPanel;

        // 2. 스테이지 클리어 화면 - 스테이지 선택 UI 를 다시 만든 뒤에 붙인다(사용자 지시로 건너뜀).

        [Tooltip("3. 캐릭터 결과 화면 - 이번 판에 쓴 캐릭터 6칸. 여기서 확인을 누르면 아파트로 나간다.")]
        [SerializeField] private BattleCharacterPanel characterPanel;

        [Tooltip("패배 화면. '패배..' + 적 Spine + 적의 대사. 여기서 터치하면 아파트로 나간다.")]
        [SerializeField] private BattleDefeatPanel defeatPanel;

        [Header("초상화 클로즈업")]
        [Tooltip("패배 화면 직전에 화면 한가운데로 끌어올 <b>배틀 화면의</b> 적 초상화. " +
                 "승리 쪽은 승리 화면이 자기 순서 안에서 직접 돌린다(순서를 그 화면이 소유하므로).")]
        [SerializeField] private PortraitCloseUpUI enemyCloseUp;

        [Header("배경")]
        [Tooltip("<b>결과 화면부터</b> 쓰는 새 배경. 퍼즐판 위에 창만 겹쳐 띄우니 조잡해 보인다는 " +
                 "지적(2026-08-25)에 따라, 여기서부터는 배틀 화면을 완전히 덮는다. " +
                 "승리 연출은 배틀 위에서 그대로 하므로 그때는 켜지 않는다.")]
        [SerializeField] private CanvasGroup resultBackground;

        [Tooltip("새 배경이 밝아지는 시간(초).")]
        [SerializeField] private float backgroundFadeDuration = 0.3f;

        // 이번 판의 결과. 화면마다 다시 물어보지 않도록 한 번 받아 들고 있는다.
        private BattleOutcome outcome;
        private bool hasOutcome;

        private void OnEnable()
        {
            if (battleManager != null)
                battleManager.OnBattleEnded += HandleBattleEnded;

            if (victoryPanel != null)
                victoryPanel.OnAdvanceRequested += ShowReward;

            if (rewardPanel != null)
                rewardPanel.OnAdvanceRequested += ShowCharacterResult;

            if (defeatPanel != null)
                defeatPanel.OnAdvanceRequested += ShowReward;
        }

        private void OnDisable()
        {
            if (battleManager != null)
                battleManager.OnBattleEnded -= HandleBattleEnded;

            if (victoryPanel != null)
                victoryPanel.OnAdvanceRequested -= ShowReward;

            if (rewardPanel != null)
                rewardPanel.OnAdvanceRequested -= ShowCharacterResult;

            if (defeatPanel != null)
                defeatPanel.OnAdvanceRequested -= ShowReward;
        }

        private void HandleBattleEnded(BattleOutcome value)
        {
            outcome = value;
            hasOutcome = true;

            // 패배는 승리 연출을 거치지 않는다 - 적이 날아갈 일도 승리 대사도 없다.
            // 새 배경을 깔고 곧바로 패배 화면으로 간다.
            if (value.result == BattleResult.Defeat)
            {
                StartCoroutine(ShowDefeatRoutine());
                return;
            }

            // 승리 연출은 스스로 이 이벤트를 구독해서 뜬다 - 여기서 부르지 않는다.
            // 패배는 화면 자체가 없어서 아무 데도 가지 않는다.
        }

        private void ShowReward()
        {
            if (!hasOutcome || rewardPanel == null)
                return;

            // 패배 화면에서 넘어온 경우의 뒷정리. 승리 쪽은 승리 화면이 스스로 하지만
            // 패배 화면은 이 흐름이 띄웠으니 치우는 것도 여기서 한다.
            //
            //  - <b>패배 화면을 닫는다</b>: Canvas 순서상 결과 화면보다 <b>위</b>에 그려져서,
            //    안 닫으면 '패배..'와 적 대사가 영수증을 덮는다.
            //  - <b>클로즈업을 푼다</b>: 확대 중에는 overrideSorting 으로 맨 앞에 서 있어서
            //    그대로 두면 다음 화면들 위에 적이 계속 얹혀 있다(승리 쪽에서 겪은 것과 같다).
            defeatPanel?.Hide();
            enemyCloseUp?.Reset();

            StartCoroutine(ShowRewardRoutine());
        }

        /// <summary>
        /// 새 배경을 먼저 깔고 결과 화면을 띄운다.
        ///
        /// <b>배경을 켜는 게 흐름의 일인 이유</b>: 이 배경은 결과 화면 하나가 아니라
        /// <b>그 뒤 화면 전부</b>가 함께 쓴다. 화면마다 자기 배경을 들고 있으면 화면이 바뀔 때
        /// 배경이 껐다 켜지며 깜빡인다.
        /// </summary>
        /// <summary>
        /// 패배 화면. 승리 쪽과 <b>같은 새 배경</b>을 깐다 - 퍼즐판 위에 창만 겹쳐 띄우지 않는다.
        /// 이 화면에서 터치하면 그 화면이 직접 아파트로 나가므로 여기서 이어붙일 게 없다.
        /// </summary>
        private System.Collections.IEnumerator ShowDefeatRoutine()
        {
            // <b>배경을 먼저 깔고 그 위로 초상화를 끌어온다.</b> 순서가 반대면 클로즈업이
            // 끝난 뒤에 배경이 덮으면서 방금 키운 얼굴을 한 번 가렸다가 다시 꺼내게 된다.
            yield return FadeInBackground();
            yield return PlayCloseUp(enemyCloseUp);

            defeatPanel?.Show();
        }

        /// <summary>
        /// 배틀 화면에 서 있던 초상화를 화면 한가운데로 끌어온다(2026-08-25 사용자 지시).
        /// 승리·패배 <b>둘 다 같은 방식</b>이라 여기 한 곳에 둔다.
        /// </summary>
        private System.Collections.IEnumerator PlayCloseUp(PortraitCloseUpUI closeUp)
        {
            if (closeUp != null)
                yield return closeUp.Play();
        }



        private System.Collections.IEnumerator ShowRewardRoutine()
        {
            yield return FadeInBackground();

            rewardPanel.Show(outcome);
        }

        private System.Collections.IEnumerator FadeInBackground()
        {
            if (resultBackground == null)
                yield break;

            // 이미 깔려 있으면 다시 밝히지 않는다 - 패배 경로는 패배 화면에서 한 번 깔고
            // 결과 화면에서 또 지나가는데, 그때 0부터 다시 올리면 화면이 한 번 껌뻑인다.
            if (resultBackground.gameObject.activeSelf && resultBackground.alpha >= 1f)
                yield break;

            resultBackground.gameObject.SetActive(true);

            if (backgroundFadeDuration <= 0f)
            {
                resultBackground.alpha = 1f;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < backgroundFadeDuration)
            {
                elapsed += Time.deltaTime;
                resultBackground.alpha = Mathf.Clamp01(elapsed / backgroundFadeDuration);
                yield return null;
            }

            resultBackground.alpha = 1f;
        }

        /// <summary>
        /// 3. 캐릭터 결과 화면. <b>2번(스테이지 클리어)은 아직 건너뛴다</b> - 스테이지 선택 UI 를
        /// 다시 만든 뒤에 붙이기로 했다(사용자 지시). 그때는 이 자리에서 2번을 띄우고,
        /// 2번의 넘기기 이벤트에서 이 함수를 부르면 된다.
        ///
        /// <b>결과 화면을 닫지 않고 그 위에 띄운다</b> - 닫으면 그 사이에 배틀 화면이 한 프레임
        /// 드러난다. 캐릭터 화면이 뒤를 완전히 덮으므로 밑에 남아 있어도 보이지 않는다.
        /// </summary>
        private void ShowCharacterResult()
        {
            characterPanel?.Show(outcome);
        }
    }
}

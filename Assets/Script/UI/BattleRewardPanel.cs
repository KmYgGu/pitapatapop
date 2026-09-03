using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.Battle;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 사용자 용어로 <b>"결과 화면"</b> - 승리 연출 다음에 오는 <b>정산 화면</b>이다.
    /// 최종 점수와 획득 골드를 알린다.
    ///
    /// <b>이름 주의</b>: 그 앞의 승리 연출(적이 날아가고 대사가 오가는 화면)은
    /// <see cref="BattleResultPanel"/> 이다. "result"와 "reward"로 갈라져 있으니 헷갈리지 말 것.
    ///
    /// 결과 뒤에 올 화면들(2026-08-25 사용자 기획):
    /// <code>
    ///   1. 결과 화면        ← 이 클래스. 최종 점수 + 획득 골드
    ///   2. 스테이지 클리어  ← 아직 없음. 스테이지 선택 UI 를 다시 만든 뒤에 붙인다
    ///   3. 캐릭터 결과      ← 아직 없음
    /// </code>
    /// 순서를 아는 건 이 클래스가 아니라 <see cref="BattleResultFlow"/> 다.
    ///
    /// <b>골드는 영수증처럼 한 줄씩 쌓아 보여준다</b>(사용자 지시). 계산 자체는
    /// <see cref="GoldReward"/> 가 하고 여기는 그 줄들을 그리기만 한다.
    /// </summary>
    public class BattleRewardPanel : MonoBehaviour
    {
        [Header("화면")]
        [Tooltip("결과 화면 전체. <b>평소엔 꺼져 있다.</b> 이 컴포넌트는 항상 켜져 있는 부모에 " +
                 "붙어야 한다 - 꺼진 오브젝트에 붙으면 흐름이 부르지 못한다.")]
        [SerializeField] private GameObject root;

        [Tooltip("뒤를 덮는 판. 알파를 0에서 원래 값까지 올리며 나타난다.")]
        [SerializeField] private Graphic backdrop;

        [SerializeField] private Text scoreText;

        [Tooltip("지금까지 쌓인 골드. 줄이 하나 뜰 때마다 이 숫자가 올라간다.")]
        [SerializeField] private Text goldText;

        [Tooltip("골드 숫자가 오를 때 말랑 튕기게 한다. 없어도 된다.")]
        [SerializeField] private SquashPunch goldPunch;

        [Header("영수증")]
        [Tooltip("줄들이 쌓일 자리. 세로로 직접 배치하므로 pivot 은 위(0.5, 1)여야 한다.")]
        [SerializeField] private RectTransform receiptContent;

        [Tooltip("줄 하나의 본. <b>꺼진 채로</b> 두면 복제해서 쓴다. " +
                 "자식 이름은 LabelText / AddedText / TotalText 여야 한다 - " +
                 "이름으로 찾으므로 바꾸면 연결이 조용히 끊긴다.")]
        [SerializeField] private GameObject receiptRowTemplate;

        [Tooltip("줄 하나의 높이(유닛).")]
        [SerializeField] private float rowHeight = 26f;

        [Tooltip("줄 사이 간격(유닛).")]
        [SerializeField] private float rowSpacing = 4f;

        [Header("데이터")]
        [Tooltip("'획득 코인량 증가' 아이템의 배율을 읽는 데 쓴다. 비워두면 그 보너스가 없는 것으로 친다.")]
        [SerializeField] private BattleItemCatalog battleItemCatalog;

        [Tooltip("최종 점수를 가져올 곳. <b>승리 화면과 같은 곳을 봐야 한다</b> - " +
                 "러시 타임에 번 점수는 BattleOutcome 의 누적 데미지에 안 들어간다(그때는 이미 " +
                 "적 체력이 0이라 데미지가 버려진다). 비워두면 누적 데미지로 물러선다.")]
        [SerializeField] private ScoreUI scoreUI;

        [Header("타이밍")]
        [Tooltip("뒤 판이 밝아지는 시간(초).")]
        [SerializeField] private float backdropFadeDuration = 0.25f;

        [Tooltip("화면이 뜨고 첫 줄이 찍히기까지(초).")]
        [SerializeField] private float firstRowDelay = 0.35f;

        [Tooltip("줄과 줄 사이 간격(초). 영수증이 <b>한 줄씩</b> 찍히는 느낌을 내는 값이다.")]
        [SerializeField] private float rowInterval = 0.32f;

        [Tooltip("마지막 줄 뒤 터치를 받기까지의 뜸(초).")]
        [SerializeField] private float afterLastRowDelay = 0.3f;

        [Tooltip("줄이 찍힌 직후 이만큼은 터치를 무시한다(초).")]
        [SerializeField] private float tapGraceSeconds = 0.25f;

        /// <summary>
        /// 플레이어가 넘기겠다고 터치한 순간 발행. 다음은 <b>2. 스테이지 클리어 화면</b>인데
        /// 아직 없어서 지금은 <see cref="BattleResultFlow"/> 가 받아 아무것도 하지 않는다.
        /// </summary>
        public event System.Action OnAdvanceRequested;

        /// <summary>이 화면이 떠 있는지.</summary>
        public bool IsShowing => root != null && root.activeSelf;

        /// <summary>이번 판에 실제로 지급한 골드. 다음 화면이 참고할 수 있게 남겨둔다.</summary>
        public int GrantedGold { get; private set; }

        // 영수증 줄. 매번 새로 만들지 않고 재사용한다(이 프로젝트의 버퍼/풀링 방침).
        private readonly List<GoldRewardLine> lines = new List<GoldRewardLine>();
        private readonly List<RewardRow> rows = new List<RewardRow>();
        private readonly StringBuilder builder = new StringBuilder(48);

        private Color backdropColor;
        private Coroutine routine;

        /// <summary>복제해둔 줄 하나. 매번 transform.Find 로 다시 찾지 않으려고 묶어둔다.</summary>
        private struct RewardRow
        {
            public GameObject go;
            public RectTransform rect;
            public Text label;
            public Text added;
            public Text total;
        }

        private void Awake()
        {
            if (backdrop != null)
                backdropColor = backdrop.color;

            if (receiptRowTemplate != null)
                receiptRowTemplate.SetActive(false);

            if (root != null)
                root.SetActive(false);
        }

        /// <summary>결과 화면을 띄운다. 흐름(<see cref="BattleResultFlow"/>)이 부른다.</summary>
        public void Show(BattleOutcome outcome)
        {
            if (routine != null)
                StopCoroutine(routine);

            routine = StartCoroutine(ShowRoutine(outcome));
        }

        /// <summary>화면을 닫는다.</summary>
        public void Hide()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            if (root != null)
                root.SetActive(false);
        }

        private IEnumerator ShowRoutine(BattleOutcome outcome)
        {
            int gold = GoldReward.Build(BuildInput(outcome), lines);

            // 줄은 하나씩 찍힐 것이므로 켜기 전에 전부 감춘다.
            EnsureRows(lines.Count);
            for (int i = 0; i < rows.Count; i++)
                rows[i].go.SetActive(false);

            if (scoreText != null)
                scoreText.text = FormatGrouped(ResolveScore(outcome));

            if (goldText != null)
                goldText.text = FormatGrouped(0);

            if (backdrop != null)
            {
                var start = backdropColor;
                start.a = 0f;
                backdrop.color = start;
            }

            if (root != null)
                root.SetActive(true);

            yield return FadeInBackdrop();

            if (firstRowDelay > 0f)
                yield return new WaitForSeconds(firstRowDelay);

            // ── 영수증을 한 줄씩 ──────────────────────────────────────────
            for (int i = 0; i < lines.Count; i++)
            {
                ApplyRow(i, lines[i], outcome);
                rows[i].go.SetActive(true);

                // 큰 골드 숫자는 그 줄까지의 합계를 따라간다 - 줄이 쌓일수록 올라가는 게 보인다.
                if (goldText != null)
                {
                    goldText.text = FormatGrouped(lines[i].runningTotal);
                    goldPunch?.Play();
                }

                if (i < lines.Count - 1 && rowInterval > 0f)
                    yield return new WaitForSeconds(rowInterval);
            }

            // 골드를 실제로 넣는 건 여기 한 곳이다.
            //
            // <b>PlayerProfile 은 아직 저장되지 않는다</b> - 씬을 다시 열면 원래 값으로 돌아간다.
            // 세이브가 생기면 고칠 곳도 여기 한 곳이다.
            GrantedGold = gold;
            PlayerProfile.Gold += gold;

            if (afterLastRowDelay > 0f)
                yield return new WaitForSeconds(afterLastRowDelay);

            yield return TapGate.Wait(0f, tapGraceSeconds);

            routine = null;
            OnAdvanceRequested?.Invoke();
        }

        /// <summary>최종 점수. 승리 화면과 같은 출처를 봐야 두 화면의 숫자가 어긋나지 않는다.</summary>
        private int ResolveScore(BattleOutcome outcome)
            => scoreUI != null ? scoreUI.CurrentScore : outcome.totalDamageDealt;

        private GoldRewardInput BuildInput(BattleOutcome outcome)
        {
            return new GoldRewardInput
            {
                piecesMatched = outcome.totalPiecesMatched,

                // 러시 스티커는 <b>러시 몫에만</b> 붙는다(시트: "러시 타임에서 획득한 총 코인+N%").
                rushGold = Mathf.RoundToInt(outcome.rushGold * (1f + StickerEffects.RushCoinBonus())),
                playerLevel = PlayerProfile.Level,

                // 적 레벨 = 스테이지의 권장 레벨(준비 화면도 그 값을 적의 레벨로 보여준다).
                enemyLevel = StageEntry.Stage != null ? StageEntry.Stage.recommendedLevel : 0,
                isVictory = outcome.result == BattleResult.Victory,
                coinItemMultiplier = ResolveCoinItemMultiplier(),

                // 붙여 둔 스티커가 코인에 보태는 몫. 아무것도 안 붙였으면 배율 1 · 덧셈 0이라
                // 영수증에 줄이 안 생긴다(예전 동작 그대로).
                stickerBookMultiplier = 1f + StickerEffects.CoinBonus(),
                stickerSkillCoins = Mathf.RoundToInt(
                    StickerEffects.ValueOf(StickerEffect.SkillCountCoin) * outcome.skillsUsed),
                stickerBigHitCoins = outcome.bigHitCoins
            };
        }

        /// <summary>
        /// 배틀 전에 "획득 코인량 증가"를 샀으면 그 배율. 안 샀으면 1.
        ///
        /// 배율을 <see cref="StageEntry"/> 에 복사해두지 않고 카탈로그에서 그때그때 읽는다 -
        /// 수치의 출처가 둘이면 아이템 값을 고쳤을 때 한쪽만 바뀐다.
        /// </summary>
        private float ResolveCoinItemMultiplier()
        {
            if (battleItemCatalog == null || battleItemCatalog.items == null)
                return 1f;

            if (!StageEntry.IsItemSelected(BattleItemKind.CoinUp))
                return 1f;

            for (int i = 0; i < battleItemCatalog.items.Length; i++)
            {
                var item = battleItemCatalog.items[i];
                if (item != null && item.kind == BattleItemKind.CoinUp)
                    return Mathf.Max(1f, item.value);
            }

            return 1f;
        }

        /// <summary>줄 하나에 문구를 채운다.</summary>
        private void ApplyRow(int index, GoldRewardLine line, BattleOutcome outcome)
        {
            var row = rows[index];

            if (row.label != null)
                row.label.text = LabelOf(line, outcome);

            // 첫 줄은 밑돌이라 "+"가 어색하다 - 그 줄만 합계와 같은 값이 두 번 나오는 셈이라
            // 더한 몫을 비워두고 합계만 보여준다.
            if (row.added != null)
                row.added.text = index == 0 ? string.Empty : "+" + FormatGrouped(line.added);

            if (row.total != null)
                row.total.text = FormatGrouped(line.runningTotal);
        }

        private string LabelOf(GoldRewardLine line, BattleOutcome outcome)
        {
            switch (line.source)
            {
                case GoldRewardSource.Base:
                    return "기본  " + outcome.totalPiecesMatched + "개 제거";
                case GoldRewardSource.PlayerLevel:
                    return "플레이어 Lv." + PlayerProfile.Level + "  " + FormatMultiplier(line.multiplier);
                case GoldRewardSource.EnemyLevel:
                    return "적 Lv." + (StageEntry.Stage != null ? StageEntry.Stage.recommendedLevel : 0)
                           + "  " + FormatMultiplier(line.multiplier);
                case GoldRewardSource.RushTime:
                    return "러시 타임";
                case GoldRewardSource.CoinItem:
                    return "코인 증가  " + FormatMultiplier(line.multiplier);
                case GoldRewardSource.StickerBook:
                    return "스티커북  " + FormatMultiplier(line.multiplier);
                case GoldRewardSource.StickerSkillCount:
                    return "스티커북 - 쓴 스킬";
                case GoldRewardSource.StickerBigHit:
                    return "스티커북 - 큰 한 방";
                default:
                    return string.Empty;
            }
        }

        private static string FormatMultiplier(float multiplier) => "x" + multiplier.ToString("0.##");

        /// <summary>천 단위 콤마. 형식을 ScoreUI 와 맞춘다.</summary>
        private string FormatGrouped(int value)
        {
            builder.Length = 0;
            ScoreUI.AppendGrouped(builder, value);
            return builder.ToString();
        }

        /// <summary>
        /// 줄이 모자라면 본을 복제해서 채운다. <b>한 번 만든 줄은 버리지 않고 다시 쓴다</b> -
        /// 판마다 만들고 버리면 그때마다 할당이 생긴다.
        /// </summary>
        private void EnsureRows(int needed)
        {
            if (receiptRowTemplate == null || receiptContent == null)
                return;

            while (rows.Count < needed)
            {
                var go = Instantiate(receiptRowTemplate, receiptContent);
                go.name = "ReceiptRow" + rows.Count;

                var rect = (RectTransform)go.transform;

                // 위에서 아래로 쌓는다. 부모 pivot 이 위쪽이라 y 가 음수로 내려간다.
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, rowHeight);
                rect.anchoredPosition = new Vector2(0f, -rows.Count * (rowHeight + rowSpacing));

                rows.Add(new RewardRow
                {
                    go = go,
                    rect = rect,
                    label = FindText(go.transform, "LabelText"),
                    added = FindText(go.transform, "AddedText"),
                    total = FindText(go.transform, "TotalText")
                });
            }
        }

private IEnumerator FadeInBackdrop()
        {
            if (backdrop == null || backdropFadeDuration <= 0f)
            {
                if (backdrop != null)
                    backdrop.color = backdropColor;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < backdropFadeDuration)
            {
                elapsed += Time.deltaTime;

                var c = backdropColor;
                c.a = backdropColor.a * Mathf.Clamp01(elapsed / backdropFadeDuration);
                backdrop.color = c;

                yield return null;
            }

            backdrop.color = backdropColor;
        }
    }
}

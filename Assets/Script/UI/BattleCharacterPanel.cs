using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 사용자 용어로 <b>"캐릭터 관련 결과 화면"</b> - 배틀 후 화면들의 마지막이다(3번).
    /// 이번 판에 쓰인 <b>퍼즐 색 6칸</b>을 캐릭터별로 늘어놓고, 각자 조각을 몇 개 썼는지 알린다.
    ///
    /// <code>
    ///   위    : 메인 화면과 같은 상태 표시줄 (레벨·경험치·골드·재화·하트)
    ///   가운데: 6슬롯 - 아이콘 | 이름·레벨 / 경험치 바·획득 경험치 / 쓴 조각 수·그 캐릭터의 소지금
    ///   아래  : 확인 -> 아파트로
    /// </code>
    ///
    /// <b>이 화면이 캐릭터에게 실제로 무언가를 준다.</b> 스테이지 경험치와 소지금 둘 다
    /// 여기서 한 번만 들어간다(GrantOnce).
    ///
    /// <b>6칸은 보드 팔레트 그대로다</b>(<see cref="Battle.BattleSetup.BuildPalette"/>) - 0=리더,
    /// 1=파트너, 나머지는 보유 캐릭터에서 무작위. 보유 캐릭터가 모자라 팔레트를 다 못 채우면
    /// 그 칸은 <b>"보유하고 있지 않음"</b> 으로 나온다(극초창기에만 보게 될 그림이다).
    ///
    /// <b>쓴 조각 수는 그 캐릭터의 돈이 된다</b>(<see cref="CharacterWallet"/>, 아파트에서 쓸 예정).
    /// 넣는 자리는 이 화면 한 곳뿐이다.
    ///
    /// 상태 표시줄은 아파트·준비 화면과 <b>같은 컴포넌트</b>(<see cref="PlayerStatusBar"/>)를 쓴다 -
    /// 구현이 두 벌이면 하트처럼 시계가 도는 값이 반드시 어긋난다.
    /// </summary>
    public class BattleCharacterPanel : MonoBehaviour
    {
        [Header("화면")]
        [Tooltip("화면 전체. <b>평소엔 꺼져 있다.</b> 이 컴포넌트는 항상 켜져 있는 부모에 붙어야 한다.")]
        [SerializeField] private GameObject root;

        [Tooltip("뒤를 덮는 판.")]
        [SerializeField] private Graphic backdrop;

        [Header("데이터")]
        [Tooltip("이번 판의 팔레트(어느 칸이 어느 캐릭터인지)를 읽는 곳.")]
        [SerializeField] private BoardView boardView;

        [Tooltip("색깔별로 조각을 몇 개 썼는지 읽는 곳.")]
        [SerializeField] private BoardInputController inputController;

        [Header("경험치 연출")]
        [Tooltip("경험치 바가 이번 판에 받은 만큼 차오르는 데 걸리는 시간(초). " +
                 "레벨이 여러 번 올라도 <b>전체가 이 시간</b>이 되도록 나눠 쓴다.")]
        [SerializeField] private float expFillDuration = 0.9f;

        [Tooltip("레벨이 오를 때 알림 글자를 띄워둘 시간(초).")]
        [SerializeField] private float levelUpHoldSeconds = 0.7f;

        [SerializeField] private string levelUpLabel = "LEVEL UP!";

        [Header("슬롯")]
        [Tooltip("슬롯들이 쌓일 자리. 세로로 직접 배치하므로 pivot 은 위(0.5, 1)여야 한다.")]
        [SerializeField] private RectTransform slotContent;

        [Tooltip("슬롯 하나의 본. <b>꺼진 채로</b> 두면 복제해서 쓴다. 자식 이름은 " +
                 "Icon / NameText / LevelText / ExpBarFill / ExpText / ExpGainText / PiecesText / " +
                 "MoneyText / EmptyText 여야 한다 - " +
                 "이름으로 찾으므로 바꾸면 연결이 조용히 끊긴다.")]
        [SerializeField] private GameObject slotTemplate;

        [Tooltip("슬롯 개수. 보드 팔레트와 같아야 한다(6).")]
        [SerializeField] private int slotCount = 6;

        [Tooltip("슬롯 하나의 높이(유닛).")]
        [SerializeField] private float slotHeight = 58f;

        [Tooltip("슬롯 사이 간격(유닛).")]
        [SerializeField] private float slotSpacing = 6f;

        [Header("버튼")]
        [Tooltip("누르면 아파트(메인 화면)로 간다.")]
        [SerializeField] private Button confirmButton;

        [Header("문구")]
        [SerializeField] private string emptyLabel = "보유하고 있지 않음";

        [Tooltip("쓴 조각 수 뒤에 붙일 글자.")]
        [SerializeField] private string piecesSuffix = "개 사용";

        [Tooltip("획득 경험치 앞에 붙일 글자.")]
        [SerializeField] private string expGainPrefix = "+";

        [Tooltip("캐릭터 소지금 앞에 붙일 글자. <b>플레이어 골드가 아니라 그 캐릭터 개인의 돈</b>이다.")]
        [SerializeField] private string moneyPrefix = "소지금 ";

        /// <summary>이 화면이 떠 있는지.</summary>
        public bool IsShowing => root != null && root.activeSelf;

        private struct CharacterSlot
        {
            public GameObject go;
            public Image icon;
            public Text name;
            public Text level;
            public Image expFill;
            public Text exp;
            public Text expGain;

            /// <summary>레벨이 오르는 순간에만 잠깐 뜨는 글자. 없어도 된다.</summary>
            public Text levelUp;
            public Text pieces;
            public Text money;
            public GameObject empty;

            /// <summary>캐릭터가 있을 때만 보여줄 것들. "보유하고 있지 않음" 일 때 한꺼번에 감춘다.</summary>
            public GameObject[] filledOnly;
        }

        private readonly List<CharacterSlot> slots = new List<CharacterSlot>();

        /// <summary>슬롯 배열 상한. 보드 팔레트는 6칸이지만 여유를 둔다.</summary>
        private const int MaxSlots = 8;

        // 보상은 한 판에 한 번만 들어가야 한다. 화면을 다시 띄워도 또 주면 안 된다.
        private bool granted;

        // 이번 판의 결과. 경험치 배율이 승패에 따라 달라진다.
        private Battle.BattleOutcome lastOutcome;

        // 이번 판에 각 자리가 받은 경험치. 지급하면서 기록해두고 화면에 그대로 보여준다 -
        // 다시 계산하면 내림이 갈려 표시와 실제가 어긋날 수 있다.
        private readonly int[] expGained = new int[MaxSlots];

        // 경험치를 넣기 <b>전</b>의 레벨과 경험치. 바는 여기서 출발해 차오른다 -
        // 지급이 끝난 뒤의 값만 들고 있으면 "이미 차 있는 바"밖에 그릴 수가 없다.
        private readonly int[] beforeLevel = new int[MaxSlots];
        private readonly int[] beforeExp = new int[MaxSlots];

        private void Awake()
        {
            if (slotTemplate != null)
                slotTemplate.SetActive(false);

            if (root != null)
                root.SetActive(false);

            if (confirmButton != null)
                confirmButton.onClick.AddListener(Confirm);
        }

        private void OnDestroy()
        {
            if (confirmButton != null)
                confirmButton.onClick.RemoveListener(Confirm);
        }

        /// <summary>
        /// 화면을 띄운다. 흐름(<see cref="BattleResultFlow"/>)이 부른다.
        /// <b>승패를 받는 이유</b>: 진 판은 경험치가 4분의 1만 들어간다(StageExpReward).
        /// </summary>
        public void Show(Battle.BattleOutcome outcome)
        {
            lastOutcome = outcome;
            EnsureSlots();

            // <b>먼저 주고 그 다음에 그린다.</b> 순서가 반대면 경험치를 받기 전 상태가 보인다.
            GrantOnce();

            for (int i = 0; i < slots.Count; i++)
                ApplySlot(i);

            if (root != null)
                root.SetActive(true);

            // 판이 켜진 뒤에 시작한다 - 꺼진 채로 돌리면 첫 프레임이 통째로 날아간다.
            StartExpFill();
        }

        /// <summary>화면을 닫는다.</summary>
        public void Hide()
        {
            StopAllCoroutines();

            if (root != null)
                root.SetActive(false);
        }

        /// <summary>
        /// 확인 - 아파트로 돌아간다.
        ///
        /// 돌아가면서 <b>방금 하던 챕터를 기억시켜 둔다</b>(2026-08-25 사용자 지시) - 아파트에서
        /// "스테이지 입장"을 누르면 챕터 목록을 거치지 않고 곧바로 그 챕터로 간다. 방금 한 판을
        /// 이어서 하는 게 압도적으로 흔한 흐름이라서다.
        /// </summary>
        public void Confirm()
        {
            ScreenRequest.ResumeChapter = StageEntry.Chapter;
            AppScenes.GoToApartment();
        }

        /// <summary>
        /// 이번 판의 보상을 캐릭터에게 넣는다. <b>한 판에 한 번만</b> - 화면을 다시 띄운다고
        /// 또 주면 안 된다.
        ///
        /// 둘을 준다:
        ///  - <b>스테이지 경험치</b> - 자리마다 배율이 다르다(<see cref="StageExpReward"/>:
        ///    리더 1.25배 / 파트너 1배 / 나머지 0.75배). 레벨을 올리는 건
        ///    <see cref="CharacterLeveling"/> 한 곳이라 여기서 직접 만지지 않는다.
        ///  - <b>소지금</b> - 쓴 조각 수만큼. <b>캐릭터마다 각자의 주머니</b>이고 플레이어 골드와
        ///    별개다(2026-08-25 사용자 확인). 하나로 합치지 말 것.
        /// </summary>
        private void GrantOnce()
        {
            if (granted || boardView == null)
                return;

            granted = true;

            int stageExp = StageEntry.Stage != null ? StageEntry.Stage.clearExp : 0;
            bool victory = lastOutcome.result == Battle.BattleResult.Victory;

            int topMatcherSlot = FindTopMatcherSlot(slotCount);

            for (int i = 0; i < slotCount && i < MaxSlots; i++)
            {
                var character = boardView.GetCharacter(i);
                if (character == null)
                    continue;

                // <b>넣기 전에</b> 적어둔다 - 넣고 나면 어디서 출발했는지 알 길이 없다.
                beforeLevel[i] = character.level;
                beforeExp[i] = character.currentExp;

                int exp = StageExpReward.ExpFor(stageExp, i, victory);

                // 스티커: "승리시, 제일 퍼즐 제거 수가 많은 캐릭터 추가 경험치+N%".
                // <b>이겼을 때만</b>이고, 한 자리에만 붙는다.
                if (victory && i == topMatcherSlot)
                    exp = Mathf.RoundToInt(exp * (1f + StickerEffects.TopMatcherExpBonus()));

                if (exp > 0 && CharacterLeveling.TryApplyExp(character, exp))
                    expGained[i] = exp;

                if (inputController != null)
                {
                    CharacterWallet.Add(character,
                        CharacterWallet.MoneyFor(inputController.GetPiecesMatched(i)));
                }
            }
        }

        /// <summary>
        /// 이번 판에 조각을 <b>제일 많이 지운</b> 자리. 아무도 안 지웠으면 -1.
        /// 같으면 앞 자리(리더)가 가져간다 - 어차피 한 장짜리 보너스라 어디로든 가야 한다.
        /// </summary>
        private int FindTopMatcherSlot(int slotCount)
        {
            if (inputController == null)
                return -1;

            int best = -1;
            int bestCount = 0;

            for (int i = 0; i < slotCount && i < MaxSlots; i++)
            {
                int count = inputController.GetPiecesMatched(i);
                if (count > bestCount)
                {
                    bestCount = count;
                    best = i;
                }
            }

            return best;
        }

        private void ApplySlot(int index)
        {
            var slot = slots[index];
            var character = boardView != null ? boardView.GetCharacter(index) : null;

            bool owned = character != null;

            for (int i = 0; i < slot.filledOnly.Length; i++)
            {
                if (slot.filledOnly[i] != null)
                    slot.filledOnly[i].SetActive(owned);
            }

            if (slot.empty != null)
            {
                slot.empty.SetActive(!owned);

                if (!owned)
                {
                    var text = slot.empty.GetComponent<Text>();
                    if (text != null)
                        text.text = emptyLabel;
                }
            }

            if (!owned)
                return;

            if (slot.icon != null)
            {
                slot.icon.sprite = character.icon;
                slot.icon.enabled = character.icon != null;
            }

            if (slot.name != null)
                slot.name.text = DisplayNameOf(character);


            // <b>받기 전 상태</b>로 그려둔다 - 바는 여기서 출발해 차오른다(FillOne).
            int level = index < MaxSlots ? beforeLevel[index] : character.level;
            int exp = index < MaxSlots ? beforeExp[index] : character.currentExp;
            DrawExp(slot, level, exp);

            if (slot.expGain != null)
            {
                int gained = index < MaxSlots ? expGained[index] : 0;
                slot.expGain.text = gained > 0 ? expGainPrefix + gained : string.Empty;
            }

            if (slot.pieces != null)
            {
                int pieces = inputController != null ? inputController.GetPiecesMatched(index) : 0;
                slot.pieces.text = pieces + piecesSuffix;
            }

            // 그 캐릭터가 <b>자기 몫으로</b> 들고 있는 돈. 플레이어 골드가 아니다.
            if (slot.money != null)
                slot.money.text = moneyPrefix + CharacterWallet.Get(character).ToString("N0");
        }

        /// <summary>
        /// 그 자리의 레벨과 경험치를 <b>있는 그대로</b> 그린다. 지금 캐릭터 상태가 아니라
        /// 넘겨받은 값을 쓰는 게 요점이다 - 차오르는 도중의 값도 그려야 하기 때문이다.
        /// </summary>
        private void DrawExp(CharacterSlot slot, int level, int exp)
        {
            bool maxed = level >= CharacterGrowthTable.MaxLevel;
            int need = maxed ? 0 : CharacterGrowthTable.GetRequiredExp(level + 1);
            float progress = need > 0 ? Mathf.Clamp01(exp / (float)need) : (maxed ? 1f : 0f);

            if (slot.level != null)
                slot.level.text = $"Lv.{level} / {CharacterGrowthTable.MaxLevel}";

            if (slot.expFill != null)
                slot.expFill.fillAmount = progress;

            if (slot.exp != null)
            {
                // 만렙이면 퍼센트가 늘 100%로 굳어 "곧 오를 것처럼" 보인다 - 그때는 그렇게 적는다.
                slot.exp.text = maxed ? "최대 레벨" : $"{Mathf.RoundToInt(progress * 100f)}%";
            }
        }

        /// <summary>
        /// 경험치 바를 <b>이번 판에 받은 만큼 차오르게</b> 한다(2026-08-30 사용자 지시 -
        /// 처음부터 차 있으면 무엇을 얼마나 받았는지 안 보인다).
        ///
        /// <b>여섯 칸을 한꺼번에 돌린다</b> - 차례로 하면 다 보는 데 여섯 배가 걸린다.
        /// </summary>
        private void StartExpFill()
        {
            // 이 컴포넌트가 돌리는 코루틴은 이것뿐이라 통째로 멈춰도 안전하다.
            StopAllCoroutines();

            for (int i = 0; i < slots.Count; i++)
            {
                // 지난 판이 알림을 띄운 채로 멈췄을 수 있다 - 켜고 끄는 것은 여기서 원위치.
                if (slots[i].levelUp != null)
                    slots[i].levelUp.gameObject.SetActive(false);

                if (slots[i].exp != null)
                    slots[i].exp.enabled = true;
            }

            for (int i = 0; i < slots.Count; i++)
                StartCoroutine(FillOne(i));
        }

        /// <summary>
        /// 한 자리의 바를 채운다. 100%를 넘기면 <b>레벨 업을 알리고 바를 0부터 다시</b> 채운다 -
        /// 한 판에 여러 레벨이 오를 수도 있어서 반복문이다.
        ///
        /// 여기 계산은 <see cref="CharacterLeveling.TryApplyExp"/> 와 <b>같은 규칙</b>이어야
        /// 하지만, 어긋나더라도 <b>마지막에 진짜 상태로 맞추므로</b> 화면이 데이터와 달라지지는 않는다.
        /// </summary>
        private IEnumerator FillOne(int index)
        {
            if (index >= slots.Count || index >= MaxSlots)
                yield break;

            var slot = slots[index];
            var character = boardView != null ? boardView.GetCharacter(index) : null;

            if (character == null)
                yield break;

            int gained = expGained[index];
            if (gained > 0)
            {
                int level = beforeLevel[index];
                int exp = beforeExp[index];
                int remaining = gained;

                while (remaining > 0 && level < CharacterGrowthTable.MaxLevel)
                {
                    int need = CharacterGrowthTable.GetRequiredExp(level + 1);
                    if (need <= 0)
                        break;

                    int step = Mathf.Min(need - exp, remaining);
                    if (step <= 0)
                        break;

                    // 걸리는 시간을 <b>받은 양에 비례</b>해 나눈다 - 레벨이 세 번 올라도
                    // 전체는 expFillDuration 이라 화면이 늘어지지 않는다.
                    yield return SlideBar(slot, level, exp, exp + step, need,
                                          expFillDuration * (step / (float)gained));

                    exp += step;
                    remaining -= step;

                    if (exp < need)
                        break;

                    exp -= need;
                    level++;

                    yield return AnnounceLevelUp(slot, level, exp);
                }
            }

            // <b>마지막은 진짜 상태로.</b> 표시와 데이터가 어긋나면 안 된다
            // (만렙에 닿으면 CharacterLeveling 이 남은 경험치를 0으로 만든다).
            DrawExp(slot, character.level, character.currentExp);
        }

        private IEnumerator SlideBar(CharacterSlot slot, int level, int from, int to, int need,
            float duration)
        {
            if (duration <= 0f)
            {
                DrawExp(slot, level, to);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;   // 결과 화면은 시간이 멈춰 있을 수 있다
                float k = Mathf.Clamp01(elapsed / duration);

                DrawExp(slot, level, Mathf.RoundToInt(Mathf.Lerp(from, to, k)));
                yield return null;
            }

            DrawExp(slot, level, to);
        }

        /// <summary>
        /// 레벨이 올랐다고 알리고 <b>바를 0부터 다시</b> 시작한다.
        /// 글자를 안 물려뒀으면 알림 없이 넘어간다 - 차오르는 것만으로도 게임은 돌아간다.
        /// </summary>
        private IEnumerator AnnounceLevelUp(CharacterSlot slot, int level, int carriedExp)
        {
            DrawExp(slot, level, carriedExp);

            if (slot.levelUp == null)
                yield break;

            slot.levelUp.text = levelUpLabel;
            slot.levelUp.gameObject.SetActive(true);

            // 글자가 경험치 바 위에 겹치므로 퍼센트는 잠깐 비켜준다 - 둘이 겹쳐 있으면 둘 다 안 읽힌다.
            if (slot.exp != null)
                slot.exp.enabled = false;

            float elapsed = 0f;
            while (elapsed < levelUpHoldSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            slot.levelUp.gameObject.SetActive(false);

            if (slot.exp != null)
                slot.exp.enabled = true;
        }

        /// <summary>
        /// 이름. 애셋의 <c>displayName</c> 이 비어 있으면 애셋 이름으로 물러선다 -
        /// 지금 캐릭터 애셋들은 그 칸이 전부 비어 있다(FormationPanel 과 같은 처리).
        /// </summary>
        private static string DisplayNameOf(PanelType character)
            => character.DisplayName;

        /// <summary>슬롯이 모자라면 본을 복제해 채운다. 한 번 만든 슬롯은 버리지 않고 다시 쓴다.</summary>
        private void EnsureSlots()
        {
            if (slotTemplate == null || slotContent == null)
                return;

            while (slots.Count < slotCount)
            {
                var go = Instantiate(slotTemplate, slotContent);
                go.name = "CharacterSlot" + slots.Count;
                go.SetActive(true);

                var rect = (RectTransform)go.transform;

                // 위에서 아래로 쌓는다. 부모 pivot 이 위쪽이라 y 가 음수로 내려간다.
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, slotHeight);
                rect.anchoredPosition = new Vector2(0f, -slots.Count * (slotHeight + slotSpacing));

                var icon = FindComponent<Image>(go.transform, "Icon");
                var nameText = FindComponent<Text>(go.transform, "NameText");
                var levelText = FindComponent<Text>(go.transform, "LevelText");
                var expFill = FindComponent<Image>(go.transform, "ExpBarFill");
                var expText = FindComponent<Text>(go.transform, "ExpText");
                var expGainText = FindComponent<Text>(go.transform, "ExpGainText");
                var levelUpText = FindComponent<Text>(go.transform, "LevelUpText");
                var piecesText = FindComponent<Text>(go.transform, "PiecesText");
                var moneyText = FindComponent<Text>(go.transform, "MoneyText");
                var emptyGo = FindChild(go.transform, "EmptyText");

                // 캐릭터가 있을 때만 보이는 것들을 미리 모아둔다 - 슬롯을 그릴 때마다
                // 다시 찾을 이유가 없다. 경험치 바는 채움의 <b>부모</b>를 감춰야 판까지 사라진다.
                slots.Add(new CharacterSlot
                {
                    go = go,
                    icon = icon,
                    name = nameText,
                    level = levelText,
                    expFill = expFill,
                    exp = expText,
                    expGain = expGainText,
                    levelUp = levelUpText,
                    pieces = piecesText,
                    money = moneyText,
                    empty = emptyGo,
                    filledOnly = new[]
                    {
                        icon != null ? icon.gameObject : null,
                        nameText != null ? nameText.gameObject : null,
                        levelText != null ? levelText.gameObject : null,
                        expFill != null && expFill.transform.parent != null
                            ? expFill.transform.parent.gameObject
                            : null,
                        expGainText != null ? expGainText.gameObject : null,
                        piecesText != null ? piecesText.gameObject : null,
                        moneyText != null ? moneyText.gameObject : null
                    }
                });
            }
        }

        private static GameObject FindChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            return child != null ? child.gameObject : null;
        }

        private static T FindComponent<T>(Transform parent, string childName) where T : Component
        {
            // 경험치 채움처럼 한 단계 더 들어가 있는 것도 있어서 자식 전체에서 이름으로 찾는다.
            var children = parent.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                    return children[i].GetComponent<T>();
            }

            return null;
        }
    }
}

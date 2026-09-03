using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.Core;
using JojoPuzzle.View;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 시작 연출 3단계 - <b>이번 판에 참가하는 무작위 조각들</b>을 조각 모양 아이콘과 전투력으로
    /// 보여준다(2026-08-28 사용자 기획).
    ///
    /// <b>왜 보여주는가</b>: 팔레트 6칸 중 4칸은 매 판 무작위로 뽑힌다. 그게 얼마나 센지 모르면
    /// 어느 색을 적극적으로 맞춰야 하는지 판단할 수가 없다 - 판이 시작되고 나서 조각을 하나씩
    /// 눌러 확인할 수는 없으니, 시작 전에 한 번 늘어놓는다.
    ///
    /// <b>편성한 둘(0=리더 1=파트너)은 빼고 보여준다</b> - 그건 플레이어가 직접 고른 것이고
    /// 준비 화면에서 이미 전투력까지 봤다. 여기서 알아야 하는 건 <b>모르고 들어온 넷</b>이다.
    ///
    /// 조각 그림은 <see cref="PuzzlePieceIcon"/> 를 쓴다 - 편성 화면·보유 목록과 같은 부품이라
    /// 프레임 색이 보드와 어긋나지 않는다.
    /// </summary>
    public class BattlePieceIntroPanel : MonoBehaviour
    {
        [Tooltip("껐다 켜는 뿌리. 이 컴포넌트는 <b>항상 켜져 있는</b> 바깥에 붙는다.")]
        [SerializeField] private GameObject root;

        [Tooltip("칸이 쌓이는 자리.")]
        [SerializeField] private RectTransform slotContent;

        [Tooltip("칸 하나의 본. 자식 이름: Piece(PuzzlePieceIcon) / PowerText / NameText")]
        [SerializeField] private GameObject slotTemplate;

        [Tooltip("칸 하나의 폭과 칸 사이 간격(유닛).")]
        [SerializeField] private float slotWidth = 58f;
        [SerializeField] private float slotSpacing = 6f;

        [Tooltip("칸이 하나씩 <b>차례로</b> 튀어나오는 간격(초). 한꺼번에 뜨면 넷을 다 볼 새가 없다.")]
        [SerializeField] private float slotInterval = 0.12f;

        [Tooltip("칸 하나가 튀어나오는 데 걸리는 시간(초).")]
        [SerializeField] private float popDuration = 0.18f;

        [Tooltip("튀어나올 때 처음 크기 배율. 1보다 크면 컸다가 제자리로 줄어든다.")]
        [SerializeField] private float popStartScale = 1.6f;

        [Tooltip("넷이 다 뜬 뒤 읽을 시간(초).")]
        [SerializeField] private float holdDuration = 0.9f;

        [Tooltip("사라지는 데 걸리는 시간(초).")]
        [SerializeField] private float fadeOutDuration = 0.2f;

        [Tooltip("이 뿌리의 CanvasGroup. 통째로 흐려질 때 쓴다.")]
        [SerializeField] private CanvasGroup group;

        private class Slot
        {
            public GameObject root;
            public RectTransform rect;
            public PuzzlePieceIcon icon;
            public Text power;
            public Text name;
        }

        private readonly List<Slot> slots = new List<Slot>();

        private void Awake()
        {
            if (slotTemplate != null)
                slotTemplate.SetActive(false);

            if (root != null)
                root.SetActive(false);
        }

        /// <summary>
        /// 팔레트에서 <paramref name="firstIndex"/> 번째부터 끝까지를 보여준다(보통 2번부터 =
        /// 편성 둘을 뺀 나머지). 다 보여주고 사라질 때까지 기다린다.
        /// </summary>
        public IEnumerator Play(BoardView boardView, int firstIndex = 2)
        {
            if (root == null || boardView == null)
                yield break;

            // 실제로 보여줄 것만 모은다. 보유 캐릭터가 모자라 팔레트를 못 채웠으면 그 칸은 없다
            // (BattleCharacterPanel 과 같은 판정 - null 인 것뿐이라 따로 세지 않는다).
            var characters = new List<PanelType>();
            for (int i = firstIndex; i < firstIndex + 8; i++)
            {
                var character = boardView.GetCharacter(i);
                if (character != null)
                    characters.Add(character);
            }

            if (characters.Count == 0)
                yield break;

            Build(characters);

            root.SetActive(true);
            if (group != null)
                group.alpha = 1f;

            // 하나씩 튀어나온다.
            for (int i = 0; i < characters.Count; i++)
            {
                slots[i].root.SetActive(true);
                StartCoroutine(Pop(slots[i].rect));

                if (slotInterval > 0f)
                    yield return new WaitForSeconds(slotInterval);
            }

            if (holdDuration > 0f)
                yield return new WaitForSeconds(holdDuration);

            yield return FadeOut();

            root.SetActive(false);
        }

        private IEnumerator Pop(RectTransform rect)
        {
            if (popDuration <= 0f)
            {
                rect.localScale = Vector3.one;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < popDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / popDuration);

                // 끝에서 잦아든다 - 등속이면 "튀어나왔다"가 아니라 "줄어들었다"로 보인다.
                float eased = 1f - (1f - t) * (1f - t);
                rect.localScale = Vector3.one * Mathf.Lerp(popStartScale, 1f, eased);
                yield return null;
            }

            rect.localScale = Vector3.one;
        }

        private IEnumerator FadeOut()
        {
            if (group == null || fadeOutDuration <= 0f)
                yield break;

            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                group.alpha = 1f - Mathf.Clamp01(elapsed / fadeOutDuration);
                yield return null;
            }

            group.alpha = 0f;
        }

        /// <summary>
        /// 칸을 채운다. <b>본을 복제해 쌓고 버리지 않고 다시 쓴다</b>(이 프로젝트의 목록 규칙).
        /// 가로 가운데 정렬이라 셋만 나와도 치우치지 않는다.
        /// </summary>
        private void Build(List<PanelType> characters)
        {
            EnsureSlots(characters.Count);

            float total = characters.Count * slotWidth + (characters.Count - 1) * slotSpacing;
            float x = -total * 0.5f + slotWidth * 0.5f;

            for (int i = 0; i < slots.Count; i++)
            {
                bool used = i < characters.Count;

                slots[i].root.SetActive(false); // Play 가 하나씩 켠다
                if (!used)
                    continue;

                slots[i].rect.anchoredPosition = new Vector2(x, 0f);
                slots[i].rect.localScale = Vector3.one;
                x += slotWidth + slotSpacing;

                var character = characters[i];

                slots[i].icon?.Show(character);

                if (slots[i].power != null)
                    slots[i].power.text = character.CombatPower.ToString("N0");

                if (slots[i].name != null)
                    slots[i].name.text = character.DisplayName;
            }
        }

        private void EnsureSlots(int count)
        {
            if (slotTemplate == null || slotContent == null)
                return;

            while (slots.Count < count)
            {
                var go = Instantiate(slotTemplate, slotContent);
                go.name = $"PieceSlot{slots.Count}";

                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(slotWidth, rect.sizeDelta.y);

                slots.Add(new Slot
                {
                    root = go,
                    rect = rect,
                    icon = go.GetComponentInChildren<PuzzlePieceIcon>(true),
                    power = FindText(go, "PowerText"),
                    name = FindText(go, "NameText")
                });
            }
        }

}
}

using System.Collections.Generic;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JojoPuzzle.Formation
{
/// <summary>
    /// <b>스티커 붙이기</b>(2026-09-03 사용자 기획). 스티커북의 여백을 누르면 열린다.
    ///
    /// ⭐ <b>그림만 늘어놓는다.</b> 설명을 줄줄이 적으면 스티커를 고르는 게 아니라 글을 읽게 된다 -
    /// 궁금하면 꾹 눌러서 말풍선으로 본다.
    ///
    /// ⭐ <b>고르면 목록이 아래로 스르륵 내려간다</b>(사용자 기획). 그다음 책에서 붙일 자리를
    /// 누른다 - 목록이 덮고 있으면 어디에 붙일지 볼 수가 없다.
    /// </summary>
    public class StickerAttachPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;

        [Tooltip("아래로 내려갈 판. 여기만 움직이고 뿌리는 그대로 둔다.")]
        [SerializeField] private RectTransform slidePanel;

        [Tooltip("내려가는 데 걸리는 시간(초).")]
        [Min(0.01f)]
        [SerializeField] private float slideSeconds = 0.25f;

        [SerializeField] private Text costText;
        [SerializeField] private Image costFill;
        [SerializeField] private Text noticeText;

        [Header("말풍선")]
        [Tooltip("꾹 눌렀을 때 뜨는 설명. 스티커 위에 붙어 따라다닌다.")]
        [SerializeField] private RectTransform bubble;

        [SerializeField] private Text bubbleText;

        [Tooltip("말풍선과 누른 칸 사이의 틈(px).")]
        [SerializeField] private float bubbleGap = 8f;

        [Header("목록")]
        [Tooltip("스티커 칸의 본. 꺼진 채로 두면 복제해 쌓는다.")]
        [SerializeField] private RectTransform cellTemplate;

        [SerializeField] private RectTransform listContent;

        [Min(1)]
        [SerializeField] private int columns = 4;

        [Tooltip("칸 사이. <b>칸 크기는 자동으로 잰다</b> - 목록 폭에 맞춰야 오른쪽이 안 잘린다.")]
        [SerializeField] private float cellGap = 8f;

        // 지금 화면에서 잰 칸 크기. ⚠ 숫자로 박으면 좁은 폰에서 <b>오른쪽 줄이 잘린다</b>
        // (2026-09-03 사용자 지적).
        private float cellSize = 52f;

        [SerializeField] private Button closeButton;
        [SerializeField] private StickerCatalog catalog;

        /// <summary>닫혔다.</summary>
        public event System.Action OnClosed;

        /// <summary>스티커를 골랐다 - 이제 책에서 붙일 자리를 고를 차례다.</summary>
        public event System.Action<int> OnStickerPicked;

        public bool IsOpen => root != null && root.activeSelf;

        private readonly List<RectTransform> cells = new List<RectTransform>();
        private Vector2 shownPosition;
        private Coroutine sliding;

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (cellTemplate != null)
                cellTemplate.gameObject.SetActive(false);

            if (slidePanel != null)
                shownPosition = slidePanel.anchoredPosition;

            bubble?.gameObject.SetActive(false);
            root?.SetActive(false);
        }

        private void OnEnable() => PlayerStickers.OnChanged += Refresh;

        private void OnDisable() => PlayerStickers.OnChanged -= Refresh;

        public void Open()
        {
            if (root == null)
                return;

            root.SetActive(true);
            bubble?.gameObject.SetActive(false);
            Refresh();

            // 아래에서 <b>미끄러져 올라온다</b>(2026-09-03 사용자 요청).
            StopSliding();
            sliding = StartCoroutine(SlideInRoutine());
        }

        /// <summary>목록을 접는다. <b>미끄러져 내려간 뒤에</b> 꺼진다.</summary>
        public void Close()
        {
            if (root == null || !root.activeSelf)
                return;

            StopSliding();
            sliding = StartCoroutine(SlideOutRoutine());
        }

        /// <summary>
        /// 아래에서 미끄러져 올라온다. <b>끝에서 느려진다</b>(슬로우 인) - 툭 나타나는 것보다
        /// 어디서 온 화면인지가 읽힌다.
        /// </summary>
        private System.Collections.IEnumerator SlideInRoutine()
        {
            if (slidePanel == null)
            {
                sliding = null;
                yield break;
            }

            Vector2 from = shownPosition + new Vector2(0f, -Screen.height);
            slidePanel.anchoredPosition = from;

            for (float t = 0f; t < slideSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / slideSeconds);
                k = 1f - Mathf.Pow(1f - k, 3f);   // 빠르게 올라와 부드럽게 멈춘다
                slidePanel.anchoredPosition = Vector2.Lerp(from, shownPosition, k);
                yield return null;
            }

            slidePanel.anchoredPosition = shownPosition;
            sliding = null;
        }

        /// <summary>
        /// 아래로 미끄러져 내려간 뒤 꺼진다(슬로우 아웃). 다 내려가기 전에 꺼 버리면
        /// 화면이 <b>사라지는 게 아니라 툭 없어진다</b>.
        /// </summary>
        private System.Collections.IEnumerator SlideOutRoutine()
        {
            if (slidePanel != null)
            {
                Vector2 from = slidePanel.anchoredPosition;
                Vector2 to = shownPosition + new Vector2(0f, -Screen.height);

                for (float t = 0f; t < slideSeconds; t += Time.unscaledDeltaTime)
                {
                    float k = Mathf.Clamp01(t / slideSeconds);
                    k = k * k;   // 아래로 떨어지듯 가속한다
                    slidePanel.anchoredPosition = Vector2.Lerp(from, to, k);
                    yield return null;
                }
            }

            sliding = null;
            root?.SetActive(false);

            if (slidePanel != null)
                slidePanel.anchoredPosition = shownPosition;

            OnClosed?.Invoke();
        }

        // ---------------------------------------------------------------- 고르기

        /// <summary>
        /// 골랐다 - <b>목록을 아래로 흘려보내고</b> 책에 자리를 고르라고 알린다.
        /// 코스트가 모자라면 고르는 것부터 막는다.
        /// </summary>
        private void Pick(int id)
        {
            HideBubble();

            var sticker = catalog != null ? catalog.Find(id) : null;
            if (sticker == null)
                return;

            // ⭐ 중복 착용이 된다(2026-09-03) - 여유분이 남아 있으면 <b>한 장 더</b> 붙이고,
            // 가진 걸 다 붙였으면 그때 한 장 뗀다. 목록을 계속 누르면 붙였다 떼었다가 된다.
            if (!PlayerStickers.CanAttachMore(id))
            {
                PlayerStickers.DetachOne(id);
                return;
            }

            int used = PlayerStickers.UsedCost(catalog);
            if (used + sticker.cost > PlayerStickers.MaxCost(PlayerProfile.Level))
            {
                Notice("코스트가 모자랍니다");
                return;
            }

            StopSliding();
            sliding = StartCoroutine(SlideAwayRoutine(id));
        }

        private System.Collections.IEnumerator SlideAwayRoutine(int id)
        {
            if (slidePanel != null)
            {
                Vector2 from = shownPosition;
                Vector2 to = shownPosition + new Vector2(0f, -Screen.height);

                for (float t = 0f; t < slideSeconds; t += Time.unscaledDeltaTime)
                {
                    float k = Mathf.Clamp01(t / slideSeconds);
                    k = k * k;   // 아래로 떨어지듯 가속한다
                    slidePanel.anchoredPosition = Vector2.Lerp(from, to, k);
                    yield return null;
                }

                slidePanel.anchoredPosition = to;
            }

            sliding = null;

            // 판을 통째로 접는다 - 배경까지 남으면 책을 누를 수가 없다.
            root?.SetActive(false);

            if (slidePanel != null)
                slidePanel.anchoredPosition = shownPosition;

            OnStickerPicked?.Invoke(id);
        }

        private void StopSliding()
        {
            if (sliding == null)
                return;

            StopCoroutine(sliding);
            sliding = null;

            if (slidePanel != null)
                slidePanel.anchoredPosition = shownPosition;
        }

        // ---------------------------------------------------------------- 말풍선

        private void ShowBubble(int id)
        {
            var sticker = catalog != null ? catalog.Find(id) : null;
            if (sticker == null || bubble == null)
                return;

            if (bubbleText != null)
                bubbleText.text = sticker.description;

            bubble.gameObject.SetActive(true);
            PlaceBubbleNear(id);
        }

        /// <summary>
        /// 말풍선을 <b>가로는 가운데</b>에 두고, 세로만 누른 칸에 맞춰 위나 아래에 띄운다
        /// (2026-09-03 사용자 지시).
        ///
        /// ⭐ 칸 옆에 딱 붙여 띄우면 가장자리 칸에서 <b>화면 밖으로 나간다</b>. 가로를 가운데로
        /// 고정하면 어느 칸을 눌러도 글이 안 잘린다 - 어차피 무엇을 눌렀는지는 손가락이 안다.
        ///
        /// 위아래는 <b>누른 칸이 어디냐</b>로 정한다: 위쪽 칸이면 아래에, 아래쪽 칸이면 위에 -
        /// 그래야 말풍선이 자기가 설명하는 칸을 가리지 않는다.
        /// </summary>
        private void PlaceBubbleNear(int id)
        {
            if (slidePanel == null)
                return;

            RectTransform found = null;
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i].GetComponent<StickerCell>();
                if (cell != null && cell.stickerId == id)
                {
                    found = cells[i];
                    break;
                }
            }

            bubble.anchorMin = bubble.anchorMax = new Vector2(0.5f, 0.5f);

            if (found == null)
            {
                bubble.anchoredPosition = Vector2.zero;
                return;
            }

            // 칸의 한가운데를 말풍선이 사는 판(SlidePanel) 좌표로 옮긴다.
            // 둘 다 기준점이 가운데라 그대로 anchoredPosition 이 된다.
            Vector3 world = found.TransformPoint(found.rect.center);
            Vector2 local = slidePanel.InverseTransformPoint(world);

            float gap = (found.rect.height + bubble.rect.height) * 0.5f + bubbleGap;
            float y = local.y > 0f ? local.y - gap : local.y + gap;

            // 판 밖으로는 안 나가게 붙든다.
            float limit = (slidePanel.rect.height - bubble.rect.height) * 0.5f;
            bubble.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, -limit, limit));
        }

        private void HideBubble() => bubble?.gameObject.SetActive(false);

        // ---------------------------------------------------------------- 그리기

        public void Refresh()
        {
            int max = PlayerStickers.MaxCost(PlayerProfile.Level);
            int used = PlayerStickers.UsedCost(catalog);

            if (costText != null)
                costText.text = $"코스트 {used} / {max}";

            if (costFill != null)
                costFill.fillAmount = max > 0 ? Mathf.Clamp01(used / (float)max) : 0f;

            BuildCells();
        }

        private void BuildCells()
        {
            if (cellTemplate == null || listContent == null || catalog == null)
                return;

            // ⭐ 칸 크기를 <b>목록 폭에서 거꾸로 구한다</b>. 기기마다 폭이 달라 숫자로 박으면 잘린다.
            float width = listContent.rect.width;
            if (width > 1f)
                cellSize = Mathf.Max(24f, (width - cellGap * (columns - 1)) / columns);

            while (cells.Count < catalog.Count)
            {
                var cell = Instantiate(cellTemplate, listContent);
                cell.name = "Cell" + cells.Count;
                cells.Add(cell);
            }

            int shown = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                var sticker = catalog.At(i);
                bool visible = sticker != null && PlayerStickers.Owns(sticker.id);

                cells[i].gameObject.SetActive(visible);
                if (!visible)
                    continue;

                FillCell(cells[i], sticker, shown);
                shown++;
            }

            int rows = Mathf.CeilToInt(shown / (float)Mathf.Max(1, columns));
            listContent.sizeDelta = new Vector2(listContent.sizeDelta.x,
                Mathf.Max(0f, rows * (cellSize + cellGap) - cellGap));

            if (noticeText != null && shown == 0)
            {
                noticeText.text = "가진 스티커가 없습니다";
                noticeText.gameObject.SetActive(true);
            }
        }

        private void FillCell(RectTransform cell, StickerDefinition sticker, int index)
        {
            int row = index / Mathf.Max(1, columns);
            int col = index % Mathf.Max(1, columns);

            cell.anchoredPosition = new Vector2(col * (cellSize + cellGap),
                                                -row * (cellSize + cellGap));

            cell.sizeDelta = new Vector2(cellSize, cellSize);

            var image = cell.GetComponent<Image>();
            if (image != null && sticker.sprite != null)
                image.sprite = sticker.sprite;

            // 가진 걸 <b>다 붙였을 때만</b> 흐리게 - 여유분이 남아 있으면 아직 더 붙일 수 있다.
            if (image != null)
                image.color = PlayerStickers.CanAttachMore(sticker.id)
                    ? Color.white : new Color(1f, 1f, 1f, 0.4f);

            var cost = cell.Find("CostText")?.GetComponent<Text>();
            if (cost != null)
                cost.text = sticker.cost.ToString();

            ShowOwnedCount(cell, PlayerStickers.OwnedCount(sticker.id));

            var hook = cell.GetComponent<StickerCell>();
            if (hook == null)
                hook = cell.gameObject.AddComponent<StickerCell>();

            hook.stickerId = sticker.id;
            hook.onPicked = Pick;
            hook.onHeld = ShowBubble;
            hook.onReleased = HideBubble;
        }

        /// <summary>
        /// 중복으로 가진 수를 칸 <b>오른쪽 아래</b>에 x2 · x3 으로 보여 준다(2026-09-03 사용자 지시).
        /// 한 장뿐이면 안 보여 준다 - 모든 칸에 x1 이 붙으면 눈만 시끄럽다.
        ///
        /// 글자는 <b>없으면 그때 만든다</b> - 칸 틀(씬)을 고치지 않고도 붙일 수 있고,
        /// 칸은 돌려쓰이므로 한 번만 만들어진다.
        /// </summary>
        private void ShowOwnedCount(RectTransform cell, int owned)
        {
            var label = cell.Find("CountText")?.GetComponent<Text>();

            if (label == null)
            {
                var go = new GameObject("CountText", typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(cell, false);

                // 오른쪽 아래 구석. 칸 크기가 기기마다 달라서 앵커로 붙인다.
                rect.anchorMin = new Vector2(0.42f, 0.02f);
                rect.anchorMax = new Vector2(0.98f, 0.42f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                label = go.AddComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.alignment = TextAnchor.LowerRight;
                label.color = Color.white;
                label.raycastTarget = false;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 8;
                label.resizeTextMaxSize = 18;
            }

            label.text = owned > 1 ? "x" + owned : string.Empty;
            label.enabled = owned > 1;
        }

        private void Notice(string text)
        {
            if (noticeText == null)
                return;

            noticeText.text = text;
            noticeText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }
    }
}

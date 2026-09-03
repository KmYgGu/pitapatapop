using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 우편함 화면. 아파트 HUD 의 우편 버튼이 연다.
    ///
    /// <code>
    ///   제목 "우편함"
    ///   편지 목록 - 제목 / 내용 / 받을 것 / [받기]
    ///   [모두 받기]  [닫기]
    /// </code>
    ///
    /// <b>규칙은 여기 없다</b> - 무엇이 들어 있고 받으면 무엇이 늘어나는지는
    /// <see cref="Mailbox"/> 와 <see cref="PlayerInventory"/> 가 안다. 이 화면은 그걸 그리고
    /// 누른 것을 전달하기만 한다(<see cref="UI.BattleRewardPanel"/> 과 같은 방침).
    ///
    /// <b>줄은 본을 복제해 쌓고 버리지 않고 다시 쓴다</b>(이 프로젝트의 목록 규칙). 편지가
    /// 줄어들면 남는 줄은 꺼두기만 한다 - 매번 Destroy 하면 여는 때마다 쓰레기가 쌓인다.
    /// </summary>
    public class MailboxPanel : MonoBehaviour
    {
        [Tooltip("껐다 켜는 뿌리. 이 컴포넌트는 <b>항상 켜져 있는</b> 바깥 오브젝트에 붙는다 - " +
                 "꺼진 오브젝트에 붙으면 아파트 HUD 가 열어달라고 부를 수가 없다.")]
        [SerializeField] private GameObject root;

        [Header("목록")]
        [Tooltip("편지 줄이 쌓이는 자리.")]
        [SerializeField] private RectTransform listContent;

        [Tooltip("편지 한 줄의 본. 꺼둔 채로 두면 복제해서 쓴다. " +
                 "자식 이름: TitleText / BodyText / RewardText / ClaimButton")]
        [SerializeField] private GameObject entryTemplate;

        [Tooltip("줄 하나의 높이(유닛).")]
        [SerializeField] private float entryHeight = 96f;

        [Tooltip("줄 사이 간격(유닛).")]
        [SerializeField] private float entrySpacing = 8f;

        [Tooltip("편지가 하나도 없을 때만 보이는 문구.")]
        [SerializeField] private GameObject emptyText;

        [Header("버튼")]
        [SerializeField] private Button claimAllButton;
        [SerializeField] private Button closeButton;

        [Header("아이템 이름")]
        [Tooltip("받을 것을 사람 말로 적으려면 아이템 이름이 필요하다. 없으면 종류 이름을 쓴다.")]
        [SerializeField] private BattleItemCatalog itemCatalog;

        /// <summary>닫혔을 때 알린다 - HUD 가 우편 배지를 다시 그린다.</summary>
        public event System.Action OnClosed;

        private readonly List<EntryRow> rows = new List<EntryRow>();
        private readonly StringBuilder rewardBuilder = new StringBuilder();

        private class EntryRow
        {
            public GameObject root;
            public Text title;
            public Text body;
            public Text reward;
            public Button claim;
            public MailEntry entry;
        }

        private void Awake()
        {
            if (entryTemplate != null)
                entryTemplate.SetActive(false);

            if (claimAllButton != null)
                claimAllButton.onClick.AddListener(ClaimAll);

            if (closeButton != null)
                closeButton.onClick.AddListener(Hide);

            if (root != null)
                root.SetActive(false);
        }

        public bool IsOpen => root != null && root.activeSelf;

        public void Show()
        {
            if (root != null)
                root.SetActive(true);

            Rebuild();
        }

        public void Hide()
        {
            if (root != null)
                root.SetActive(false);

            OnClosed?.Invoke();
        }

        private void ClaimAll()
        {
            Mailbox.ClaimAll();
            Rebuild();
        }

        private void ClaimOne(int index)
        {
            if (index < 0 || index >= rows.Count)
                return;

            Mailbox.Claim(rows[index].entry);
            Rebuild();
        }

        /// <summary>
        /// 목록을 다시 그린다. <b>받을 때마다 통째로 다시 그린다</b> - 한 통을 받으면 그 아래
        /// 줄들이 전부 위로 올라와야 해서, 지운 줄만 손대는 것보다 이쪽이 오히려 단순하다.
        /// 편지가 몇 통 되지 않는 화면이라 비용도 문제가 안 된다.
        /// </summary>
        private void Rebuild()
        {
            var entries = Mailbox.Entries;
            int count = entries.Count;

            EnsureRows(count);

            float y = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool used = i < count;

                row.root.SetActive(used);
                if (!used)
                {
                    row.entry = null;
                    continue;
                }

                var entry = entries[i];
                row.entry = entry;

                var rect = (RectTransform)row.root.transform;
                rect.anchoredPosition = new Vector2(0f, -y);
                y += entryHeight + entrySpacing;

                SetText(row.title, entry.title);
                SetText(row.body, entry.body);
                SetText(row.reward, DescribeRewards(entry));
            }

            // 목록 높이를 실제 줄 수에 맞춘다 - 스크롤이 달려 있으면 이 값으로 굴러간다.
            if (listContent != null)
            {
                float height = count > 0 ? y - entrySpacing : 0f;
                listContent.sizeDelta = new Vector2(listContent.sizeDelta.x, Mathf.Max(0f, height));
            }

            if (emptyText != null)
                emptyText.SetActive(count == 0);

            if (claimAllButton != null)
                claimAllButton.interactable = count > 0;
        }

        private void EnsureRows(int count)
        {
            if (entryTemplate == null || listContent == null)
                return;

            while (rows.Count < count)
            {
                var go = Instantiate(entryTemplate, listContent);
                go.name = $"MailEntry{rows.Count}";

                var rect = (RectTransform)go.transform;

                // 위에서부터 아래로 쌓는다. 가로는 부모를 채운다.
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
                rect.sizeDelta = new Vector2(0f, entryHeight);

                var claim = FindButton(go, "ClaimButton");

                var row = new EntryRow
                {
                    root = go,
                    title = FindText(go, "TitleText"),
                    body = FindText(go, "BodyText"),
                    reward = FindText(go, "RewardText"),
                    claim = claim
                };

                int index = rows.Count;
                if (claim != null)
                    claim.onClick.AddListener(() => ClaimOne(index));

                rows.Add(row);
            }
        }

        /// <summary>"데미지 증가 x2, 코인 증가 x2 ..." 로 적는다.</summary>
        private string DescribeRewards(MailEntry entry)
        {
            rewardBuilder.Length = 0;

            for (int i = 0; i < entry.rewards.Count; i++)
            {
                if (rewardBuilder.Length > 0)
                    rewardBuilder.Append(", ");

                var reward = entry.rewards[i];
                rewardBuilder.Append(DisplayName(reward.kind));
                rewardBuilder.Append(" x");
                rewardBuilder.Append(reward.count);
            }

            return rewardBuilder.ToString();
        }

        private string DisplayName(BattleItemKind kind)
        {
            var items = itemCatalog != null ? itemCatalog.items : null;
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] != null && items[i].kind == kind
                        && !string.IsNullOrEmpty(items[i].displayName))
                        return items[i].displayName;
                }
            }

            return kind.ToString();
        }

private static Button FindButton(GameObject root, string childName)
        {
            var child = root.transform.Find(childName);
            return child != null ? child.GetComponent<Button>() : null;
        }
    }
}

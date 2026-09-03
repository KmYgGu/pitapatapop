using System;
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
    /// 방 화면의 <b>정보</b>(2026-08-28 사용자 기획).
    ///
    /// <code>
    ///   입주 기간        3일째
    ///   좋아하는 음식    A, B, C
    ///   싫어하는 음식    D, E, F
    ///   ── 다른 입주자와의 관계 ──
    ///   2층 라뷰린스      친함
    ///   3층 미스틱        서먹함
    /// </code>
    ///
    /// <b>여기 규칙은 없다</b> - 값은 전부 <see cref="ResidentState"/> 와
    /// <see cref="CharacterTasteTable"/> 이 갖고 있고 이 화면은 읽어서 적기만 한다.
    /// 그래서 나중에 "무엇으로 관계가 오르내리는지"가 정해져도 이 파일은 안 바뀐다.
    /// </summary>
    public class RoomInfoPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;

        [SerializeField] private Text titleText;
        [SerializeField] private Text residencyText;
        [SerializeField] private Text likesText;
        [SerializeField] private Text dislikesText;

        [Header("관계 목록")]
        [SerializeField] private RectTransform relationContent;

        [Tooltip("줄 하나의 본. 자식 이름: NameText / ValueText")]
        [SerializeField] private GameObject relationTemplate;

        [SerializeField] private float rowHeight = 26f;
        [SerializeField] private float rowSpacing = 4f;

        [Tooltip("같이 사는 사람이 없을 때만 보이는 문구.")]
        [SerializeField] private GameObject aloneText;

        [Header("데이터")]
        [SerializeField] private ApartmentRooms rooms;
        [SerializeField] private CharacterTasteTable tasteTable;

        [Tooltip("음식 목록. 먹어본 음식의 점수를 매기려면 필요하다.")]
        [SerializeField] private FoodCatalog foodCatalog;

        [Tooltip("아직 못 알아낸 자리에 적을 글자.")]
        [SerializeField] private string unknownFood = "???";

        [Tooltip("좋아하는·싫어하는 음식을 몇 줄까지 보여줄지.")]
        [Min(1)]
        [SerializeField] private int foodLines = 3;

        [Header("버튼")]
        [SerializeField] private Button closeButton;

        private class Row
        {
            public GameObject root;
            public RectTransform rect;
            public Text name;
            public Text value;
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly List<string> foodBuffer = new List<string>();
        private readonly StringBuilder builder = new StringBuilder();

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake()
        {
            if (relationTemplate != null)
                relationTemplate.SetActive(false);

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (root != null)
                root.SetActive(false);
        }

        public void Open(int roomIndex, PanelType resident)
        {
            if (root == null)
                return;

            root.SetActive(true);

            if (titleText != null)
                titleText.text = resident != null ? resident.DisplayName : "정보";

            if (residencyText != null)
                residencyText.text = ResidentState.DescribeResidency(resident, DateTime.UtcNow);

            SetFood(likesText, resident, likes: true);
            SetFood(dislikesText, resident, likes: false);

            BuildRelations(roomIndex, resident);
        }

        public void Close()
        {
            if (root != null)
                root.SetActive(false);
        }

        /// <summary>
        /// 좋아하는(싫어하는) 음식을 <b>한 줄에 하나씩</b> 적는다(2026-08-28 사용자 지시 -
        /// 예전엔 쉼표로 이어 붙였는데 좁은 폰에서 줄이 넘쳤다).
        ///
        /// <b>아직 못 알아낸 자리는 "???" 로 채운다</b>(사용자 기획): 좋아하는 음식은 미리
        /// 정해진 게 아니라 <b>먹어봐야</b> 아는 것이라, 아무것도 안 먹었으면 셋 다 물음표다.
        /// 빈 줄로 두면 "데이터가 없는 건지 아직 모르는 건지"를 구분할 수 없다.
        /// </summary>
        private void SetFood(Text target, PanelType resident, bool likes)
        {
            if (target == null)
                return;

            ResidentState.CollectTasted(resident, foodCatalog, tasteTable, likes,
                                        foodBuffer, foodLines);

            builder.Length = 0;
            for (int i = 0; i < foodLines; i++)
            {
                if (i > 0)
                    builder.AppendLine();

                builder.Append(i < foodBuffer.Count ? foodBuffer[i] : unknownFood);
            }

            target.text = builder.ToString();
        }

        /// <summary>
        /// 같이 사는 사람들과의 관계. <b>자기 자신은 뺀다</b>.
        /// 줄은 본을 복제해 쌓고 버리지 않고 다시 쓴다(이 프로젝트의 목록 규칙).
        /// </summary>
        private void BuildRelations(int roomIndex, PanelType resident)
        {
            int count = 0;

            if (rooms != null && resident != null)
            {
                float y = 0f;

                for (int room = 0; room < rooms.Count; room++)
                {
                    if (room == roomIndex)
                        continue;

                    var other = ApartmentResidents.Get(room);
                    if (other == null || other == resident)
                        continue;

                    EnsureRows(count + 1);

                    var row = rows[count];
                    row.root.SetActive(true);
                    row.rect.anchoredPosition = new Vector2(0f, -y);
                    y += rowHeight + rowSpacing;

                    if (row.name != null)
                        row.name.text = $"{rooms.GetName(room)} {other.DisplayName}";

                    if (row.value != null)
                        row.value.text = ResidentState.DescribeRelation(resident, other);

                    count++;
                }

                if (relationContent != null)
                {
                    float height = count > 0 ? y - rowSpacing : 0f;
                    relationContent.sizeDelta =
                        new Vector2(relationContent.sizeDelta.x, Mathf.Max(0f, height));
                }
            }

            for (int i = count; i < rows.Count; i++)
                rows[i].root.SetActive(false);

            if (aloneText != null)
                aloneText.SetActive(count == 0);
        }

        private void EnsureRows(int count)
        {
            if (relationTemplate == null || relationContent == null)
                return;

            while (rows.Count < count)
            {
                var go = Instantiate(relationTemplate, relationContent);
                go.name = $"RelationRow{rows.Count}";

                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
                rect.sizeDelta = new Vector2(0f, rowHeight);

                rows.Add(new Row
                {
                    root = go,
                    rect = rect,
                    name = FindText(go, "NameText"),
                    value = FindText(go, "ValueText")
                });
            }
        }

}
}

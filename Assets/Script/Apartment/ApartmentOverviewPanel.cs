using UnityEngine;
using JojoPuzzle.UI;
using UnityEngine.UI;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// <b>아파트 전체 보기</b>(2026-08-28 사용자 기획). 동이 늘어나면 한 화면에 다 안 들어오고,
    /// 위층은 한 손으로 누르기도 어렵다 - 그래서 전부를 한눈에 놓고 고르는 화면이 따로 있다
    /// ([[project-jojopuzzle-apartment]] 의 "맨 위층이 가장 안 닿는다" 항목).
    ///
    /// <code>
    ///   (카메라가 동 전부를 한 화면에 담는다. 메인 HUD 는 비켜난다)
    ///   [뒤로가기]            [동 추가]
    /// </code>
    ///
    /// <b>입주민은 여기서 끌어서 옮긴다</b>(<see cref="ApartmentResidentDragger"/>) -
    /// 2026-08-28 사용자가 '입주민 바꾸기' 모드 버튼을 없애고 그 조작으로 바꿨다.
    /// 버튼은 "지금 무슨 모드인지"를 따로 기억해야 하는데, 집어서 옮기는 건 보이는 그대로다.
    ///
    /// 이 화면은 <b>상태만 알린다</b> - 카메라를 움직이거나 방을 여는 건
    /// <see cref="ApartmentRoomFlow"/> 가 한다(다른 화면들과 같은 방침).
    /// </summary>
    public class ApartmentOverviewPanel : MonoBehaviour
    {
        [Tooltip("껐다 켜는 뿌리. 이 컴포넌트는 <b>항상 켜져 있는</b> 바깥에 붙는다.")]
        [SerializeField] private GameObject root;

        [SerializeField] private Button backButton;

        [Tooltip("동을 하나 늘린다. 원래는 레벨·골드가 드는 기능이라 <b>임시</b>다. " +
                 "여기 둔 이유는 스페이스바가 실제 기기에서는 쓸 수 없기 때문이다.")]
        [SerializeField] private Button addBuildingButton;

        /// <summary>뒤로가기를 눌렀다.</summary>
        [Tooltip("아래 띠(뒤로가기·동 추가). 카메라가 아파트를 담을 때 " +
                 "<b>실제로 덮는 만큼</b>을 재려고 참조한다 - 레터박스 배율이 기기마다 다르다.")]
        [SerializeField] private RectTransform bottomBar;

        /// <summary>아래 띠가 화면 아래쪽에서 덮는 비율. 안 물려뒀으면 음수.</summary>
        public float BottomCoverFraction => UiScreenMetrics.CoverFractionFromBottom(bottomBar);

        public event System.Action OnBackRequested;

        /// <summary>동 추가를 눌렀다.</summary>
        public event System.Action OnAddBuildingRequested;

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake()
        {
            if (backButton != null)
                backButton.onClick.AddListener(() => OnBackRequested?.Invoke());

            if (addBuildingButton != null)
                addBuildingButton.onClick.AddListener(() => OnAddBuildingRequested?.Invoke());

            if (root != null)
                root.SetActive(false);
        }

        public void Show()
        {
            if (root != null)
                root.SetActive(true);
        }

        public void Hide()
        {
            if (root != null)
                root.SetActive(false);
        }

    }
}

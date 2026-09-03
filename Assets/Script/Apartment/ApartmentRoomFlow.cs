using UnityEngine;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 방을 누른 뒤의 <b>순서를 아는 유일한 곳</b>
    /// (<c>BattleResultFlow</c>·<c>StageSelectFlow</c> 와 같은 방침).
    ///
    /// <code>
    ///   방 터치 → 카메라가 그 방을 확대 → 입주 화면
    ///           → 결정/닫기 → 카메라가 아파트 전체로 돌아간다
    /// </code>
    ///
    /// <b>부품들은 서로를 모른다</b> - 고르는 쪽(<see cref="ApartmentRoomSelector"/>)은 방 번호만
    /// 알리고, 화면(<see cref="RoomResidentPanel"/>)은 닫혔다는 것만 알린다. 그래서 나중에
    /// "확대된 방에서 캐릭터끼리 자동 대화"(기획에 있는 다음 단계)가 붙어도 여기만 고치면 된다.
    /// </summary>
    public class ApartmentRoomFlow : MonoBehaviour
    {
        [SerializeField] private ApartmentCameraRig cameraRig;
        [SerializeField] private ApartmentRooms rooms;
        [SerializeField] private ApartmentRoomSelector selector;
        [SerializeField] private RoomResidentPanel residentPanel;

        [Tooltip("방 안에 캐릭터를 세우는 쪽. 입주가 바뀌면 그 방만 다시 그린다.")]
        [SerializeField] private ApartmentRoomView roomView;

        [Tooltip("사는 방을 눌렀을 때 뜨는 방 화면(상단 상태 + 하단 버튼). " +
                 "비워두면 예전처럼 확대만 하고 아무 데나 눌러 돌아온다.")]
        [SerializeField] private RoomScreenPanel roomScreen;

        [Tooltip("방꾸미기 화면. 비워두면 방꾸미기 버튼이 아무 일도 안 한다.")]
        [SerializeField] private RoomDecorPanel decorPanel;

        [Tooltip("방 화면의 위·아래 띠가 각각 차지하는 화면 비율. 그만큼 방을 좁혀 담아 " +
                 "<b>띠에 가리지 않게</b> 한다.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float roomScreenTopFraction = 0.16f;

        [Range(0f, 0.6f)]
        [SerializeField] private float roomScreenBottomFraction = 0.26f;

        [Tooltip("아파트 전체 보기 화면. 비워두면 그 기능이 없다.")]
        [SerializeField] private ApartmentOverviewPanel overviewPanel;

        [Tooltip("동 목록. 동이 늘면 카메라를 다시 맞추고 방 그림도 다시 그린다.")]
        [SerializeField] private ApartmentBuildings buildings;

        [Tooltip("캐릭터를 끌어서 옮기는 쪽. 전체 보기에 들어가야 켜진다.")]
        [SerializeField] private ApartmentResidentDragger dragger;

        [Tooltip("메인 화면에서 동을 하나씩 보여주고 밀어서 옮겨 다니게 하는 쪽. " +
                 "전체 보기에서는 꺼진다(거기서는 전부를 한 화면에 담는다).")]
        [SerializeField] private ApartmentBuildingPager pager;

        [Tooltip("입주 화면이 아래에서 차지하는 화면 비율. 그만큼 방을 위로 밀어 올려 " +
                 "<b>화면에 안 가린 채</b> 확대한다. 화면 높이를 바꾸면 이 값도 같이 맞출 것.")]
        [Range(0f, 0.8f)]
        [SerializeField] private float panelHeightFraction = 0.44f;

        [Tooltip("방에 들어간 동안 화면 밖으로 비켜날 HUD(2026-08-28 사용자 지시). " +
                 "아파트 컨텐츠와 상관없는 것들이라 자리를 비운다. " +
                 "<b>끄지 않고 밀어낸다</b> - 꺼버리면 카메라가 HUD 판 크기를 못 재게 된다.")]
        [SerializeField] private HudSlideAway hud;

        private void OnEnable()
        {
            if (selector != null)
                selector.OnRoomPicked += HandleRoomPicked;

            if (residentPanel != null)
            {
                residentPanel.OnClosed += HandlePanelClosed;
                residentPanel.OnResidentChanged += HandleResidentChanged;
                residentPanel.OnPreviewed += HandleResidentPreviewed;
            }

            if (roomScreen != null)
            {
                roomScreen.OnBackRequested += CloseRoomScreen;
                roomScreen.OnDecorRequested += OpenDecor;
            }

            if (overviewPanel != null)
            {
                overviewPanel.OnBackRequested += CloseOverview;
                overviewPanel.OnAddBuildingRequested += AddBuilding;
            }

            if (dragger != null)
            {
                dragger.OnResidentsChanged += HandleResidentsDragged;
                dragger.enabled = false;   // 전체 보기에서만 켠다
            }

            if (buildings != null)
                buildings.OnBuildingsChanged += HandleBuildingsChanged;
        }

        /// <summary>
        /// 미니게임을 끝내고 돌아왔으면 <b>그 방을 다시 열어준다</b>(2026-09-02).
        /// 방에서 나갔는데 아파트 전체 화면으로 떨어지면 "잠긐 놀다 온" 흐름이 끊긴다.
        ///
        /// <b>Start 에서 한다</b> - 방을 여는 데 필요한 것들(동·카메라·판)이 자기 Awake 에서
        /// 준비되므로, 그보다 먼저 불리면 빈 방을 여는 꼴이 된다.
        /// </summary>
        private void Start()
        {
            int room = ScreenRequest.ConsumeOpenRoom();
            if (room >= 0)
            {
                // ⭐ 씬에 들어오자마자 여는 길이다. HUD 는 <b>연출 없이</b> 치운다 -
                // 밀려나는 걸 보여주면 "메인 화면이 한 번 떴다 치워지는" 흔적이 된다
                // (2026-09-02 사용자 지적).
                OpenRoom(room, instant: true);
            }
        }

        /// <summary>
        /// 입주가 바뀌었다. <b>바뀐 방만</b> 다시 그리면 될 것 같지만 전부 그린다 -
        /// 이사는 <b>원래 살던 방도 비우기</b> 때문이다(그 방을 안 그리면 캐릭터가 둘로 보인다).
        /// </summary>
        private void HandleResidentChanged(int roomIndex) => roomView?.Refresh();

        /// <summary>목록에서 누굴 눌러봤다 - 아직 정한 건 아니고 방에 세워만 본다.</summary>
        private void HandleResidentPreviewed(int roomIndex, PanelType character)
            => roomView?.Preview(roomIndex, character);

        private void OnDisable()
        {
            if (selector != null)
                selector.OnRoomPicked -= HandleRoomPicked;

            if (residentPanel != null)
            {
                residentPanel.OnClosed -= HandlePanelClosed;
                residentPanel.OnResidentChanged -= HandleResidentChanged;
                residentPanel.OnPreviewed -= HandleResidentPreviewed;
            }

            if (roomScreen != null)
            {
                roomScreen.OnBackRequested -= CloseRoomScreen;
                roomScreen.OnDecorRequested -= OpenDecor;
            }

            if (overviewPanel != null)
            {
                overviewPanel.OnBackRequested -= CloseOverview;
                overviewPanel.OnAddBuildingRequested -= AddBuilding;
            }

            if (dragger != null)
                dragger.OnResidentsChanged -= HandleResidentsDragged;

            if (buildings != null)
                buildings.OnBuildingsChanged -= HandleBuildingsChanged;
        }

        /// <summary>
        /// 동이 늘었다. <b>카메라를 다시 재고</b> 새 동의 방도 그린다 -
        /// 크기를 한 번 재고 캐시해두기 때문에 다시 재라고 알려주지 않으면 새 동이 화면 밖에 남는다.
        /// </summary>
        private void HandleBuildingsChanged()
        {
            roomView?.Refresh();

            // <b>메인 화면에서는 카메라를 전체로 되돌리지 않는다</b>(2026-08-28) - 거기서는
            // 동 하나만 보는 게 규칙이라, 페이저가 새로 지은 동으로 옮겨 준다.
            if (overviewPanel != null && overviewPanel.IsOpen)
                cameraRig?.RemeasureAndRefocus();
        }

        // ------------------------------------------------------------------ 전체 보기

        /// <summary>
        /// 전체 보기를 연다. <b>HUD 의 '아파트 전체 보기' 버튼이 부른다</b> - 버튼 연결은
        /// <see cref="ApartmentHudController"/> 가 도맡는다(우편함과 같은 방식).
        /// </summary>
        public void OpenOverview()
        {
            if (overviewPanel == null || overviewPanel.IsOpen)
                return;

            viewingOnly = false;

            hud?.SlideAway();

            // 전체 보기에서만 동 전부를 담는다. 미는 조작은 여기서 꺼야 한다 -
            // 안 그러면 전체를 보는 중에 밀려서 한 동으로 좁혀진다.
            if (pager != null)
                pager.enabled = false;

            // <b>전체 보기에서는 HUD 자리를 안 비켜준다</b> - HUD 는 방금 화면 밖으로 나갔다.
            // 판을 먼저 켜야 아래 띠 크기를 잴 수 있다.
            overviewPanel.Show();
            Canvas.ForceUpdateCanvases();
            FocusAllForOverview();

            if (dragger != null)
                dragger.enabled = true;
        }

        private void AddBuilding() => buildings?.AddBuilding();

        /// <summary>
        /// 전체 보기의 카메라. HUD 는 비켜나 있으니 그 자리를 남기지 않고, 아래 띠가
        /// <b>실제로 덮는 만큼</b>만 비운다(비율을 숫자로 박지 않는다).
        /// </summary>
        private void FocusAllForOverview()
        {
            float bottom = overviewPanel != null ? overviewPanel.BottomCoverFraction : -1f;
            cameraRig?.FocusAll(smooth: true, keepHud: false, extraBottom: Mathf.Max(0f, bottom));
        }

        /// <summary>끌어서 옮긴 결과. 두 방이 한꺼번에 바뀔 수 있어 전부 다시 그린다.</summary>
        private void HandleResidentsDragged() => roomView?.Refresh();

        private void CloseOverview()
        {
            if (dragger != null)
                dragger.enabled = false;

            overviewPanel?.Hide();
            hud?.SlideBack();

            // 메인 화면으로 돌아왔으니 <b>보던 동 하나</b>로 좁힌다.
            BackToMainView();
        }

        /// <summary>메인 화면의 기본 카메라 상태 - 동 하나. 페이저가 없으면 예전처럼 전체를 본다.</summary>
        private void BackToMainView()
        {
            if (pager != null)
            {
                pager.enabled = true;
                pager.Reapply();
                return;
            }

            cameraRig?.FocusAll(smooth: true);
        }

        // 입주 화면 없이 방만 들여다보는 중인지. 그때는 아무 데나 누르면 돌아간다.
        private bool viewingOnly;

        // 들여다보기를 시작한 프레임. 그 프레임의 터치로 곧바로 닫히는 걸 막는다
        // (방을 연 그 손가락이 같은 프레임에 '돌아가기'로도 읽힌다).
        private int viewStartedFrame = -1;

        private void HandleRoomPicked(int roomIndex) => OpenRoom(roomIndex, instant: false);

        /// <param name="instant">
        /// HUD 와 카메라를 <b>연출 없이</b> 제자리에 둘지. 씬에 들어오면서 여는 길에서만 켠다 -
        /// 거기서 움직임이 보이면 "메인 화면이 한 번 떴다가 들어가는" 흔적이 된다.
        /// 손으로 방을 누른 것이라면 확대되는 게 보여야 자연스럽다.
        /// </param>
        private void OpenRoom(int roomIndex, bool instant)
        {
            // ⭐ <b>완전히 돌아오기 전에는 방을 열지 않는다</b>(2026-09-02 사용자 지시).
            // '뒤로가기' 뒤에 다른 캐릭터의 방이 겹쳐 있으면, 그 버튼을 누른 손가락이
            // 곧바로 그 방을 여는 일이 있었다. 카메라가 다 물러날 때까지는 아무 방도 안 받는다.
            if (IsReturning)
                return;

            // 이미 열려 있으면 무시한다 - 확대 중에 뒤의 방이 또 눌리면 화면이 겹친다.
            if ((roomScreen != null && roomScreen.IsOpen)
                || (residentPanel != null && residentPanel.IsOpen))
                return;

            // <b>이미 사는 사람이 있으면 고르는 화면을 띄우지 않는다</b>(2026-08-28 사용자 지시).
            // 거주 캐릭터를 바꾸는 건 '아파트 전체 보기'에서 할 일이고, 여기서는 그 방을
            // 들여다보기만 한다.
            // <b>전체 보기의 '바꾸기' 모드에서는 사는 방도 연다</b> - 거기가 거주 캐릭터를
            // 바꾸는 유일한 길이라, 여기서까지 막으면 바꿀 방법이 아예 없어진다.
            // <b>전체 보기에서는 방이 눌리지 않는다.</b> 거기서 하는 일은 끌어서 옮기기다
            // (2026-08-28 사용자가 조작을 바꿨다) - 확대까지 되면 무엇을 하는 화면인지 흐려진다.
            if (overviewPanel != null && overviewPanel.IsOpen)
                return;

            // <b>캐릭터를 들고 있었으면 그건 옮기기지 누르기가 아니다.</b>
            if (dragger != null && dragger.IsHolding)
                return;

            bool occupied = ApartmentResidents.Get(roomIndex) != null;

            // 사는 방은 <b>방 화면</b>이 위아래 띠를 쓰고, 빈 방은 입주 화면이 아래만 쓴다.
            bool useRoomScreen = occupied && roomScreen != null;
            string name = rooms != null ? rooms.GetName(roomIndex) : string.Empty;

            // <b>화면을 먼저 켠다</b>(2026-08-30에 순서를 바꿈). 띠가 화면을 얼마나 덮는지는
            // 레터박스 배율에 따라 기기마다 다르므로 <b>실제로 그려진 자리를 재야</b> 하는데,
            // 켜기 전에는 잴 게 없다. 예전엔 인스펙터 비율을 그대로 썼다가, 좁은 폰에서
            // 띠가 그보다 넓게 덮어 방 위아래가 가려졌다.
            openRoom = useRoomScreen ? roomIndex : -1;

            if (occupied)
            {
                if (useRoomScreen)
                    roomScreen.Open(roomIndex, name);
                else
                {
                    // 방 화면이 없으면 예전처럼 들여다보기만 한다(아무 데나 눌러 돌아온다).
                    viewingOnly = true;
                    viewStartedFrame = Time.frameCount;
                }
            }
            else
                residentPanel?.Open(roomIndex, name);

            // 방금 켠 판은 아직 크기가 안 잡혀 있다 - 재기 전에 한 번 밀어준다.
            Canvas.ForceUpdateCanvases();

            float bottom = useRoomScreen ? Measured(roomScreen.BottomCoverFraction, roomScreenBottomFraction)
                                         : (occupied ? 0f
                                            : Measured(residentPanel != null ? residentPanel.CoverFraction : -1f,
                                                       panelHeightFraction));
            float top = useRoomScreen ? Measured(roomScreen.TopCoverFraction, roomScreenTopFraction) : 0f;

            if (cameraRig != null && rooms != null && rooms.TryGetRoomBounds(roomIndex, out var room))
                cameraRig.FocusRoom(room, bottom, top, smooth: !instant);

            if (instant)
                hud?.HideInstantly();
            else
                hud?.SlideAway();

            // 방을 보는 동안 밀면 카메라가 동으로 튕겨 나간다.
            if (pager != null)
                pager.enabled = false;
        }

        /// <summary>재본 값이 쓸 만하면 그걸, 아니면 인스펙터에 적힌 값을.</summary>
        private static float Measured(float measured, float fallback)
            => measured >= 0f ? measured : fallback;

        /// <summary>
        /// 들여다보는 중에는 <b>아무 데나 누르면</b> 전체로 돌아간다. 닫을 버튼이 없는 화면이라
        /// 나갈 길이 하나는 있어야 한다 - HUD 도 비켜나 있어서 '아파트 전체 보기'를 누를 수도 없다.
        /// </summary>
        private void Update()
        {
            Update_ReleaseReturnLock();
            UpdateRoomSwipe();

            if (!viewingOnly || Time.frameCount == viewStartedFrame)
                return;

            // 전체 보기가 떠 있으면 그 화면의 '뒤로가기'가 나가는 길이다.
            if (overviewPanel != null && overviewPanel.IsOpen)
                return;

            if (Input.GetMouseButtonDown(0))
                ReturnToAll();
        }

        /// <summary>
        /// 입주 화면이 닫혔다. <b>전체 보기에서 열었으면 그쪽으로 돌아간다</b> -
        /// 방을 하나 바꿀 때마다 메인 화면까지 나갔다 다시 들어오게 하면 손이 많이 간다.
        /// </summary>
        /// <summary>
        /// 방꾸미기를 연다. <b>어느 방인지는 여기가 안다</b> - 방 화면은 눌렸다는 것만 알린다.
        /// 방 화면은 그대로 두고 그 위에 얹는다 - 꾸미고 나면 곧바로 그 방이 보여야 한다.
        /// </summary>
        private void OpenDecor()
        {
            if (decorPanel == null || openRoom < 0)
                return;

            decorPanel.Open(openRoom, rooms != null ? rooms.GetName(openRoom) : string.Empty);
        }

        private void CloseRoomScreen()
        {
            openRoom = -1;
            roomScreen?.Close();
            ReturnToAll();
        }

        private void HandlePanelClosed()
        {
            // 미리 세워봤던 캐릭터를 <b>입주 정보대로</b> 되돌린다 - 결정을 눌렀으면 그게 곧
            // 입주 정보라 그대로고, 취소했으면 원래 살던 사람으로 돌아온다.
            roomView?.Refresh();

            if (overviewPanel != null && overviewPanel.IsOpen)
            {
                FocusAllForOverview();
                return;
            }

            ReturnToAll();
        }

        // 방에서 빠져나오는 중. 카메라가 다 물러날 때까지 방을 안 받는다.
        private bool returning;
        private int returnStartedFrame = -1;

        /// <summary>
        /// 아직 <b>메인 화면으로 다 돌아오지 않았는지</b>.
        ///
        /// 시작한 프레임은 무조건 막는다 - 버튼을 누른 그 손가락이 <b>같은 프레임에</b>
        /// 떼어지면서 뒤의 방까지 여는 게 원래 증상이었다. 그 뒤로는 카메라가 멎을 때까지 막는다.
        /// </summary>
        private bool IsReturning => returning
            && (Time.frameCount == returnStartedFrame
                || cameraRig == null || cameraRig.IsMoving);

        private void Update_ReleaseReturnLock()
        {
            if (returning && !IsReturning)
                returning = false;
        }

        private void ReturnToAll()
        {
            viewingOnly = false;

            returning = true;
            returnStartedFrame = Time.frameCount;

            hud?.SlideBack();
            BackToMainView();
        }
        // ------------------------------------------------------------------ 방 사이 밀기

        [Header("방 사이 밀어서 옮기기")]
        [Tooltip("이만큼(화면의 짧은 변 대비) 밀어야 옆방으로 넘어간다. " +
                 "픽셀이 아니라 비율이라 기기 해상도가 달라도 손맛이 같다.")]
        [Range(0.02f, 0.5f)]
        [SerializeField] private float roomSwipeFraction = 0.12f;

        // 지금 방 화면으로 열려 있는 방. 없으면 -1.
        private int openRoom = -1;

        private bool swiping;
        private Vector3 swipeStart;

        /// <summary>
        /// ⭐ 방 화면에서 <b>다른 방 쪽으로 밀면 그 방으로 옮겨 간다</b>(2026-09-02 사용자 지시).
        /// 위로 밀면 윗층, 아래로 밀면 아랫층, 옆으로 밀면 옆 동의 같은 층이다 -
        /// <b>미는 방향이 곧 가려는 방향</b>이다.
        ///
        /// ⚠ 메인 화면의 동 넘기기(<see cref="ApartmentBuildingPager"/>)는 반대 규칙이다
        /// (거기서는 아파트를 손으로 끄는 것이라 왼쪽으로 밀면 오른쪽 동이 들어온다).
        ///
        /// <b>빈 방으로는 안 넘어간다</b> - 방 화면은 사는 사람이 있어야 열리는 화면이라,
        /// 빈 방으로 넘기면 보여줄 게 없다.
        /// </summary>
        private void UpdateRoomSwipe()
        {
            // 방꾸미기가 위에 떠 있으면 그 화면의 조작이다 - 뒤의 방이 넘어가면 안 된다.
            if (decorPanel != null && decorPanel.IsOpen)
            {
                swiping = false;
                return;
            }

            if (openRoom < 0 || rooms == null || roomScreen == null || !roomScreen.IsOpen || IsReturning)
            {
                swiping = false;
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                swiping = true;
                swipeStart = Input.mousePosition;
                return;
            }

            if (!swiping)
                return;

            if (!Input.GetMouseButton(0))
            {
                swiping = false;
                return;
            }

            Vector3 moved = Input.mousePosition - swipeStart;

            // 짧은 변을 기준으로 삼는다 - 가로세로 어느 쪽으로 밀든 손맛이 같아야 한다.
            float threshold = Mathf.Min(Screen.width, Screen.height) * roomSwipeFraction;
            if (moved.sqrMagnitude < threshold * threshold)
                return;

            swiping = false;

            int target = NeighbourRoom(moved);
            if (target < 0)
                return;

            // 이 화면을 닫고 그 방을 연다. <b>CloseRoomScreen 을 쓰지 않는다</b> -
            // 그건 메인 화면으로 물러나는 길이라 잠금이 걸려 새 방이 안 열린다.
            roomScreen.Close();
            openRoom = -1;

            OpenRoom(target, instant: false);
        }

        /// <summary>민 방향에 있는 방. 없거나 빈 방이면 -1.</summary>
        private int NeighbourRoom(Vector3 moved)
        {
            int building = rooms.BuildingOf(openRoom);
            int floor = rooms.FloorOf(openRoom);

            if (Mathf.Abs(moved.x) > Mathf.Abs(moved.y))
                building += moved.x > 0f ? 1 : -1;
            else
                floor += moved.y > 0f ? 1 : -1;

            if (building < 0 || building >= rooms.BuildingCount)
                return -1;

            if (floor < 0 || floor >= rooms.FloorsPerBuilding)
                return -1;

            int target = rooms.ToRoomIndex(building, floor);
            return ApartmentResidents.Get(target) != null ? target : -1;
        }

    }
}

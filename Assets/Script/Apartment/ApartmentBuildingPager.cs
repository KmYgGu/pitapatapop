using UnityEngine;
using UnityEngine.EventSystems;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 메인 화면에서 <b>동을 하나씩</b> 보여주고, 좌우로 밀어 옆 동으로 옮겨 간다
    /// (2026-08-28 사용자 지시).
    ///
    /// <b>왜 한 동만 보이나</b>: 동이 늘어날수록 전부를 한 화면에 담으면 방 하나가 작아져
    /// 누를 수가 없다. 메인 화면은 <b>지금 보는 동</b>에 집중하고, 전부를 훑는 건
    /// <see cref="ApartmentOverviewPanel"/>(전체 보기)가 맡는다.
    ///
    /// <b>⚠ 방 누르기와 겹치지 않아야 한다.</b> <see cref="ApartmentRoomSelector"/> 가
    /// "누른 자리에서 거의 안 움직이고 뗐을 때"만 방을 열도록 바뀐 것이 그 짝이다 -
    /// 안 그러면 미는 순간 방이 열린다.
    /// </summary>
    public class ApartmentBuildingPager : MonoBehaviour
    {
        [SerializeField] private ApartmentCameraRig cameraRig;
        [SerializeField] private ApartmentBuildings buildings;

        [Tooltip("이만큼(화면 폭 대비 비율) 밀어야 옆 동으로 넘어간다. 덜 밀면 제자리다. " +
                 "<b>픽셀이 아니라 비율</b>이라 기기 해상도가 달라도 손맛이 같다.")]
        [Range(0.02f, 0.5f)]
        [SerializeField] private float swipeFraction = 0.12f;

        [Header("가장자리에서 계속 넘기기")]
        [Tooltip("화면 좌우 <b>가장자리 띠</b>의 폭(화면 폭 대비 비율). 민 손가락을 이 안에서 " +
                 "떼지 않고 있으면 그 방향으로 계속 넘어간다 - 갤러리에서 사진을 끝까지 " +
                 "넘길 때와 같은 조작이다(2026-08-28 사용자 지시: 끝 동까지 가려고 계속 " +
                 "미는 건 피곤하다).")]
        [Range(0.02f, 0.4f)]
        [SerializeField] private float edgeFraction = 0.14f;

        [Tooltip("가장자리에 닿고 <b>처음</b> 다시 넘어가기까지의 시간(초). 짧으면 한 번 밀었을 뿐인데 " +
                 "두 동이 지나간다.")]
        [SerializeField] private float repeatDelay = 0.5f;

        [Tooltip("그 뒤로 계속 넘어가는 간격(초).")]
        [SerializeField] private float repeatInterval = 0.35f;

        /// <summary>지금 보고 있는 동 번호.</summary>
        public int CurrentBuilding { get; private set; }

        private bool pressing;
        private Vector3 pressStart;

        // 문턱을 한 번이라도 넘겼는지. <b>가장자리 자동 넘김은 그 뒤에만</b> 동작한다 -
        // 가만히 가장자리를 누르고만 있어도 넘어가면 손이 닿기만 해도 화면이 흐른다.
        private bool dragging;

        private float nextRepeatTime;

        private void OnEnable()
        {
            if (buildings != null)
                buildings.OnBuildingsChanged += HandleBuildingsChanged;
        }

        /// <summary>
        /// 처음부터 <b>동 하나</b>로 맞춰둔다. 카메라의 Start 는 "전부 보기"로 맞추므로,
        /// 그걸 여기서 좁혀준다 - 동이 하나뿐일 때는 결과가 같고, 늘어난 뒤에 이게 없으면
        /// 메인 화면이 전부 보기로 시작한다.
        ///
        /// <b>부드럽게 하지 않는다</b> - 화면이 처음 뜨는 순간에 카메라가 미끄러지면 고장처럼 보인다.
        /// </summary>
        private void Start() => Show(CurrentBuilding, smooth: false);

        private void OnDisable()
        {
            if (buildings != null)
                buildings.OnBuildingsChanged -= HandleBuildingsChanged;

            pressing = false;
        }

        /// <summary>
        /// 동이 늘면 <b>새로 생긴 동으로 옮겨 간다</b> - 방금 지은 것을 보여주는 게 자연스럽다.
        /// </summary>
        private void HandleBuildingsChanged()
        {
            if (buildings == null)
                return;

            Show(buildings.Count - 1);
        }

        /// <summary>
        /// <b>손을 떼기 전에</b> 넘긴다(2026-08-28). 갤러리처럼 밀자마자 따라와야 하고,
        /// 무엇보다 <b>가장자리에서 누르고 있는 동안 계속</b> 넘어가야 하기 때문이다 -
        /// 뗄 때 한 번만 넘기면 끝 동까지 가는 데 손짓을 여러 번 해야 한다.
        /// </summary>
        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                bool overUI = EventSystem.current != null
                              && EventSystem.current.IsPointerOverGameObject();

                pressing = !overUI;
                dragging = false;
                pressStart = Input.mousePosition;
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                pressing = false;
                dragging = false;
                return;
            }

            if (!pressing || !Input.GetMouseButton(0))
                return;

            float moved = Input.mousePosition.x - pressStart.x;
            float threshold = Screen.width * swipeFraction;

            // 문턱을 넘기면 <b>그 자리에서</b> 한 칸 넘어간다. 기준점을 지금 자리로 옮겨서
            // 계속 밀면 계속 넘어가게 한다.
            if (Mathf.Abs(moved) >= threshold)
            {
                Step(moved < 0f ? 1 : -1);
                pressStart = Input.mousePosition;
                dragging = true;
                nextRepeatTime = Time.time + repeatDelay;
                return;
            }

            if (!dragging)
                return;

            // 손가락이 가장자리 띠 안에 머물러 있으면 그 방향으로 계속 넘어간다.
            int edge = EdgeDirection(Input.mousePosition.x);
            if (edge == 0)
                return;

            if (Time.time < nextRepeatTime)
                return;

            Step(edge);
            nextRepeatTime = Time.time + repeatInterval;
        }

        /// <summary>
        /// 그 x 좌표가 어느 가장자리인지. 왼쪽 띠면 +1(다음 동), 오른쪽 띠면 -1(이전 동),
        /// 가운데면 0.
        ///
        /// <b>방향이 미는 방향과 같다</b>: 왼쪽으로 밀면 오른쪽 동이 따라오므로, 손가락이
        /// 왼쪽 끝에 머문다는 건 계속 왼쪽으로 밀고 있다는 뜻이다.
        /// </summary>
        private int EdgeDirection(float screenX)
        {
            float band = Screen.width * edgeFraction;

            if (screenX <= band)
                return 1;

            if (screenX >= Screen.width - band)
                return -1;

            return 0;
        }

        /// <summary>한 칸 옮긴다. 양끝에서는 아무 일도 일어나지 않는다.</summary>
        private void Step(int delta) => Show(CurrentBuilding + delta);

        /// <summary>그 동을 화면에 담는다. 범위를 벗어나면 아무 일도 하지 않는다(양끝에서 멈춘다).</summary>
        public void Show(int index, bool smooth = true)
        {
            if (buildings == null || cameraRig == null)
                return;

            int clamped = Mathf.Clamp(index, 0, Mathf.Max(0, buildings.Count - 1));
            if (!buildings.TryGetBuildingBounds(clamped, out var bounds))
                return;

            CurrentBuilding = clamped;
            cameraRig.FocusBuilding(bounds, smooth);
        }

        /// <summary>지금 동을 다시 맞춘다. 메인 화면으로 돌아올 때 흐름이 부른다.</summary>
        public void Reapply(bool smooth = true) => Show(CurrentBuilding, smooth);
    }
}

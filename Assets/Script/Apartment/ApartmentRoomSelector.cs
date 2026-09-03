using UnityEngine;
using UnityEngine.EventSystems;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// <b>아파트의 방을 눌렀는지</b>를 알아내는 곳. 눌린 방 번호를 알리기만 하고, 확대나 화면
    /// 열기는 <see cref="ApartmentRoomFlow"/> 가 정한다(퍼즐 쪽의 입력/규칙 분리와 같은 방침).
    ///
    /// <b>⚠ 콜라이더를 쓰지 않는다.</b> 아파트 모델은 메시가 하나뿐이고 방은 머티리얼 이름으로만
    /// 갈라져 있어서 방마다 콜라이더를 놓으려면 모델을 뜯어야 한다. 대신 <b>모델 앞면에 평면을
    /// 하나 세워 거기를 맞히고</b>, 맞은 높이가 어느 방의 띠에 드는지로 방을 정한다
    /// (<see cref="ApartmentRooms"/>). 모델을 갈아끼워도 따라오고, 임포트 배율이 바뀌어도 맞는다.
    /// </summary>
    public class ApartmentRoomSelector : MonoBehaviour
    {
        [Tooltip("아파트를 비추는 카메라. 비워두면 이 오브젝트의 Camera 를 쓴다.")]
        [SerializeField] private Camera targetCamera;

        [SerializeField] private ApartmentCameraRig cameraRig;
        [SerializeField] private ApartmentRooms rooms;

        [Tooltip("모델 가로 폭에서 이만큼 벗어난 곳을 눌러도 방으로 친다(폭 대비 비율). " +
                 "0이면 모델 바깥은 전부 무시한다. 손가락이 굵어 가장자리를 놓치는 걸 막는 여유다.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float horizontalSlack = 0.05f;

        /// <summary>방을 눌렀다(인자 = 방 번호, 0 = 1층).</summary>
        public event System.Action<int> OnRoomPicked;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = GetComponent<Camera>();
        }

        [Tooltip("이만큼(화면 픽셀) 안에서 떼야 '눌렀다'로 친다. 그보다 많이 움직였으면 " +
                 "<b>미는 조작</b>이므로 방을 열지 않는다(동 넘기기와 겹치지 않게).")]
        [SerializeField] private float tapSlopPixels = 20f;

        private bool pressValid;
        private Vector3 pressStart;

        /// <summary>
        /// <b>누른 순간이 아니라 뗄 때</b> 방을 연다(2026-08-28). 화면을 밀어 동을 넘기는
        /// 조작이 생기면서, 누르자마자 열면 <b>밀기 시작하는 순간 방이 열려버린다.</b>
        /// </summary>
        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                // <b>UI 위를 누른 건 무시한다.</b> 버튼을 눌렀는데 그 뒤의 방까지 같이 눌리면
                // 화면이 두 개 열린다. 아파트 HUD 는 가운데를 비워두는 규칙이라 평소엔 안 겹치지만,
                // 확대 화면이 떠 있는 동안에는 화면 전체가 UI 다.
                pressValid = !IsOverUI();
                pressStart = Input.mousePosition;
                return;
            }

            if (!Input.GetMouseButtonUp(0) || !pressValid)
                return;

            pressValid = false;

            if ((Input.mousePosition - pressStart).sqrMagnitude > tapSlopPixels * tapSlopPixels)
                return;   // 밀었다 - 누른 게 아니다

            int room = PickRoomAt(Input.mousePosition);
            if (room >= 0)
                OnRoomPicked?.Invoke(room);
        }

        /// <summary>
        /// 손가락이 UI 위에 있는지.
        ///
        /// ⚠ <b>터치는 손가락 번호를 넣어 물어야 한다.</b> 인자 없이 부르면 <b>마우스</b>(-1번)를
        /// 기준으로 보기 때문에, 실제 기기에서는 버튼 위를 눌러도 <b>false</b> 가 나온다 -
        /// 그래서 '뒤로가기'를 눌렀는데 그 뒤의 방까지 같이 눌렸다(2026-09-02 사용자 신고).
        /// 에디터에서는 마우스라 멀쩡히 보여 놓치기 쉬운 함정이다.
        /// </summary>
        private static bool IsOverUI()
        {
            var events = EventSystem.current;
            if (events == null)
                return false;

            if (Input.touchCount > 0)
                return events.IsPointerOverGameObject(Input.GetTouch(0).fingerId);

            return events.IsPointerOverGameObject();
        }

        /// <summary>
        /// 화면 좌표가 가리키는 방 번호. 방이 아니면 -1.
        /// </summary>
        /// <param name="extraSlack">
        /// 방 밖으로 벗어난 것을 얼마나 봐줄지. 0이면 딱 방 안이어야 한다(누르기).
        /// 캐릭터를 <b>옮겨 놓을 때</b>는 값을 넣어 너그럽게 본다 - 아래 두 곳에 함께 쓰인다:
        /// 가로는 동 폭 대비, 세로는 층 높이 대비 비율이다.
        /// </param>
        public int PickRoomAt(Vector3 screenPosition, float extraSlack = 0f)
        {
            if (targetCamera == null || cameraRig == null || rooms == null)
                return -1;

            if (!cameraRig.TryGetModelBounds(out var bounds))
                return -1;

            // 모델 <b>앞면</b>에 평면을 세운다. 가운데에 세우면 방이 위아래로 쌓인 모델에서
            // 비스듬히 맞아 한 층씩 어긋난다(원근이라 더 그렇다).
            Vector3 forward = targetCamera.transform.forward;
            float halfDepth = Mathf.Abs(bounds.extents.x * forward.x)
                            + Mathf.Abs(bounds.extents.y * forward.y)
                            + Mathf.Abs(bounds.extents.z * forward.z);

            Vector3 frontPoint = bounds.center - forward * halfDepth;
            var plane = new Plane(-forward, frontPoint);

            var ray = targetCamera.ScreenPointToRay(screenPosition);
            if (!plane.Raycast(ray, out float distance))
                return -1;

            // 어느 <b>동</b>의 어느 층인지는 방 목록이 안다 - 동이 늘어나도 여기는 안 고친다.
            return rooms.FindRoomAt(ray.GetPoint(distance),
                                    horizontalSlack + Mathf.Max(0f, extraSlack),
                                    Mathf.Max(0f, extraSlack));
        }
    }
}

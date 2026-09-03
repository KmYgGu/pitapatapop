using UnityEngine;
using UnityEngine.EventSystems;
using JojoPuzzle.App;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 방에 서 있는 캐릭터를 <b>꾹 눌러 집어 옮긴다</b>(2026-08-28 사용자 지시로 '입주민 바꾸기'
    /// 버튼을 대신하게 된 조작).
    ///
    /// <code>
    ///   방의 캐릭터를 꾹 누른다      → 손에 들린다(따라다닌다)
    ///   아파트 밖에 놓는다           → 쫓겨나 빈 방이 된다
    ///   다른 방에 놓는다             → 그 방으로 이사. 그 방에 누가 있으면 <b>서로 맞바꾼다</b>
    /// </code>
    ///
    /// <b>왜 버튼이 아니라 이 방식인가</b>(사용자 판단): 버튼은 "지금 무슨 모드인지"를 따로
    /// 기억해야 하는데, 집어서 옮기는 건 화면에 보이는 그대로라 설명이 필요 없다.
    ///
    /// <b>⚠ 짧게 누른 것과 구분해야 한다.</b> 방을 짧게 누르면 확대(들여다보기)이므로,
    /// <see cref="holdSeconds"/> 만큼 누르고 있어야 비로소 들린다.
    /// </summary>
    public class ApartmentResidentDragger : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private ApartmentRoomSelector selector;
        [SerializeField] private ApartmentRooms rooms;
        [SerializeField] private ApartmentRoomView roomView;

        [Tooltip("이만큼 누르고 있어야 캐릭터가 들린다(초). 짧게 누르면 방 확대다.")]
        [SerializeField] private float holdSeconds = 0.35f;

        [Tooltip("누른 채 이만큼(화면 픽셀) 움직이면 <b>기다리지 않고</b> 곧바로 들린다 - " +
                 "끌려는 의도가 분명하기 때문.")]
        [SerializeField] private float dragStartPixels = 24f;

        [Tooltip("놓을 때 방 판정을 얼마나 너그럽게 볼지. <b>방 크기 대비 비율</b>이라 " +
                 "0.6이면 층 높이의 60%(가로는 동 폭의 60%)만큼 벗어나도 그 방에 놓은 것으로 본다.\n" +
                 "0이면 방 안에 정확히 놓아야 하고, 조금만 빗나가도 캐릭터가 쫓겨난다. " +
                 "너무 키우면 이번엔 <b>내보내기가 어려워진다</b>.")]
        [Range(0f, 1.5f)]
        [SerializeField] private float dropSlack = 0.6f;

        [Tooltip("들고 있는 동안 커지는 배율. 손에 들렸다는 게 보여야 한다.")]
        [SerializeField] private float heldScale = 1.15f;

        /// <summary>입주가 바뀌었다. 흐름이 받아서 방 그림을 다시 그린다.</summary>
        public event System.Action OnResidentsChanged;

        private bool pressing;
        private bool holding;
        private float pressStartTime;
        private Vector3 pressStartPos;

        private int sourceRoom = -1;
        private Transform held;
        private Vector3 heldHomePosition;
        private Vector3 heldHomeScale;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
                BeginPress();
            else if (Input.GetMouseButton(0))
                ContinuePress();
            else if (Input.GetMouseButtonUp(0))
                Release();
        }

        private void BeginPress()
        {
            // UI 위를 누른 건 무시한다 - 버튼을 누르다 캐릭터가 딸려 오면 안 된다.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (selector == null)
                return;

            int room = selector.PickRoomAt(Input.mousePosition);
            if (room < 0 || ApartmentResidents.Get(room) == null)
                return;

            pressing = true;
            holding = false;
            sourceRoom = room;
            pressStartTime = Time.time;
            pressStartPos = Input.mousePosition;
        }

        private void ContinuePress()
        {
            if (!pressing)
                return;

            if (!holding)
            {
                bool longEnough = Time.time - pressStartTime >= holdSeconds;
                bool movedEnough = (Input.mousePosition - pressStartPos).sqrMagnitude
                                   >= dragStartPixels * dragStartPixels;

                if (longEnough || movedEnough)
                    StartHolding();
                else
                    return;
            }

            if (held != null)
                held.position = PointerWorldPosition(heldHomePosition.z);
        }

        private void StartHolding()
        {
            held = roomView != null ? roomView.GetSlot(sourceRoom) : null;
            if (held == null)
            {
                // 그림이 없어도 규칙은 돌아야 한다 - 들린 것처럼 보이지만 않을 뿐이다.
                holding = true;
                return;
            }

            holding = true;
            heldHomePosition = held.position;
            heldHomeScale = held.localScale;
            held.localScale = heldHomeScale * Mathf.Max(0.01f, heldScale);
        }

        private void Release()
        {
            if (!pressing)
                return;

            bool wasHolding = holding;
            int from = sourceRoom;

            // 들었던 것을 먼저 제자리로 돌려놓는다 - 아래에서 다시 그리지만, 규칙이
            // 아무것도 안 바꾸는 경우(같은 방에 놓기)에는 그리기가 일어나지 않는다.
            if (held != null)
            {
                held.position = heldHomePosition;
                held.localScale = heldHomeScale;
            }

            pressing = false;
            holding = false;
            held = null;
            sourceRoom = -1;

            if (!wasHolding || from < 0)
                return;   // 짧게 눌렀다 - 방 확대는 흐름이 알아서 한다

            // <b>방에 딱 맞춰 놓지 않아도 된다</b>(2026-08-30 사용자 지시 - 퍼즐 조각을 놓을 때와
            // 같은 생각). 층과 층 사이 틈이나 동 가장자리에 조금 걸쳐 놓았다고 캐릭터가 쫓겨나면
            // 옮기는 일 자체가 조심스러워진다. 정말 멀리 놓았을 때만 내보낸다.
            int to = selector != null ? selector.PickRoomAt(Input.mousePosition, dropSlack) : -1;

            if (to < 0)
            {
                // <b>아파트 밖에 놓았다</b> - 쫓겨난다.
                ApartmentResidents.Vacate(from);
            }
            else if (to != from)
            {
                // <b>다른 방에 놓았다</b> - 이사. 그 방에 누가 있으면 서로 맞바꾼다.
                ApartmentResidents.Swap(from, to);
            }
            else
            {
                return;   // 같은 방 - 아무것도 안 바뀐다
            }

            OnResidentsChanged?.Invoke();
        }

        /// <summary>
        /// 손가락이 가리키는 월드 자리. 캐릭터가 서 있던 <b>깊이(z)</b>를 그대로 유지한다 -
        /// 원근이라 깊이가 바뀌면 크기까지 같이 변해서 들고 있는 게 커졌다 작아졌다 한다.
        /// </summary>
        private Vector3 PointerWorldPosition(float z)
        {
            if (targetCamera == null)
                return heldHomePosition;

            var plane = new Plane(-targetCamera.transform.forward, new Vector3(0f, 0f, z));
            var ray = targetCamera.ScreenPointToRay(Input.mousePosition);

            return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : heldHomePosition;
        }

        /// <summary>지금 무언가를 들고 있는지. 흐름이 이걸 보고 "짧게 누른 것"과 가른다.</summary>
        public bool IsHolding => holding;
    }
}

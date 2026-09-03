using System;
using UnityEngine;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 층 하나가 모델의 <b>어느 높이 띠</b>인지. 절대 좌표가 아니라 <b>비율</b>이다.
    /// </summary>
    [Serializable]
    public class ApartmentRoom
    {
        [Tooltip("층 이름. 동이 여럿이면 앞에 '2동'이 붙는다.")]
        public string displayName = "1층";

        [Tooltip("동 높이에서 이 층의 바닥이 있는 비율(0=맨 아래, 1=맨 위).")]
        [Range(0f, 1f)]
        public float bottomRatio;

        [Tooltip("천장 비율.")]
        [Range(0f, 1f)]
        public float topRatio = 1f;
    }

    /// <summary>
    /// 방을 세는 <b>단 하나의 기준</b>. 방 번호는 <b>동과 층을 합친 전역 번호</b>다:
    ///
    /// <code>
    ///   방 번호 = 동 번호 x 층수 + 층 번호      (0 = 1동 1층)
    /// </code>
    ///
    /// <b>왜 전역 번호인가</b>: 입주 정보(<see cref="App.ApartmentResidents"/>)가 int 하나로
    /// 방을 가리키는데, 동이 늘어난다고 그 자료구조를 (동,층) 쌍으로 바꾸면 저장·화면이 전부
    /// 따라 바뀐다. 층수가 고정이라 전역 번호는 동이 늘어도 <b>이미 쓰던 번호가 안 밀린다</b>.
    ///
    /// <b>⚠ 비율로 자른다.</b> 모델은 메시가 하나뿐이고 방은 머티리얼 이름으로만 갈라져 있으며,
    /// 임포트 배율은 재익스포트로 바뀔 수 있다(아파트 함정 ②). 그래서 동의 실제 bounds 를
    /// 런타임에 재서 그 안의 비율로 층을 자른다.
    /// </summary>
    public class ApartmentRooms : MonoBehaviour
    {
        [Tooltip("동 하나의 층 목록. 아래층부터.")]
        [SerializeField]
        private ApartmentRoom[] rooms =
        {
            new ApartmentRoom { displayName = "1층", bottomRatio = 0.020f, topRatio = 0.327f },
            new ApartmentRoom { displayName = "2층", bottomRatio = 0.347f, topRatio = 0.653f },
            new ApartmentRoom { displayName = "3층", bottomRatio = 0.673f, topRatio = 0.980f },
        };

        [Tooltip("방을 확대할 때 위아래로 조금 더 잡아줄 여유(방 높이 대비 비율).")]
        [Range(0f, 0.5f)]
        [SerializeField] private float focusMargin = 0.06f;

        [Tooltip("동 목록. 비워두면 동이 하나뿐인 것으로 친다.")]
        [SerializeField] private ApartmentBuildings buildings;

        /// <summary>동 하나에 있는 층 수.</summary>
        public int FloorsPerBuilding => rooms != null ? rooms.Length : 0;

        /// <summary>동 수(모르면 1).</summary>
        public int BuildingCount => buildings != null ? Mathf.Max(1, buildings.Count) : 1;

        /// <summary>지금 존재하는 방의 총 수.</summary>
        public int Count => FloorsPerBuilding * BuildingCount;

        public int BuildingOf(int roomIndex)
            => FloorsPerBuilding > 0 ? roomIndex / FloorsPerBuilding : 0;

        public int FloorOf(int roomIndex)
            => FloorsPerBuilding > 0 ? roomIndex % FloorsPerBuilding : 0;

        public int ToRoomIndex(int building, int floor) => building * FloorsPerBuilding + floor;

        /// <summary>화면에 적을 방 이름. 동이 둘 이상이면 "2동 3층".</summary>
        public string GetName(int roomIndex)
        {
            var floor = GetFloor(roomIndex);
            if (floor == null)
                return string.Empty;

            return BuildingCount > 1
                ? $"{BuildingOf(roomIndex) + 1}동 {floor.displayName}"
                : floor.displayName;
        }

        private ApartmentRoom GetFloor(int roomIndex)
        {
            int floor = FloorOf(roomIndex);
            return rooms != null && floor >= 0 && floor < rooms.Length ? rooms[floor] : null;
        }

        /// <summary>그 방이 속한 동의 영역.</summary>
        public bool TryGetBuildingBounds(int roomIndex, out Bounds bounds)
        {
            if (buildings != null)
                return buildings.TryGetBuildingBounds(BuildingOf(roomIndex), out bounds);

            bounds = default;
            return false;
        }

        /// <summary>
        /// 방 하나가 월드에서 차지하는 영역. 가로·깊이는 동 그대로 쓰고 <b>높이만 잘라낸다</b> -
        /// 층은 위아래로 쌓여 있고 좌우로는 나뉘어 있지 않다.
        /// </summary>
        public bool TryGetRoomBounds(int roomIndex, out Bounds result)
        {
            result = default;

            var floor = GetFloor(roomIndex);
            if (floor == null || !TryGetBuildingBounds(roomIndex, out var building))
                return false;

            result = CutFloor(building, floor);
            return true;
        }

        private Bounds CutFloor(Bounds building, ApartmentRoom floor)
        {
            float min = building.min.y + building.size.y * Mathf.Min(floor.bottomRatio, floor.topRatio);
            float max = building.min.y + building.size.y * Mathf.Max(floor.bottomRatio, floor.topRatio);

            float margin = (max - min) * focusMargin;
            min -= margin;
            max += margin;

            var center = building.center;
            center.y = (min + max) * 0.5f;

            var size = building.size;
            size.y = Mathf.Max(0.001f, max - min);

            return new Bounds(center, size);
        }

        /// <summary>
        /// 그 자리에 있는 방 번호. 동과 층을 <b>둘 다</b> 본다.
        /// 어느 방에도 안 걸리면 -1(동 사이의 빈 곳, 슬래브, 지붕).
        /// </summary>
        /// <param name="verticalSlack">
        /// 층 밖으로 얼마나 벗어나도 그 층으로 볼지, <b>층 높이 대비 비율</b>.
        /// 0이면 딱 안에 들어와야 한다(방을 눌러 여는 자리는 이쪽).
        /// 0보다 크면 <b>가장 가까운 층</b>으로 끌어당긴다 - 캐릭터를 옮겨 놓을 때 쓴다
        /// (퍼즐 조각을 놓을 때 칸에 딱 맞추지 않아도 되는 것과 같은 생각).
        /// </param>
        public int FindRoomAt(Vector3 worldPoint, float horizontalSlack = 0.05f,
            float verticalSlack = 0f)
        {
            int building = buildings != null
                ? buildings.FindBuildingAt(worldPoint.x, horizontalSlack)
                : 0;

            if (building < 0 || rooms == null)
                return -1;

            if (!TryGetBuildingBounds(ToRoomIndex(building, 0), out var bounds)
                || bounds.size.y <= 0.0001f)
                return -1;

            float ratio = (worldPoint.y - bounds.min.y) / bounds.size.y;

            // 층과 층 사이에는 <b>틈이 있다</b>(1층 …0.327 / 2층 0.347…). 그 틈에 놓았다고
            // 아무 방도 아니라고 하면, 캐릭터가 쫓겨난다 - 그래서 가까운 층을 같이 찾아둔다.
            int nearest = -1;
            float nearestDistance = float.MaxValue;

            for (int floor = 0; floor < rooms.Length; floor++)
            {
                float min = Mathf.Min(rooms[floor].bottomRatio, rooms[floor].topRatio);
                float max = Mathf.Max(rooms[floor].bottomRatio, rooms[floor].topRatio);

                if (ratio >= min && ratio <= max)
                    return ToRoomIndex(building, floor);

                if (verticalSlack <= 0f)
                    continue;

                float height = max - min;
                if (height <= 0.0001f)
                    continue;

                float distance = ratio < min ? min - ratio : ratio - max;
                if (distance <= height * verticalSlack && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = ToRoomIndex(building, floor);
                }
            }

            return nearest;
        }
    }
}

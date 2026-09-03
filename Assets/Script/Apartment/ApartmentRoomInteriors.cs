using System;
using UnityEngine;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 아파트 화면에서 <b>방마다 인테리어를 칠하는</b> 곳(2026-09-02 사용자 지시).
    ///
    /// 예전에는 모델의 공용 머티리얼에 벽지가 발려 있어서, <b>동을 하나 늘리면 새 동의 1층도
    /// 같은 벽지</b>였다. 이제 생김새는 <see cref="ApartmentRoomDecor"/> 가 방 번호별로 들고 있고,
    /// 여기가 동마다 그 값대로 칠한다 - 같은 모델을 복제해 써도 방끼리 안 섞인다.
    ///
    /// <b>동이 늘거나 꾸미기가 바뀔 때만</b> 칠한다. 매 프레임 할 일이 아니다.
    /// </summary>
    public class ApartmentRoomInteriors : MonoBehaviour
    {
        [Serializable]
        public class Seed
        {
            [Tooltip("방 번호(동 x 층수 + 층). 1동 1층이 0.")]
            public int room;

            [Tooltip("처음에 발라져 있을 인테리어 번호. 0은 안 꾸민 상태다.")]
            public int interior;
        }

        [SerializeField] private ApartmentBuildings buildings;
        [SerializeField] private ApartmentRooms rooms;

        [Tooltip("바를 수 있는 인테리어 목록. 미니게임 화면과 <b>같은 것</b>을 물려야 한다.")]
        [SerializeField] private RoomInteriorLibrary library;

        [Tooltip("처음부터 꾸며져 있는 방. 꾸미기 기능이 붙기 전까지의 자리다 - " +
                 "<b>이미 꾸민 방은 덮어쓰지 않는다.</b>")]
        [SerializeField] private Seed[] startingDecor = new Seed[0];

        private void Awake()
        {
            if (startingDecor != null)
            {
                for (int i = 0; i < startingDecor.Length; i++)
                {
                    var seed = startingDecor[i];
                    if (seed != null)
                        ApartmentRoomDecor.SeedIfUnset(seed.room, seed.interior);
                }
            }
        }

        private void OnEnable()
        {
            if (buildings != null)
                buildings.OnBuildingsChanged += Paint;

            ApartmentRoomDecor.OnChanged += PaintRoom;
            Paint();
        }

        /// <summary>
        /// 한 번 더 칠한다. <see cref="ApartmentBuildings"/> 가 자기 Awake 에서 동 목록을 채우는데,
        /// 그보다 먼저 OnEnable 이 불릴 수도 있어 그때는 칠할 동이 없다.
        /// 이미 맞게 칠해져 있으면 아무 일도 안 일어난다.
        /// </summary>
        private void Start() => Paint();

        private void OnDisable()
        {
            if (buildings != null)
                buildings.OnBuildingsChanged -= Paint;

            ApartmentRoomDecor.OnChanged -= PaintRoom;
        }

        private void PaintRoom(int roomIndex) => Paint();

        // 모델의 <b>원래</b> 머티리얼 자리 이름. 처음 칠하기 직전에 한 번만 뜬다 -
        // 동을 늘리는 건 이미 칠해진 1동을 복제하는 것이라, 복제본에서 읽으면 층이 어긋난다.
        private string[][] slots;

        /// <summary>동 전부를 자기 방 값대로 칠한다.</summary>
        public void Paint()
        {
            if (buildings == null || rooms == null || library == null)
                return;

            if (slots == null)
            {
                slots = RoomInteriorPainter.CaptureSlots(buildings.Get(0));
                if (slots == null)
                    return;
            }

            for (int b = 0; b < buildings.Count; b++)
            {
                var building = buildings.Get(b);
                if (building == null)
                    continue;

                int index = b;   // 람다가 늦게 읽어 마지막 동만 칠하는 걸 막는다
                RoomInteriorPainter.Paint(building, slots, library,
                    floor => ApartmentRoomDecor.Get(rooms.ToRoomIndex(index, floor)));
            }
        }
    }
}

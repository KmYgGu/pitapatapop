using UnityEngine;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 아파트 모델의 <b>방 머티리얼 자리</b>를 찾아 인테리어를 갈아 끼우는 곳.
    /// 아파트 화면과 미니게임 화면이 같이 쓴다.
    ///
    /// <b>⚠ 모델은 메시가 하나고 방은 머티리얼 이름으로만 갈라져 있다</b>
    /// (<see cref="ApartmentRoomSelector"/> 와 같은 사정). 이름은 <c>ROOM_1_WALL</c> 꼴이고,
    /// 앞 숫자가 층(1층부터), 뒷말이 천장·벽·바닥이다. <c>ROOM_n_UI</c> 와 <c>STRUCTURE_*</c> 는
    /// 방의 생김새가 아니므로 건드리지 않는다.
    ///
    /// ⭐⭐ <b>원래 자리 이름표를 미리 떠 두고, 동마다 그걸 같이 쓴다.</b>
    /// 한 번 칠하고 나면 그 자리에는 인테리어 머티리얼 이름이 들어가서 몇 층 무엇이었는지
    /// 알아볼 수가 없다. 게다가 <b>동을 늘리는 건 이미 칠해진 1동을 복제하는 것</b>이라
    /// (<see cref="ApartmentBuildings"/>), 복제본에서 이름을 읽으면 층이 어긋난다.
    /// 모델이 하나뿐이니 이름표도 하나면 된다.
    /// </summary>
    public static class RoomInteriorPainter
    {
        /// <summary>
        /// 그 동의 <b>지금</b> 머티리얼 이름들을 렌더러 순서대로 떠 둔다.
        /// <b>아직 아무것도 안 칠했을 때</b> 불러야 한다.
        /// </summary>
        public static string[][] CaptureSlots(Transform building)
        {
            if (building == null)
                return null;

            var renderers = building.GetComponentsInChildren<Renderer>(includeInactive: true);
            var slots = new string[renderers.Length][];

            for (int r = 0; r < renderers.Length; r++)
            {
                var materials = renderers[r].sharedMaterials;
                var names = new string[materials.Length];

                for (int i = 0; i < materials.Length; i++)
                    names[i] = materials[i] != null ? materials[i].name : string.Empty;

                slots[r] = names;
            }

            return slots;
        }

        /// <summary>
        /// 한 동을 칠한다.
        /// </summary>
        /// <param name="slots"><see cref="CaptureSlots"/> 로 떠 둔 원래 이름표.</param>
        /// <param name="interiorOfFloor">
        /// 층(0 = 1층)을 주면 인테리어 번호를 돌려주는 것. <b>-1 을 돌려주면 그 층은 그대로 둔다.</b>
        /// </param>
        public static void Paint(Transform building, string[][] slots,
                                 RoomInteriorLibrary library,
                                 System.Func<int, int> interiorOfFloor)
        {
            if (building == null || slots == null || library == null || interiorOfFloor == null)
                return;

            var renderers = building.GetComponentsInChildren<Renderer>(includeInactive: true);

            // 이름표는 <b>같은 모델</b>에서 뜬 것이라야 한다. 개수가 다르면 딴 모델이다.
            if (renderers.Length != slots.Length)
                return;

            for (int r = 0; r < renderers.Length; r++)
                PaintRenderer(renderers[r], slots[r], library, interiorOfFloor);
        }

        /// <summary>
        /// 한 층만 칠한다. 미니게임 방처럼 <b>화면에 한 층만 나오는</b> 곳이 쓴다.
        /// </summary>
        public static void PaintFloor(Transform building, string[][] slots,
                                      RoomInteriorLibrary library, int floor, int interior)
        {
            Paint(building, slots, library, f => f == floor ? interior : -1);
        }

        private static void PaintRenderer(Renderer renderer, string[] slots,
                                          RoomInteriorLibrary library,
                                          System.Func<int, int> interiorOfFloor)
        {
            if (renderer == null || slots == null)
                return;

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length != slots.Length)
                return;

            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!TryParseSlot(slots[i], out int floor, out int surface))
                    continue;

                int index = interiorOfFloor(floor);
                if (index < 0)
                    continue;

                var interior = library.Get(index);
                var material = interior != null ? interior.Surface(surface) : null;

                if (material == null || materials[i] == material)
                    continue;

                materials[i] = material;
                changed = true;
            }

            // 바뀐 게 없으면 넣지 않는다 - 렌더러에 배열을 꽂는 것만으로도 일이 생긴다.
            if (changed)
                renderer.sharedMaterials = materials;
        }

        /// <summary>
        /// <c>ROOM_1_WALL</c> 을 (층 0, 벽) 으로. 방의 생김새가 아닌 자리면 false.
        /// 복제된 머티리얼은 이름 뒤에 " (Instance)" 가 붙으므로 <b>앞에서부터</b> 읽는다.
        /// </summary>
        private static bool TryParseSlot(string name, out int floor, out int surface)
        {
            floor = 0;
            surface = -1;

            if (string.IsNullOrEmpty(name) || !name.StartsWith("ROOM_"))
                return false;

            int underscore = name.IndexOf('_', 5);
            if (underscore < 0)
                return false;

            if (!int.TryParse(name.Substring(5, underscore - 5), out int floorNumber))
                return false;

            // 모델은 1층부터 ROOM_1 이다. 방 번호 쪽은 0부터라 여기서 맞춰 준다.
            floor = floorNumber - 1;
            if (floor < 0)
                return false;

            string tail = name.Substring(underscore + 1);
            if (tail.StartsWith("CEILING")) surface = 0;
            else if (tail.StartsWith("WALL")) surface = 1;
            else if (tail.StartsWith("FLOOR")) surface = 2;
            else return false;   // ROOM_n_UI 등 - 방의 생김새가 아니다

            return true;
        }
    }
}

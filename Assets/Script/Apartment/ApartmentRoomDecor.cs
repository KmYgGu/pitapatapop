using System.Collections.Generic;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// <b>방마다</b> 어떤 인테리어를 발랐는지. <see cref="RoomInteriorLibrary"/> 의 번호를 들고 있다.
    ///
    /// ⭐ <b>방마다 따로다. 공유되는 건 없다</b>(2026-09-02 사용자 지시).
    /// 예전에는 모델의 공용 머티리얼에 벽지를 발라 뒀던 탓에, <b>동을 하나 늘리면 새 동의 1층까지
    /// 같은 벽지</b>가 됐다. 이제 생김새는 여기 적힌 방 번호별 값이 정하고, 모델이 들고 있는
    /// 머티리얼은 "아직 안 칠한 상태"일 뿐이다.
    ///
    /// <b>static 이라 씬을 옮겨다녀도 남는다</b> - 미니게임 방이 그 캐릭터의 방 인테리어를
    /// 따라가야 하므로(<see cref="ApartmentResidents"/> 와 같은 수명).
    ///
    /// <b>⚠ 파일로 저장하지는 않는다</b> - 동 수(<see cref="ApartmentBuildings"/>)와 같은 방침이라,
    /// 앱을 껐다 켜면 처음 상태로 돌아간다. 꾸미기 기능이 붙을 때 같이 저장하면 된다.
    /// </summary>
    public static class ApartmentRoomDecor
    {
        /// <summary>아직 안 꾸민 방. 목록의 0번이 이것이어야 한다.</summary>
        public const int Plain = 0;

        private static readonly Dictionary<int, int> chosen = new Dictionary<int, int>();

        /// <summary>어느 방의 인테리어가 바뀌었다(인자 = 방 번호).</summary>
        public static event System.Action<int> OnChanged;

        /// <summary>적어둔 게 없으면 <see cref="Plain"/> - 새로 지은 방은 안 꾸민 상태다.</summary>
        public static int Get(int roomIndex)
            => chosen.TryGetValue(roomIndex, out int interior) ? interior : Plain;

        public static void Set(int roomIndex, int interior)
        {
            if (roomIndex < 0)
                return;

            if (Get(roomIndex) == interior)
                return;

            if (interior == Plain)
                chosen.Remove(roomIndex);
            else
                chosen[roomIndex] = interior;

            OnChanged?.Invoke(roomIndex);
        }

        /// <summary>
        /// 아직 아무것도 안 정해진 방에만 적어 넣는다. 씬에 적어둔 <b>처음 상태</b>가 쓴다 -
        /// 이미 플레이어가 꾸민 방을 씬을 다시 열었다고 덮어쓰면 안 된다.
        /// </summary>
        public static void SeedIfUnset(int roomIndex, int interior)
        {
            if (roomIndex < 0 || interior == Plain || chosen.ContainsKey(roomIndex))
                return;

            chosen[roomIndex] = interior;
        }
    }
}

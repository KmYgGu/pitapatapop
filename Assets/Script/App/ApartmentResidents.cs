using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// <b>어느 방에 누가 사는지.</b> 아파트 방을 눌러 캐릭터를 들여보내면 여기 적힌다
    /// (2026-08-28 사용자 기획).
    ///
    /// 규칙 셋(사용자 확정):
    ///  - <b>한 방에 한 명.</b>
    ///  - <b>한 캐릭터는 한 방에만.</b> 다른 방에 살던 캐릭터를 고르면 <b>이사</b>다 - 원래 방이 빈다.
    ///  - <b>방을 비우는 길은 없다.</b> 한 번 들어오면 교체만 된다.
    ///
    /// <b>저장되지 않는다</b>(<see cref="PlayerInventory"/>·<see cref="Mailbox"/> 와 같은 방침).
    /// 세이브가 생기면 여기에 값을 채워주면 되고 화면 코드는 안 고쳐도 된다.
    /// </summary>
    public static class ApartmentResidents
    {
        // 방 번호 -> 사는 캐릭터. <b>비어 있는 방은 아예 항목이 없다</b> - null 을 넣어두면
        // "비었다"와 "모르는 방"이 같은 답을 주게 된다.
        private static readonly Dictionary<int, PanelType> byRoom = new Dictionary<int, PanelType>();

        /// <summary>한 명이라도 살고 있는지. 첫 배치를 이미 했는지 판단할 때 쓴다.</summary>
        public static bool HasAny => byRoom.Count > 0;

        /// <summary>그 방에 사는 캐릭터. 비었으면 null.</summary>
        public static PanelType Get(int roomIndex)
            => byRoom.TryGetValue(roomIndex, out var character) ? character : null;

        /// <summary>그 캐릭터가 사는 방 번호. 아무 데도 안 살면 -1.</summary>
        public static int FindRoomOf(PanelType character)
        {
            if (character == null)
                return -1;

            foreach (var pair in byRoom)
            {
                if (pair.Value == character)
                    return pair.Key;
            }

            return -1;
        }

        /// <summary>그 캐릭터가 어딘가에 살고 있는지.</summary>
        public static bool IsHoused(PanelType character) => FindRoomOf(character) >= 0;

        /// <summary>
        /// 그 방에 들여보낸다. <b>다른 방에 살고 있었으면 거기서 빠진다</b>(이사).
        ///
        /// 원래 방을 먼저 비우는 순서가 중요하다 - 나중에 비우면, 같은 방에 다시 넣는 경우
        /// (아무것도 안 바뀌는 경우)에 방금 넣은 것을 도로 지워버린다.
        /// </summary>
        public static void MoveIn(int roomIndex, PanelType character)
        {
            if (roomIndex < 0 || character == null)
                return;

            int previous = FindRoomOf(character);
            if (previous >= 0)
                byRoom.Remove(previous);

            byRoom[roomIndex] = character;

            // <b>처음 들어온 시각만</b> 적힌다 - 방을 옮겨도 "이 아파트에 산 기간"은 이어져야 한다.
            ResidentState.NoteMovedIn(character, System.DateTime.UtcNow);
        }

        /// <summary>
        /// 두 방의 주인을 <b>맞바꾼다</b>(2026-08-28 드래그 이사).
        /// 한쪽이 비어 있으면 그냥 옮겨가고 원래 방이 빈다.
        ///
        /// <see cref="MoveIn"/> 로는 안 된다 - 그건 "한 캐릭터는 한 방에만"을 지키려고
        /// 상대를 <b>내쫓아 버린다</b>. 교체는 둘 다 살아남아야 한다.
        /// </summary>
        public static void Swap(int roomA, int roomB)
        {
            if (roomA < 0 || roomB < 0 || roomA == roomB)
                return;

            var a = Get(roomA);
            var b = Get(roomB);

            if (b != null) byRoom[roomA] = b; else byRoom.Remove(roomA);
            if (a != null) byRoom[roomB] = a; else byRoom.Remove(roomB);
        }

        /// <summary>
        /// 그 방을 <b>빈 방으로</b> 되돌린다(2026-08-28 사용자 추가:
        /// "실수로 좋아하지 않은 캐릭터를 넣었는데 교체할 다른 캐릭터가 없을 수도 있다").
        /// </summary>
        public static void Vacate(int roomIndex) => byRoom.Remove(roomIndex);

        /// <summary>전부 비운다. 세이브를 불러오기 전에 초기화하는 자리.</summary>
        public static void Clear() => byRoom.Clear();
    }
}

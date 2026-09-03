using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 미니게임 씬에 <b>누구와 어느 방에서</b> 하는지를 건네주는 자리(2026-09-02).
    ///
    /// <b>왜 static 인가</b>: 씬을 넘어가면 오브젝트는 전부 사라지므로 인스펙터로는 못 넘긴다.
    /// <see cref="StageEntry"/> · <see cref="PartySelection"/> 과 같은 방식이다 -
    /// 씬 전환 직전에 여기 적고, 새 씬의 컨트롤러가 <see cref="Awake"/> 에서 읽는다.
    ///
    /// <b>돌아갈 방 번호를 같이 들고 간다</b> - 미니게임이 끝나면 아파트로 돌아가는데,
    /// 그때 <b>그 방이 다시 열려 있어야</b> "방에서 잠깐 놀다 온" 흐름이 된다.
    /// 아파트 씬은 <see cref="ScreenRequest"/> 로 그 요청을 받는다.
    /// </summary>
    public static class MiniGameEntry
    {
        /// <summary>같이 놀 캐릭터. 비어 있으면 미니게임 씬이 아무것도 못 한다.</summary>
        public static PanelType Character { get; private set; }

        /// <summary>돌아갈 방 번호(전역 번호). 음수면 그냥 아파트 메인으로 돌아간다.</summary>
        public static int RoomIndex { get; private set; } = -1;

        /// <summary>미니게임에 들어가기 직전에 부른다.</summary>
        public static void Set(PanelType character, int roomIndex)
        {
            Character = character;
            RoomIndex = roomIndex;
        }

        /// <summary>들고 있던 걸 비운다. 아파트로 돌아온 뒤에 부르면 된다.</summary>
        public static void Clear()
        {
            Character = null;
            RoomIndex = -1;
        }
    }
}

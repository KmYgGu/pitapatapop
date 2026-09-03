using JojoPuzzle.App;
using JojoPuzzle.Apartment;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 미니게임 방을 <b>그 캐릭터가 사는 방의 인테리어</b>로 칠한다(2026-09-02 사용자 지시).
    ///
    /// 예전에는 모델이 들고 있는 머티리얼을 그대로 썼던 탓에 <b>누구와 놀든 1층 인테리어</b>였다.
    /// 이제 <see cref="ApartmentResidents"/> 에게 그 캐릭터가 어느 방에 사는지 묻고,
    /// <see cref="ApartmentRoomDecor"/> 에 적힌 그 방의 인테리어를 가져와 바른다.
    ///
    /// <b>화면에 나오는 층만 칠한다</b> - 미니게임 카메라는 한 층만 담으므로
    /// (<see cref="MiniGameStage"/> 의 floorBottomRatio/floorTopRatio), 나머지 층은 건드릴 이유가 없다.
    ///
    /// ⚠ <b>테이블은 여기서 안 건드린다</b>(사용자: "테이블은 일단 제외").
    /// </summary>
    public class MiniGameRoomInterior : MonoBehaviour
    {
        [Tooltip("방 모델의 뿌리. MiniGameStage 가 재는 것과 같은 것을 물린다.")]
        [SerializeField] private Transform room;

        [Tooltip("인테리어 목록. 아파트 화면과 <b>같은 것</b>을 물려야 방이 화면마다 달라 보이지 않는다.")]
        [SerializeField] private RoomInteriorLibrary library;

        [Tooltip("미니게임 카메라가 담는 층. MiniGameStage 의 층과 같아야 한다(0 = 1층).")]
        [Min(0)]
        [SerializeField] private int shownFloor;

        /// <summary>
        /// <b>MiniGameStage 보다 먼저 칠한다.</b> 무대는 크기를 재서 카메라를 맞추는데,
        /// 머티리얼만 바꾸는 이 일은 크기에 영향이 없으므로 순서는 사실 상관없다 -
        /// 그래도 Awake 에서 끝내 두면 첫 프레임부터 제 모습이다.
        /// </summary>
        private void Awake() => Paint();

        // 모델의 원래 머티리얼 자리 이름. 칠하기 전에 한 번 떠 둔다.
        private string[][] slots;

        public void Paint()
        {
            if (room == null || library == null)
                return;

            if (slots == null)
            {
                slots = RoomInteriorPainter.CaptureSlots(room);
                if (slots == null)
                    return;
            }

            RoomInteriorPainter.PaintFloor(room, slots, library, shownFloor, InteriorOfCharacter());
        }

        /// <summary>
        /// 지금 상대하는 캐릭터가 사는 방의 인테리어. 사는 방을 못 찾으면 안 꾸민 방으로 둔다 -
        /// 아무 방이나 골라 칠하면 "저 방이 저 사람 방인가?" 하고 헷갈린다.
        /// </summary>
        private int InteriorOfCharacter()
        {
            var character = MiniGameEntry.Character;
            if (character == null)
                return ApartmentRoomDecor.Plain;

            int lives = ApartmentResidents.FindRoomOf(character);
            return lives >= 0 ? ApartmentRoomDecor.Get(lives) : ApartmentRoomDecor.Plain;
        }
    }
}

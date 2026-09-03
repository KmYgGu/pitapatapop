using System.Collections;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 미니게임 씬 한 번의 <b>바깥 틀</b>(2026-09-02 사용자 기획으로 갈라져 나왔다).
    ///
    /// <code>
    ///   들어옴 → 캐릭터를 세우고 인사 → <b>무엇을 하고 놀지 고른다</b>
    ///   → 고른 게임을 돌린다 → 그만하면 다시 고르는 화면 → 나가면 방으로
    /// </code>
    ///
    /// <b>왜 게임과 나눴나</b>: 예전에는 <see cref="MiniGameFlow"/> 가 캐릭터 세우기·인사·나가기까지
    /// 다 했다. 도박이 여러 개가 되면 그 셋은 <b>게임이 무엇이든 똑같이</b> 필요하므로,
    /// 게임은 자기 판만 책임지게 두고 공통 부분을 여기로 뺐다.
    ///
    /// <b>왼쪽 위 '나가기'는 한 칸씩 물러난다</b> - 게임 중이면 고르는 화면으로, 고르는 화면이면
    /// 방으로. 버튼을 둘로 나누지 않은 건 화면이 좁아서다.
    /// </summary>
    public class MiniGameSession : MonoBehaviour
    {
        [Header("이어붙일 것들")]
        [SerializeField] private MiniGameMenu menu;

        [Tooltip("인디언 포커. 다른 도박이 생기면 여기 옆에 하나씩 붙인다.")]
        [SerializeField] private MiniGameFlow poker;

        [SerializeField] private PokerTableUI table;

        [Tooltip("블랙잭.")]
        [SerializeField] private BlackjackFlow blackjack;

        [SerializeField] private BlackjackTableUI blackjackTable;

        [Tooltip("도둑잡기.")]
        [SerializeField] private OldMaidFlow oldMaid;

        [SerializeField] private OldMaidTableUI oldMaidTable;

        [Tooltip("대사창. 없으면 대사 없이 굴러간다.")]
        [SerializeField] private SpeechDirector speech;

        [Tooltip("테이블 건너편에 선 캐릭터.")]
        [SerializeField] private MiniGameCharacterStand characterStand;

        [Header("표시")]
        [Tooltip("고르는 화면의 머리말. {0} 자리에 캐릭터 이름이 들어간다.")]
        [SerializeField] private string menuTitleFormat = "{0} 와(과) 무엇을 하고 놀까?";

        private PanelType character;
        private bool leaving;

        private void Awake()
        {
            character = MiniGameEntry.Character;

            if (menu != null)
                menu.OnPicked += HandlePicked;

            if (table != null)
                table.OnLeaveRequested += HandleBack;

            if (poker != null)
            {
                poker.OnQuitRequested += HandleBack;
                poker.OnRoomExitRequested += LeaveToRoom;
            }

            if (blackjack != null)
            {
                blackjack.OnQuitRequested += HandleBack;
                blackjack.OnRoomExitRequested += LeaveToRoom;
            }

            if (oldMaid != null)
            {
                oldMaid.OnQuitRequested += HandleBack;
                oldMaid.OnRoomExitRequested += LeaveToRoom;
            }
        }

        private void OnDestroy()
        {
            if (menu != null)
                menu.OnPicked -= HandlePicked;

            if (table != null)
                table.OnLeaveRequested -= HandleBack;

            if (poker != null)
            {
                poker.OnQuitRequested -= HandleBack;
                poker.OnRoomExitRequested -= LeaveToRoom;
            }

            if (blackjack != null)
            {
                blackjack.OnQuitRequested -= HandleBack;
                blackjack.OnRoomExitRequested -= LeaveToRoom;
            }

            if (oldMaid != null)
            {
                oldMaid.OnQuitRequested -= HandleBack;
                oldMaid.OnRoomExitRequested -= LeaveToRoom;
            }
        }

        private IEnumerator Start()
        {
            if (character == null)
            {
                // 누구와 하는지 모르면 할 게 없다. 들어온 길이 잘못된 것이므로 돌려보낸다.
                Debug.LogWarning("[MiniGameSession] 상대 캐릭터가 없습니다 - 아파트로 돌아갑니다.");
                LeaveToRoom();
                yield break;
            }

            characterStand?.Bind(character);
            table?.BindCharacter(character);
            blackjackTable?.BindCharacter(character);
            oldMaidTable?.BindCharacter(character);
            HideGames();

            // 인사를 먼저 하고 목록을 연다 - 들어오자마자 고르라고 들이미는 것보다
            // 한마디 듣고 고르는 편이 자연스럽다(대사가 없는 캐릭터면 그냥 곧바로 열린다).
            yield return Speak(SpeechTrigger.MiniGameStart);

            OpenMenu();
        }

        private void OpenMenu()
        {
            HideGames();

            string name = character != null ? character.DisplayName : string.Empty;
            menu?.Open(string.Format(menuTitleFormat, name));
        }

        private void HandlePicked(MiniGameKind kind)
        {
            menu?.Close();

            switch (kind)
            {
                case MiniGameKind.IndianPoker:
                    table?.SetVisible(true);
                    poker?.Begin();
                    break;

                case MiniGameKind.Blackjack:
                    blackjackTable?.SetVisible(true);
                    blackjack?.Begin();
                    break;

                case MiniGameKind.OldMaid:
                    oldMaidTable?.SetVisible(true);
                    oldMaid?.Begin();
                    break;

                default:
                    // 아직 안 만든 게임. 목록에서 눌리지 않지만, 눌렸다면 조용히 되돌아간다.
                    OpenMenu();
                    break;
            }
        }

        /// <summary>왼쪽 위 '나가기' - 한 칸씩 물러난다.</summary>
        private void HandleBack()
        {
            if (leaving)
                return;

            if (menu != null && menu.IsOpen)
            {
                LeaveToRoom();
                return;
            }

            OpenMenu();
        }

        private void LeaveToRoom()
        {
            if (leaving)
                return;

            leaving = true;
            StartCoroutine(LeaveRoutine());
        }

        private IEnumerator LeaveRoutine()
        {
            // 판을 먼저 접고 인사한다 - 게임 화면을 띄운 채 나가면 끝난 느낌이 안 든다.
            menu?.Close();
            HideGames();

            yield return Speak(SpeechTrigger.MiniGameEnd);

            // 놀던 방이 다시 열린 채로 아파트에 도착한다.
            ScreenRequest.OpenRoomIndex = MiniGameEntry.RoomIndex;
            MiniGameEntry.Clear();
            AppScenes.GoToApartment();
        }

        /// <summary>돌던 게임을 전부 접고 화면에서 내린다.</summary>
        private void HideGames()
        {
            table?.SetVisible(false);
            poker?.Stop();

            blackjackTable?.SetVisible(false);
            blackjack?.Stop();

            oldMaidTable?.SetVisible(false);
            oldMaid?.Stop();
        }

        private IEnumerator Speak(SpeechTrigger trigger)
        {
            if (speech == null || character == null)
                yield break;

            yield return speech.Play(character, trigger, SpeechSide.Enemy);
        }
    }
}

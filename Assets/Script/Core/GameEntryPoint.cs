using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.Core;
using JojoPuzzle.Board;
using JojoPuzzle.Battle;
using JojoPuzzle.App;
using JojoPuzzle.UI;

namespace JojoPuzzle.View
{
    /// <summary>
    /// 씬에 빈 GameObject 하나 만들어서 이 컴포넌트만 붙이면 됨.
    /// 실행 순서: 팔레트 결정(BattleSetup) → 보드 생성(BoardGenerator) → BoardManager 생성
    ///           → BoardView.Initialize → BoardInputController.Initialize
    /// </summary>
    public class GameEntryPoint : MonoBehaviour
    {
        [Header("보드 크기")]
        [SerializeField] private int boardWidth = 6;
        [SerializeField] private int boardHeight = 6;

        [Header("편성 파티 (정확히 2개)")]
        [SerializeField] private List<PanelType> partyPanels;

        [Header("씬 참조")]
        [SerializeField] private BoardView boardView;
        [SerializeField] private BoardInputController inputController;
        [SerializeField] private CameraFitter cameraFitter;
        [SerializeField] private BattleManager battleManager; // 선택 - 없으면 제한시간·승패 없이 보드만 돌아감

        [Tooltip("시작 연출. 비워두면 연출 없이 곧바로 시작한다(배틀 씬을 직접 열어 테스트할 때).")]
        [SerializeField] private BattleIntroSequence introSequence;

        [Header("보유 캐릭터 목록")]
        [SerializeField] private CharacterRoster ownedRoster;

        [Header("스티커")]
        [Tooltip("붙여 둔 스티커가 전투에 얼마나 보태는지 읽을 목록. " +
                 "⭐ 스티커는 <b>전투 데이터</b>지 UI 데이터가 아니라서 여기가 든다 - " +
                 "스티커북 화면이 대신 물려주게 하면, 그 화면을 안 거치고 들어온 판에서 조용히 안 듣는다. " +
                 "비워두면 스티커 효과가 전부 0이 된다(배틀 씬을 직접 열어 테스트할 때).")]
        [SerializeField] private StickerCatalog stickerCatalog;

        [Tooltip("화면에 서 있는 초상화를 이번 판의 캐릭터로 갈아끼운다. " +
                 "비워두면 씬에 박아둔 스켈레톤이 그대로 선다(예전 동작).")]
        [SerializeField] private BattlePortraitBinder portraitBinder;

        // 판이 도는 동안 듣는 스티커들. Start 에서 만든다 - 팔레트가 서야 색을 읽을 수 있다.
        private StickerBattleEffects stickerEffects;

        /// <summary>
        /// 스티커의 시간이 <b>멈춰야 하는</b> 구간인지. 대사창·스킬 연출로 판이 멈춰 있는 동안
        /// 시계가 흐르면 플레이어가 아무것도 못 한 사이에 효과가 녹는다 - 다른 시계들과 같은 방침이다.
        /// </summary>
        private bool IsBoardPaused()
            => inputController == null
               || !inputController.IsPlayablePhase
               || inputController.IsPausedByMenu;

        private void Start()
        {
            // 스테이지 선택을 거쳐 왔으면 거기서 정한 편성을 쓴다. 편성 화면이 아직 없어서
            // 지금은 준비 화면의 임시 편성이 그대로 넘어오고, 아무것도 없으면 인스펙터 값으로
            // 물러선다(배틀 씬을 직접 열어 테스트하는 경우).
            var party = PartySelection.HasParty
                ? new List<PanelType> { PartySelection.Leader, PartySelection.Partner }
                : partyPanels;

            if (party == null || party.Count != 2)
            {
                Debug.LogError("[GameEntryPoint] 편성은 정확히 2개여야 합니다.");
                return;
            }

            // 스티커 목록을 전투 쪽에 물려준다. <b>팔레트를 짜기 전에</b> 해둔다 -
            // 리필 확률과 데미지가 판이 시작하자마자 이걸 읽는다.
            StickerEffects.Catalog = stickerCatalog;

            var rng = new System.Random();

            // 1. 팔레트 결정: 편성 2색 + 보유 캐릭터 중 랜덤 4색
            var ownedPool = ownedRoster != null ? ownedRoster.ownedCharacters : new List<PanelType>();
            var palette = BattleSetup.BuildPalette(party, ownedPool, rng);

            // 2. 초기 보드 생성 (매치 없는 상태로)
            var boardData = BoardGenerator.GenerateInitialBoard(boardWidth, boardHeight, palette.Count, rng);

            // 3. 로직 매니저 생성
            var boardManager = new BoardManager(boardData, palette.Count, rng);

            // 4. 뷰/입력 초기화
            boardView.Initialize(boardManager, palette);
            inputController.Initialize(boardManager, boardView);

            // 4-0. 붙여 둔 스티커 중 판이 도는 동안 듣는 것들을 굴린다.
            //      <b>팔레트가 선 뒤여야 한다</b> - 색마다의 리젠을 팔레트에서 읽는다.
            stickerEffects = new StickerBattleEffects(boardManager, boardView, palette.Count);
            stickerEffects.ApplyRefillWeights();
            StartCoroutine(stickerEffects.RunLeaderRegenBurstRoutine(IsBoardPaused));

            if (battleManager != null)
            {
                StartCoroutine(stickerEffects.RunSkillGaugeOverTimeRoutine(
                    IsBoardPaused, battleManager.ChargeAllSkillGauges));
            }

            // 4-1. 화면에 서 있는 초상화를 이번 판의 캐릭터로. <b>팔레트가 선 뒤, 시작 연출이
            //      초상화를 화면 밖에 세우기 전</b>이어야 한다 - 순서를 스스로 잡게 두면
            //      Start 끼리 순서가 보장되지 않아 팔레트가 없는 프레임에 걸린다.
            portraitBinder?.Apply();

            // 5. 화면 비율(기종)에 관계없이 보드 전체가 보이도록 카메라 자동 조정
            if (cameraFitter != null)
            {
                var size = boardView.GetBoardWorldSize();
                cameraFitter.FitToBoard(size.x, size.y, boardView.GetBoardWorldCenter());
            }

            // 6. 배틀 시작 - 반드시 마지막에. 여기서 제한시간 타이머가 돌기 시작하므로
            //    보드가 아직 만들어지는 중에 시간이 깎이면 안 된다.
            //
            //    <b>시작 연출이 물려 있으면 시계만 뒤로 미룬다</b>(2026-08-28). 판·적 체력·아이템
            //    효과는 지금 다 적용해두고, 연출이 끝나는 '시작!'에서 시계가 돈다. 연출이 없으면
            //    (배틀 씬을 직접 열어 테스트할 때) 예전 그대로 곧바로 시작한다.
            bool hasIntro = introSequence != null;

            // 캐릭터를 화면 밖에 세우는 건 <b>BeginBattle 보다 먼저</b>다 - 한 프레임이라도
            // 제자리에 서 있는 게 보이면 "튀었다 돌아오는" 것처럼 읽힌다.
            if (hasIntro)
                introSequence.PrepareOffscreen();

            battleManager?.BeginBattle(deferStart: hasIntro);

            if (hasIntro)
                StartCoroutine(introSequence.Play());
        }
    }
}
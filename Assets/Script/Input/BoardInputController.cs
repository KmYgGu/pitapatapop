using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.Board;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using JojoPuzzle.App;

namespace JojoPuzzle.View
{
    /// <summary>
    /// 드래그 이동형 입력 처리.
    /// 손가락(마우스)으로 패널(또는 박스) 하나를 눌러 집어들고, 뗄 때까지 자유롭게 이동시키다가
    /// 손을 뗀 위치(스냅된 셀)만 실제 데이터에 반영된다(중간 경로는 무시, 최종 위치만 "덮어쓰기").
    /// 박스를 같은 자리에서 두 번 연속 탭하면(첫 탭은 "이동"으로 해석될 수 있으므로) 십자 5칸 변환이 발동한다.
    ///
    /// 동시 진행 처리: 매치 하나가 판정/이펙트/낙하를 처리하는 동안에도 플레이어는 계속해서
    /// 다른(관련 없는) 칸을 조작할 수 있다. "지금 애니메이션 중이거나 드래그로 잡고 있는 칸"만
    /// 잠금에 등록해서 그 칸만 못 만지게 하고, 낙하 계산도 그 칸들을 구멍(Hole)처럼
    /// 고정된 자리로 취급해서 여러 매치가 동시에 진행돼도 서로 꼬이지 않게 한다.
    ///
    /// 스탠드업 타임 배너가 떠 있는 동안은 터치 입력만 막고, 낙하/매치 진행(GravityAndCascadeRoutine 등)은
    /// 그대로 계속 돈다 - StandUpTimeUI의 OnBannerShown/OnBannerHidden 이벤트로 연동.
    /// </summary>
    public class BoardInputController : MonoBehaviour, ICascadeHost, IMatchHost
    {
        [SerializeField] private BoardView boardView;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float doubleTapWindow = 0.5f; // 같은 박스를 두 번째 탭했다고 인정할 시간 제한(초)

        [Header("안착 대기 (미안착 = 판에 보이지만 아직 매치 대상이 아님)")]
        [Tooltip("박스가 십자로 펼쳐진 조각들이 매치 대상이 되기까지 기다릴 시간(초). " +
                 "플레이어가 박스를 썼다는 걸 인지할 시간이자, 그 위에 다음 수를 이어 붙일 여유다.")]
        [SerializeField] private float boxSettleDuration = 1f;

        [Tooltip("낙하로 새로 채워진 조각의 안착 대기 시간(초). 0이면 곧바로 매치 대상이 된다(기본). " +
                 "여기에 값을 주면 캐스케이드 전체 템포가 그만큼 느려지므로 신중히.")]
        [SerializeField] private float refillSettleDuration = 0f;

        [Tooltip("<b>스탠드업 타임 동안</b> 낙하·리필을 몇 배 빠르게 할지(2026-08-28 사용자 지시). " +
                 "채워지는 조각은 <b>다 떨어진 뒤에야</b> 매치 판정에 잡히는데, 10초뿐인 구간에서 " +
                 "그 대기가 쌓이면 '분명 맞는 색인데 안 터지고 끝나버린다'가 된다. " +
                 "인스펙터의 fallDuration 을 건드리지 않고 나누기만 하므로 끝나면 정확히 복원된다 " +
                 "(러시 타임의 FallSpeedMultiplier 와 같은 손잡이).")]
        [SerializeField] private float standUpFallSpeedMultiplier = 2f;

        [Tooltip("리필된 조각이 놓이자마자 매치가 성립하는 색은 피한다. 초기 판 생성은 원래부터 " +
                 "공짜 매치를 피하고 있었는데 리필만 순수 무작위라 비대칭이었다 - 그걸 맞춘 것. " +
                 "끄면 예전처럼 완전 무작위로 채운다(공짜 연쇄가 다시 생긴다).")]
        [SerializeField] private bool refillAvoidsImmediateMatch = true;

        [SerializeField] private StandUpTimeUI standUpTimeUI; // 선택 연결 - 없으면 입력 차단 기능 없이 그냥 동작
        [SerializeField] private ScreenDimOverlay screenDimOverlay; // 선택 연결 - 없으면 화면 어둡게 하는 연출 없이 그냥 동작

        private BoardManager boardManager;

        private bool isDragging;
        private int dragFromX, dragFromY;
        private PanelView draggedView;
        private (int x, int y)? dragHighlightCell; // 지금 테두리가 표시된 칸 - 매 프레임 새로 그리지 않고 바뀔 때만 갱신
        /// <summary>
        /// 일시정지 메뉴가 열려 있는 동안 true (PauseMenuUI가 설정). Time.timeScale=0으로
        /// 낙하/매치/타이머 같은 시간 기반 진행은 전부 멈추지만 Update()는 timeScale과 무관하게
        /// 계속 호출되므로, 터치로 퍼즐을 집어드는 것만은 이렇게 따로 막아야 한다.
        /// </summary>
        public bool IsPausedByMenu { get; set; }

        /// <summary>
        /// 지금 판이 어느 <b>단계</b>인지. 예전에 따로 놀던 네 bool(시작 연출 / 종료 처리 /
        /// 러시 타임 / 종료됨)을 하나로 모은 것이다 - 왜 그랬는지는 <see cref="BattlePhase"/> 참고.
        ///
        /// <b>여기에 가림막을 섞지 말 것.</b> 대사창·암전·일시정지·스탠드업 배너는 단계가 아니라
        /// 잠깐 겹쳤다 사라지는 것들이고, 서로 겹칠 수도 있어서 하나의 값으로 담을 수 없다.
        /// </summary>
        public BattlePhase Phase { get; private set; } = BattlePhase.Playing;

        /// <summary>
        /// 단계를 옮긴다. <b>단계에 딸린 규칙은 전부 여기서 한 번에 처리한다</b> -
        /// 부르는 쪽마다 곁들여 켜고 끄면 그중 하나를 빠뜨렸을 때 조용히 어긋난다
        /// (상자 만들기를 러시가 끝날 때 도로 켜서 쓸 수 없는 상자가 남던 게 그런 경우였다).
        /// </summary>
        public void EnterPhase(BattlePhase phase)
        {
            Phase = phase;

            // <b>상자는 평소에만 생긴다.</b> 종료 처리가 시작된 뒤에 생긴 상자는 쓸 기회가 없고,
            // 짧은 러시 구간에 쌓인 상자도 쓰지 못하고 끝난다(2026-08-25·28 사용자 기획).
            if (boardManager != null)
                boardManager.BoxCreationEnabled = phase == BattlePhase.Playing;
        }

        /// <summary>
        /// <b>플레이어가 판을 만질 수 있는 단계인가.</b> 가림막은 여기서 안 본다 - 그건 따로 겹친다.
        /// 러시 타임도 포함이다(보너스지만 실제로 조작하는 구간이다).
        /// </summary>
        public bool IsPlayablePhase => Phase == BattlePhase.Playing || Phase == BattlePhase.RushTime;

        /// <summary>
        /// 퍼즐판이 무언가에 덮여 있어서 조작하면 안 되는 상태(BoardDimOverlay가 켠다).
        /// 대사창이 떠 있는 동안처럼 "화면상 퍼즐판이 가려진" 경우를 위한 것으로, 가리는 쪽이
        /// 여러 개일 수 있어 그 합산은 BoardDimOverlay가 하고 여기엔 결과만 들어온다.
        ///
        /// 가려지는 순간 <b>지금 재생 중인 접기 연출을 전부 취소</b>한다(남은 조각은 그 자리에서
        /// 제거 연출과 함께 사라짐). 아직 시작 안 한 매치는 IsMatchResolveFrozen이 멈춰주지만, 이미
        /// 돌고 있는 연출은 그 대기 지점을 지나와서 안 잡히기 때문에 따로 끊어줘야 한다 -
        /// 스탠드업 종료 직전에 박스를 쓰면 불꽃이 모이는 위로 접기 연출이 겹쳐 보이던 게 이 경우다.
        /// </summary>
        public bool IsBoardCovered
        {
            get => isBoardCovered;
            set
            {
                if (isBoardCovered == value)
                    return;

                isBoardCovered = value;

                if (value)
                    boardView?.CancelAllCollectEffects();
            }
        }
        private bool isBoardCovered;

        /// <summary>
        /// 스탠드업 종료 시퀀스가 시작될 때 발행(불꽃이 날아가기 직전). 짝이 되는 종료 알림은
        /// OnStandUpTimeEnd다 - 그 사이가 곧 "조각이 불꽃이 되어 날아가고 캐릭터가 공격하는" 구간이라,
        /// 퍼즐판을 가려두는 연출(BoardDimOverlay)이 이 두 이벤트로 켜지고 꺼진다.
        /// </summary>
        public event System.Action OnStandUpEndSequenceStart;

        /// <summary>
        /// 스탠드업 종료 시퀀스(불꽃 날아감 → 정지 → 데미지 확정)가 재생 중인지.
        /// 이 구간은 몇 초씩 걸리고 그 끝에 큰 데미지가 확정되므로, BattleManager가 제한시간이
        /// 다 됐을 때 곧바로 패배로 처리하지 않고 이 연출이 끝날 때까지 기다리는 데 쓴다.
        /// </summary>
        public bool IsResolvingStandUpEnd => inputBlockedByStandUpEnd;

        /// <summary>
        /// 스탠드업 <b>한 판 전체</b>가 진행 중인지 - 게이지가 만충되어 배너가 뜨는 순간부터
        /// 종료 연출이 완전히 끝날 때(OnStandUpTimeEnd)까지 <b>한 번도 끊기지 않고</b> true다.
        ///
        /// <b>왜 따로 필요한가</b>: <see cref="IsStandUpTimeActive"/>는 10초가 끝나는 순간 꺼지는데
        /// 종료 연출은 그 뒤에 시작된다. 그 사이에 합체 연출(MatchResolver.MergesPlaying)이 있어서,
        /// 두 플래그를 OR로 묶어도 <b>둘 다 false인 구간이 최대 0.5초쯤 생긴다.</b> 화면에는 고정된
        /// 조각이 그대로 합쳐져 있는데 코드만 "스탠드업 아님"이라고 답하는 구간이다.
        /// 실제로 그 틈으로 적의 방해가 새어 나왔고(2026-08-21 사용자 신고), 제한시간이 하필
        /// 그때 다 되면 패배 유예도 새어 나간다.
        /// 스탠드업 중인지 물어야 할 곳은 두 플래그를 조합하지 말고 <b>이걸</b> 쓸 것.
        /// </summary>
        public bool IsStandUpEpisodeActive { get; private set; }

        /// <summary>
        /// 팔레트 색(=캐릭터)별로 <b>이번 판에 매치한 조각 수</b>. 인덱스가 곧 팔레트 인덱스다.
        /// 캐릭터 결과 화면이 이걸 읽어 "이 캐릭터를 얼마나 썼는가"를 보여준다.
        ///
        /// <b>배틀 상태를 안 본다</b>(BattleManager.TotalPiecesMatched 와 다른 점) - 러시 타임에
        /// 지운 조각도 그 캐릭터를 쓴 것이므로 여기서는 그대로 센다. 골드 쪽은 러시 몫을 따로
        /// 계산하느라 나눠 세지만 이건 "쓴 횟수"라 나눌 이유가 없다.
        /// </summary>
        public IReadOnlyList<int> PiecesMatchedByPanel => piecesByPanel;

        /// <summary>팔레트 인덱스 하나의 매치 조각 수. 범위 밖이면 0.</summary>
        public int GetPiecesMatched(int panelIndex)
            => panelIndex >= 0 && panelIndex < piecesByPanel.Count ? piecesByPanel[panelIndex] : 0;

        /// <summary>
        /// 새 판을 시작할 때 되돌린다. <see cref="Battle.BattleManager.BeginBattle"/> 이 부른다.
        ///
        /// 조각 수뿐 아니라 <b>종료 처리 상태까지</b> 되돌린다 - 같은 씬에서 다시 싸울 때
        /// 조작이 막힌 채로 시작하거나 마무리 처리를 건너뛰면 안 된다.
        /// </summary>
        public void ResetForNewBattle()
        {
            for (int i = 0; i < piecesByPanel.Count; i++)
                piecesByPanel[i] = 0;

            finisherRunning = false;
            finisherRan = false;

            // 단계를 되돌리면 상자 만들기도 같이 되살아난다(EnterPhase 가 함께 처리한다).
            EnterPhase(BattlePhase.Playing);
        }

        // 팔레트는 6칸이지만 색 인덱스가 그보다 클 수도 있어서 필요한 만큼 늘린다.
        private readonly List<int> piecesByPanel = new List<int>();

        private void AddPieceCount(int panelIndex, int count)
        {
            if (panelIndex < 0 || count <= 0)
                return;

            while (piecesByPanel.Count <= panelIndex)
                piecesByPanel.Add(0);

            piecesByPanel[panelIndex] += count;
        }

        /// <summary>
        /// <b>마무리 처리가 도는 중인지</b> - 그동안은 리필이 멈춘다. 남은 조각을 전부 데미지로
        /// 바꾸고 끝내는 구간이라 새 조각이 계속 채워지면 영원히 끝나지 않는다.
        ///
        /// <b>단계(<see cref="BattlePhase.Ending"/>)와 같지 않다</b> - 종료 처리에 들어간 뒤에도
        /// 마무리가 시작되기 <b>전까지</b>는 판을 가득 채워야 하기 때문이다(안내 띠가 흐르는 동안).
        /// 그래서 조작 차단(단계)과 리필 정지(이것)를 나눠 뒀다.
        ///
        /// <b>낙하는 이때도 그대로 둔다</b> - 상자가 터져 생긴 빈 칸 위로 조각이 안 내려오면
        /// 판이 공중에 뜬 채로 남는다.
        /// </summary>
        private bool finisherRunning;

        /// <summary>마무리 처리 중 리필을 건너뛸 때 돌려줄 빈 결과. 매번 새로 만들지 않는다.</summary>

        // 빛이 날아갈 목표 좌표. 매번 새 리스트를 만들지 않는다.
        private readonly List<Vector3> finisherLightTargets = new List<Vector3>();

        // 마무리 처리 전용 버퍼. 매치 스캔 버퍼와 나눠 쓴다 - 이 코루틴이 도는 동안에도
        // 다른 매치 처리가 같은 버퍼를 쓸 수 있다(이 프로젝트의 버퍼 분리 규칙).
        private readonly List<(int x, int y)> finisherBuffer = new List<(int x, int y)>();

        [Header("게임 종료 마무리")]
        [Tooltip("상자를 하나씩 터뜨리는 사이 간격(초). 한꺼번에 터뜨리면 무슨 일이 났는지 안 보인다.")]
        [SerializeField] private float finisherBoxInterval = 0.25f;

        [Tooltip("상자를 다 쓴 뒤 캐스케이드가 잦아들기를 기다리는 시간(초).")]
        [SerializeField] private float finisherSettleWait = 0.7f;

        [Tooltip("리더 차례와 파트너 차례 사이 간격(초).")]
        [SerializeField] private float finisherSkillInterval = 0.45f;

        [Tooltip("게이지가 가득 찼을 때의 데미지 배율.")]
        [SerializeField] private float finisherFullGaugeDamageMultiplier = 2f;

        [Tooltip("게이지가 빛이 되어 자기 조각으로 날아가는 연출. 비워두면 조각이 그냥 사라진다.")]
        [SerializeField] private SkillLightEffect skillLight;

        [Tooltip("빛이 출발할 자리(스킬 게이지). 0=리더, 1=파트너 순서로 넣을 것. " +
                 "비워두면 리더 초상화 자리에서 출발한다.")]
        [SerializeField] private RectTransform[] skillGaugeAnchors = new RectTransform[0];

        /// <summary>
        /// 게임이 끝났을 때의 마무리 처리(2026-08-25 사용자 기획). 타임오버든 적 체력 0이든
        /// <b>똑같이</b> 지나간다 - 남은 자원을 전부 데미지로 바꾸고 끝내는 구간이라,
        /// <b>타임오버로 들어와도 여기서 적을 눕혀 승리가 될 수 있다.</b>
        ///
        /// <code>
        ///   1. 조작 차단 + 리필 정지
        ///   2. 판에 남은 상자를 전부 강제로 사용하고 그 매칭까지 처리
        ///   3. 리더·파트너의 스킬 게이지를 소진해 <b>자기 색 조각</b>을 지우고 데미지
        /// </code>
        ///
        /// <b>승패를 여기서 판정하지 않는다.</b> 이 코루틴이 끝난 뒤의 적 체력을 보고
        /// <see cref="Battle.BattleManager"/> 가 정한다 - 판정이 두 군데 있으면 어긋난다.
        /// </summary>
        /// <param name="leaderGauge">리더의 남은 스킬 게이지(0~1).</param>
        /// <param name="partnerGauge">파트너의 남은 스킬 게이지(0~1).</param>
        public IEnumerator RunFinisher(float leaderGauge, float partnerGauge)
        {
            if (finisherRan)
                yield break;

            finisherRan = true;

            // 이미 막혀 있으면 아무 일도 안 한다 - 보통은 타임오버 띠가 뜨기 전에 이미 막혔다.
            BeginEndSequence();

            // <b>여기서부터 리필을 멈춘다</b>(조작 차단과 나눠져 있다) - 이 앞에서 RefillBoard 가
            // 판을 가득 채워주고, 그 조각들을 여기서 전부 데미지로 바꾼다. 더 채워지면 안 끝난다.
            finisherRunning = true;

            yield return StartCoroutine(FinisherUseAllBoxes());
            yield return StartCoroutine(FinisherSkillBurst(0, leaderGauge));

            if (finisherSkillInterval > 0f)
                yield return new WaitForSeconds(finisherSkillInterval);

            yield return StartCoroutine(FinisherSkillBurst(1, partnerGauge));

            // <b>단계를 여기서 되돌리지 않는다.</b> 마무리가 끝나도 조작은 계속 막혀 있어야 하고
            // 리필도 멈춘 채여야 한다 - 러시에 들어갈 때 SetRushTime 이 풀어주고, 러시가 없으면
            // 곧바로 Finished 로 넘어간다.
        }

        /// <summary>
        /// <b>게임 종료 처리가 시작됐다</b> - 지금부터 퍼즐을 만질 수 없고 리필도 멈춘다.
        ///
        /// <see cref="RunFinisher"/> 보다 <b>먼저</b> 불러야 한다. 타임오버 띠처럼 마무리 앞에
        /// 끼어드는 연출이 있는데, 거기서 막지 않으면 <b>띠가 떠 있는 동안에도 조각을 옮길 수
        /// 있다</b>(2026-08-25 실제로 그랬다). 여러 번 불러도 안전하다.
        /// </summary>
        public void BeginEndSequence()
        {
            if (Phase == BattlePhase.Ending)
                return;

            // 상자 만들기도 여기서 같이 꺼진다(EnterPhase 가 처리한다) - 종료 처리에 들어간
            // 뒤에 생긴 상자는 쓸 기회가 없다.
            EnterPhase(BattlePhase.Ending);

            // 들고 있던 조각이 있으면 지금 자리에 놓는다 - 이 뒤로는 손에 붙어 있으면 안 된다.
            // (조작 차단 구간에서 이미 쓰는 것과 같은 방식)
            if (isDragging)
                EndDrag(Input.mousePosition);
        }

        // 마무리 처리를 이미 돌렸는지. Ending 단계는 그보다 먼저 켜지므로 재진입 판정에 못 쓴다.
        private bool finisherRan;

        /// <summary>
        /// <b>판을 가득 채운다</b>(2026-08-28 사용자 지시). 종료 안내(타임오버 띠·러시 개시 띠)가
        /// 흐르는 동안 불러서, 마무리 처리에 들어갈 때 판이 비어 있지 않게 한다 -
        /// <b>조각이 얼마 없는 판으로 마무리를 해봤자 역전할 여지가 없다.</b>
        ///
        /// 채워진 조각이 매치를 이루면 그대로 캐스케이드가 돌아 <b>데미지까지 들어간다</b> -
        /// 그래서 이 리필만으로 적이 쓰러져 승리가 될 수도 있다(기획 의도).
        /// 단계(= 조작 차단)는 건드리지 않는다.
        ///
        /// <b>⚠ 리필 억제를 풀기만 하고 되돌리지 않는다</b>(2026-08-28, 러시 타임이 빈 칸을 낀 채
        /// 시작하던 버그의 원인). 낙하·리필은 <b>코루틴 하나가 아니다</b> - 매치를 처리하다
        /// 안착·방해 처리가 <see cref="GravityAndCascadeRoutine"/> 을 <b>기다리지 않고</b> 또 띄운다.
        /// 그것들은 내 코루틴보다 오래 살아남으므로, 끝나면서 억제를 도로 켜버리면 <b>그 아이들이
        /// 자기 열의 리필만 건너뛰고</b> 그 자리가 영영 빈 칸으로 굳는다.
        /// 부르는 쪽 둘(마무리 직전 / 러시 직전) 다 그 뒤에 리필이 켜져 있어야 맞다 -
        /// 마무리는 <see cref="RunFinisher"/> 가 자기 손으로 다시 켠다.
        ///
        /// <b>빈 칸이 없어질 때까지 돌린다.</b> 한 번으로 안 되는 경우가 있다: 그때 다른 코루틴이
        /// 쥐고 있던 칸(잠금)은 <c>RefillEmptyCells</c> 가 건너뛰는데, 그 칸을 나중에
        /// 놓아줘도 <b>다시 굴려주는 사람이 없다.</b>
        /// </summary>
        public IEnumerator RefillBoard()
        {
            finisherRunning = false;

            // 판이 6x6 이라 두세 번이면 끝난다. 상한은 서로 물려 안 끝나는 경우를 끊기 위한 것.
            const int MaxPasses = 12;

            for (int pass = 0; pass < MaxPasses; pass++)
            {
                yield return StartCoroutine(GravityAndCascadeRoutine());

                if (!HasEmptyCell())
                    yield break;

                // 남은 빈 칸은 지금 다른 코루틴이 쥐고 있는 것이다. 한 프레임 쉬며 놓아줄 틈을 준다.
                yield return null;
            }

            // 여기까지 왔으면 <b>주인이 돌아오지 않는 잠금</b>이다. 뺏어서 채운다.
            if (ForceReleaseEmptyCells())
                yield return StartCoroutine(GravityAndCascadeRoutine());
        }

        // 무작위 생성 스킬(라미아 브릴란스)이 쓰는 창구들 -----------------------------------

        /// <summary>
        /// 판 안에서 <b>서로 다른</b> 칸을 <paramref name="count"/> 개 무작위로 고른다.
        ///
        /// <b>칸의 내용을 가리지 않는다</b>(2026-08-30 사용자 확정: "완전히 무작위") - 방해블록도
        /// 상자도 스탠드업 고정 칸도 뽑힌다. 덮어쓸 수 없는 칸(구멍)이 뽑히면 그 지점은
        /// <b>헛발</b>이 되고, 그건 이 스킬의 운 요소다.
        /// </summary>
        public void PickRandomCells(int count, List<(int x, int y)> into)
        {
            into.Clear();

            var board = boardManager != null ? boardManager.Board : null;
            if (board == null || count <= 0)
                return;

            int total = board.width * board.height;
            count = Mathf.Min(count, total);

            // 칸 수가 적어(6x6=36) 다시 뽑기로 충분하다 - 섞을 배열을 만들면 그게 더 큰 낭비다.
            // 상한을 둬서 최악의 경우에도 프레임을 잡아먹지 않게 한다.
            int guard = total * 8;
            while (into.Count < count && guard-- > 0)
            {
                var cell = (Random.Range(0, board.width), Random.Range(0, board.height));
                if (!into.Contains(cell))
                    into.Add(cell);
            }
        }

        [Header("특수 퍼즐 (미스틱 포지셔닝)")]
        [Tooltip("특수 퍼즐이 접히는 속도 배율. 1이면 일반 조각과 같다. " +
                 "한때 2로 뒀다가 <b>너무 빨라서 안 보인다</b>는 지적을 받고 되돌렸다(2026-08-30).")]
        [SerializeField] private float specialFoldSpeed = 1f;

        [Tooltip("접힌 뒤 새 2x2 가 나타나기까지 쉬는 시간(초). 0이면 곧바로 나타난다.")]
        [SerializeField] private float specialRelocateDelay = 0.12f;

        // 뿌리가 뻗을 후보를 모으는 버퍼. 사이클마다 다시 쓴다(사이클마다 새 List 를 만들지 않는다).
        private readonly List<(int x, int y)> growthCandidates = new List<(int x, int y)>();

        /// <summary>
        /// <b>직전 블록들의 상하좌우</b> 중에서 덮어쓸 수 있는 칸을 골라 <paramref name="count"/> 개까지
        /// 무작위로 뽑는다(2026-08-30 사용자 지시: "뿌리처럼 자라게").
        ///
        /// <b>자기 조각이 있는 쪽은 뺀다</b>(2026-08-30 사용자 지시로 바뀐 규칙) - 뿌리는 늘
        /// 새 자리로 뻗는다. 후보가 모자라면 있는 만큼만 돌려주고, 하나도 없으면 비워서
        /// 돌려준다(= 사방이 자기 조각이라 뿌리가 막혔다 - 거기서 연쇄가 끝난다).
        /// </summary>
        public void PickGrowthCells(IReadOnlyList<(int x, int y)> from, int count,
            int panelIndex, bool overwritesBoxes, List<(int x, int y)> into)
        {
            into.Clear();
            growthCandidates.Clear();

            if (boardManager == null || from == null || count <= 0)
                return;

            for (int i = 0; i < from.Count; i++)
            {
                var (x, y) = from[i];

                AddGrowthCandidate(x + 1, y, panelIndex, overwritesBoxes);
                AddGrowthCandidate(x - 1, y, panelIndex, overwritesBoxes);
                AddGrowthCandidate(x, y + 1, panelIndex, overwritesBoxes);
                AddGrowthCandidate(x, y - 1, panelIndex, overwritesBoxes);
            }

            // 후보가 많지 않아(최대 8칸) 뽑을 때마다 목록에서 빼는 게 가장 단순하고 확실하다.
            while (into.Count < count && growthCandidates.Count > 0)
            {
                int pick = Random.Range(0, growthCandidates.Count);
                into.Add(growthCandidates[pick]);
                growthCandidates.RemoveAt(pick);
            }
        }

        private void AddGrowthCandidate(int x, int y, int panelIndex, bool overwritesBoxes)
        {
            if (!boardManager.CanConvert(x, y, overwritesBoxes))
                return;

            // <b>자기 조각이 있는 쪽으로는 안 뻗는다</b>(2026-08-30 사용자 지시). 거기 뻗어봐야
            // 판이 안 변하고, 오히려 그 칸에 걸려 있던 강화가 지워진다. 뿌리는 늘 새 자리로 간다.
            if (boardManager.IsOwnPiece(x, y, panelIndex))
                return;

            if (!growthCandidates.Contains((x, y)))
                growthCandidates.Add((x, y));
        }

        /// <summary>그 색 캐릭터의 전투력. 없으면 0.</summary>
        private float PanelCombatPower(int panelIndex)
        {
            var character = boardView != null ? boardView.GetCharacter(panelIndex) : null;
            return character != null ? character.CombatPower : 0f;
        }

        /// <summary>
        /// 검은 파동!이 <b>차례로 때릴 줄</b>을 뽑는다(2026-08-30 사용자 지시: 컷인이 2연타라
        /// 열 한 번, 행 한 번으로 나눠서 때린다). 세로줄이 먼저, 그 다음이 가로줄이다.
        /// 같은 줄은 두 번 뽑지 않는다.
        /// </summary>
        public void PickWipeLines(int columns, int rows, List<(bool vertical, int index)> into)
        {
            into.Clear();

            var board = boardManager != null ? boardManager.Board : null;
            if (board == null)
                return;

            crossLines.Clear();
            for (int i = 0; i < Mathf.Min(columns, board.width); i++)
            {
                int x;
                do { x = Random.Range(0, board.width); } while (crossLines.Contains(x));
                crossLines.Add(x);
                into.Add((true, x));
            }

            crossLines.Clear();
            for (int i = 0; i < Mathf.Min(rows, board.height); i++)
            {
                int y;
                do { y = Random.Range(0, board.height); } while (crossLines.Contains(y));
                crossLines.Add(y);
                into.Add((false, y));
            }
        }

        /// <summary>그 줄이 덮는 칸을 모은다.</summary>
        public void CollectLine(bool vertical, int index, List<(int x, int y)> into)
        {
            into.Clear();

            var board = boardManager != null ? boardManager.Board : null;
            if (board == null)
                return;

            if (vertical)
            {
                for (int y = 0; y < board.height; y++)
                    into.Add((index, y));
            }
            else
            {
                for (int x = 0; x < board.width; x++)
                    into.Add((x, index));
            }
        }

        // 이번에 뽑은 줄 번호. 같은 줄을 두 번 뽑지 않으려고 들고 있는다.
        private readonly List<int> crossLines = new List<int>();

        /// <summary>
        /// 그 칸들을 쓸어버리고 자기 패널로 채운다. <b>실제로 바뀔 칸의 전투력 합만큼</b>
        /// 적에게 데미지가 들어간다(2026-08-30 사용자 확정 - 미스틱의 포지셔닝과 같은 기준).
        ///
        /// 상자와 미스틱의 특수 퍼즐은 <b>바뀌지 않으므로 데미지에도 안 들어간다</b> -
        /// 세는 기준과 바꾸는 기준이 어긋나면 "안 지워졌는데 값은 받았다"가 된다.
        /// </summary>
        public IEnumerator WipeCellsToPanelRoutine(IReadOnlyList<(int x, int y)> cells, int panelIndex,
            HashSet<(int x, int y)> alreadyWiped = null)
        {
            wipeTargets.Clear();
            for (int i = 0; i < cells.Count; i++)
            {
                // <b>이미 이번 스킬로 때린 칸은 건너뛴다</b>(2026-08-30) - 열과 행이 만나는
                // 교차점이 그렇다. 두 번 때리면 <b>방금 내가 놓은 조각</b>의 전투력이
                // 데미지로 또 세어져서, 화면에는 아무 일도 안 일어나는데 값만 붙는다.
                if (alreadyWiped != null && alreadyWiped.Contains(cells[i]))
                    continue;

                if (boardManager.CanConvert(cells[i].x, cells[i].y, overwritesBoxes: false))
                    wipeTargets.Add(cells[i]);
            }

            alreadyWiped?.UnionWith(wipeTargets);

            // 데이터를 바꾸기 <b>전에</b> 세야 한다(이 프로젝트의 되풀이되는 함정).
            float removedPower = boardManager.SumCombatPower(wipeTargets, PanelCombatPower);

            yield return ConvertCellsToPanelRoutine(wipeTargets, panelIndex);

            int damage = ScaleDamage(removedPower);
            if (damage > 0)
                OnMatchDamage?.Invoke(damage);
        }

        private readonly List<(int x, int y)> wipeTargets = new List<(int x, int y)>();

        // 버닝 트랙은 따로 산다(2026-09-03에 옮김). 규칙은 BoardManager 가, 연출은
        // BoardView 가 이미 갖고 있어서 여기 남아 있던 건 순서 잡기뿐이었다 - JojoPuzzle.View.BurnTrack.
        private BurnTrack burnTrack;

        // 미스틱의 특수 퍼즐도 따로 산다(2026-09-03에 옮김) - JojoPuzzle.View.SpecialPuzzle.
        private SpecialPuzzle special;

        // 매치 판정도 따로 산다(2026-09-03에 옮김) - JojoPuzzle.View.MatchResolver.
        private MatchResolver matchResolver;

        /// <summary>
        /// 매치 하나를 처리한다. <b>부르는 자리가 넷</b>이라(플레이어 드롭 / 캐스케이드 /
        /// 안착 재스캔 / 십자변환 직후) 이름을 그대로 남겼다.
        /// </summary>
        private IEnumerator ResolveSingleGroup(ConnectionResult group, int anchorX, int anchorY)
            => matchResolver.Resolve(group, anchorX, anchorY);

        // ---- IMatchHost ----
        // 매치는 판만이 아니라 전투 전체를 건드린다 - 그것들이 어디 붙어 있는지는 여기가 안다.

        bool IMatchHost.IsResolveFrozen => IsMatchResolveFrozen;

        bool IMatchHost.IsStandUpTimeActive => IsStandUpTimeActive;

        void IMatchHost.NotifyActivity() => NotifyMatchResolved();

        void IMatchHost.MatchCounted(Vector3 worldPosition) => OnMatchCounted?.Invoke(worldPosition);

        void IMatchHost.PiecesCleared(int panelIndex, int count, int empoweredCount)
        {
            AddPieceCount(panelIndex, count);

            // 코인·경험치는 <b>실제로 지운 수</b>를 쓴다.
            OnPiecesMatched?.Invoke(count);

            // 스킬 게이지만 강화 조각을 여러 개로 친다(시트: "스킬 채우기에 용이").
            // 스티커가 없으면 배수가 1이라 실제 수와 같아진다.
            float per = StickerEffects.ValueOf(StickerEffect.EmpoweredCountsAsThree);
            int extra = per > 1f ? Mathf.RoundToInt(empoweredCount * (per - 1f)) : 0;
            OnGaugePiecesMatched?.Invoke(count + extra);
        }

        /// <summary>
        /// 스킬 게이지를 채울 때 쓰는 조각 수. <see cref="OnPiecesMatched"/> 와 <b>다르다</b> -
        /// 강화 조각을 여러 개로 치는 스티커가 이쪽에만 듣는다.
        /// </summary>
        public event System.Action<int> OnGaugePiecesMatched;

        void IMatchHost.RaiseMatchDamage(ConnectionResult group, float matchWeight)
            => RaiseMatchDamage(group, matchWeight);

        void IMatchHost.ChargeGaugeByOnePiece() => ChargeGaugeByOnePiece();

        Coroutine IMatchHost.Run(IEnumerator routine) => StartCoroutine(routine);

        /// <summary>유나·미스틱의 스킬 연출(SkillPresentation)이 부르는 자리. 안쪽만 옮겼다.</summary>
        public IEnumerator WaitForPlaceableSquare(int size, PlacementStyle style, List<(int x, int y)> into)
            => special.WaitForPlaceableSquare(size, style, into);

        /// <summary>같은 이유로 이름을 남긴 다리.</summary>
        public IEnumerator MakeSpecialPanelsRoutine(IEnumerable<(int x, int y)> cells, int panelIndex,
            int matches, List<(int x, int y)> madeOut = null)
            => special.MakeRoutine(cells, panelIndex, matches, madeOut);

        /// <summary>매치가 특수 뭉치를 접은 뒤, 남은 횟수가 있으면 다른 자리에 다시 심는다.</summary>
        private IEnumerator RelocateSpecialCluster(int panelIndex, int matchesLeft)
            => special.RelocateRoutine(panelIndex, matchesLeft);

        /// <summary>그 열들을 다시 굴린다. <b>기다리지 않는다</b> - 부르는 쪽 주석 참고.</summary>
        private void RequestCascade(ISet<int> columns)
            => StartCoroutine(GravityAndCascadeRoutine(initialColumns: columns));

        /// <summary>유나의 스킬 연출(SkillPresentation)이 부르는 자리. 안쪽 구현만 옮겼다.</summary>
        public void PickBurnTrackCells(int count, PlacementStyle style, List<(int x, int y)> into)
            => burnTrack.PickCells(count, style, into);

        /// <summary>같은 이유로 이름을 남긴 다리.</summary>
        public IEnumerator PlaceBurnTracksRoutine(IReadOnlyList<(int x, int y)> cells)
            => burnTrack.PlaceRoutine(cells);

        /// <summary>
        /// 태우고 나서 <b>낙하까지</b> 이어 붙인다.
        ///
        /// 낙하는 <b>실제로 탔을 때만</b> 굴린다. 판이 바뀌어 발동이 취소되면 아무것도
        /// 안 지워졌으니 굴릴 이유가 없다 - 예전에 yield break 로 건너뛰던 갈림길이
        /// 지금은 "탄 열이 비었는가"로 바뀌었을 뿐 같은 판단이다.
        /// </summary>
        private IEnumerator BurnTrackRoutine(int fuelX, int fuelY, int trackX, int trackY,
            PanelView riser)
        {
            var columns = new HashSet<int>();
            yield return burnTrack.IgniteRoutine(fuelX, fuelY, trackX, trackY, riser, columns);

            if (columns.Count > 0)
                yield return StartCoroutine(GravityAndCascadeRoutine(initialColumns: columns));
        }

        /// <summary>
        /// 날것의 전투력에 배율을 씌워 적에게 먹인다.
        /// 판을 고치는 처리들이 하나같이 쓰던 두 줄이라 이름을 붙여 뒀다.
        /// </summary>
        private void DealMatchDamage(float rawPower)
        {
            int damage = ScaleDamage(rawPower);
            if (damage > 0)
                OnMatchDamage?.Invoke(damage);
        }

        // ---- ICascadeHost ----
        // 낙하는 입력과 상관없이 돌지만, "지금 판을 굴려도 되는 구간인가"는 여기가 안다.

        bool ICascadeHost.IsBoardStopped => IsBoardStopped;

        bool ICascadeHost.IsFallFrozen => IsBoardFallFrozen;

        bool ICascadeHost.IsStandUpTimeActive => IsStandUpTimeActive;

        bool ICascadeHost.IsFinisherRunning => finisherRunning;

        bool ICascadeHost.IsHeldByPlayer((int x, int y) cell)
            => isDragging && cell == (dragFromX, dragFromY);

        Coroutine ICascadeHost.ResolveMatch(ConnectionResult group, int anchorX, int anchorY)
            => StartCoroutine(ResolveSingleGroup(group, anchorX, anchorY));

        // 낙하·연쇄는 따로 산다(2026-09-03에 옮김). 판이 스스로 사는 일이라
        // 입력 컨트롤러의 몫이 아니었다 - JojoPuzzle.View.BoardCascade.
        private BoardCascade cascade;

        /// <summary>
        /// 낙하·리필·연쇄를 굴린다. <b>부르는 자리가 열한 군데</b>라 이름을 그대로 남겼다 -
        /// 무슨 일 뒤에 판을 다시 굴리는지는 그 자리들의 문맥이라 여기서 읽히는 게 낫다.
        /// </summary>
        private IEnumerator GravityAndCascadeRoutine((int x, int y)? protectOnFirstPass = null,
            ISet<int> initialColumns = null)
            => cascade.Run(protectOnFirstPass, initialColumns);

        /// <summary>
        /// 그 칸의 상하좌우에 <b>아직 강화되지 않은</b> 자기 색 조각이 있는지.
        /// 브릴란스가 "한 사이클 더 갈지"를 이걸로 판단한다.
        /// </summary>
        public bool HasPlainOwnNeighbor(int x, int y, int panelIndex)
            => boardManager != null && boardManager.HasPlainOwnNeighbor(x, y, panelIndex);

        /// <summary>고른 칸만 강화하고, 실제로 바뀐 칸은 화면에도 반영한다.</summary>
        public void EmpowerCells(IReadOnlyList<(int x, int y)> cells, float multiplier)
        {
            if (boardManager == null || cells == null || cells.Count == 0)
                return;

            var changed = boardManager.EmpowerCells(cells, multiplier);
            if (changed.Count > 0)
                boardView?.RefreshEmpowerLook();
        }

        /// <summary>아직 이 색으로 바꿀 칸이 남아 있는지. 끝없이 도는 스킬의 종료 조건이다.</summary>
        public bool HasCellToConvert(int panelIndex, bool overwritesBoxes)
            => boardManager != null && boardManager.HasCellToConvert(panelIndex, overwritesBoxes);

        /// <summary>
        /// <b>비어 있는데 잠겨 있는 칸을 강제로 놓아준다.</b> 채우는 쪽(<c>RefillEmptyCells</c>)은
        /// 잠긴 칸을 건너뛰므로, 주인 없는 잠금이 하나라도 있으면 그 자리는 <b>영영 빈 칸</b>이다
        /// (2026-08-30 사용자 재신고: 러시가 빈 칸을 낀 채 시작했다).
        ///
        /// <b>왜 주인이 사라지는가</b>: 칸을 잠그는 코루틴들이 <b>정상 경로에서만</b> 풀어주기
        /// 때문이다. 단계가 <see cref="BattlePhase.Finished"/> 로 바뀌어 낙하 코루틴이 빠져나가거나,
        /// 연출이 중간에 멈추면 잠금이 남는다. 경로를 하나씩 막는 것도 하고 있지만(try/finally),
        /// <b>마지막에 한 번 걷어내는 그물</b>이 있어야 새 연출이 붙어도 이 증상이 안 돌아온다.
        ///
        /// 여기서 푸는 건 <b>이미 비어 있는 칸</b>뿐이라 남의 조각을 빼앗지 않는다.
        /// </summary>
        private bool ForceReleaseEmptyCells()
        {
            var board = boardManager != null ? boardManager.Board : null;
            if (board == null)
                return false;

            bool released = false;

            for (int y = 0; y < board.height; y++)
            {
                for (int x = 0; x < board.width; x++)
                {
                    if (board.Get(x, y).kind != CellKind.Empty)
                        continue;

                    var cell = (x, y);
                    if (!locks.Release(cell))
                        continue;

                    locks.DropOwnership(cell);
                    locks.DisallowPlayer(cell);
                    released = true;

                    // <b>조용히 넘어가지 않는다</b> - 어느 연출이 놓아주지 않았는지 알아야 고친다.
                    Debug.LogWarning($"[BoardInputController] 빈 칸 ({x},{y}) 의 잠금이 남아 있어 " +
                                     "강제로 풀었습니다. 어떤 연출이 이 칸을 놓아주지 않았는지 확인이 필요합니다.");
                }
            }

            return released;
        }

        /// <summary>판에 빈 칸이 하나라도 있는지. 구멍(Hole)은 빈 칸이 아니다 - 채울 대상이 아니라 방해다.</summary>
        private bool HasEmptyCell()
        {
            var board = boardManager != null ? boardManager.Board : null;
            if (board == null)
                return false;

            for (int y = 0; y < board.height; y++)
            {
                for (int x = 0; x < board.width; x++)
                {
                    if (board.Get(x, y).kind == CellKind.Empty)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 판에 남은 상자를 전부 터뜨린다. <b>매번 다시 훑는다</b> - 상자가 터지면서 생긴 매치가
        /// 또 상자를 만들 수 있어서, 좌표를 미리 모아두면 그 새 상자를 놓친다.
        /// </summary>
        private IEnumerator FinisherUseAllBoxes()
        {
            var board = boardManager.Board;

            // 무한 반복 방지. 6x6 판이라 상자가 이만큼 나올 일은 없지만, 상자가 상자를 낳는
            // 고리가 생기면 여기서 끊는다.
            const int MaxBoxes = 64;

            for (int used = 0; used < MaxBoxes; used++)
            {
                bool found = false;
                int bx = 0, by = 0;

                for (int y = 0; y < board.height && !found; y++)
                {
                    for (int x = 0; x < board.width && !found; x++)
                    {
                        if (board.Get(x, y).kind != CellKind.Box)
                            continue;

                        bx = x;
                        by = y;
                        found = true;
                    }
                }

                if (!found)
                    break;

                yield return StartCoroutine(TriggerBoxCrossRoutine(bx, by));

                if (finisherBoxInterval > 0f)
                    yield return new WaitForSeconds(finisherBoxInterval);
            }

            // 상자가 만든 매치의 접기 연출이 잦아들 때까지 기다린다.
            if (finisherSettleWait > 0f)
                yield return new WaitForSeconds(finisherSettleWait);
        }

        /// <summary>
        /// 스킬 게이지를 소진해 <b>그 캐릭터 색의 조각을 전부</b> 지우고 데미지를 준다.
        ///
        /// <b>게이지는 데미지 배율이다</b>(2026-08-25 사용자 정정 - 예전에 "비율"로 잘못 읽었다):
        ///  - <b>조각은 언제나 전부 지워진다.</b> 게이지가 적다고 덜 지우지 않는다.
        ///  - <b>데미지 배율</b> = 남은 게이지. 가득 차 있으면 2배.
        ///
        /// 연출은 <b>게이지가 빛이 되어 자기 조각으로 날아가고, 닿은 조각이 사라지는</b> 순서다.
        /// 그냥 한꺼번에 지우면 무슨 일이 일어났는지 안 보인다는 지적(2026-08-25)에 따른 것이라,
        /// <b>빛이 도착할 때까지 기다렸다가</b> 지운다.
        /// </summary>
        private IEnumerator FinisherSkillBurst(int panelIndex, float gauge)
        {
            if (gauge <= 0f)
                yield break;

            var character = boardView.GetCharacter(panelIndex);
            if (character == null)
                yield break;

            finisherBuffer.Clear();
            boardManager.CollectCellsOfPanel(panelIndex, finisherBuffer);
            if (finisherBuffer.Count == 0)
                yield break;

            // <b>여기서 게이지를 썼다</b>고 알린다(2026-08-28 사용자 지적). 빛이 게이지에서
            // 튀어나가는데 막대가 그대로 차 있으면 무엇을 쓴 건지 읽히지 않는다.
            // 실제로 비우는 건 게이지의 주인인 BattleManager 다 - 여기서 HUD 를 직접 만지면
            // 게이지를 채우는 곳과 비우는 곳이 갈라진다.
            // <b>발동이 성립한 뒤에</b> 쏜다 - 위 조기 종료(내 색 조각이 하나도 없음)로 아무 일도
            // 일어나지 않았는데 게이지만 사라지면 그게 더 이상하다.
            OnFinisherGaugeSpent?.Invoke(panelIndex);

            // 자기 색 조각 <b>전부</b>가 대상이다.
            var cells = new List<(int x, int y)>(finisherBuffer);

            // 강화 배율은 <b>데이터를 비우기 전에</b> 세야 한다(일반 매치와 같은 함정).
            float weight = boardManager.SumDamageWeight(cells);

            yield return StartCoroutine(FinisherFlyLightsAndClear(panelIndex, cells));

            AddPieceCount(panelIndex, cells.Count);
            OnPiecesMatched?.Invoke(cells.Count);

            float multiplier = gauge >= 1f
                ? Mathf.Max(1f, finisherFullGaugeDamageMultiplier)
                : Mathf.Clamp01(gauge);

            int damage = ScaleDamage(character.CombatPower * weight * matchDamageMultiplier * multiplier);
            if (damage > 0)
                OnMatchDamage?.Invoke(damage);
        }

        /// <summary>
        /// 게이지에서 빛을 쏘고, <b>빛이 닿은 조각부터 하나씩</b> 지운다.
        /// 연출기가 없으면 기다리지 않고 곧바로 전부 지운다(연출은 없어도 규칙은 돌아야 한다).
        /// </summary>
        private IEnumerator FinisherFlyLightsAndClear(int panelIndex, List<(int x, int y)> cells)
        {
            var detached = boardView.DetachGroupForCollectEffect(cells);

            if (skillLight == null)
            {
                ClearFinisherCells(cells);
                yield return StartCoroutine(
                    boardView.RemoveDetachedViews(detached, cells[0].x, cells[0].y));
                yield break;
            }

            finisherLightTargets.Clear();
            for (int i = 0; i < cells.Count; i++)
                finisherLightTargets.Add(boardView.GridToWorld(cells[i].x, cells[i].y));

            int arrived = 0;
            System.Action<int> onArrived = _ => arrived++;

            skillLight.OnLightArrived += onArrived;
            skillLight.Launch(GetSkillGaugeWorldPosition(panelIndex), finisherLightTargets);

            // 빛이 전부 도착할 때까지 기다린다. <b>도착한 순서대로 지우지 않고 다 모아서 지우는</b>
            // 이유: 조각을 하나씩 비우면 그 사이 중력이 끼어들어 아직 안 맞은 조각이 내려가버린다.
            while (arrived < cells.Count && skillLight.IsPlaying)
                yield return null;

            skillLight.OnLightArrived -= onArrived;

            ClearFinisherCells(cells);

            yield return StartCoroutine(
                boardView.RemoveDetachedViews(detached, cells[0].x, cells[0].y));
        }

        private void ClearFinisherCells(List<(int x, int y)> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                boardManager.Board.Set(cells[i].x, cells[i].y,
                    new Cell { kind = CellKind.Empty, panelIndex = -1 });
                locks.Release(cells[i]);
            }
        }

        /// <summary>
        /// 그 자리의 스킬 게이지가 화면에서 어디에 있는지를 <b>월드 좌표</b>로 바꾼다 -
        /// 빛은 퍼즐판과 같은 월드에 그려지기 때문이다(리더 초상화 좌표를 구하는 것과 같은 방식).
        /// </summary>
        private Vector3 GetSkillGaugeWorldPosition(int panelIndex)
        {
            RectTransform anchor = panelIndex >= 0 && panelIndex < skillGaugeAnchors.Length
                ? skillGaugeAnchors[panelIndex]
                : null;

            if (anchor == null || targetCamera == null)
                return GetLeaderPortraitWorldPosition();

            Vector3 screen = RectTransformUtility.WorldToScreenPoint(
                null, anchor.TransformPoint(anchor.rect.center));

            Vector3 world = targetCamera.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, Mathf.Abs(targetCamera.transform.position.z)));
            world.z = 0f;

            return world;
        }

        /// <summary>
        /// 스탠드업 동안의 낙하 속도를 걸거나 푼다. <b>러시 타임과 같은 손잡이를 쓴다</b>
        /// (<see cref="View.BoardView.FallSpeedMultiplier"/>) - 둘이 겹치는 일은 없다
        /// (러시에는 스탠드업 게이지가 아예 안 찬다).
        /// </summary>
        private void ApplyStandUpFallSpeed()
        {
            if (boardView == null)
                return;

            boardView.FallSpeedMultiplier = IsStandUpTimeActive
                ? Mathf.Max(0.01f, standUpFallSpeedMultiplier)
                : 1f;
        }

        /// <summary>러시 타임을 켜고 끈다. <see cref="UI.RushTimeController"/> 가 부른다.</summary>
        /// <param name="fallSpeedMultiplier">켤 때 쓸 낙하 속도 배율. 끌 때는 무시하고 1로 되돌린다.</param>
        public void SetRushTime(bool active, float fallSpeedMultiplier)
        {
            if (boardView != null)
                boardView.FallSpeedMultiplier = active ? Mathf.Max(0.01f, fallSpeedMultiplier) : 1f;

            if (!active)
            {
                // 러시가 끝나면 종료 처리로 돌아간다 - 승패 확정(Finished)은 BattleManager 가 찍는다.
                // <b>Playing 으로 되돌리지 않는다</b>: 그러면 상자가 다시 생겨서, 러시 꼬리에
                // 성립한 6매치가 쓸 수 없는 상자를 남긴다(2026-08-28 실제로 그랬다).
                EnterPhase(BattlePhase.Ending);
                return;
            }

            // <b>마무리 처리가 판을 비우고 리필을 멈춰둔 상태</b>로 여기 들어온다(마무리가 러시보다
            // 앞이다). 단계를 옮겨 조작을 열고, 리필 정지도 따로 풀어줘야 판이 다시 채워진다.
            EnterPhase(BattlePhase.RushTime);
            finisherRunning = false;

            // <b>한 번 굴리는 게 아니라 빈 칸이 없어질 때까지 굴린다</b>(2026-08-28) - 러시가
            // 빈 칸을 낀 채로 시작하던 일이 실제로 있었다. 띠가 흐르는 동안 RushTimeController 가
            // 이미 한 번 채워두지만, 그 사이 다른 코루틴이 쥐고 있던 칸이 남을 수 있다.
            StartCoroutine(RefillBoard());
        }

        /// <summary>
        /// 러시 타임에 들어가며 <b>판에 남은 특수 블록을 전부 걷어낸다</b>(2026-09-03 사용자 기획).
        ///
        /// 러시는 <b>평범한 매치에만 집중하는 구간</b>이다 - 점화 블록을 어디로 밀지, 특수 퍼즐을
        /// 몇 번 더 매치해야 하는지까지 신경 쓰면 집중이 흩어진다.
        ///
        /// 걷어낸 조각은 <b>러시 골드로 쳐준다</b>(사용자 확정) - 데미지는 주지 않는다.
        /// 이미 승패가 갈린 구간이라 데미지는 뜻이 없고, 골드는 플레이어가 벌어온 몫이다.
        ///
        /// ⚠ <b>리필보다 먼저 불러야 한다</b>(2026-09-03 사용자 지시). 채운 뒤에 걷어내면
        /// 그 자리가 다시 비어서 두 번 채우게 되고, 띠가 흐르는 동안 판이 한 번 출렁인다.
        /// 그래서 <see cref="RefillBoard"/> 를 부르던 자리(RushTimeController)가 이걸 대신 부른다.
        /// </summary>
        public IEnumerator ClearSpecialBlocksThenRefillRoutine()
        {
            boardManager.CollectSpecialBlocks(rushClearBuffer);

            if (rushClearBuffer.Count > 0)
            {
                // 연출이 도는 동안 다른 처리가 이 칸을 가져가지 못하게 잡아둔다.
                // <b>푸는 건 finally 가 책임진다</b> - 잠금이 남으면 그 자리가 영영 안 채워진다.
                locks.ClaimExclusive(rushClearBuffer);
                try
                {
                    var views = boardView.DetachGroupForCollectEffect(rushClearBuffer);
                    boardManager.ClearCells(rushClearBuffer);

                    // 지운 만큼 러시 골드로. 스킬 게이지(AddPieceCount)는 건드리지 않는다 -
                    // 이 구간에는 더 쓸 스킬이 없다.
                    OnPiecesMatched?.Invoke(rushClearBuffer.Count);

                    if (views.Count > 0)
                    {
                        yield return StartCoroutine(boardView.RemoveDetachedViews(
                            views, rushClearBuffer[0].x, rushClearBuffer[0].y));
                    }

                    // 남은 횟수 표시(룬)를 데이터에 다시 맞춘다.
                    boardView.RefreshSpecialLook();
                }
                finally
                {
                    locks.ReleaseExclusive(rushClearBuffer);
                }
            }

            yield return StartCoroutine(RefillBoard());
        }

        // 러시 시작 때 걷어낼 특수 블록을 담는 버퍼. 한 판에 한 번만 쓰인다.
        private readonly List<(int x, int y)> rushClearBuffer = new List<(int x, int y)>();

        /// <summary>
        /// 남은 스탠드업 10초를 <b>지금 당장 끝내고</b> 종료 연출로 넘어간다. 연출 자체는 그대로
        /// 재생되므로 고정된 조각은 평소처럼 불꽃이 되어 날아가고 칸도 정상적으로 정리된다.
        ///
        /// <b>쓰는 자리</b>: 스탠드업 도중에 적이 쓰러졌을 때(<see cref="Battle.BattleManager"/>).
        /// 이미 이긴 판인데 남은 카운트다운을 그대로 세고 있을 이유가 없다. 그렇다고 연출을
        /// 통째로 잘라내면 StandHeld 칸이 고정된 채 남고 잠금도 안 풀리므로, <b>끊지 않고
        /// 앞당기는</b> 쪽으로 처리한다.
        /// </summary>
        public void CutStandUpTimeShort() => standUpTimeCutShort = true;

        // 위 요청이 들어왔는지. 카운트다운 루프가 이걸 보고 즉시 빠져나온다.
        private bool standUpTimeCutShort;

        private bool inputBlockedByStandUpBanner; // 스탠드업 배너가 떠 있는 동안 true

        /// <summary>
        /// 매치 처리(접기/고정 연출)를 멈춰야 하는 구간. 화면에서 퍼즐판이 아니라 그 위의 다른 것을
        /// 봐야 하는 때다:
        ///  - 퍼즐판 가림막이 떠 있을 때(BoardDimOverlay - 대사창 / 스탠드업 종료 불꽃 연출)
        ///  - 스탠드업 타임 개시 배너가 재생 중일 때
        /// 이 구간에 들어서는 순간 진행 중이던 접기 연출은 취소되어 제거 연출과 함께 사라지고, 그 뒤로 새로
        /// 성립하는 매치는 처리를 시작하지 않고 그 자리에 가만히 멈춰 있다가, 구간이 끝나면
        /// 그때 평소 연출과 함께 처리된다. 없애는 게 아니라 미루는 것이라 데미지·게이지·박스 결과는
        /// 달라지지 않는다.
        /// </summary>
        /// <summary>
        /// 매치 처리를 멈춰야 하는 상태인지.
        ///
        /// <b>화면 암전(ScreenDimOverlay)도 포함한다.</b> 스킬 연출 중에는 판을 볼 수도 만질 수도
        /// 없는데, 그 사이에 매치가 처리되면 리더 스킬로 만든 조각이 파트너 스킬을 쓰기도 전에
        /// 터져버린다(안착/미안착 연계가 성립하지 않던 원인).
        /// </summary>
        private bool IsMatchResolveFrozen =>
            IsBoardCovered
            || inputBlockedByStandUpBanner
            || (screenDimOverlay != null && screenDimOverlay.IsDimmed);

        /// <summary>
        /// 낙하·리필까지 멈춰야 하는 구간 - <b>스탠드업 종료 연출(불꽃이 리더에게 모이는 동안)뿐</b>이다.
        /// 그때만 판이 완전히 조용해야 해서 리필까지 막는다.
        ///
        /// 개시 배너나 대사창은 여기 해당하지 않는다: 조각은 계속 떨어지고 채워지되, 그러다 매치가
        /// 성립하면 그 처리만 IsMatchResolveFrozen으로 미뤄진다. 가림막(IsBoardCovered)이 아니라
        /// 종료 연출 플래그를 직접 보는 이유가 이것 - 가림막은 대사창으로도 켜지기 때문이다.
        /// </summary>
        private bool IsBoardFallFrozen => inputBlockedByStandUpEnd;

        /// <summary>
        /// <b>판을 아예 세워야 하는가.</b> 승패가 확정된 뒤가 그렇다 - 결과 화면이 불투명하게
        /// 덮고 있어서 보이지도 않는 계산을 계속 돌릴 이유가 없다(모바일 발열, 2026-08-28 사용자 결정).
        ///
        /// <see cref="IsBoardFallFrozen"/> 과 다르다: 저쪽은 "잠깐 멈췄다 이어서 한다"이고
        /// 이쪽은 <b>다시 돌 일이 없다</b>. 그래서 기다리지 않고 코루틴을 끝낸다.
        /// 다음 판은 어차피 판을 새로 만들므로 잃는 것도 없다.
        /// </summary>
        private bool IsBoardStopped => Phase == BattlePhase.Finished;

        /// <summary>
        /// <b>새로 조각을 집을 수 있는가.</b> 단계와 가림막을 <b>둘 다</b> 본다 -
        /// 이 프로젝트에서 "지금 만져도 되나"를 묻는 곳은 전부 여기를 지난다.
        /// </summary>
        private bool CanPickUpPiece =>
            IsPlayablePhase
            && !IsPausedByMenu
            && !IsBoardCovered
            && !inputBlockedByStandUpBanner
            && !inputBlockedByStandUpEnd;

        /// <summary>
        /// <b>들고 있던 조각을 지금 내려놓아야 하는가.</b> <see cref="CanPickUpPiece"/> 의 반대가
        /// <b>아니다</b> - 스탠드업 배너와 일시정지는 일부러 뺐다. 잠깐 멈췄다 이어서 하는
        /// 상태라 손에 든 걸 뺏으면 오히려 어색하다.
        /// </summary>
        private bool MustReleaseHeldPiece =>
            !IsPlayablePhase || IsBoardCovered || inputBlockedByStandUpEnd;

        /// <summary>
        /// 스탠드업 타임 10초가 멈춰야 하는 상태인지.
        ///
        /// 판이 가려져 있으면(가림막 = 대사창·스탠드업 종료 연출, 화면 암전 = 스킬 연출) 플레이어는
        /// 아무것도 못 한다. 그 사이에 10초가 깎이면 억울하므로 시계를 세운다.
        /// 제한시간(BattleManager.ApplyTimerPause)이 이미 같은 두 오버레이를 보고 멈추므로,
        /// 두 시계가 같은 기준으로 움직이게 된다.
        /// </summary>
        private bool IsStandUpTimeFrozen =>
            IsBoardCovered || (screenDimOverlay != null && screenDimOverlay.IsDimmed);
        private bool inputBlockedByStandUpEnd; // 스탠드업 타임 종료 시퀀스(불꽃 흡수 + 정지) 재생 중 true - 원래는 캐릭터 공격 모션 동안이라 입력을 막는 것과 같은 취지

        private (int x, int y)? pendingBoxTapCell; // 첫 번째 탭을 기다리고 있는 박스 좌표
        private float pendingBoxTapTime;

        // ⭐ 잠금은 <b>따로 산다</b>(2026-09-03에 객체로 뺐다). 집합 셋을 직접 만지는 대신
        // 이름 붙은 동작을 부른다 - JojoPuzzle.View.BoardCellLocks 를 볼 것.
        // 판을 고치는 처리들(BurnTrack·BoardCascade·SpecialPuzzle)도 이 객체를 그대로 나눠 든다.
        private readonly BoardCellLocks locks = new BoardCellLocks();

        // 박스 십자변환이 피해야 할 칸(=데이터가 아직 확정되지 않은 칸)을 담는 재사용 버퍼.
        // 탭할 때마다 새로 만들지 않도록 돌려쓴다.
        private readonly HashSet<(int x, int y)> boxBlockedCells = new HashSet<(int x, int y)>();

        public void Initialize(BoardManager manager, BoardView view)
        {
            boardManager = manager;
            boardView = view;

            // ⚠ 힌트는 <b>여기서</b> 만든다. boardManager 가 런타임에 들어오므로, 미루어 만들면
            // 그 전에 ClearHint 가 한 번이라도 불릴 때 null 을 문 채로 굳는다.
            hint = new BoardHint(boardManager, boardView,
                                 hintIdleDelay, hintSearchInterval, CollectHintBlockedCells);
            burnTrack = new BurnTrack(boardManager, boardView, locks,
                                      PanelCombatPower, DealMatchDamage);
            cascade = new BoardCascade(boardManager, boardView, locks, this,
                                       refillSettleDuration, refillAvoidsImmediateMatch);
            special = new SpecialPuzzle(boardManager, boardView, locks,
                                        PanelCombatPower, DealMatchDamage, RequestCascade,
                                        specialRelocateDelay);
            matchResolver = new MatchResolver(boardManager, boardView, locks, special, this,
                                             specialFoldSpeed);
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (standUpTimeUI != null)
            {
                standUpTimeUI.OnBannerShown += () =>
                {
                    inputBlockedByStandUpBanner = true;
                    // 배너가 팝인하는 속도와 똑같은 시간 동안 어두워지게 - 배너 강조 연출은
                    // 나중에 캐릭터 스킬 컷인에도 재사용할 예정이라, 하드코딩 대신 배너 자신의
                    // 타이밍 값(PopInDuration)을 그대로 넘겨서 자동으로 싱크되게 함.
                    screenDimOverlay?.SetDim(true, standUpTimeUI.PopInDuration);
                };
                standUpTimeUI.OnExitStart += () =>
                {
                    // 배너가 퇴장을 시작하는 바로 그 순간부터, 퇴장에 걸리는 시간과 똑같이 다시 밝아짐
                    screenDimOverlay?.SetDim(false, standUpTimeUI.ExitDuration);
                };
                standUpTimeUI.OnBannerHidden += () =>
                {
                    inputBlockedByStandUpBanner = false;
                    // 배너가 끝났다고 곧바로 게이지를 초기화하지 않고, 10초짜리 스탠드업 타임을 시작.
                    // 게이지는 그 10초에 맞춰 실시간으로 100%→0%로(채워졌던 것과 반대 방향으로) 줄어듦.
                    StartCoroutine(StandUpTimeCountdownRoutine());
                };
            }
        }

        [Header("스탠드업 타임 본편 (배너가 끝난 뒤 주어지는 제한시간)")]
        [SerializeField] private float standUpTimeDuration = 10f;

        [Header("스탠드업 타임 종료 시퀀스")]
        [Tooltip("합쳐진 불꽃 하나가 리더 초상화까지 날아가는 시간(초). 여러 개면 이 시간만큼 순차로 반복된다.")]
        [SerializeField] private float standUpFlameFlyDuration = 0.22f;

        [Tooltip("날아가는 동안 줄어드는 최종 배율 - 캐릭터에게 빨려 들어가는 느낌을 준다.")]
        [SerializeField] private float standUpFlameArriveScale = 0.35f;

        [Tooltip("덩어리 하나가 도착한 뒤 다음 덩어리가 출발하기까지 쉬는 시간(초). " +
                 "연달아 붙여 보내면 몇 개가 넘어갔는지 눈으로 세어지지 않는다. 마지막 덩어리 뒤에는 쉬지 않는다.")]
        [SerializeField] private float standUpFlameBatchInterval = 0.35f;

        [Tooltip("마지막 불꽃이 도착한 뒤 공격을 시작하기까지 버티는 시간(초). " +
                 "이 동안 리더는 4.readyattack 자세를 유지한다. 0이면 기를 받자마자 때려서 " +
                 "너무 급해 보인다.")]
        [SerializeField] private float standUpAttackWindupDuration = 0.8f;

        [Tooltip("공격 모션(5.attackdone) 재생 시간(초). 이 시간이 지나면 데미지가 확정된다. " +
                 "Spine 클립 길이에 맞출 것 - 현재 5.attackdone은 0.667초다. " +
                 "Animator의 전환 블렌드(0.25초)는 클립 시작과 동시에 진행되므로 더할 필요 없다.")]
        [SerializeField] private float standUpAttackMotionDuration = 0.667f;

        [Tooltip("데미지 숫자가 뜬 뒤 스탠드업이 실제로 끝나기까지 붙잡아두는 시간(초). " +
                 "이게 없으면 숫자가 뜨자마자 판이 밝아지고 조각이 쏟아져서 얼마나 나왔는지 읽을 틈이 없다. " +
                 "이 동안 리더는 5.attackdone 마지막 프레임 자세로 멈춰 있고, 가림막도 그대로 유지된다. " +
                 "<b>DamagePopupUI.TotalDuration(1.35초)보다 길게 잡아야</b> 숫자가 아직 떠 있는데 " +
                 "판이 먼저 밝아지는 일이 없다. 팝업이 머무는 시간을 늘리면 여기도 같이 늘릴 것.")]
        [SerializeField] private float standUpDamageReadDuration = 1.5f;

        [Tooltip("불꽃이 날아갈 목표. 리더 초상화(PlayerCharImage2)의 RectTransform.")]
        [SerializeField] private RectTransform leaderPortrait;

        /// <summary>스탠드업 타임이 시작될 때 발행 - 나중에 여기서 특수 규칙(패널 변형 등)을 켜면 됨.</summary>
        public event System.Action OnStandUpTimeStart;

        /// <summary>스탠드업 타임이 끝날 때 발행 - 나중에 여기서 특수 규칙을 끄고 데미지를 정산하면 됨.</summary>
        public event System.Action OnStandUpTimeEnd;

        /// <summary>
        /// 일반 매치가 성립해 데미지가 발생했을 때 발행(인자 = 데미지 값).
        /// 스탠드업 타임 중에는 조각이 사라지지 않고 고정되며 별도 연출로 정산되므로 발행하지 않는다.
        /// DamagePopupUI가 숫자를 띄우고, ScoreUI가 점수로 누적하고, BattleManager가 적 체력을 깎는다.
        /// </summary>
        public event System.Action<int> OnMatchDamage;

        /// <summary>
        /// 스탠드업 종료 시, 합쳐진 불꽃 하나가 리더에게 도착할 때마다 발행(인자 = 누적 진행도 0~1).
        /// 구독자가 이 값으로 리더 불꽃 크기를 1배 → 2배로 보간한다. 불꽃이 하나뿐이면 첫 도착에
        /// 곧바로 1.0이 오므로 즉시 2배가 되고, 여러 개면 그 수만큼 나눠서 커진다.
        /// </summary>
        public event System.Action<float> OnStandUpFlameArrived;

        /// <summary>
        /// 매치가 성립해 조각이 실제로 처리됐을 때 발행 (인자 = 그 수). 캐릭터 스킬 게이지를 채우는 데 쓴다.
        ///
        /// 색은 넘기지 않는다 - 편성한 두 캐릭터의 게이지는 <b>어떤 색을 맞췄든 함께</b> 오르기 때문.
        /// (캐릭터마다 분모 skillRequiredMatchCount가 달라서 차는 속도만 다르다.)
        ///
        /// 두 경로에서 발행된다:
        ///  - 일반 매치: 실제로 지워진 수. 박스가 생기는 매치는 앵커 한 칸이 박스로 남으므로 그만큼 뺀다.
        ///  - 스탠드업 타임: 이번에 <b>새로 고정된</b> 수. 이미 고정돼 있던 칸까지 세면, 무리에 한 칸씩
        ///    붙을 때마다 무리 전체가 다시 세어져서 게이지가 폭주한다.
        /// </summary>
        public event System.Action<int> OnPiecesMatched;

        /// <summary>
        /// 마무리 처리가 그 자리의 <b>스킬 게이지를 썼다</b>(인자는 자리 번호 - 0=리더, 1=파트너).
        /// 게이지의 주인인 <see cref="Battle.BattleManager"/> 가 받아서 실제로 비운다.
        /// </summary>
        public event System.Action<int> OnFinisherGaugeSpent;

        /// <summary>
        /// 스탠드업 종료 시, 불꽃을 다 흡수하고 <b>공격 모션을 시작할 때</b> 발행.
        ///
        /// 불꽃 도착(OnStandUpFlameArrived)과 일부러 분리했다. 마지막 불꽃이 닿자마자 때리면
        /// "기를 받자마자 휘두르는" 그림이라, 그 사이에 standUpAttackWindupDuration만큼 버틴 뒤
        /// 이 이벤트가 나간다. 리더 애니메이터는 이걸 받아 5.attackdone으로 넘어간다.
        /// </summary>
        public event System.Action OnStandUpAttackStart;

        /// <summary>
        /// 스탠드업 종료 데미지가 확정됐을 때 발행(인자 = 데미지 값).
        /// 일반 매치와 공식도 표시 위치도 달라서(적 한가운데) OnMatchDamage와 분리했다.
        /// </summary>
        public event System.Action<int> OnStandUpDamage;

        /// <summary>
        /// 매치가 하나 성립할 때마다 발행. 인자는 <b>그 매치의 pivot 칸 월드 좌표</b>다 -
        /// 연속 매칭 카운트를 그 근처에 띄우는 데 쓴다(ComboCountUI).
        ///
        /// 예전엔 커서 화면 좌표를 넘겼는데, 판 가장자리에서 매치하면 숫자와 대사가 화면 밖으로
        /// 잘려 나갔다(2026-08-23 사용자 신고). 받는 쪽이 판 중심 쪽으로 당겨서 띄우도록
        /// <b>보드 좌표계</b>로 넘기는 게 맞다 - 화면 좌표는 이미 잘린 자리라 되돌릴 수 없다.
        /// 횟수를 여기서 세지 않는 이유는, 무엇을 "연속"으로 볼지(판 전체인지, 끊기는 조건이
        /// 있는지)가 아직 정해지지 않아서다 - 세는 쪽이 정하게 두었다.
        /// </summary>
        public event System.Action<Vector3> OnMatchCounted;

        [Header("데미지 (임시 공식 - BattleManager가 생기면 그쪽으로 옮길 것)")]
        [Tooltip("매치 데미지 = 그 색 캐릭터의 전투력 × 제거된 조각 수 × 이 배수. " +
                 "일반 매치의 정식 공식은 기획에 아직 없어서 임시로 잡아둔 값이다 " +
                 "(엑셀에 있는 건 스탠드업 전용 StandDamage 공식뿐).")]
        [SerializeField] private float matchDamageMultiplier = 1f;

        /// <summary>
        /// <b>이번 판에만</b> 걸리는 데미지 배율. "데미지 증가" 아이템을 사면 올라간다
        /// (2026-08-27 사용자 기획 - 각 퍼즐 조각의 전투력이 +50%).
        ///
        /// 데미지는 전투력에 <b>선형</b>이라 "전투력 +50%"와 "데미지 x1.5"는 같은 말이다.
        /// 그래서 전투력을 캐릭터 애셋에서 건드리지 않는다 - 그건 도감 데이터라 한 판 때문에
        /// 고치면 판이 끝나도 남는다(레벨·경험치로 이미 겪고 있는 부채).
        ///
        /// <b>인스펙터 값(matchDamageMultiplier)과 따로 둔다</b> - 그건 게임 전체의 밸런싱이고
        /// 이건 한 판짜리다. 섞으면 판이 끝날 때 원래 값이 뭐였는지 알 수 없다.
        /// </summary>
        public float ItemDamageMultiplier { get; set; } = 1f;

        /// <summary>아이템 배율까지 얹은 최종 데미지. 데미지를 내보내는 자리는 전부 여기를 지난다.</summary>
        private int ScaleDamage(float raw)
            => Mathf.RoundToInt(raw * Mathf.Max(0f, ItemDamageMultiplier));

        // 게이지가 100%를 찍어 배너를 재생시키는 순간(ChargeGaugeByOnePiece)부터 이미 true가 됨 -
        // 배너 구간은 IsMatchResolveFrozen이라 그동안 매치 처리가 멈춰 있는데, 배너가 끝나고
        // 밀린 매치가 처리될 때 이 플래그가 켜져 있어야 일반 제거가 아니라 스탠드업 고정(회전)
        // 처리로 잡힌다. OnStandUpTimeStart 이벤트 자체는 실제 10초 카운트다운이 시작되는
        // 시점(배너가 사라진 직후) 그대로 유지 - 스킬/규칙 토글 등은 그 타이밍이 맞음.
        public bool IsStandUpTimeActive { get; private set; }

        /// <summary>
        /// 배너가 사라진 직후부터 10초 동안, 100%였던 게이지를 실시간으로 0%까지 줄임
        /// (SetGaugeProgress는 값만 받아 그리므로, 채울 때와 같은 함수에 감소하는 값을 넣으면
        /// 자동으로 채워졌던 순서 그대로 거꾸로 줄어듦 - 별도의 "역방향 그리기" 로직 불필요).
        /// 스탠드업 타임 동안 무엇을 할지는 나중에 정해서 OnStandUpTimeStart/End에 붙이면 됨.
        /// </summary>
        private IEnumerator StandUpTimeCountdownRoutine()
        {
            // IsStandUpTimeActive는 ChargeGaugeByOnePiece에서 배너를 트리거할 때 이미 true가 됨.
            OnStandUpTimeStart?.Invoke();

            float t = 0f;
            while (t < standUpTimeDuration && !standUpTimeCutShort)
            {
                // 판이 가려져 있는 동안은 스탠드업 시계도 멈춘다. 스킬 연출이나 대사창처럼
                // 플레이어가 아무것도 못 하는 구간에서 10초가 깎이면 억울하다 - 제한시간(BattleManager)이
                // 같은 이유로 이미 멈추고 있어서 기준을 맞춘 것이다.
                if (!IsStandUpTimeFrozen)
                {
                    t += Time.deltaTime;
                    float remainingFraction = 1f - Mathf.Clamp01(t / standUpTimeDuration);
                    boardView.SetGaugeProgress(remainingFraction);
                }

                yield return null;
            }

            boardView.SetGaugeProgress(0f);
            gaugePieceCount = 0;
            gaugeAwaitingStandUpReset = false;
            IsStandUpTimeActive = false;
            standUpTimeCutShort = false;
            ApplyStandUpFallSpeed();

            // IsStandUpTimeActive가 꺼진 시점에도, 막판에 매치돼서 합체 회전을 재생 중인
            // 코루틴이 남아있을 수 있다. 데이터는 이미 커밋됐으니 정리 대상에서 빠질 일은
            // 없지만(2026-09-03에 순서를 뒤집었다), 회전이 도는 중에 아래 ClearAllStandHeldCells가
            // 뷰를 회수하면 돌아가던 연출이 엉뚱한 칸에 남는다 - 끝날 때까지 기다린다.
            while (matchResolver.MergesPlaying > 0)
                yield return null;

            yield return StartCoroutine(StandUpTimeEndSequenceRoutine());

            // 여기까지 와야 스탠드업 한 판이 진짜로 끝난 것이다. 이 줄이 알림보다 앞에 있어야
            // 구독자가 "스탠드업이 끝났는지"를 물었을 때 일관된 답을 받는다.
            IsStandUpEpisodeActive = false;

            OnStandUpTimeEnd?.Invoke();

            // 정지가 끝난 지금부터 낙하/리필 시작 - 스탠드업 중 쌓여있던 빈 칸을 전부 채움
            // (열 범위를 좁힐 필요 없음 - 보드 전체에 걸쳐 한꺼번에 빈 칸이 생겼으므로).
            StartCoroutine(GravityAndCascadeRoutine());
        }

        /// <summary>
        /// 스탠드업 타임 종료 연출.
        ///  1) StandHeld 무리를 정사각형 단위로 쪼개 불꽃으로 바꾼다 - 정사각형마다 하나,
        ///     정사각형에 못 낀 낱개 칸마다 하나.
        ///     데미지 계산과 같은 기준으로 나뉘므로 화면에 날아가는 덩어리와 데미지 구성이 일치한다.
        ///     이 시점엔 아직 전부 <b>스탠드업 캐릭터 아이콘 그대로</b> 판 위에 떠서 말랑하게 숨쉬고 있다.
        ///  2) 덩어리는 자기 차례가 왔을 때 비로소 불꽃으로 바뀌어(BeginFlameFlight) 리더 초상화로
        ///     날아간다. 같은 매치의 조각들은 다 같이 움직이고, 매치끼리는 한 박자씩 쉬며 순차로 진행한다.
        ///  3) 한 매치가 도착할 때마다 리더의 불꽃이 커진다(최대 2배 - OnStandUpFlameArrived 구독자가 처리).
        ///  4) 다 흡수한 뒤 standUpAttackWindupDuration만큼 버티고(readyattack 자세 유지),
        ///     OnStandUpAttackStart로 공격 모션을 띄운 다음, 모션이 끝나는 순간
        ///     (standUpAttackMotionDuration) 스탠드업 공식으로 계산한 데미지를 적 한가운데에 띄운다.
        ///  5) 숫자를 읽을 시간(standUpDamageReadDuration)만큼 그대로 붙잡아둔 뒤에야 정지를 푼다.
        ///     이 구간 내내 판은 어둡고 멈춰 있고 리더는 공격 마지막 자세로 서 있다.
        ///
        /// 데미지를 맨 앞에서 미리 계산하는 이유: 아래에서 보드의 StandHeld 칸을 전부 지워버리는데,
        /// 데미지 공식이 바로 그 칸들의 배치(정사각형 크기)에 의존하기 때문이다.
        ///
        /// 이 시퀀스 내내 새 입력은 막히고(inputBlockedByStandUpEnd), 비워진 칸은 잠금로
        /// 잠가둔다 - 동시에 진행 중인 다른 매치의 낙하/리필이 아직 연출 중인 이 칸들을 먼저
        /// 채워버리는 걸 막기 위함.
        /// </summary>
        private IEnumerator StandUpTimeEndSequenceRoutine()
        {
            // 무리 정보를 먼저 뽑는다 - <b>고정된 조각이 하나도 없으면 보여줄 게 없다</b>.
            // 그때도 연출을 돌리면 불꽃 없이 몇 초 동안 판만 얼어붙고(IsBoardFallFrozen),
            // 그 사이에 생긴 빈 칸이 그동안 안 채워져 <b>구멍이 뚫린 것처럼</b> 보인다
            // (2026-08-30. 적을 눕혀 스탠드업을 앞당겼을 때 실제로 이 상태가 된다 -
            // 배너 동안 밀려 있던 매치가 고정되지 못하고 일반 제거로 처리되기 때문이다).
            var groups = boardManager.FindStandHeldGroups();
            if (groups.Count == 0)
                yield break;

            inputBlockedByStandUpEnd = true;
            OnStandUpEndSequenceStart?.Invoke();

            // <b>⚠ 잠금과 정지는 무슨 일이 있어도 풀려야 한다</b>(2026-08-30 사용자 재신고:
            // 빈 칸 버그). 이 시퀀스가 중간에 멈추면(코루틴 정지·오브젝트 비활성·예외)
            // <c>inputBlockedByStandUpEnd</c> 가 켜진 채로 남고, 그건 <b>낙하와 리필을 통째로
            // 얼린다</b>(<see cref="IsBoardFallFrozen"/>) - 그 뒤로 생기는 빈 칸은 영영 안 채워진다.
            // 비운 칸의 잠금도 마찬가지다. 그래서 정상 경로가 아니라 finally 에서 푼다.
            var clearedCells = new List<(int x, int y)>();

            try
            {
                int standUpDamage = CalculateStandUpDamage();

                // 화면에 합쳐져 보이던 정사각형을 해제 전에 스냅샷해두고 불꽃을 쪼갤 때 기준으로 넘긴다 -
                // 쪼개는 방법이 여럿일 때(데미지는 같음) 마지막 순간에 덩어리가 다시 나뉘어 보이지 않게.
                var shownSquares = boardView.GetActiveStandSquares();
                boardView.ClearAllStandSquareMerges();

                var flameBatches = boardView.BuildStandUpFlames(groups, shownSquares);

                // 뷰는 flames가 들고 있으므로 데이터는 지금 비워도 안전하다.
                clearedCells.AddRange(boardManager.ClearAllStandHeldCells());
                foreach (var cell in clearedCells)
                    locks.Claim(cell);

                // 매치 하나에서 나온 불꽃들은 다 같이 움직이고, 매치와 매치 사이는 순차로 진행한다.
                // 진행도는 0~1이라 구독자(BattleFlameController)가 1배 → 2배로 보간하면 되고,
                // 매치가 하나뿐이면 첫 도착에서 곧바로 1.0이 되어 즉시 2배가 된다.
                Vector3 leaderWorld = GetLeaderPortraitWorldPosition();
                for (int i = 0; i < flameBatches.Count; i++)
                {
                    // 자기 차례가 된 지금에서야 캐릭터 아이콘 → 타들어가는 불꽃으로 바꾼다.
                    // 아직 차례가 안 온 덩어리들은 계속 캐릭터인 채로 판 위에서 기다린다.
                    boardView.BeginFlameFlight(flameBatches[i]);

                    yield return StartCoroutine(
                        boardView.AnimateFlameBatchToTarget(flameBatches[i], leaderWorld, standUpFlameFlyDuration, standUpFlameArriveScale));

                    OnStandUpFlameArrived?.Invoke((i + 1f) / flameBatches.Count);

                    // 덩어리와 덩어리 사이에 한 박자 쉰다 - 연달아 붙어서 날아가면 몇 개가 넘어갔는지
                    // 눈으로 세어지지 않는다. 마지막 덩어리 뒤에는 쉬지 않고 바로 공격 모션으로 넘어간다.
                    if (i < flameBatches.Count - 1 && standUpFlameBatchInterval > 0f)
                        yield return new WaitForSeconds(standUpFlameBatchInterval);
                }

                // 다 흡수했다고 곧바로 때리지 않는다. 잠깐 버텨야 "기를 모았다가 친다"로 읽힌다.
                if (standUpAttackWindupDuration > 0f)
                    yield return new WaitForSeconds(standUpAttackWindupDuration);

                // 여기서 리더가 5.attackdone으로 넘어간다.
                OnStandUpAttackStart?.Invoke();

                // 모션이 끝나는 순간에 데미지를 확정한다 - 휘두르는 도중에 숫자가 뜨면 따로 논다.
                if (standUpAttackMotionDuration > 0f)
                    yield return new WaitForSeconds(standUpAttackMotionDuration);

                OnStandUpDamage?.Invoke(ScaleDamage(standUpDamage));

                // 숫자를 읽을 시간. 여기서 아직 풀지 않는 게 핵심이다 - inputBlockedByStandUpEnd가
                // 켜져 있는 동안은 낙하/리필이 멈춰 있고(IsBoardFallFrozen) 가림막도 유지되며,
                // 리더는 BackToIdle을 아직 안 받았으니 5.attackdone 마지막 프레임에 그대로 서 있다.
                // 먼저 풀고 기다리면 판이 밝아지며 조각이 쏟아지는 와중에 숫자만 떠 있게 된다.
                if (standUpDamageReadDuration > 0f)
                    yield return new WaitForSeconds(standUpDamageReadDuration);
            }
            finally
            {
                foreach (var cell in clearedCells)
                    locks.Release(cell);

                inputBlockedByStandUpEnd = false;
            }
        }

        /// <summary>
        /// 스탠드업 데미지. 무리마다 다음 둘을 더한다:
        ///  - 그 무리 안에서 찾아낸 "모든" 정사각형: 전투력 × 칸 수 × 크기배율(StandUpDamageTable)
        ///    예) 전투력 150이 3×3을 만들면 150 × 9 × 2.1배 = 2,835
        ///  - 정사각형에 속하지 못한 나머지 칸: 전투력 × 칸 수 (배율 없이 일반 매치 그대로)
        ///
        /// 두 항목 모두 "칸 수" 자리에 <b>실효 칸 수</b>(Cell.DamageWeight 의 합)를 쓴다 -
        /// 파트너 스킬로 강화된 칸은 1이 아니라 자기 배율만큼 세어진다. 강화가 없으면 칸 수와
        /// 똑같은 값이라 예전 계산과 결과가 일치한다.
        ///
        /// 정사각형을 화면 합체 목록(activeStandMerges)이 아니라 보드 데이터에서 다시 찾는 이유:
        /// 합체 목록은 뷰가 있을 때만 등록되는 "표시용" 정보라서, 어떤 이유로 뷰가 비면 실제로는
        /// 존재하는 정사각형이 데미지에서 통째로 빠질 수 있었다. 데이터에서 직접 세면 그럴 일이 없다.
        /// SquareMergeFinder는 큰 것부터 겹치지 않게 전부 찾아내므로 화면에 보이는 합체와도 일치한다.
        ///
        /// 데미지가 전투력에 선형 비례하도록 바뀐 뒤로는 int 범위를 넘길 일이 사실상 없지만
        /// (최대치가 Lv50 GR 6×6 = 1,134,000), 여러 무리를 합산하는 자리라 안전하게 long으로 모은다.
        /// </summary>
        private int CalculateStandUpDamage()
        {
            long total = 0;

            // 화면에 합쳐져 보이는 정사각형을 기준으로 넘긴다 - 데미지가 같은 조합이 여럿일 때
            // 화면과 다른 쪽을 골라도 합계는 같지만, "보이는 덩어리와 데미지 구성이 1:1"이라는
            // 이 함수의 전제를 지키려면 같은 기준으로 쪼개야 한다.
            var shownSquares = boardView.GetActiveStandSquares();

            foreach (var group in boardManager.FindStandHeldGroups())
            {
                if (group.Count == 0)
                    continue;

                var character = boardView.GetCharacter(boardManager.Board.Get(group[0].x, group[0].y).panelIndex);
                if (character == null)
                    continue;

                int power = character.CombatPower;

                // 칸 수가 아니라 "실효 칸 수"(강화 배율의 합)로 센다. 강화가 하나도 없으면
                // 이 값은 정확히 칸 수와 같아서 예전 계산과 결과가 완전히 같다.
                float groupWeight = boardManager.SumDamageWeight(group);

                float weightInSquares = 0f;
                foreach (var square in SquareMergeFinder.FindSquareBlocks(group, shownSquares))
                {
                    float squareWeight = 0f;
                    for (int dx = 0; dx < square.size; dx++)
                    {
                        for (int dy = 0; dy < square.size; dy++)
                            squareWeight += boardManager.Board.Get(square.originX + dx, square.originY + dy).DamageWeight;
                    }

                    total += StandUpDamageTable.CalculateSquareDamage(power, square.size, squareWeight);
                    weightInSquares += squareWeight;
                }

                // 정사각형을 이루지 못하고 남은 칸들은 평범한 매치처럼 (전투력 × 실효 칸 수)
                total += (long)System.Math.Round(power * (double)(groupWeight - weightInSquares));
            }

            return (int)System.Math.Min(total, int.MaxValue);
        }

        /// <summary>
        /// 리더 초상화(UI)의 중심을 보드와 같은 월드 좌표로 변환한다.
        /// 보드는 월드 스프라이트인데 초상화는 Screen Space - Overlay 캔버스라 좌표계가 달라서,
        /// 화면 좌표를 한 번 거쳐야 한다. 참조가 없으면 보드 위쪽으로 날아가도록 대체값을 준다.
        /// </summary>
        private Vector3 GetLeaderPortraitWorldPosition()
        {
            if (leaderPortrait == null || targetCamera == null)
                return boardView.GetBoardWorldCenter() + Vector3.up * 5f;

            Vector3 screen = RectTransformUtility.WorldToScreenPoint(
                null, leaderPortrait.TransformPoint(leaderPortrait.rect.center));

            Vector3 world = targetCamera.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, Mathf.Abs(targetCamera.transform.position.z)));
            world.z = 0f;

            return world;
        }

        /// <summary>
        /// 미안착 조각들의 남은 시간을 흘려보내고, 이번에 안착된 칸이 있으면 그 열의 매치 판정을
        /// 다시 돌린다.
        ///
        /// 안착은 "새 매치가 생길 수 있는데 아무도 스캔을 유발하지 않는" 유일한 순간이다 -
        /// 낙하·리필·드롭은 각자 끝나면서 스캔까지 이어주지만, 안착은 시간이 지나서 조용히 일어나기
        /// 때문에 여기서 직접 이어줘야 한다. 그러지 않으면 박스로 만든 매치가 영영 안 터진다.
        ///
        /// Time.deltaTime을 쓰므로 일시정지(timeScale=0) 중에는 저절로 멈춘다. 반대로 대사창이나
        /// 스킬 연출 중에는 계속 흘러가는데, 그 구간은 어차피 매치 처리가 얼어 있어서(IsMatchResolveFrozen)
        /// 안착돼도 실제 처리는 연출이 끝난 뒤로 밀린다 - 그게 의도한 동작이다.
        /// </summary>
        private void TickSettleAndRescan()
        {
            // 판이 아예 섰으면 안착도 재판정도 하지 않는다 - 결과 화면 뒤에서 매 프레임 도는
            // 계산이 그대로 발열이 된다.
            if (IsBoardStopped)
                return;

            // 매치가 멈춰 있는 동안은 안착 시간도 멈춘다. 안 그러면 스킬 연출을 보는 사이에
            // 미안착 시간이 다 흘러버려서, 연출이 끝나는 순간 조각이 곧바로 터진다 -
            // 파트너 스킬을 이어 쓸 틈(미안착 창)이 연출 시간에 잡아먹히는 셈이다.
            if (IsMatchResolveFrozen)
                return;

            var settled = boardManager.TickSettle(Time.deltaTime);
            if (settled.Count == 0)
                return;

            // 안착된 칸이 걸친 열만 다시 굴린다 - 무관한 열까지 건드리면 그쪽에서 진행 중인
            // 다른 매치의 연출 위로 리필이 끼어든다(이 프로젝트가 계속 지켜온 규칙).
            var columns = new HashSet<int>();
            foreach (var (x, _) in settled)
                columns.Add(x);

            StartCoroutine(GravityAndCascadeRoutine(initialColumns: columns));
        }

        [Header("효과음")]
        [Tooltip("조작 관련 효과음을 재생할 대상. 비워두면 소리 없이 진행된다.")]
        [SerializeField] private JojoPuzzle.Audio.SfxPlayer sfx;

        [Header("힌트")]
        [Tooltip("이 시간(초) 동안 매치가 한 번도 성립하지 않으면 힌트를 보여준다. " +
                 "판이 멈춰 있는 동안(일시정지·가림막·암전·스탠드업)은 흐르지 않는다.")]
        [SerializeField] private float hintIdleDelay = 2.5f;

        [Tooltip("힌트로 보여줄 수를 못 찾았을 때 다시 찾아보기까지의 간격(초). " +
                 "매 프레임 보드를 훑지 않기 위한 값이다.")]
        [SerializeField] private float hintSearchInterval = 0.5f;

        // ⭐ 힌트는 <b>따로 산다</b>(2026-09-03에 옮김). 시계와 좌표 몇 개일 뿐인데
        // 이 클래스의 필드 열 개를 차지하고 있었다 - JojoPuzzle.View.BoardHint 를 볼 것.
        // Initialize 에서 만든다. 그 전에는 null 이므로 아래 다리들이 전부 null 을 견딘다.
        private BoardHint hint;

        /// <summary>
        /// 힌트에서 뺄 칸을 채운다. 다른 처리가 쓰고 있는 칸은 힌트 대상이 아니다
        /// (박스·스킬과 같은 기준).
        ///
        /// ⚠ <b>버퍼를 boxBlockedCells 와 나눠 쓴다.</b> 그쪽은 코루틴이 만들어 쓰는 것이고
        /// 여기는 Update 라, 같이 쓰면 진행 중인 코루틴의 기준이 힌트 때문에 바뀔 수 있다.
        /// </summary>
        private void CollectHintBlockedCells(HashSet<(int x, int y)> into)
        {
            locks.CollectUnsettled(into);
        }

        /// <summary>
        /// 매치가 하나 성립했거나 플레이어가 무언가를 했다 - 힌트 시계를 되돌린다.
        ///
        /// ⭐ 예전에는 <c>NotifyMatchResolved</c>·<c>NotifyPlayerActed</c> 두 이름이
        /// <b>똑같은 본문</b>을 들고 있었다(2026-09-03 확인) - 하나로 합쳤다.
        /// </summary>
        private void NotifyPlayerActed() => hint?.NotifyActivity();

        private void NotifyMatchResolved() => hint?.NotifyActivity();

        private void ClearHint() => hint?.Clear();

        /// <summary>
        /// 힌트가 나와도 되는 구간인지. <b>어느 구간이 조작 구간인지는 입력 쪽만 안다</b>.
        ///
        /// 스탠드업 타임에도 띄운다 - 그때는 움직일 수 있는 조각이 줄어 오히려 다음 수가 안 보인다.
        /// 개시 배너와 종료 연출 구간은 뺀다 - 그때는 판을 볼 때가 아니고, 애초에 손을 못 대는
        /// 구간은 <b>막힌 게 아니다</b>(2026-08-28 사용자 신고).
        /// <b>러시 타임은 조작 구간이라 포함된다</b>(IsPlayablePhase 가 그렇게 정의돼 있다).
        /// </summary>
        private bool CanShowHint()
            => IsPlayablePhase && !IsMatchResolveFrozen && !inputBlockedByStandUpEnd
               && !IsPausedByMenu;

        private void TickHint()
            => hint?.Tick(Time.deltaTime, CanShowHint(), IsStandUpTimeActive);

        private void Update()
        {
            if (boardManager == null)
                return;

            TickSettleAndRescan();
            TickHoles();
            TickHint();

            // 퍼즐판이 가려진(=조작 불가) 상태로 막 전환됐는데 아직 조각을 들고 있으면, 지금 가리키고
            // 있는 자리에 즉시 놓아버린다. 아래 분기는 "새로 집는 것"만 막을 뿐 이미 들고 있던 조각의
            // 이동/놓기는 그대로 통과시키기 때문에, 그러지 않으면 스탠드업 타임이 끝나 판이 어두워진
            // 뒤에도 조각을 계속 끌고 다니다 아무 때나 놓을 수 있었다(실제로 있던 버그).
            // 스탠드업 "배너"와 일시정지는 일부러 제외한다 - 잠깐 멈췄다 이어서 하는 상태라
            // 들고 있던 조각을 뺏으면 오히려 어색하다.
            if (isDragging && MustReleaseHeldPiece)
            {
                EndDrag(Input.mousePosition);
                return;
            }

            // isDragging은 "지금 내가 뭔가를 손가락으로 누르고 있는지"만 나타냄 - 이걸로 전체 입력을
            // 막지 않고, 새로운 픽업 시도(TryBeginDrag)는 언제나 허용하되 잠긴 칸이면 그 안에서 거절함.
            // 스탠드업 배너가 떠 있을 땐 "새로 집는 것"만 막음 - 이미 드래그 중이던 조각은 계속
            // 움직이고 놓을 수 있어야 함(허공에 멈춰버리면 안 되므로).
            if (Input.GetMouseButtonDown(0) && !isDragging)
            {
                if (CanPickUpPiece)
                    TryBeginDrag(Input.mousePosition);
            }
            else if (Input.GetMouseButton(0) && isDragging)
                UpdateDrag(Input.mousePosition);
            else if (Input.GetMouseButtonUp(0) && isDragging)
                EndDrag(Input.mousePosition);
        }

        private Vector3 ScreenToWorld(Vector3 screenPosition)
        {
            Vector3 worldPos = targetCamera.ScreenToWorldPoint(screenPosition);
            worldPos.z = 0f;
            return worldPos;
        }

        private void TryBeginDrag(Vector3 screenPosition)
        {
            Vector3 worldPos = ScreenToWorld(screenPosition);
            if (!boardView.TryWorldToGrid(worldPos, out int gx, out int gy))
                return;

            // 일반 패널 또는 박스만 집어들 수 있음 (오자마/구멍/빈칸은 애초에 불가)
            if (!boardManager.Board.Get(gx, gy).CanBeDragged)
                return;

            // 지금 다른 매치 이펙트/낙하 처리 중인 칸이면 안전하게 막음.
            // 단 플레이어 예외에 있으면 예외 - 데이터는 이미 확정됐고 연출만 남은 칸이라
            // 집어도 안전하다(낙하/리필 중인 조각을 기다리지 않고 바로 집을 수 있게 하는 통로).
            if (locks.IsLocked((gx, gy)) && !locks.PlayerAllowed((gx, gy)))
                return;

            draggedView = boardView.DetachView(gx, gy);
            if (draggedView == null)
                return;

            draggedView.SetHeldOnTop(true); // 다른 패널에 가려지지 않도록 최상단 레이어로

            dragFromX = gx;
            dragFromY = gy;
            isDragging = true;

            NotifyPlayerActed();

            // 손을 떼기 전까지 이 칸은 다른 작업(동시에 진행 중인 낙하 등)이 건드리면 안 됨
            locks.Claim((gx, gy));

            // 집어든 순간엔 제자리(원래 칸)가 곧 "지금 놓으면 여기에 놓인다"는 위치이므로 바로 표시.
            UpdateDragTargetHighlight(gx, gy);
        }

        private void UpdateDrag(Vector3 screenPosition)
        {
            Vector3 worldPos = ScreenToWorld(screenPosition);

            // 중간 경로는 실제 판정에 영향 없음(로직 데이터는 아직 안 건드림) - 순수 시각적 추적만.
            draggedView.MoveTo(worldPos);

            if (boardView.TryWorldToGrid(worldPos, out int gx, out int gy))
            {
                // 유나의 점화 블록은 <b>닿는 순간</b> 불이 붙는다(2026-09-01 사용자 지시) -
                // 손을 뗄 때까지 기다리지 않는다.
                if (TryIgniteBurnTrack(gx, gy))
                    return;

                UpdateDragTargetHighlight(gx, gy);
            }
            else
                ClearDragTargetHighlight();
        }

        /// <summary>
        /// 끌고 있는 조각이 점화 블록 칸에 닿았으면 <b>거기서 드래그를 끝내고</b> 버닝 트랙!을 발동한다.
        ///
        /// 손을 떼는 걸 기다리지 않는 게 요점이다 - 블록은 "놓을 자리"가 아니라 <b>스위치</b>라서,
        /// 조각을 갖다 대는 동작 자체가 곧 발동이어야 조작이 읽힌다.
        ///
        /// UpdateDrag(끌고 가는 중)와 EndDrag(한 프레임 만에 눌렀다 뗀 경우) 양쪽에서 부른다.
        /// </summary>
        private bool TryIgniteBurnTrack(int toX, int toY)
        {
            if (draggedView == null)
                return false;

            if (toX == dragFromX && toY == dragFromY)
                return false;

            if (!boardManager.Board.Get(toX, toY).IsBurnTrack)
                return false;

            // 연료는 <b>일반 조각만</b>이다 - 큐브나 또 다른 점화 블록은 연료가 아니다.
            if (boardManager.Board.Get(dragFromX, dragFromY).kind != CellKind.Normal)
                return false;

            // 드래그는 여기서 끝난다. 손이 아직 화면에 붙어 있어도 마찬가지다.
            isDragging = false;
            NotifyPlayerActed();
            ClearDragTargetHighlight();
            locks.Release((dragFromX, dragFromY));
            locks.DisallowPlayer((dragFromX, dragFromY));

            // 집고 있던 뷰가 그대로 <b>타고 올라가는 조각</b>이 된다 - 제자리에 돌려놓지 않는다.
            var riser = draggedView;
            draggedView = null;

            sfx?.PlayPiecePlaced();
            StartCoroutine(BurnTrackRoutine(dragFromX, dragFromY, toX, toY, riser));
            return true;
        }

        /// <summary>
        /// 손을 뗐을 때 실제로 (x,y)에 놓을 수 있는지 미리 판정 - EndDrag의 판정 로직(제자리 탭 예외,
        /// 잠금+플레이어 예외 우회, BlocksNormalOverwrite)과 최대한 같은 기준을
        /// 재사용해서, 테두리가 초록/빨강으로 보여준 것과 실제 결과가 어긋나지 않게 한다.
        /// </summary>
        private bool IsValidDropTarget(int x, int y)
        {
            if (x == dragFromX && y == dragFromY)
                return true; // 제자리 탭(박스면 더블탭 판정)은 항상 허용되는 동작

            if (locks.IsLocked((x, y)) && !locks.PlayerAllowed((x, y)))
                return false;

            return !boardManager.Board.Get(x, y).BlocksNormalOverwrite;
        }

        private void UpdateDragTargetHighlight(int x, int y)
        {
            if (dragHighlightCell.HasValue && dragHighlightCell.Value == (x, y))
                return; // 칸이 안 바뀌었으면 매 프레임 다시 그릴 필요 없음

            dragHighlightCell = (x, y);
            boardView.ShowDragTargetHighlight(x, y, IsValidDropTarget(x, y));
        }

        private void ClearDragTargetHighlight()
        {
            if (!dragHighlightCell.HasValue)
                return;

            dragHighlightCell = null;
            boardView.HideDragTargetHighlight();
        }

        private void EndDrag(Vector3 screenPosition)
        {
            isDragging = false;
            NotifyPlayerActed();
            ClearDragTargetHighlight();
            locks.Release((dragFromX, dragFromY)); // 드래그 상호작용 자체는 여기서 끝 - 이후는 결과에 따라 처리

            // 낙하 중인 조각을 집었던 경우, 그 칸의 정리를 GravityAndCascadeRoutine이 "아직 들고 있다"며
            // 건너뛰었을 수 있다. 손을 뗀 지금 그 예외를 확실히 거둔다 - 안 그러면 남은 예외가
            // 나중에 엉뚱한 칸의 잠금을 우회시킨다. (집을 수 있었다는 건 데이터가 있는 칸이라는 뜻이므로,
            // 접기 연출 중인 빈 칸의 예외를 여기서 잘못 건드릴 일은 없다.)
            locks.DisallowPlayer((dragFromX, dragFromY));

            Vector3 worldPos = ScreenToWorld(screenPosition);
            bool hasTarget = boardView.TryWorldToGrid(worldPos, out int toX, out int toY);

            if (!hasTarget)
            {
                RevertDragToOrigin();
                return;
            }

            bool isSameCellTap = (toX == dragFromX && toY == dragFromY);
            bool wasBox = boardManager.Board.Get(dragFromX, dragFromY).kind == CellKind.Box;

            if (isSameCellTap && wasBox)
            {
                // 이동이 아니라 "제자리 탭" - 박스는 이걸로 더블탭 판정을 함 (한 번은 이동으로 볼 수 있어서)
                RevertDragToOrigin();
                HandleBoxTap(dragFromX, dragFromY);
                return;
            }

            if (!isSameCellTap && locks.IsLocked((toX, toY)) && !locks.PlayerAllowed((toX, toY)))
            {
                // 목적지가 지금 다른 매치 이펙트 처리 중인 칸이면 절대 덮어쓰면 안 됨.
                // (예: 다른 색 매치가 박스로 바뀌는 중인 칸에 여기서 드롭하면 그 박스 색이 뒤바뀌는 버그가 있었음)
                // 단, 플레이어 예외에 있는 칸(데이터는 이미 비워졌고 접기 연출만 남은 칸)은
                // 예외 - 자동 시스템(중력/리필)에게는 여전히 벽이지만 플레이어 드롭만은 허용한다.
                RevertDragToOrigin();
                return;
            }

            // 점화 블록은 보통 UpdateDrag 에서 닿는 순간 이미 발동한다. 여기 한 번 더 있는 건
            // <b>같은 프레임에 눌렀다 뗀 경우</b>(빠른 탭)를 위해서다 - 그때는 UpdateDrag 가
            // 한 번도 안 돈다. MoveAndResolve 앞이어야 하는 이유는, 점화 블록이
            // BlocksNormalOverwrite 라 그냥 두면 "막힌 칸"으로 보여 조각이 튕겨 나가기 때문이다.
            if (!isSameCellTap && TryIgniteBurnTrack(toX, toY))
                return;

            // MoveAndResolve는 이동 + 판정만 하고, 실제 제거/박스 생성 데이터 반영은 안 함
            // (3D 수집 이펙트가 끝난 뒤 ResolveMoveRoutine에서 별도로 커밋함)
            var outcome = boardManager.MoveAndResolve(dragFromX, dragFromY, toX, toY, locks.Blocked, includeStandHeld: IsStandUpTimeActive);

            if (!outcome.moved)
            {
                // 오자마/구멍/박스 등으로 이동이 막힌 경우 → 원래 자리로 복귀
                RevertDragToOrigin();
                return;
            }

            // 이 이동 건은 계속 진행 중이니, 목적지 칸은 처리가 끝날 때까지 다시 잠금.
            // 낙하 중이던 칸에 놓은 경우엔 이미 gravity 쪽이 잠가둔 상태인데, 지금부터는 이 이동이
            // 그 잠금의 주인이다 - 표시해두지 않으면 낙하가 끝날 때 gravity가 먼저 풀어버린다.
            locks.Claim((toX, toY));
            locks.TakeOwnership((toX, toY));

            // 조각이 실제로 놓인 지금 소리를 낸다. 위쪽의 되돌리는 분기들(빈 곳에 놓음·잠긴 칸·
            // 막힌 칸)은 조각이 제자리로 튕겨 돌아가는 것이라 "넣었다"는 소리가 어울리지 않는다.
            sfx?.PlayPiecePlaced();

            StartCoroutine(ResolveMoveRoutine(dragFromX, toX, toY, outcome.connection));
        }

        private void RevertDragToOrigin()
        {
            draggedView.SetHeldOnTop(false);
            boardView.PlaceView(draggedView, dragFromX, dragFromY);
            draggedView = null;
        }

        /// <summary>
        /// 박스를 제자리에서 탭했을 때 호출. 같은 박스를 시간 제한 안에 두 번째로 탭한 것이면
        /// 십자 5칸 변환을 발동시키고, 아니면(첫 탭이거나 다른 박스면) 대기 상태만 갱신.
        /// </summary>
        private void HandleBoxTap(int x, int y)
        {
            bool isSecondTap = pendingBoxTapCell.HasValue
                && pendingBoxTapCell.Value.x == x && pendingBoxTapCell.Value.y == y
                && (Time.time - pendingBoxTapTime) <= doubleTapWindow;

            if (isSecondTap)
            {
                pendingBoxTapCell = null;
                // (참고: 변환 자체는 동기적으로 즉시 일어나므로 미리 잠글 필요가 없음.
                //  예전엔 여기서 잠갔었는데, 변환 후에도 안 풀려서 매치 스캔이 방금 일반 패널로
                //  바뀐 중심 칸을 여전히 벽처럼 취급해 십자 모양이 끊겨버리는 버그가 있었음)
                StartCoroutine(TriggerBoxCrossRoutine(x, y));
            }
            else
            {
                pendingBoxTapCell = (x, y);
                pendingBoxTapTime = Time.time;
            }
        }

        /// <summary>
        /// 이동이 실제로 반영된 후: 목적지 시각 갱신 → 매치됐으면 곧바로 데이터에 제거/박스 커밋 →
        /// 뷰 정리 → 접기 연출 재생(완료까지 대기) → 중력 → 리필 → 캐스케이드.
        /// 접기 연출이 재생되는 동안에도 pivot을 제외한 매치 칸은 이미 잠금이 풀려 있어 다른 조작이
        /// 가능하지만, 낙하(중력)는 연출이 끝날 때까지 시작되지 않는다(ResolveSingleGroup 참고).
        /// 낙하/리필은 이 이동이 실제로 건드린 열(원래 있던 열 + 옮겨간 열 + 매치된 칸들이 걸친 열)
        /// 로만 범위를 좁힌다 - 그러지 않으면 이 매치와 무관한 다른 열에서 진행 중인 다른 매치의
        /// 접기 연출 위로 리필된 조각이 끼어들어 겹쳐 보이는 문제가 있었음.
        /// </summary>
        private IEnumerator ResolveMoveRoutine(int fromX, int toX, int toY, ConnectionResult connection)
        {
            // 목적지에 원래 있던 패널의 뷰를 제거하고, 드래그해온 뷰를 그 자리에 등록
            draggedView.SetHeldOnTop(false);
            boardView.DestroyViewAt(toX, toY);
            boardView.PlaceView(draggedView, toX, toY);
            draggedView = null;

            var affectedColumns = new HashSet<int> { fromX, toX };

            if (connection.canRemove)
            {
                foreach (var cell in connection.cells)
                    affectedColumns.Add(cell.x);

                // TODO: 데미지/스킬게이지 이벤트는 여기서 발행 (Battle 시스템 연결 지점)
                yield return StartCoroutine(ResolveSingleGroup(connection, toX, toY));
            }
            else
            {
                locks.Release((toX, toY)); // 매치 안 됐으면 더 이상 보호할 이유 없음
            }

            // 목적지 처리가 끝났으니 소유권을 놓는다. 낙하 중인 칸에 놓았던 경우 gravity 쪽 정리에서
            // 건너뛰어졌을 수 있으므로, 플레이어 예외도 여기서 확실히 거둔다.
            locks.DropOwnership((toX, toY));
            locks.DisallowPlayer((toX, toY));

            // 매치 성공 여부와 무관하게 실행: 이동 자체만으로도 원래 있던 자리(fromX, fromY)가
            // 비었으므로, 매치가 안 됐어도 그 위 패널들이 내려와서 빈 칸을 채워야 함.
            // 다만 같은 열 위쪽으로 옮긴 경우, 방금 그 조각 자신이 이 낙하에 휩쓸려 한 칸
            // 내려가버리면 조작감이 어색하므로(놓은 자리에 그대로 있어야 자연스러움), 이번
            // 낙하 한 번만 (toX,toY)를 보호 대상으로 넘긴다.
            yield return StartCoroutine(GravityAndCascadeRoutine((toX, toY), affectedColumns));
        }

        /// <summary>
        /// 캐릭터 스킬: 판에 있는 특정 색 조각을 전부 강화한다(파트너 스킬).
        ///
        /// 조각을 만들거나 없애지 않고 표시만 붙이는 거라 낙하도 매치도 유발하지 않는다.
        /// 그래서 잠금도 걸지 않고 캐스케이드도 돌리지 않는다 - 변환 스킬과 다른 점이다.
        /// 강화된 칸은 <b>매치되거나 덮어써질 때까지</b> 그대로 남는다(Cell.empowerMultiplier).
        /// </summary>
        /// <summary>스킬 대상 칸을 미리 알아보는 용도(연출을 그 자리에 먼저 피우기 위해).</summary>
        public void CollectCellsOfPanel(int panelIndex, List<(int x, int y)> result)
        {
            boardManager.CollectCellsOfPanel(panelIndex, result);
        }

        /// <param name="multiplier">강화 데미지 배율(1.5 = 1.5배). SkillDefinition 이 정한다.</param>
        public void EmpowerPanelColor(int panelIndex, float multiplier)
        {
            // 데이터가 아직 확정되지 않은 칸은 건드리지 않는다(다른 스킬과 같은 기준).
            boxBlockedCells.Clear();
            locks.CollectUnsettled(boxBlockedCells);

            var changed = boardManager.EmpowerCellsOfPanel(panelIndex, multiplier, boxBlockedCells);
            if (changed.Count > 0)
                boardView.RefreshEmpowerLook();
        }

        /// <summary>
        /// 적의 방해: 방해블록이 생길 칸을 무작위로 하나 고른다. <b>고르기만 하고 바꾸지 않는다</b> -
        /// 호출부가 그 자리에 구름을 먼저 피우고 가려진 뒤에 <see cref="PlaceObstacle"/>로 바꾼다.
        /// 스킬이 "조회 → 구름 → 적용" 순서를 지키는 것과 같은 이유다(바뀌는 순간이 보이면 안 된다).
        /// </summary>
        public bool TryPickHarassCell(out (int x, int y) cell, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            BuildObstacleBlockedCells();
            if (!boardManager.TryPickHarassTarget(obstacleBlockedCells, out cell))
                return false;

            worldPosition = boardView.GridToWorld(cell.x, cell.y);
            return true;
        }

        /// <summary>
        /// 고른 칸을 실제로 방해블록으로 바꾸고 뷰를 갱신한다.
        ///
        /// 박스·스킬 변환과 같은 규율을 따른다: 데이터가 확정되지 않은 칸은 피하고, 데이터를 먼저
        /// 커밋한 뒤 뷰를 갱신하고, 합체를 깨뜨렸으면 다시 맞춘다.
        /// 낙하는 유발하지 않는다 - 칸을 비우는 게 아니라 다른 것으로 덮어쓰는 것이라 빈 칸이 안 생긴다.
        /// </summary>
        /// <returns>실제로 바뀌었으면 true. 그 사이 그 칸이 자격을 잃었으면 false.</returns>
        public bool PlaceObstacle((int x, int y) cell)
        {
            BuildObstacleBlockedCells();
            if (!boardManager.TryPlaceObstacle(cell, obstacleBlockedCells))
                return false;

            // 스티커: "방해 블록이 생성되면 <b>한 번만</b> 그 자리에 리더의 박스로 덮어씌우기".
            // 방해가 <b>실제로 놓인 뒤에</b> 덮는다 - 놓이지도 않았는데 한 번을 써 버리면 억울하다.
            TryCoverObstacleWithBox(cell);

            // 스탠드업 중에는 방해가 걸리지 않지만(EnemyHarassment 가 막는다), 그래도 같은 규율을
            // 지켜둔다 - 나중에 다른 경로가 이 함수를 부르면 그때 조용히 깨진다.
            obstacleCellBuffer.Clear();
            obstacleCellBuffer.Add(cell);
            boardView.BreakStandSquareMergesOverlapping(obstacleCellBuffer);

            boardView.ApplyCrossConversion(obstacleCellBuffer);
            boardView.RefreshStandUpSquareMerges();

            // 힌트가 이 칸을 가리키고 있었을 수 있다. 보드가 바뀌었으니 다시 찾게 한다.
            ClearHint();

            return true;
        }

        /// <summary>
        /// 붙여 둔 스티커가 있으면 방금 생긴 방해블록을 <b>리더의 박스</b>로 덮는다.
        /// <b>한 판에 한 번뿐이다</b>(시트) - 그래서 썼는지를 여기서 기억한다.
        /// </summary>
        private void TryCoverObstacleWithBox((int x, int y) cell)
        {
            if (obstacleCoverUsed || !StickerEffects.Has(StickerEffect.CoverObstacle))
                return;

            // 리더는 팔레트 0번이다(BattleSetup.BuildPalette 가 그렇게 넣는다).
            if (!boardManager.MakeBox(cell.x, cell.y, 0))
                return;

            obstacleCoverUsed = true;
        }

        // 방해블록 덮기 스티커를 이미 썼는지. 판이 다시 시작하면 ResetForNewBattle 이 되돌린다.
        private bool obstacleCoverUsed;

        /// <summary>데이터가 아직 확정되지 않은 칸(박스·스킬과 같은 기준).</summary>
        private void BuildObstacleBlockedCells()
        {
            obstacleBlockedCells.Clear();
            locks.CollectUnsettled(obstacleBlockedCells);

            // 플레이어가 지금 들고 있는 조각의 원래 자리도 건드리지 않는다 - 손에 든 것이
            // 놓이기도 전에 그 자리가 막히면 어디로 돌아가야 할지 알 수 없다.
            if (isDragging)
                obstacleBlockedCells.Add((dragFromX, dragFromY));
        }

        private readonly HashSet<(int x, int y)> obstacleBlockedCells = new HashSet<(int x, int y)>();
        private readonly List<(int x, int y)> obstacleCellBuffer = new List<(int x, int y)>();

        [Header("적의 방해 - 구멍")]
        [Tooltip("구멍이 유지되는 시간(초). 구멍은 박스로도 스킬로도 지울 수 없어서, 영구히 " +
                 "남으면 판이 되돌릴 수 없이 좁아진다. 그래서 스스로 사라지게 해 균형을 맞춘다. " +
                 "0 이하로 두면 영구히 남으니 주의.")]
        [SerializeField] private float holeDuration = 10f;

        [Tooltip("구멍이 사라질 때 그 자리를 덮을 뭉게구름. 생길 때와 같은 연출을 써서 " +
                 "'여기가 원래대로 돌아왔다'를 알린다. 비워두면 조각이 그냥 나타난다.")]
        [SerializeField] private CloudBurstEffect holeCloudBurst;

        [Tooltip("구름이 피어오르고 구멍이 실제로 사라지기까지의 시간(초). 스킬·방해블록과 같은 값.")]
        [SerializeField] private float holeClearDelayUnderClouds = 0.12f;

        /// <summary>
        /// 적의 방해(가장 낮은 단계): 그 칸을 <b>다른 무작위 색</b>의 조각으로 바꾼다.
        /// 지금 그 자리에 있는 색은 후보에서 빠지므로 반드시 눈에 띄게 달라진다.
        /// </summary>
        /// <returns>실제로 바뀌었으면 true. 그 사이 그 칸이 자격을 잃었으면 false.</returns>
        public bool RecolorCell((int x, int y) cell)
        {
            BuildObstacleBlockedCells();
            if (!boardManager.TryRecolorCell(cell, obstacleBlockedCells))
                return false;

            obstacleCellBuffer.Clear();
            obstacleCellBuffer.Add(cell);
            boardView.BreakStandSquareMergesOverlapping(obstacleCellBuffer);

            boardView.ApplyCrossConversion(obstacleCellBuffer);
            boardView.RefreshStandUpSquareMerges();

            ClearHint(); // 힌트가 이 칸을 가리키고 있었을 수 있다

            // 색이 바뀌면서 매치가 성립했을 수 있으니 그 열을 다시 굴린다.
            // 기다리지 않는다 - 방해 연출은 이미 끝났고 처리는 자기 속도로 진행하면 된다.
            holeColumnBuffer.Clear();
            holeColumnBuffer.Add(cell.x);
            StartCoroutine(GravityAndCascadeRoutine(initialColumns: holeColumnBuffer));

            return true;
        }

        /// <summary>
        /// 적의 방해: 그 칸을 구멍으로 만든다. 방해블록과 달리 <b>지울 수단이 없는 대신
        /// holeDuration 뒤에 스스로 사라진다.</b>
        ///
        /// 구멍은 낙하에서 "벽"이라(Cell.BlocksGravity) 위 조각이 통과하지 못한다 -
        /// 방해블록(고정)보다 훨씬 강한 방해라 시간제한이 붙는 것이다.
        /// </summary>
        /// <returns>실제로 생겼으면 true. 그 사이 그 칸이 자격을 잃었으면 false.</returns>
        public bool PlaceHole((int x, int y) cell)
        {
            BuildObstacleBlockedCells();
            if (!boardManager.TryPlaceHole(cell, obstacleBlockedCells, holeDuration))
                return false;

            obstacleCellBuffer.Clear();
            obstacleCellBuffer.Add(cell);
            boardView.BreakStandSquareMergesOverlapping(obstacleCellBuffer);

            boardView.ApplyCrossConversion(obstacleCellBuffer);
            boardView.RefreshStandUpSquareMerges();

            ClearHint(); // 힌트가 이 칸을 가리키고 있었을 수 있다
            return true;
        }

        /// <summary>
        /// 수명이 다한 구멍을 정리한다. 생길 때와 마찬가지로 <b>구름을 먼저 피우고</b> 가려진
        /// 뒤에 없앤다 - 조각이 허공에서 튀어나온 것처럼 보이지 않게.
        /// </summary>
        private IEnumerator ClearExpiredHoleRoutine((int x, int y) cell)
        {
            // ⭐ <b>데이터가 먼저다</b>(2026-09-03 연출 규칙). 구름은 이미 사라진 구멍을
            // 뒤늦게 보여주는 겉보기다 - 예전엔 구름을 띄우고 <b>기다렸다가</b> 지웠는데,
            // 그동안 판은 "아직 구멍이 있다"고 답했다.
            if (!boardManager.ClearExpiredHole(cell))
                yield break; // 이미 없어졌다면 할 일이 없다

            // 데이터는 비었는데 화면엔 아직 구멍 그림이 있다. 그 사이 낙하가 이 칸을 채우면
            // 구멍 위에 조각이 얹히므로, 그림을 치울 때까지 아무도 못 쓰게 잡아둔다.
            // <b>푸는 건 finally 가 책임진다</b> - 잠금이 남으면 그 자리가 영영 안 채워진다.
            locks.Claim(cell);
            try
            {
                holeCloudBurst?.Burst(boardView.GridToWorld(cell.x, cell.y));

                if (holeClearDelayUnderClouds > 0f)
                    yield return new WaitForSeconds(holeClearDelayUnderClouds);

                boardView.ReleaseViewAt(cell.x, cell.y);
            }
            finally
            {
                locks.Release(cell);
            }

            // 구멍이 사라져 빈 칸이 됐으니 그 열을 다시 굴려서 채운다.
            // 기다리지 않는다 - 이 코루틴은 연출용이고, 낙하·리필은 자기 속도로 진행하면 된다.
            holeColumnBuffer.Clear();
            holeColumnBuffer.Add(cell.x);
            StartCoroutine(GravityAndCascadeRoutine(initialColumns: holeColumnBuffer));
        }

        private readonly HashSet<int> holeColumnBuffer = new HashSet<int>();

        /// <summary>
        /// 구멍의 수명을 흘려보내고, 다 된 구멍을 정리하기 시작한다.
        ///
        /// 판이 멈춰 있는 동안은 시간도 멈춘다 - 다른 시계(제한시간·미안착·스탠드업)와 같은
        /// 방침이다. 플레이어가 아무것도 못 하는 사이에 방해가 저절로 풀리면 안 된다.
        /// </summary>
        private void TickHoles()
        {
            if (IsMatchResolveFrozen)
                return;

            var expired = boardManager.TickHoles(Time.deltaTime);
            for (int i = 0; i < expired.Count; i++)
                StartCoroutine(ClearExpiredHoleRoutine(expired[i]));
        }

        /// <summary>
        /// 캐릭터 스킬: 지정한 칸들을 그 캐릭터 색으로 바꾼다(리더의 구역 변환 스킬 등).
        ///
        /// 보드를 건드리는 새로운 주체라서 박스 십자변환과 <b>같은 규율</b>을 따른다:
        ///  - 데이터가 아직 확정되지 않은 칸만 피한다(잠긴 칸을 통째로 피하면 진행 중인 낙하 근처에서
        ///    변환이 통째로 빠진다 - 박스에서 겪었던 그 문제다).
        ///  - 바뀐 칸은 미안착으로 둬서 곧바로 매치 처리에 들어가지 않게 한다(콤보를 이어 쓸 틈).
        ///  - 합체를 깨뜨렸으면 다시 맞춘다.
        ///  - 데이터를 먼저 커밋하고 뷰는 그 뒤에 갱신한다.
        ///
        /// 연출(구름 등)은 호출부가 담당한다 - 이 코루틴은 보드만 책임진다.
        /// 반환은 코루틴이라 결과를 못 주므로, 실제로 바뀐 칸이 필요하면 convertedOut 에 담아 준다.
        /// </summary>
        public IEnumerator ConvertCellsToPanelRoutine(IEnumerable<(int x, int y)> cells, int panelIndex,
            List<(int x, int y)> convertedOut = null, bool overwritesBoxes = false)
        {
            // 박스와 같은 기준: 잠긴 칸 전부가 아니라 "데이터가 아직 확정되지 않은 칸"만 피한다.
            boxBlockedCells.Clear();
            locks.CollectUnsettled(boxBlockedCells);

            var converted = boardManager.ConvertCellsToPanel(cells, panelIndex, boxBlockedCells,
                                                             boxSettleDuration, overwritesBoxes);
            convertedOut?.Clear();
            if (converted.Count == 0)
                yield break;

            convertedOut?.AddRange(converted);

            // 변환으로 StandHeld 무리가 매치 기준 밑으로 줄었으면 풀어준다(박스와 같은 처리).
            var released = boardManager.ReleaseUndersizedStandHeldGroupsNear(converted);

            var affected = new List<(int x, int y)>(converted);
            affected.AddRange(released);
            boardView.BreakStandSquareMergesOverlapping(affected);
            boardView.RestoreDefaultLook(released);
            boardView.RefreshStandUpSquareMerges();

            // 데이터가 확정된 뒤에 뷰를 갱신한다 - 순서가 반대면 화면이 데이터보다 앞서간다.
            boardView.ApplyCrossConversion(converted);

            // 연출이 끝날 때까지 다른 매치가 이 칸을 가로채지 못하게 잠근다.
            foreach (var cell in converted)
            {
                locks.Claim(cell);
                locks.TakeOwnership(cell);
                locks.DisallowPlayer(cell);
            }

            yield return null; // 뷰 갱신이 한 프레임 반영되도록

            foreach (var cell in converted)
            {
                locks.Release(cell);
                locks.DropOwnership(cell);
            }

            var columns = new HashSet<int>();
            foreach (var cell in affected)
                columns.Add(cell.x);

            // 낙하/캐스케이드는 <b>기다리지 않고</b> 띄운다.
            //
            // 기다리면 교착이 난다: 스킬 연출은 이 코루틴이 끝나길 기다리고, 이 코루틴은
            // 캐스케이드를 기다리는데, 캐스케이드 안의 매치 처리는 화면 암전이 풀리길 기다리고,
            // 그 암전은 스킬 연출이 끝나야 풀린다 - 넷이 서로를 물고 영영 안 끝난다.
            //
            // 안 기다려도 안전하다. 방금 바꾼 칸은 미안착이라 당장 매치 대상이 아니고,
            // 빈 칸을 만든 것도 아니라 낙하할 것도 없다. 실제 처리는 연출이 끝나 암전이
            // 풀린 뒤에 이어진다.
            StartCoroutine(GravityAndCascadeRoutine(initialColumns: columns));
        }

        /// <summary>
        /// 박스를 두 번 탭해서 십자 5칸(자신+상하좌우)을 박스 색의 일반 패널로 변환.
        /// 변환으로 우연히 매치가 생겨도, 여기서 생기는 매치는 절대 새 박스를 만들지 않음
        /// (안 그러면 박스로 박스를 계속 만들어내서 난이도가 무너짐).
        ///
        /// 변환은 탭 즉시 일어난다(기다리지 않음). 그 순간 다른 연출(매치/낙하/리필)이 쓰고 있는 칸은
        /// 덮어쓸 수 없어 빠지는데, 그건 그대로 넘어간다 - 진행 중인 연출의 칸을 가로채면 그 매치의
        /// 색이 뒤바뀌는 버그가 재현되고, 나중에 채워 넣는 방식은 조각이 뒤늦게 튀어나와 어색하다.
        /// </summary>
        private IEnumerator TriggerBoxCrossRoutine(int x, int y)
        {
            // 잠긴 칸을 전부 피하지 않고, "데이터가 아직 확정되지 않은 칸"만 피한다.
            // 낙하·리필 중이거나 접기 연출 중인 칸은 잠겨 있어도 보드 데이터는 이미 확정된
            // 상태라(남은 건 0.25초짜리 연출뿐) 덮어써도 된다 - 플레이어 예외가 정확히
            // 그 집합이고, 드래그 판정(TryBeginDrag/EndDrag)도 이미 같은 기준을 쓰고 있다.
            // 예전엔 여기서만 잠금를 통째로 피해서, 매치나 리필이 도는 근처에서 박스를
            // 쓰면 십자 일부가 통째로 빠지는 버그가 있었다.
            boxBlockedCells.Clear();
            locks.CollectUnsettled(boxBlockedCells);

            var converted = boardManager.ConvertCrossToNormal(x, y, boxBlockedCells);

            // 변환으로 근처 StandHeld 무리가 매치 기준(4개) 밑으로 줄어들면 - 더 이상 매치된 상태가
            // 아니므로 - 그 무리 전체를 다시 움직일 수 있는 일반 패널로 풀어준다. 안 그러면 조각
            // 수가 모자란데도 방해블록처럼 계속 고정된 채로 남아서 이동도 매치도 안 되는 버그가 있었음.
            var releasedFromStandHeld = boardManager.ReleaseUndersizedStandHeldGroupsNear(converted);

            // 변환된 칸이든, 방금 풀린 칸이든 정사각형으로 합체돼 있던 뷰가 있으면 원래 크기의
            // 개별 조각으로 되돌린다. 뷰가 그 칸들을 파괴/재생성하기 전에(AnimateBoxUnfold 이전에)
            // 먼저 풀어줘야 함 - 순서가 바뀌면 확대된 채인 호스트 뷰가 그대로 파괴되거나 숨겨진
            // 멤버 뷰가 다른 칸에 잘못 재활용됨.
            var affectedCells = new List<(int x, int y)>(converted);
            affectedCells.AddRange(releasedFromStandHeld);
            boardView.BreakStandSquareMergesOverlapping(affectedCells);

            // 고정이 풀린 칸은 다시 평범한 패널이 됐으므로 스탠드업 전용 아이콘도 원래 아이콘으로
            // 되돌린다. converted(십자변환된 칸)는 아래 AnimateBoxUnfold가 뷰를 새로 스폰하면서
            // 알아서 기본 아이콘으로 그려지지만, 이 칸들은 기존 뷰를 그대로 재사용하므로 여기서
            // 명시적으로 되돌려주지 않으면 스탠드업 아이콘이 그대로 남는다.
            boardView.RestoreDefaultLook(releasedFromStandHeld);

            // 합체를 깨뜨렸으면 남은 칸들로 다시 맞춰준다. 3x3의 한 칸만 덮어써져도 위에서 9칸이
            // 통째로 풀리는데, 남은 8칸으로 여전히 2x2가 성립한다. 이 호출이 없으면 다음 매치가
            // 일어날 때까지 낱개인 채로 남아서 "분명히 정사각형인데 안 커진" 상태가 되고,
            // 데미지는 보드 데이터에서 다시 구하므로 그 사이 화면과 데미지가 어긋난다.
            // 이 시점엔 변환/해제가 데이터에 이미 커밋돼 있어서 그대로 다시 계산하면 된다.
            boardView.RefreshStandUpSquareMerges();

            // 펼쳐진 조각들은 곧바로 매치 대상이 되지 않고 잠시 미안착 상태로 둔다.
            // 예전엔 AnimateBoxUnfold 안에서 1초를 통째로 기다려 "플레이어가 박스 사용을 인지할
            // 시간"을 벌었는데, 그동안 보드 전체가 멈춰버렸다. 이제 이 5칸만 기다리고 다른 열은
            // 계속 굴러간다. 시간이 지나면 Update의 TickSettle이 알아서 매치 판정을 이어준다.
            boardManager.MarkUnsettled(converted, boxSettleDuration);

            // 펼쳐지는 애니메이션 동안 관련된 칸을 잠가서 다른 매치가 끼어들지 못하게 함.
            //
            // 소유권 표시에도 넣는 게 중요하다. 이제 십자가 낙하·리필이 진행 중인 칸까지
            // 덮어쓸 수 있게 됐는데, 그 칸의 잠금은 원래 GravityAndCascadeRoutine의 것이다.
            // 표시해두지 않으면 그 루틴이 자기 연출을 마치면서 "내 잠금"인 줄 알고 풀어버려,
            // 펼치기가 아직 도는 중에 다른 매치가 이 칸을 가로챈다. 소유권 표시가 바로
            // "이 칸의 잠금 주인이 바뀌었다"를 알리는 기존 장치다(드롭 처리가 같은 이유로 쓴다).
            foreach (var cell in converted)
            {
                locks.Claim(cell);
                locks.TakeOwnership(cell);

                // 낙하 중이던 칸이면 gravity가 "놓아도 되는 칸"으로 열어뒀을 수 있다. 지금부터는
                // 펼치기 연출이 도는 중이라 다시 닫는다(예전 박스 동작과 동일하게).
                locks.DisallowPlayer(cell);
            }

            // 박스가 열리며 십자로 펼쳐지는 이펙트 - 끝날 때까지 매치 판정을 미룸
            yield return StartCoroutine(boardView.AnimateBoxUnfold(x, y, converted));

            // 펼치기 애니메이션이 완전히 끝난 지금에서야 잠금을 풀고 매치를 확인.
            // 잠금을 미리 풀면 스캔이 이 칸들을 벽처럼 취급 못 해서(=매치 대상으로 정상 포함되지만),
            // 애니메이션이 채 안 끝났는데 다른 매치가 이 칸을 가로챌 위험이 생김.
            foreach (var cell in converted)
            {
                locks.Release(cell);
                locks.DropOwnership(cell);
            }

            // TODO: 변환 자체에 대한 스킬 이펙트/사운드는 여기서 발행 예정

            // 이 박스 변환이 실제로 건드린 열(변환된 칸 + 방금 풀린 StandHeld 칸)만 낙하/리필 대상으로
            // 좁혀서, 무관한 다른 열에서 진행 중인 다른 매치의 접기 연출 위로 리필된 조각이 끼어들지
            // 않게 한다(ResolveMoveRoutine과 동일한 이유).
            var affectedColumns = new HashSet<int>();
            foreach (var cell in affectedCells)
                affectedColumns.Add(cell.x);

            var groups = boardManager.ScanBoardForMatches(locks.Blocked, includeStandHeld: IsStandUpTimeActive);

            // 박스 코루틴도 동시에 여러 개가 돌 수 있으므로 호출마다 따로 가진다.
            var boxResolvingGroups = new List<Coroutine>();
            foreach (var group in groups)
            {
                var forcedGroup = group;
                // 십자 변환으로 생긴 매치는 6개 이상이어도 박스 생성 금지.
                // 지금은 ResolveSingleGroup이 Cell.bornFromBox로도 막으므로 이건 이중 안전장치다
                // (boxSettleDuration이 0이 되면 변환된 조각이 이 스캔에 바로 잡히는데, 그때도 동일하게 막힌다).
                forcedGroup.createsBox = false;

                foreach (var cell in forcedGroup.cells)
                    affectedColumns.Add(cell.x);

                var anchorCell = forcedGroup.cells[Random.Range(0, forcedGroup.cells.Count)];

                // 캐스케이드와 같은 이유로 한꺼번에 띄운다 - 십자변환으로 여러 곳이 동시에 터져도
                // 접기 연출이 줄줄이 이어지지 않게.
                boxResolvingGroups.Add(StartCoroutine(ResolveSingleGroup(forcedGroup, anchorCell.x, anchorCell.y)));
            }

            for (int i = 0; i < boxResolvingGroups.Count; i++)
                yield return boxResolvingGroups[i];
            boxResolvingGroups.Clear();

            yield return StartCoroutine(GravityAndCascadeRoutine(initialColumns: affectedColumns));
        }

        /// <summary>
        /// 매치 하나의 데미지를 계산해서 OnMatchDamage로 알린다.
        /// 데미지 = 그 색 캐릭터의 전투력(레벨·등급으로 결정) × 제거된 조각의 실효 수 × matchDamageMultiplier.
        /// 조각 수를 곱하는 건 "많이 이을수록 세다"는 최소한의 규칙일 뿐, 확정된 기획 공식이 아니다.
        /// </summary>
        /// <param name="weightedCount">
        /// 강화를 반영한 실효 조각 수(Cell.DamageWeight의 합). 강화가 없으면 group.Count와 같다.
        /// 호출부가 <b>데이터를 비우기 전에</b> 미리 세서 넘겨준다 - 여기서 보드를 다시 읽으면
        /// 이미 지워진 칸이라 강화가 통째로 빠진다.
        /// </param>
        private void RaiseMatchDamage(ConnectionResult group, float weightedCount)
        {
            if (OnMatchDamage == null)
                return; // 듣는 쪽이 없으면 계산 자체를 건너뜀

            var character = boardView.GetCharacter(group.panelIndex);
            if (character == null)
                return;

            // 붙여 둔 스티커가 이 색의 데미지를 얼마나 보태는지 묻는다.
            // ⭐ <b>전투 코드는 스티커를 모른다</b> - "이 색에 얼마 더해?"만 묻는다.
            // 스티커가 늘어나도 이 줄은 안 고친다(StickerEffects 안에서만 늘어난다).
            float stickerBonus = StickerBonus(group.panelIndex);

            int damage = ScaleDamage(
                character.CombatPower * weightedCount * matchDamageMultiplier * stickerBonus);
            if (damage > 0)
                OnMatchDamage.Invoke(damage);
        }

        /// <summary>
        /// 그 색 조각의 데미지에 스티커가 곱할 배수. 아무것도 안 붙었으면 1이다.
        /// 색을 가리는 것과 <b>중복색</b>을 가리는 것이 따로 있어서 둘 다 더한다.
        /// </summary>
        private float StickerBonus(int panelIndex)
        {
            float bonus = 1f;

            var color = boardView.ColorOf(panelIndex);
            if (color.HasValue)
                bonus += StickerEffects.DamageBonus(color.Value);

            if (boardView.IsDuplicateColor(panelIndex))
                bonus += StickerEffects.DuplicateDamageBonus();

            return bonus;
        }

        [Header("스탠드 게이지 (보드 배경판 뒤 라인 게이지 - 이게 곧 스탠드업 타임 게이지)")]
        [SerializeField] private int piecesPerFullGauge = 50; // 이만큼 조각이 제거되면 게이지가 100% 채워짐
        private int gaugePieceCount;
        private bool gaugeAwaitingStandUpReset; // 100% 찍은 뒤 배너가 끝날 때까지 이 상태로 유지

        /// <summary>
        /// 스탠드 게이지(보드 배경판 뒤 라인 게이지)를 조각 하나만큼 충전.
        /// 이 게임엔 게이지가 두 개뿐: 스킬 게이지(HUD, 아직 UI만 있고 기능 없음)와
        /// 이 스탠드 게이지. 스탠드 게이지가 100%(가득 참)가 되는 순간 스탠드업 타임 배너를
        /// 재생하고 게이지를 리셋해서 다음 스탠드업 타임을 위해 다시 채워지게 함.
        /// </summary>
        private void ChargeGaugeByOnePiece()
        {
            if (piecesPerFullGauge <= 0 || gaugeAwaitingStandUpReset)
                return; // 스탠드업 배너가 아직 안 끝났으면 게이지를 100%로 유지, 추가 충전도 안 함

            // <b>스탠드업 게이지는 평소에만 찬다.</b> 시작 연출·종료 처리·러시·결과 화면에서
            // 터지는 조각은 대부분 저절로 터진 것이라(리필 캐스케이드·마무리 처리) 그걸로
            // 스탠드업을 얻는 건 규칙상 맞지 않고, 실제로 <b>러시 안내 위로 스탠드업 배너가
            // 덮치는</b> 사고가 났다(2026-08-28 사용자 신고).
            //
            // 예전에는 이 자리에 단계 이름을 or 로 나열했는데, 단계가 하나 늘 때마다 그 줄을
            // 찾아 고쳐야 했다 - 그러다 한 군데를 빠뜨린 게 위 사고다.
            if (Phase != BattlePhase.Playing)
                return;

            gaugePieceCount++;

            if (gaugePieceCount >= piecesPerFullGauge)
            {
                gaugePieceCount = piecesPerFullGauge; // 넘치지 않게 고정
                boardView.SetGaugeProgress(1f);

                if (standUpTimeUI != null)
                {
                    gaugeAwaitingStandUpReset = true; // 배너(OnBannerHidden)가 끝날 때 리셋됨

                    // 배너가 나오기 시작하는 지금부터 이미 스탠드업 취급으로 전환한다(실제 10초
                    // 카운트다운이 시작되기 전이라도). 배너 동안 밀려 있던 매치가 배너가 끝나고
                    // 처리될 때 일반 제거가 아니라 스탠드업 고정으로 잡히려면 이게 켜져 있어야 한다.
                    IsStandUpTimeActive = true;
                    ApplyStandUpFallSpeed();

                    // 종료 연출까지 끊기지 않고 이어지는 플래그. 여기서 켜고 OnStandUpTimeEnd
                    // 직전에 끈다(IsStandUpEpisodeActive 주석 참고).
                    IsStandUpEpisodeActive = true;

                    // <b>지난 판의 요청은 여기서 지운다</b>(2026-08-30에 자리를 옮김).
                    // 예전엔 카운트다운이 시작할 때 지웠는데, 그러면 <b>배너가 떠 있는 동안</b>
                    // 들어온 요청까지 같이 지워졌다 - 게이지를 채운 그 매치의 데미지로 적이
                    // 쓰러지는 흔한 경우가 딱 그래서, 이미 이긴 판인데 10초를 다 세고 있었다
                    // (2026-08-30 사용자 신고). 한 판의 시작은 배너지 카운트다운이 아니다.
                    standUpTimeCutShort = false;

                    // 스탠드업 타임 배너가 더 강조돼야 하므로, 지금 한창 접히고 있는 매칭 시각효과는
                    // (지금 이 콜백을 유발한 조각이 속한 것까지 포함해서) 전부 취소한다 - 남은 조각은
                    // 접기를 그만두고 그 자리에서 제거 연출과 함께 사라진다(RemoveDetachedViews).
                    // 반드시 Play()보다 먼저 - 배너가 떠서 판이 얼어붙기(inputBlockedByStandUpBanner)
                    // 전에 취소가 걸려야 접다 만 조각이 그대로 굳어버리지 않는다.
                    boardView.CancelAllCollectEffects();

                    standUpTimeUI.Play();
                }
                else
                {
                    // 배너가 씬에 연결 안 돼 있으면 끝을 알려줄 방법이 없으니 안전하게 즉시 리셋
                    gaugePieceCount = 0;
                    boardView.SetGaugeProgress(0f);
                }
            }
            else
            {
                boardView.SetGaugeProgress((float)gaugePieceCount / piecesPerFullGauge);
            }
        }

}
}
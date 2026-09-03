using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.Core;
using JojoPuzzle.View;
using JojoPuzzle.Battle;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 스킬을 썼을 때의 연출 순서를 담당한다.
    ///
    ///   1) 캐릭터 대사        - SpeechDirector (대사가 없으면 즉시 넘어간다)
    ///   2) 캐릭터 애니메이션  - 아직 없어서 <b>화면 암전만 유지</b>한다(자리만 잡아둔 단계)
    ///   3) 스킬 효과 연출     - 지금은 뭉게구름만. 실제 보드 변경은 여기 뒤에 붙는다
    ///
    /// <b>왜 이 순서가 한 코루틴에 모여 있어야 하는가</b>: 각 단계가 앞 단계가 끝나야 시작되고,
    /// 중간에 판이 어두워져 있어야 한다. 이벤트마다 따로 반응하게 흩어놓으면 순서가 어긋나고
    /// 암전을 언제 풀어야 하는지도 알 수 없다.
    ///
    /// 대사는 BattleSpeechBinder 가 아니라 여기서 재생한다 - 그쪽 speakOnSkill 을 반드시 꺼야
    /// 대사가 두 번 나오지 않는다.
    /// </summary>
    public class SkillPresentation : MonoBehaviour
    {
        [SerializeField] private BattleManager battleManager;

        [Tooltip("슬롯 번호로 편성 캐릭터를 찾는 데 쓴다(팔레트 조회).")]
        [SerializeField] private BoardView boardView;

        [SerializeField] private SpeechDirector speechDirector;

        [Tooltip("2단계에서 화면을 어둡게 하는 데 쓴다. 캐릭터 애니메이션이 생기면 그 뒤로 물러날 자리.")]
        [SerializeField] private ScreenDimOverlay screenDim;

        [Tooltip("3단계에서 퍼즐이 바뀌는 순간을 덮을 구름 연출. 칸마다 하나씩 터진다.")]
        [SerializeField] private CloudBurstEffect cloudBurst;

        [Tooltip("보드를 실제로 바꾸는 쪽. 잠금/미안착/합체 재계산을 전부 여기가 처리한다.")]
        [SerializeField] private BoardInputController boardInput;

        [Header("스킬 데이터가 없는 캐릭터용 임시값")]
        [Tooltip("리더 색으로 바꿀 칸들. PanelType.skill 에 SkillDefinition 애셋이 붙어 있으면 " +
                 "그쪽이 이기고 이 값은 쓰이지 않는다 - 애셋이 아직 없는 캐릭터를 위한 대체값이다.")]
        [SerializeField]
        private Vector2Int[] leaderBlockCells =
        {
            new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(0, 3), new Vector2Int(0, 4),
            new Vector2Int(1, 1), new Vector2Int(1, 2), new Vector2Int(1, 3), new Vector2Int(1, 4),
            new Vector2Int(2, 1), new Vector2Int(2, 4)
        };

        [Tooltip("구름이 피어오른 뒤 보드를 바꾸기까지의 시간(초). 구름이 칸을 충분히 덮은 " +
                 "뒤에 바꿔야 플레이어가 바뀌는 순간을 못 본다. 0이면 같은 프레임에 바뀐다.")]
        [SerializeField] private float changeDelayUnderClouds = 0.12f;

        [Tooltip("[ScatterConvert] 사이클과 사이클 사이 간격(초). 연쇄가 판을 옮겨 다니는 게 " +
                 "이 스킬의 볼거리라 한 박자 쉬어야 몇 번 이어졌는지 눈에 세어진다. " +
                 "0이면 쉬지 않고 곧바로 다음 사이클로 간다.")]
        [SerializeField] private float scatterCycleInterval = 0.18f;

        [Tooltip("[CrossWipe] 한 타와 다음 타 사이 간격(초). 루바니아는 컷인이 <b>2연타</b>라 " +
                 "열 한 번, 행 한 번으로 나눠 때린다 - 안 쉬면 두 번이 한 번으로 뭉쳐 보인다.")]
        [SerializeField] private float wipeStrikeInterval = 0.35f;

        [Tooltip("파트너 스킬이 강화할 대상 색(편성 슬롯). 0이면 리더 색 조각을 강화한다. " +
                 "위와 같은 임시값 - SkillDefinition 애셋이 있으면 쓰이지 않는다.")]
        [SerializeField] private int empowerTargetSlot = 0;

        [Tooltip("임시값으로 강화할 때의 데미지 배율. SkillDefinition 애셋이 있으면 쓰이지 않는다.")]
        [Min(1f)]
        [SerializeField] private float fallbackEmpowerMultiplier = 1.5f;

        /// <summary>편성 0번 = 리더. 슬롯 번호가 곧 팔레트 색 인덱스다.</summary>
        private const int LeaderSlot = 0;

        [Header("타이밍")]
        [Tooltip("캐릭터 애니메이션 자리. 지금은 이 시간만큼 화면을 어둡게 유지만 한다.")]
        [SerializeField] private float animationHoldSeconds = 1f;

        [Tooltip("화면이 어두워지고 밝아지는 데 걸리는 시간(초).")]
        [SerializeField] private float dimFadeDuration = 0.15f;

        [Tooltip("구름이 다 흩어질 때까지 기다린 뒤 암전을 풀지. 끄면 구름이 남아 있는 채로 밝아진다.")]
        [SerializeField] private bool waitForClouds = true;

        /// <summary>스킬 연출이 진행 중인지. 연출이 겹쳐 돌면 암전이 어긋나므로 중복 발동을 막는다.</summary>
        public bool IsPlaying { get; private set; }

        // 좌표를 보드 쪽 형식으로 옮길 때 쓰는 재사용 버퍼(스킬마다 새 리스트를 만들지 않도록).
        private readonly List<(int x, int y)> cellBuffer = new List<(int x, int y)>();

        // 효과 하나가 건드릴 칸을 받아오는 자리. BoardManager.CollectCellsOfPanel 이 넘겨준 목록을
        // 비우고 채우기 때문에 cellBuffer 에 곧바로 이어붙일 수 없어서 한 단계를 거친다.
        private readonly List<(int x, int y)> effectCellBuffer = new List<(int x, int y)>();

        // 효과가 여럿인 스킬에서 같은 칸에 구름이 두 번 피지 않게 거른다.
        private readonly HashSet<(int x, int y)> cloudCellSet = new HashSet<(int x, int y)>();

        // SkillDefinition 애셋이 아직 없는 캐릭터를 위한 대체 효과. 인스펙터 값으로 한 번만 만들어
        // 돌려쓴다 - 발동할 때마다 새로 만들면 스킬마다 힙 할당이 생긴다.
        private SkillEffect[] fallbackLeaderEffects;
        private SkillEffect[] fallbackPartnerEffects;

        private void OnEnable()
        {
            if (battleManager != null)
                battleManager.OnCharacterSkillUsed += HandleSkillUsed;
        }

        private void OnDisable()
        {
            if (battleManager != null)
                battleManager.OnCharacterSkillUsed -= HandleSkillUsed;
        }

        /// <param name="slot">편성 순서. 0=리더, 1=파트너.</param>
        private void HandleSkillUsed(int slot)
        {
            if (IsPlaying)
                return; // 연출 중에 또 들어오면 무시 - 암전이 겹쳐 풀리면 판이 계속 어두워진다

            StartCoroutine(RunSkillRoutine(slot));
        }

        private IEnumerator RunSkillRoutine(int slot)
        {
            IsPlaying = true;

            // 연출이 끝날 때까지 다른 스킬이 발동되지 않게 잠근다.
            // BattleManager 는 게이지를 <b>비우기 전에</b> 이 잠금을 확인하므로, 이 사이에 다른
            // 게이지를 눌러도 게이지가 그대로 남는다(게이지만 날아가던 버그의 수정).
            if (battleManager != null)
                battleManager.HoldSkillActivation(true);

            // try/finally 로 감싸는 이유: 중간에 코루틴이 끊겨도(오브젝트 비활성 등) 잠금과 암전이
            // 반드시 풀려야 한다. 안 풀리면 스킬이 영영 안 눌리고 판이 어두운 채로 남는다.
            try
            {
                yield return RunSkillSteps(slot);
            }
            finally
            {
                if (battleManager != null)
                    battleManager.HoldSkillActivation(false);

                if (screenDim != null)
                    screenDim.SetDim(false, dimFadeDuration);

                IsPlaying = false;
            }
        }

        private IEnumerator RunSkillSteps(int slot)
        {
            var character = boardView != null ? boardView.GetCharacter(slot) : null;

            // 1) 대사. 대사가 없으면 SpeechDirector 가 아무것도 하지 않고 즉시 끝낸다.
            if (speechDirector != null && character != null)
                yield return speechDirector.Play(character, SpeechTrigger.SkillActivate);

            // 2) 캐릭터 애니메이션이 들어올 자리. 지금은 화면만 어둡게 잡아둔다.
            if (screenDim != null)
                screenDim.SetDim(true, dimFadeDuration);

            if (animationHoldSeconds > 0f)
                yield return new WaitForSeconds(animationHoldSeconds);

            // 3) 스킬 효과. 무엇을 할지는 캐릭터의 SkillDefinition 이 정하고, 여기는 순서만 책임진다.
            var effects = GetEffects(character, slot);

            //    먼저 <b>이번 스킬이 실제로 건드릴 칸</b>을 전부 모은다. 예전엔 슬롯과 상관없이
            //    리더의 변환 칸에만 구름을 피웠는데, 그러면 파트너 스킬을 써도 엉뚱한 자리에서
            //    구름만 나고 정작 강화되는 조각 위에는 아무 일도 안 일어난 것처럼 보였다.
            cellBuffer.Clear();
            cloudCellSet.Clear();
            if (effects != null)
            {
                foreach (var effect in effects)
                    CollectAffectedCells(effect, slot);
            }

            // 구름을 칸마다 하나씩 먼저 피운다. 순서가 핵심이다 - 바꾸고 나서 피우면
            // 바뀌는 순간이 그대로 보인다.
            if (cloudBurst != null && boardView != null)
            {
                foreach (var (cx, cy) in cellBuffer)
                    cloudBurst.Burst(boardView.GridToWorld(cx, cy));
            }

            if (changeDelayUnderClouds > 0f)
                yield return new WaitForSeconds(changeDelayUnderClouds);

            // 보드 변경. 구름에 가려져 있어서 플레이어는 바뀌는 순간을 인지하지 못한다.
            if (effects != null && boardInput != null)
            {
                foreach (var effect in effects)
                    yield return ApplyEffect(effect, slot);
            }

            if (cloudBurst != null && waitForClouds)
            {
                while (cloudBurst.IsPlaying)
                    yield return null;
            }

            // 암전 해제와 잠금 해제는 RunSkillRoutine 의 finally 가 책임진다 - 여기서 풀면
            // 중간에 코루틴이 끊겼을 때 영영 안 풀린다.
        }

        /// <summary>
        /// 이번에 발동한 캐릭터의 스킬 효과 목록. 애셋이 붙어 있으면 그게 전부고, 없으면
        /// 인스펙터의 임시값으로 만든 대체 효과를 준다(애셋이 없는 캐릭터도 예전처럼 동작하게).
        ///
        /// 대체값은 슬롯 하나당 한 번만 만들어 캐시한다 - 스킬을 쓸 때마다 배열을 새로 만들면
        /// 발동마다 힙 할당이 생긴다.
        /// </summary>
        private SkillEffect[] GetEffects(PanelType character, int slot)
        {
            if (character != null && character.skill != null && character.skill.effects != null
                && character.skill.effects.Length > 0)
            {
                return character.skill.effects;
            }

            if (slot == LeaderSlot)
            {
                if (fallbackLeaderEffects == null)
                {
                    fallbackLeaderEffects = new[]
                    {
                        new SkillEffect
                        {
                            kind = SkillEffectKind.ConvertRegion,
                            targetSlot = -1,          // 시전자 자신의 색으로
                            cells = leaderBlockCells
                        }
                    };
                }

                return fallbackLeaderEffects;
            }

            if (fallbackPartnerEffects == null)
            {
                fallbackPartnerEffects = new[]
                {
                    new SkillEffect
                    {
                        kind = SkillEffectKind.EmpowerColor,
                        targetSlot = empowerTargetSlot,
                        empowerMultiplier = fallbackEmpowerMultiplier
                    }
                };
            }

            return fallbackPartnerEffects;
        }

        /// <summary>
        /// 효과 하나가 건드릴 칸을 cellBuffer 에 더한다(구름을 그 자리에 피우기 위해).
        /// 효과가 여럿이면 같은 칸이 겹칠 수 있어서 cloudCellSet 으로 한 번 거른다 -
        /// 안 그러면 겹친 칸에만 구름이 두 겹으로 피어 유독 짙어 보인다.
        /// </summary>
        private void CollectAffectedCells(SkillEffect effect, int casterSlot)
        {
            if (effect == null)
                return;

            switch (effect.kind)
            {
                case SkillEffectKind.ConvertRegion:
                    if (effect.cells != null)
                    {
                        foreach (var cell in effect.cells)
                            AddCloudCell((cell.x, cell.y));
                    }
                    break;

                case SkillEffectKind.EmpowerColor:
                    if (boardInput != null)
                    {
                        boardInput.CollectCellsOfPanel(effect.ResolveTargetSlot(casterSlot), effectCellBuffer);
                        foreach (var cell in effectCellBuffer)
                            AddCloudCell(cell);
                    }
                    break;

                case SkillEffectKind.ScatterConvert:
                case SkillEffectKind.SpecialAnchor:
                case SkillEffectKind.CrossWipe:
                case SkillEffectKind.BurnTrack:
                    // <b>미리 모을 칸이 없다</b> - 어디에 생길지는 굴려봐야 안다.
                    // 구름은 아래 ApplyEffect 가 직접 피운다.
                    break;
            }
        }

        private void AddCloudCell((int x, int y) cell)
        {
            if (cloudCellSet.Add(cell))
                cellBuffer.Add(cell);
        }

        /// <summary>
        /// 효과 하나를 실제로 보드에 적용한다. 구름이 이미 그 칸을 덮고 있는 시점에 불린다.
        /// </summary>
        private IEnumerator ApplyEffect(SkillEffect effect, int casterSlot)
        {
            if (effect == null)
                yield break;

            int target = effect.ResolveTargetSlot(casterSlot);

            switch (effect.kind)
            {
                case SkillEffectKind.ConvertRegion:
                    if (effect.cells != null && effect.cells.Length > 0)
                    {
                        // 이 효과가 바꿀 칸만 따로 담아서 넘긴다. cellBuffer 에는 다른 효과의
                        // 칸까지 섞여 있어서 그대로 넘기면 엉뚱한 칸까지 변환된다.
                        effectCellBuffer.Clear();
                        foreach (var cell in effect.cells)
                            effectCellBuffer.Add((cell.x, cell.y));

                        yield return boardInput.ConvertCellsToPanelRoutine(effectCellBuffer, target);
                    }
                    break;

                case SkillEffectKind.EmpowerColor:
                    // 판에 있는 대상 색 조각을 전부 강화한다.
                    // (원래 기획은 일정 범위만이지만, 연계를 시험하기 좋게 지금은 화면 전체를 대상으로 둔다.)
                    boardInput.EmpowerPanelColor(target, effect.empowerMultiplier);
                    break;

                case SkillEffectKind.ScatterConvert:
                    yield return RunScatterConvert(effect, target);
                    break;

                case SkillEffectKind.SpecialAnchor:
                    yield return RunSpecialAnchor(effect, target);
                    break;

                case SkillEffectKind.CrossWipe:
                    yield return RunCrossWipe(effect, target);
                    break;

                case SkillEffectKind.BurnTrack:
                    yield return RunBurnTrack(effect);
                    break;
            }
        }

        // 사이클마다 다시 쓰는 버퍼들. 스킬 한 번에 수십 사이클이 돌 수 있어서
        // 사이클마다 새 List 를 만들면 그게 그대로 쓰레기가 된다(모바일 방침).
        private readonly List<(int x, int y)> scatterPicked = new List<(int x, int y)>();
        private readonly List<(int x, int y)> scatterCreated = new List<(int x, int y)>();
        private readonly List<(int x, int y)> scatterQualified = new List<(int x, int y)>();

        // 직전 사이클에 만든 블록 = <b>뿌리 끝</b>. 다음 사이클은 여기서 뻗는다.
        private readonly List<(int x, int y)> scatterTips = new List<(int x, int y)>();

        /// <summary>
        /// <b>라미아의 브릴란스</b>(2026-08-30 사용자 기획). 한 사이클씩 눈에 보이게 이어 붙인다.
        ///
        /// <code>
        ///   [첫 사이클]   무작위 N지점
        ///   [그 다음부터] <b>직전 사이클 블록의 상하좌우</b> 중 무작위 N칸   ← 뿌리처럼 뻗는다
        ///
        ///   구름 → 자기 패널 생성
        ///   → 상하좌우에 (강화 안 된) 자기 조각이 있으면 <b>방금 만든 그 블록</b>을 강화
        ///   → 하나라도 강화됐으면 한 사이클 더
        /// </code>
        ///
        /// <b>강화하는 건 이웃이 아니라 방금 만든 블록이다</b>(2026-08-30 사용자가 시험 뒤 바꾼 규칙).
        /// 그리고 <b>강화된 조각은 다음 탐지에서 빠진다</b> - 이게 제동 장치라, 연쇄가 이어질수록
        /// 탐지에 걸리는 칸이 줄어 저절로 짧아진다.
        ///
        /// <b>왜 사이클마다 구름을 다시 피우는가</b>: 다른 스킬은 건드릴 칸을 미리 알아서 한 번에
        /// 피우는데, 이 스킬은 <b>굴려봐야 어디인지 안다</b>. 게다가 연쇄가 판을 옮겨 다니는 게
        /// 이 스킬의 볼거리라, 사이클마다 그 자리에 피우는 편이 읽기도 좋다.
        ///
        /// <b>끝나는 조건은 둘</b>이다. 이웃이 하나도 없거나(기획 그대로), <b>더 바꿀 칸이 없거나</b>.
        /// 뒤엣것이 없으면 판이 다 자기 색이 됐을 때 영원히 돈다 - 그때는 이웃이 늘 있기 때문이다.
        /// </summary>
        private IEnumerator RunScatterConvert(SkillEffect effect, int panelIndex)
        {
            int perCycle = Mathf.Max(1, effect.scatterCount);
            int limit = effect.maxCycles > 0 ? effect.maxCycles : AbsoluteScatterCycleLimit;

            for (int cycle = 0; cycle < limit; cycle++)
            {
                // 더 바꿀 게 없으면 굴려도 판이 안 변한다 - 여기서 멈춘다.
                if (!boardInput.HasCellToConvert(panelIndex, effect.overwritesBoxes))
                    yield break;

                // <b>첫 사이클만 무작위</b>다. 그 뒤로는 직전 블록에서 뻗는다
                // (2026-08-30 사용자 지시: "뿌리처럼 생성되는 걸 원했다").
                if (cycle == 0)
                    boardInput.PickRandomCells(perCycle, scatterPicked);
                else
                    boardInput.PickGrowthCells(scatterTips, perCycle, panelIndex,
                                               effect.overwritesBoxes, scatterPicked);

                if (scatterPicked.Count == 0)
                    yield break;   // 뻗을 곳이 없다 - 뿌리가 막혔다

                // 구름을 먼저 피우고 그 아래에서 바꾼다(이 프로젝트의 스킬 연출 순서).
                if (cloudBurst != null && boardView != null)
                {
                    foreach (var (cx, cy) in scatterPicked)
                        cloudBurst.Burst(boardView.GridToWorld(cx, cy));
                }

                if (changeDelayUnderClouds > 0f)
                    yield return new WaitForSeconds(changeDelayUnderClouds);

                yield return boardInput.ConvertCellsToPanelRoutine(
                    scatterPicked, panelIndex, scatterCreated, effect.overwritesBoxes);

                // 덮어쓸 수 없는 칸(구멍)만 뽑혔으면 <b>헛발</b>이다 - 강화도 연쇄도 없다.
                // (첫 사이클만 생기는 일이다. 뻗을 때는 덮어쓸 수 있는 칸만 후보로 고른다.)
                if (scatterCreated.Count == 0)
                    yield break;

                // 이번에 만든 것이 다음 사이클의 뿌리 끝이 된다.
                scatterTips.Clear();
                scatterTips.AddRange(scatterCreated);

                // <b>판정을 먼저 다 끝내고 나서 강화한다.</b> 하나 강화하고 다음을 보면, 방금
                // 강화한 칸이 이웃인 블록은 탐지에서 빠져 <b>순서에 따라 결과가 달라진다</b>.
                scatterQualified.Clear();
                foreach (var (cx, cy) in scatterCreated)
                {
                    if (boardInput.HasPlainOwnNeighbor(cx, cy, panelIndex))
                        scatterQualified.Add((cx, cy));
                }

                if (scatterQualified.Count == 0)
                    yield break;   // 근처에 (강화 안 된) 자기 조각이 없다 - 여기서 끝

                boardInput.EmpowerCells(scatterQualified, effect.empowerMultiplier);

                if (scatterCycleInterval > 0f)
                    yield return new WaitForSeconds(scatterCycleInterval);
            }
        }

        /// <summary>
        /// <b>루바니아의 검은 파동!</b>(2026-08-30 사용자 기획). 무작위 열과 행을 쓸어버리고
        /// 그 자리를 자기 색으로 채운다. 한 번에 끝나는 스킬이라 사이클이 없다.
        ///
        /// 바뀐 칸은 <b>잠깐 미안착</b>이라(ConvertCellsToPanelRoutine) 곧바로 터지지 않는다 -
        /// 그 틈에 파트너 스킬로 강화하거나 조각을 더 이어 붙일 수 있다(사용자 확정).
        /// </summary>
        private IEnumerator RunCrossWipe(SkillEffect effect, int panelIndex)
        {
            boardInput.PickWipeLines(effect.wipeColumns, effect.wipeRows, wipeLines);
            if (wipeLines.Count == 0)
                yield break;

            wipedCells.Clear();

            for (int i = 0; i < wipeLines.Count; i++)
            {
                var (vertical, index) = wipeLines[i];
                boardInput.CollectLine(vertical, index, scatterPicked);

                // 구름은 <b>줄 전체</b>에 피운다 - 교차점이 이미 처리됐어도 한 줄이 통째로
                // 쓸린 것처럼 보여야 한다.
                if (cloudBurst != null && boardView != null)
                {
                    foreach (var (cx, cy) in scatterPicked)
                        cloudBurst.Burst(boardView.GridToWorld(cx, cy));
                }

                if (changeDelayUnderClouds > 0f)
                    yield return new WaitForSeconds(changeDelayUnderClouds);

                yield return boardInput.WipeCellsToPanelRoutine(scatterPicked, panelIndex, wipedCells);

                // 다음 타 전에 한 박자 쉰다 - 안 쉬면 두 번 때린 게 한 번으로 뭉쳐 보인다.
                if (i < wipeLines.Count - 1 && wipeStrikeInterval > 0f)
                    yield return new WaitForSeconds(wipeStrikeInterval);
            }
        }

        // 이번 발동에 때릴 줄들과, 이미 때린 칸(교차점을 두 번 세지 않으려고).
        private readonly List<(bool vertical, int index)> wipeLines = new List<(bool vertical, int index)>();
        private readonly HashSet<(int x, int y)> wipedCells = new HashSet<(int x, int y)>();

        /// <summary>
        /// <b>미스틱의 포지셔닝</b>(2026-08-30 사용자 기획). 무작위 정사각 구역을 특수 패널로 박는다.
        /// 한 번에 끝나는 스킬이라 사이클이 없다 - 구름을 피우고, 그 아래에서 바꾼다.
        /// </summary>
        private IEnumerator RunSpecialAnchor(SkillEffect effect, int panelIndex)
        {
            // <b>자리부터 잡는다</b> - 낙하·리필이 도는 칸을 골랐다가는 2x2 가 조각난다
            // (2026-08-30 사용자 신고). 자리가 날 때까지 잠깐 기다린다.
            yield return boardInput.WaitForPlaceableSquare(Mathf.Max(1, effect.specialSize), effect.placementStyle, scatterPicked);
            if (scatterPicked.Count == 0)
                yield break;

            if (cloudBurst != null && boardView != null)
            {
                foreach (var (cx, cy) in scatterPicked)
                    cloudBurst.Burst(boardView.GridToWorld(cx, cy));
            }

            if (changeDelayUnderClouds > 0f)
                yield return new WaitForSeconds(changeDelayUnderClouds);

            yield return boardInput.MakeSpecialPanelsRoutine(
                scatterPicked, panelIndex, Mathf.Max(1, effect.specialMatches));
        }

        /// <summary>
        /// 사이클 상한을 안 정했을 때 쓰는 <b>마지막 안전선</b>. 실제로는 "더 바꿀 칸이 없다"에서
        /// 먼저 끝나므로 여기까지 오지 않는다 - 그래도 없으면 한 번의 실수가 게임을 멈춘다.
        /// </summary>
        private const int AbsoluteScatterCycleLimit = 300;

        /// <summary>
        /// <b>유나의 버닝 트랙!</b>(2026-09-01 사용자 기획). 맨 아랫줄에 점화 블록을 놓는다.
        ///
        /// <b>연출이 하는 일은 여기까지다</b> - 어느 열을 태울지는 스킬이 아니라 플레이어가
        /// 정한다. 그래서 다른 스킬과 달리 데미지도 여기서 나가지 않는다.
        /// </summary>
        private IEnumerator RunBurnTrack(SkillEffect effect)
        {
            boardInput.PickBurnTrackCells(Mathf.Max(1, effect.burnBlocks), effect.placementStyle, scatterPicked);
            if (scatterPicked.Count == 0)
                yield break;

            if (cloudBurst != null && boardView != null)
            {
                foreach (var (cx, cy) in scatterPicked)
                    cloudBurst.Burst(boardView.GridToWorld(cx, cy));
            }

            if (changeDelayUnderClouds > 0f)
                yield return new WaitForSeconds(changeDelayUnderClouds);

            yield return boardInput.PlaceBurnTracksRoutine(scatterPicked);
        }
    }
}

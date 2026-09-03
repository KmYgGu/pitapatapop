namespace JojoPuzzle.Core
{
    /// <summary>적의 방해가 <b>언제</b> 걸리는지.</summary>
    public enum HarassTriggerKind
    {
        /// <summary>매치로 지운 조각이 value 개 쌓일 때마다. <b>반복</b>.</summary>
        MatchedPieces = 0,

        /// <summary>적 체력이 value 비율(0~1) 아래로 처음 내려갈 때. <b>1회</b>.</summary>
        EnemyHealthBelow = 1,

        /// <summary>남은 시간이 value 초 아래로 처음 내려갈 때. <b>1회</b>.</summary>
        RemainingTimeBelow = 2,

        /// <summary>배틀이 시작되고 value 초마다. <b>반복</b>.</summary>
        EverySeconds = 3,
    }

    /// <summary>적의 방해가 <b>무엇을</b> 하는지(2026-08-25 사용자 기획).</summary>
    public enum HarassEffectKind
    {
        /// <summary>블록 변경 - 무작위 칸 하나를 다른 색으로 바꾼다. 가장 가벼운 방해.</summary>
        Recolor = 0,

        /// <summary>오자마 패널 - 매치도 이동도 안 되는 방해블록을 놓는다.</summary>
        Obstacle = 1,

        /// <summary>구멍 - 그 칸을 일정 시간 벽으로 만든다(위 조각도 통과 못 한다).</summary>
        Hole = 2,
    }

    /// <summary>
    /// 방해 하나의 설정. "언제(<see cref="kind"/>·<see cref="value"/>) 무엇을(<see cref="effect"/>)".
    ///
    /// <b>스테이지 애셋이 이 목록을 갖는다</b>(<see cref="StageDefinition.harassTriggers"/>) -
    /// 스테이지마다 난이도가 다른 게 당연하고, 그걸 배틀 씬 인스펙터에 박아두면 스테이지를
    /// 늘릴 때마다 씬을 고쳐야 한다. 그래서 적 체력·제한시간과 같은 자리에 뒀다.
    ///
    /// 이 클래스가 <c>Battle</c> 이 아니라 <c>Core</c> 에 있는 이유도 같다 - 스테이지 데이터가
    /// 참조해야 하므로 데이터 쪽에 있어야 한다.
    /// </summary>
    [System.Serializable]
    public class HarassTrigger
    {
        public HarassTriggerKind kind;

        [UnityEngine.Tooltip("종류에 따라 뜻이 다르다 - 조각 수 / 체력 비율(0~1) / 남은 초 / 간격(초).")]
        public float value = 20f;

        [UnityEngine.Tooltip("이 방해가 무엇을 할지. 블록 변경이 가장 가볍고 구멍이 가장 아프다.")]
        public HarassEffectKind effect = HarassEffectKind.Recolor;

        // 아래 둘은 런타임 상태라 인스펙터에 저장하지 않는다(배틀마다 새로 시작해야 한다).
        [System.NonSerialized] public bool fired;        // 1회짜리가 이미 터졌는지
        [System.NonSerialized] public float progress;    // 반복짜리가 얼마나 쌓였는지
    }
}

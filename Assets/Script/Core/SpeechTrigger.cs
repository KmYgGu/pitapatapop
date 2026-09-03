namespace JojoPuzzle.Core
{
    /// <summary>
    /// 캐릭터가 대사를 할 "상황". 게임 코드는 <b>무슨 대사인지가 아니라 무슨 상황인지만</b> 알린다
    /// (SpeechDirector.Play / TryReport). 그래서 대사를 고치거나 늘려도 게임 코드는 바뀌지 않는다.
    ///
    /// 상황을 추가하려면 여기 항목 하나를 늘리고 캐릭터의 CharacterSpeechSet에 줄을 추가하면 된다.
    /// 대사가 없는 상황은 그냥 아무 일도 일어나지 않는다(대사창이 안 뜬다).
    /// </summary>
    public enum SpeechTrigger
    {
        None = 0,

        BattleStart,      // 배틀 시작
        BossAppear,       // 보스 등장

        SkillActivate,    // 스킬 발동 - 스킬 시퀀스 1단계
        StandUpStart,     // 스탠드업 타임 개시
        StandUpFinish,    // 스탠드업 종료(큰 한 방을 꽂기 직전)

        BigMatch,         // 큰 매치를 만들었을 때 (가벼운 감탄 - 없어도 그만)
        LowTime,          // 제한시간이 얼마 안 남았을 때

        // ⚠ 아래 둘은 <b>플레이어 기준</b>의 상황 이름이다(2026-08-28 사용자 정정).
        //   Victory = 플레이어가 이겼다 -> 그 자리에서 말하는 건 <b>아군</b>(승리 화면)
        //   Defeat  = 플레이어가 졌다   -> 그 자리에서 말하는 건 <b>적</b>(패배 화면)
        // "말하는 사람 기준"이 아니다. 대사집도 그렇게 쓰여 있다 - Defeat 줄은 전부
        // "소녀에게 패배하셨군요?" 처럼 <b>진 사람에게 건네는 말</b>이다.
        // 예전에 이걸 거꾸로 읽어서 패배 화면이 적의 Victory 줄을 띄우고 있었다.
        Victory,
        Defeat,

        // ------------------------------------------------------------------
        // 미니게임 - 인디언 포커(2026-09-02 사용자 기획).
        // <b>항목을 뒤에 붙인다</b> - 값이 애션에 숫자로 저장되어 있어서
        // 중간에 끼우면 이미 써둔 대사의 상황이 통째로 밀린다.
        //
        // "대사가 중요하다"는 기획이라 한 판의 마디마다 자리를 만들어둔다.
        // 빈 자리는 그냥 조용히 넘어간다(대사가 없으면 아무 일도 안 일어난다).
        // ------------------------------------------------------------------

        /// <summary>미니게임에 들어와 마주 앉았을 때.</summary>
        MiniGameStart,

        /// <summary>패를 받고 <b>플레이어 패가 낮아</b> 해볼 만하다고 보았을 때.</summary>
        PokerConfident,

        /// <summary>패를 받고 <b>플레이어 패가 높아</b> 불리하다고 보았을 때.</summary>
        PokerWorried,

        /// <summary>플레이어의 베팅을 그대로 받을 때(콜).</summary>
        PokerCall,

        /// <summary>손에 믿는 구석이 있어 지를 때(레이즈).</summary>
        PokerRaise,

        /// <summary>
        /// <b>허세</b>로 질 때. 정직한 캐릭터일수록 이 자리에 오는 일이 드물다 -
        /// 그것만으로도 누가 무서운 상대인지가 드러난다.
        /// </summary>
        PokerBluff,

        /// <summary>접을 때(다이).</summary>
        PokerFold,

        /// <summary>한 판을 이겼을 때.</summary>
        PokerWin,

        /// <summary>한 판을 졌을 때.</summary>
        PokerLose,

        /// <summary>가장 낮은 패(1)로 뒤집어 이겼을 때 - 판돈이 두 배가 된다.</summary>
        PokerLowCardWin,

        /// <summary>소지금이 바닥나서 더 못 할 때.</summary>
        PokerBroke,

        /// <summary>미니게임을 그만두고 나갈 때.</summary>
        MiniGameEnd,

        // ------------------------------------------------------------------
        // 블랙잭(2026-09-02). 포커와 줄을 섮지 않는다 - 같은 "이겼다"라도
        // 허세를 부리는 판과 숫자를 밀어붙이는 판은 할 말이 다르기 때문이다.
        // ------------------------------------------------------------------

        /// <summary>한 장 더 받으며.</summary>
        BlackjackHit,

        /// <summary>여기서 멈추며.</summary>
        BlackjackStand,

        /// <summary>21을 넘겨버렸을 때.</summary>
        BlackjackBust,

        /// <summary>딱 21을 만들었을 때.</summary>
        BlackjackPerfect,

        /// <summary>한 판을 이겼을 때.</summary>
        BlackjackWin,

        /// <summary>한 판을 졌을 때.</summary>
        BlackjackLose,

        // ------------------------------------------------------------------
        // 도둑잡기(2026-09-02). 조커를 든 쪽이 밀고 상대가 집는 놀이라,
        // <b>밀 때와 집을 때의 말이 다르다</b>.
        // ------------------------------------------------------------------

        /// <summary>카드를 밀어 올리며 권할 때.</summary>
        OldMaidOffer,

        /// <summary>플레이어가 조커를 집어 <b>넘기는 데 성공했을</b> 때.</summary>
        OldMaidPassed,

        /// <summary>플레이어가 안전패를 집어 <b>조커를 든 채 졌을</b> 때.</summary>
        OldMaidLose,

        /// <summary>캐릭터가 집었는데 <b>조커였을</b> 때.</summary>
        OldMaidDrewJoker,

        /// <summary>캐릭터가 안전패를 집어 <b>이겼을</b> 때.</summary>
        OldMaidWin
    }
}

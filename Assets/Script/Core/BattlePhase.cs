namespace JojoPuzzle.Core
{
    /// <summary>
    /// 한 판이 지나가는 <b>단계</b>. 앞에서 뒤로만 흐르고 <b>한 번에 하나</b>다.
    ///
    /// <code>
    ///   Intro → Playing → Ending → (RushTime →) Finished
    /// </code>
    ///
    /// <b>왜 열거형인가</b>(2026-08-28 사용자 지적): 예전에는 이 다섯이 전부 따로 노는 bool
    /// 이었다(<c>IsIntroPlaying</c> / <c>IsFinishing</c> / <c>IsRushTimeActive</c> /
    /// <c>IsBattleEnded</c>). 서로 배타적인 값인데 따로 두니 묻는 쪽마다
    /// <c>IsRushTimeActive || IsFinishing || IsBattleEnded</c> 같은 줄이 생겼고,
    /// <b>새 단계가 생길 때마다 그 줄들을 전부 찾아 고쳐야 했다</b> - 실제로 한 군데를 빠뜨려서
    /// 러시 안내 위로 스탠드업 배너가 덮치는 버그가 났다.
    ///
    /// <b>가림막과는 다른 축이다.</b> 대사창·암전·일시정지·스탠드업 배너처럼 <b>잠깐 겹쳤다
    /// 사라지는</b> 것들은 단계가 아니라 따로 본다(그것들은 서로 겹칠 수 있다).
    /// </summary>
    public enum BattlePhase
    {
        /// <summary>시작 연출. 판은 다 준비됐지만 시계가 아직 안 돈다.</summary>
        Intro,

        /// <summary><b>평소.</b> 여기서만 스탠드업 게이지가 차고 스킬을 쓸 수 있고 상자가 생긴다.</summary>
        Playing,

        /// <summary>
        /// 종료 처리. 타임오버 띠 → 판 다시 채우기 → 마무리 처리 → 러시 개시 띠까지.
        /// 조작은 막히지만 <b>데미지는 아직 통한다</b>(마무리로 역전할 수 있어야 한다).
        /// </summary>
        Ending,

        /// <summary>클리어 보너스 구간. 다시 조작할 수 있지만 스킬·스탠드업·상자는 없다.</summary>
        RushTime,

        /// <summary>승패가 확정된 뒤. 결과 화면들이 도는 동안.</summary>
        Finished
    }
}

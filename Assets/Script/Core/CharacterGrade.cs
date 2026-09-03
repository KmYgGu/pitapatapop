namespace JojoPuzzle.Core
{
    /// <summary>
    /// 캐릭터(퍼즐 조각) 등급. 같은 레벨이라도 등급에 따라 전투력이 다르다
    /// (CharacterGrowthTable 참고 - 레벨 1에서 GR 150 / SR 110 / BR 80으로 시작해
    ///  레벨 50에서 1800 / 1100 / 800으로 끝남).
    /// 열거형 순서는 강한 쪽부터 - 정렬이나 비교에 그대로 쓸 수 있게.
    /// </summary>
    public enum CharacterGrade
    {
        GR = 0,
        SR = 1,
        BR = 2
    }
}

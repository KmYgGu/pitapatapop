namespace JojoPuzzle.Core
{
    /// <summary>
    /// 퍼즐 조각 프레임(배경) 16색. 그리드 이미지의 읽기 순서(왼→오, 위→아래)와 동일한 인덱스.
    /// 0~7은 캐릭터에 배정되는 기본색, 8~15는 팔레트 스왑 전용 색(= 기본색 인덱스 + 8)으로
    /// 편성한 리더/파트너가 같은 기본색일 때 파트너 쪽을 구분해 보여주기 위해서만 쓰인다.
    /// </summary>
    public enum PanelFrameColor
    {
        Yellow = 0,
        YellowGreen,
        Purple,
        Pink,
        Blue,
        White,
        Red,
        Black,
        Brown,
        DarkGreen,
        LightPurple,
        Orange,
        Cyan,
        Gray,
        Maroon,
        Rainbow
    }
}

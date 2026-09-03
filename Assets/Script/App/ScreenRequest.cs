namespace JojoPuzzle.App
{
    /// <summary>
    /// 씬을 바꾸면서 "열리자마자 이 화면부터 보여줘"라고 부탁하는 자리.
    ///
    /// <b>왜 필요한가</b>: 편성 화면은 스테이지 선택 씬 안의 패널인데, 아파트의 "편성" 버튼은
    /// 챕터 목록을 거치지 않고 곧바로 그리로 가야 한다. 씬이 갈리므로 static 으로 넘긴다.
    ///
    /// <b>한 번 쓰고 지운다</b>(<see cref="ConsumeOpenFormation"/>) - 안 지우면 그 뒤로 스테이지
    /// 선택에 들어갈 때마다 편성이 먼저 뜬다.
    /// </summary>
    public static class ScreenRequest
    {
        public static bool OpenFormationDirectly { get; set; }

        /// <summary>요청이 있었는지 확인하고 <b>동시에 지운다</b>.</summary>
        public static bool ConsumeOpenFormation()
        {
            bool value = OpenFormationDirectly;
            OpenFormationDirectly = false;
            return value;
        }

        /// <summary>
        /// 스테이지 선택에 들어가면 <b>이 챕터부터</b> 열어달라는 요청.
        ///
        /// 배틀을 끝내고 아파트로 돌아왔을 때, 거기서 "스테이지 입장"을 누르면 챕터 목록을
        /// 거치지 않고 방금 하던 챕터로 곧바로 가야 한다(2026-08-25 사용자 지시).
        /// 방금 한 판을 이어서 하는 게 압도적으로 흔한 흐름이라 그렇다.
        /// </summary>
        public static Core.ChapterDefinition ResumeChapter { get; set; }

        /// <summary>요청을 확인하고 <b>동시에 지운다</b>. 안 지우면 그다음부터 계속 그 챕터로 끌려간다.</summary>
        public static Core.ChapterDefinition ConsumeResumeChapter()
        {
            var value = ResumeChapter;
            ResumeChapter = null;
            return value;
        }

        /// <summary>
        /// 아파트에 들어가면 <b>이 방 화면부터</b> 열어달라는 요청. 음수면 요청 없음.
        ///
        /// 미니게임을 끝내고 돌아왔을 때 쓴다 - 방에서 나갔는데 아파트 전체 화면으로
        /// 떨어지면 "방에서 잠긐 놀다 온" 흐름이 끊긴다.
        /// </summary>
        public static int OpenRoomIndex { get; set; } = -1;

        /// <summary>요청을 확인하고 <b>동시에 지운다</b>. 안 지우면 아파트에 올 때마다 그 방이 열린다.</summary>
        public static int ConsumeOpenRoom()
        {
            int value = OpenRoomIndex;
            OpenRoomIndex = -1;
            return value;
        }
    }
}

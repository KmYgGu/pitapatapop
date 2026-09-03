using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 이번 판에 데리고 갈 리더와 파트너. 편성 화면이 생기면 그 화면이 여기에 써 넣는다.
    ///
    /// <b>왜 static 인가</b>: 준비 화면과 배틀이 서로 다른 씬인데 같은 편성을 봐야 한다.
    /// 편성 화면이 아직 없어서 지금은 비어 있고, 비어 있으면 각 화면이 자기 기본값
    /// (배틀은 <c>GameEntryPoint.partyPanels</c>, 준비 화면은 인스펙터 칸)으로 물러선다.
    ///
    /// <b>팔레트 색 인덱스 = 편성 순서</b>라는 기존 계약이 있으므로 리더가 0, 파트너가 1이다.
    /// </summary>
    public static class PartySelection
    {
        public static PanelType Leader { get; private set; }

        public static PanelType Partner { get; private set; }

        public static bool HasParty => Leader != null && Partner != null;

        public static void Set(PanelType leader, PanelType partner)
        {
            Leader = leader;
            Partner = partner;
        }

        /// <summary>
        /// 담보로 잡히는 등으로 <b>못 쓰게 된 캐릭터</b>를 자리에서 뺀다.
        /// 안 빼면 목록에는 없는데 편성에는 남아 그대로 전투에 나간다.
        /// </summary>
        public static void Release(PanelType character)
        {
            if (character == null)
                return;

            if (ReferenceEquals(Leader, character))
                Leader = null;

            if (ReferenceEquals(Partner, character))
                Partner = null;
        }

        public static void Clear()
        {
            Leader = null;
            Partner = null;
        }

        /// <summary>리더 + 파트너 전투력 합. 준비 화면의 "종합 전투력".</summary>
        public static int GetTotalCombatPower(PanelType fallbackLeader, PanelType fallbackPartner)
        {
            var leader = Leader != null ? Leader : fallbackLeader;
            var partner = Partner != null ? Partner : fallbackPartner;

            int total = 0;
            if (leader != null) total += leader.CombatPower;
            if (partner != null) total += partner.CombatPower;
            return total;
        }
    }
}

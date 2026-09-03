using System;
using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 캐릭터를 <b>언제 얻었는지</b>. 편성 화면의 "획득순"과 "며칠 경과" 표시가 쓴다.
    ///
    /// <b>왜 PanelType 에 넣지 않는가</b>: `PanelType` 은 캐릭터 도감 데이터인데 거기에 이미
    /// level·grade·exp 같은 유저 상태가 섞여 있는 게 이 프로젝트의 가장 큰 부채로 적혀 있다.
    /// 획득 시각은 명백히 유저 상태라 같은 실수를 반복하지 않는다.
    ///
    /// <b>지금은 저장되지 않는다.</b> 세이브가 생기면 여기에 실제 값을 넣어주면 되고,
    /// 화면 코드는 안 고쳐도 된다.
    /// </summary>
    public static class PlayerCollection
    {
        private static readonly Dictionary<PanelType, DateTime> acquiredUtc =
            new Dictionary<PanelType, DateTime>();

        /// <summary>실제 획득 시각을 기록한다. 세이브를 불러올 때 부르면 된다.</summary>
        public static void SetAcquired(PanelType character, DateTime utc)
        {
            if (character == null)
                return;

            acquiredUtc[character] = utc;
        }

        public static bool TryGetAcquired(PanelType character, out DateTime utc)
        {
            utc = default;
            return character != null && acquiredUtc.TryGetValue(character, out utc);
        }

        /// <summary>
        /// 획득 시각. 기록이 없으면 <b>보유 목록 순서로 지어낸 값</b>을 돌려준다 -
        /// 목록에 먼저 들어 있는 캐릭터가 먼저 얻은 것이라는 가정이다.
        ///
        /// <b>이건 진짜 값이 아니다.</b> 세이브가 생기기 전까지 화면이 빈 칸으로 보이지 않게
        /// 하는 임시 조치이고, <see cref="SetAcquired"/> 로 실제 값을 넣으면 그쪽이 이긴다.
        /// </summary>
        public static DateTime GetAcquired(PanelType character, int rosterIndex, int rosterCount, DateTime utcNow)
        {
            if (TryGetAcquired(character, out DateTime real))
                return real;

            // 목록 앞쪽일수록 오래 전에 얻은 것으로 친다(0번이 가장 오래됨).
            int daysAgo = Math.Max(0, rosterCount - rosterIndex);
            return utcNow.AddDays(-daysAgo);
        }

        /// <summary>얻은 지 며칠 지났는지. 오늘 얻었으면 0.</summary>
        public static int GetDaysOwned(PanelType character, int rosterIndex, int rosterCount, DateTime utcNow)
        {
            DateTime when = GetAcquired(character, rosterIndex, rosterCount, utcNow);

            // 시각이 아니라 <b>날짜</b> 차이로 센다 - 몇 시에 얻었느냐로 "1일 전"이 갈리면 어색하다.
            int days = (int)(utcNow.Date - when.Date).TotalDays;
            return days < 0 ? 0 : days;
        }

        /// <summary>세이브가 없던 시절 값이 남지 않도록 비운다(계정 전환 등).</summary>
        public static void Clear() => acquiredUtc.Clear();
    }
}

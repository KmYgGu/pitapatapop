using System;
using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// <b>캐릭터를 담보로 보석을 빌린다</b>(2026-09-02 사용자 기획).
    ///
    /// <code>
    ///   가장 강한 캐릭터를 맡긴다 → 전투력에 비례한 보석을 받는다
    ///   기한 안에 갚으면          → 캐릭터가 돌아온다
    ///   기한을 넘기면            → 캐릭터는 은행 감옥에 갇힌다. 꺼내려면 빌린 것의 1.5배
    /// </code>
    ///
    /// ⭐⭐ <b>가장 강한 캐릭터만 맡길 수 있다</b>(사용자 확정). 아무나 맡길 수 있으면
    /// <b>약한 캐릭터를 마구 맡기고 안 갚는다</b> - 그러면 담보가 담보가 아니게 된다.
    ///
    /// ⭐ <b>맡긴 캐릭터는 어디서도 쓸 수 없고 아파트에서도 사라진다.</b> 은행 감옥에서만 보인다.
    /// 그래서 판정을 여기 <see cref="IsLocked"/> 하나로 모았다 - 화면마다 따로 판단하면
    /// 어느 한 곳이 빠져서 <b>담보로 잡힌 캐릭터가 전투에 나가는</b> 구멍이 난다.
    ///
    /// <b>⚠ 저장되지 않는다</b>(이 프로젝트의 모든 유저 상태와 같다).
    /// </summary>
    public static class BankLoan
    {
        /// <summary>맡겨 둔 캐릭터. 없으면 null.</summary>
        public static PanelType Collateral { get; private set; }

        /// <summary>빌린 보석.</summary>
        public static int Borrowed { get; private set; }

        /// <summary>갚아야 하는 시각.</summary>
        public static DateTime DueAt { get; private set; }

        /// <summary>기한을 넘겨 <b>감옥에 갇혔는지</b>. 이 뒤로는 1.5배를 내야 꺼낸다.</summary>
        public static bool IsSeized { get; private set; }

        /// <summary>빌리거나 갚았다. 화면들이 다시 그린다.</summary>
        public static event Action OnChanged;

        /// <summary>마지막으로 갚은 시각. 쿨타임을 여기서 잰다. 한 번도 안 빌렸으면 <c>MinValue</c>.</summary>
        public static DateTime RepaidAt { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// ⭐ <b>갚은 뒤 한동안은 다시 못 빌린다</b>(2026-09-03 사용자 지시: "돈을 빌리는 건
        /// 장난이 아니니까"). 갚자마자 또 맡기면 담보가 그냥 환전 창구가 된다.
        /// </summary>
        public static TimeSpan Cooldown(DateTime now, float hours)
        {
            if (RepaidAt == DateTime.MinValue || hours <= 0f)
                return TimeSpan.Zero;

            TimeSpan left = RepaidAt.AddHours(hours) - now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        public static bool CanBorrow(DateTime now, float cooldownHours)
            => !HasLoan && Cooldown(now, cooldownHours) == TimeSpan.Zero;

        public static bool HasLoan => Collateral != null;

        /// <summary>
        /// 그 캐릭터가 <b>지금 묶여 있는지</b>. 아파트·편성·전투가 전부 이걸 본다.
        /// </summary>
        public static bool IsLocked(PanelType character)
            => character != null && ReferenceEquals(character, Collateral);

        /// <summary>기한까지 남은 시간. 이미 넘겼으면 0.</summary>
        public static TimeSpan TimeLeft(DateTime now)
        {
            TimeSpan left = DueAt - now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>
        /// 기한을 넘겼으면 압류한다. <b>화면이 열릴 때마다 불러 주면 된다</b> -
        /// 시계를 도는 물건을 따로 두지 않는다(<see cref="HeartMeter"/> 와 같은 방침).
        /// </summary>
        public static void Tick(DateTime now)
        {
            if (!HasLoan || IsSeized || now < DueAt)
                return;

            IsSeized = true;
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 담보로 잡을 수 있는 <b>단 하나</b>의 캐릭터 - 가진 것 중 전투력이 가장 높은 쪽.
        /// </summary>
        public static PanelType FindStrongest(IReadOnlyList<PanelType> owned)
        {
            if (owned == null)
                return null;

            PanelType best = null;
            for (int i = 0; i < owned.Count; i++)
            {
                var c = owned[i];
                if (c == null)
                    continue;

                if (best == null || c.CombatPower > best.CombatPower)
                    best = c;
            }

            return best;
        }

        /// <summary>전투력에 비례한 한도.</summary>
        public static int MaxLoan(PanelType character, float gemsPerPower)
            => character == null ? 0 : Math.Max(0, (int)(character.CombatPower * gemsPerPower));

        /// <summary>
        /// 갚을 기한. <b>많이 빌릴수록 길다</b> - 큰 빚에 짧은 기한까지 얹으면 두 번 벌하는 셈이다.
        /// </summary>
        public static float TermHours(int gems, float baseHours, float gemsPerHour, float maxHours)
        {
            float hours = baseHours + (gemsPerHour > 0.0001f ? gems / gemsPerHour : 0f);
            return Math.Min(hours, maxHours);
        }

        /// <summary>빌린다. 이미 빌린 게 있으면 거절한다.</summary>
        public static bool TryBorrow(PanelType character, int gems, float hours, DateTime now)
        {
            if (HasLoan || character == null || gems <= 0)
                return false;

            Collateral = character;
            Borrowed = gems;
            DueAt = now.AddHours(hours);
            IsSeized = false;

            PlayerProfile.Gems += gems;

            // ⚠ 편성에 들어가 있으면 빼낸다 - 목록에서만 지우면 <b>이미 편성된 캐릭터가
            // 그대로 전투에 나간다</b>.
            PartySelection.Release(character);

            OnChanged?.Invoke();
            return true;
        }

        /// <summary>지금 갚아야 하는 금액. 감옥에 갇힌 뒤에는 1.5배다.</summary>
        public static int AmountDue(float rescueMultiplier)
            => IsSeized ? (int)Math.Ceiling(Borrowed * (double)rescueMultiplier) : Borrowed;

        /// <summary>
        /// 갚고 캐릭터를 되찾는다. 감옥에 갇힌 뒤라면 <paramref name="rescueMultiplier"/> 배를 낸다.
        /// </summary>
        public static bool TryRepay(float rescueMultiplier)
        {
            if (!HasLoan)
                return false;

            int due = AmountDue(rescueMultiplier);
            if (!PlayerProfile.TrySpendGems(due))
                return false;

            Collateral = null;
            Borrowed = 0;
            IsSeized = false;
            RepaidAt = DateTime.UtcNow;

            OnChanged?.Invoke();
            return true;
        }

        public static void Clear()
        {
            if (!HasLoan)
                return;

            Collateral = null;
            Borrowed = 0;
            IsSeized = false;
            OnChanged?.Invoke();
        }
    }
}

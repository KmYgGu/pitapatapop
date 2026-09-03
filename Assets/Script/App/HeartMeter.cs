using System;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 스테이지 입장에 쓰는 하트. <b>최대 5개, 1개 차는 데 10분</b>(2026-08-24 기획).
    ///
    /// <b>개수를 세는 게 아니라 시각을 들고 있는다.</b> 매 초 타이머를 굴려 개수를 올리는 방식이면
    /// 앱을 껐다 켠 동안 흐른 시간이 통째로 사라진다. 여기서는 "마지막 충전이 시작된 시각"만
    /// 기억하고, 물어볼 때마다 그 사이 흐른 시간으로 계산한다 - 앱이 꺼져 있었든 화면이 멈춰
    /// 있었든 결과가 같다. 나중에 서버 세이브가 생기면 <see cref="RechargeStartedUtc"/> 하나만
    /// 저장하면 된다.
    ///
    /// UnityEngine 을 참조하지 않는 순수 로직이다(이 프로젝트의 로직/뷰 분리 원칙).
    /// </summary>
    public class HeartMeter
    {
        public const int MaxHearts = 5;

        /// <summary>1개를 채우는 데 걸리는 시간. 기획값 10분.</summary>
        public static readonly TimeSpan RechargeInterval = TimeSpan.FromMinutes(10);

        private int count;

        /// <summary>
        /// 지금 채워지고 있는 하트의 충전이 시작된 시각(UTC). 가득 찼을 때는 의미가 없다.
        /// </summary>
        public DateTime RechargeStartedUtc { get; private set; }

        public HeartMeter(int startCount = MaxHearts)
        {
            count = Clamp(startCount);
            RechargeStartedUtc = DateTime.UtcNow;
        }

        /// <summary>지금 시각 기준 하트 개수. 물어보는 순간 밀린 충전이 함께 반영된다.</summary>
        public int GetCount(DateTime utcNow)
        {
            Advance(utcNow);
            return count;
        }

        /// <summary>
        /// 다음 하트까지 남은 시간. 가득 찼으면 <see cref="TimeSpan.Zero"/>.
        /// </summary>
        public TimeSpan GetTimeToNext(DateTime utcNow)
        {
            Advance(utcNow);

            if (count >= MaxHearts)
                return TimeSpan.Zero;

            TimeSpan remaining = RechargeInterval - (utcNow - RechargeStartedUtc);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>하트를 쓴다. 모자라면 아무것도 하지 않고 false.</summary>
        public bool TrySpend(DateTime utcNow, int amount = 1)
        {
            Advance(utcNow);

            if (amount <= 0 || count < amount)
                return false;

            // 가득 찬 상태에서 처음 하나를 쓰는 순간이 곧 충전 시작이다. 그게 아니면
            // 이미 돌고 있는 충전을 그대로 이어간다(쓸 때마다 초기화하면 시간이 늘어난다).
            bool wasFull = count >= MaxHearts;
            count -= amount;

            if (wasFull)
                RechargeStartedUtc = utcNow;

            return true;
        }

        /// <summary>보상 등으로 하트를 더한다. 최대치를 넘지 않는다.</summary>
        public void Add(DateTime utcNow, int amount)
        {
            Advance(utcNow);
            count = Clamp(count + amount);

            if (count >= MaxHearts)
                RechargeStartedUtc = utcNow;
        }

        /// <summary>흐른 시간만큼 충전을 반영한다. 조회·소비 어느 쪽으로 들어와도 먼저 거친다.</summary>
        private void Advance(DateTime utcNow)
        {
            if (count >= MaxHearts)
            {
                // 가득 찼으면 시간이 쌓이지 않게 시각을 끌고 온다. 안 그러면 하나 쓰는 순간
                // 그동안 쌓인 시간이 한꺼번에 반영돼 즉시 다시 차버린다.
                RechargeStartedUtc = utcNow;
                return;
            }

            TimeSpan elapsed = utcNow - RechargeStartedUtc;
            if (elapsed < TimeSpan.Zero)
            {
                // 기기 시계가 뒤로 간 경우(사용자가 시간을 돌렸거나 서버 동기화). 앞당겨 주지 않고
                // 지금부터 다시 센다 - 시계를 돌려 하트를 얻는 걸 막는 최소한의 방어.
                RechargeStartedUtc = utcNow;
                return;
            }

            long gained = (long)(elapsed.Ticks / RechargeInterval.Ticks);
            if (gained <= 0)
                return;

            int before = count;
            count = Clamp(count + (int)Math.Min(gained, MaxHearts));

            if (count >= MaxHearts)
                RechargeStartedUtc = utcNow;
            else
                RechargeStartedUtc = RechargeStartedUtc.AddTicks((count - before) * RechargeInterval.Ticks);
        }

        private static int Clamp(int value) => value < 0 ? 0 : (value > MaxHearts ? MaxHearts : value);
    }
}

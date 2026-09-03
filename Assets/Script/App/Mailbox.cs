using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>우편 한 통이 주는 것 하나.</summary>
    public struct MailReward
    {
        public BattleItemKind kind;
        public int count;
    }

    /// <summary>우편 한 통.</summary>
    public class MailEntry
    {
        public string title = string.Empty;
        public string body = string.Empty;
        public readonly List<MailReward> rewards = new List<MailReward>();

        /// <summary>이미 받았는지. 받은 우편은 목록에서 사라진다.</summary>
        public bool Claimed { get; internal set; }
    }

    /// <summary>
    /// 우편함. <b>배틀 아이템을 골드 없이 나눠주는 첫 번째 경로</b>다(2026-08-28 사용자 기획) -
    /// 네 아이템을 다 사면 5,000골드인데 한 판에 100 남짓 버는 초반에는 살 수가 없어서,
    /// 현물을 주는 길이 따로 있어야 한다.
    ///
    /// <b>저장되지 않는다</b>(<see cref="PlayerInventory"/>·<see cref="PlayerProfile"/> 과 같은 방침).
    /// 씬을 다시 열면 안 받은 상태로 돌아온다 - 세이브가 생기면 <see cref="entries"/> 를
    /// 서버에서 받아 채우면 되고 화면 코드는 안 고쳐도 된다.
    ///
    /// <b>여기 규칙은 "받는다"뿐이다.</b> 무엇을 주는지는 <see cref="MailEntry.rewards"/> 가 들고
    /// 있고, 실제로 넣는 건 <see cref="PlayerInventory.Add"/> 한 곳이다 - 나중에 보물 상자나
    /// 시간 보상이 생겨도 같은 문으로 들어오면 개수가 어긋나지 않는다.
    /// </summary>
    public static class Mailbox
    {
        private static readonly List<MailEntry> entries = new List<MailEntry>();
        private static bool seeded;

        /// <summary>
        /// 지금 우편함에 든 편지들(안 받은 것만). 화면이 이걸 그대로 그린다.
        /// </summary>
        public static IReadOnlyList<MailEntry> Entries
        {
            get
            {
                EnsureSeeded();
                return entries;
            }
        }

        /// <summary>안 받은 우편 수. 아파트 버튼에 배지를 붙일 때 쓴다.</summary>
        public static int UnreadCount => Entries.Count;

        /// <summary>
        /// 한 통을 받는다. <b>받은 편지는 목록에서 빠진다</b> - 남겨두면 같은 걸 몇 번이고
        /// 받게 되고, 그걸 막으려고 화면에서 따로 표시를 관리하게 된다.
        /// </summary>
        /// <returns>실제로 받았으면 true(이미 받았거나 없는 편지면 false).</returns>
        public static bool Claim(MailEntry entry)
        {
            EnsureSeeded();

            if (entry == null || entry.Claimed || !entries.Contains(entry))
                return false;

            for (int i = 0; i < entry.rewards.Count; i++)
                PlayerInventory.Add(entry.rewards[i].kind, entry.rewards[i].count);

            entry.Claimed = true;
            entries.Remove(entry);
            return true;
        }

        /// <summary>전부 받는다.</summary>
        /// <returns>받은 편지 수.</returns>
        public static int ClaimAll()
        {
            EnsureSeeded();

            int claimed = 0;

            // 뒤에서부터 지운다 - 앞에서부터 지우면 인덱스가 밀린다.
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (Claim(entries[i]))
                    claimed++;
            }

            return claimed;
        }

        /// <summary>
        /// 첫 접근에 <b>임시 편지</b>를 한 통 채운다(2026-08-28 사용자 지시:
        /// "우편함을 열면 퍼즐 관련 세트 각각 2개씩").
        ///
        /// <b>여기 있는 건 임시값이다</b> - 실제로는 서버가 보내주거나 이벤트가 넣어줄 자리다.
        /// 개수(2)와 문구도 여기 한 곳에만 적혀 있어서 바꾸기 쉽다.
        /// </summary>
        private static void EnsureSeeded()
        {
            if (seeded)
                return;

            seeded = true;

            var welcome = new MailEntry
            {
                title = "퍼즐 아이템 세트",
                body = "배틀에 쓸 보조 아이템을 보내드립니다."
            };

            foreach (BattleItemKind kind in System.Enum.GetValues(typeof(BattleItemKind)))
                welcome.rewards.Add(new MailReward { kind = kind, count = SeedCountPerItem });

            entries.Add(welcome);
        }

        /// <summary>임시 편지가 종류마다 주는 개수.</summary>
        public const int SeedCountPerItem = 2;
    }
}

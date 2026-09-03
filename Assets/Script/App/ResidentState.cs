using System;
using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>두 입주자 사이가 어떤지. 숫자를 그대로 보여주지 않고 이 딱지로 읽힌다.</summary>
    public enum RelationLevel
    {
        Cold,      // 서먹함
        Neutral,   // 그저 그럼
        Friendly,  // 친함
        Close      // 각별함
    }

    /// <summary>
    /// 입주한 캐릭터의 <b>살아가는 값</b> - 포만도·기분·관계.
    /// 방 화면 상단과 정보 화면이 이걸 읽는다(2026-08-28 사용자 기획).
    ///
    /// <b>⚠ 지금은 전부 임시 고정값이다</b>(사용자 확정: "지금은 고정값으로 두고 표시만").
    /// 무엇으로 오르내릴지가 정해지면 <b>여기에 규칙을 넣고</b> 화면은 안 고치면 된다.
    /// 값을 바꾸는 통로(<see cref="SetSatiety"/> 등)는 미리 열어뒀다.
    ///
    /// <b>소지금은 여기 없다</b> - 그건 이미 <see cref="CharacterWallet"/> 이 갖고 있다.
    /// 같은 값을 두 곳에 두면 반드시 어긋난다.
    ///
    /// 저장되지 않는다(이 프로젝트의 모든 유저 상태와 같다).
    /// </summary>
    public static class ResidentState
    {
        /// <summary>값의 범위. 화면이 게이지를 그릴 때 쓴다.</summary>
        public const int Max = 100;

        /// <summary>기록이 없는 캐릭터가 보여줄 임시값.</summary>
        public const int DefaultSatiety = 72;
        public const int DefaultMood = 64;

        private static readonly Dictionary<PanelType, int> satiety = new Dictionary<PanelType, int>();
        private static readonly Dictionary<PanelType, int> mood = new Dictionary<PanelType, int>();
        private static readonly Dictionary<PanelType, DateTime> movedInUtc =
            new Dictionary<PanelType, DateTime>();

        // 두 캐릭터 사이의 값. <b>키를 한쪽으로 정렬해</b> (A,B)와 (B,A)가 같은 칸을 보게 한다 -
        // 안 그러면 방향에 따라 다른 답이 나온다.
        private static readonly Dictionary<(PanelType, PanelType), int> relation =
            new Dictionary<(PanelType, PanelType), int>();

        public static int GetSatiety(PanelType character)
            => character != null && satiety.TryGetValue(character, out int v) ? v : DefaultSatiety;

        public static void SetSatiety(PanelType character, int value)
        {
            if (character != null)
                satiety[character] = Math.Max(0, Math.Min(Max, value));
        }

        public static int GetMood(PanelType character)
            => character != null && mood.TryGetValue(character, out int v) ? v : DefaultMood;

        public static void SetMood(PanelType character, int value)
        {
            if (character != null)
                mood[character] = Math.Max(0, Math.Min(Max, value));
        }

        /// <summary>기분을 사람 말로. 게이지 옆에 적는다.</summary>
        public static string DescribeMood(PanelType character)
        {
            int value = GetMood(character);

            if (value >= 80) return "아주 좋음";
            if (value >= 60) return "좋음";
            if (value >= 40) return "그저 그럼";
            if (value >= 20) return "언짢음";
            return "많이 상함";
        }

        // ------------------------------------------------------------------ 입주 기간

        /// <summary>
        /// 그 캐릭터가 <b>처음 입주한 시각</b>을 적는다. 이미 적혀 있으면 덮어쓰지 않는다 -
        /// 방을 옮겨도 "이 아파트에 산 기간"은 이어져야 한다(사용자 기획의 '입주 기간').
        /// </summary>
        public static void NoteMovedIn(PanelType character, DateTime utcNow)
        {
            if (character != null && !movedInUtc.ContainsKey(character))
                movedInUtc[character] = utcNow;
        }

        /// <summary>입주한 지 얼마나 됐는지. 기록이 없으면 0.</summary>
        public static TimeSpan GetResidencyDuration(PanelType character, DateTime utcNow)
            => character != null && movedInUtc.TryGetValue(character, out var since)
                ? utcNow - since
                : TimeSpan.Zero;

        /// <summary>입주 기간을 사람 말로. 갓 들어왔으면 "오늘 입주".</summary>
        public static string DescribeResidency(PanelType character, DateTime utcNow)
        {
            var span = GetResidencyDuration(character, utcNow);

            if (span.TotalDays >= 1)
                return $"{(int)span.TotalDays}일째";

            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}시간째";

            return "오늘 입주";
        }

        // ------------------------------------------------------------------ 관계

        private static (PanelType, PanelType) Key(PanelType a, PanelType b)
        {
            // 어느 쪽을 앞에 둘지 <b>일정하게</b> 정하기만 하면 된다. 이름 순으로 가른다.
            return string.CompareOrdinal(a.name, b.name) <= 0 ? (a, b) : (b, a);
        }

        /// <summary>두 캐릭터 사이의 값(0~100). 기록이 없으면 그저 그런 값.</summary>
        public static int GetRelation(PanelType a, PanelType b)
        {
            if (a == null || b == null || a == b)
                return 50;

            return relation.TryGetValue(Key(a, b), out int v) ? v : 50;
        }

        public static void SetRelation(PanelType a, PanelType b, int value)
        {
            if (a == null || b == null || a == b)
                return;

            relation[Key(a, b)] = Math.Max(0, Math.Min(Max, value));
        }

        public static RelationLevel GetRelationLevel(PanelType a, PanelType b)
        {
            int value = GetRelation(a, b);

            if (value >= 80) return RelationLevel.Close;
            if (value >= 60) return RelationLevel.Friendly;
            if (value >= 35) return RelationLevel.Neutral;
            return RelationLevel.Cold;
        }

        public static string DescribeRelation(PanelType a, PanelType b)
        {
            switch (GetRelationLevel(a, b))
            {
                case RelationLevel.Close: return "각별함";
                case RelationLevel.Friendly: return "친함";
                case RelationLevel.Cold: return "서먹함";
                default: return "그저 그럼";
            }
        }

        // ------------------------------------------------------------------ 먹어본 음식

        // 캐릭터마다 <b>먹어본 음식 이름</b>. 좋아하는지 싫어하는지는 먹어봐야 알 수 있다
        // (2026-08-28 사용자 기획) - 그래서 "무엇을 먹었나"만 여기 쌓고, 그게 좋은 맛이었는지는
        // 그때그때 입맛으로 계산한다(FoodPreference). 취향을 여기 굳혀두면 입맛 표를 고쳤을 때
        // 이미 먹은 것만 옛 판정으로 남는다.
        private static readonly Dictionary<PanelType, HashSet<string>> eaten =
            new Dictionary<PanelType, HashSet<string>>();

        /// <summary>그 음식을 먹었다고 적는다. 먹이는 기능이 생기면 여기로 들어온다.</summary>
        public static void NoteAte(PanelType character, string foodName)
        {
            if (character == null || string.IsNullOrEmpty(foodName))
                return;

            if (!eaten.TryGetValue(character, out var set))
            {
                set = new HashSet<string>();
                eaten[character] = set;
            }

            set.Add(foodName);
        }

        public static bool HasEaten(PanelType character, string foodName)
            => character != null && !string.IsNullOrEmpty(foodName)
               && eaten.TryGetValue(character, out var set) && set.Contains(foodName);

        /// <summary>
        /// <b>먹어본 것 중에서</b> 좋아하는(또는 싫어하는) 음식을 점수 순으로 최대 <paramref name="max"/>개.
        ///
        /// <b>안 먹어본 음식은 나오지 않는다</b> - 화면은 그 자리를 "???" 로 채운다.
        /// 먹었어도 그저 그런 맛이면 어느 쪽에도 안 들어간다.
        /// </summary>
        public static void CollectTasted(PanelType character, FoodCatalog catalog,
            CharacterTasteTable table, bool likes, List<string> into, int max = 3)
        {
            into.Clear();

            if (character == null || catalog == null
                || !eaten.TryGetValue(character, out var set) || set.Count == 0)
                return;

            // 점수와 이름을 같이 모아 정렬한다. 먹어본 음식이 몇 개 안 되는 화면이라
            // 여기서 리스트를 하나 만드는 비용은 문제가 안 된다.
            var scored = new List<(string name, int score)>();

            for (int i = 0; i < catalog.Count; i++)
            {
                var food = catalog.Get(i);
                if (food == null || !set.Contains(food.displayName))
                    continue;

                int score = FoodPreference.Score(table, character, food);
                var opinion = FoodPreference.ToOpinion(score);

                if (likes && opinion != FoodOpinion.Liked)
                    continue;
                if (!likes && opinion != FoodOpinion.Disliked)
                    continue;

                scored.Add((food.displayName, score));
            }

            // 좋아하는 건 <b>가장 좋아하는 것부터</b>, 싫어하는 건 가장 싫어하는 것부터.
            scored.Sort((a, b) => likes ? b.score.CompareTo(a.score) : a.score.CompareTo(b.score));

            for (int i = 0; i < scored.Count && into.Count < max; i++)
                into.Add(scored[i].name);
        }

        /// <summary>전부 지운다. 세이브를 불러오기 전에 초기화하는 자리.</summary>
        public static void Clear()
        {
            satiety.Clear();
            mood.Clear();
            movedInUtc.Clear();
            relation.Clear();
            eaten.Clear();
        }
    }
}

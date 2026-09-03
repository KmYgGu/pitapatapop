using System;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>챕터의 종류. 스테이지 목록을 거치는지가 여기서 갈린다.</summary>
    public enum ChapterKind
    {
        /// <summary>보통 챕터. 스테이지 목록을 거쳐 고른다.</summary>
        Normal,

        /// <summary>특별 챕터. 목록 없이 곧바로 준비 화면으로 간다.</summary>
        Special,

        /// <summary>이벤트 챕터. 특별과 같고, 개최 기간이 있는 게 보통이다.</summary>
        Event
    }

    /// <summary>
    /// 챕터 하나. 스테이지 몇 개를 묶고, 목록에 보여줄 정보(권장 레벨·개최 기간)를 들고 있다.
    ///
    /// <b>날짜를 문자열로 두는 이유</b>: Unity 는 <c>DateTime</c> 을 직렬화하지 못한다. 인스펙터에서
    /// 손으로 적기 쉬운 <c>yyyy-MM-dd</c> 문자열로 두고 필요할 때만 해석한다. 비워두면
    /// <b>상설</b>(기간 제한 없음)이라는 뜻이다.
    /// </summary>
    [CreateAssetMenu(fileName = "Chapter", menuName = "JojoPuzzle/Chapter")]
    public class ChapterDefinition : ScriptableObject
    {
        [Header("표시")]
        public string displayName = "1챕터";

        [TextArea(1, 3)]
        public string description = string.Empty;

        [Tooltip("목록 카드에 깔리는 그림. 비어 있으면 단색으로 나온다.")]
        public Sprite banner;

        [Header("분류")]
        public ChapterKind kind = ChapterKind.Normal;

        [Tooltip("권장 레벨. 목록 카드에 표시된다.")]
        public int recommendedLevel = 1;

        [Header("개최 기간 (비우면 상설)")]
        [Tooltip("yyyy-MM-dd. 비워두면 시작 제한이 없다.")]
        public string openDate = string.Empty;

        [Tooltip("yyyy-MM-dd. 이 날<b>까지</b> 열려 있다(그날 23:59 까지).")]
        public string closeDate = string.Empty;

        [Header("스테이지 (최소 1개, 최대 5개)")]
        public StageDefinition[] stages = new StageDefinition[0];

        /// <summary>
        /// 스테이지 목록을 건너뛰고 준비 화면으로 바로 갈지.
        /// 특별·이벤트 챕터거나, 스테이지가 하나뿐이면 목록을 보여줄 이유가 없다.
        /// </summary>
        public bool GoesStraightToPrep =>
            kind != ChapterKind.Normal || stages == null || stages.Length <= 1;

        /// <summary>기간이 정해져 있는지. 둘 다 비어 있으면 상설이다.</summary>
        public bool HasSchedule =>
            !string.IsNullOrWhiteSpace(openDate) || !string.IsNullOrWhiteSpace(closeDate);

        /// <summary>
        /// 지금 들어갈 수 있는 기간인지. 날짜를 못 읽으면 <b>열린 것으로 본다</b> -
        /// 오타 하나로 챕터가 통째로 사라지는 것보다 낫다.
        /// </summary>
        public bool IsOpen(DateTime now)
        {
            if (TryParseDate(openDate, out DateTime start) && now.Date < start.Date)
                return false;

            if (TryParseDate(closeDate, out DateTime end) && now.Date > end.Date)
                return false;

            return true;
        }

        /// <summary>목록 카드에 적을 기간 문구. 상설이면 빈 문자열.</summary>
        public string GetScheduleText()
        {
            if (!HasSchedule)
                return string.Empty;

            bool hasStart = TryParseDate(openDate, out DateTime start);
            bool hasEnd = TryParseDate(closeDate, out DateTime end);

            if (hasStart && hasEnd)
                return $"{Format(start)} ~ {Format(end)}";

            if (hasEnd)
                return $"~ {Format(end)}";

            if (hasStart)
                return $"{Format(start)} ~";

            // 적혀 있긴 한데 날짜로 못 읽은 경우. 적은 그대로 보여주는 게 고치기 쉽다.
            return $"{openDate} ~ {closeDate}".Trim(' ', '~');
        }

        private static string Format(DateTime d) => d.ToString("M/d");

        private static bool TryParseDate(string text, out DateTime value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return DateTime.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture,
                                     System.Globalization.DateTimeStyles.None, out value);
        }
    }
}

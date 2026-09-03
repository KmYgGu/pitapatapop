namespace JojoPuzzle.Core
{
    /// <summary>
    /// 레벨별 전투력 곡선 종류. 기획 엑셀("데미지계산, 레벨디자인.xlsx" → 전투력 시트)에
    /// 두 가지 안이 나란히 들어 있어서 둘 다 옮겨왔다. 시작(Lv1)과 끝(Lv50) 값은 두 안이 같고,
    /// 중간 구간이 어떻게 차오르는지만 다르다.
    /// </summary>
    public enum CombatPowerCurve
    {
        /// <summary>
        /// 선형증가 - 레벨당 거의 일정한 폭으로 오름. 기획상 잘못 작성된 안이라 실제로는 쓰지 않고,
        /// 비교/참고용으로만 남겨둔 표다(DefaultCurve 참고).
        /// </summary>
        Linear = 0,

        /// <summary>곡선증가 - 초반엔 완만하고 중후반에 가파르게 오르다 막판에 상한으로 수렴. 기획 확정안.</summary>
        Curved = 1
    }

    /// <summary>
    /// 레벨/등급별 전투력과 레벨업 필요 경험치 테이블. 기획 엑셀
    /// "데미지계산, 레벨디자인.xlsx"(전투력 시트 / 필요경험치 시트)의 값을 그대로 옮긴 것으로,
    /// 수치를 바꿀 땐 엑셀을 먼저 고치고 여기에 반영해야 둘이 어긋나지 않는다.
    ///
    /// MonoBehaviour도 ScriptableObject도 아닌 순수 정적 클래스인 이유: 씬 배치나 참조 연결 없이
    /// 어디서든 조회만 하면 되는 읽기 전용 상수 표이고, 이 프로젝트가 로직 계층을 순수 C#으로
    /// 두는 원칙(BoardManager/ConnectionFinder 등)과도 맞기 때문. 유닛테스트도 그대로 가능하다.
    /// </summary>
    public static class CharacterGrowthTable
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 50;

        /// <summary>
        /// 전투력 계산에 쓸 곡선. 기획 확정: 곡선증가(Curved).
        /// 엑셀에 선형증가 안도 함께 들어 있지만 그쪽은 잘못 작성된 것이라 쓰지 않는다
        /// (표 자체는 비교/참고용으로 남겨둠). 다시 바꿀 일이 생기면 이 한 줄만 고치면 된다.
        /// </summary>
        public const CombatPowerCurve DefaultCurve = CombatPowerCurve.Curved;

        // --- 선형증가(엑셀 전투력 시트 왼쪽 표) : 인덱스 0 = Lv1 ---
        private static readonly int[] LinearGR =
        {
            150, 184, 217, 251, 285, 318, 352, 386, 419, 453,
            487, 520, 554, 588, 621, 655, 689, 722, 756, 790,
            823, 857, 891, 924, 958, 992, 1025, 1059, 1093, 1126,
            1160, 1194, 1227, 1261, 1295, 1328, 1362, 1396, 1429, 1463,
            1497, 1530, 1564, 1598, 1631, 1665, 1699, 1732, 1766, 1800
        };

        private static readonly int[] LinearSR =
        {
            110, 130, 150, 171, 191, 211, 231, 251, 272, 292,
            312, 332, 352, 373, 393, 413, 433, 454, 474, 494,
            514, 534, 555, 575, 595, 615, 635, 656, 676, 696,
            716, 736, 757, 777, 797, 817, 837, 858, 878, 898,
            918, 938, 959, 979, 999, 1019, 1039, 1060, 1080, 1100
        };

        private static readonly int[] LinearBR =
        {
            80, 95, 109, 124, 139, 153, 168, 183, 198, 212,
            227, 242, 256, 271, 286, 300, 315, 330, 345, 359,
            374, 389, 403, 418, 433, 447, 462, 477, 492, 506,
            521, 536, 550, 565, 580, 594, 609, 624, 639, 653,
            668, 683, 697, 712, 727, 741, 756, 771, 785, 800
        };

        // --- 곡선증가(엑셀 전투력 시트 오른쪽 표) : 인덱스 0 = Lv1 ---
        private static readonly int[] CurvedGR =
        {
            150, 161, 173, 186, 200, 215, 231, 248, 266, 285,
            305, 326, 348, 371, 395, 421, 448, 476, 505, 535,
            567, 600, 634, 670, 707, 746, 786, 828, 871, 916,
            963, 1011, 1061, 1113, 1167, 1222, 1279, 1338, 1399, 1462,
            1527, 1594, 1663, 1734, 1760, 1778, 1789, 1795, 1798, 1800
        };

        private static readonly int[] CurvedSR =
        {
            110, 117, 124, 132, 140, 149, 158, 168, 178, 189,
            201, 213, 226, 240, 254, 269, 285, 301, 318, 336,
            355, 374, 394, 415, 437, 460, 484, 509, 534, 561,
            589, 618, 648, 679, 711, 744, 778, 813, 849, 886,
            924, 963, 1003, 1044, 1059, 1070, 1080, 1090, 1096, 1100
        };

        private static readonly int[] CurvedBR =
        {
            80, 85, 90, 96, 102, 108, 115, 122, 130, 138,
            146, 155, 164, 174, 184, 195, 206, 218, 230, 243,
            256, 270, 285, 300, 316, 333, 350, 369, 388, 408,
            429, 450, 472, 495, 519, 544, 570, 597, 624, 652,
            681, 711, 742, 753, 765, 775, 784, 792, 797, 800
        };

        // --- 필요 경험치(엑셀 필요경험치 시트) : 인덱스 0 = Lv1 ---
        // RequiredExp[i] = (i+1)레벨이 "되기 위해" 필요한 경험치. Lv1은 시작 레벨이라 0.
        private static readonly int[] RequiredExp =
        {
            0, 100, 120, 150, 180, 220, 270, 330, 400, 500,
            650, 800, 1000, 1200, 1500, 1800, 2200, 2600, 3100, 3700,
            4400, 5200, 6100, 7100, 8200, 9500, 11000, 12500, 14200, 16000,
            18000, 20000, 22500, 25000, 28000, 31000, 34000, 38000, 42000, 46000,
            51000, 56000, 62000, 68000, 75000, 82000, 90000, 100000, 110000, 120000
        };

        // CumulativeExp[i] = Lv1부터 (i+1)레벨까지 오는 데 든 총 경험치.
        private static readonly int[] CumulativeExp =
        {
            0, 100, 220, 370, 550, 770, 1040, 1370, 1770, 2270,
            2920, 3720, 4720, 5920, 7420, 9220, 11420, 14020, 17120, 20820,
            25220, 30420, 36520, 43620, 51820, 61320, 72320, 84820, 99020, 115020,
            133020, 153020, 175520, 200520, 228520, 259520, 293520, 331520, 373520, 419520,
            470520, 526520, 588520, 656520, 731520, 813520, 903520, 1003520, 1113520, 1233520
        };

        /// <summary>
        /// 등급/레벨에 해당하는 전투력. 레벨이 범위를 벗어나면 양 끝 값으로 고정(clamp)한다 -
        /// 세이브 데이터가 깨졌거나 인스펙터에 이상한 값이 들어가도 예외 대신 안전한 값을 주기 위함.
        /// </summary>
        public static int GetCombatPower(CharacterGrade grade, int level, CombatPowerCurve curve = DefaultCurve)
        {
            int index = ClampLevel(level) - 1;
            return SelectTable(grade, curve)[index];
        }

        /// <summary>
        /// targetLevel에 도달하기 위해 (targetLevel - 1)레벨에서 추가로 벌어야 하는 경험치.
        /// 시작 레벨(1)이나 범위 밖이면 0.
        /// </summary>
        public static int GetRequiredExp(int targetLevel)
        {
            if (targetLevel <= MinLevel || targetLevel > MaxLevel)
                return 0;

            return RequiredExp[targetLevel - 1];
        }

        /// <summary>
        /// 1레벨부터 해당 레벨까지 오는 데 필요한 총 경험치. 나중에 "총 획득 경험치"만 저장하는
        /// 세이브 구조가 생기면 이 값과 비교해서 레벨을 역산할 수 있다.
        /// </summary>
        public static int GetCumulativeExp(int level) => CumulativeExp[ClampLevel(level) - 1];

        public static int ClampLevel(int level)
        {
            if (level < MinLevel) return MinLevel;
            if (level > MaxLevel) return MaxLevel;
            return level;
        }

        private static int[] SelectTable(CharacterGrade grade, CombatPowerCurve curve)
        {
            if (curve == CombatPowerCurve.Curved)
            {
                switch (grade)
                {
                    case CharacterGrade.GR: return CurvedGR;
                    case CharacterGrade.SR: return CurvedSR;
                    default: return CurvedBR;
                }
            }

            switch (grade)
            {
                case CharacterGrade.GR: return LinearGR;
                case CharacterGrade.SR: return LinearSR;
                default: return LinearBR;
            }
        }
    }
}

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 스테이지를 깼을 때 <b>캐릭터가 받는 경험치</b>. 스테이지가 주는 값 하나에 자리별 배율을 곱한다.
    ///
    ///   경험치 = 스테이지 클리어 경험치 × 자리 배율
    ///
    /// 자리 배율(2026-08-25 사용자 기획):
    /// <code>
    ///   리더(팔레트 0)   1.25배   - 직접 편성해 앞세운 값
    ///   파트너(팔레트 1) 1배
    ///   나머지 4칸       0.75배   - 편성하지 않았지만 판에 색이 깔려 함께 쓰인 캐릭터
    /// </code>
    ///
    /// <b>배율을 화면이 아니라 여기 두는 이유</b>: 이건 보상 규칙이지 그리기 설정이 아니다.
    /// 나중에 결과 화면 말고 다른 곳(예: 아파트에서 경험치 미리보기)에서도 같은 답이 나와야 한다.
    ///
    /// <see cref="GoldReward"/>·<see cref="StandUpDamageTable"/> 과 같은 순수 static 이다 -
    /// UnityEngine 에 기대지 않으므로 그대로 검산할 수 있다.
    /// </summary>
    public static class StageExpReward
    {
        /// <summary>리더 자리(팔레트 0) 배율.</summary>
        public const float LeaderMultiplier = 1.25f;

        /// <summary>파트너 자리(팔레트 1) 배율.</summary>
        public const float PartnerMultiplier = 1f;

        /// <summary>편성하지 않은 나머지 칸의 배율.</summary>
        public const float OtherMultiplier = 0.75f;

        /// <summary>
        /// <b>졌을 때</b>의 배율. 클리어를 못 했으니 경험치도 그만큼만 준다
        /// (2026-08-27 사용자 기획). 0으로 두지 않은 이유는 진 판도 시간을 쓴 판이라서다.
        /// </summary>
        public const float DefeatMultiplier = 0.25f;

        /// <summary>팔레트 자리에 해당하는 배율. 0=리더, 1=파트너, 나머지는 그 밖.</summary>
        public static float MultiplierFor(int paletteIndex)
        {
            if (paletteIndex == 0)
                return LeaderMultiplier;

            if (paletteIndex == 1)
                return PartnerMultiplier;

            return OtherMultiplier;
        }

        /// <summary>
        /// 이 자리의 캐릭터가 받을 경험치.
        /// <b>내림한다</b> - 화면에 정수로 나가는 값이라 여기서 정수로 만들어야 표시와 실제가 같다.
        /// </summary>
        /// <param name="victory">
        /// 이겼는지. 졌으면 <see cref="DefeatMultiplier"/> 가 한 번 더 곱해진다.
        /// </param>
        public static int ExpFor(int stageClearExp, int paletteIndex, bool victory = true)
        {
            if (stageClearExp <= 0)
                return 0;

            float multiplier = MultiplierFor(paletteIndex);
            if (!victory)
                multiplier *= DefeatMultiplier;

            return (int)(stageClearExp * multiplier);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using JojoPuzzle.Core;

namespace JojoPuzzle.Battle
{
    /// <summary>
    /// 팔레트 한 칸: 어떤 캐릭터이고, 보드 위에서 실제로 어떤 프레임 색으로 그려질지(스왑 반영 후).
    /// 캐릭터 자체의 기본색과 렌더링 색이 다를 수 있어서(리더/파트너 스왑) 하나로 묶어서 들고 다님.
    /// </summary>
    public struct PaletteSlot
    {
        public PanelType character;
        public PanelFrameColor frameColor;

        /// <summary>
        /// 리더와 <b>기본색이 같아서 색을 갈아 낀</b> 슬롯인지(파트너 쪽만 그렇게 된다).
        ///
        /// ⭐ 스티커 두 장이 이걸 묻는다(2026-09-03): "중복색이 있을 때 원본색 리젠 +%",
        /// "중복색 조각의 데미지 +%". 예전엔 색을 갈아 낀 사실이 <b>아무 데도 안 남아서</b>
        /// 나중에 되물을 수 없었다 - 스왑하는 자리에서 같이 적어 둔다.
        /// </summary>
        public bool isSwappedColor;
    }

    /// <summary>
    /// 배틀 시작 시 보드에 쓰일 6색 팔레트를 결정한다.
    /// 규칙: 편성한 파티(2명)의 색은 고정 포함 + 보유 캐릭터(편성 제외) 중 무작위 4색.
    /// 프레임 색 규칙: 리더+파트너가 같은 기본색이면 파트너 쪽만 +8(스왑색)로 렌더링해서
    /// 시각적으로 겹치지 않게 함. 무작위로 뽑히는 나머지 4색은 이미 팔레트에 쓰인 어떤
    /// 프레임 색과도 겹치지 않는 캐릭터만 후보로 삼음(무작위 색끼리의 중복도 방지).
    /// </summary>
    public static class BattleSetup
    {
        /// <summary>
        /// party: 편성한 캐릭터 2명의 PanelType (필수, 정확히 2개 가정)
        /// ownedPool: 현재 보유 중인 모든 캐릭터의 PanelType 목록 (party 포함 여부 상관없음, 내부에서 제외 처리)
        /// rng: 테스트 시드 고정을 위해 외부에서 주입 가능하도록 System.Random을 인자로 받음
        /// </summary>
        public static List<PaletteSlot> BuildPalette(List<PanelType> party, List<PanelType> ownedPool, System.Random rng)
        {
            if (party == null || party.Count == 0)
                throw new System.ArgumentException("편성 파티가 비어있습니다.");

            var palette = new List<PaletteSlot>();
            var usedColors = new HashSet<PanelFrameColor>();

            var leader = party[0];
            palette.Add(new PaletteSlot { character = leader, frameColor = leader.frameColor });
            usedColors.Add(leader.frameColor);

            if (party.Count > 1)
            {
                var partner = party[1];
                bool swapped = partner.frameColor == leader.frameColor;
                var partnerColor = swapped ? SwapColor(partner.frameColor) : partner.frameColor;

                palette.Add(new PaletteSlot
                {
                    character = partner,
                    frameColor = partnerColor,
                    isSwappedColor = swapped
                });
                usedColors.Add(partnerColor);
            }

            // 편성에 이미 들어간 캐릭터는 후보에서 제외 (중복 방지)
            var candidates = ownedPool
                .Where(p => !party.Contains(p))
                .Distinct()
                .ToList();
            Shuffle(candidates, rng);

            int needed = 6 - palette.Count;
            foreach (var candidate in candidates)
            {
                if (needed <= 0)
                    break;

                if (usedColors.Contains(candidate.frameColor))
                    continue; // 이미 팔레트에 쓰인 프레임 색과 겹치면 후보 탈락

                palette.Add(new PaletteSlot { character = candidate, frameColor = candidate.frameColor });
                usedColors.Add(candidate.frameColor);
                needed--;
            }

            // 보유 캐릭터가 부족해서(또는 색이 겹쳐서) 6색을 못 채우는 예외 상황은 호출부에서 별도 처리 권장
            return palette;
        }

        /// <summary>
        /// 기본색(0~7)을 짝이 되는 스왑색(+8)으로 바꾼다. 이미 스왑색인 값이 들어오는 경우는
        /// 없다고 가정(캐릭터의 frameColor는 항상 기본색 0~7로만 지정됨).
        /// </summary>
        private static PanelFrameColor SwapColor(PanelFrameColor color) => (PanelFrameColor)((int)color + 8);

        private static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}

using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 붙여 둔 스티커가 전투에 <b>얼마나</b> 보태는지 묻는 창구.
    ///
    /// ⭐ <b>전투 코드는 스티커를 몰라도 된다.</b> "지금 노란색 리젠에 얼마 더해?"만 물으면 된다 -
    /// 스티커가 늘어나도 묻는 쪽은 안 고친다. 반대로 여기서 목록을 훑지 않으면
    /// 전투 곳곳에 스티커 판정이 흩어지고, 하나 빠뜨리면 조용히 안 듣는다.
    ///
    /// <b>목록은 짧다</b>(붙일 수 있는 게 코스트로 묶여 있어 많아야 열 몇 장) -
    /// 매번 훑어도 부담이 없다. 대신 <b>전투 도중에 값이 바뀌지 않으므로</b>
    /// 자주 묻는 자리는 판이 시작할 때 한 번 받아 두는 게 낫다.
    /// </summary>
    public static class StickerEffects
    {
        /// <summary>지금 판에 쓰는 스티커 목록. 전투 씬이 시작할 때 물려 준다.</summary>
        public static StickerCatalog Catalog { get; set; }

        /// <summary>그 색 조각이 다시 나올 확률에 더할 값(0.01 = +1%).</summary>
        public static float RegenBonus(PanelFrameColor color)
            => Sum(StickerEffect.BlockRegen, (int)color) * 0.01f;

        /// <summary>중복색이 있을 때 원본색 리젠에 더할 값.</summary>
        public static float DuplicateRegenBonus()
            => Sum(StickerEffect.DuplicateRegen, -1) * 0.01f;

        /// <summary>그 색 조각의 데미지 배수에 더할 값(0.01 = +1%).</summary>
        public static float DamageBonus(PanelFrameColor color)
            => Sum(StickerEffect.BlockDamage, (int)color) * 0.01f;

        public static float DuplicateDamageBonus()
            => Sum(StickerEffect.DuplicateDamage, -1) * 0.01f;

        /// <summary>판이 끝날 때 코인에 곱할 값에 더한다.</summary>
        public static float CoinBonus() => Sum(StickerEffect.CoinGain, -1) * 0.01f;

        public static float RushCoinBonus() => Sum(StickerEffect.RushCoin, -1) * 0.01f;

        public static float TopMatcherExpBonus() => Sum(StickerEffect.TopMatcherExp, -1) * 0.01f;

        /// <summary>그 효과를 가진 스티커가 붙어 있는지. 발동형은 대개 이걸로 충분하다.</summary>
        public static bool Has(StickerEffect effect) => Find(effect) != null;

        /// <summary>
        /// 그 효과의 스티커를 찾는다. 없으면 null.
        /// <b>세기와 초를 같이 봐야 하는</b> 효과가 있어서 값 하나만 주는 걸로는 모자란다.
        /// </summary>
        public static StickerDefinition FindAttached(StickerEffect effect) => Find(effect);

        /// <summary>그 효과의 세기. 없으면 0.</summary>
        public static float ValueOf(StickerEffect effect)
        {
            var sticker = Find(effect);
            return sticker != null ? sticker.value : 0f;
        }

        private static StickerDefinition Find(StickerEffect effect)
        {
            if (Catalog == null)
                return null;

            var attached = PlayerStickers.Attached;
            for (int i = 0; i < attached.Count; i++)
            {
                var sticker = Catalog.Find(attached[i].id);
                if (sticker != null && sticker.effect == effect)
                    return sticker;
            }

            return null;
        }

        /// <summary><paramref name="color"/> 가 -1 이면 색을 안 가린다.</summary>
        private static float Sum(StickerEffect effect, int color)
        {
            if (Catalog == null)
                return 0f;

            float total = 0f;
            var attached = PlayerStickers.Attached;

            for (int i = 0; i < attached.Count; i++)
            {
                var sticker = Catalog.Find(attached[i].id);
                if (sticker == null || sticker.effect != effect)
                    continue;

                if (color >= 0 && sticker.color != color)
                    continue;

                total += sticker.value;
            }

            return total;
        }
    }
}

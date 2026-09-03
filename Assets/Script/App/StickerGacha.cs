using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 스티커 뽑기의 <b>화폐</b>. 무엇으로 뽑느냐에 따라 나오는 것이 달라진다.
    /// </summary>
    public enum StickerDrawKind
    {
        /// <summary>골드로. <b>저코스트가 잘 나온다.</b></summary>
        Gold = 0,

        /// <summary>보석으로. <b>고코스트가 잘 나온다.</b></summary>
        Gem = 1,
    }

    /// <summary>
    /// <b>스티커 뽑기</b>(2026-09-03 사용자 기획). 캐릭터 가챠와는 다른 것이다.
    ///
    /// <code>
    ///   골드로 사면  → 저코스트 스티커가 나올 확률이 높다
    ///   보석으로 사면 → 고코스트 스티커가 나올 확률이 높다
    /// </code>
    ///
    /// ⭐ <b>확률을 코스트에서 이끌어낸다.</b> 스티커마다 확률을 손으로 적어 두면 스티커가
    /// 늘 때마다 표를 고쳐야 하고, 하나 빠뜨리면 그건 영영 안 나온다. 코스트(5·10·15·20)는
    /// 이미 그 스티커가 얼마나 센지를 나타내므로, 그걸 그대로 무게로 쓴다.
    ///
    /// ⭐ <b>중복도 그대로 준다</b>(사용자 확정) - 스티커는 중복 보유·중복 착용이 되므로
    /// 이미 가진 게 또 나와도 버려지지 않는다. "꽝"이 없다.
    /// </summary>
    public static class StickerGacha
    {
        /// <summary>
        /// 코스트를 무게로 바꾸는 <b>기울기</b>. 클수록 한쪽으로 더 쏠린다.
        ///
        /// 골드는 <c>1 / cost^기울기</c>, 보석은 <c>cost^기울기</c> 로 무게를 잡는다 -
        /// 두 쪽이 <b>서로 뒤집힌 모양</b>이라 값 하나로 양쪽 쏠림을 함께 다룰 수 있다.
        /// </summary>
        private const float Slope = 1.5f;

        /// <summary>
        /// 한 번 뽑아서 <b>바로 준다</b>. 목록이 비었으면 null.
        ///
        /// ⚠ <b>값은 여기서 안 받는다</b> - 값은 상점 카탈로그(ShopGood.price)가 들고 있다.
        /// 여기에 또 적으면 출처가 둘이 되어, 기획에서 값을 고쳤을 때 한쪽만 바뀐다.
        /// 부르는 쪽이 먼저 값을 치르고 이걸 부른다.
        /// </summary>
        public static StickerDefinition Draw(StickerCatalog catalog, StickerDrawKind kind)
        {
            if (catalog == null || catalog.Count == 0)
                return null;

            var picked = Roll(catalog, kind);
            if (picked != null)
                PlayerStickers.Grant(picked.id);

            return picked;
        }

        /// <summary>
        /// 그 스티커가 나올 <b>무게</b>. 화면이 확률을 보여줄 때도 같은 값을 쓴다 -
        /// 보여주는 값과 실제로 굴리는 값이 다르면 그건 거짓말이다.
        /// </summary>
        public static float WeightOf(StickerDefinition sticker, StickerDrawKind kind)
        {
            if (sticker == null)
                return 0f;

            // 코스트가 0인 스티커는 없지만, 있어도 0으로 나누지 않게 막는다.
            float cost = Mathf.Max(1, sticker.cost);

            return kind == StickerDrawKind.Gem
                ? Mathf.Pow(cost, Slope)
                : 1f / Mathf.Pow(cost, Slope);
        }

        private static StickerDefinition Roll(StickerCatalog catalog, StickerDrawKind kind)
        {
            float total = 0f;
            for (int i = 0; i < catalog.Count; i++)
                total += WeightOf(catalog.At(i), kind);

            if (total <= 0f)
                return catalog.At(Random.Range(0, catalog.Count));

            float roll = Random.value * total;
            for (int i = 0; i < catalog.Count; i++)
            {
                roll -= WeightOf(catalog.At(i), kind);
                if (roll <= 0f)
                    return catalog.At(i);
            }

            return catalog.At(catalog.Count - 1);
        }

        /// <summary>
        /// 코스트별로 나올 확률을 <b>백분율</b>로 채운다. 상점 화면이 "무엇이 잘 나오는지"를
        /// 보여줄 때 쓴다 - 뽑기는 값을 치르는 일이라 무엇을 사는지 알 수 있어야 한다.
        /// </summary>
        public static void FillCostOdds(StickerCatalog catalog, StickerDrawKind kind,
            SortedDictionary<int, float> into)
        {
            into.Clear();
            if (catalog == null)
                return;

            float total = 0f;
            for (int i = 0; i < catalog.Count; i++)
            {
                var sticker = catalog.At(i);
                if (sticker == null)
                    continue;

                float w = WeightOf(sticker, kind);
                total += w;
                into[sticker.cost] = into.TryGetValue(sticker.cost, out float had) ? had + w : w;
            }

            if (total <= 0f)
                return;

            var costs = new List<int>(into.Keys);
            for (int i = 0; i < costs.Count; i++)
                into[costs[i]] = into[costs[i]] / total * 100f;
        }
    }
}

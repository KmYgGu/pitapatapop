using System.Collections.Generic;
using JojoPuzzle.Core;
using UnityEngine;

namespace JojoPuzzle.App
{
    /// <summary>
    /// <b>스티커북</b> - 가진 스티커와 붙여 둔 자리(2026-09-03 사용자 기획).
    ///
    /// ⭐ <b>스티커북이 여섯 권이다</b>(사용자 지시). 스티커 조합에 따라 판이 아주 달라지므로,
    /// 판마다 새로 붙이지 않고 <b>미리 짜둔 것을 골라 쓴다</b>. 좌우로 넘겨 고르고,
    /// 전투는 <b>지금 펼친 권</b>의 확정본만 읽는다.
    ///
    /// ⭐ <b>코스트 안에서만 붙일 수 있다.</b> 최대 코스트는 레벨을 따라 오르고
    /// <b>레벨 50에서 90</b>이 된다 - 다 붙일 수 있으면 스티커북은 그냥 체크리스트가 된다.
    ///
    /// ⭐ <b>캐릭터도 스티커로 친다</b>(<see cref="LeaderSlot"/>·<see cref="PartnerSlot"/>).
    /// 같은 초안에 담아야 스티커를 캐릭터 위에 붙일 수 있고 캐릭터도 끌어서 옮길 수 있다.
    ///
    /// ⭐ <b>놓는다고 확정되지 않는다.</b> 붙이고 옮기는 건 전부 <b>초안</b>에서 일어나고
    /// <see cref="Commit"/> 을 불러야 전투가 읽는 쪽으로 넘어간다.
    ///
    /// <b>⚠ 저장되지 않는다</b>(이 프로젝트의 모든 유저 상태와 같다).
    /// </summary>
    public static class PlayerStickers
    {
        /// <summary>짜둘 수 있는 스티커북 수(사용자 확정).</summary>
        public const int BookCount = 6;

        /// <summary>
        /// 캐릭터 자리. 스티커 번호는 100000 대라 <b>음수는 겹치지 않는다</b>.
        /// </summary>
        public const int LeaderSlot = -1;
        public const int PartnerSlot = -2;

        /// <summary>캐릭터 자리는 코스트를 안 먹는다.</summary>
        public static bool IsCharacterSlot(int id) => id < 0;

        /// <summary>레벨 1에서 쓸 수 있는 코스트.</summary>
        public const int BaseCost = 10;

        /// <summary>다 자란 코스트와 그 레벨(사용자 확정).</summary>
        public const int FullCost = 90;
        public const int FullLevel = 50;

        public static int MaxCost(int level)
        {
            if (level >= FullLevel)
                return FullCost;

            if (level <= 1)
                return BaseCost;

            float t = (level - 1f) / (FullLevel - 1f);
            return Mathf.RoundToInt(Mathf.Lerp(BaseCost, FullCost, t));
        }

        /// <summary>붙여 둔 스티커 한 장. 자리까지 들고 있다.</summary>
        public struct Placed
        {
            public int id;

            /// <summary>
            /// 이 <b>배치</b>의 고유 번호. id 와 다르다 - 같은 스티커를 여러 장 붙일 수 있어서
            /// id 만으로는 "어느 쪽을 옮기는 건지"를 가릴 수 없다(2026-09-03 사용자 확정).
            /// 캐릭터 자리도 번호를 받는다 - 다루는 길을 하나로 두는 게 낫다.
            /// </summary>
            public int key;

            /// <summary>책 안에서의 자리. 0~1 비율이라 <b>기기 해상도와 상관없다</b>.</summary>
            public Vector2 position;
        }

        // 스티커는 <b>중복 보유가 된다</b>(2026-09-03 사용자 확정) - 그래서 "가졌는가"가 아니라
        // <b>몇 장 가졌는가</b>를 센다. 뽑기에서 같은 게 또 나와도 버려지지 않는다.
        private static readonly Dictionary<int, int> owned = new Dictionary<int, int>();

        // 배치마다 하나씩 오르는 번호. 저장하지 않는다 - 한 실행 안에서 서로 구분만 되면 된다.
        private static int nextPlacedKey;

        // 권마다 <b>확정본</b>과 <b>초안</b>을 따로 갖는다. 한 권을 만지다 옆 권으로 넘어가도
        // 하던 손질이 안 날아간다.
        private static readonly List<Placed>[] books = NewBooks();
        private static readonly List<Placed>[] drafts = NewBooks();
        private static readonly string[] names = NewNames();

        // ⭐ 권마다 <b>편성도 따로</b> 갖는다(2026-09-03 사용자 지시). 스티커 조합에 맞는
        // 캐릭터가 따로 있는데 늘 첫 권의 편성을 물려 쓰면 권을 나눈 뜻이 없다.
        private static readonly PanelType[] leaders = new PanelType[BookCount];
        private static readonly PanelType[] partners = new PanelType[BookCount];

        private static List<Placed>[] NewBooks()
        {
            var made = new List<Placed>[BookCount];
            for (int i = 0; i < BookCount; i++)
                made[i] = new List<Placed>();

            return made;
        }

        private static string[] NewNames()
        {
            var made = new string[BookCount];
            for (int i = 0; i < BookCount; i++)
                made[i] = DefaultName(i);

            return made;
        }

        private static string DefaultName(int book) => "스티커북" + (book + 1);

        private static int active;

        /// <summary>지금 펼친 권. 전투는 이 권의 확정본을 쓴다.</summary>
        public static int ActiveBook
        {
            get => active;
            set
            {
                int wrapped = Wrap(value);
                if (wrapped == active)
                    return;

                active = wrapped;
                EnsureCharacterSlots();   // 빈 권이면 캐릭터 자리를 먼저 만든다
                ApplyParty();
                OnChanged?.Invoke();
            }
        }

        /// <summary>
        /// ⭐ <b>끝에서 넘기면 반대쪽 끝으로</b>(2026-09-03 사용자 지시).
        /// 여섯 권을 고리처럼 돌면 마지막 권에서 첫 권으로 가려고 다섯 번 되돌릴 일이 없다.
        /// </summary>
        private static int Wrap(int book) => ((book % BookCount) + BookCount) % BookCount;

        /// <summary>지금 권의 편성을 <see cref="PartySelection"/> 에 얹는다.</summary>
        public static void ApplyParty()
            => PartySelection.Set(leaders[active], partners[active]);

        /// <summary>지금 <see cref="PartySelection"/> 에 있는 편성을 이 권에 담아 둔다.</summary>
        public static void StoreParty()
        {
            leaders[active] = PartySelection.Leader;
            partners[active] = PartySelection.Partner;
        }

        public static string NameOf(int book)
            => book >= 0 && book < BookCount ? names[book] : string.Empty;

        /// <summary>이름을 바꾼다. 빈 이름은 기본 이름으로 되돌린다 - 이름 없는 권은 못 고른다.</summary>
        public static void Rename(int book, string value)
        {
            if (book < 0 || book >= BookCount)
                return;

            names[book] = string.IsNullOrWhiteSpace(value) ? DefaultName(book) : value.Trim();
            OnChanged?.Invoke();
        }

        /// <summary>가진 것이나 붙인 것이 달라졌다.</summary>
        public static event System.Action OnChanged;

        /// <summary>⭐ <b>확정된</b> 스티커(지금 권). 전투가 읽는 건 이것뿐이다.</summary>
        public static IReadOnlyList<Placed> Attached => books[active];

        /// <summary>지금 만지고 있는 것(지금 권). 스티커북 화면이 이걸 그린다.</summary>
        public static IReadOnlyList<Placed> Draft => drafts[active];

        /// <summary>그 스티커를 <b>몇 장</b> 가졌는지. 없으면 0.</summary>
        public static int OwnedCount(int id) => owned.TryGetValue(id, out int n) ? n : 0;

        public static bool Owns(int id) => OwnedCount(id) > 0;

        /// <summary>지금 권에 그 스티커를 <b>몇 장</b> 붙여 뒀는지.</summary>
        public static int AttachedCount(int id)
        {
            var draft = drafts[active];
            int n = 0;

            for (int i = 0; i < draft.Count; i++)
            {
                if (draft[i].id == id)
                    n++;
            }

            return n;
        }

        /// <summary>
        /// 한 장 더 붙일 수 있는지. <b>가진 수보다 적게 붙어 있어야</b> 한다 -
        /// 중복 착용은 되지만 없는 장을 붙일 수는 없다.
        /// </summary>
        public static bool CanAttachMore(int id) => AttachedCount(id) < OwnedCount(id);

        /// <summary>
        /// 그 id 로 붙어 있는 배치의 <b>고유 번호</b>. 없으면 0.
        /// 캐릭터 자리(리더·파트너)처럼 <b>id 가 하나뿐인</b> 것에 쓴다 -
        /// 스티커는 같은 id 가 여럿일 수 있어서 이걸로 못 가린다.
        /// </summary>
        public static int KeyOf(int id)
        {
            int at = IndexOf(id);
            return at >= 0 ? drafts[active][at].key : 0;
        }

        /// <summary>고유 번호로 배치를 찾는다. 없으면 -1.</summary>
        private static int IndexOfKey(int key)
        {
            var draft = drafts[active];

            for (int i = 0; i < draft.Count; i++)
            {
                if (draft[i].key == key)
                    return i;
            }

            return -1;
        }

        private static int IndexOf(int id)
        {
            var draft = drafts[active];
            for (int i = 0; i < draft.Count; i++)
            {
                if (draft[i].id == id)
                    return i;
            }

            return -1;
        }

        public static Vector2 PositionOf(int id)
        {
            int at = IndexOf(id);
            return at >= 0 ? drafts[active][at].position : new Vector2(0.5f, 0.5f);
        }

        /// <summary>확정 안 한 손질이 남아 있는지(지금 권).</summary>
        public static bool IsDirty
        {
            get
            {
                var draft = drafts[active];
                var done = books[active];

                if (draft.Count != done.Count)
                    return true;

                for (int i = 0; i < draft.Count; i++)
                {
                    if (draft[i].id != done[i].id
                        || (draft[i].position - done[i].position).sqrMagnitude > 0.000001f)
                        return true;
                }

                return false;
            }
        }

        /// <summary>지금 권을 확정한다. 여기서부터 전투가 달라진다.</summary>
        public static void Commit()
        {
            books[active].Clear();
            books[active].AddRange(drafts[active]);
            OnChanged?.Invoke();
        }

        /// <summary>지금 권을 마지막 확정 상태로 되돌린다.</summary>
        public static void Revert() => Revert(active);

        public static void Revert(int book)
        {
            if (book < 0 || book >= BookCount)
                return;

            drafts[book].Clear();
            drafts[book].AddRange(books[book]);
            OnChanged?.Invoke();
        }

        /// <summary>모든 권을 되돌린다. 책을 열 때 부른다.</summary>
        public static void RevertAll()
        {
            for (int i = 0; i < BookCount; i++)
            {
                drafts[i].Clear();
                drafts[i].AddRange(books[i]);
            }

            OnChanged?.Invoke();
        }

        /// <summary>
        /// ⭐ 이 권의 스티커를 <b>전부 뗀다</b>(2026-09-03 사용자 지시). 캐릭터 자리는 남긴다 -
        /// 캐릭터까지 사라지면 초기화가 아니라 고장으로 보인다.
        /// </summary>
        public static void ClearBook()
        {
            // ⭐ 캐릭터 자리도 <b>기본 자리로</b> 되돌린다(2026-09-03 사용자 지시).
            // 스티커만 지우고 캐릭터가 엉뚱한 데 남아 있으면 초기화한 것 같지가 않다.
            drafts[active].Clear();
            EnsureCharacterSlots();

            OnChanged?.Invoke();
        }

        /// <summary>
        /// <b>모든 권</b>의 캐릭터 자리를 챙긴다. 지금 권만 챙기면 다른 권으로 넘어갔을 때
        /// 자리가 없어 둘 다 가운데에 겹쳐 나온다(2026-09-03 사용자 지적).
        /// </summary>
        public static void EnsureCharacterSlotsAll()
        {
            int keep = active;

            for (int i = 0; i < BookCount; i++)
            {
                active = i;
                EnsureCharacterSlots();
            }

            active = keep;
        }

        /// <summary>
        /// 캐릭터 두 자리가 초안에 반드시 있게 한다.
        ///
        /// ⚠⚠ <b>확정본에도 같이 넣는다.</b> 초안에만 넣으면 <b>책을 여는 것만으로</b>
        /// "확정 안 한 손질"이 생겨서, 손도 안 댄 권이 계속 잠긴다
        /// (2026-09-03 사용자 지적: "강제로 꾸미고 나서야 여길 벗어날 수 있다").
        /// 자동으로 놓아 준 자리는 <b>사용자의 손질이 아니다</b>.
        /// </summary>
        public static void EnsureCharacterSlots()
        {
            bool changed = Ensure(LeaderSlot, new Vector2(0.27f, 0.70f))
                         | Ensure(PartnerSlot, new Vector2(0.73f, 0.70f));

            if (changed)
                OnChanged?.Invoke();
        }

        private static bool Ensure(int id, Vector2 spot)
        {
            if (IndexOf(id) >= 0)
                return false;

            var placed = new Placed { id = id, key = ++nextPlacedKey, position = spot };
            drafts[active].Add(placed);

            // 확정본에도 같은 자리로 넣어 둔다 - 그래야 '손 안 댄 권'이 깨끗하게 남는다.
            var done = books[active];
            bool has = false;
            for (int i = 0; i < done.Count; i++)
            {
                if (done[i].id == id)
                {
                    has = true;
                    break;
                }
            }

            if (!has)
                done.Add(placed);

            return true;
        }

        /// <summary>한 장 더 준다. <b>이미 가진 것이어도 장수가 는다</b>(중복 보유).</summary>
        public static void Grant(int id)
        {
            owned[id] = OwnedCount(id) + 1;
            OnChanged?.Invoke();
        }

        /// <summary>지금 권에 붙여 둔 것들의 코스트 합. 캐릭터 자리는 값이 없다.</summary>
        public static int UsedCost(StickerCatalog catalog)
        {
            if (catalog == null)
                return 0;

            int total = 0;
            var draft = drafts[active];

            for (int i = 0; i < draft.Count; i++)
            {
                var sticker = catalog.Find(draft[i].id);
                if (sticker != null)
                    total += sticker.cost;
            }

            return total;
        }

        /// <summary>붙인다. 가진 것이어야 하고, 코스트가 남아 있어야 한다.</summary>
        public static bool TryAttach(StickerCatalog catalog, int id, int level, Vector2 position)
            => TryAttach(catalog, id, level, position, out _);

        /// <summary>
        /// 붙이고 <b>그 배치의 고유 번호</b>를 돌려준다. 붙이자마자 집어 든 상태로 두려면
        /// 어느 장인지를 알아야 한다 - 같은 스티커가 여러 장일 수 있다.
        /// </summary>
        public static bool TryAttach(StickerCatalog catalog, int id, int level, Vector2 position,
            out int key)
        {
            key = 0;
            // 중복 착용이 되므로 "이미 붙었는가"가 아니라 <b>더 붙일 수 있는가</b>를 본다.
            if (catalog == null || !CanAttachMore(id))
                return false;

            var sticker = catalog.Find(id);
            if (sticker == null)
                return false;

            if (UsedCost(catalog) + sticker.cost > MaxCost(level))
                return false;

            key = ++nextPlacedKey;
            drafts[active].Add(new Placed { id = id, key = key, position = position });
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>붙인 채로 자리만 옮긴다. <b>고유 번호로 가린다</b>(같은 스티커가 둘일 수 있다).</summary>
        public static void MoveByKey(int key, Vector2 position)
        {
            int at = IndexOfKey(key);
            if (at < 0)
                return;

            var placed = drafts[active][at];
            placed.position = position;
            drafts[active][at] = placed;

            OnChanged?.Invoke();
        }

        /// <summary>
        /// 그 스티커를 <b>한 장</b> 뗀다(마지막에 붙인 것부터). 목록 화면처럼 고유 번호를
        /// 모르는 쪽이 쓴다 - 어느 장을 떼든 결과가 같으므로 가장 최근 것을 뗀다.
        /// </summary>
        public static bool DetachOne(int id)
        {
            var draft = drafts[active];

            for (int i = draft.Count - 1; i >= 0; i--)
            {
                if (draft[i].id != id)
                    continue;

                draft.RemoveAt(i);
                OnChanged?.Invoke();
                return true;
            }

            return false;
        }

        /// <summary>한 장 뗀다. <b>고유 번호로 가린다.</b></summary>
        public static bool DetachByKey(int key)
        {
            int at = IndexOfKey(key);
            if (at < 0)
                return false;

            drafts[active].RemoveAt(at);
            OnChanged?.Invoke();
            return true;
        }

        public static void Clear()
        {
            owned.Clear();

            for (int i = 0; i < BookCount; i++)
            {
                books[i].Clear();
                drafts[i].Clear();
                names[i] = DefaultName(i);
                leaders[i] = null;
                partners[i] = null;
            }

            active = 0;
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 아직 얻는 길(상점·보상)이 없어서 <b>처음 열 때 전부 가진 것으로</b> 채운다.
        /// 상점의 스티커 칸이 생기면 이 함수를 지우면 된다.
        /// </summary>
        public static void GrantAllForTesting(StickerCatalog catalog)
        {
            if (catalog == null)
                return;

            bool changed = false;
            for (int i = 0; i < catalog.Count; i++)
            {
                var sticker = catalog.At(i);
                if (sticker == null || OwnedCount(sticker.id) > 0)
                    continue;

                owned[sticker.id] = 1;
                changed = true;
            }

            if (changed)
                OnChanged?.Invoke();
        }
    }
}

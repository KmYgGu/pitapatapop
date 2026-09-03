using System;
using System.Collections;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 도둑잡기의 패를 <b>테이블 위 3D 오브젝트로</b> 놓는다(2026-09-02 사용자 기획).
    ///
    /// <code>
    ///   평소   : 조커를 든 쪽이 두 장, 집을 쪽이 한 장 - <b>양쪽 다 자기 패를 쥐고 있다</b>
    ///   내밀 때 : 한 장이 <b>집을 사람 쪽으로</b> 튀어나온다
    ///   집을 때 : 앞면으로 뒤집힌 뒤 <b>포물선을 그리며 집은 사람에게</b> 간다
    /// </code>
    ///
    /// ⭐ <b>각자 자기 쪽으로 눕혀 쥔다</b>(2026-09-02 사용자 지시).
    /// 내 패는 나를 향해 깊이 눕혀서 <b>건너편에서는 안 보이고</b>, 캐릭터의 패는 세워 들어서
    /// 나에게는 뒷면만 보인다 - 각도 하나로 "누구 패인지"와 "누가 못 보는지"가 같이 읽힌다.
    ///
    /// ⭐ <b>가져가는 걸 뿅 하고 바꾸면 안 된다</b> - 손으로 가져가는 게 보여야 누가 가져갔는지 남는다.
    ///
    /// ⭐ <b>카드를 직접 눌러서 집는다</b> - 버튼으로 고르게 하면 밀어 올린 카드를 보는 재미가 없다.
    /// 카드마다 콜라이더를 달고 카메라에서 레이를 쏜다.
    ///
    /// 크기와 자리는 <see cref="BlackjackCardBoard"/> 와 같은 방식으로 <b>테이블을 재서</b> 정한다 -
    /// 좌표를 숫자로 박으면 임포트 배율이 바뀔 때 어긋난다.
    /// </summary>
    public class OldMaidCardBoard : MonoBehaviour
    {
        [Header("이어붙일 것들")]
        [Tooltip("카드 앞면 52장(무늬 순서대로 13장씩). 다른 화면과 같은 것을 물려도 된다.")]
        [SerializeField] private Sprite[] cardFaces;

        [Tooltip("카드 뒷면. <b>남의 패는 늘 이걸로</b> 보인다.")]
        [SerializeField] private Sprite cardBack;

        [Tooltip("조커 그림.")]
        [SerializeField] private Sprite jokerFace;

        [Tooltip("테이블 모델의 뿌리. 이걸 재서 카드 크기와 자리를 정한다.")]
        [SerializeField] private Transform table;

        [Header("크기 - 테이블에 대한 비율")]
        [Tooltip("카드 높이 = 테이블 깊이 x 이 값.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float cardHeightFraction = 0.34f;

        [Tooltip("두 장이 좌우로 벌어지는 정도(카드 폭 대비).")]
        [Range(0.4f, 2f)]
        [SerializeField] private float spreadFraction = 0.62f;

        [Tooltip("각자 자기 쪽으로 얼마나 붙일지(테이블 깊이 대비). " +
                 "<b>가운데에 두면 누가 들고 있는지 안 읽힌다</b>(2026-09-02 사용자 지적).")]
        [Range(0f, 0.5f)]
        [SerializeField] private float sideOffsetFraction = 0.3f;

        [Header("쥐는 각도")]
        [Tooltip("<b>내 패</b>를 나를 향해 눕히는 각도. 클수록 눕고, 그만큼 건너편에서는 안 보인다.")]
        [Range(0f, 85f)]
        [SerializeField] private float playerTilt = 62f;

        [Tooltip("<b>캐릭터의 패</b>를 세우는 각도. 작을수록 나를 마주 본다 - 어차피 뒷면만 보인다.")]
        [Range(0f, 85f)]
        [SerializeField] private float opponentTilt = 22f;

        [Tooltip("테이블 상판에서 더 띄우는 높이(카드 높이 대비).")]
        [Range(0f, 0.3f)]
        [SerializeField] private float liftFraction = 0.02f;

        [Header("내미는 연출")]
        [Tooltip("밀어 올린 장이 <b>집을 사람 쪽으로</b> 나오는 거리(카드 높이 대비).")]
        [Range(0f, 1.5f)]
        [SerializeField] private float offerPush = 0.55f;

        [Tooltip("밀어 올린 장이 위로 뜨는 높이(카드 높이 대비).")]
        [Range(0f, 1f)]
        [SerializeField] private float offerLift = 0.18f;

        [Tooltip("튀어나오는 데 걸리는 시간(초).")]
        [Min(0.01f)]
        [SerializeField] private float offerDuration = 0.28f;

        [Header("가져가는 연출")]
        [Tooltip("집은 카드가 <b>포물선을 그리며</b> 집은 사람에게 가는 시간(초).")]
        [Min(0.05f)]
        [SerializeField] private float takeDuration = 0.55f;

        [Tooltip("그 포물선이 얼마나 높이 뜨는지(카드 높이 대비).")]
        [Range(0f, 3f)]
        [SerializeField] private float takeArcHeight = 1.1f;

        [Header("섞는 연출")]
        [Tooltip("두 장이 자리를 한 번 바꾸는 데 걸리는 시간(초).")]
        [Min(0.05f)]
        [SerializeField] private float shuffleStepDuration = 0.26f;

        [Tooltip("섞을 때 한 장이 위로 넘어가는 높이(카드 높이 대비).")]
        [Range(0f, 2f)]
        [SerializeField] private float shuffleArc = 0.5f;

        [Tooltip("몇 번 바꿔 칠지.")]
        [Range(1, 8)]
        [SerializeField] private int shuffleTimes = 3;

        /// <summary>카드를 눌러서 집었다. 값은 자리(0 또는 1).</summary>
        public event Action<int> OnCardPicked;

        // ⭐ 그리는 순서는 <b>주인</b>이 정한다 - 든 사람의 역할이 아니라.
        // 내 카드는 내 앞에 있으니 언제나 캐릭터 카드보다 앞에 그려져야 한다
        // (2026-09-02 사용자 지적: "상대가 가진 카드가 먼저 앞에 그려진다").
        private const int OpponentOrder = 20;
        private const int PlayerOrder = 60;

        // 테이블에 내려놓은 카드. 누구의 손패도 아니니 <b>둘 사이</b>에 그린다 -
        // 위로 올렸더니 내 손패까지 덮었다(2026-09-02 사용자 지적).
        private const int TableOrder = 40;

        private readonly SpriteRenderer[] held = new SpriteRenderer[OldMaid.HandSize];
        private readonly Vector3[] resting = new Vector3[OldMaid.HandSize];
        private readonly Coroutine[] moving = new Coroutine[OldMaid.HandSize];

        // 집을 쪽이 원래 들고 있는 한 장. 규칙엔 상관없고 <b>누가 뭘 쥐었는지</b>를 위해 놓는다.
        private SpriteRenderer spare;

        // 지금 이 패를 들고 있는 쪽. 놓는 자리와 내미는 방향이 여기서 갈린다.
        private bool holderIsPlayer;

        // 방금 카드를 가져간 사람이 쥔 두 장. 섞는 연출이 이 둘을 바꿔 친다.
        private SpriteRenderer shuffleA, shuffleB;
        private Vector3 shufflePosA, shufflePosB;
        private bool shuffleOwnerIsPlayer;

        private Bounds tableBounds;
        private bool measured;
        private bool pickable;
        private Camera cam;

        private void Update()
        {
            if (!pickable || !Input.GetMouseButtonDown(0))
                return;

            if (cam == null)
                cam = Camera.main;

            if (cam == null)
                return;

            // 카드를 직접 누른 것만 받는다 - 빈 곳을 눌러도 아무 일이 없어야 한다.
            if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out RaycastHit hit))
                return;

            // 조커를 든 쪽의 두 장만 집을 수 있다 - 여벌 한 장은 눌려도 아무 일이 없어야 한다.
            for (int i = 0; i < held.Length; i++)
            {
                if (held[i] != null && hit.transform == held[i].transform)
                {
                    pickable = false;
                    OnCardPicked?.Invoke(i);
                    return;
                }
            }
        }

        /// <summary>
        /// 패를 깐다. 조커를 든 쪽 앞에 두 장, 집을 쪽 앞에 한 장이 놓인다.
        /// <b>자기 패만 앞면으로 보인다</b> - 남의 패는 늘 뒷면이다.
        /// </summary>
        /// <param name="holder">조커를 든 쪽이 플레이어인지.</param>
        /// <param name="cardAt">조커를 든 쪽의 자리별 카드.</param>
        /// <param name="suitAt">그 자리별 무늬. 조커는 -1.</param>
        /// <param name="spareCard">집을 쪽이 들고 있는 한 장.</param>
        /// <param name="spareSuit">그 한 장의 무늬.</param>
        public void Deal(bool holder, Func<int, int> cardAt, Func<int, int> suitAt,
                         int spareCard, int spareSuit)
        {
            if (!Measure())
                return;

            // 새로 깔면 지난 판의 섞기 대상은 잊는다.
            shuffleA = shuffleB = null;

            holderIsPlayer = holder;
            int holderOrder = holderIsPlayer ? PlayerOrder : OpponentOrder;

            for (int i = 0; i < held.Length; i++)
            {
                var card = Ensure(ref held[i], "Held" + i);

                // 내가 든 패면 내 눈에는 보인다. 캐릭터가 든 패는 뒷면이다.
                card.sprite = holderIsPlayer && cardAt != null
                    ? FaceOf(cardAt(i), suitAt != null ? suitAt(i) : 0)
                    : cardBack;

                card.gameObject.SetActive(true);
                card.sortingOrder = holderOrder + i;

                Fit(card);
                Stop(i);
                card.transform.position = resting[i] = Seat(i, held.Length, card, holderIsPlayer);
                card.transform.rotation = Hold(holderIsPlayer);
            }

            // 집을 쪽의 여벌 한 장. 내 것이면 앞면, 캐릭터 것이면 뒷면.
            bool spareIsPlayer = !holderIsPlayer;
            var one = Ensure(ref spare, "Spare");
            one.sprite = spareIsPlayer ? FaceOf(spareCard, spareSuit) : cardBack;
            one.gameObject.SetActive(true);
            one.sortingOrder = spareIsPlayer ? PlayerOrder : OpponentOrder;

            Fit(one);
            one.transform.position = Seat(0, 1, one, spareIsPlayer);
            one.transform.rotation = Hold(spareIsPlayer);

            pickable = false;
        }

        /// <summary>한 장을 <b>집을 사람 쪽으로</b> 민다. 나머지는 제자리로 돌아간다.</summary>
        public void Offer(int slot)
        {
            if (!measured)
                return;

            float cardHeight = CardHeight;
            float toPicker = holderIsPlayer ? -1f : 1f;

            for (int i = 0; i < held.Length; i++)
            {
                if (held[i] == null)
                    continue;

                Vector3 target = resting[i];
                if (i == slot)
                    target += new Vector3(0f, cardHeight * offerLift,
                                          cardHeight * offerPush * toPicker);

                Stop(i);
                moving[i] = StartCoroutine(SlideRoutine(i, target));
            }
        }

        /// <summary>이제 눌러서 집을 수 있다.</summary>
        public void SetPickable(bool value) => pickable = value;

        /// <summary>집은 장을 앞면으로 뒤집는다.</summary>
        public void Reveal(int slot, int card, int suit)
        {
            if (slot < 0 || slot >= held.Length || held[slot] == null)
                return;

            held[slot].sprite = FaceOf(card, suit);
            Fit(held[slot]);
        }

        /// <summary>
        /// ⭐ 집은 장이 <b>포물선을 그리며 집은 사람의 손으로</b> 간다(2026-09-02 사용자 지시).
        /// 뿅 하고 바뀌면 누가 가져갔는지가 안 남는다.
        ///
        /// 가면서 세 가지가 같이 일어난다:
        /// <list type="bullet">
        /// <item>가져간 사람이 <b>쥐는 각도</b>로 돌아간다.</item>
        /// <item>남의 손에 들어가는 것이면 <b>뒤집혀서 뒷면이 된다</b> - 도착하면 남의 패니까.</item>
        /// <item>원래 쥐고 있던 한 장 <b>옆자리</b>에 선다 - 같은 깊이라 크기도 저절로 같아진다.</item>
        /// </list>
        /// 쥐고 있던 장은 옆으로 비켜 주고, 뺏긴 쪽의 남은 한 장은 가운데로 모인다.
        /// </summary>
        /// <param name="slot">집은 자리.</param>
        /// <param name="continues">
        /// 조커라서 판이 이어지는지. <b>이어질 때만</b> 렌더러 배역을 바꿔 끼운다 -
        /// 승부가 났는데도 바꾸면 마지막에 공개할 때 <b>내 카드가 조커로 둔갑한다</b>
        /// (2026-09-02 사용자 지적).
        /// </param>
        public IEnumerator TakeRoutine(int slot, bool continues)
        {
            if (!measured || slot < 0 || slot >= held.Length || held[slot] == null)
                yield break;

            Stop(slot);

            // 집는 쪽은 조커를 든 쪽의 반대다.
            bool takerIsPlayer = !holderIsPlayer;

            var taken = held[slot];
            var card = taken.transform;

            // ⭐ 도착지는 <b>가져간 사람의 두 장짜리 자리</b>다. 허공이 아니라 손 안이라야
            // 원래 쥐고 있던 장과 크기·높이가 딱 맞는다(2026-09-02 사용자 지시).
            Vector3 takenTarget = Seat(1, 2, taken, takerIsPlayer);

            if (spare != null)
            {
                // 쥐고 있던 장이 옆으로 비켜 준다.
                shufflePosA = Seat(0, 2, spare, takerIsPlayer);
                StartCoroutine(SlideTo(spare.transform, shufflePosA, takeDuration * 0.55f));
            }

            // 뺏긴 쪽에는 한 장만 남는다. 가운데로 모아 준다.
            int left = 1 - slot;
            if (left >= 0 && left < held.Length && held[left] != null)
            {
                Stop(left);
                StartCoroutine(SlideTo(held[left].transform,
                                       Seat(0, 1, held[left], holderIsPlayer),
                                       takeDuration * 0.55f));
            }

            Vector3 from = card.position;
            float arc = CardHeight * takeArcHeight;

            Quaternion fromRot = card.rotation;
            Quaternion toRot = Hold(takerIsPlayer);

            // 남의 손에 들어가면 뒷면이 되어야 한다. 가면서 한 번 뒤집는다.
            bool endsHidden = !takerIsPlayer;
            int takerOrder = takerIsPlayer ? PlayerOrder : OpponentOrder;

            float baseScale = card.localScale.x;
            bool turned = false;

            for (float t = 0f; t < takeDuration; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / takeDuration);

                Vector3 p = Vector3.Lerp(from, takenTarget, k);
                p.y += Mathf.Sin(k * Mathf.PI) * arc;   // 가운데가 제일 높은 포물선

                card.position = p;
                card.rotation = Quaternion.Slerp(fromRot, toRot, k);

                if (endsHidden)
                {
                    Flip(card, baseScale, k);

                    // 카드가 모로 서서 안 보이는 순간에 그림과 그리는 순서를 같이 넘긴다.
                    if (!turned && k >= 0.5f)
                    {
                        taken.sprite = cardBack;
                        baseScale = Fit(taken);   // ⚠ 뒷면은 유닛 크기가 달라 다시 맞춰야 한다
                        taken.sortingOrder = takerOrder + 1;
                        turned = true;
                    }
                }

                yield return null;
            }

            if (endsHidden)
                taken.sprite = cardBack;

            Fit(taken);

            card.position = takenTarget;
            card.rotation = toRot;
            taken.sortingOrder = takerOrder + 1;

            // ⚠ 승부가 났으면 여기서 멈춘다. 배역을 바꿔 두면 마지막 공개가
            // held[i] 를 게임의 i 번째 카드로 알고 덧칠해 <b>내 카드가 조커로 둔갑한다</b>.
            if (!continues)
                yield break;

            // ⭐ 판이 이어지면 손이 바뀌었으니 <b>어느 렌더러가 누구 패인지</b>도 바꿔 끼운다.
            // 이걸 안 하면 다음에 깔 때 두 장이 테이블을 가로질러 순간이동한다 -
            // 카드는 이미 제자리에 있는데 배역만 어긋나 있기 때문이다.
            var leftover = held[left];
            held[0] = spare;     // 가져간 사람이 원래 쥐고 있던 장
            held[1] = taken;     // 방금 가져간 장
            spare = leftover;    // 뺏긴 쪽에 남은 한 장

            // 섞는 연출이 바꿔 칠 두 장.
            shuffleA = held[0];
            shuffleB = held[1];
            shufflePosB = takenTarget;
            shuffleOwnerIsPlayer = takerIsPlayer;
        }

        /// <summary>
        /// ⭐ 진 쪽이 남은 한 장(조커)을 <b>테이블에 눕혀 보여 준다</b>(2026-09-02 사용자 지시).
        /// 이긴 사람의 손패는 건드리지 않는다 - 가져온 장과 원래 쥐고 있던 장이 짝이라는 게
        /// 그대로 보여야 한다.
        /// </summary>
        public IEnumerator LayDownRoutine(int slot)
        {
            if (!measured || slot < 0 || slot >= held.Length || held[slot] == null)
                yield break;

            Stop(slot);

            var shown = held[slot];
            var card = shown.transform;

            Vector3 from = card.position;
            Vector3 to = new Vector3(tableBounds.center.x,
                                     tableBounds.max.y + CardHeight * liftFraction,
                                     tableBounds.center.z);

            Quaternion fromRot = card.rotation;
            Quaternion toRot = Quaternion.Euler(FlatAngle, 0f, 0f);

            float baseScale = card.localScale.x;
            bool turned = false;

            // 테이블 위라 누구의 손패도 아니다. 캐릭터 카드보다는 앞, 내 카드보다는 뒤.
            shown.sortingOrder = TableOrder;

            for (float t = 0f; t < takeDuration; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / takeDuration);
                float e = 1f - (1f - k) * (1f - k);

                card.position = Vector3.Lerp(from, to, e);
                card.rotation = Quaternion.Slerp(fromRot, toRot, e);
                Flip(card, baseScale, k);

                // 모로 서는 순간에 조커를 드러낸다.
                if (!turned && k >= 0.5f)
                {
                    shown.sprite = FaceOf(OldMaid.Joker, -1);
                    baseScale = Fit(shown);   // ⚠ 조커도 유닛 크기가 달라 다시 맞춰야 한다
                    turned = true;
                }

                yield return null;
            }

            shown.sprite = FaceOf(OldMaid.Joker, -1);
            Fit(shown);

            card.position = to;
            card.rotation = toRot;
        }

        /// <summary>
        /// ⭐ 가져간 사람이 <b>두 장을 번갈아 바꿔 친다</b>(2026-09-02 사용자 기획).
        /// 게임에는 아무 영향이 없다 - 어느 쪽이 조커인지 <b>상대를 헷갈리게 하려는</b> 손장난이고,
        /// 캐릭터도 나도 똑같이 한다.
        /// </summary>
        public IEnumerator ShuffleRoutine()
        {
            if (shuffleA == null || shuffleB == null)
                yield break;

            int baseOrder = shuffleOwnerIsPlayer ? PlayerOrder : OpponentOrder;
            float lift = CardHeight * shuffleArc;
            int swaps = 0;

            for (int n = 0; n < shuffleTimes; n++)
            {
                // 번갈아 위로 넘긴다 - 늘 같은 쪽이 넘어가면 눈으로 따라가기 쉬워진다.
                bool aOverB = n % 2 == 0;
                shuffleA.sortingOrder = baseOrder + (aOverB ? 3 : 1);
                shuffleB.sortingOrder = baseOrder + (aOverB ? 1 : 3);

                Vector3 fromA = shufflePosA;
                Vector3 fromB = shufflePosB;

                for (float t = 0f; t < shuffleStepDuration; t += Time.deltaTime)
                {
                    float k = Mathf.Clamp01(t / shuffleStepDuration);
                    float hop = Mathf.Sin(k * Mathf.PI) * lift;

                    Vector3 a = Vector3.Lerp(fromA, fromB, k);
                    Vector3 b = Vector3.Lerp(fromB, fromA, k);

                    a.y += aOverB ? hop : -hop * 0.25f;
                    b.y += aOverB ? -hop * 0.25f : hop;

                    shuffleA.transform.position = a;
                    shuffleB.transform.position = b;
                    yield return null;
                }

                shuffleA.transform.position = fromB;
                shuffleB.transform.position = fromA;

                // 자리를 맞바꿨으니 다음 바퀴는 여기서 시작한다.
                Vector3 swap = shufflePosA;
                shufflePosA = shufflePosB;
                shufflePosB = swap;
                swaps++;
            }

            shuffleA.sortingOrder = baseOrder;
            shuffleB.sortingOrder = baseOrder + 1;

            // 홀수 번 바꿔 쳤으면 두 장이 자리를 바꾼 채로 끝난다. 배역도 같이 바꿔 둬야
            // 다음에 깔 때 제자리로 튀지 않는다.
            if (swaps % 2 == 1 && held.Length >= 2)
            {
                var first = held[0];
                held[0] = held[1];
                held[1] = first;
            }
        }

        /// <summary>판을 비운다.</summary>
        public void Clear()
        {
            pickable = false;
            shuffleA = shuffleB = null;

            for (int i = 0; i < held.Length; i++)
            {
                Stop(i);
                if (held[i] != null)
                    held[i].gameObject.SetActive(false);
            }

            if (spare != null)
                spare.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- 안쪽

        private float CardHeight => tableBounds.size.z * cardHeightFraction;

        /// <summary>테이블에 <b>납작하게</b> 눕힌 각도. 앞면이 위를 본다.</summary>
        private const float FlatAngle = 90f;

        /// <summary>
        /// 그 사람이 쥐는 각도.
        ///
        /// ⚠ <b>스프라이트의 앞면 법선은 -Z 다</b>(+Z 가 아니다). 그래서 X 회전각을 그대로 쓰면
        /// 카드가 뒤를 보여 <b>상하로 반전된 채</b> 그려진다(2026-09-02 사용자 지적).
        /// <c>Euler(angle,0,0)</c> 을 기준으로 읽으면:
        /// <list type="bullet">
        /// <item><c>0</c> - 앞면이 캐릭터를 본다(나에게는 뒷면)</item>
        /// <item><c>90</c> - 테이블에 납작하게 눕는다</item>
        /// <item><c>180</c> - 앞면이 나를 똑바로 본다</item>
        /// </list>
        /// 그래서 "0 이면 나를 보고 90 이면 눕는다"는 뜻의 tilt 값을 <c>180 - tilt</c> 로 바꿔 준다.
        /// 이러면 <b>양쪽 카드 모두 앞면이 카메라를 향해</b> 반전이 없다 - 남의 패는 그 앞면에
        /// 뒷면 그림이 그려져 있을 뿐이고, 그게 실제로 건너편에서 보이는 모습이다.
        /// </summary>
        private Quaternion Hold(bool isPlayer)
            => Quaternion.Euler(180f - (isPlayer ? playerTilt : opponentTilt), 0f, 0f);

        /// <summary>그 사람 앞의 자리. <paramref name="count"/> 장을 좌우로 벌린다.</summary>
        private Vector3 Seat(int slot, int count, SpriteRenderer card, bool isPlayer)
        {
            float cardHeight = CardHeight;
            float cardWidth = card.sprite != null
                ? card.sprite.bounds.size.x * card.transform.localScale.x
                : cardHeight * 0.75f;

            float tilt = isPlayer ? playerTilt : opponentTilt;
            float sink = cardHeight * 0.5f * Mathf.Cos(tilt * Mathf.Deg2Rad);
            float y = tableBounds.max.y + sink + cardHeight * liftFraction;

            float step = cardWidth * spreadFraction * 2f;
            float x = tableBounds.center.x + (slot - (count - 1) * 0.5f) * step;

            // 자기 쪽으로 붙인다. 내 것이면 화면 앞(+Z), 캐릭터 것이면 건너편(-Z).
            float side = isPlayer ? 1f : -1f;
            float z = tableBounds.center.z + side * tableBounds.size.z * sideOffsetFraction;

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 숫자와 무늬로 그림을 고른다.
        /// ⚠ <b>무늬를 여기서 굴리면 안 된다</b> - 다시 그릴 때마다 카드가 바뀌어 보인다
        /// (2026-09-02 사용자 지적, 블랙잭에서도 같은 걸 겪었다). 무늬는 규칙 쪽이 판 시작 때
        /// 한 번 정해 넘겨준다.
        /// </summary>
        private Sprite FaceOf(int card, int suit)
        {
            if (card == OldMaid.Joker)
                return jokerFace != null ? jokerFace : cardBack;

            int suits = cardFaces != null && cardFaces.Length >= 13
                ? Mathf.Max(1, cardFaces.Length / 13)
                : 1;

            int index = card - 1 + (suit < 0 ? 0 : suit % suits) * 13;
            return cardFaces != null && index >= 0 && index < cardFaces.Length
                ? cardFaces[index]
                : cardBack;
        }

        /// <summary>
        /// 카드 높이를 테이블 기준으로 맞추고 콜라이더도 그 크기로. 맞춘 배율을 돌려준다.
        ///
        /// ⚠⚠ <b>그림을 바꾸면 반드시 다시 불러야 한다.</b> 뒷면·조커(PPU 50)와 트럼프 시트(PPU 100)는
        /// 픽셀 크기가 같아도 <b>유닛 크기가 정확히 두 배 차이</b>라, 그림만 갈아 끼우면 그 카드만
        /// 두 배로 커진다(2026-09-02 사용자 지적 "다시 크기 문제가 돌아왔어").
        ///
        /// ⚠ <b>Renderer.bounds 로 재면 안 된다</b> - 회전이 섞인 월드 크기라 눕힐수록 커진다
        /// (블랙잭 카드에서 겪은 버그). Sprite.bounds 가 그림 자체의 크기다.
        /// </summary>
        private float Fit(SpriteRenderer card)
        {
            if (card.sprite == null)
                return card.transform.localScale.x;

            Vector3 size = card.sprite.bounds.size;
            float scale = size.y > 0.0001f ? CardHeight / size.y : 1f;
            card.transform.localScale = Vector3.one * scale;

            var box = card.GetComponent<BoxCollider>();
            if (box != null)
                box.size = new Vector3(size.x, size.y, size.y * 0.05f);

            return scale;
        }

        private IEnumerator SlideRoutine(int slot, Vector3 to)
        {
            var card = held[slot].transform;
            Vector3 from = card.position;

            for (float t = 0f; t < offerDuration; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / offerDuration);
                k = 1f - (1f - k) * (1f - k);   // 끝에서 부드럽게 멎는다
                card.position = Vector3.Lerp(from, to, k);
                yield return null;
            }

            card.position = to;
            moving[slot] = null;
        }

        /// <summary>
        /// ⭐ 카드를 뒤집는 모양. <b>세로로 납작해졌다가 다시 펴진다</b> -
        /// 이게 <b>X 축으로 도는</b> 카드의 생김새다(2026-09-02 사용자 지시: "y축이 아닌 x축으로").
        ///
        /// 진짜로 180도 돌리지 않는 이유: 그러면 끝 각도가 목표에서 반 바퀴 어긋난 채 끝나고,
        /// 그림을 바꾸는 순간도 카메라 각도에 따라 드러난다. 납작해지는 순간은 <b>어느 각도에서든</b>
        /// 안 보이므로 거기서 그림을 갈아 끼우면 된다.
        /// </summary>
        private static void Flip(Transform card, float baseScale, float k)
        {
            float squeeze = Mathf.Abs(Mathf.Cos(k * Mathf.PI));
            card.localScale = new Vector3(baseScale, baseScale * squeeze, baseScale);
        }

        /// <summary>자리만 옮기는 짧은 미끄러짐. 슬롯에 매이지 않는다.</summary>
        private IEnumerator SlideTo(Transform card, Vector3 to, float duration)
        {
            Vector3 from = card.position;

            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / duration);
                k = 1f - (1f - k) * (1f - k);
                card.position = Vector3.Lerp(from, to, k);
                yield return null;
            }

            card.position = to;
        }

        private void Stop(int slot)
        {
            if (moving[slot] != null)
            {
                StopCoroutine(moving[slot]);
                moving[slot] = null;
            }
        }

        private SpriteRenderer Ensure(ref SpriteRenderer slot, string name)
        {
            if (slot == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, worldPositionStays: false);

                slot = go.AddComponent<SpriteRenderer>();
                go.AddComponent<BoxCollider>().isTrigger = true;
            }

            return slot;
        }

        private bool Measure()
        {
            if (measured)
                return true;

            if (table == null)
                return false;

            var renderers = table.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers.Length == 0)
                return false;

            tableBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                tableBounds.Encapsulate(renderers[i].bounds);

            measured = tableBounds.size.z > 0.0001f;
            return measured;
        }
    }
}

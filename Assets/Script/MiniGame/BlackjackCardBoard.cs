using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 블랙잭의 카드를 <b>테이블 위 3D 오브젝트로</b> 놓는다(2026-09-02 사용자 지시).
    ///
    /// UI 로 그리면 화면에 붙은 그림이라 "테이블에서 논다"는 느낌이 안 난다. 월드 공간에
    /// 놓으면 테이블·캐릭터와 같은 공간에 있어서 원근도 조명도 함께 받는다.
    ///
    /// ⭐ <b>새로 받은 카드는 각자 자리에서 밀어 넣는다</b> - 상대 카드는 테이블 건너편에서,
    /// 내 카드는 화면 앞쪽에서 자기 줄로 미끄러져 들어온다. 누가 받은 장인지가 눈으로 읽힌다.
    ///
    /// <b>크기와 자리는 테이블을 재서 정한다</b> - 좌표를 숫자로 박으면 임포트 배율이 바뀔 때
    /// 통째로 어긋난다(<see cref="MiniGameStage"/> 에서 한 번 물린 함정).
    /// 테이블은 그 <see cref="MiniGameStage"/> 가 Awake 에서 크기를 바꾸므로,
    /// 여기서는 <b>처음 카드를 놓을 때</b> 잰다.
    /// </summary>
    public class BlackjackCardBoard : MonoBehaviour
    {
        [Header("이어붙일 것들")]
        [Tooltip("카드 앞면 52장(무늬 순서대로 13장씩). 포커 화면과 같은 것을 물려도 된다.")]
        [SerializeField] private Sprite[] cardFaces;

        [Tooltip("카드 뒷면. 딜러가 덮어둔 장을 이걸로 그린다.")]
        [SerializeField] private Sprite cardBack;

        [Tooltip("테이블 모델의 뿌리. 이걸 재서 카드 크기와 자리를 정한다.")]
        [SerializeField] private Transform table;

        [Header("크기 - 테이블에 대한 비율")]
        [Tooltip("카드 높이 = 테이블 깊이 x 이 값.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float cardHeightFraction = 0.34f;

        [Tooltip("두 줄이 테이블 가운데에서 앞뒤로 얼마나 떨어지는지(테이블 깊이 대비).")]
        [Range(0f, 0.5f)]
        [SerializeField] private float rowOffsetFraction = 0.22f;

        [Tooltip("카드끼리 얼마나 내미는지. 카드 폭의 이 비율만큼씩 오른쪽으로 나간다. " +
                 "<b>0.2 안팎이면 왼쪽 위 구석의 숫자만 드러난다</b>(2026-09-02 사용자 지시). " +
                 "1에 가까우면 서로 안 겹친다.")]
        [Range(0.1f, 1.2f)]
        [SerializeField] private float spacingFraction = 0.22f;

        [Tooltip("<b>내 손패만</b> 반대쪽(왼쪽)으로 펼칠지. 상대 줄과 같은 방향으로 펼치면 " +
                 "내 쪽은 숫자가 가려져 읽기 나빴다(2026-09-02 사용자 지적) - " +
                 "두 줄이 서로 마주 보는 모양이 되어야 각자 자기 숫자가 드러난다.")]
        [SerializeField] private bool playerFansLeft = true;

        [Header("모양")]
        [Tooltip("카드를 뒤로 눕히는 각도. 0이면 화면을 마주 보고 서 있고, 90에 가까울수록 " +
                 "테이블에 눕는다.\n" +
                 "⚠ <b>카메라가 수평이면 90도는 옆에서 보는 셈이라 거의 안 보인다</b> - 카메라를 살짝 내려다보게 하거나 75~85 로 낮추면 읽힌다.")]
        [Range(0f, 90f)]
        [SerializeField] private float tiltAngle = 90f;

        [Tooltip("테이블 상판에서 <b>더</b> 띄우는 높이(카드 높이 대비). " +
                 "눕힌 각도 때문에 모서리가 상판을 파고드는 분은 자동으로 더해진다.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float liftFraction = 0.02f;

        [Header("미는 연출")]
        [Tooltip("한 장이 자기 자리로 미끄러져 들어오는 시간(초).")]
        [Min(0.01f)]
        [SerializeField] private float slideDuration = 0.35f;

        [Tooltip("미는 시작점이 테이블 가운데에서 얼마나 떨어져 있는지(테이블 깊이 대비). " +
                 "상대는 건너편에서, 나는 화면 앞쪽에서 민다.")]
        [Range(0.3f, 2f)]
        [SerializeField] private float pushDistanceFraction = 0.9f;

        // 놓여 있는 카드들. 판마다 지우지 않고 <b>돌려 쓴다</b>(모바일 방침 - 매 판 Destroy 하지 않는다).
        private readonly List<SpriteRenderer> opponentCards = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> playerCards = new List<SpriteRenderer>();

        // 무늬는 그 카드를 처음 놓을 때 정하고 판이 끝날 때까지 유지한다.
        private readonly List<int> opponentSuits = new List<int>();
        private readonly List<int> playerSuits = new List<int>();

        // 카드가 캐릭터보다 앞에, 내 카드가 상대 카드보다 앞에 오도록 매긴 번호.
        private const int OpponentOrder = 20;
        private const int PlayerOrder = 60;

        private Bounds tableBounds;
        private bool measured;

        /// <summary>판을 비운다. 카드 오브젝트는 남겨두고 감추기만 한다.</summary>
        public void Clear()
        {
            HideFrom(opponentCards, 0);
            HideFrom(playerCards, 0);
            opponentSuits.Clear();
            playerSuits.Clear();
        }

        /// <summary>
        /// 두 손패를 테이블에 놓는다. <b>새로 늘어난 장만</b> 밀어 넣는 연출이 붙는다.
        /// </summary>
        /// <param name="opponentHiddenFrom">캐릭터의 이 번째 장부터 뒷면으로 덮는다.
        /// 음수면 다 보여준다. 딜러가 홀 카드를 감출 때 1 을 넘긴다.</param>
        public void ShowHands(IReadOnlyList<int> opponentHand, IReadOnlyList<int> playerHand,
            int opponentHiddenFrom = -1)
        {
            if (!Measure())
                return;

            Place(opponentCards, opponentSuits, opponentHand, isOpponent: true,
                  hiddenFrom: opponentHiddenFrom);
            Place(playerCards, playerSuits, playerHand, isOpponent: false, hiddenFrom: -1);
        }

        // ---------------------------------------------------------------- 안쪽

        /// <summary>테이블을 잰다. 한 번 재면 그대로 쓴다(무대가 Awake 에서 크기를 다 잡아둔 뒤다).</summary>
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

        private void Place(List<SpriteRenderer> slots, List<int> suits,
            IReadOnlyList<int> hand, bool isOpponent, int hiddenFrom)
        {
            int count = hand != null ? hand.Count : 0;

            int suitCount = cardFaces != null && cardFaces.Length >= 13
                ? Mathf.Max(1, cardFaces.Length / 13)
                : 1;

            while (suits.Count < count)
                suits.Add(Random.Range(0, suitCount));

            float cardHeight = tableBounds.size.z * cardHeightFraction;

            // 눕힐수록 카드의 아래 모서리가 아래로 내려간다 - 그만큼 자동으로 띄운다.
            float sink = cardHeight * 0.5f * Mathf.Cos(tiltAngle * Mathf.Deg2Rad);
            float top = tableBounds.max.y + sink + cardHeight * liftFraction;

            // 상대 줄은 테이블 건너편(-Z), 내 줄은 화면 쪽(+Z).
            float side = isOpponent ? -1f : 1f;
            float rowZ = tableBounds.center.z + side * tableBounds.size.z * rowOffsetFraction;
            float sourceZ = tableBounds.center.z + side * tableBounds.size.z * pushDistanceFraction;

            var tilt = Quaternion.Euler(tiltAngle, 0f, 0f);

            for (int i = 0; i < count; i++)
            {
                var card = Ensure(slots, i);
                bool isNew = !card.gameObject.activeSelf;

                // 덮어둘 장이면 뒷면으로. 뒷면 그림이 없으면 그냥 앞면을 쓴다(게임은 돌아가야 한다).
                bool hidden = hiddenFrom >= 0 && i >= hiddenFrom && cardBack != null;

                int index = hand[i] - 1 + suits[i] * 13;
                if (!hidden && (cardFaces == null || index < 0 || index >= cardFaces.Length))
                {
                    card.gameObject.SetActive(false);
                    continue;
                }

                card.sprite = hidden ? cardBack : cardFaces[index];
                card.gameObject.SetActive(true);

                // 카드 높이를 테이블 기준으로 맞춘다(그림 원본 크기와 무관하게).
                //
                // ⚠ <b>Renderer.bounds 로 재면 안 된다</b>(2026-09-02 버그): 그건 <b>회전까지 반영된
                // 월드 AABB</b> 라, 카드를 눕혀두면 세로 크기가 cos(각도) 만큼 납작하게 잡힌다.
                // 그 값으로 배율을 다시 구하면 카드가 1/cos(각도) 배씩 <b>커지고, 손패를 다시
                // 그릴 때마다 곱해져서</b> 한 장 받을 때마다 눈덩이처럼 불어난다(70도면 매번 2.9배).
                // 90도에서는 0으로 나누는 셈이라 아예 터진다.
                //
                // Sprite.bounds 는 <b>그림 자체의 로컬 크기</b>라 회전·배율과 무관하다 - 이게 맞는 자.
                Vector3 spriteSize = card.sprite.bounds.size;
                float scale = spriteSize.y > 0.0001f ? cardHeight / spriteSize.y : 1f;
                card.transform.localScale = Vector3.one * scale;

                float cardWidth = spriteSize.x * scale;

                // ⭐ 내 줄은 반대쪽으로 펼친다 - 그래야 뒤에 놓인 장의 <b>숫자 구석</b>이
                // 안 가려진다. 상대 줄과 같은 방향이면 내 쪽만 숫자가 덮인다.
                float fan = (!isOpponent && playerFansLeft) ? -1f : 1f;

                float step = cardWidth * spacingFraction * fan;
                float x = tableBounds.center.x + (i - (count - 1) * 0.5f) * step;

                // 뒤 카드가 앞 카드 위로 올라온다 - 오른쪽으로 내밀어 쌓는 모양이다.
                var target = new Vector3(x, top + i * cardHeight * 0.012f, rowZ);

                card.transform.rotation = tilt;

                // ⚠ <b>그리는 순서를 거리로 맡기면 안 된다</b> - 누워 있는 카드끼리는
                // 거리 차가 거의 없어 순서가 흔들린다. 번호로 못 박는다.
                card.sortingOrder = (isOpponent ? OpponentOrder : PlayerOrder) + i;

                if (isNew)
                {
                    // ⭐ 새로 받은 장은 <b>자기 자리에서</b> 밀어 넣는다.
                    var from = new Vector3(x, top, sourceZ);
                    card.transform.position = from;
                    StartCoroutine(SlideRoutine(card.transform, from, target));
                }
                else
                    card.transform.position = target;
            }

            HideFrom(slots, count);
        }

        private System.Collections.IEnumerator SlideRoutine(Transform card, Vector3 from, Vector3 to)
        {
            for (float t = 0f; t < slideDuration; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / slideDuration);

                // 끝에서 부드럽게 멎는다 - 밀어 놓은 카드가 스르륵 서는 느낌.
                k = 1f - (1f - k) * (1f - k);

                card.position = Vector3.Lerp(from, to, k);
                yield return null;
            }

            card.position = to;
        }

        private SpriteRenderer Ensure(List<SpriteRenderer> slots, int index)
        {
            while (slots.Count <= index)
            {
                var go = new GameObject("Card" + slots.Count);
                go.transform.SetParent(transform, worldPositionStays: false);
                go.SetActive(false);
                slots.Add(go.AddComponent<SpriteRenderer>());
            }

            return slots[index];
        }

        private static void HideFrom(List<SpriteRenderer> slots, int from)
        {
            for (int i = from; i < slots.Count; i++)
            {
                if (slots[i] != null)
                    slots[i].gameObject.SetActive(false);
            }
        }
    }
}

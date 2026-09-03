using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 퍼즐이 매치될 때마다 적 초상화 근처의 무작위 위치에 데미지 숫자를 띄운다.
    /// 스탠드업 타임 중에는 BoardInputController가 OnMatchDamage를 발행하지 않으므로
    /// (그때는 조각이 고정되고 별도 연출로 정산됨) 여기서 따로 걸러낼 필요가 없다.
    ///
    /// 텍스트는 시작할 때 template을 복제해 풀에 채워두고 계속 재사용한다 - 매치는 매우 잦아서
    /// 그때마다 Instantiate/Destroy하면 GC와 발열로 바로 이어진다. 풀이 비면 새로 만들지 않고
    /// 가장 오래된 것을 회수해서 다시 쓰므로, 실행 중 생성은 단 한 번도 일어나지 않는다.
    ///
    /// 애니메이션은 팝업마다 코루틴을 띄우지 않고 이 컴포넌트의 Update 하나가 전부 굴린다.
    /// </summary>
    public class DamagePopupUI : MonoBehaviour
    {
        [SerializeField] private BoardInputController boardInput;

        [Tooltip("데미지가 뜰 기준 위치. 보통 EnemyImage의 RectTransform.")]
        [SerializeField] private RectTransform anchor;

        [Tooltip("복제해서 풀을 채울 원본. 평소엔 비활성 상태로 씬에 놔둔다.")]
        [SerializeField] private Text template;

        [Tooltip("데미지가 뜨는 순간 함께 움찔거릴 대상(선택). 보통 EnemyImage의 HitFlinchUI. " +
                 "HitFlinchUI 자체는 재사용 컴포넌트라, 나중에 적의 방해 효과로 리더/파트너가 맞을 때는 " +
                 "그쪽 초상화의 HitFlinchUI를 그 연출 코드에서 직접 부르면 된다.")]
        [SerializeField] private HitFlinchUI targetFlinch;

        [Header("퍼짐")]
        [Tooltip("무작위로 흩어질 반경 - anchor 가로 크기 대비 비율이라 해상도가 바뀌어도 비율이 유지된다.")]
        [SerializeField] private float spreadFactor = 0.55f;

        [Header("연출 - 팽창하며 등장")]
        [Tooltip("0에서 부풀어 제 크기에 자리 잡기까지의 시간(초). 짧을수록 '탁' 하고 튀어나온다.")]
        [SerializeField] private float popInDuration = 0.2f;

        [Tooltip("정점을 지나 숫자가 떠 있는 시간(초). <b>읽을 시간이라 넉넉히 준다</b> - " +
                 "자릿수가 늘어날수록(1,134,000 같은 수) 눈으로 훑는 데 시간이 걸린다. " +
                 "여기를 늘리면 BoardInputController.standUpDamageReadDuration 도 같이 늘릴 것.")]
        [SerializeField] private float holdDuration = 1f;

        [Tooltip("부풀 때 얼마나 세게 튀어나갈지. 2.5면 최대 1.28배까지 커졌다가 돌아온다 " +
                 "(1.7이면 1.18배, 3.5면 1.41배).")]
        [SerializeField] private float popInOvershoot = 2.5f;

        [Tooltip("정점을 지난 뒤 남는 <b>반동</b>의 크기. 0.12면 ±12%쯤 출렁인다.")]
        [SerializeField] private float wobbleAmount = 0.12f;

        [Tooltip("반동이 한 번 출렁이는 데 걸리는 시간(초). 짧을수록 잘게 떨린다.")]
        [SerializeField] private float wobblePeriod = 0.3f;

        [Tooltip("반동이 잦아드는 속도. <b>작을수록 오래 남는다</b> - 1.2면 사라질 때까지 " +
                 "눈에 띄게 출렁이고, 크게 잡으면 금방 잠잠해진다.")]
        [SerializeField] private float wobbleDecay = 1.2f;

        [Tooltip("부풀 때 가로/세로가 서로 반대로 늘어나는 정도(젤리처럼). 0이면 그냥 커졌다 작아진다.")]
        [Range(0f, 1f)]
        [SerializeField] private float squashFactor = 0.35f;

        [Header("연출 - 뿅 사라지기")]
        [Tooltip("사라지는 데 걸리는 시간(초).")]
        [SerializeField] private float popOutDuration = 0.15f;

        [Tooltip("사라지기 직전에 살짝 부푸는 정도. 이게 있어야 '뿅' 하고 터지듯 사라진다.")]
        [SerializeField] private float popOutOvershoot = 1.35f;

        [Header("연출 - 그 밖")]
        [Tooltip("일반 매치 숫자가 떠오르는 거리 - anchor 세로 크기 대비 비율. " +
                 "<b>0이면 제자리에서 부풀었다 사라진다</b>. 숫자가 겹쳐 읽기 어려우면 조금 올릴 것.")]
        [SerializeField] private float riseFactor;

        [Header("스탠드업 총 데미지 (천천히 떠오르기)")]
        [Tooltip("스탠드업이 끝날 때 뜨는 <b>총 데미지</b>는 다른 연출을 쓴다 - 부풀었다 터지지 않고 " +
                 "예전처럼 천천히 떠오르며 흐려진다. 한 판의 결산이라 요란한 것보다 " +
                 "차분히 읽히는 편이 어울린다(사용자 방침).")]
        [SerializeField] private float standUpRiseFactor = 0.45f;

        [Tooltip("총 데미지가 떠 있는 전체 시간(초). 일반 매치보다 길게 잡아도 되지만 " +
                 "BoardInputController.standUpDamageReadDuration 보다는 짧아야 한다.")]
        [SerializeField] private float standUpDuration = 1.35f;

        [SerializeField] private int poolSize = 12;

        /// <summary>
        /// 숫자 하나가 뜨고 사라지기까지 걸리는 전체 시간(초).
        ///
        /// <b>BoardInputController.standUpDamageReadDuration 보다 짧아야 한다</b> -
        /// 스탠드업 종료 데미지는 그 시간만큼 판을 붙잡아두고 숫자를 읽게 하는데,
        /// 이게 더 길면 숫자가 아직 떠 있는데 판이 먼저 밝아지며 조각이 쏟아진다.
        /// (지금: 이쪽 1.35초 &lt; 저쪽 1.5초)
        /// </summary>
        public float TotalDuration => Mathf.Max(0.01f, popInDuration)
                                      + Mathf.Max(0f, holdDuration)
                                      + Mathf.Max(0.01f, popOutDuration);

        private sealed class Popup
        {
            public RectTransform rect;
            public Text text;
            public Vector3 startLocal;
            public float elapsed;

            /// <summary>true 면 "천천히 떠오르기"(스탠드업 총 데미지), false 면 "부풀었다 뿅"(일반 매치).</summary>
            public bool rises;
        }

        private readonly Stack<Popup> pool = new Stack<Popup>();
        private readonly List<Popup> active = new List<Popup>();
        private RectTransform selfRect;

        private void Awake()
        {
            selfRect = (RectTransform)transform;

            if (template == null)
                return;

            template.gameObject.SetActive(false); // 원본은 절대 화면에 나오지 않게

            for (int i = 0; i < poolSize; i++)
            {
                var copy = Instantiate(template, transform);
                copy.gameObject.SetActive(false);
                pool.Push(new Popup
                {
                    rect = (RectTransform)copy.transform,
                    text = copy
                });
            }
        }

        private void OnEnable()
        {
            if (boardInput == null)
                return;

            boardInput.OnMatchDamage += Show;
            boardInput.OnStandUpDamage += ShowCentered;
        }

        private void OnDisable()
        {
            if (boardInput == null)
                return;

            boardInput.OnMatchDamage -= Show;
            boardInput.OnStandUpDamage -= ShowCentered;
        }

        /// <summary>
        /// 스탠드업 종료 데미지처럼 "한 방"을 강조해야 할 때 - 흩어뜨리지 않고 적 한가운데에 띄운다.
        /// </summary>
        public void ShowCentered(int damage) => Show(damage, false, true);

        /// <summary>데미지 숫자 하나를 적 근처 무작위 위치에 띄운다.</summary>
        public void Show(int damage) => Show(damage, true, false);

        private void Show(int damage, bool scatter, bool rises)
        {
            // 움찔 연출은 숫자가 뜨든 말든(풀이 꽉 찼든) 항상 재생되도록 먼저 호출한다.
            if (targetFlinch != null)
                targetFlinch.Flinch();

            if (anchor == null || selfRect == null)
                return;

            var popup = Rent();
            if (popup == null)
                return;

            // anchor의 중심을 이 레이어의 로컬 좌표로 변환한다. 두 RectTransform의 앵커/피벗 설정이
            // 서로 달라도 안전하게 맞아떨어지도록 월드를 한 번 거친다.
            Vector3 world = anchor.TransformPoint(anchor.rect.center);
            Vector3 local = selfRect.InverseTransformPoint(world);

            // 흩어지는 반경과 떠오르는 거리를 anchor 크기에 비례시켜서, 화면 크기가 달라져도
            // 적 초상화 대비 같은 비율로 보이게 한다(HUD 전체가 레터박스로 스케일되므로).
            Vector2 offset = scatter
                ? Random.insideUnitCircle * (anchor.rect.width * spreadFactor)
                : Vector2.zero;

            popup.startLocal = new Vector3(local.x + offset.x, local.y + offset.y, 0f);
            popup.elapsed = 0f;
            popup.rises = rises;
            popup.rect.localPosition = popup.startLocal;

            // 떠오르는 쪽은 처음부터 제 크기다(부풀지 않는다).
            popup.rect.localScale = rises ? Vector3.one : Vector3.zero;

            popup.text.text = damage.ToString("N0");
            var color = popup.text.color;
            color.a = 1f;
            popup.text.color = color;

            popup.text.gameObject.SetActive(true);
            active.Add(popup);
        }

        private Popup Rent()
        {
            if (pool.Count > 0)
                return pool.Pop();

            // 풀이 비었으면 새로 만들지 않고 가장 오래된 것을 빼앗아 다시 쓴다.
            // 실행 중 Instantiate를 한 번도 하지 않기 위한 처리 - 동시에 뜨는 숫자가
            // poolSize를 넘으면 가장 먼저 뜬 숫자가 조금 일찍 사라질 뿐이다.
            if (active.Count == 0)
                return null;

            var oldest = active[0];
            active.RemoveAt(0);
            return oldest;
        }

        private void Update()
        {
            if (active.Count == 0)
                return;

            float rise = anchor != null ? anchor.rect.height * riseFactor : 0f;

            float popIn = Mathf.Max(0.01f, popInDuration);
            float hold = Mathf.Max(0f, holdDuration);
            float popOut = Mathf.Max(0.01f, popOutDuration);
            float total = popIn + hold + popOut;

            // 뒤에서부터 훑어야 중간에서 제거해도 인덱스가 밀리지 않는다.
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var popup = active[i];
                popup.elapsed += Time.deltaTime; // timeScale을 따르므로 일시정지 중엔 함께 멈춤

                float e = popup.elapsed;

                if (popup.rises)
                {
                    // 스탠드업 총 데미지 - 예전 방식 그대로 천천히 떠오르며 흐려진다.
                    float span = Mathf.Max(0.01f, standUpDuration);
                    float rp = e / span;
                    if (rp >= 1f)
                    {
                        popup.text.gameObject.SetActive(false);
                        active.RemoveAt(i);
                        pool.Push(popup);
                        continue;
                    }

                    float standUpRise = anchor != null ? anchor.rect.height * standUpRiseFactor : 0f;
                    popup.rect.localPosition = popup.startLocal + Vector3.up * (standUpRise * rp);
                    popup.rect.localScale = Vector3.one;

                    // 처음엔 또렷하다가 끝에서 빠르게 사라지도록 제곱으로 감쇠
                    var riseColor = popup.text.color;
                    riseColor.a = 1f - rp * rp;
                    popup.text.color = riseColor;
                    continue;
                }

                if (e >= total)
                {
                    popup.text.gameObject.SetActive(false);
                    active.RemoveAt(i);
                    pool.Push(popup);
                    continue;
                }

                // 크기 = "0에서 부풀어 오르는 곡선" + "오래 남는 반동".
                //
                // 둘을 나눠서 더하는 이유: 감쇠 진동 하나로 처리하면(1 - e^-dt·cos ωt) 처음 부풀 때
                // 1.6배까지 튀고 곧바로 0.6배까지 쪼그라들어 숫자가 망가진 것처럼 보인다.
                // 부푸는 세기와 남는 반동의 크기를 따로 잡을 수가 없기 때문이다.
                // 나눠두면 "크게 한 번 부풀되(1.28배) 그 뒤 잔물결은 작게(±12%) 오래" 가 된다.
                float p = Mathf.Clamp01(e / popIn);
                float q = p - 1f;
                float grow = 1f + (popInOvershoot + 1f) * q * q * q + popInOvershoot * q * q;

                float wobble = wobbleAmount * Mathf.Exp(-wobbleDecay * e)
                               * Mathf.Sin(2f * Mathf.PI * e / Mathf.Max(0.01f, wobblePeriod));

                float scale = grow + wobble;

                float alpha = 1f;
                if (e > popIn + hold)
                {
                    // 마지막에 살짝 더 부풀었다가 순식간에 오므라든다 - "뿅".
                    // 진행 중이던 반동에 곱하는 방식이라 사라지기 시작하는 순간 크기가 튀지 않는다.
                    float out01 = (e - popIn - hold) / popOut;
                    scale *= Mathf.Lerp(1f, popOutOvershoot, Mathf.Sin(out01 * Mathf.PI))
                             * (1f - out01 * out01);
                    alpha = 1f - out01;
                }

                // 부풀면 가로로 넓어지고 세로로 눌린다(젤리). 1에서 벗어난 만큼만 적용하므로
                // 잠잠해지면 저절로 정사각형 비율로 돌아온다.
                float squash = (scale - 1f) * squashFactor;
                popup.rect.localScale = new Vector3(scale + squash, scale - squash, 1f);

                popup.rect.localPosition = popup.startLocal + Vector3.up * (rise * (e / total));

                var color = popup.text.color;
                color.a = alpha;
                popup.text.color = color;
            }
        }
    }
}

using System.Text;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 상단 좌측 점수 표시 배너. 적에게 준 데미지가 그대로 점수로 누적된다
    /// (일반 매치와 스탠드업 종료 데미지 둘 다).
    /// 천 단위 구분 콤마를 붙여서 표시(예: 276,100). 'Score' 글자는 배너 그림에 이미 있어서
    /// 텍스트로는 숫자만 쓴다.
    ///
    /// 점수가 오를 때 곧바로 바뀌지 않고 목표값까지 숫자가 차르륵 굴러간다. 굴러가는 동안 새 점수가
    /// 또 들어와도 지금 보이는 값에서 이어서 굴러가므로 끊기거나 되돌아가지 않는다.
    /// 코루틴을 쓰지 않고 Update에서 직접 보간한다 - 굴릴 대상이 이 컴포넌트 하나뿐이라 코루틴을
    /// 띄웠다 없앴다 할 이유가 없고, 다 따라잡은 뒤에는 맨 첫 줄에서 즉시 반환하므로 부담도 없다.
    /// Time.deltaTime을 쓰므로 일시정지(timeScale=0) 중에는 저절로 멈춘다.
    /// </summary>
    public class ScoreUI : MonoBehaviour
    {
        [SerializeField] private Text scoreText;

        [Tooltip("숫자 앞에 붙일 문구. <b>기본은 비어 있다</b> - 배너 그림에 'Score' 가 이미 " +
                 "그려져 있어서 글자로 또 쓰면 두 번 나온다. 다른 배너를 쓸 때만 채울 것.")]
        [SerializeField] private string prefix = "";

        [Tooltip("데미지를 점수로 누적할 대상. 비워두면 외부에서 AddScore를 직접 불러야 한다.")]
        [SerializeField] private BoardInputController boardInput;

        [Tooltip("점수가 목표값까지 굴러가는 데 걸리는 시간(초). 오른 폭과 상관없이 항상 이 시간이 걸린다 - " +
                 "스탠드업 한 방(백만 단위)과 일반 매치(수천)의 차이가 워낙 커서, 초당 몇 점씩 올리는 " +
                 "방식으로는 큰 수가 끝없이 굴러가 버린다.")]
        [SerializeField] private float rollDuration = 0.5f;

        // 논리값(진짜 점수)과 화면에 지금 그려진 값을 분리해서 들고 있는다.
        private int targetScore;
        private int displayedScore;

        // 이번 굴리기의 시작값과 경과 시간. 굴러가는 도중 점수가 또 들어오면 "지금 보이는 값"에서
        // 다시 시작하므로, 숫자가 뒤로 튀거나 뚝 끊기지 않는다.
        private int rollFromScore;
        private float rollElapsed;

        /// <summary>누적된 진짜 점수. 화면이 아직 다 못 따라갔어도 이 값이 정답이다.</summary>
        public int CurrentScore => targetScore;

        /// <summary>지금 화면에 그려져 있는 값.</summary>
        public int DisplayedScore => displayedScore;

        // 매 프레임 문자열을 새로 만들지 않도록 재사용하는 버퍼. Text.text에 넣는 순간 문자열 하나는
        // 어차피 생기지만, prefix + ToString("N0") 조합이 만들던 중간 문자열은 이걸로 없어진다.
        private readonly StringBuilder builder = new StringBuilder(32);

        private void Awake()
        {
            ApplyVisual(); // 씬에 적어둔 임시 문구 대신 실제 형식으로 맞춰서 시작
        }

        private void OnEnable()
        {
            if (boardInput == null)
                return;

            boardInput.OnMatchDamage += AddScore;
            boardInput.OnStandUpDamage += AddScore;
        }

        private void OnDisable()
        {
            if (boardInput == null)
                return;

            boardInput.OnMatchDamage -= AddScore;
            boardInput.OnStandUpDamage -= AddScore;
        }

        private void Update()
        {
            if (displayedScore == targetScore)
                return; // 다 따라잡음 - 굴릴 게 없으면 여기서 즉시 끝

            rollElapsed += Time.deltaTime;

            long span = (long)targetScore - rollFromScore;

            int next;
            if (rollDuration <= 0f || rollElapsed >= rollDuration || span == 0)
            {
                next = targetScore;
            }
            else
            {
                // 감속 보간(ease-out) - 처음엔 빠르게 훑고 끝에서 천천히 멈춰야 "차르륵"으로 읽힌다.
                float t = rollElapsed / rollDuration;
                float eased = 1f - (1f - t) * (1f - t);

                // 폭이 int를 넘길 수 있어(스탠드업 백만 단위 + 기존 점수) long으로 계산한 뒤 자른다.
                next = (int)(rollFromScore + (long)(span * eased));

                // 보간값이 아직 1점도 못 움직이는 구간(폭이 작고 t가 작을 때)에서 화면이 멈춰
                // 보이는 걸 막기 위해 최소 1점은 움직인다.
                if (next == displayedScore)
                    next = displayedScore + (span > 0 ? 1 : -1);

                // 최소 1점 보정이나 반올림 때문에 목표를 지나치지 않도록 고정
                if (span > 0 ? next > targetScore : next < targetScore)
                    next = targetScore;
            }

            if (next == displayedScore)
                return; // 값이 그대로면 문자열을 새로 만들지 않는다

            displayedScore = next;
            ApplyVisual();
        }

        /// <summary>점수를 특정 값으로 맞추고 그 값까지 굴린다.</summary>
        public void SetScore(int score)
        {
            targetScore = score;
            rollFromScore = displayedScore;
            rollElapsed = 0f;
        }

        /// <summary>굴리기 없이 즉시 반영. 배틀 시작 시 0으로 초기화하는 용도.</summary>
        public void SetScoreImmediate(int score)
        {
            targetScore = score;
            displayedScore = score;
            rollFromScore = score;
            rollElapsed = 0f;
            ApplyVisual();
        }

        /// <summary>굴러가던 걸 끊고 목표값을 즉시 보여준다. 결과 화면처럼 기다릴 수 없을 때 쓴다.</summary>
        public void SnapToTarget() => SetScoreImmediate(targetScore);

        /// <summary>
        /// 점수를 누적. 스탠드업 데미지는 값이 매우 커질 수 있어 합산 중 int를 넘길 수 있으므로
        /// long으로 더한 뒤 상한에서 자른다.
        /// </summary>
        public void AddScore(int amount)
        {
            long next = (long)targetScore + amount;
            SetScore((int)System.Math.Min(next, int.MaxValue));
        }

        private void ApplyVisual()
        {
            if (scoreText == null)
                return;

            builder.Length = 0;
            builder.Append(prefix);
            AppendGrouped(builder, displayedScore);
            scoreText.text = builder.ToString();
        }

        /// <summary>
        /// 천 단위 콤마를 붙여 StringBuilder에 직접 쓴다. ToString("N0")은 호출할 때마다 문자열을
        /// 새로 만드는데, 굴러가는 동안은 이게 매 프레임이라 직접 자릿수를 찍는 쪽으로 바꿨다.
        ///
        /// <b>public 인 이유</b>: 결과 화면(<see cref="BattleResultPanel"/>)의 총점도 같은 방식으로
        /// 굴러가는데, 형식을 두 벌로 두면 같은 숫자가 화면마다 다르게 찍힌다.
        /// </summary>
        public static void AppendGrouped(StringBuilder sb, int value)
        {
            if (value < 0)
            {
                sb.Append('-');
                value = -value; // 점수가 음수가 될 일은 없지만 방어적으로
            }

            int digits = 1;
            for (int v = value; v >= 10; v /= 10)
                digits++;

            int divisor = 1;
            for (int i = 1; i < digits; i++)
                divisor *= 10;

            while (divisor > 0)
            {
                sb.Append((char)('0' + (value / divisor) % 10));
                digits--;

                if (digits > 0 && digits % 3 == 0)
                    sb.Append(',');

                divisor /= 10;
            }
        }
    }
}

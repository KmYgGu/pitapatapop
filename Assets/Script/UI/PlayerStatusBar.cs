using System;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 플레이어 상태 표시줄 - 레벨 / 경험치 / 골드 / 보석 / 하트.
    /// 메인 화면과 스테이지 준비 화면이 <b>같은 것을</b> 보여줘야 해서 컴포넌트로 뺐다
    /// (특히 하트는 시계가 도는 물건이라 구현이 두 벌 있으면 반드시 어긋난다).
    ///
    /// <b>표시 전용이다.</b> 규칙은 <see cref="HeartMeter"/> 와 <see cref="PlayerProfile"/> 에 있다.
    /// 칸이 없는 화면(예: 준비 화면엔 배너가 없다)에서는 그 칸만 비워두면 된다 - 전부 null 검사한다.
    /// </summary>
    public class PlayerStatusBar : MonoBehaviour
    {
        [Header("레벨 / 경험치")]
        [SerializeField] private Text levelText;

        [Tooltip("Image Type = Filled, Horizontal. fillAmount 로 채운다.")]
        [SerializeField] private Image expFill;
        [SerializeField] private Text expPercentText;

        [Header("재화")]
        [SerializeField] private Text goldText;
        [Tooltip("보석. 뽑기와 상점이 함께 쓰는 재화다(상단의 보라색 칸).")]
        [SerializeField] private Text gemText;

        [Header("하트")]
        [Tooltip("왼쪽부터 순서대로. 개수는 HeartMeter.MaxHearts 와 같아야 한다.")]
        [SerializeField] private Image[] heartIcons;
        [SerializeField] private Text heartCountText;

        [Tooltip("다음 하트까지 남은 시간. 가득 차면 \"가득\" 으로 바뀐다(칸이 좁아 두 글자).")]
        [SerializeField] private Text heartTimerText;

        [Header("하트 색")]
        [SerializeField] private Color heartFilledColor = new Color(0.94f, 0.35f, 0.42f);
        [SerializeField] private Color heartEmptyColor = new Color(0.32f, 0.30f, 0.36f);

        /// <summary>
        /// 마지막으로 그린 값. 바뀐 프레임에만 글자를 다시 만든다 - 매 프레임 string 을 새로
        /// 만들면 그것만으로 GC 가 계속 돈다(모바일 방침).
        /// </summary>
        private int lastHeartCount = -1;
        private int lastSecondsToNext = -1;

        private void OnEnable()
        {
            // 값이 바뀌면 알아서 따라간다 - 돈을 건드린 쪽이 화면을 찾아다니지 않아도 된다.
            PlayerProfile.OnCurrencyChanged += RefreshProfile;

            // 화면을 다시 켰을 때 곧바로 최신 값이 보이도록 캐시를 비운다.
            lastHeartCount = -1;
            lastSecondsToNext = -1;

            RefreshProfile();
        }

        private void OnDisable() => PlayerProfile.OnCurrencyChanged -= RefreshProfile;

        private void Update()
        {
            RefreshHearts();
        }

        /// <summary>
        /// 자주 바뀌지 않는 값들(레벨·경험치·재화). 값을 바꾼 쪽에서 다시 불러주면 된다 -
        /// 매 프레임 돌릴 이유가 없다.
        /// </summary>
        public void RefreshProfile()
        {
            if (levelText != null)
                levelText.text = $"Lv.{PlayerProfile.Level}";

            float fraction = PlayerProfile.ExpFraction;

            if (expFill != null)
                expFill.fillAmount = fraction;

            if (expPercentText != null)
                expPercentText.text = $"{Mathf.RoundToInt(fraction * 100f)}%";

            if (goldText != null)
                goldText.text = PlayerProfile.Gold.ToString("N0");

            if (gemText != null)
                gemText.text = PlayerProfile.Gems.ToString("N0");
        }

        private void RefreshHearts()
        {
            var hearts = PlayerProfile.Hearts;
            DateTime now = DateTime.UtcNow;

            int count = hearts.GetCount(now);
            TimeSpan toNext = hearts.GetTimeToNext(now);

            // 올림해서 보여준다 - 내림하면 마지막 1초가 "0:00"으로 멈춰 있는 것처럼 보인다.
            int seconds = (int)Math.Ceiling(toNext.TotalSeconds);

            if (count == lastHeartCount && seconds == lastSecondsToNext)
                return;

            lastHeartCount = count;
            lastSecondsToNext = seconds;

            if (heartIcons != null)
            {
                for (int i = 0; i < heartIcons.Length; i++)
                {
                    if (heartIcons[i] == null)
                        continue;

                    heartIcons[i].color = i < count ? heartFilledColor : heartEmptyColor;
                }
            }

            if (heartCountText != null)
                heartCountText.text = $"{count}/{HeartMeter.MaxHearts}";

            if (heartTimerText != null)
                heartTimerText.text = count >= HeartMeter.MaxHearts
                    ? "가득"
                    : $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.Core;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 스킬이 퍼즐판의 어느 칸을 건드리는지 보여주는 그림 한 장.
    ///
    /// <b>⚠ 2026-08-29 방침이 바뀌었다 - 예전엔 데이터에서 격자를 그렸다.</b>
    /// 사용자가 <c>Assets/image/skillpanel</c> 에 그림을 넣으면서 "이 그림을 쓰자"고 정했다.
    /// 색칠한 사각형 36개보다 보기 좋고, 판 배경까지 한 장에 들어 있다.
    ///
    /// <b>그 대신 새로 생긴 위험</b>: 그림은 <see cref="SkillEffect.cells"/> 를 고쳐도 따라오지
    /// 않는다 - <b>범위를 바꾸면 그림도 같이 갈아야 한다</b>. 예전 방식이 막아주던 게 이거였다.
    /// (라뷰린스의 그림 <c>Rabrith1.png</c> 은 지금 Skill_AA 의 열 칸과 정확히 같다.)
    ///
    /// <b>범위가 정해지지 않은 스킬</b>(기획의 <c>random</c>, <c>random 2x2</c>)은 그릴 게 없다.
    /// 그런 스킬은 빈 판(<c>none.png</c>) 위에 "무작위"라고 적는다 - 빈 판만 두면 "범위가 없다"로
    /// 읽힌다. 지금은 라뷰린스만 고정 범위고 나머지는 전부 무작위다.
    /// </summary>
    public class SkillRangePreview : MonoBehaviour
    {
        [Header("그림")]
        [Tooltip("범위 그림을 그릴 Image. 판 전체를 덮게 놓는다.")]
        [SerializeField] private Image boardImage;

        [Tooltip("범위가 정해지지 않았을 때(또는 스킬이 없을 때) 깔 빈 판. skillpanel/none.png")]
        [SerializeField] private Sprite emptyBoard;

        [Header("무작위 표시")]
        [Tooltip("빈 판 위에 겹쳐 적을 글자. 판을 덮게 놓는다. 없어도 된다.")]
        [SerializeField] private Text randomText;

        [SerializeField] private string randomLabel = "무작위";

        /// <summary>
        /// 스킬의 범위를 보이고, <b>범위가 정해진 스킬인지</b>를 돌려준다.
        /// null 이면 빈 판만 깔고 글자도 안 적는다("스킬 없음"은 부르는 쪽이 따로 적는다).
        /// </summary>
        public bool Show(SkillDefinition skill)
        {
            var sprite = skill != null ? skill.rangeImage : null;
            bool fixedRange = sprite != null;

            if (boardImage != null)
            {
                boardImage.sprite = fixedRange ? sprite : emptyBoard;

                // 스프라이트가 없으면 아예 끈다 - 켜두면 흰 사각형이 남는다(편성 아이콘과 같은 규칙).
                boardImage.enabled = boardImage.sprite != null;
            }

            if (randomText != null)
                randomText.text = skill != null && !fixedRange ? randomLabel : string.Empty;

            return fixedRange;
        }

        public void Clear() => Show(null);
    }
}

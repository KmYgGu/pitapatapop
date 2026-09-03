using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.Core;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 캐릭터를 <b>퍼즐 조각처럼</b> 보여주는 작은 부품 - 프레임 위에 캐릭터 아이콘.
    /// 편성 화면의 슬롯과 보유 목록 칸이 같은 모양을 써야 해서 컴포넌트로 뺐다.
    ///
    /// 프레임 스프라이트는 <see cref="PanelFrameSet"/> 에서 캐릭터의 <c>frameColor</c> 로 찾는다 -
    /// 배틀의 <c>BoardView</c> 가 조각을 그릴 때와 같은 경로라 색이 어긋나지 않는다.
    /// </summary>
    public class PuzzlePieceIcon : MonoBehaviour
    {
        [SerializeField] private Image frameImage;
        [SerializeField] private Image iconImage;

        [Tooltip("프레임 스프라이트 모음. 비어 있으면 프레임 없이 아이콘만 나온다.")]
        [SerializeField] private PanelFrameSet frameSet;

        /// <summary>캐릭터를 그린다. null 이면 빈 칸으로 둔다.</summary>
        public void Show(PanelType character)
        {
            bool has = character != null;

            if (frameImage != null)
            {
                var sprite = has && frameSet != null ? frameSet.GetSprite(character.frameColor) : null;
                frameImage.sprite = sprite;

                // 스프라이트가 없으면 프레임 색으로라도 칠해 조각처럼 보이게 한다.
                frameImage.color = sprite != null
                    ? Color.white
                    : (has && frameSet != null ? frameSet.GetColor(character.frameColor)
                                               : new Color(1f, 1f, 1f, 0.12f));
            }

            if (iconImage != null)
            {
                iconImage.sprite = has ? character.icon : null;
                iconImage.enabled = has && character.icon != null;
            }
        }

        public void Clear() => Show(null);
    }
}

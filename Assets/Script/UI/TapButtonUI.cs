using UnityEngine;
using UnityEngine.EventSystems;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 탭하면 OnTapped를 발행하는 범용 버튼. 유니티 기본 Button/Selectable을 쓰지 않는 이유는
    /// 이 프로젝트가 이미 SkillGaugeUI에서 같은 방식(Graphic + IPointerClickHandler)을 쓰고 있어서
    /// 스타일을 통일하기 위함 - 상태별 색 전환 같은 Selectable 기능이 필요 없는 버튼들이라
    /// 이쪽이 더 가볍고 다루기 쉽다.
    /// 붙이는 오브젝트에 Raycast Target이 켜진 Graphic(Image 등)이 있어야 탭이 감지된다.
    /// </summary>
    public class TapButtonUI : MonoBehaviour, IPointerClickHandler
    {
        public event System.Action OnTapped;

        public void OnPointerClick(PointerEventData eventData)
        {
            OnTapped?.Invoke();
        }
    }
}

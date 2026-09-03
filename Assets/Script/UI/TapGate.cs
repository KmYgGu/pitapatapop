using System.Collections;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// "화면 아무 데나 눌러서 넘기기"를 기다리는 규칙. 결과 화면들이 이걸 같이 쓴다.
    ///
    /// <b>왜 따로 뺐는가</b>: 넘기기 규칙에는 눈에 잘 안 띄는 함정이 하나 있는데
    /// (아래 <c>graceSeconds</c>), 화면마다 따로 짜면 어느 한 곳에서 빠뜨리게 된다.
    ///
    /// 터치는 EventSystem 이 아니라 <see cref="Input.GetMouseButtonDown"/> 을 직접 읽는다
    /// (<see cref="View.BoardInputController"/> 와 같은 방식). 결과 화면들은 뒤 판이 raycast 를
    /// 먹고 있어서 화면 어디를 눌러도 되고, 버튼을 따로 둘 이유가 없다.
    /// </summary>
    public static class TapGate
    {
        /// <summary>
        /// 터치를 기다린다.
        /// </summary>
        /// <param name="autoAdvanceAfter">
        /// 0보다 크면 그 시간(초)이 지날 때 저절로 넘어간다. 0 이하면 <b>터치할 때까지 계속 기다린다.</b>
        /// </param>
        /// <param name="graceSeconds">
        /// 시작 직후 터치를 무시할 시간(초). <b>이게 핵심이다</b> - 앞 단계를 넘기려고 누른 그
        /// 손가락이 다음 단계까지 한 번에 넘겨버리면 읽을 틈이 없다.
        /// </param>
        public static IEnumerator Wait(float autoAdvanceAfter, float graceSeconds)
        {
            float elapsed = 0f;
            float grace = Mathf.Max(0f, graceSeconds);

            while (true)
            {
                elapsed += Time.deltaTime;

                if (elapsed >= grace && Input.GetMouseButtonDown(0))
                    yield break;

                if (autoAdvanceAfter > 0f && elapsed >= autoAdvanceAfter)
                    yield break;

                yield return null;
            }
        }
    }
}

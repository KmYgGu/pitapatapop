using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.View
{
    /// <summary>
    /// PanelView 오브젝트 풀. Instantiate/Destroy를 반복하지 않고 재사용한다.
    /// 핵심 이점 두 가지:
    /// 1) 성능 - 매치가 잦은 게임 특성상 Instantiate/Destroy가 빈번한데, 풀링으로 GC 부담을 줄임
    /// 2) 타이밍 - Destroy()는 실제로 프레임 끝에 지연 처리되어 그 프레임 동안 잔상처럼 남는데,
    ///    풀은 Release 즉시 SetActive(false)로 그 프레임에 바로 사라지게 해서
    ///    "지워진 패널과 새로 내려온 패널이 겹쳐 보이는" 문제를 근본적으로 없앤다.
    /// </summary>
    public class PanelViewPool
    {
        private readonly PanelView prefab;
        private readonly Transform parent;
        private readonly Stack<PanelView> pool = new Stack<PanelView>();

        public PanelViewPool(PanelView prefab, Transform parent, int prewarmCount = 0)
        {
            this.prefab = prefab;
            this.parent = parent;

            for (int i = 0; i < prewarmCount; i++)
            {
                var view = Object.Instantiate(prefab, parent);
                view.gameObject.SetActive(false);
                pool.Push(view);
            }
        }

        /// <summary>
        /// 풀에서 하나 꺼내 지정된 위치에 활성화. 풀이 비어있으면 새로 Instantiate.
        /// </summary>
        public PanelView Get(Vector3 position)
        {
            PanelView view;

            if (pool.Count > 0)
            {
                view = pool.Pop();
                view.transform.position = position;
            }
            else
            {
                view = Object.Instantiate(prefab, position, Quaternion.identity, parent);
            }

            view.gameObject.SetActive(true);
            view.ResetForReuse();
            return view;
        }

        /// <summary>
        /// 사용이 끝난 뷰를 즉시 비활성화하고 풀에 반납. null이면 안전하게 무시.
        /// </summary>
        public void Release(PanelView view)
        {
            if (view == null)
                return;

            view.gameObject.SetActive(false);
            pool.Push(view);
        }
    }
}

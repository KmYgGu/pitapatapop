using System;
using UnityEngine;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 방에 바를 수 있는 <b>인테리어 목록</b>. 아파트 화면과 미니게임 화면이 <b>같은 것을 본다</b> -
    /// 씬마다 따로 적어두면 한쪽만 고쳐져 방이 화면마다 달라 보인다.
    ///
    /// ⭐ <b>0번은 언제나 "아직 안 꾸민 방"</b>이다(2026-09-02 사용자 결정: 회색 민무늬).
    /// 새로 지은 동의 방은 전부 여기서 시작한다.
    ///
    /// <b>머티리얼을 그대로 물린다</b> - 텍스처만 바꿔 끼우면 색·타일링 같은 나머지 설정을
    /// 또 어딘가에 적어둬야 한다. 만들어 둔 .mat 이 곧 "이 인테리어의 생김새"다.
    /// </summary>
    public class RoomInteriorLibrary : ScriptableObject
    {
        [Serializable]
        public class Interior
        {
            [Tooltip("꾸미기 화면에 보일 이름.")]
            public string displayName = "민무늬";

            public Material ceiling;
            public Material wall;
            public Material floor;

            /// <summary>천장 0 · 벽 1 · 바닥 2.</summary>
            public Material Surface(int surface)
            {
                switch (surface)
                {
                    case 0: return ceiling;
                    case 1: return wall;
                    case 2: return floor;
                    default: return null;
                }
            }
        }

        [Tooltip("0번은 반드시 '안 꾸민 방'이어야 한다 - 새 방이 여기서 시작한다.")]
        [SerializeField] private Interior[] interiors = new Interior[0];

        public int Count => interiors != null ? interiors.Length : 0;

        /// <summary>범위를 벗어나면 0번(안 꾸민 방)을 돌려준다 - 빈 방으로 두는 것보다 낫다.</summary>
        public Interior Get(int index)
        {
            if (interiors == null || interiors.Length == 0)
                return null;

            return interiors[Mathf.Clamp(index, 0, interiors.Length - 1)];
        }

        public string NameOf(int index)
        {
            var interior = Get(index);
            return interior != null ? interior.displayName : string.Empty;
        }
    }
}

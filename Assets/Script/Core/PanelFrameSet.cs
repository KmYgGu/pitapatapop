using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 퍼즐 조각 프레임 16색 스프라이트 모음. 하나의 4x4 시트를 Sprite Editor로 16개로 슬라이스한 뒤,
    /// PanelFrameColor 순서(0=Yellow ... 15=Rainbow)에 맞춰 인스펙터에서 채워두는 단일 애셋.
    /// 씬에서는 BoardView가 이 애셋을 참조해서 팔레트 슬롯마다 어떤 프레임을 그릴지 조회한다.
    /// </summary>
    [CreateAssetMenu(fileName = "PanelFrameSet", menuName = "JojoPuzzle/Panel Frame Set")]
    public class PanelFrameSet : ScriptableObject
    {
        public Sprite[] frames = new Sprite[16];

        /// <summary>
        /// 프레임 색 16종의 대표 RGB 값. 프레임은 스프라이트라 색을 코드에서 알 수 없는데,
        /// 불꽃 오라처럼 "그 조각의 색"으로 칠해야 하는 연출이 이 값을 참조한다.
        /// PanelFrameColor의 이름을 기준으로 한 기본값을 넣어뒀으니, 실제 프레임 아트와 어긋나면
        /// 인스펙터에서 스포이드로 찍어 맞추면 된다(인덱스는 frames 배열과 동일한 순서).
        /// </summary>
        public Color[] frameColors =
        {
            new Color(1f,    0.85f, 0.15f), // 0  Yellow
            new Color(0.65f, 0.85f, 0.2f),  // 1  YellowGreen
            new Color(0.6f,  0.3f,  0.85f), // 2  Purple
            new Color(1f,    0.5f,  0.7f),  // 3  Pink
            new Color(0.25f, 0.5f,  1f),    // 4  Blue
            new Color(0.95f, 0.95f, 0.95f), // 5  White
            new Color(0.95f, 0.25f, 0.25f), // 6  Red
            new Color(0.3f,  0.3f,  0.35f), // 7  Black
            new Color(0.6f,  0.4f,  0.2f),  // 8  Brown
            new Color(0.2f,  0.55f, 0.3f),  // 9  DarkGreen
            new Color(0.75f, 0.6f,  0.95f), // 10 LightPurple
            new Color(1f,    0.55f, 0.15f), // 11 Orange
            new Color(0.3f,  0.85f, 0.9f),  // 12 Cyan
            new Color(0.6f,  0.6f,  0.65f), // 13 Gray
            new Color(0.55f, 0.15f, 0.25f), // 14 Maroon
            new Color(0.9f,  0.6f,  0.9f)   // 15 Rainbow
        };

        public Sprite GetSprite(PanelFrameColor color)
        {
            int index = (int)color;
            if (frames == null || index < 0 || index >= frames.Length)
                return null;

            return frames[index];
        }

        /// <summary>
        /// 프레임 색의 대표 RGB. 값이 채워져 있지 않으면 흰색을 돌려줘서, 색을 못 찾았을 때
        /// 불꽃이 사라지는 대신 최소한 보이기는 하게 한다.
        /// </summary>
        public Color GetColor(PanelFrameColor color)
        {
            int index = (int)color;
            if (frameColors == null || index < 0 || index >= frameColors.Length)
                return Color.white;

            var value = frameColors[index];
            return value.a <= 0f ? Color.white : value; // 알파 0으로 비어있는 칸 방어
        }
    }
}

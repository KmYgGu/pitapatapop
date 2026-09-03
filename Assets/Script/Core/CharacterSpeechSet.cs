using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 대사 한 줄. 같은 trigger에 여러 줄을 넣어두면 그중 하나가 무작위로 뽑힌다
    /// (같은 상황에서 매번 같은 말을 하면 금방 질린다).
    /// </summary>
    [System.Serializable]
    public struct SpeechLine
    {
        public SpeechTrigger trigger;

        [TextArea(1, 3)]
        public string message;

        [Tooltip("대사창을 띄워둘 시간(초). 0 이하면 SpeechDirector의 기본값을 쓴다.\n" +
                 "음수는 쓰지 말 것 - SpeechBubbleUI에서 음수는 '직접 닫을 때까지 유지'라서, " +
                 "그동안 매치 처리가 통째로 멈춘다.")]
        public float holdSeconds;

        [Tooltip("겹쳤을 때 누가 이길지. 클수록 우선. 보스 등장 같은 건 높게, 잡담은 낮게.")]
        public int priority;
    }

    /// <summary>
    /// 캐릭터 한 명의 대사 모음.
    ///
    /// <b>PanelType에 직접 넣지 않고 애셋을 나눈 이유</b>: 대사는 고치고 늘릴 일이 압도적으로 많은데,
    /// 성장/퍼즐 설정까지 들어 있는 PanelType을 매번 열면 편집이 번거롭고 실수로 다른 값을 건드리기 쉽다.
    /// 나중에 대사가 수십 줄로 늘어나면 스프레드시트(csv)에서 편집해 이 애셋으로 구워내는
    /// 임포터를 붙이면 되는데, 그때도 이 애셋만 갈아끼우면 되므로 다른 데이터가 안전하다.
    /// </summary>
    [CreateAssetMenu(fileName = "Speech_", menuName = "JojoPuzzle/Character Speech Set")]
    public class CharacterSpeechSet : ScriptableObject
    {
        [Tooltip("대사창에 띄울 Spine 캐릭터. 이게 있으면 정지 이미지 대신 스파인 애니메이션이 나온다. " +
                 "이 캐릭터의 *_SkeletonData 애셋을 넣으면 된다.")]
        public Spine.Unity.SkeletonDataAsset spine;

        [Tooltip("대사 중 재생할 애니메이션 이름. 비워두면 스켈레톤의 첫 애니메이션을 쓴다.")]
        public string talkAnimation = "1.idle";

        [Tooltip("Spine이 없을 때 대신 띄울 정지 초상화. 이것도 비면 PanelType.icon을 쓴다.")]
        public Sprite portrait;

        public List<SpeechLine> lines = new List<SpeechLine>();

        /// <summary>
        /// 이 상황에 쓸 대사가 <b>하나라도 있는지</b>. 쿨다운은 보지 않는다 -
        /// "아직 안 적은 상황"과 "방금 말해서 쉬는 중"은 다른 이야기다.
        ///
        /// 대사가 비어 있을 때 다른 상황의 말로 대신하려는 쪽이 이걸 본다
        /// (예: 블랙잭 승패 대사가 없으면 포커 승패 대사를 쓴다).
        /// </summary>
        public bool Has(SpeechTrigger trigger)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].trigger == trigger && !string.IsNullOrEmpty(lines[i].message))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 이 상황에 쓸 대사를 하나 고른다. 후보가 없으면 false.
        /// avoidMessage와 같은 대사는 후보가 둘 이상일 때만 피한다(연달아 같은 말을 안 하도록).
        /// </summary>
        public bool TryPick(SpeechTrigger trigger, System.Random rng, string avoidMessage, out SpeechLine picked)
        {
            picked = default;

            int count = 0;
            int firstIndex = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].trigger != trigger || string.IsNullOrEmpty(lines[i].message))
                    continue;

                count++;
                if (firstIndex < 0)
                    firstIndex = i;
            }

            if (count == 0)
                return false;

            if (count == 1)
            {
                picked = lines[firstIndex];
                return true;
            }

            // 후보가 여럿이면 무작위로 뽑되, 직전과 같은 대사는 한 번 다시 뽑는다.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int target = rng.Next(count);
                int seen = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].trigger != trigger || string.IsNullOrEmpty(lines[i].message))
                        continue;

                    if (seen == target)
                    {
                        picked = lines[i];
                        break;
                    }
                    seen++;
                }

                if (picked.message != avoidMessage)
                    return true;
            }

            return true; // 두 번 뽑아도 같으면 그냥 그걸 쓴다
        }
    }
}

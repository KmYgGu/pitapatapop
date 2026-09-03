using System;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>무엇으로 값을 치르는지.</summary>
    public enum GachaCurrency
    {
        Gem = 0,

        /// <summary>골드 가챠만 쓴다 - 배틀과 도박으로 버는 돈이다.</summary>
        Gold = 1,
    }

    /// <summary>
    /// 배너의 <b>성격</b>. 뽑는 값과 횟수는 데이터(<see cref="GachaPull"/>)가 들고,
    /// 여기서는 <b>규칙이 통째로 다른 것</b>만 가른다.
    /// </summary>
    public enum GachaBannerKind
    {
        /// <summary>평범한 배너. 1회 · 10연차.</summary>
        Standard = 0,

        /// <summary>
        /// <b>골드 가챠</b> - 젬 대신 골드를 쓴다. GR 확률이 아주 낮고, 나와도 상시 캐릭터뿐이다.
        /// ⚠ <b>여기서만 포인트를 안 준다</b>(<see cref="GachaBanner.givesPoints"/>).
        /// </summary>
        Gold = 1,

        /// <summary>
        /// <b>박스 가챠</b> - 뽑은 등급을 상자에서 빼고 남은 것으로 확률을 다시 잡는다.
        /// 뽑을수록 GR 이 나올 확률이 오른다.
        /// </summary>
        Box = 2,

        /// <summary>
        /// <b>스텝업 가챠</b> - 뽑을 때마다 단계가 오르고 내용이 달라진다.
        /// 단계는 <see cref="GachaBanner.pulls"/> 가 순서대로 들고 있다.
        /// </summary>
        StepUp = 3,

        /// <summary>
        /// <b>프라이즈 가챠</b> - 값은 평범한 10연차인데, <b>한 번 뽑을 때마다 룰렛</b>을 돌려
        /// 포인트를 더 준다(<see cref="GachaBanner.prizes"/>).
        /// </summary>
        Prize = 4,
    }

    /// <summary>
    /// <b>한 번에 뽑는 묶음</b>. "1회", "10연차", 스텝업의 각 단계가 전부 이것이다.
    ///
    /// ⭐ 횟수·값·보장을 전부 데이터로 둔 이유: 스텝업이 4단계에서 5단계가 되거나 값이 바뀌어도
    /// <b>애셋만 고치면 된다</b>. 코드에 단계를 박으면 그때마다 스크립트를 손대게 된다.
    /// </summary>
    [Serializable]
    public class GachaPull
    {
        [Tooltip("버튼에 적을 말. 예: '1회', '10연차', '1스텝 - 반값 10연차'.")]
        public string label;

        [Min(1)]
        [Tooltip("이 묶음으로 뽑는 횟수.")]
        public int count = 1;

        [Min(0)]
        [Tooltip("값. 기본은 1회 30젬 · 10연차 300젬이고, 스텝업 1단계처럼 깎인 값도 여기 적는다.")]
        public int price = 30;

        [Tooltip("결과가 <b>전부</b> SR 이상인지. 스텝업 3단계가 그렇다.")]
        public bool allSrOrBetter;

        [Tooltip("이 중 <b>하나는 SR 이상</b>인지. 10연차의 기본 보장이다.")]
        public bool guaranteeSr = true;

        [Tooltip("이 중 <b>하나는 GR</b>인지. 스텝업 4단계가 그렇다.")]
        public bool guaranteeGr;

        [Tooltip("위 보장으로 나오는 자리에 <b>픽업 캐릭터</b>가 나올 수 있는지.")]
        public bool pickupOnGuarantee;
    }

    /// <summary>
    /// 프라이즈 가챠의 룰렛 한 칸. <b>한 번 뽑을 때마다</b> 한 칸이 걸린다.
    /// </summary>
    [Serializable]
    public class GachaPrize
    {
        [Tooltip("등수. 1등이 제일 좋다.")]
        [Min(1)]
        public int rank = 1;

        [Tooltip("이 칸에서 받는 포인트. 6등·5등은 0, 4등은 본전인 1, 1등은 5.")]
        [Min(0)]
        public int points;

        [Tooltip("룰렛에 적을 말. 비워두면 '<등수>등' 으로 적는다.")]
        public string label;

        public string Label => string.IsNullOrEmpty(label) ? rank + "등" : label;
    }

    /// <summary>
    /// <b>가챠 배너 하나.</b> 배너가 늘어도 <b>애셋만 추가</b>하면 되도록 규칙을 전부 데이터로 뒀다.
    ///
    /// <code>
    ///   공통  1회 30젬 · 10연차 300젬 · 10연차는 하나가 SR 이상
    ///         한 번 뽑을 때마다 1포인트(골드 가챠 제외, 배너끼리 <b>합쳐서</b> 쌓인다)
    ///         50포인트 → GR 확정권 · 100포인트 → 픽업 교환권
    /// </code>
    ///
    /// ⚠ <b>확률은 아직 여기 없다</b>(2026-09-03 사용자 지시: "확률은 생각하지 말고").
    /// 배너와 화면을 먼저 세우고, 확률은 그 위에 얹는다.
    /// </summary>
    public class GachaBanner : ScriptableObject
    {
        [Header("이름표")]
        [Tooltip("저장·기록에 쓰는 이름표. <b>겹치면 안 된다</b> - 스텝업 진행도가 이걸로 기억된다.")]
        public string bannerId;

        public string displayName;

        [TextArea(2, 4)]
        public string description;

        [Tooltip("배너 그림. 비워두면 색만 칠한다.")]
        public Sprite art;

        [Header("성격")]
        public GachaBannerKind kind = GachaBannerKind.Standard;

        public GachaCurrency currency = GachaCurrency.Gem;

        [Tooltip("한 번 뽑을 때마다 포인트를 주는지. ⚠ <b>골드 가챠만 끈다</b>(사용자 확정).")]
        public bool givesPoints = true;

        [Header("뽑기 묶음")]
        [Tooltip("고를 수 있는 묶음들. <b>스텝업이면 이게 단계 순서</b>가 된다 - " +
                 "지금 단계에 해당하는 것 하나만 뽑을 수 있다.")]
        public GachaPull[] pulls = new GachaPull[0];

        [Header("픽업")]
        [Tooltip("이 배너에서 확률이 올라가는 캐릭터들. 비어 있으면 픽업이 없는 배너다.")]
        public PanelType[] pickup = new PanelType[0];

        [Tooltip("⚠ <b>골드 가챠에 켠다</b> - GR 이 나와도 픽업이 아니라 <b>상시 캐릭터</b>만 나온다.")]
        public bool standardPoolOnly;

        [Header("프라이즈 룰렛")]
        [Tooltip("프라이즈 가챠일 때만 쓴다. 한 번 뽑을 때마다 한 칸이 걸린다.")]
        public GachaPrize[] prizes = new GachaPrize[0];

        /// <summary>화면에 적을 이름. 비어 있으면 애셋 이름으로 물러선다.</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>스텝업인지. 뽑을 때마다 단계가 오른다.</summary>
        public bool IsStepUp => kind == GachaBannerKind.StepUp;

        /// <summary>단계 수. 스텝업이 아니면 0.</summary>
        public int StepCount => IsStepUp && pulls != null ? pulls.Length : 0;

        /// <summary>그 번째 묶음. 범위 밖이면 null.</summary>
        public GachaPull PullAt(int index)
            => pulls != null && index >= 0 && index < pulls.Length ? pulls[index] : null;
    }
}

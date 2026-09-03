using System.Collections;
using UnityEngine;
using JojoPuzzle.Board;

namespace JojoPuzzle.View
{
    /// <summary>
    /// 매치 판정(<see cref="MatchResolver"/>)이 <b>자기 주인에게 물어야 하는 것들</b>.
    ///
    /// 매치는 판만 고치는 게 아니라 <b>전투 전체를 건드린다</b> - 적을 때리고, 스킬 게이지를
    /// 채우고, 콤보를 세고, 힌트 시계를 되돌린다. 그것들이 어디 붙어 있는지는 주인이 안다.
    /// </summary>
    public interface IMatchHost
    {
        /// <summary>
        /// 지금 매치 처리를 미뤄야 하는지(가림막·스탠드업 배너·화면 암전).
        /// 판이 아니라 그 위를 봐야 하는 구간이라, 그 위로 조각이 접히고 사라지면 어수선해진다.
        /// </summary>
        bool IsResolveFrozen { get; }

        /// <summary>스탠드업 타임인지. <b>매치가 성립했을 때 무슨 일이 일어나는지를 가른다.</b></summary>
        bool IsStandUpTimeActive { get; }

        /// <summary>플레이어가 뭔가 해냈다 - 힌트 시계를 처음으로 되돌린다.</summary>
        void NotifyActivity();

        /// <summary>연속 매칭 카운트. 판을 기준으로 잡은 좌표에 숫자가 뜬다.</summary>
        void MatchCounted(Vector3 worldPosition);

        /// <summary>
        /// 이번 매치로 조각이 이만큼 처리됐다.
        ///
        /// ⚠ <b>강화된 조각 수를 따로 알린다.</b> "강화 조각 하나를 N조각으로 치는" 스티커가
        /// <b>스킬 게이지에만</b> 듣기 때문이다(시트: "스킬 채우기에 용이"). 한 숫자로 합쳐
        /// 보내면 코인·경험치까지 같이 부푼다.
        /// </summary>
        void PiecesCleared(int panelIndex, int count, int empoweredCount);

        /// <summary>이 매치가 적을 때린다. 실효 칸 수는 데이터를 비우기 전에 재둔 값이다.</summary>
        void RaiseMatchDamage(ConnectionResult group, float matchWeight);

        /// <summary>조각 하나가 접혀 들어올 때마다 게이지를 채운다. 연출 쪽에 그대로 넘긴다.</summary>
        void ChargeGaugeByOnePiece();

        /// <summary>
        /// 코루틴을 띄우고 <b>손잡이를 돌려준다</b> - 특수 뭉치는 일반 조각과
        /// <b>나란히</b> 접히므로 기다릴 손잡이가 필요하다.
        /// </summary>
        Coroutine Run(IEnumerator routine);
    }
}

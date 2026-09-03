using UnityEngine;
using JojoPuzzle.Board;

namespace JojoPuzzle.View
{
    /// <summary>
    /// 낙하·연쇄(<see cref="BoardCascade"/>)가 <b>자기 주인에게 물어야 하는 것들</b>.
    ///
    /// 낙하는 판이 스스로 사는 일이라 입력과 상관없이 돌지만, "지금 판을 굴려도 되는
    /// 구간인가"와 "매치가 나면 어떻게 처리하는가"는 주인이 안다. 그 둘만 좁혀서 받는다.
    /// </summary>
    public interface ICascadeHost
    {
        /// <summary>승패가 확정됐다 - 판을 아예 세운다(결과 화면이 덮고 있어 보이지도 않는다).</summary>
        bool IsBoardStopped { get; }

        /// <summary>
        /// 낙하만 잠깐 멈춘다(스탠드업 종료 연출 등). <see cref="IsBoardStopped"/> 와 다르다:
        /// 저쪽은 끝난 것이고 이쪽은 <b>이어서 한다</b>.
        /// </summary>
        bool IsFallFrozen { get; }

        /// <summary>고정된 조각까지 이어 붙여 매치가 성립하는 구간인지(스탠드업 타임).</summary>
        bool IsStandUpTimeActive { get; }

        /// <summary>
        /// 마무리 처리 중인지. 그때는 <b>새 조각을 채우지 않는다</b> - 남은 조각을 전부
        /// 데미지로 바꾸고 끝내는 구간이라 계속 채워지면 끝나지 않는다.
        /// </summary>
        bool IsFinisherRunning { get; }

        /// <summary>플레이어가 지금 이 칸을 집어 들고 있는지. 그러면 잠금을 풀지 않는다.</summary>
        bool IsHeldByPlayer((int x, int y) cell);

        /// <summary>
        /// 매치 하나를 처리하기 시작한다. <b>코루틴 손잡이를 돌려준다</b> -
        /// 한 번에 여러 무리를 띄워 놓고 전부 끝나기를 기다려야 하기 때문이다.
        /// </summary>
        Coroutine ResolveMatch(ConnectionResult group, int anchorX, int anchorY);
    }
}

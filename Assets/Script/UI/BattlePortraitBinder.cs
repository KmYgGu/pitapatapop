using System;
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 배틀 화면에 서 있는 초상화 셋(리더·파트너·적)을 <b>이번 판의 실제 캐릭터</b>로 갈아끼운다.
    ///
    /// <b>왜 필요했나</b>(2026-08-30 사용자 신고: "스킬 쓸 땐 파트너 얼굴이 제대로 나오는데
    /// 화면에선 안 나온다"): 씬의 초상화는 에디터 도구(<c>SpinePortraitSetup</c>)가 <b>라뷰린스로
    /// 박아둔 것</b>이고, 런타임에 그걸 바꿔주는 곳이 <b>아무 데도 없었다</b>. 대사창만
    /// <see cref="SpeechBubbleUI"/> 가 갈아끼우고 있어서, 스킬 대사에서는 맞는 얼굴이 나오고
    /// 화면에 서 있는 초상화는 셋 다 라뷰린스였다.
    ///
    /// <b>누가 누구인지는 팔레트가 안다</b> - 팔레트 색 인덱스가 곧 편성 순서다
    /// (0=리더, 1=파트너 · <c>BattleSetup.BuildPalette</c>). 적은 스테이지가 안다
    /// (<see cref="StageDefinition.enemy"/>). <see cref="BattleSpeechBinder"/> 와 같은 규칙이다.
    ///
    /// <b>부르는 쪽이 순서를 정한다</b> - <c>Start</c> 에서 스스로 하지 않고
    /// <see cref="GameEntryPoint"/> 가 팔레트를 세운 직후에 <see cref="Apply"/> 를 부른다.
    /// Start 끼리는 순서가 보장되지 않아서, 스스로 하면 팔레트가 없는 프레임에 걸릴 수 있다.
    /// 시작 연출이 초상화를 화면 밖에 세우기 <b>전에</b> 끝나야 하는 이유도 있다.
    /// </summary>
    public class BattlePortraitBinder : MonoBehaviour
    {
        public enum PortraitSource
        {
            /// <summary>편성 0번.</summary>
            Leader = 0,

            /// <summary>편성 1번.</summary>
            Partner = 1,

            /// <summary>스테이지가 정한 적.</summary>
            Enemy = 2,
        }

        [Serializable]
        private class Entry
        {
            [Tooltip("초상화 안의 SpineChar 에 붙은 SkeletonGraphic.")]
            public SkeletonGraphic portrait;

            [Tooltip("이 자리에 설 사람.")]
            public PortraitSource source;

            [Tooltip("그 캐릭터에게 Spine 이 없을 때 대신 켤 정지 그림. 비워두면 그냥 감춘다. " +
                     "<b>씬에 박힌 남의 스켈레톤을 그대로 두지는 않는다</b> - 그건 다른 사람 얼굴이다.")]
            public Image fallbackImage;
        }

        [Tooltip("팔레트로 편성 캐릭터를 찾는 데 쓴다.")]
        [SerializeField] private BoardView boardView;

        [SerializeField] private Entry[] portraits = new Entry[0];

        /// <summary>
        /// 초상화를 이번 판의 캐릭터로 맞춘다. 팔레트가 만들어진 뒤에 불러야 한다.
        /// </summary>
        public void Apply()
        {
            if (portraits == null)
                return;

            for (int i = 0; i < portraits.Length; i++)
            {
                var entry = portraits[i];
                if (entry == null || entry.portrait == null)
                    continue;

                var character = Resolve(entry.source);
                ApplyOne(entry, character);
            }
        }

        private PanelType Resolve(PortraitSource source)
        {
            if (source == PortraitSource.Enemy)
                return StageEntry.Stage != null ? StageEntry.Stage.enemy : null;

            return boardView != null ? boardView.GetCharacter((int)source) : null;
        }

        private static void ApplyOne(Entry entry, PanelType character)
        {
            var spine = character != null && character.speech != null ? character.speech.spine : null;

            if (spine == null)
            {
                // Spine 이 없는 캐릭터다. 씬에 박혀 있던 스켈레톤을 그대로 두면 <b>남의 얼굴</b>이
                // 서 있게 되므로 감추고, 있으면 정지 그림으로 대신한다.
                entry.portrait.gameObject.SetActive(false);
                ShowFallback(entry, character);
                return;
            }

            entry.portrait.gameObject.SetActive(true);

            if (entry.fallbackImage != null)
                entry.fallbackImage.enabled = false;

            if (entry.portrait.skeletonDataAsset != spine)
            {
                entry.portrait.skeletonDataAsset = spine;

                // <b>Initialize(true) 를 반드시 부른다</b> - 안 부르면 데이터만 바뀌고 화면은 이전
                // 캐릭터 그대로 남는다(대사창·방 화면에서도 겪은 함정). 좌우 반전(initialFlipX)은
                // 직렬화된 값이라 이때 다시 반영되므로 적은 계속 왼쪽을 본다.
                entry.portrait.Initialize(true);
            }
            else if (!entry.portrait.IsValid)
            {
                // 갈아끼울 게 없어도 초기화는 확인한다 - 아직 안 됐으면 AnimationState 가 없어서
                // 아래 idle 이 조용히 씹힌다.
                entry.portrait.Initialize(false);
            }

            // <b>바뀌지 않았어도 idle 은 반드시 튼다</b>(2026-08-30 사용자 신고: "라뷰린스를
            // 파트너로 두면 안 움직인다"). 파트너 초상화에는 애니메이터가 붙어 있지 않고
            // 씬의 SkeletonAnimation 에도 재생할 동작 이름이 안 적혀 있어서, 여기서 안 틀면
            // 아무도 안 튼다(리더는 LeaderMecanimAnimator, 적은 EnemyBattleAnimator 가 튼다).
            //
            // 재생은 옆에 붙은 SkeletonAnimation 이 맡는다(4.3부터 렌더링과 재생이 나뉘었다).
            // 없는 동작을 그 캐릭터의 idle 로 메우는 규칙은 SpinePlayback 한 곳에 있다.
            var player = entry.portrait.GetComponent<SkeletonAnimation>();
            if (player != null)
                SpinePlayback.Play(player, SpinePlayback.Idle, true);
        }

        private static void ShowFallback(Entry entry, PanelType character)
        {
            if (entry.fallbackImage == null)
                return;

            var sprite = character != null
                ? (character.speech != null && character.speech.portrait != null
                    ? character.speech.portrait
                    : character.icon)
                : null;

            entry.fallbackImage.sprite = sprite;

            // 그림이 없으면 아예 끈다 - 켜두면 흰 사각형이 남는다(이 프로젝트의 아이콘 규칙).
            entry.fallbackImage.enabled = sprite != null;
        }
    }
}

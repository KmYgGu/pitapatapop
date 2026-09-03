using JojoPuzzle.Core;
using JojoPuzzle.UI;
using Spine.Unity;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 미니게임 테이블 <b>건너편에 선 캐릭터</b>. 월드 공간의 Spine 이다(2026-09-02).
    ///
    /// <b>왜 UI 가 아닌가</b>: 방과 테이블이 3D 라 그 사이에 끼워 넣어야 한다. UI 캔버스에
    /// 올리면 언제나 3D 위에 덮여서 테이블 뒤에 설 수가 없다 - 아파트 방의 거주 캐릭터가
    /// 월드 오브젝트인 것과 같은 이유다.
    ///
    /// <b>보정값을 캐릭터마다 두지 않는다</b>(사용자 방침). 크기는 <see cref="targetHeight"/>
    /// 하나로 정하고, 실제로 그려진 높이를 재서 그 비율만큼 줄인다. 발끝도 좌표로 맞추지 않고
    /// <b>그려진 아래끝</b>을 재서 올린다 - 리깅마다 원점이 달라서 계산으로는 못 맞춘다.
    /// </summary>
    public class MiniGameCharacterStand : MonoBehaviour
    {
        [Tooltip("Spine 이 설 자리. 비워두면 이 오브젝트 자신에 만든다.")]
        [SerializeField] private Transform slot;

        [Tooltip("월드 단위로 이 높이에 맞춘다. 방 크기에 맞춰 인스펙터에서 조절한다.")]
        [Min(0.01f)]
        [SerializeField] private float targetHeight = 18f;

        [Tooltip("발끝이 닿을 바닥 높이(월드 y). 테이블 상판보다 아래여야 한다.")]
        [SerializeField] private float floorY;

        [Tooltip("Spine 이 없는 캐릭터를 세울 때 쓸 대체 스켈레톤. 비워두면 아무것도 안 선다.")]
        [SerializeField] private SkeletonDataAsset placeholderSpine;

        [Tooltip("평소 재생할 동작. 없는 동작은 그 캐릭터의 idle 로 메운다(SpinePlayback).")]
        [SerializeField] private string idleAnimation = SpinePlayback.Idle;

        private SkeletonAnimation spine;

        /// <summary>지금 세워둔 캐릭터. 없으면 null.</summary>
        public PanelType Character { get; private set; }

        /// <summary>
        /// 키와 바닥 높이를 밖에서 정해준다. <see cref="MiniGameStage"/> 가 <b>방을 재서</b>
        /// 불러준다 - 임포트 배율이 바뀌어도 캐릭터가 방에 맞는다.
        /// </summary>
        public void Configure(float height, float floor)
        {
            targetHeight = Mathf.Max(0.01f, height);
            floorY = floor;
        }

        /// <summary>그 캐릭터를 테이블 건너편에 세운다.</summary>
        public void Bind(PanelType character)
        {
            Character = character;

            var data = character != null && character.speech != null ? character.speech.spine : null;
            if (data == null)
                data = placeholderSpine;

            if (data == null)
            {
                if (spine != null)
                    spine.gameObject.SetActive(false);
                return;
            }

            EnsureSpine(data);
            Play(idleAnimation, loop: true);

            // ⚠ 방금 만든 SkeletonAnimation 은 <b>아직 메시가 없어서</b> 크기를 잴 수 없다.
            // 한 프레임 뒤에 맞춘다(아파트 방 캐릭터에서 같은 함정을 겪었다).
            StartCoroutine(FitNextFrame());
        }

        /// <summary>
        /// 동작을 하나 재생한다. 없는 동작은 그 캐릭터의 idle 로 메워진다 -
        /// 그 규칙은 <see cref="SpinePlayback"/> 한 곳에만 있다.
        /// </summary>
        public void Play(string animationName, bool loop)
        {
            if (spine == null)
                return;

            SpinePlayback.Play(spine.AnimationState, spine.Skeleton?.Data, animationName, loop);
        }

        private void EnsureSpine(SkeletonDataAsset data)
        {
            var parent = slot != null ? slot : transform;

            if (spine == null)
            {
                var go = new GameObject("Spine");
                go.transform.SetParent(parent, false);
                spine = SkeletonAnimation.AddToGameObject(go, data).skeletonAnimation;
            }
            else if (spine.skeletonDataAsset != data)
            {
                spine.skeletonDataAsset = data;

                // Initialize(true) 를 반드시 부른다 - 안 부르면 데이터만 바뀌고 화면은
                // 이전 캐릭터 그대로 남는다(spine-unity 의 흔한 함정).
                spine.Initialize(true);
            }

            spine.gameObject.SetActive(true);
        }

        private System.Collections.IEnumerator FitNextFrame()
        {
            yield return null;
            Fit();
        }

        private void Fit()
        {
            if (spine == null)
                return;

            var renderer = spine.GetComponent<Renderer>();
            if (renderer == null)
                return;

            var t = spine.transform;
            t.localScale = Vector3.one;

            // ⭐ <b>원점 위쪽만 잰다</b>. 원점 아래로 삐져나온 그림까지 키에 넣으면 그 캐릭터만
            // 작아진다 - 카우펜스는 원점 아래로 키의 12%가 나와 있다(2026-09-02).
            float above = renderer.bounds.max.y - t.position.y;
            float raw = above > 0.0001f ? above : renderer.bounds.size.y;

            if (raw > 0.0001f)
                t.localScale = Vector3.one * (targetHeight / raw);

            // ⭐⭐ <b>Spine 은 원점이 곧 발밑이다</b>(사용자 확인). 그려진 아래끝을 재서 올리면
            // 원점 아래로 그림이 나온 캐릭터가 그만큼 떠 보인다.
            var p = t.position;
            t.position = new Vector3(p.x, floorY, p.z);
        }
    }
}

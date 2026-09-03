using UnityEngine;
using UnityEngine.UI;
using Spine.Unity;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 지정한 칸 안에 Spine 캐릭터를 <b>런타임에</b> 만들어 세우고 idle 을 돌린다.
    /// 캐릭터를 바꾸면 안에 있던 걸 지우고 다시 만든다.
    ///
    /// <b>왜 씬에 미리 박지 않는가</b>: 이 캐릭터의 아틀라스는 여러 페이지라 머티리얼도 여러 개고,
    /// Unity UI 는 렌더러 하나당 텍스처 하나만 쓴다. 그래서 <c>SkeletonGraphic</c> 은 나머지
    /// 페이지용 자식 <c>CanvasRenderer</c> 를 <b>Spine 내부 코드가 직접 만들어야</b> 하고,
    /// 그건 씬 YAML 로 흉내 낼 수 없다. 공식 팩토리를 런타임에 부르면 그 과정을 전부 대신해준다
    /// (배틀 초상화는 에디터 메뉴로 같은 일을 한다 - 여기는 <b>어느 캐릭터가 설지 실행해봐야
    /// 알기 때문에</b> 런타임이어야 한다).
    ///
    /// 배치 순서에 함정이 있어서 <see cref="Rebuild"/> 의 주석을 꼭 읽을 것.
    /// </summary>
    public class SpineCharacterView : MonoBehaviour
    {
        private const string ChildName = "SpineChar";

        [Tooltip("캐릭터가 설 칸. 비워두면 자기 RectTransform 을 쓴다.")]
        [SerializeField] private RectTransform slot;

        [Tooltip("SkeletonGraphic 용 머티리얼. 이 아틀라스가 straight alpha 라 " +
                 "PMA 용을 쓰면 반투명 경계가 검게 뜬다 - Straight 머티리얼이어야 한다.")]
        [SerializeField] private Material skeletonGraphicMaterial;

        [Tooltip("세워둘 애니메이션 이름. 이름에 점이 들어가는 데 주의(예: 1.idle).")]
        [SerializeField] private string idleAnimation = "1.idle";

        [Tooltip("칸 대비 캐릭터를 얼마나 키울지. 1이면 칸에 딱 맞고, 크면 칸 밖으로 조금 넘친다.")]
        [SerializeField] private float sizeMultiplier = 1.15f;

        [Tooltip("크기를 <b>이 캐릭터 기준으로</b> 잰다(보통 라뷰린스). 그러면 누가 서든 같은 배율이 " +
                 "되고, 지팡이·날개 같은 소품은 칸 밖으로 넘친다 - 배틀 화면과 같은 방식이다.\n" +
                 "비워두면 캐릭터마다 자기 덩치에 맞추는데, 소품이 큰 캐릭터가 혼자 작아 보인다.")]
        [SerializeField] private SkeletonDataAsset sizeReference;

        [Tooltip("좌우 반전. 마주 보게 세울 때 적 쪽만 켠다.")]
        [SerializeField] private bool flipX;

        private SkeletonDataAsset current;
        private GameObject spawned;

        // 방금 만든 캐릭터의 그리기·재생 담당. 애니메이션만 갈아끼울 때 다시 찾지 않으려고 들고 있는다.
        // spine-unity 4.3부터 둘이 나뉘어 있다(SpinePortraitSetup 주석 참고).
        private SkeletonGraphic activeGraphic;
        private SkeletonAnimation activePlayer;

        private RectTransform Slot => slot != null ? slot : (RectTransform)transform;

        /// <summary>세울 캐릭터를 바꾼다. 같은 캐릭터면 아무것도 하지 않는다.</summary>
        public void Show(SkeletonDataAsset skeletonData)
        {
            if (skeletonData == current && spawned != null)
                return;

            current = skeletonData;
            Rebuild();
        }

        /// <summary>
        /// 세울 캐릭터와 재생할 애니메이션을 함께 지정한다. 대사창처럼 <b>같은 캐릭터가 상황마다
        /// 다른 동작</b>을 해야 하는 자리에서 쓴다.
        ///
        /// 캐릭터가 그대로면 <b>다시 만들지 않고 애니메이션만 갈아끼운다</b> - 대사는 잦은데
        /// 매번 새로 만들면 그때마다 메시가 다시 만들어지고 오브젝트가 버려진다.
        /// </summary>
        public void Show(SkeletonDataAsset skeletonData, string animationName)
        {
            bool animationChanged = !string.IsNullOrEmpty(animationName) && animationName != idleAnimation;
            if (animationChanged)
                idleAnimation = animationName;

            if (skeletonData == current && spawned != null)
            {
                if (animationChanged)
                    ApplyAnimation();
                return;
            }

            current = skeletonData;
            Rebuild();
        }

        /// <summary>지금 서 있는 캐릭터의 애니메이션만 바꾼다. 서 있지 않으면 아무 일도 없다.</summary>
        private void ApplyAnimation()
        {
            if (activePlayer == null || activeGraphic == null || activeGraphic.Skeleton == null)
                return;

            // 이름은 대사 데이터에서 오는 값이라 실제로 틀릴 수 있고, 새 캐릭터는 idle 밖에
            // 없을 수도 있다. 없으면 그 캐릭터의 idle 로 메우는 규칙은 SpinePlayback 에 있다.
            SpinePlayback.Play(activePlayer.AnimationState, activeGraphic.Skeleton.Data,
                               idleAnimation, true);
        }

        /// <summary>비운다.</summary>
        public void Clear()
        {
            current = null;
            DestroySpawned();
        }

        /// <summary>좌우 반전을 바꾼다. 이미 서 있으면 다시 만든다.</summary>
        public void SetFlipX(bool value)
        {
            if (flipX == value)
                return;

            flipX = value;
            if (spawned != null)
                Rebuild();
        }

        private void OnDisable()
        {
            // 화면을 껐다 켜면 칸 크기가 다시 잡히므로, 켜질 때 새로 만드는 편이 안전하다.
            DestroySpawned();
        }

        private void OnEnable()
        {
            if (current != null)
                Rebuild();
        }

        private void Rebuild()
        {
            DestroySpawned();

            if (current == null || skeletonGraphicMaterial == null)
                return;

            var parent = Slot;
            if (parent == null)
                return;

            var go = new GameObject(ChildName, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);

            // 칸을 채우되 sizeMultiplier 만큼 키운다. 중앙 기준으로 대칭 확장해서 캐릭터가
            // 칸 한가운데 머문다. 이 프로젝트 규칙대로 앵커만 쓰고 sizeDelta 는 안 건드린다.
            float margin = (sizeMultiplier - 1f) * 0.5f;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(-margin, -margin);
            rect.anchorMax = new Vector2(1f + margin, 1f + margin);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);

            // <b>크기는 기준 캐릭터로 잰다</b> - 그래서 먼저 기준 캐릭터로 만든 뒤,
            // 재고 나서 진짜 캐릭터로 갈아끼운다(자세한 이유는 아래 측정 부분 주석 참고).
            var measured = sizeReference != null ? sizeReference : current;

            // 공식 팩토리 - SkeletonGraphic + SkeletonAnimation 을 붙이고 서로 연결까지 해준다.
            var components = SkeletonGraphic.AddSkeletonGraphicAnimationComponents(go, measured, skeletonGraphicMaterial);
            var graphic = components.skeletonRenderer;
            var animation = components.skeletonAnimation;

            if (graphic == null || animation == null)
            {
                Debug.LogError($"[SpineCharacterView] '{name}' Spine 컴포넌트 생성 실패", this);
                Destroy(go);
                return;
            }

            // 아틀라스가 여러 페이지면 반드시 켜야 한다. 안 켜면 일부 파츠가 아예 안 그려진다.
            // 재는 동안과 갈아끼운 뒤가 다를 수 있으므로 <b>둘 중 하나라도 여러 장이면</b> 켠다.
            graphic.allowMultipleCanvasRenderers =
                HasMultipleAtlasPages(measured) || HasMultipleAtlasPages(current);

            // 캐릭터 그림은 터치 대상이 아니다 - 그 위에 겹치는 버튼이 막힌다.
            graphic.raycastTarget = false;

            // Transform 의 localScale 을 뒤집는 게 아니라 Spine 이 스켈레톤 자체를 뒤집는 정석 경로.
            graphic.initialFlipX = flipX;

            graphic.Initialize(true);
            graphic.SetAllDirty();

            graphic.layoutScaleMode = SkeletonGraphic.LayoutMode.FitInParent;

            // <b>여기가 함정이다.</b> FitInParent 의 배율은 "지금 rect 크기 / referenceSize" 인데
            // referenceSize 기본값이 (1,1)이라 그냥 두면 배율이 칸의 픽셀 크기(수백 배)가 되어
            // 캐릭터가 화면을 뒤덮는다. 아래 호출이 실제 메시 바운드를 재서 referenceSize 에 넣는다.
            // 부르기 전에 두 가지가 되어 있어야 한다:
            //  - 캔버스 레이아웃 갱신: 방금 만든 자식이라 rect 크기가 아직 0일 수 있고,
            //    그 상태로 부르면 유효성 검사에 걸려 조용히 실패한다.
            //  - 메시 생성: 바운드를 재려면 메시가 있어야 한다(위 Initialize/SetAllDirty 가 그 역할).
            //
            // <b>⚠ 재는 대상은 '기준 캐릭터'다</b>(2026-08-30 사용자 지시). 캐릭터마다 자기 덩치로
            // 재면, 소품이 큰 캐릭터가 그만큼 작아진다 - 미스틱은 지팡이 때문에 폭이 라뷰린스의
            // 1.8배라 몸이 절반 가까이 줄었다. 기준 하나로 재두면 <b>누가 서든 같은 배율</b>이고
            // 소품은 칸 밖으로 넘친다. 배틀 화면이 원래 그렇게 하고 있었다(거기는 이 값이 씬에
            // 박혀 있다). 캐릭터를 추가해도 손댈 값이 없다.
            Canvas.ForceUpdateCanvases();
            if (!graphic.MatchReferenceRectWithBounds())
            {
                Debug.LogWarning($"[SpineCharacterView] '{name}': 크기 자동 측정 실패 " +
                                 $"(칸 크기 {rect.rect.size}). 캐릭터가 지나치게 크거나 작게 보이면 " +
                                 "칸이 0 크기인 채로 만들어진 것이다.", this);
            }

            SwapToRealCharacter(graphic, measured);

            spawned = go;
            activeGraphic = graphic;
            activePlayer = animation;

            // 재생은 마지막에. 갈아끼우면서 트랙이 비워지므로 여기서 틀어야 한다.
            ApplyAnimation();
        }

        /// <summary>
        /// 크기를 잰 뒤 <b>진짜 캐릭터로 갈아끼운다</b>. 재둔 <c>referenceSize</c> 는 그대로 남아서
        /// 누가 서든 같은 배율이 된다 - 배틀 화면이 하는 것과 똑같다(거기는 그 값이 씬에 박혀 있고
        /// <see cref="BattlePortraitBinder"/> 가 스켈레톤만 갈아끼운다).
        /// </summary>
        private void SwapToRealCharacter(SkeletonGraphic graphic, SkeletonDataAsset measured)
        {
            if (measured == current)
                return;

            graphic.skeletonDataAsset = current;

            // 갈아끼웠으면 <b>Initialize(true) 를 반드시 부른다</b> - 안 부르면 데이터만 바뀌고
            // 화면은 기준 캐릭터가 그대로 남는다.
            graphic.Initialize(true);
            graphic.SetAllDirty();
        }

        private void DestroySpawned()
        {
            activeGraphic = null;
            activePlayer = null;

            if (spawned == null)
            {
                // 씬에 남아 있던 잔재(에디터에서 만들어 둔 것 등)도 같이 걷어낸다.
                var leftover = Slot != null ? Slot.Find(ChildName) : null;
                if (leftover != null)
                    Destroy(leftover.gameObject);

                return;
            }

            Destroy(spawned);
            spawned = null;
        }

        private static bool HasMultipleAtlasPages(SkeletonDataAsset skeletonData)
        {
            if (skeletonData.atlasAssets == null || skeletonData.atlasAssets.Length == 0)
                return false;

            if (skeletonData.atlasAssets.Length > 1)
                return true;

            return skeletonData.atlasAssets[0].MaterialCount > 1;
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Spine.Unity;

namespace JojoPuzzle.EditorTools
{
    /// <summary>
    /// PlayerCharImage1 / PlayerCharImage2 / EnemyImage 안에 Spine 캐릭터를 배치하고
    /// idle 애니메이션을 재생시키는 1회성 셋업 도구. 메뉴에서 실행한다.
    ///
    /// 왜 씬 YAML을 직접 쓰지 않고 스크립트로 하는가:
    ///  - 이 캐릭터의 아틀라스가 <b>3페이지</b>라 머티리얼도 3개다. Unity UI는 렌더러 하나당
    ///    텍스처 하나만 쓸 수 있어서, SkeletonGraphic이 allowMultipleCanvasRenderers를 켜고
    ///    나머지 페이지용 자식 CanvasRenderer 오브젝트를 <b>직접 생성</b>해야 한다.
    ///    그 자식 오브젝트들은 Spine 내부 코드가 만드는 것이라 YAML로 손으로 흉내 낼 수 없다.
    ///  - spine-unity 4.3부터 SkeletonGraphic(렌더링)과 SkeletonAnimation(재생)이 두 컴포넌트로
    ///    분리됐고, 둘을 이어주는 초기화(Initialize)가 필요하다. 공식 팩토리 메서드
    ///    AddSkeletonGraphicAnimationComponents가 그 과정을 전부 처리한다.
    ///
    /// 기존 Image는 건드리지 않고 <b>자식 오브젝트</b>로 추가한다 - PlayerCharImage2는
    /// BoardInputController.leaderPortrait(불꽃이 날아갈 목표)로, EnemyImage는 HitFlinchUI 대상으로
    /// 이미 참조되고 있어서 컴포넌트를 갈아끼우면 그 연결이 끊긴다.
    /// </summary>
    public static class SpinePortraitSetup
    {
        private const string SkeletonDataPath = "Assets/SpineChar/1.Rabrith/20260816피타팝라뷰린스2_SkeletonData.asset";

        // 아틀라스가 straight alpha(머티리얼에 _STRAIGHT_ALPHA_INPUT 켜짐)라 PMA용이 아니라
        // -Straight 머티리얼을 써야 한다. PMA용을 쓰면 반투명 경계가 검게 뜬다.
        private const string MaterialPath =
            "Assets/Spine/Runtime/spine-unity/Materials/UI-StraightAlphaTex/SkeletonGraphicDefault-Straight.mat";

        private const string IdleAnimation = "1.idle";
        private const string ChildName = "SpineChar";

        /// <summary>
        /// 초상화 칸 대비 캐릭터를 얼마나 키울지. FitInParent는 "자식 rect에 딱 맞게" 줄이는데,
        /// 자식 rect를 부모보다 이만큼 크게 잡으면 그만큼 캐릭터도 커진다.
        /// referenceScale/referenceSize가 protected라 밖에서 못 건드리기 때문에 이 방식을 쓴다.
        /// 1.0이면 칸에 딱 맞고, 1.35면 칸 밖으로 조금 넘칠 만큼 커진다.
        /// </summary>
        private const float SizeMultiplier = 1.6f;

        /// <summary>
        /// 적은 왼쪽(플레이어 쪽)을 보도록 좌우 반전한다. 플레이어 초상화는 화면 왼쪽,
        /// 적은 오른쪽에 있어서 원본 방향 그대로면 둘 다 같은 쪽을 본다.
        /// </summary>
        private const string EnemyObjectName = "EnemyImage";

        private static readonly string[] TargetNames =
        {
            "PlayerCharImage1",
            "PlayerCharImage2",
            EnemyObjectName
        };

        [MenuItem("JojoPuzzle/Spine/초상화에 Spine 캐릭터 배치")]
        public static void Setup()
        {
            var skeletonData = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(SkeletonDataPath);
            if (skeletonData == null)
            {
                Debug.LogError($"[SpinePortraitSetup] SkeletonDataAsset을 못 찾음: {SkeletonDataPath}");
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Debug.LogError($"[SpinePortraitSetup] 머티리얼을 못 찾음: {MaterialPath}");
                return;
            }

            int done = 0;
            foreach (string targetName in TargetNames)
            {
                var parent = FindInScene(targetName);
                if (parent == null)
                {
                    Debug.LogWarning($"[SpinePortraitSetup] '{targetName}'을(를) 씬에서 못 찾음 - 건너뜀");
                    continue;
                }

                if (SetupOne(parent, skeletonData, material))
                    done++;
            }

            if (done > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log($"[SpinePortraitSetup] {done}개 배치 완료. 씬을 저장하세요.");
            }
        }

        /// <summary>
        /// 대사창(SpeechBubbleUI)의 초상화 칸에 Spine 캐릭터를 배치한다.
        /// 초상화 3개와 같은 방식이라 SetupOne을 그대로 재사용한다 - 다만 대상은 이름이 아니라
        /// SpeechBubbleUI가 참조하고 있는 portrait RectTransform으로 찾는다(이름이 바뀌어도 안전).
        ///
        /// 만들고 나면 SpeechBubbleUI의 portraitSpine 필드에 자동으로 연결까지 해준다.
        /// 어떤 캐릭터가 나올지는 런타임에 CharacterSpeechSet.spine으로 갈아끼우므로,
        /// 여기서 넣는 스켈레톤은 "자리를 잡기 위한 초기값"일 뿐이다.
        /// </summary>
        [MenuItem("JojoPuzzle/Spine/대사창에 Spine 캐릭터 배치")]
        public static void SetupSpeechBubble()
        {
            var bubble = Object.FindAnyObjectByType<JojoPuzzle.UI.SpeechBubbleUI>(FindObjectsInactive.Include);
            if (bubble == null)
            {
                Debug.LogError("[SpinePortraitSetup] 씬에서 SpeechBubbleUI를 못 찾았습니다.");
                return;
            }

            var so = new SerializedObject(bubble);
            var portraitProp = so.FindProperty("portrait");
            var target = portraitProp != null ? portraitProp.objectReferenceValue as RectTransform : null;
            if (target == null)
            {
                Debug.LogError("[SpinePortraitSetup] SpeechBubbleUI의 portrait가 비어 있습니다. " +
                               "먼저 인스펙터에서 초상화 RectTransform을 연결하세요.", bubble);
                return;
            }

            var skeletonData = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(SkeletonDataPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (skeletonData == null || material == null)
            {
                Debug.LogError("[SpinePortraitSetup] SkeletonDataAsset 또는 머티리얼을 못 찾았습니다.");
                return;
            }

            if (!SetupOne(target.gameObject, skeletonData, material))
                return;

            var spineChild = target.Find(ChildName);
            var graphic = spineChild != null ? spineChild.GetComponent<SkeletonGraphic>() : null;
            if (graphic != null)
            {
                so.FindProperty("portraitSpine").objectReferenceValue = graphic;
                so.ApplyModifiedProperties();
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[SpinePortraitSetup] 대사창에 Spine 캐릭터를 배치하고 portraitSpine에 연결했습니다. 씬을 저장하세요.");
        }

        private static bool SetupOne(GameObject parent, SkeletonDataAsset skeletonData, Material material)
        {
            // 이미 만들어둔 게 있으면 지우고 다시 만든다 - 애니메이션을 재작업해서 다시 임포트한 뒤
            // 이 메뉴를 또 눌러도 깨끗한 상태로 다시 세팅되게 하기 위함.
            var existing = parent.transform.Find(ChildName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject(ChildName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Setup Spine Portrait");
            go.layer = parent.layer;
            go.transform.SetParent(parent.transform, false);

            // 부모(초상화 칸)를 채우되 SizeMultiplier만큼 키운 크기로 스트레치.
            // 앵커를 중앙 기준으로 대칭 확장해서 캐릭터가 칸 중앙에 그대로 머물게 한다.
            // 이 프로젝트는 위치·크기를 퍼센트 앵커로만 정하는 규칙이라 sizeDelta/anchoredPosition은
            // 건드리지 않고 앵커만 쓴다.
            float margin = (SizeMultiplier - 1f) * 0.5f;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(-margin, -margin);
            rect.anchorMax = new Vector2(1f + margin, 1f + margin);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);

            // 공식 팩토리 - SkeletonGraphic + SkeletonAnimation을 붙이고 서로 연결까지 해준다.
            var components = SkeletonGraphic.AddSkeletonGraphicAnimationComponents(go, skeletonData, material);
            var graphic = components.skeletonRenderer;
            var animation = components.skeletonAnimation;

            if (graphic == null || animation == null)
            {
                Debug.LogError($"[SpinePortraitSetup] '{parent.name}' 컴포넌트 생성 실패");
                Object.DestroyImmediate(go);
                return false;
            }

            // 아틀라스가 여러 페이지면 반드시 켜야 한다. 안 켜면 Spine이 에러를 내고 일부 파츠가
            // 아예 안 그려진다(Unity UI는 렌더러 하나당 텍스처 하나만 지원).
            graphic.allowMultipleCanvasRenderers = HasMultipleAtlasPages(skeletonData);

            // 초상화는 터치 대상이 아니다. 특히 PlayerCharImage 쪽은 그 위에 스킬 게이지(SkillImage)가
            // 겹쳐 있어서, 여기서 레이캐스트를 먹으면 스킬 탭이 막힌다.
            graphic.raycastTarget = false;

            // 적은 왼쪽(플레이어 초상화 쪽)을 보도록 좌우 반전. initialFlipX는 직렬화되는 값이라
            // 씬을 저장하면 그대로 남고, Initialize 때 Skeleton.ScaleX = -1로 반영된다
            // (Transform의 localScale을 뒤집는 것과 달리 Spine이 스켈레톤 자체를 뒤집는 정석 경로).
            graphic.initialFlipX = parent.name == EnemyObjectName;

            animation.AnimationName = IdleAnimation;
            animation.loop = true;

            graphic.Initialize(true);
            graphic.SetAllDirty();

            // 부모 칸 크기에 맞춰 자동으로 스케일되게 한다. 캐릭터 원본이 823x1241이라
            // 이걸 안 하면 초상화 칸을 한참 벗어난다.
            graphic.layoutScaleMode = SkeletonGraphic.LayoutMode.FitInParent;

            // FitInParent의 배율은 "현재 rect 크기 / referenceSize"로 계산되는데, referenceSize의
            // 기본값이 (1,1)이라 그대로 두면 배율이 칸의 픽셀 크기(=수백 배)가 되어 캐릭터가
            // 화면을 뒤덮는다. 아래 호출이 실제 스켈레톤 메시 바운드를 재서 referenceSize에 넣어준다.
            //
            // 두 가지를 먼저 해줘야 한다:
            //  - Canvas 레이아웃 강제 갱신: 방금 만든 자식이라 rect 크기가 아직 0일 수 있는데,
            //    그 상태로 부르면 유효성 검사에 걸려 그냥 실패한다.
            //  - 메시 생성: 바운드를 재려면 메시가 있어야 한다(Initialize/SetAllDirty가 그 역할).
            Canvas.ForceUpdateCanvases();
            bool matched = graphic.MatchReferenceRectWithBounds();

            Vector2 rectSize = rect.rect.size;
            if (!matched)
            {
                Debug.LogWarning(
                    $"[SpinePortraitSetup] '{parent.name}': referenceSize 자동 측정 실패 " +
                    $"(칸 크기 {rectSize}). 캐릭터가 지나치게 크거나 작게 보이면 인스펙터에서 " +
                    $"SkeletonGraphic > Advanced > Match RectTransform with Bounds를 눌러주세요.", go);
            }
            else
            {
                Debug.Log($"[SpinePortraitSetup] '{parent.name}' 배치 완료 (칸 크기 {rectSize})", go);
            }

            EditorUtility.SetDirty(graphic);
            EditorUtility.SetDirty(animation);
            return true;
        }

        private static bool HasMultipleAtlasPages(SkeletonDataAsset skeletonData)
        {
            if (skeletonData.atlasAssets == null || skeletonData.atlasAssets.Length == 0)
                return false;

            if (skeletonData.atlasAssets.Length > 1)
                return true;

            return skeletonData.atlasAssets[0].MaterialCount > 1;
        }

        /// <summary>비활성 오브젝트까지 포함해서 이름으로 찾는다(GameObject.Find는 비활성을 못 찾음).</summary>
        private static GameObject FindInScene(string name)
        {
            foreach (var go in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go.name == name)
                    return go.gameObject;
            }
            return null;
        }
    }
}

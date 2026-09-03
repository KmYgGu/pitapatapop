using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Spine.Unity;
using Spine.Unity.Editor;

namespace JojoPuzzle.EditorTools
{
    /// <summary>
    /// 초상화의 Spine 캐릭터를 <b>코드 제어(SkeletonAnimation)</b>에서
    /// <b>Unity 애니메이터 제어(SkeletonMecanim)</b>로 바꾸거나 되돌린다.
    ///
    /// 바뀌는 것:
    ///  - SkeletonAnimation(코드가 AnimationState를 직접 조작) → SkeletonMecanim(Animator가 몰아줌)
    ///  - Animator 컴포넌트가 붙고(SkeletonMecanim이 RequireComponent로 요구), 거기에
    ///    Spine이 생성한 AnimatorController가 물린다. 그 컨트롤러를 Animator 창에서 편집하면 된다.
    ///
    /// 컨트롤러는 Spine의 공식 생성기(SkeletonBaker.GenerateMecanimAnimationClips)로 만든다.
    /// SkeletonDataAsset 옆에 <c>*_Controller.controller</c>로 생기고, 스켈레톤의 모든
    /// 애니메이션이 State로 자동 등록된다. 이미 있으면 그걸 그대로 쓴다.
    ///
    /// 이 흐름은 spine-unity 자신의 AssetUtility.TryInitializeSkeletonMecanim을 그대로 따른 것이다.
    /// </summary>
    public static class SpineMecanimSwitch
    {
        private const string ChildName = "SpineChar";

        private static readonly string[] TargetNames =
        {
            "PlayerCharImage1",
            "PlayerCharImage2",
            "EnemyImage"
        };

        [MenuItem("JojoPuzzle/Spine/애니메이터(Mecanim)로 전환")]
        public static void SwitchToMecanim()
        {
            int done = 0;
            foreach (string targetName in TargetNames)
            {
                var spineChar = FindSpineChar(targetName);
                if (spineChar == null)
                    continue;

                if (ConvertOne(spineChar))
                    done++;
            }

            if (done == 0)
            {
                Debug.LogWarning("[SpineMecanimSwitch] 전환한 대상이 없습니다. " +
                                 "먼저 'JojoPuzzle > Spine > 초상화에 Spine 캐릭터 배치'를 실행하세요.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[SpineMecanimSwitch] {done}개를 Mecanim으로 전환했습니다. 씬을 저장하세요.\n" +
                      "Animator 창에서 편집할 컨트롤러는 SkeletonData 애셋 옆의 *_Controller 파일입니다.");
        }

        private static bool ConvertOne(GameObject spineChar)
        {
            var graphic = spineChar.GetComponent<SkeletonGraphic>();
            if (graphic == null)
            {
                Debug.LogWarning($"[SpineMecanimSwitch] '{spineChar.name}'에 SkeletonGraphic이 없습니다.", spineChar);
                return false;
            }

            var skeletonData = graphic.skeletonDataAsset;
            if (skeletonData == null)
            {
                Debug.LogWarning($"[SpineMecanimSwitch] '{spineChar.name}'에 SkeletonDataAsset이 없습니다.", spineChar);
                return false;
            }

            // 이미 SkeletonMecanim이 있다고 그냥 넘기면 안 된다.
            // 이전 버전이 남긴 SkeletonAnimation이 함께 붙어 있는 상태가 실제로 있었고
            // (Undo 장부가 꼬여 지운 것이 되살아났다), 그때 여기서 건너뛰면
            // 잔재가 영영 안 걷혀 "전환은 되는데 본이 안 움직임"이 그대로 남는다.
            // 깨끗하면(1개) 건너뛰고, 지저분하면 아래 경로로 내려가 통째로 다시 만든다.
            if (spineChar.GetComponent<SkeletonMecanim>() != null &&
                CountAnimationComponents(spineChar) == 1)
            {
                // 컴포넌트는 정상. 컨트롤러만 빠졌으면 채워주고 끝낸다.
                var existingAnimator = spineChar.GetComponent<Animator>();
                if (existingAnimator != null && existingAnimator.runtimeAnimatorController == null &&
                    skeletonData.controller != null)
                {
                    existingAnimator.runtimeAnimatorController = skeletonData.controller;
                    EditorUtility.SetDirty(existingAnimator);
                    Debug.Log($"[SpineMecanimSwitch] '{spineChar.name}'에 컨트롤러를 다시 물렸습니다.", spineChar);
                    return true;
                }

                Debug.Log($"[SpineMecanimSwitch] '{spineChar.name}'는 이미 Mecanim입니다 - 건너뜀", spineChar);
                return false;
            }

            // 컨트롤러가 없으면 지금 만든다. 스켈레톤의 모든 애니메이션이 State로 들어간
            // AnimatorController가 SkeletonData 애셋 옆에 생성되고 controller 필드에 물린다.
            if (skeletonData.controller == null)
            {
                SkeletonBaker.GenerateMecanimAnimationClips(skeletonData);
                AssetDatabase.SaveAssets();

                if (skeletonData.controller == null)
                {
                    Debug.LogError($"[SpineMecanimSwitch] '{skeletonData.name}' 컨트롤러 생성 실패", skeletonData);
                    return false;
                }
                Debug.Log($"[SpineMecanimSwitch] 컨트롤러 생성: " +
                          $"{AssetDatabase.GetAssetPath(skeletonData.controller)}", skeletonData.controller);
            }

            // 기존 애니메이션 컴포넌트를 남김없이 걷어낸다.
            //
            // 하나만 남기는 게 절대 조건이다. SkeletonGraphic의 animation 참조는 인터페이스 타입
            // (ISkeletonAnimation)이라 씬에 직렬화되지 않고 런타임에 GetComponent로 다시 찾는다.
            // 그래서 SkeletonAnimation과 SkeletonMecanim이 함께 붙어 있으면 어느 쪽이 잡힐지
            // 컴포넌트 순서에 좌우되고, 둘 다 같은 스켈레톤에 포즈를 써서 서로 덮어쓴다
            // (증상: 애니메이터 전환은 되는데 본이 셋업 포즈에서 안 움직임).
            RemoveAnimationComponents(spineChar);
            graphic.Animation = null;

            // Undo.AddComponent와 일반 DestroyImmediate를 섞으면 실행 취소 장부가 꼬여
            // 방금 지운 컴포넌트가 되살아나는 일이 있다. 추가/삭제를 같은 방식으로 통일한다.
            var mecanim = spineChar.AddComponent<SkeletonMecanim>();

            // SkeletonMecanim은 [RequireComponent(typeof(Animator))]라 Animator가 함께 붙는다.
            // 컨트롤러는 반드시 Initialize 전에 물려야 한다 - translator가 Animator를 잡을 때
            // 컨트롤러가 없으면 IsValid가 false로 남는다.
            var animator = spineChar.GetComponent<Animator>();
            animator.runtimeAnimatorController = skeletonData.controller;

            graphic.Animation = mecanim;
            mecanim.Initialize(true);
            graphic.Initialize(true);
            graphic.SetAllDirty();

            EditorUtility.SetDirty(graphic);
            EditorUtility.SetDirty(mecanim);
            EditorUtility.SetDirty(animator);

            VerifySingleAnimationComponent(spineChar, "SkeletonMecanim");
            return true;
        }

        [MenuItem("JojoPuzzle/Spine/코드 제어(SkeletonAnimation)로 되돌리기")]
        public static void SwitchBackToCode()
        {
            int done = 0;
            foreach (string targetName in TargetNames)
            {
                var spineChar = FindSpineChar(targetName);
                if (spineChar == null)
                    continue;

                var graphic = spineChar.GetComponent<SkeletonGraphic>();
                var mecanim = spineChar.GetComponent<SkeletonMecanim>();
                if (graphic == null || mecanim == null)
                    continue;

                RemoveAnimationComponents(spineChar);
                graphic.Animation = null;

                // Animator는 SkeletonMecanim이 요구해서 붙었던 것 - 같이 걷어낸다.
                var animator = spineChar.GetComponent<Animator>();
                if (animator != null)
                    Object.DestroyImmediate(animator);

                var animation = spineChar.AddComponent<SkeletonAnimation>();
                graphic.Animation = animation;
                animation.Initialize(true);
                graphic.Initialize(true);
                graphic.SetAllDirty();

                EditorUtility.SetDirty(graphic);
                VerifySingleAnimationComponent(spineChar, "SkeletonAnimation");
                done++;
            }

            if (done > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log($"[SpineMecanimSwitch] {done}개를 코드 제어로 되돌렸습니다. 씬을 저장하세요.");
            }
        }

        /// <summary>
        /// 이 오브젝트에 붙은 애니메이션 컴포넌트(ISkeletonAnimation 구현체)를 전부 제거한다.
        /// 구체 타입이 아니라 인터페이스로 훑는 이유: SkeletonAnimation / SkeletonMecanim /
        /// 사용자 파생 클래스 중 무엇이 남아 있든 확실히 잡아내기 위함.
        /// </summary>
        private static void RemoveAnimationComponents(GameObject go)
        {
            foreach (var component in go.GetComponents<MonoBehaviour>())
            {
                if (component is ISkeletonAnimation)
                    Object.DestroyImmediate(component);
            }
        }

        /// <summary>
        /// 이 오브젝트에 붙은 애니메이션 컴포넌트(ISkeletonAnimation) 개수.
        /// </summary>
        private static int CountAnimationComponents(GameObject go)
        {
            int count = 0;
            foreach (var component in go.GetComponents<MonoBehaviour>())
            {
                if (component is ISkeletonAnimation)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 애니메이션 컴포넌트가 정확히 하나만 남았는지 확인한다. 둘 이상이면 서로 스켈레톤을
        /// 덮어써서 "전환은 되는데 본이 안 움직이는" 증상이 나므로, 조용히 넘기지 않고 에러로 알린다.
        /// </summary>
        private static void VerifySingleAnimationComponent(GameObject go, string expected)
        {
            int count = 0;
            string found = "";
            foreach (var component in go.GetComponents<MonoBehaviour>())
            {
                if (component is ISkeletonAnimation)
                {
                    count++;
                    found += (found.Length > 0 ? ", " : "") + component.GetType().Name;
                }
            }

            if (count == 1)
                return;

            Debug.LogError($"[SpineMecanimSwitch] '{go.name}'의 애니메이션 컴포넌트가 {count}개입니다 " +
                           $"({found}). {expected} 하나만 남아야 정상입니다 - 인스펙터에서 직접 지워주세요.", go);
        }

        private static GameObject FindSpineChar(string portraitName)
        {
            foreach (var rect in Object.FindObjectsByType<RectTransform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rect.name != portraitName)
                    continue;

                var child = rect.Find(ChildName);
                if (child != null)
                    return child.gameObject;

                Debug.LogWarning($"[SpineMecanimSwitch] '{portraitName}' 안에 '{ChildName}'가 없습니다.", rect);
                return null;
            }

            Debug.LogWarning($"[SpineMecanimSwitch] '{portraitName}'을(를) 씬에서 못 찾았습니다.");
            return null;
        }
    }
}

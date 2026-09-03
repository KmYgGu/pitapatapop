using System.IO;
using UnityEditor;
using UnityEngine;

namespace JojoPuzzle.EditorTools
{
    /// <summary>
    /// 아파트 모델의 <b>방 UI 판</b> 머티리얼을 만들고 모델에 연결한다.
    ///
    /// <b>메뉴가 남아 있는 이유</b>: 일회성 작업은 메뉴로 만들지 않는 게 이 프로젝트 방침이지만
    /// (씬·머티리얼·임포터 설정은 전부 YAML 이라 직접 고칠 수 있다), 이건 <b>모델을 다시
    /// 익스포트할 때마다</b> 다시 필요할 수 있는 작업이다. FBX 를 갈아끼우면 방 UI 판이 다시
    /// 불투명한 기본 머티리얼로 잡히는 경우가 있어서, 그때 이 메뉴 한 번이면 복구된다.
    ///
    /// 예전에 여기 있던 <c>씬 만들기</c> 메뉴는 <b>삭제했다</b>(2026-08-24 사용자 지시).
    /// 로그인·아파트 씬은 이미 만들어졌고, 아파트 씬에는 손으로 써 넣은 메인 화면 HUD 가
    /// 들어 있어서 다시 돌리면 오히려 그걸 날렸다. 모델을 바꿔도 씬은 다시 만들 필요가 없다 -
    /// 모델은 프리팹 인스턴스라 FBX 를 교체하면 씬이 저절로 따라온다.
    /// </summary>
    public static class ApartmentMaterialSetup
    {
        private const string ModelPath = "Assets/3dObject/최종아파트.fbx";

        /// <summary>모델용 머티리얼을 두는 곳. FBX 안에 묻어두면 손으로 못 고친다.</summary>
        private const string MaterialFolder = "Assets/3dObject/Materials";

        /// <summary>
        /// 방마다 하나씩 있는 <b>알림/UI 판</b>. 방 정면을 덮는 평면 한 장이고, 알림이 없을 때는
        /// 투명이라 방 안의 캐릭터가 그대로 보여야 한다(2026-08-24 사용자 설명).
        /// FBX 안의 머티리얼 이름과 정확히 같아야 remap 이 걸린다.
        /// </summary>
        private static readonly string[] RoomUiMaterialNames = { "ROOM_1_UI", "ROOM_2_UI", "ROOM_3_UI" };

        [MenuItem("JojoPuzzle/아파트/방 UI 머티리얼 만들기 (투명)")]
        public static void SetupRoomUiMaterialsMenu()
        {
            bool ok = TrySetupRoomUiMaterials(out string error);

            EditorUtility.DisplayDialog("방 UI 머티리얼",
                ok ? $"{RoomUiMaterialNames.Length}개를 투명으로 만들고 모델에 연결했습니다.\n\n{MaterialFolder}"
                   : error, "확인");
        }

        /// <summary>
        /// 방 UI 판을 <b>완전 투명</b>한 머티리얼로 만들고 모델의 그 슬롯에 연결한다.
        ///
        /// <b>왜 머티리얼을 애셋으로 빼는가</b>: FBX 가 만들어내는 머티리얼은 모델 안에 묻혀 있어
        /// 값을 고칠 수 없고, 모델을 다시 익스포트하면 되돌아간다. 애셋으로 빼서 remap 으로
        /// 연결해두면 모델을 몇 번 갈아끼워도 이 설정이 살아남는다.
        /// </summary>
        private static bool TrySetupRoomUiMaterials(out string error)
        {
            error = string.Empty;

            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                error = $"모델을 찾지 못했습니다:\n{ModelPath}";
                return false;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                error = "URP/Lit 셰이더를 찾지 못했습니다.";
                return false;
            }

            EnsureFolder(MaterialFolder);

            foreach (string name in RoomUiMaterialNames)
            {
                string path = $"{MaterialFolder}/{name}.mat";

                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }

                MakeFullyTransparent(material);
                EditorUtility.SetDirty(material);

                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
            }

            AssetDatabase.SaveAssets();
            importer.SaveAndReimport();
            return true;
        }

        /// <summary>
        /// URP/Lit 를 완전 투명(알파 0)으로 만든다.
        ///
        /// <b>함정</b>: URP/Lit 는 인스펙터에서 Surface Type 을 바꿀 때 프로퍼티와 <b>셰이더 키워드를
        /// 같이</b> 바꾼다. 코드로 만들면서 프로퍼티만 세우면 셰이더가 여전히 불투명 경로를 타서
        /// 알파를 0으로 줘도 <b>회색 판이 그대로 남는다</b>. 키워드·렌더큐·블렌드까지 같이 세워야 한다.
        /// </summary>
        private static void MakeFullyTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);   // 0 = Opaque, 1 = Transparent
            material.SetFloat("_Blend", 0f);     // 0 = Alpha
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            // 투명한 판이 깊이를 쓰면 뒤에 있는 방 안쪽이 가려진다.
            material.SetFloat("_ZWrite", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // 알파 0 = 알림이 없는 평소 상태. 알림을 띄울 때 이 알파만 올리면 판이 나타난다.
            var clear = new Color(1f, 1f, 1f, 0f);
            material.SetColor("_BaseColor", clear);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", clear);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.Core;

namespace JojoPuzzle.View
{
    /// <summary>
    /// 보드 위 패널 하나의 시각 표현. 로직은 전혀 갖지 않고 BoardView가 시키는 대로만 그림.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PanelView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;

        public int GridX { get; private set; }
        public int GridY { get; private set; }

        /// <summary>
        /// 풀에서 꺼내질 때마다(=전혀 다른 조각으로 다시 쓰일 때마다) 1씩 오르는 일련번호.
        ///
        /// 진행 중인 연출이 "내가 붙잡고 있던 그 조각이 맞는지"를 확인하는 데 쓴다. 풀이 스택이라
        /// 방금 반납한 뷰가 바로 다음 스폰에 그대로 다시 나오는데, 좌표만 비교하면 재사용된 뷰를
        /// 같은 조각으로 착각한다(박스 십자변환이 낙하 중인 칸을 덮어쓸 때 실제로 일어난다 -
        /// 낙하 연출과 펼치기 연출이 같은 오브젝트를 서로 다른 경로로 끌어당겨 덜덜 떨린다).
        /// </summary>
        public int Serial { get; private set; }
        public CellKind Kind { get; private set; }

        [Header("정렬 레이어")]
        [SerializeField] private int defaultSortingOrder = 0;
        [SerializeField] private int draggingSortingOrder = 100; // 드래그 중엔 항상 다른 패널들 위로

        /// <summary>
        /// BoardDimOverlay(퍼즐판 가림막)가 그려지는 층. 일반 패널(프레임 0 / 불꽃 +1 / 아이콘 +2)과
        /// 드래그 중인 패널(100)보다 위, AboveDimSortingOrder보다 아래다.
        /// 가림막이 이 값을 그대로 가져다 쓰므로 여기가 유일한 기준점이다.
        /// </summary>
        public const int DimOverlaySortingOrder = 150;

        [Tooltip("가림막 위로 올려 그릴 때 쓸 정렬 순서. 스탠드업 종료 시 날아가는 불꽃처럼 " +
                 "화면이 어두워져도 밝게 남아야 하는 것에만 쓴다.")]
        [SerializeField] private int aboveDimSortingOrder = 200;

        // 한 패널 안의 그리기 순서: 프레임(+0) → 불꽃(+1) → 아이콘(+2).
        // 불꽃은 "아이콘 뒤, 프레임 앞"에 와야 해서 그 사이에 한 칸을 비워둔 것.
        private const int FlameSortingOffset = 1;
        private const int IconSortingOffset = 2;

        // 셀 하나가 차지해야 할 월드 유닛 크기. BoardView가 Initialize 때 전달.
        // 프레임 이미지의 원본 해상도/PPU가 제각각이어도 이 값에 맞춰 자동으로 스케일 조정됨.
        private float targetCellSize = 1f;
        private Vector3 baseScale = Vector3.one; // FitScaleToCell로 계산된 "정상 크기". 애니메이션 배율의 기준값.
        private Color baseColor = Color.white; // Setup 시점의 원래 색. 반짝임 효과의 기준값.
        private Coroutine pulseCoroutine; // 이 패널 자신이 소유하는 반짝임 코루틴 - 재사용 시 확실히 정지 가능

        [Header("캐릭터 아이콘 (프레임 위에 겹쳐 그리는 자식 레이어)")]
        [SerializeField] private float iconInsetScale = 0.8f; // 프레임보다 살짝 작게 - 프레임 테두리가 보이도록
        private SpriteRenderer iconRenderer;
        private Sprite currentIconSprite; // FitScaleToCell이 합체 등으로 재호출될 때 아이콘도 같이 재조정하기 위해 캐싱
        private Vector3 iconBaseScale = Vector3.one; // FitIconToCell이 계산한 아이콘의 "정상 크기"
        private float iconScaleMultiplier = 1f;      // 그 위에 곱하는 연출용 배율(스탠드업 숨쉬기)

        [Header("스탠드업 불꽃 (아이콘 뒤 / 프레임 앞)")]
        [SerializeField] private Material flameMaterial;       // Assets/Shader/FlameAura.mat - 모든 패널이 공유
        [SerializeField] private float flameScale = 1.5f;      // 셀 크기 대비 불꽃 쿼드 크기(오라가 프레임을 감싸도록 넉넉하게)
        [SerializeField] private float flameYOffset = 0.05f;   // 셀 크기 비율로 살짝 위로(오라가 위로 솟는 건 셰이더 _UpBias가 담당)
        [SerializeField] private Color flameTint = Color.white; // 정점 색으로 곱해지는 색 - 런타임에 프레임 색으로 덮어씀
        private SpriteRenderer flameRenderer;

        [Header("박스 아이템 3D 큐브")]
        [SerializeField] private float boxTiltX = -25f;              // 바닥면이 보이도록 아래로 기울이는 고정 각도
        [SerializeField] private float boxOscillationAmplitude = 35f; // 좌우로 오가는 최대 회전각(중앙 기준)
        [SerializeField] private float boxOscillationSpeed = 1.2f;    // 왕복 속도(클수록 빠르게 좌우로 오감)
        [SerializeField] private float boxApproachDistance = 0.25f;  // 정면(중앙)일 때 카메라 쪽으로 다가오는 거리
        private Transform cubeVisual;
        private Renderer cubeRenderer;
        private float boxRotTimer;

        // SpriteRenderer는 sprite가 null이면 color를 지정해도 아무것도 그리지 않는다.
        // 아이콘 아트가 없는 동안에도 색으로 구분되게 하기 위한 1x1 흰색 sprite fallback.
        private static Sprite fallbackSprite;

        public static Sprite FallbackSprite
        {
            get
            {
                if (fallbackSprite == null)
                {
                    var tex = new Texture2D(1, 1);
                    tex.SetPixel(0, 0, Color.white);
                    tex.Apply();
                    fallbackSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                }
                return fallbackSprite;
            }
        }

        private void Awake()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            // 큐브(박스 아이템 비주얼)는 여기서 만들지 않고 SetupBox가 처음 불릴 때 지연 생성한다.
            // 풀은 보드 칸 수만큼(기본 36개) 미리 만들어두는데, 실제로 동시에 존재하는 박스는
            // 보통 한두 개뿐이라 나머지 패널이 큐브 오브젝트/머티리얼을 들고 있을 이유가 없다.
            CreateIconVisual();
        }

        /// <summary>
        /// 캐릭터 아이콘을 프레임(루트의 spriteRenderer) 위에 겹쳐 그릴 자식 SpriteRenderer를
        /// 미리 만들어둔다(기본 비활성). 캐릭터가 계속 늘어나도 프레임 이미지와 아이콘 이미지를
        /// 미리 합성해둘 필요 없이, 이 둘을 런타임에 겹쳐서 그리는 방식으로 확장에 대응한다.
        /// </summary>
        private void CreateIconVisual()
        {
            var iconObj = new GameObject("IconVisual");
            iconObj.transform.SetParent(transform, false);

            iconRenderer = iconObj.AddComponent<SpriteRenderer>();
            iconRenderer.enabled = false;
        }

        // 모든 PanelView가 공유하는 큐브 메시. 6면의 UV 방향까지 통일된 완전히 동일한 지오메트리라
        // 인스턴스마다 새로 만들 이유가 없다 - 예전엔 패널마다 BuildUniformCubeMesh를 호출해서
        // 보드 칸 수만큼(36회) 같은 메시를 만들고 RecalculateNormals/Bounds까지 반복했었음.
        private static Mesh sharedCubeMesh;

        // Shader.Find는 느린 API라 패널마다(그리고 후보 이름마다) 반복 호출하면 로딩이 눈에 띄게 늘어난다.
        // 한 번 찾은 결과를 클래스 전체가 공유한다.
        private static Shader cachedCubeShader;
        private static bool cubeShaderResolved;

        /// <summary>
        /// 박스 아이템용 3D 큐브를 자식 오브젝트로 준비(최초 1회만 생성, 이후 재사용).
        /// Unity 기본 Cube 프리미티브는 면마다 UV 방향이 서로 달라(정면은 맞아도 옆면은 뒤집히는 등)
        /// 재질 레벨의 UV 보정 하나로는 모든 면을 동시에 맞출 수 없음. 그래서 프리미티브 대신
        /// 모든 면이 처음부터 일관된 방향의 UV를 갖도록 큐브 메시를 직접 생성함(메시는 static 공유).
        /// 머티리얼은 박스마다 다른 텍스처를 입혀야 해서 인스턴스별로 갖되, 이 패널이 실제로
        /// 박스가 될 때까지는 만들지 않는다.
        /// </summary>
        private void EnsureCubeVisual()
        {
            if (cubeVisual != null)
                return;

            var cubeObj = new GameObject("BoxCubeVisual");
            cubeObj.transform.SetParent(transform, false);

            if (sharedCubeMesh == null)
                sharedCubeMesh = BuildUniformCubeMesh();

            var meshFilter = cubeObj.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = sharedCubeMesh;

            cubeRenderer = cubeObj.AddComponent<MeshRenderer>();
            cubeRenderer.material = new Material(ResolveOpaqueUnlitShader());

            cubeVisual = cubeObj.transform;
            cubeVisual.gameObject.SetActive(false);
        }

        /// <summary>
        /// 한 변 길이 1인 큐브 메시를 직접 생성. 6개 면 각각 (법선 기준) 밖에서 봤을 때
        /// 좌하단=(0,0), 우하단=(1,0), 우상단=(1,1), 좌상단=(1,0)이 되도록 UV를 통일해서,
        /// 어떤 면이든 텍스처가 항상 같은 방향(뒤집힘 없이)으로 보이게 함.
        /// 감김 순서(winding)는 계산된 노멀이 의도한 바깥 방향과 일치하는지 확인해서 자동 보정.
        /// </summary>
        private static Mesh BuildUniformCubeMesh()
        {
            Vector3[] faceNormals = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down };

            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

            float h = 0.5f;

            foreach (var normal in faceNormals)
            {
                // 노멀과 거의 평행하지 않은 기준축을 골라 up/right를 모든 면에 대해 일관되게 계산
                Vector3 helper = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(helper, normal).normalized;
                Vector3 up = Vector3.Cross(normal, right).normalized;

                Vector3 center = normal * h;
                Vector3 bl = center - right * h - up * h;
                Vector3 tl = center - right * h + up * h;
                Vector3 tr = center + right * h + up * h;
                Vector3 br = center + right * h - up * h;

                int baseIndex = vertices.Count;
                vertices.Add(bl); vertices.Add(tl); vertices.Add(tr); vertices.Add(br);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));

                // bl->tl->tr 순서로 만든 노멀이 의도한 방향과 반대면 감김 순서를 뒤집어서 바깥을 향하게 함
                Vector3 computedNormal = Vector3.Cross(tl - bl, tr - bl).normalized;
                if (Vector3.Dot(computedNormal, normal) < 0f)
                {
                    triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1);
                    triangles.Add(baseIndex); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
                }
                else
                {
                    triangles.Add(baseIndex); triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);
                }
            }

            var colors = new Color[vertices.Count];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = Color.white; // 셰이더가 버텍스컬러를 곱하는 경우 대비 - 항상 흰색으로 채워둠

            var mesh = new Mesh { name = "UniformCube" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.colors = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// 큐브용 셰이더를 찾는다. Sprites/Default는 2D 스프라이트용이라 ZWrite Off(깊이를 안 씀)라서
        /// 3D 큐브에서는 실제 카메라 거리가 아니라 메시 내부 그리기 순서대로 면이 덮어써지는 문제가 있음.
        /// 그래서 ZWrite On(불투명)인 셰이더를 우선 찾고, 없을 때만 Sprites/Default로 폴백한다.
        /// </summary>
        private static Shader ResolveOpaqueUnlitShader()
        {
            // 결과를 static에 캐시 - Shader.Find는 비용이 큰 편이라 패널마다(그리고 못 찾은 후보마다)
            // 반복 호출하면 로딩 시간이 그만큼 늘어난다. 프로젝트가 쓰는 렌더 파이프라인은 실행 중에
            // 바뀌지 않으므로 한 번 찾은 결과를 계속 쓰면 된다(못 찾아서 null인 경우도 캐시).
            if (cubeShaderResolved)
                return cachedCubeShader;

            string[] candidates =
            {
                "Universal Render Pipeline/Unlit", // URP
                "Unlit/Texture",                   // Built-in RP
                "Standard",                        // Built-in RP (라이팅 있음, 최후 대비)
                "Sprites/Default"                  // 최후의 폴백 (ZWrite Off라 면 우선순위 문제 있을 수 있음)
            };

            foreach (var name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader != null)
                {
                    cachedCubeShader = shader;
                    break;
                }
            }

            cubeShaderResolved = true;
            return cachedCubeShader;
        }

        private void Update()
        {
            if (Kind != CellKind.Box || cubeVisual == null || !cubeVisual.gameObject.activeSelf)
                return;

            boxRotTimer += Time.deltaTime;

            // 사인파: 양 끝(진폭 최대점)에서 자연스럽게 속도가 0에 가까워졌다가(슬로우 인)
            // 반대 방향으로 다시 가속(슬로우 아웃)됨 - 상태머신 없이 수학적으로 그냥 나오는 효과.
            // 시작 위상(-π/2)을 줘서 t=0일 때 왼쪽 끝(-amplitude)에서 시작하게 함.
            // Z축도 Y축과 완전히 동일한 패턴(같은 진폭/속도/위상)으로 반시계→시계 왕복.
            float yAngle = boxOscillationAmplitude * Mathf.Sin(boxRotTimer * boxOscillationSpeed - Mathf.PI / 2f);
            float zAngle = yAngle;
            cubeVisual.localRotation = Quaternion.Euler(boxTiltX, yAngle, zAngle / 4);

            // 카메라 쪽으로 다가왔다 멀어지는 Z축 움직임: 현재 각도를 -1~1로 정규화해서 1-t^2 곡선을 그리면
            // 중앙(정면, angle=0)일 때 가장 가까이 다가오고 양 끝(좌/우 측면)일 때 원래 자리로 돌아옴.
            // "터치해봐"하고 슬쩍 다가오는 듯한 느낌을 노림. 카메라가 -Z에서 보고 있다고 가정하고
            // -Z 방향(카메라 쪽)으로 다가가게 함 - 반대로 움직이면 approachDistance 부호를 뒤집으면 됨.
            float normalizedAngle = boxOscillationAmplitude > 0f ? yAngle / boxOscillationAmplitude : 0f;
            float approachFactor = 1f - normalizedAngle * normalizedAngle;
            cubeVisual.localPosition = new Vector3(0f, 0f, -boxApproachDistance * approachFactor);
        }

        public void SetTargetCellSize(float size)
        {
            targetCellSize = size;
        }

        /// <summary>
        /// 스탠드업 타임 중 정사각형 합체 표시용: 이미 배치된 패널의 크기를 즉시 재조정한다.
        /// SetTargetCellSize는 값만 기억해뒀다가 다음 Setup 때 반영되는데, 합체는 Setup 없이
        /// 이미 떠 있는 패널을 그 자리에서 즉시 키우거나(원래 크기로) 되돌려야 해서 FitScaleToCell을
        /// 바로 재적용하는 별도 진입점이 필요함.
        /// </summary>
        public void SetMergedCellSize(float size)
        {
            targetCellSize = size;
            FitScaleToCell();
        }

        /// <summary>
        /// 현재 spriteRenderer.sprite(프레임)의 실제 unit 크기를 읽어서 targetCellSize에 맞게
        /// localScale을 역산. PPU가 이미지마다 달라도 화면상 크기는 항상 셀 크기로 통일됨.
        /// 아이콘(currentIconSprite)이 있으면 그 자식 스케일도 함께 재계산 - SetMergedCellSize로
        /// 정사각형이 커질 때도 아이콘이 프레임 크기에 맞춰 비율대로 같이 커지게 하기 위함.
        /// </summary>
        private void FitScaleToCell()
        {
            var sprite = spriteRenderer.sprite;
            if (sprite == null)
                return;

            Vector2 nativeSize = sprite.bounds.size; // 월드 유닛 기준 원본 크기 (PPU 반영된 값)
            if (nativeSize.x <= 0f || nativeSize.y <= 0f)
                return;

            // 가로/세로 중 "더 작은" 쪽을 기준으로 맞춰서 셀을 완전히 채움(cover 방식).
            // Max를 쓰면 정사각형이 아닌 이미지의 짧은 쪽에 여백(레터박스)이 생겨서
            // cellGap을 0으로 해도 간격처럼 보이는 문제가 있었음 - Min으로 채우면 그 문제가 없어짐
            // (다만 이미지가 정사각형이 아니면 긴 쪽이 살짝 잘릴 수 있음).
            float scale = targetCellSize / Mathf.Min(nativeSize.x, nativeSize.y);
            baseScale = new Vector3(scale, scale, 1f);
            transform.localScale = baseScale;

            FitIconToCell();
        }

        /// <summary>
        /// 아이콘 자식의 로컬 스케일을 계산. 부모(프레임)가 이미 baseScale만큼 커져 있으므로,
        /// 그 배율을 나눠서 보정해야 아이콘이 항상 "셀 크기 * iconInsetScale"의 월드 크기로
        /// 보인다(프레임과 아이콘의 원본 해상도가 서로 달라도 무관).
        /// </summary>
        private void FitIconToCell()
        {
            if (currentIconSprite == null || baseScale.x <= 0f)
                return;

            Vector2 iconNativeSize = currentIconSprite.bounds.size;
            if (iconNativeSize.x <= 0f || iconNativeSize.y <= 0f)
                return;

            float desiredWorldSize = targetCellSize * iconInsetScale;
            float iconScale = (desiredWorldSize / Mathf.Min(iconNativeSize.x, iconNativeSize.y)) / baseScale.x;

            iconBaseScale = new Vector3(iconScale, iconScale, 1f);
            ApplyIconScale();
        }

        /// <summary>
        /// 아이콘만 정상 크기(iconBaseScale)에 배율을 곱해서 키운다. 프레임이나 불꽃은 그대로 두고
        /// <b>아이콘 하나만</b> 움직이는 연출(스탠드업 중 말랑하게 숨쉬기)에 쓴다.
        /// 몸통까지 같이 키우는 SetScaleMultiplier와는 대상이 다르다.
        /// </summary>
        public void SetIconScaleMultiplier(float multiplier)
        {
            iconScaleMultiplier = multiplier;
            ApplyIconScale();
        }

        private void ApplyIconScale()
        {
            if (iconRenderer != null)
                iconRenderer.transform.localScale = iconBaseScale * iconScaleMultiplier;
        }

        /// <summary>
        /// 일반 패널로 표시. 프레임(frameSprite)이 배경으로 깔리고 그 위에 panelType.icon이
        /// 자식 레이어로 겹쳐진다. frameSprite가 없으면(프레임 셋 미연결 등) 기존처럼 단색
        /// 폴백으로 대체. 아이콘이 없는 캐릭터는 프레임만 보이고 아이콘 레이어는 꺼둔다
        /// (캐릭터 아트가 아직 없어도 프레임 색만으로 구분 가능하게).
        /// </summary>
        public void SetupNormal(int x, int y, PanelType panelType, Sprite frameSprite)
        {
            GridX = x;
            GridY = y;
            Kind = CellKind.Normal;

            if (frameSprite != null)
            {
                spriteRenderer.sprite = frameSprite;
                spriteRenderer.color = Color.white;
            }
            else
            {
                spriteRenderer.sprite = FallbackSprite;
                spriteRenderer.color = panelType != null ? panelType.themeColor : Color.gray;
            }

            baseColor = spriteRenderer.color;

            currentIconSprite = panelType != null ? panelType.icon : null;
            iconRenderer.enabled = currentIconSprite != null;
            iconRenderer.sprite = currentIconSprite;
            iconRenderer.color = Color.white;
            iconRenderer.sortingOrder = spriteRenderer.sortingOrder + IconSortingOffset;

            FitScaleToCell();
        }

        /// <summary>
        /// 불꽃 레이어를 준비(최초 1회만 생성). 큐브와 마찬가지로 지연 생성하는 이유는, 스탠드업
        /// 타임에 실제로 불타는 조각만 필요한데 풀에 미리 만들어두는 36개 전부가 오브젝트를
        /// 들고 있을 이유가 없기 때문.
        /// </summary>
        [Header("강화 표시 (파직파직)")]
        [Tooltip("강화된 조각 위에서 번쩍이는 스파크 스프라이트들. 여러 개 넣고 빠르게 갈아끼워 " +
                 "'파직파직' 하는 느낌을 낸다.")]
        [SerializeField] private Sprite[] sparkSprites;

        [Tooltip("스파크 기본 크기. 스프라이트 PPU 가 커서 값이 크게 들어간다 - " +
                 "3.7 이 대략 셀의 60%, 6 이면 셀을 살짝 넘는다.")]
        [SerializeField] private float sparkScale = 6f;

        [Tooltip("조각 하나에 붙는 스파크 수. 여러 개가 함께 돌면 '한 덩어리의 전기'로 보인다.")]
        [Min(1)]
        [SerializeField] private int sparkCount = 3;

        [Tooltip("스파크 색. 조금 푸르게 두면 전기처럼 보인다.")]
        [SerializeField] private Color sparkTint = new Color(0.8f, 0.95f, 1f, 1f);

        private SpriteRenderer[] sparkRenderers;
        private bool empowered;

        [Header("특수 패널 (미스틱 포지셔닝)")]
        [Tooltip("특수 패널 위를 도는 룬. 남은 매치 횟수만큼 돈다 - 숫자 없이도 몇 번 남았는지 읽힌다.")]
        [SerializeField] private Sprite specialSprite;

        [Tooltip("룬 색. 신비로운 느낌이라 보랏빛으로 둔다.")]
        [SerializeField] private Color specialTint = new Color(0.78f, 0.6f, 1f, 1f);

        [Tooltip("룬 크기(칸 크기 대비).")]
        [SerializeField] private float specialScale = 0.34f;

        private SpriteRenderer[] specialRenderers;
        private int specialShown;

        /// <summary>지금 몇 개의 룬을 돌리고 있는지. 0이면 특수 패널이 아니다.</summary>
        public int SpecialShown => specialShown;

        /// <summary>
        /// 특수 패널 표시를 켜고 끈다. <paramref name="matchesLeft"/> 만큼 룬이 돈다 -
        /// <b>남은 횟수를 숫자 없이 보여주는 방법</b>이라 매치할 때마다 하나씩 줄어든다.
        /// 실제로 도는 건 BoardView 의 Update 하나가 몰아서 굴린다(스파크와 같은 방식).
        /// </summary>
        public void SetSpecial(int matchesLeft)
        {
            specialShown = Mathf.Max(0, matchesLeft);

            EnsureSpecialVisual(specialShown);

            if (specialRenderers == null)
                return;

            for (int i = 0; i < specialRenderers.Length; i++)
            {
                if (specialRenderers[i] != null)
                    specialRenderers[i].enabled = i < specialShown && specialSprite != null;
            }
        }

        /// <summary>룬 하나를 한 프레임분 굴린다(BoardView 가 부른다).</summary>
        public void StepSpecial(int slot, Vector2 offset, float rotation, float alpha)
        {
            if (specialShown <= 0 || specialRenderers == null || specialSprite == null)
                return;

            if (slot < 0 || slot >= specialRenderers.Length)
                return;

            var sr = specialRenderers[slot];
            if (sr == null || !sr.enabled)
                return;

            sr.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);

            var color = specialTint;
            color.a = alpha;
            sr.color = color;
        }

        private void EnsureSpecialVisual(int count)
        {
            if (count <= 0)
                return;

            if (specialRenderers != null && specialRenderers.Length >= count)
                return;

            // 늘어날 일이 거의 없어(3개면 끝) 그때만 다시 만든다.
            var made = new SpriteRenderer[count];
            for (int i = 0; i < count; i++)
            {
                if (specialRenderers != null && i < specialRenderers.Length && specialRenderers[i] != null)
                {
                    made[i] = specialRenderers[i];
                    continue;
                }

                var obj = new GameObject("SpecialRune" + i);
                obj.transform.SetParent(transform, false);

                var sr = obj.AddComponent<SpriteRenderer>();
                sr.sprite = specialSprite;
                sr.sortingOrder = defaultSortingOrder + IconSortingOffset + 2; // 스파크보다도 위
                sr.enabled = false;
                made[i] = sr;
            }

            specialRenderers = made;

            for (int i = 0; i < specialRenderers.Length; i++)
            {
                if (specialRenderers[i] != null)
                {
                    specialRenderers[i].sprite = specialSprite;
                    specialRenderers[i].transform.localScale = Vector3.one * specialScale;
                }
            }
        }

        /// <summary>이 조각에 붙는 스파크 수. BoardView 가 몇 개를 굴릴지 알아야 해서 노출한다.</summary>
        public int SparkCount => Mathf.Max(1, sparkCount);

        /// <summary>이 조각이 강화 표시를 하고 있는지. BoardView 가 깜빡임 대상 목록을 세울 때 본다.</summary>
        public bool IsEmpowered => empowered;

        /// <summary>
        /// 강화 표시를 켜고 끈다. 실제 깜빡임은 BoardView 의 Update 하나가 몰아서 굴린다 -
        /// 조각마다 코루틴을 띄우지 않는 이 프로젝트의 방식이다.
        /// </summary>
        public void SetEmpowered(bool value)
        {
            empowered = value;

            if (!value)
            {
                if (sparkRenderers != null)
                {
                    for (int i = 0; i < sparkRenderers.Length; i++)
                    {
                        if (sparkRenderers[i] != null)
                            sparkRenderers[i].enabled = false;
                    }
                }
                return;
            }

            EnsureSparkVisual();
            for (int i = 0; i < sparkRenderers.Length; i++)
                sparkRenderers[i].enabled = true;
        }

        /// <summary>
        /// 스파크를 한 프레임분 갱신한다(BoardView 가 부른다).
        /// 스프라이트·위치·회전을 통째로 바꿔서 "같은 자리에서 계속 튄다"가 아니라
        /// "여기저기서 파직거린다"로 보이게 한다.
        /// </summary>
        public void StepSpark(int slot, int spriteIndex, Vector2 offset, float rotation,
            float scaleMultiplier, float alpha)
        {
            if (!empowered || sparkRenderers == null || sparkSprites == null || sparkSprites.Length == 0)
                return;

            if (slot < 0 || slot >= sparkRenderers.Length)
                return;

            var sr = sparkRenderers[slot];
            sr.sprite = sparkSprites[Mathf.Abs(spriteIndex) % sparkSprites.Length];
            sr.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            sr.transform.localScale = Vector3.one * (sparkScale * scaleMultiplier);

            var color = sparkTint;
            color.a = alpha;
            sr.color = color;
        }

        private void EnsureSparkVisual()
        {
            int count = SparkCount;
            if (sparkRenderers != null && sparkRenderers.Length == count)
                return;

            sparkRenderers = new SpriteRenderer[count];
            for (int i = 0; i < count; i++)
            {
                var obj = new GameObject("SparkVisual" + i);
                obj.transform.SetParent(transform, false);
                obj.transform.localScale = Vector3.one * sparkScale;

                var sr = obj.AddComponent<SpriteRenderer>();
                sr.sortingOrder = defaultSortingOrder + IconSortingOffset + 1; // 아이콘보다 위
                sr.enabled = false;
                sparkRenderers[i] = sr;
            }
        }

        [Header("힌트 표시")]
        [Tooltip("힌트로 지목된 조각을 밝게 보이게 할 때 쓸 머티리얼. 비워두면 기본 스프라이트 " +
                 "머티리얼로 흰색을 덮는다(원래 색 쪽으로 하얗게 뜬다). " +
                 "'Blend SrcAlpha One' 머티리얼을 넣으면 덮는 대신 더해져 빛나는 느낌이 난다.")]
        [SerializeField] private Material hintGlowMaterial;

        [Tooltip("힌트가 가장 밝을 때의 세기. 1이면 완전히 하얗게 덮인다.")]
        [Range(0f, 1f)]
        [SerializeField] private float hintGlowMaxStrength = 0.55f;

        private SpriteRenderer hintGlowRenderer;

        /// <summary>
        /// 힌트 반짝임의 세기를 설정한다(0 = 꺼짐, 1 = 가장 밝음).
        /// 실제 깜빡임 곡선은 BoardView 의 Update 하나가 굴린다 - 조각마다 코루틴을 띄우지 않는
        /// 이 프로젝트의 방식(스파크·숨쉬기와 같다).
        ///
        /// <b>밝기를 색 곱셈(SetBrightness)으로 내지 않는 이유</b>: 프레임 스프라이트가 붙어 있으면
        /// SpriteRenderer.color 가 흰색이라 아무리 곱해도 Clamp01 에서 잘려 하나도 안 밝아진다.
        /// 그래서 곱하는 대신 같은 그림을 흰색으로 <b>위에 한 장 더 얹는다</b>.
        /// </summary>
        public void SetHintGlow(float amount)
        {
            if (amount <= 0f)
            {
                if (hintGlowRenderer != null)
                    hintGlowRenderer.enabled = false;
                return;
            }

            EnsureHintGlowVisual();

            // 매번 프레임 스프라이트를 따라간다 - 스킬 변환으로 색이 바뀌어도 저절로 맞는다.
            hintGlowRenderer.sprite = spriteRenderer.sprite;
            hintGlowRenderer.sortingOrder = spriteRenderer.sortingOrder + IconSortingOffset + 1;

            var color = Color.white;
            color.a = Mathf.Clamp01(amount) * hintGlowMaxStrength;
            hintGlowRenderer.color = color;
            hintGlowRenderer.enabled = true;
        }

        private void EnsureHintGlowVisual()
        {
            if (hintGlowRenderer != null)
                return;

            // 루트에 프레임 스프라이트가 붙어 있으므로, 자식을 그대로(스케일 1, 위치 0) 두면
            // 같은 그림이 정확히 겹친다. 불꽃·스파크와 같은 지연 생성 방식.
            var obj = new GameObject("HintGlow");
            obj.transform.SetParent(transform, false);

            hintGlowRenderer = obj.AddComponent<SpriteRenderer>();
            if (hintGlowMaterial != null)
                hintGlowRenderer.sharedMaterial = hintGlowMaterial;
            hintGlowRenderer.enabled = false;
        }

        private void EnsureFlameVisual()
        {
            if (flameRenderer != null)
                return;

            var flameObj = new GameObject("FlameVisual");
            flameObj.transform.SetParent(transform, false);

            flameRenderer = flameObj.AddComponent<SpriteRenderer>();
            flameRenderer.sprite = FallbackSprite; // 셰이더가 UV만으로 그리므로 1x1 흰색이면 충분
            flameRenderer.sharedMaterial = flameMaterial; // 모든 패널이 같은 머티리얼을 공유(인스턴스 생성 안 함)
            flameRenderer.enabled = false;
        }

        /// <summary>
        /// 스탠드업 타임에 이 조각이 고정될 때 아이콘 뒤에서 타오르는 불꽃을 켜고 끈다.
        /// 색은 머티리얼이 아니라 SpriteRenderer.color(정점 색)로 넘기므로, 캐릭터마다 색이 달라도
        /// 머티리얼 인스턴스가 늘어나지 않는다.
        /// </summary>
        public void SetFlameActive(bool active)
        {
            if (!active)
            {
                if (flameRenderer != null)
                    flameRenderer.enabled = false;
                return;
            }

            if (flameMaterial == null)
                return; // 머티리얼이 연결 안 돼 있으면 조용히 무시(불꽃만 안 나올 뿐 게임은 정상 진행)

            EnsureFlameVisual();

            flameRenderer.color = flameTint;
            flameRenderer.sortingOrder = spriteRenderer.sortingOrder + FlameSortingOffset;

            // 프레임보다 크게 그려서 불꽃이 조각 밖으로 솟아 보이게 한다. 루트가 이미 baseScale만큼
            // 커져 있으므로 그 배율을 나눠서 보정해야 의도한 실제 크기가 나온다(큐브와 같은 이유).
            float rootScale = baseScale.x != 0f ? baseScale.x : 1f;
            float spriteUnits = FallbackSprite.bounds.size.x; // 1x1 폴백 스프라이트의 원본 크기
            float worldSize = targetCellSize * flameScale;
            flameRenderer.transform.localScale = Vector3.one * (worldSize / spriteUnits / rootScale);
            flameRenderer.transform.localPosition = new Vector3(0f, targetCellSize * flameYOffset / rootScale, 0f);

            flameRenderer.enabled = true;
        }

        /// <summary>
        /// 프레임과 아이콘(=조각의 몸통)만 숨기고 불꽃은 그대로 둔다.
        /// 스탠드업 종료 연출에서 "프레임은 안 보이고 불꽃만 남은" 상태를 만들기 위한 것.
        /// 풀에 반납됐다가 재사용될 때 ResetForReuse가 다시 켜주므로 따로 되돌릴 필요는 없다.
        /// </summary>
        public void SetBodyVisible(bool visible)
        {
            spriteRenderer.enabled = visible;

            if (iconRenderer != null)
                iconRenderer.enabled = visible && currentIconSprite != null;
        }

        /// <summary>
        /// 이번 사용에 한해 불꽃 머티리얼을 바꾼다(스탠드업 종료의 "타들어가는" 연출용).
        /// null을 주면 기본 머티리얼로 되돌아가며, 풀에 반납됐다 재사용될 때도 ResetForReuse가 되돌린다.
        /// </summary>
        public void SetFlameMaterialOverride(Material material)
        {
            EnsureFlameVisual();

            if (flameRenderer != null)
                flameRenderer.sharedMaterial = material != null ? material : flameMaterial;
        }

        /// <summary>
        /// 불꽃 크기를 셀 크기와 무관하게 직접 지정한다(월드 유닛).
        /// 여러 칸짜리 무리를 통째로 덮는 큰 불꽃 하나를 만들 때 사용.
        /// </summary>
        public void SetFlameWorldSize(float worldSize)
        {
            if (flameRenderer == null)
                return;

            // 루트가 이미 baseScale만큼 커져 있으므로 그 배율을 나눠야 의도한 실제 크기가 나온다.
            float rootScale = baseScale.x != 0f ? baseScale.x : 1f;
            float spriteUnits = FallbackSprite.bounds.size.x;

            flameRenderer.transform.localScale = Vector3.one * (worldSize / spriteUnits / rootScale);
            flameRenderer.transform.localPosition = Vector3.zero;
        }

        /// <summary>
        /// 불꽃 색을 바꾼다. 이 조각이 화면에 그려지는 프레임 색을 그대로 넘기는 용도
        /// (BoardView.ApplyStandUpLook 참고). 켜져 있는 동안 호출해도 즉시 반영된다.
        /// </summary>
        public void SetFlameTint(Color tint)
        {
            flameTint = tint;
            if (flameRenderer != null)
                flameRenderer.color = tint;
        }

        /// <summary>
        /// 이미 배치된 패널의 아이콘만 교체한다(프레임/색/위치는 그대로).
        /// 스탠드업 타임에 고정된 조각을 전용 아이콘으로 바꾸거나, 고정이 풀려서 원래 아이콘으로
        /// 되돌릴 때 사용. Setup* 계열은 프레임까지 전부 다시 그리므로 이 용도로는 과하다.
        /// 크기는 새 스프라이트의 원본 해상도에 맞춰 다시 계산한다(FitIconToCell) - 아이콘마다
        /// 해상도가 달라도 화면상 크기가 일정하게 유지되도록.
        /// </summary>
        public void SetIconSprite(Sprite sprite)
        {
            if (currentIconSprite == sprite)
                return; // 같은 아이콘이면 스케일 재계산도 할 필요 없음

            currentIconSprite = sprite;
            iconRenderer.sprite = sprite;
            iconRenderer.enabled = sprite != null;

            FitIconToCell();
        }

        /// <summary>
        /// 박스 아이템으로 표시. 2D 스프라이트 대신 3D 큐브를 보여주고, 큐브 표면에는
        /// 매치를 만든 퍼즐(panelType)의 이미지를 텍스처로 입힌다. 아이콘이 없으면 테마 색으로 대체.
        /// </summary>
        public void SetupBox(int x, int y, PanelType panelType, Sprite frameSprite)
        {
            GridX = x;
            GridY = y;
            Kind = CellKind.Box;

            EnsureCubeVisual(); // 이 패널이 박스로 쓰이는 첫 순간에만 큐브를 만든다

            spriteRenderer.enabled = false; // 2D 표시는 숨기고
            iconRenderer.enabled = false; // 박스는 3D 큐브 표면에 프레임+아이콘을 합성한 텍스처를 입히므로 2D 레이어는 불필요
            cubeVisual.gameObject.SetActive(true);
            boxRotTimer = 0f; // 왕복 진동을 항상 왼쪽 끝(정면+바닥+왼쪽면 보이는 자세)에서 새로 시작
            cubeVisual.localRotation = Quaternion.Euler(boxTiltX, -boxOscillationAmplitude, -boxOscillationAmplitude / 4);

            // cubeVisual은 루트(이 PanelView)의 자식이라 루트의 localScale이 곱해져서 적용된다.
            // 프레임 이미지 도입 이후 루트 스케일이 항상 1이라는 보장이 없어졌으므로(프레임 원본
            // 해상도에 따라 훨씬 작은 값일 수 있음), 그 배율을 나눠서 보정해야 "셀 크기의 80%"라는
            // 의도한 실제 월드 크기가 유지된다.
            float rootScale = baseScale.x != 0f ? baseScale.x : 1f;
            cubeVisual.localScale = Vector3.one * (targetCellSize * 0.8f / rootScale);

            var icon = panelType != null ? panelType.icon : null;

            if (frameSprite != null)
            {
                // 다른 퍼즐 조각과 같은 모습(프레임 배경 + 중앙 아이콘)이 되도록 큐브용 텍스처를
                // 합성해서 쓴다. 같은 (프레임,아이콘) 조합이면 매번 새로 만들지 않고 캐시에서
                // 재사용 - 박스가 자주 생성/소멸되는 모바일 환경에서 텍스처를 반복 생성/Destroy하며
                // 발생하는 GC 부담과 발열을 피하기 위함(GetOrBuildCompositeBoxTexture 참고).
                cubeRenderer.material.mainTexture = GetOrBuildCompositeBoxTexture(frameSprite, icon, iconInsetScale);
                cubeRenderer.material.mainTextureScale = Vector2.one;
                cubeRenderer.material.mainTextureOffset = Vector2.zero;
                cubeRenderer.material.color = Color.white;
            }
            else if (icon != null)
            {
                // 프레임 스프라이트가 없으면(프레임 셋 미연결 등) 기존처럼 아이콘만 큐브에 입힘
                cubeRenderer.material.mainTexture = icon.texture;

                // 스프라이트가 아틀라스(여러 이미지를 하나로 합친 텍스처)로 패킹돼 있으면
                // sprite.texture는 아틀라스 전체를 가리키므로, 기본 UV(0~1)로는 엉뚱한/빈 영역이
                // 큐브 면에 매핑돼서 "비어 보이는" 문제가 생김. 스프라이트의 실제 서브 영역만
                // 정확히 샘플링하도록 텍스처 오프셋/스케일을 보정 - 이러면 정면을 포함한 모든 면에
                // 항상 올바른 이미지가 나옴.
                Rect r = icon.rect;
                Vector2 texSize = new Vector2(icon.texture.width, icon.texture.height);
                cubeRenderer.material.mainTextureScale = new Vector2(r.width / texSize.x, r.height / texSize.y);
                cubeRenderer.material.mainTextureOffset = new Vector2(r.x / texSize.x, r.y / texSize.y);

                cubeRenderer.material.color = Color.white;
            }
            else
            {
                cubeRenderer.material.mainTexture = null;
                cubeRenderer.material.mainTextureScale = Vector2.one;
                cubeRenderer.material.mainTextureOffset = Vector2.zero;
                cubeRenderer.material.color = panelType != null ? panelType.themeColor : Color.yellow;
            }
        }

        // (프레임, 아이콘) 조합별로 합성한 큐브 텍스처를 캐싱 - 모든 PanelView 인스턴스가 공유하는
        // static 캐시. 같은 캐릭터+색 조합의 박스가 몇 번을 생성/소멸(풀 재사용)해도 텍스처 합성은
        // 그 조합이 처음 등장할 때 딱 한 번만 일어나고, 이후로는 계속 재사용만 함 - 런타임에
        // 텍스처를 반복 생성/Destroy하며 생기는 GC 부담과 발열을 피하기 위함(모바일 최적화).
        // 캐릭터 로스터가 늘어나도 조합 수는 결국 "캐릭터 수 x 2(기본색/스왑색)" 정도로 유한하므로
        // 캐시가 무한히 커질 걱정은 없음.
        private static readonly Dictionary<(Sprite frame, Sprite icon), Texture2D> boxTextureCache
            = new Dictionary<(Sprite frame, Sprite icon), Texture2D>();

        private static Texture2D GetOrBuildCompositeBoxTexture(Sprite frame, Sprite icon, float iconInsetScale)
        {
            var key = (frame, icon);
            if (boxTextureCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var texture = BuildCompositeBoxTexture(frame, icon, iconInsetScale);
            boxTextureCache[key] = texture;
            return texture;
        }

        /// <summary>
        /// 프레임 스프라이트를 배경으로, 그 중앙에 아이콘 스프라이트를 얹은 정사각 텍스처를 합성.
        /// GetPixels/SetPixels는 둘 다 "y=0이 맨 아래 행"이라는 같은 좌표계를 쓰므로, 소스와
        /// 대상을 같은 규칙으로만 다루면 상하 반전 걱정 없이 안전하게 합성할 수 있다.
        /// 소스 텍스처에 <b>Read/Write Enabled 가 필요 없다</b> - 픽셀을 GPU 를 거쳐 받아온다
        /// (<see cref="ReadSpritePixels"/>. 왜 그래야 하는지가 거기 적혀 있다).
        /// 호출부(GetOrBuildCompositeBoxTexture)가 캐싱하므로 이 메서드 자체는 캐시를 모른 채
        /// 순수하게 "한 번 합성"만 담당.
        /// </summary>
        private static Texture2D BuildCompositeBoxTexture(Sprite frame, Sprite icon, float iconInsetScale)
        {
            const int size = 128;
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;

            BlitSpriteIntoPixels(pixels, size, frame, 0, 0, size, size, false);

            if (icon != null)
            {
                int inset = Mathf.RoundToInt(size * (1f - iconInsetScale) * 0.5f);
                int iconSize = size - inset * 2;
                BlitSpriteIntoPixels(pixels, size, icon, inset, inset, iconSize, iconSize, true);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// sprite의 제 영역을 (destW x destH)로 줄여 dest 픽셀 배열의 (destX, destY) 자리에
        /// 그려 넣는다. alphaBlend가 true면 아이콘처럼 투명 배경을 살려서 기존 픽셀 위에
        /// 덧그리고, false면 프레임처럼 완전히 덮어씀.
        ///
        /// <b>줄이는 일은 GPU 가 한다</b>(2026-08-30) - 예전엔 원본 크기 픽셀을 통째로 받아
        /// 여기서 최근접으로 솎아냈는데, 1024짜리 아이콘 하나에 16MB 배열이 잠깐 생겼다.
        /// 지금은 받아온 배열이 이미 destW x destH 라 그대로 옮기기만 한다.
        /// </summary>
        private static void BlitSpriteIntoPixels(Color[] dest, int destSize, Sprite sprite,
            int destX, int destY, int destW, int destH, bool alphaBlend)
        {
            if (sprite == null || destW <= 0 || destH <= 0)
                return;

            Color[] srcPixels = ReadSpritePixels(sprite, destW, destH);
            if (srcPixels == null)
                return;

            for (int y = 0; y < destH; y++)
            {
                int dy = destY + y;
                if (dy < 0 || dy >= destSize)
                    continue;

                for (int x = 0; x < destW; x++)
                {
                    int dx = destX + x;
                    if (dx < 0 || dx >= destSize)
                        continue;

                    Color srcColor = srcPixels[y * destW + x];
                    int destIndex = dy * destSize + dx;
                    dest[destIndex] = alphaBlend ? Color.Lerp(dest[destIndex], srcColor, srcColor.a) : srcColor;
                }
            }
        }

        // 스프라이트를 줄여 받을 때 쓰는 임시 판. 합성은 조합마다 딱 한 번이지만, 그 한 번마다
        // 텍스처를 새로 만들면 그것도 쓰레기가 된다 - 하나 만들어 계속 다시 쓴다.
        private static Texture2D scratchTexture;

        /// <summary>
        /// 스프라이트의 제 영역만 <paramref name="width"/>x<paramref name="height"/> 로 줄여
        /// CPU 가 읽을 수 있는 픽셀로 돌려준다. 못 읽으면 null.
        ///
        /// <b>왜 GPU 를 거치는가</b>(2026-08-30, 사용자가 아이콘을 새로 넣자 터져서 고침):
        /// 예전엔 <c>sprite.texture.GetPixels</c> 를 바로 불렀는데 그건 원본 텍스처가
        /// <b>Read/Write Enabled</b> 여야 한다. 새 아이콘은 꺼져 있어서 큐브를 만드는 순간
        /// 예외가 났다(<c>Texture2D.GetPixels: texture data is ... not readable</c>).
        ///
        /// 그 깃발을 켜서 막을 수도 있지만 <b>그러면 안 된다</b>:
        /// <list type="bullet">
        ///   <item>깃발을 켜면 텍스처마다 CPU 사본이 <b>영구히</b> 남는다. 아이콘이
        ///         1024x1024 면 한 장에 4MB, 1551x1551 이면 9MB 다.</item>
        ///   <item>원본 크기 그대로 <c>GetPixels</c> 하면 1024x1024 한 번에 <b>16MB 짜리
        ///         Color 배열</b>이 잠깐 생긴다 - 128칸짜리 그림 하나 만들려고.</item>
        ///   <item>무엇보다 <b>아이콘을 새로 넣을 때마다 또 터진다</b>. 깃발을 잊으면 끝이다.</item>
        /// </list>
        ///
        /// GPU 로 옮기면 셋 다 사라진다 - 깃발이 필요 없고, 줄인 뒤에 읽으니 배열도 작다.
        /// 스프라이트가 아틀라스 안에 있어도 제 영역만 잘라 온다.
        /// </summary>
        private static Color[] ReadSpritePixels(Sprite sprite, int width, int height)
        {
            var source = sprite != null ? sprite.texture : null;
            if (source == null || width <= 0 || height <= 0)
                return null;

            if (scratchTexture == null || scratchTexture.width < width || scratchTexture.height < height)
            {
                if (scratchTexture != null)
                    DestroyTexture(scratchTexture);

                scratchTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            // sRGB 로 오가야 색이 그대로다. 이 프로젝트는 Linear 색공간이라, 여기서 선형으로
            // 받으면 원본보다 밝게(또는 어둡게) 나온다.
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                                                RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;

            try
            {
                Rect rect = sprite.rect;
                var scale = new Vector2(rect.width / source.width, rect.height / source.height);
                var offset = new Vector2(rect.x / source.width, rect.y / source.height);

                // 그냥 옮겨 담기다(섞지 않는다) - 알파도 원본 그대로 넘어온다.
                Graphics.Blit(source, rt, scale, offset);

                RenderTexture.active = rt;
                scratchTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                scratchTexture.Apply(false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            return scratchTexture.GetPixels(0, 0, width, height);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (Application.isPlaying)
                Destroy(texture);
            else
                DestroyImmediate(texture);
        }

        [Header("방해블록")]
        [Tooltip("방해블록(Obstacle)으로 그릴 이미지. 비워두면 예전처럼 회색 사각형으로 그린다 - " +
                 "그림이 없어도 게임은 돌아가야 하므로 필수는 아니다.")]
        [SerializeField] private Sprite obstacleSprite;

        public void SetupObstacle(int x, int y)
        {
            GridX = x;
            GridY = y;
            Kind = CellKind.Obstacle;

            if (obstacleSprite != null)
            {
                spriteRenderer.sprite = obstacleSprite;
                spriteRenderer.color = Color.white; // 그림의 색을 그대로 쓴다
            }
            else
            {
                spriteRenderer.sprite = FallbackSprite;
                spriteRenderer.color = new Color(0.3f, 0.3f, 0.3f);
            }

            baseColor = spriteRenderer.color;
            currentIconSprite = null;
            iconRenderer.enabled = false; // 캐릭터가 아니므로 아이콘 레이어 없음

            // 그림의 원본 크기와 무관하게 셀 크기에 맞춰진다(PPU가 달라도 화면에서는 같은 크기).
            FitScaleToCell();
        }

        [Tooltip("구멍(Hole)으로 그릴 이미지. 비워두면 예전처럼 검은 사각형으로 그린다.")]
        [SerializeField] private Sprite holeSprite;

        public void SetupHole(int x, int y)
        {
            GridX = x;
            GridY = y;
            Kind = CellKind.Hole;

            if (holeSprite != null)
            {
                spriteRenderer.sprite = holeSprite;
                spriteRenderer.color = Color.white; // 그림의 색을 그대로 쓴다
            }
            else
            {
                spriteRenderer.sprite = FallbackSprite;
                spriteRenderer.color = Color.black;
            }

            baseColor = spriteRenderer.color;
            currentIconSprite = null;
            iconRenderer.enabled = false; // 캐릭터가 아니므로 아이콘 레이어 없음
            FitScaleToCell();
        }

        [Tooltip("유나의 점화 블록(BurnTrack)으로 그릴 이미지. 비워두면 주황색 사각형으로 그린다.")]
        [SerializeField] private Sprite burnTrackSprite;

        /// <summary>
        /// 유나의 <b>버닝 트랙!</b> 점화 블록. 색이 없는 장치라 방해블록·구멍과 같은
        /// 그리기 경로를 탄다 - 캐릭터 아이콘을 올리지 않는다.
        /// </summary>
        public void SetupBurnTrack(int x, int y)
        {
            GridX = x;
            GridY = y;
            Kind = CellKind.BurnTrack;

            if (burnTrackSprite != null)
            {
                spriteRenderer.sprite = burnTrackSprite;
                spriteRenderer.color = Color.white; // 그림의 색을 그대로 쓴다
            }
            else
            {
                spriteRenderer.sprite = FallbackSprite;
                spriteRenderer.color = new Color(0.92f, 0.45f, 0.12f);
            }

            baseColor = spriteRenderer.color;
            currentIconSprite = null;
            iconRenderer.enabled = false; // 캐릭터가 아니므로 아이콘 레이어 없음
            FitScaleToCell();
        }

        /// <summary>
        /// 풀에서 재사용될 때 이전 상태(정렬 순서 등)가 남아있지 않도록 초기화.
        /// PanelViewPool.Get()이 활성화 직후 자동으로 호출함.
        /// </summary>
        /// <summary>
        /// "지금부터 이 뷰의 위치·크기는 내가 정한다"고 선언한다. 진행 중이던 낙하/리필 연출은
        /// 시작 시점의 Serial을 들고 있으므로, 이 호출 이후로는 이 뷰를 건드리지 않는다.
        ///
        /// 정사각형 합체가 이걸 쓴다. 합체는 보드 데이터(칸 좌표)로 위치를 정하는데, 그 대상이
        /// 마침 낙하 중인 조각이면 낙하 연출이 매 프레임 자기 목적지로 다시 끌고 가서 합체된
        /// 블록이 엉뚱한 자리에 놓인다. 데이터로 정한 자리가 이겨야 한다.
        /// </summary>
        public void TakeLayoutOwnership()
        {
            Serial++;
        }

        public void ResetForReuse()
        {
            Serial++; // 이 시점부터는 "다른 조각"이다 - 예전 연출이 붙잡고 있었다면 여기서 끊긴다

            SetEmpowered(false); // 강화는 조각에 붙는 것이라, 다른 조각으로 재사용될 때 반드시 꺼야 한다
            SetSpecial(0);       // 특수 패널 표시도 같은 이유로 반드시 끈다
            SetHintGlow(0f);     // 힌트도 마찬가지 - 켜진 채 재사용되면 엉뚱한 조각이 반짝인다

            spriteRenderer.enabled = true;
            if (cubeVisual != null)
                cubeVisual.gameObject.SetActive(false);

            spriteRenderer.sortingOrder = defaultSortingOrder;

            currentIconSprite = null;
            iconRenderer.enabled = false;
            iconRenderer.sortingOrder = defaultSortingOrder + IconSortingOffset;
            iconBaseScale = Vector3.one;
            iconScaleMultiplier = 1f; // 스탠드업 숨쉬기 중에 반납됐어도 다음 사용은 정상 크기로 시작
            iconRenderer.transform.localScale = Vector3.one;

            // 스탠드업 중 불타던 조각이 그대로 반납됐을 수 있으므로 확실히 끈다 -
            // 안 그러면 전혀 다른 칸에서 재사용됐을 때 뜬금없이 불꽃이 남아있게 된다.
            SetFlameActive(false);

            // 종료 연출에서 갈아끼웠던 머티리얼도 기본으로 되돌린다.
            if (flameRenderer != null)
                flameRenderer.sharedMaterial = flameMaterial;

            transform.rotation = Quaternion.identity; // 폴드 이펙트로 회전됐던 값 초기화
            SetScaleMultiplier(1f, 1f); // 축소 이펙트로 줄었던 스케일 초기화
            StopPulsing(); // 반짝임 코루틴이 혹시 돌고 있었다면 확실히 정지 + 기본색 복원
        }

        /// <summary>
        /// 반짝임(밝아졌다 기본색으로 반복) 시작. 이 컴포넌트 자신이 코루틴을 소유하므로
        /// StopPulsing()으로 언제든 확실하게(같은 프레임에) 멈출 수 있음 - 외부 HashSet 등으로
        /// "멈춰야 함"을 전달하고 다음 체크 때까지 기다리는 간접 방식보다 안전함.
        /// </summary>
        public void StartPulsing(float period, float maxBrightnessFactor)
        {
            StopPulsing(); // 혹시 이미 돌고 있으면 먼저 정리
            pulseCoroutine = StartCoroutine(PulseRoutine(period, maxBrightnessFactor));
        }

        /// <summary>
        /// 반짝임을 즉시 정지하고 기본색으로 복원. 코루틴 자체를 StopCoroutine으로 끊으므로
        /// "다음 체크까지 기다렸다가 멈추는" 지연이 전혀 없음 - 재사용(ResetForReuse) 시에도 호출됨.
        /// </summary>
        public void StopPulsing()
        {
            if (pulseCoroutine != null)
            {
                StopCoroutine(pulseCoroutine);
                pulseCoroutine = null;
            }
            SetBrightness(1f);
        }

        private IEnumerator PulseRoutine(float period, float maxBrightnessFactor)
        {
            float t = 0f;
            while (true)
            {
                t += Time.deltaTime;
                float phase = (t % period) / period; // 0→1 반복
                // 사인의 양수 구간만 사용 - 어두워지지 않고 "밝아졌다가 기본색으로" 반복되게
                float brighten = Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f));
                SetBrightness(1f + brighten * (maxBrightnessFactor - 1f));
                yield return null;
            }
        }

        /// <summary>
        /// baseColor에 밝기 배율을 곱해서 적용. 1이면 기본색, 1보다 크면 밝아짐.
        /// 매치 대기 중 반짝임 효과에 사용.
        /// </summary>
        public void SetBrightness(float factor)
        {
            spriteRenderer.color = new Color(
                Mathf.Clamp01(baseColor.r * factor),
                Mathf.Clamp01(baseColor.g * factor),
                Mathf.Clamp01(baseColor.b * factor),
                baseColor.a);
        }

        public void SetGridPosition(int x, int y)
        {
            GridX = x;
            GridY = y;
        }

        /// <summary>
        /// 드래그로 집어든 동안 다른 패널들에 가려지지 않도록 최상단 레이어로.
        /// </summary>
        public void SetHeldOnTop(bool isHeld)
            => ApplySortingBase(isHeld ? draggingSortingOrder : defaultSortingOrder);

        /// <summary>
        /// 퍼즐판 가림막(BoardDimOverlay)보다 위로 올려 그린다. 스탠드업 종료 시 조각이 불꽃이 되어
        /// 캐릭터에게 날아가는 동안 쓴다 - 그 구간엔 퍼즐판이 어두워지는데, 정작 주인공인 불꽃까지
        /// 같이 어두워지면 안 되기 때문. 풀에 반납될 때 ResetState가 기본 층으로 되돌린다.
        /// </summary>
        public void SetRenderAboveDim(bool above)
            => ApplySortingBase(above ? aboveDimSortingOrder : defaultSortingOrder);

        /// <summary>한 패널 안의 상대 순서(프레임 → 불꽃 → 아이콘)를 유지한 채 기준 층만 옮긴다.</summary>
        private void ApplySortingBase(int order)
        {
            spriteRenderer.sortingOrder = order;
            iconRenderer.sortingOrder = order + IconSortingOffset; // 아이콘은 항상 프레임 위
            if (flameRenderer != null)
                flameRenderer.sortingOrder = order + FlameSortingOffset; // 불꽃은 그 사이
        }

        public void MoveTo(Vector3 worldPos)
        {
            transform.position = worldPos;
        }

        /// <summary>
        /// 정상 크기(baseScale)에 배율을 곱해서 적용. 수집 애니메이션에서 축소/플립 효과를 줄 때 사용.
        /// xMul을 음수로 주면 가로가 뒤집혀서 "회전하는 것처럼" 보이는 가짜 3D 플립 효과가 됨.
        /// </summary>
        public void SetScaleMultiplier(float xMul, float yMul)
        {
            transform.localScale = new Vector3(baseScale.x * xMul, baseScale.y * yMul, 1f);
        }
    }
}
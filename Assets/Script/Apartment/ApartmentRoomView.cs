using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// <b>입주한 캐릭터를 방 안에 실제로 세운다</b>(2026-08-28 사용자 지시).
    /// <see cref="ApartmentResidents"/> 가 "누가 어디 사는지"를 알고, 이 컴포넌트는 그걸 그린다.
    ///
    /// <b>세 갈래로 물러선다</b> - 캐릭터 데이터가 아직 고르지 않기 때문이다:
    ///  1. Spine 이 있으면 그걸 세운다(지금은 두 캐릭터뿐).
    ///  2. 없고 아이콘만 있으면 <b>아이콘을 세운다</b>.
    ///  3. 둘 다 없으면 <b>프레임 색 사각형</b>(`PuzzlePieceIcon` 이 쓰는 것과 같은 물러서기).
    /// 그림이 없다고 아무것도 안 세우면 "입주했는데 방이 비어 보인다"가 된다.
    ///
    /// <b>런타임에 만들고 버리지 않는다</b> - 방마다 자리를 하나씩 만들어두고 내용만 갈아끼운다
    /// (모바일 발열 방침, [[feedback-mobile-optimization]]).
    ///
    /// <b>월드 오브젝트다.</b> 아파트가 3D 모델이라 UI 로는 방 안에 넣을 수 없다.
    /// 아트 방침이 "2D 스프라이트를 세워둔 것처럼"이라 평면 하나를 세우는 것으로 충분하다.
    /// </summary>
    public class ApartmentRoomView : MonoBehaviour
    {
        [SerializeField] private ApartmentCameraRig cameraRig;
        [SerializeField] private ApartmentRooms rooms;

        [Tooltip("보유 캐릭터 목록. 시작할 때 <b>빈 방을 임시로 채우는</b> 데만 쓴다. " +
                 "비워두면 아무도 살지 않는 상태로 시작한다.")]
        [SerializeField] private CharacterRoster seedRoster;

        [Tooltip("켜면 <b>맨 처음 한 번만</b> 빈 방을 보유 캐릭터로 채워 그림을 확인할 수 있다. " +
                 "세이브가 생기면 꺼야 한다 - 저장된 입주 정보를 덮어쓰게 된다.")]
        [SerializeField] private bool seedEmptyRooms = true;

        /// <summary>
        /// 첫 배치를 이미 했는지. <b>static 이라 씬을 옮겨다녀도 남는다</b>(입주 정보와 같은 수명).
        ///
        /// <b>⚠ 이게 없으면 편성·스테이지 화면에 갔다 오는 것만으로 배치가 초기화된다</b>
        /// (2026-08-28 사용자 신고). 아파트 씬으로 돌아올 때마다 Start 가 다시 돌아서
        /// <b>비워둔 방을 로스터 순서대로 도로 채워버리기</b> 때문이다 - 옮기거나 내보낸 것이
        /// 전부 없던 일이 된다.
        /// </summary>
        private static bool seedApplied;

        [Header("크기와 자리")]
        [Tooltip("캐릭터 키를 방 높이의 몇 할로 할지.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float heightFraction = 0.58f;

        [Tooltip("방 바닥에서 얼마나 띄울지(방 높이 대비 비율). 0이면 바닥에 딱 붙는다.")]
        [Range(-0.2f, 0.3f)]
        [SerializeField] private float floorOffset = 0.02f;

        [Tooltip("방 앞면에서 안쪽으로 얼마나 들어가 설지(방 깊이 대비 비율). " +
                 "0이면 앞면에 붙어 벽을 뚫고 나온 것처럼 보인다.")]
        [Range(0f, 1f)]
        [SerializeField] private float depthFraction = 0.3f;

        [Header("Spine")]
        [SerializeField] private string idleAnimation = "1.idle";

        [Tooltip("Spine 이 없는 캐릭터를 세울 때 쓸 대체 스켈레톤. 비워두면 아이콘으로 물러선다.")]
        [SerializeField] private SkeletonDataAsset placeholderSpine;

        [Header("프레임 색 물러서기")]
        [Tooltip("아이콘도 Spine 도 없을 때 세울 사각형. 비워두면 흰 사각형이 된다.")]
        [SerializeField] private Sprite blankSprite;

        [SerializeField] private PanelFrameSet frameSet;

        private class Slot
        {
            public GameObject root;
            public SpriteRenderer sprite;
            public SkeletonAnimation spine;
            public PanelType shown;
            public bool shownAsSpine;

            /// <summary>
            /// <b>배율 1일 때의 키</b>. 한 번 재두면 같은 캐릭터인 동안 다시 안 잰다 -
            /// 다시 재려면 한 프레임 숨겼다 꺼내야 해서, 방을 새로 그릴 때마다 그러면 낭비다.
            /// 캐릭터가 바뀌면 0으로 되돌린다.
            /// </summary>
            public float measuredHeight;
        }

        private readonly List<Slot> slots = new List<Slot>();

        private void Start()
        {
            SeedIfEmpty();
            Refresh();
        }

        /// <summary>
        /// 저장이 없어서 늘 빈 방으로 시작한다 - 그림을 확인할 수 있게 보유 캐릭터를 앞에서부터
        /// 넣어준다. <b>이미 사는 방은 건드리지 않는다.</b>
        /// </summary>
        private void SeedIfEmpty()
        {
            if (!seedEmptyRooms || seedRoster == null || rooms == null)
                return;

            // 이미 한 번 채웠거나, 누군가 살고 있으면 손대지 않는다.
            if (seedApplied || ApartmentResidents.HasAny)
            {
                seedApplied = true;
                return;
            }

            seedApplied = true;

            var owned = seedRoster.ownedCharacters;
            if (owned == null)
                return;

            int next = 0;
            for (int room = 0; room < rooms.Count && next < owned.Count; room++)
            {
                if (ApartmentResidents.Get(room) != null)
                    continue;

                while (next < owned.Count
                       && (owned[next] == null || ApartmentResidents.IsHoused(owned[next])))
                    next++;

                if (next < owned.Count)
                    ApartmentResidents.MoveIn(room, owned[next]);
            }
        }

        /// <summary>방 전부를 다시 그린다.</summary>
        public void Refresh()
        {
            if (rooms == null)
                return;

            for (int i = 0; i < rooms.Count; i++)
                Refresh(i);
        }

        /// <summary>
        /// ⭐ 담보로 잡히거나 풀리면 <b>그 자리에서</b> 다시 그린다(2026-09-03 사용자 지시:
        /// "감옥에 가는 즉시 아파트에서 사라지게"). 상점이 아파트와 같은 씬이라, 알림을 안 받으면
        /// 다른 일로 다시 그려질 때까지 캐릭터가 남아 있다.
        /// </summary>
        private void OnEnable() => JojoPuzzle.App.BankLoan.OnChanged += Refresh;

        private void OnDisable() => JojoPuzzle.App.BankLoan.OnChanged -= Refresh;

        /// <summary>방 하나를 다시 그린다. 입주가 바뀐 뒤에 부른다.</summary>
        public void Refresh(int roomIndex) => Show(roomIndex, Visible(ApartmentResidents.Get(roomIndex)));

        /// <summary>
        /// ⭐ <b>담보로 잡힌 캐릭터는 아파트에서 사라진다</b>(2026-09-02 사용자 기획) -
        /// 은행 감옥에서만 볼 수 있다. 입주 정보는 그대로 두고 <b>그리지만 않는다</b> -
        /// 방을 비워 버리면 되찾은 뒤에 어디 살았는지가 사라진다.
        /// </summary>
        private static PanelType Visible(PanelType character)
            => JojoPuzzle.App.BankLoan.IsLocked(character) ? null : character;

        /// <summary>
        /// <b>아직 정하기 전의 캐릭터</b>를 방에 미리 세워 본다(2026-08-30 사용자 지시 -
        /// 목록에서 누굴 눌렀을 때 방에 바로 보여야 고른 줄 안다).
        ///
        /// <b>입주 정보는 건드리지 않는다</b> - 그러니 취소하고 나가면
        /// <see cref="Refresh()"/> 한 번으로 원래대로 돌아온다.
        /// </summary>
        public void Preview(int roomIndex, PanelType character) => Show(roomIndex, character);

        private void Show(int roomIndex, PanelType character)
        {
            if (rooms == null)
                return;

            EnsureSlots(rooms.Count);
            if (roomIndex < 0 || roomIndex >= slots.Count)
                return;

            var slot = slots[roomIndex];

            if (character == null)
            {
                slot.root.SetActive(false);
                slot.shown = null;
                return;
            }

            slot.root.SetActive(true);

            // 같은 캐릭터면 다시 만들지 않는다 - 스켈레톤을 갈아끼우면 메시가 다시 만들어진다
            // (SpineCharacterView 와 같은 방침).
            if (slot.shown != character)
            {
                slot.shown = character;
                Apply(slot, character);
            }

            if (!rooms.TryGetRoomBounds(roomIndex, out var room)
                || !rooms.TryGetBuildingBounds(roomIndex, out var building))
                return;

            if (slot.shownAsSpine)
            {
                // <b>⚠ Spine 은 다음 프레임에 재야 한다</b>(2026-08-28 사용자 신고: Spine 캐릭터가
                // 아예 안 보였다). 방금 만든 SkeletonAnimation 은 <b>아직 메시가 없어서</b>
                // 렌더러 bounds 가 비어 있고, 스켈레톤 데이터의 Height 도 아틀라스·리깅에 따라
                // 0이거나 엉뚱한 단위일 수 있다. 그대로 배율을 잡으면 캐릭터가 먼지만 해지거나
                // 화면 밖으로 나갈 만큼 커진다.
                // 이미 재둔 캐릭터면 기다릴 것 없이 그대로 놓는다 - 옮길 때마다 한 프레임씩
                // 숨겼다 꺼내면 그것도 눈에 띈다.
                if (slot.measuredHeight > 0.0001f)
                    Place(slot, room, building);
                else
                    StartCoroutine(PlaceNextFrame(slot, room, building));

                return;
            }

            Place(slot, room, building);
        }

        /// <summary>그 캐릭터를 무엇으로 그릴지 정하고 세운다.</summary>
        private void Apply(Slot slot, PanelType character)
        {
            // 다른 캐릭터가 서면 키도 다르다 - 기억해둔 값을 버려야 다시 잰다.
            slot.measuredHeight = 0f;

            var spine = character.speech != null ? character.speech.spine : null;
            if (spine == null)
                spine = placeholderSpine;

            slot.shownAsSpine = spine != null;

            if (slot.shownAsSpine)
            {
                slot.sprite.enabled = false;
                ShowSpine(slot, spine);
                return;
            }

            if (slot.spine != null)
                slot.spine.gameObject.SetActive(false);

            slot.sprite.enabled = true;

            bool hasIcon = character.icon != null;
            slot.sprite.sprite = hasIcon ? character.icon : blankSprite;

            // 그림이 없으면 <b>프레임 색으로라도</b> 칠한다 - PuzzlePieceIcon 과 같은 물러서기.
            slot.sprite.color = hasIcon
                ? Color.white
                : (frameSet != null ? frameSet.GetColor(character.frameColor) : Color.white);
        }

        private void ShowSpine(Slot slot, SkeletonDataAsset data)
        {
            if (slot.spine == null)
            {
                var go = new GameObject("Spine");
                go.transform.SetParent(slot.root.transform, false);

                var components = SkeletonAnimation.AddToGameObject(go, data);
                slot.spine = components.skeletonAnimation;
            }
            else if (slot.spine.skeletonDataAsset != data)
            {
                slot.spine.skeletonDataAsset = data;

                // <b>Initialize(true) 를 반드시 부른다</b> - 안 부르면 데이터만 바뀌고 화면은
                // 이전 캐릭터 그대로 남는다(spine-unity 에서 흔히 겪는 함정).
                slot.spine.Initialize(true);
            }

            slot.spine.gameObject.SetActive(true);

            // 없는 동작은 그 캐릭터의 idle 로 메운다(규칙은 SpinePlayback 한 곳에 있다).
            SpinePlayback.Play(slot.spine.AnimationState, slot.spine.Skeleton?.Data,
                               idleAnimation, true);
        }

        /// <summary>
        /// 방 안에 자리를 잡는다. <b>크기는 방 높이에서 잰다</b> - 임포트 배율이 바뀌어도
        /// 캐릭터가 방에 맞는다(카메라가 크기를 맞추는 방식과 같다).
        /// </summary>
        private void Place(Slot slot, Bounds room, Bounds model)
        {
            float target = room.size.y * heightFraction;

            // 앞면에서 안쪽으로 조금 들어간 자리. 앞면(+Z)이 카메라 쪽이다.
            float z = model.max.z - model.size.z * depthFraction;

            float floorY = room.min.y + room.size.y * floorOffset;
            slot.root.transform.position = new Vector3(room.center.x, floorY, z);

            float raw = MeasureHeight(slot);
            float scale = raw > 0.0001f ? target / raw : 1f;
            slot.root.transform.localScale = Vector3.one * scale;

            AlignFeet(slot, floorY);
        }

        /// <summary>
        /// <b>발끝을 바닥에 맞춘다</b>(2026-08-28 사용자 지시).
        ///
        /// ⭐⭐ <b>Spine 은 원점이 곧 발밑이다</b>(2026-09-02 사용자 확인: "스파인에서는 똑바로
        /// 바닥에 닿도록 작업했음"). 그러니 재지 말고 원점을 바닥에 두면 된다.
        ///
        /// ⚠ 예전에는 <b>그려진 아래끝</b>을 재서 올렸는데, 그러면 원점 아래로 삐져나온 그림이
        /// 있는 캐릭터가 그만큼 <b>떠 보인다</b> - 카우펜스는 경계 상자가 원점 아래로 172.8
        /// (키의 12%)까지 내려와 있어 혼자 공중에 떠 있었다(다른 캐릭터는 2% 안쪽이라 안 보였다).
        ///
        /// <b>스프라이트는 여전히 잰다</b> - 대개 가운데가 원점이라 그냥 놓으면 절반이 바닥 아래로 들어간다.
        /// </summary>
        private void AlignFeet(Slot slot, float floorY)
        {
            if (slot.shownAsSpine && slot.spine != null)
            {
                var p = slot.root.transform.position;
                slot.root.transform.position = new Vector3(p.x, floorY, p.z);
                return;
            }

            var renderer = ActiveRenderer(slot);
            if (renderer == null)
                return;

            float delta = floorY - renderer.bounds.min.y;
            slot.root.transform.position += new Vector3(0f, delta, 0f);
        }

        private Renderer ActiveRenderer(Slot slot)
        {
            if (slot.shownAsSpine && slot.spine != null)
                return slot.spine.GetComponent<Renderer>();

            return slot.sprite;
        }

        /// <summary>그 방의 캐릭터가 서 있는 오브젝트. 드래그로 들어 올릴 때 쓴다.</summary>
        public Transform GetSlot(int roomIndex)
            => roomIndex >= 0 && roomIndex < slots.Count && slots[roomIndex].root.activeSelf
                ? slots[roomIndex].root.transform
                : null;

        /// <summary>
        /// 한 프레임 기다렸다가 <b>실제로 그려진 크기</b>로 자리를 잡는다.
        /// 배율을 1로 되돌려 재야 "배율 1일 때의 높이"가 나온다.
        ///
        /// <b>⚠ 재는 동안은 안 보이게 한다</b>(2026-08-30 사용자 신고: 캐릭터를 옮길 때 가끔
        /// 화면을 뒤덮는 커다란 모습이 한 순간 스쳤다). 배율 1의 Spine 은 방보다 수십 배 커서,
        /// 그 한 프레임이 그대로 화면에 찍힌다.
        ///
        /// <b>오브젝트를 끄면 안 된다</b> - 꺼두면 SkeletonAnimation 이 메시를 안 만들어서
        /// 애초에 잴 게 없어진다. 그리기만 끄면 메시는 그대로 만들어진다.
        /// </summary>
        private System.Collections.IEnumerator PlaceNextFrame(Slot slot, Bounds room, Bounds building)
        {
            slot.root.transform.localScale = Vector3.one;

            // <b>⚠ 렌더러를 끄는 방식으로 숨기면 안 된다</b>(2026-08-30에 그렇게 했다가 캐릭터가
            // 아예 안 보였다). spine-unity 는 <c>MeshRenderer</c> 가 꺼져 있으면 <b>메시를 아예
            // 안 만든다</b>(SkeletonRenderer.NeedsToGenerateMesh) - 그러면 잴 게 없어서 크기가
            // 엉뚱해지고, 결국 먼지만 하게 그려진다.
            //
            // 대신 <b>카메라 밖으로 잠깐 치워둔다</b>. 메시는 그대로 만들어지고, 크기를 재는 데
            // 쓰는 <c>bounds.size</c> 는 어디에 있든 같다.
            var home = slot.root.transform.position;
            slot.root.transform.position = home + Vector3.up * HideDistance(building);

            // 기다리는 사이에 다른 캐릭터가 들어오면 이 재기는 무효다 - 그쪽이 자기 재기를
            // 새로 시작했을 테니, 여기서 또 놓으면 옛 크기로 덮어쓴다.
            var measuring = slot.shown;

            yield return null;

            if (slot.root == null)
                yield break;

            if (slot.root.activeSelf && slot.shown == measuring)
                Place(slot, room, building);   // Place 가 제자리를 다시 잡는다
            else
                slot.root.transform.position = home;
        }

        /// <summary>
        /// 재는 동안 치워둘 거리. <b>화면 밖이기만 하면 된다</b> - 동 높이에서 재므로
        /// 임포트 배율이 바뀌어도 따라온다(아파트 함정 ②).
        /// </summary>
        private static float HideDistance(Bounds building)
            => Mathf.Max(1f, building.size.y) * 50f;

        /// <summary>지금 그린 것의 <b>배율 1일 때</b> 높이(월드 유닛).</summary>
        private float MeasureHeight(Slot slot)
        {
            // 한 번 잰 값을 그대로 쓴다. 다시 재면 <b>그때의 동작 자세</b>가 잡혀서 값이 조금씩
            // 달라지고, 방을 다시 그릴 때마다 캐릭터가 미세하게 커졌다 작아졌다 한다.
            if (slot.measuredHeight > 0.0001f)
                return slot.measuredHeight;

            if (slot.shownAsSpine && slot.spine != null)
            {
                // <b>렌더러가 그린 실제 크기를 먼저 믿는다</b> - 스켈레톤 데이터의 Height 는
                // 리깅에 따라 0이거나 setup 자세 기준이라 화면에 보이는 것과 다를 수 있다.
                var renderer = ActiveRenderer(slot);
                if (renderer != null && renderer.bounds.size.y > 0.0001f)
                {
                    // ⭐ <b>원점 위쪽만 잰다</b>(2026-09-02). 원점 아래로 삐져나온 그림까지 키에
                    // 넣으면 그 캐릭터만 작아진다 - 카우펜스는 원점 아래로 키의 12%가 나와 있다.
                    float above = renderer.bounds.max.y - slot.root.transform.position.y;
                    float raw = above > 0.0001f ? above : renderer.bounds.size.y;

                    slot.measuredHeight =
                        raw / Mathf.Max(0.0001f, slot.root.transform.localScale.y);
                    return slot.measuredHeight;
                }

                var data = slot.spine.Skeleton?.Data;
                if (data != null && data.Height > 0.0001f)
                    return data.Height;

                Debug.LogWarning($"[ApartmentRoomView] Spine 크기를 못 쟀습니다: {slot.shown?.name}", this);
            }

            if (slot.sprite != null && slot.sprite.sprite != null)
                return slot.sprite.sprite.bounds.size.y;

            return 1f;
        }

        private void EnsureSlots(int count)
        {
            while (slots.Count < count)
            {
                var go = new GameObject($"Room{slots.Count}Resident");
                go.transform.SetParent(transform, false);
                go.SetActive(false);

                var sprite = go.AddComponent<SpriteRenderer>();
                sprite.enabled = false;

                slots.Add(new Slot { root = go, sprite = sprite });
            }
        }
    }
}

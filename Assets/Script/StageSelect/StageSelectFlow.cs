using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.Formation;

namespace JojoPuzzle.StageSelect
{
    /// <summary>
    /// 스테이지 선택 씬의 흐름: <b>챕터 목록 → 스테이지 목록 → 준비 화면</b>.
    ///
    /// 셋을 별개 씬으로 나누지 않고 한 씬의 패널로 둔 이유는, 앞뒤로 오가는 게 잦은데 씬을
    /// 갈아끼우면 그때마다 로딩이 보이기 때문이다. 대신 <b>어디로 돌아가야 하는지는 여기 한 곳만</b>
    /// 안다 - 패널들은 "눌렸다"고 알릴 뿐 다음 화면을 모른다.
    ///
    /// <b>특별·이벤트 챕터는 스테이지 목록을 건너뛴다</b>(기획). 그래서 준비 화면에서 뒤로 갈 때
    /// 목록을 거쳐 왔는지 기억해야 한다 - 안 그러면 안 지나온 화면으로 돌아간다.
    /// </summary>
    public class StageSelectFlow : MonoBehaviour
    {
        [Header("데이터")]
        [SerializeField] private ChapterCatalog catalog;

        [Header("패널")]
        [SerializeField] private ChapterListPanel chapterList;
        [SerializeField] private StageListPanel stageList;
        [SerializeField] private StagePrepPanel prep;
        [SerializeField] private FormationPanel formation;

        [Tooltip("스티커북. 아파트의 '편성' 버튼이 <b>여기로</b> 온다(2026-09-03 사용자 기획) - " +
                 "편성 화면은 책 위의 캐릭터를 눌러야 열린다.")]
        [SerializeField] private JojoPuzzle.Formation.StickerBookPanel stickerBook;

        [Tooltip("스티커 붙이기 화면. 책의 여백을 누르면 열린다.")]
        [SerializeField] private JojoPuzzle.Formation.StickerAttachPanel stickerAttach;

        [Header("공통")]
        [Tooltip("챕터 목록에서 뒤로 - 아파트로 돌아간다.")]
        [SerializeField] private Button closeButton;

        /// <summary>준비 화면에 스테이지 목록을 거쳐 왔는지. 뒤로 가기가 어디로 갈지 정한다.</summary>
        private bool cameThroughStageList;

        /// <summary>
        /// 아파트에서 <b>편성만 하러</b> 들어왔는지. 그때는 스테이지를 고른 적이 없으므로
        /// 편성에서 나갈 때 준비 화면이 아니라 아파트로 돌아가야 한다.
        /// </summary>
        private bool formationOnly;

        private void Awake()
        {
            if (chapterList != null)
                chapterList.OnChapterChosen += HandleChapterChosen;

            if (stageList != null)
            {
                stageList.OnStageChosen += HandleStageChosen;
                stageList.OnBack += ShowChapterList;
            }

            if (prep != null)
            {
                prep.OnBack += HandlePrepBack;
                prep.OnFormationRequested += HandleFormationRequested;
            }

            if (formation != null)
            {
                formation.OnBack += LeaveFormation;
                formation.OnConfirmed += LeaveFormation;
            }

            if (stickerBook != null)
            {
                stickerBook.OnFormationRequested += ShowFormation;
                stickerBook.OnAttachRequested += OpenStickerAttach;
                stickerBook.OnBackRequested += LeaveStickerBook;
            }

            if (stickerAttach != null)
            {
                stickerAttach.OnClosed += HandleAttachClosed;
                stickerAttach.OnStickerPicked += BeginPlacingSticker;
            }

            if (closeButton != null)
                closeButton.onClick.AddListener(AppScenes.GoToApartment);
        }

        private void OnDestroy()
        {
            // 씬을 나갈 때 구독을 풀어둔다. 패널이 먼저 파괴되면 남은 구독이 죽은 객체를 부른다.
            if (chapterList != null)
                chapterList.OnChapterChosen -= HandleChapterChosen;

            if (stageList != null)
            {
                stageList.OnStageChosen -= HandleStageChosen;
                stageList.OnBack -= ShowChapterList;
            }

            if (prep != null)
            {
                prep.OnBack -= HandlePrepBack;
                prep.OnFormationRequested -= HandleFormationRequested;
            }

            if (formation != null)
            {
                formation.OnBack -= LeaveFormation;
                formation.OnConfirmed -= LeaveFormation;
            }

            if (stickerBook != null)
            {
                stickerBook.OnFormationRequested -= ShowFormation;
                stickerBook.OnAttachRequested -= OpenStickerAttach;
                stickerBook.OnBackRequested -= LeaveStickerBook;
            }

            if (stickerAttach != null)
            {
                stickerAttach.OnClosed -= HandleAttachClosed;
                stickerAttach.OnStickerPicked -= BeginPlacingSticker;
            }
        }

        private void Start()
        {
            // 아파트의 "편성" 버튼으로 들어왔으면 챕터 목록을 건너뛰고 편성부터 연다.
            // 요청은 한 번 쓰고 지워지므로 그다음부터는 평소대로 챕터 목록이 먼저다.
            if (ScreenRequest.ConsumeOpenFormation())
            {
                formationOnly = true;

                // ⭐ 편성 버튼은 이제 <b>스티커북</b>으로 온다. 편성 화면은 책 위의
                // 캐릭터를 눌러야 열린다(2026-09-03 사용자 기획).
                if (stickerBook != null)
                {
                    ShowStickerBook();
                    return;
                }

                if (formation != null)
                {
                    ShowFormation();
                    return;
                }
            }

            formationOnly = false;

            // 방금 하던 챕터로 돌아가 달라는 요청이 있으면 목록을 건너뛴다.
            // 이것도 한 번 쓰고 지워지므로 그다음부터는 평소대로 챕터 목록이 먼저다.
            var resume = ScreenRequest.ConsumeResumeChapter();
            if (resume != null)
            {
                HandleChapterChosen(resume);
                return;
            }

            ShowChapterList();
        }

        /// <summary>
        /// 스티커북을 펼친다. 다른 화면은 전부 접는다 - 이 화면이 편성으로 들어가는 문이다.
        /// </summary>
        private void ShowStickerBook()
        {
            HideAll();
            stickerAttach?.Close();
            stickerBook?.Open();
        }

        private void OpenStickerAttach() => stickerAttach?.Open();

        /// <summary>붙이기 화면을 닫으면 책이 다시 앞으로 나온다 - 붙인 게 바로 보여야 한다.</summary>
        private void HandleAttachClosed() => stickerBook?.Refresh();

        /// <summary>
        /// 목록에서 하나를 골랐다 - 목록은 이미 아래로 내려갔고, 이제 <b>책에서 자리를 고른다</b>.
        /// </summary>
        private void BeginPlacingSticker(int stickerId) => stickerBook?.BeginPlacing(stickerId);

        /// <summary>
        /// 책에서 뒤로가기. <b>편성 버튼으로 들어온 길</b>이라 아파트로 돌아간다 -
        /// 챕터 목록으로 떨어지면 왜 여기 있는지가 흐려진다.
        /// </summary>
        /// <summary>
        /// 스티커북에서 나간다. <b>들어온 문으로 돌아간다</b> - 아파트의 편성 버튼으로 왔으면
        /// 아파트로, 준비 화면의 편성 버튼으로 왔으면 준비 화면으로.
        /// 한쪽으로만 보내면 스테이지를 고르던 사람이 아파트까지 튕겨 나간다.
        /// </summary>
        /// <summary>
        /// 스티커북에서 나간다 - <b>어디서 왔든 메인화면으로</b>(2026-09-03 사용자 지시).
        ///
        /// 예전엔 들어온 문으로 되돌렸는데, 스티커북은 스테이지 고르기와 상관없는 곳이라
        /// 준비 화면으로 되돌아가면 오히려 흐름이 끊긴다. 한 번에 나가는 게 읽힌다.
        /// </summary>
        private void LeaveStickerBook() => JojoPuzzle.App.AppScenes.GoToApartment();

        private void ShowChapterList()
        {
            HideAll();
            cameThroughStageList = false;

            if (chapterList != null)
                chapterList.Show(catalog);
        }

        private void HandleChapterChosen(ChapterDefinition chapter)
        {
            if (chapter == null)
                return;

            if (chapter.GoesStraightToPrep)
            {
                // 스테이지가 하나뿐이거나 특별·이벤트 챕터. 목록을 건너뛴다.
                var only = chapter.stages != null && chapter.stages.Length > 0 ? chapter.stages[0] : null;
                if (only == null)
                {
                    Debug.LogWarning($"[StageSelectFlow] '{chapter.displayName}' 에 스테이지가 없습니다.", chapter);
                    return;
                }

                cameThroughStageList = false;
                OpenPrep(chapter, only);
                return;
            }

            HideAll();
            cameThroughStageList = true;

            if (stageList != null)
                stageList.Show(chapter);
        }

        private void HandleStageChosen(ChapterDefinition chapter, StageDefinition stage)
        {
            OpenPrep(chapter, stage);
        }

        private void OpenPrep(ChapterDefinition chapter, StageDefinition stage)
        {
            // 고른 것을 기록하는 건 여기 한 곳. 준비 화면은 StageEntry 를 읽기만 한다.
            StageEntry.Select(chapter, stage);

            HideAll();

            if (prep != null)
                prep.Show();
        }

        private void HandlePrepBack()
        {
            if (cameThroughStageList && stageList != null && StageEntry.Chapter != null)
            {
                HideAll();
                stageList.Show(StageEntry.Chapter);
                return;
            }

            ShowChapterList();
        }

        private void HandleFormationRequested()
        {
            if (formation == null)
            {
                if (prep != null)
                    prep.Notify("편성 - 준비 중입니다");

                return;
            }

            formationOnly = false;

            // ⭐ 준비 화면의 편성 버튼도 <b>스티커북</b>으로 간다(2026-09-03 사용자 지시).
            // 아파트의 편성 버튼과 같은 문이어야 한다 - 같은 이름의 버튼이 서로 다른 곳으로
            // 가면 플레이어가 어디로 갈지 예측할 수 없다. 편성 화면은 책 위의 캐릭터를 눌러 연다.
            if (stickerBook != null)
            {
                ShowStickerBook();
                return;
            }

            ShowFormation();
        }

        private void ShowFormation()
        {
            HideAll();
            formation.Show();
        }

        /// <summary>
        /// 편성에서 나간다. 뒤로가기와 확정이 <b>같은 곳으로</b> 간다 - 확정은 편성을 저장하고
        /// 나가는 것일 뿐 다음 화면이 따로 있지 않다. 저장은 FormationPanel 이 이미 했다.
        /// </summary>
        private void LeaveFormation()
        {
            if (formationOnly)
            {
                // ⭐ 들어온 문이 스티커북이므로 거기로 돌아간다(2026-09-03).
                // 곧장 아파트로 나가면 방금 고친 편성이 책에 어떻게 앉았는지를 못 본다.
                if (stickerBook != null)
                {
                    // ⭐ 방금 고른 편성을 <b>지금 권에</b> 담는다 - 권마다 캐릭터가 따로다.
                    HideAll();
                    stickerAttach?.Close();
                    stickerBook.OpenAfterFormation();
                    return;
                }

                AppScenes.GoToApartment();
                return;
            }

            HideAll();

            if (prep != null)
                prep.Show();
        }

        private void HideAll()
        {
            stickerBook?.Close();
            stickerAttach?.Close();

            if (chapterList != null) chapterList.Hide();
            if (stageList != null) stageList.Hide();
            if (prep != null) prep.Hide();
            if (formation != null) formation.Hide();
        }
    }
}

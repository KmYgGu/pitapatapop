using System;
using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 방에서 같이 할 수 있는 도박 종류. <b>항목을 뒤에 붙인다</b> - 값이 씬에 숫자로 저장된다.
    /// </summary>
    public enum MiniGameKind
    {
        /// <summary>인디언 포커 - 자기 패는 안 보이고 상대 패만 보인다.</summary>
        IndianPoker = 0,

        /// <summary>블랙잭 - 캐릭터가 딜러. 21에 가까운 쪽이 이긴다.</summary>
        Blackjack = 1,

        /// <summary>도둑잡기 - 조커를 든 쪽이 밀고, 상대가 둘 중 하나를 집는다.</summary>
        OldMaid = 2
    }

    /// <summary>
    /// 미니게임 씬에 들어오면 먼저 뜨는 <b>무엇을 하고 놀지 고르는 화면</b>(2026-09-02 사용자 기획).
    ///
    /// 예전에는 들어오자마자 인디언 포커가 시작됐다. 도박을 여러 개 만들 계획이라
    /// <b>고르는 자리를 먼저 만들어두고</b> 하나씩 채운다.
    ///
    /// <b>아직 안 만든 것도 목록에 둔다</b>(<see cref="Entry.ready"/> 를 끄면 눌리지 않고 곁말이 뜬다) -
    /// 앞으로 뭐가 생기는지 보이는 편이 낫고, 만들면 켜기만 하면 된다.
    /// </summary>
    public class MiniGameMenu : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("이 줄이 어떤 게임인지. 고르면 이 값이 넘어간다.")]
            public MiniGameKind kind;

            public Button button;

            [Tooltip("게임 이름.")]
            public Text label;

            [Tooltip("오른쪽에 붙는 곁말. 아직 안 만든 것은 여기에 '준비 중'이 뜬다.")]
            public Text note;

            [Tooltip("만들어졌는지. 꺼두면 눌리지 않는다.")]
            public bool ready;
        }

        [Tooltip("껐다 켜는 뿌리. 이 컴포넌트는 <b>항상 켜져 있는</b> 바깥에 붙는다.")]
        [SerializeField] private GameObject root;

        [SerializeField] private Text titleText;

        [Tooltip("아직 안 만든 줄에 띄울 곁말.")]
        [SerializeField] private string notReadyNote = "준비 중";

        [SerializeField] private Entry[] entries;

        /// <summary>고른 게임.</summary>
        public event Action<MiniGameKind> OnPicked;

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake()
        {
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry == null || entry.button == null)
                        continue;

                    var captured = entry;
                    entry.button.onClick.AddListener(() => Pick(captured));
                    entry.button.interactable = entry.ready;

                    if (entry.note != null)
                        entry.note.text = entry.ready ? string.Empty : notReadyNote;
                }
            }

            Close();
        }

        public void Open(string title)
        {
            if (titleText != null)
                titleText.text = title;

            if (root != null)
                root.SetActive(true);
        }

        public void Close()
        {
            if (root != null)
                root.SetActive(false);
        }

        private void Pick(Entry entry)
        {
            // 안 만든 게임은 눌러도 아무 일이 없다(버튼도 꺼져 있지만 이중으로 막는다).
            if (entry == null || !entry.ready)
                return;

            OnPicked?.Invoke(entry.kind);
        }
    }
}

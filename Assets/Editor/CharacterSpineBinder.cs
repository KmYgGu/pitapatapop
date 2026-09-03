using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using JojoPuzzle.Core;
using JojoPuzzle.UI;

namespace JojoPuzzle.EditorTools
{
    /// <summary>
    /// 캐릭터에게 Spine 스켈레톤을 물려주는 창(2026-08-30 사용자 요청 - "매번 부탁하지 않고
    /// 메뉴에서 원버튼으로 적용하고 싶다").
    ///
    /// <b>무엇을 하는가</b>: 캐릭터마다 아래를 한 번에 처리한다.
    /// <list type="number">
    ///   <item><see cref="CharacterSpeechSet"/> 이 없으면 <c>Assets/TextSet</c> 에 만들고
    ///         <see cref="PanelType.speech"/> 에 물린다.</item>
    ///   <item>그 대사 애셋의 <c>spine</c> 에 스켈레톤을 넣는다.</item>
    ///   <item><c>talkAnimation</c> 이 비어 있으면 <c>1.idle</c> 로 채운다.</item>
    /// </list>
    ///
    /// <b>짝은 알아서 찾되, 고친 건 기억한다</b>: 처음엔 이름과 번호로 짐작하고(아래
    /// <see cref="Guess"/>), 한 번 적용하고 나면 그 값이 대사 애셋에 남아 다음부터는 그게 그대로
    /// 뜬다. 그래서 <b>짐작이 틀리는 캐릭터도 손으로 고르는 건 딱 한 번</b>이다.
    ///
    /// <b>동작이 모자라도 괜찮다</b>: 없는 동작은 실행 중에 그 캐릭터의 <c>1.idle</c> 로 메운다
    /// (<see cref="SpinePlayback"/>). 나중에 동작을 만들어 다시 넣으면 그날부터 그게 나온다 -
    /// 코드도 이 창도 다시 건드릴 필요가 없다. 아래 표의 '가진 동작'이 지금 상태를 보여준다.
    /// </summary>
    public class CharacterSpineBinder : EditorWindow
    {
        private const string SpineRoot = "Assets/SpineChar";
        private const string SpeechFolder = "Assets/TextSet";

        /// <summary>표에서 있는지 없는지 보여줄 동작들. 연출이 실제로 부르는 것과 같아야 한다.</summary>
        private static readonly string[] WatchedAnimations =
        {
            SpinePlayback.Idle,
            SpinePlayback.Win,
            SpinePlayback.ReadyAttack,
            SpinePlayback.AttackDone,
        };

        private class Row
        {
            public PanelType character;
            public CharacterSpeechSet speech;      // 없을 수 있다 - 적용할 때 만든다
            public SkeletonDataAsset current;      // 지금 물려 있는 것
            public SkeletonDataAsset picked;       // 적용할 것
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly List<SkeletonDataAsset> skeletons = new List<SkeletonDataAsset>();
        private readonly Dictionary<SkeletonDataAsset, string> animationSummary =
            new Dictionary<SkeletonDataAsset, string>();

        private Vector2 scroll;

        [MenuItem("JojoPuzzle/Spine/캐릭터 스파인 연결")]
        public static void Open()
        {
            var window = GetWindow<CharacterSpineBinder>(true, "캐릭터 스파인 연결");
            window.minSize = new Vector2(720f, 320f);
            window.Refresh();
        }

        private void OnEnable() => Refresh();

        // ------------------------------------------------------------------ 모으기

        private void Refresh()
        {
            rows.Clear();
            skeletons.Clear();
            animationSummary.Clear();

            foreach (string guid in AssetDatabase.FindAssets("t:SkeletonDataAsset", new[] { SpineRoot }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (asset != null)
                    skeletons.Add(asset);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:PanelType"))
            {
                var character = AssetDatabase.LoadAssetAtPath<PanelType>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (character == null)
                    continue;

                var speech = character.speech;
                var current = speech != null ? speech.spine : null;

                rows.Add(new Row
                {
                    character = character,
                    speech = speech,
                    current = current,
                    picked = current != null ? current : Guess(character, speech),
                });
            }

            rows.Sort((a, b) => EditorUtility.NaturalCompare(a.character.name, b.character.name));
        }

        /// <summary>
        /// 아직 안 물린 캐릭터의 짝을 짐작한다. 못 찾으면 null - 그때는 사람이 고르면 되고,
        /// 한 번 고르면 대사 애셋에 남아 다음부터는 짐작할 일이 없다.
        ///
        /// 두 가지를 본다:
        /// <list type="number">
        ///   <item><b>이름</b> - 스켈레톤 애셋 이름에 캐릭터 이름이 들어 있는가
        ///         (<c>카우펜스_SkeletonData</c>, <c>…피타팝라뷰린스2</c>).</item>
        ///   <item><b>앞의 번호</b> - 대사 애셋 <c>2.Mystic</c> 과 폴더 <c>2.myistc</c> 처럼
        ///         이 프로젝트는 같은 번호를 붙여 왔다. 이름이 전혀 안 닮은 짝을 이걸로 잇는다.</item>
        /// </list>
        /// </summary>
        private SkeletonDataAsset Guess(PanelType character, CharacterSpeechSet speech)
        {
            string name = character.name;

            foreach (var skeleton in skeletons)
            {
                if (skeleton.name.Contains(name))
                    return skeleton;
            }

            string number = LeadingNumber(speech != null ? speech.name : null);
            if (number == null)
                return null;

            foreach (var skeleton in skeletons)
            {
                string folder = Path.GetFileName(Path.GetDirectoryName(
                    AssetDatabase.GetAssetPath(skeleton)));

                if (LeadingNumber(folder) == number)
                    return skeleton;
            }

            return null;
        }

        /// <summary>"2.Mystic" -> "2". 번호로 시작하지 않으면 null.</summary>
        private static string LeadingNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var match = Regex.Match(text, @"^(\d+)\.");
            return match.Success ? match.Groups[1].Value : null;
        }

        // ------------------------------------------------------------------ 그리기

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "캐릭터에 Spine 스켈레톤을 물립니다. 대사 애셋(CharacterSpeechSet)이 없으면 만들어 물려줍니다.\n" +
                "없는 동작은 실행 중에 그 캐릭터의 1.idle 로 대신 나옵니다 - 나중에 동작을 만들어 " +
                "다시 넣으면 그날부터 그게 나옵니다.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("다시 훑기", GUILayout.Width(90f)))
                    Refresh();

                GUILayout.FlexibleSpace();
                GUILayout.Label($"캐릭터 {rows.Count}명 / 스켈레톤 {skeletons.Count}개",
                                EditorStyles.miniLabel);
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("캐릭터", EditorStyles.miniBoldLabel, GUILayout.Width(120f));
                GUILayout.Label("붙일 스켈레톤", EditorStyles.miniBoldLabel, GUILayout.Width(230f));
                GUILayout.Label("가진 동작", EditorStyles.miniBoldLabel);
            }

            using (var view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;

                foreach (var row in rows)
                    DrawRow(row);
            }

            EditorGUILayout.Space();

            int ready = 0;
            foreach (var row in rows)
            {
                if (row.picked != null && row.picked != row.current)
                    ready++;
            }

            using (new EditorGUI.DisabledScope(ready == 0))
            {
                if (GUILayout.Button($"모두 적용 ({ready}명 바뀜)", GUILayout.Height(30f)))
                    ApplyAll();
            }
        }

        private void DrawRow(Row row)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(row.character, typeof(PanelType), false,
                                            GUILayout.Width(120f));

                var picked = (SkeletonDataAsset)EditorGUILayout.ObjectField(
                    row.picked, typeof(SkeletonDataAsset), false, GUILayout.Width(230f));

                if (picked != row.picked)
                    row.picked = picked;

                string note;
                if (row.picked == null)
                    note = "— 스켈레톤을 고르세요";
                else if (row.picked == row.current)
                    note = "이미 물려 있음 · " + Summarize(row.picked);
                else
                    note = Summarize(row.picked);

                GUILayout.Label(note, EditorStyles.miniLabel);
            }
        }

        /// <summary>"1.idle ○ / 2.win × / … (동작 3개)" 처럼 한 줄로 요약한다.</summary>
        private string Summarize(SkeletonDataAsset skeleton)
        {
            if (animationSummary.TryGetValue(skeleton, out string cached))
                return cached;

            var data = skeleton.GetSkeletonData(true);
            if (data == null)
                return "(스켈레톤을 읽지 못했습니다)";

            var builder = new StringBuilder();
            foreach (string name in WatchedAnimations)
            {
                if (builder.Length > 0)
                    builder.Append("  ");

                builder.Append(data.FindAnimation(name) != null ? "○ " : "× ").Append(name);
            }

            builder.Append($"   (전체 {data.Animations.Count}개)");

            string summary = builder.ToString();
            animationSummary[skeleton] = summary;
            return summary;
        }

        // ------------------------------------------------------------------ 적용

        private void ApplyAll()
        {
            int changed = 0;
            var report = new StringBuilder();

            foreach (var row in rows)
            {
                if (row.picked == null || row.picked == row.current)
                    continue;

                var speech = row.speech != null ? row.speech : CreateSpeechSet(row);
                if (speech == null)
                    continue;

                Undo.RecordObject(speech, "캐릭터 스파인 연결");
                speech.spine = row.picked;

                // 비어 있을 때만 채운다 - 캐릭터마다 다른 대사 동작을 쓰기로 했다면 지키게.
                if (string.IsNullOrEmpty(speech.talkAnimation))
                    speech.talkAnimation = SpinePlayback.Idle;

                EditorUtility.SetDirty(speech);

                row.current = row.picked;
                row.speech = speech;
                changed++;

                report.AppendLine($"  {row.character.name} ← {row.picked.name}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Refresh();

            Debug.Log(changed == 0
                ? "[CharacterSpineBinder] 바뀐 게 없습니다."
                : $"[CharacterSpineBinder] {changed}명 연결했습니다.\n{report}");
        }

        /// <summary>
        /// 대사 애셋이 없는 캐릭터를 위해 하나 만들어 <see cref="PanelType.speech"/> 에 물린다.
        ///
        /// 이름은 <b>스켈레톤이 든 폴더 이름</b>을 그대로 쓴다(<c>3.cowpens</c>) - 이 프로젝트가
        /// 이미 <c>1.Rabrith</c> / <c>2.Mystic</c> 처럼 번호를 맞춰 왔고, 그 번호가 다음번
        /// 짐작의 열쇠이기 때문이다.
        /// </summary>
        private CharacterSpeechSet CreateSpeechSet(Row row)
        {
            if (!AssetDatabase.IsValidFolder(SpeechFolder))
            {
                Debug.LogError($"[CharacterSpineBinder] {SpeechFolder} 폴더가 없습니다.");
                return null;
            }

            string folder = Path.GetFileName(Path.GetDirectoryName(
                AssetDatabase.GetAssetPath(row.picked)));

            string path = AssetDatabase.GenerateUniqueAssetPath($"{SpeechFolder}/{folder}.asset");

            var speech = CreateInstance<CharacterSpeechSet>();
            AssetDatabase.CreateAsset(speech, path);

            Undo.RecordObject(row.character, "캐릭터 스파인 연결");
            row.character.speech = speech;
            EditorUtility.SetDirty(row.character);

            Debug.Log($"[CharacterSpineBinder] {row.character.name} 의 대사 애셋을 만들었습니다: {path}");
            return speech;
        }
    }
}

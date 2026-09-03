using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 스테이지 선택 화면이 읽는 챕터 목록. <b>목록의 유일한 소스</b>라 화면 코드는 챕터 애셋을
    /// 직접 찾아다니지 않는다(찾아다니면 어떤 챕터가 목록에 나오는지가 폴더 구조에 좌우된다).
    ///
    /// 순서가 곧 화면에 나오는 순서다.
    /// </summary>
    [CreateAssetMenu(fileName = "ChapterCatalog", menuName = "JojoPuzzle/Chapter Catalog")]
    public class ChapterCatalog : ScriptableObject
    {
        public ChapterDefinition[] chapters = new ChapterDefinition[0];
    }
}

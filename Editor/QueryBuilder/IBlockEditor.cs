#if USE_QUERY_BUILDER
using System.Collections.Generic;

namespace UnityEditor.Search
{
    interface IBlockSource
    {
        string name { get; }
        string editorTitle { get; }
        SearchContext context { get; }
        bool formatNames { get; }

        void Apply(in SearchProposition searchProposition);
        IEnumerable<SearchProposition> FetchPropositions();

        void CloseEditor();
    }

    interface IBlockEditor
    {
        EditorWindow window { get; }
    }
}
#endif

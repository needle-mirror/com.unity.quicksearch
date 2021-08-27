#if USE_QUERY_BUILDER
namespace UnityEditor.Search
{
    interface IQuerySource
    {
        ISearchView searchView { get; }
        SearchContext context { get; }
        void AddBlock(string text);
        void AddBlock(QueryBlock block);
        void RemoveBlock(in QueryBlock block);
        void Apply();
        void Repaint();
    }
}
#endif

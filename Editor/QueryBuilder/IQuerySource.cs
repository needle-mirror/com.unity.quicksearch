#if USE_QUERY_BUILDER
namespace UnityEditor.Search
{
    interface IQuerySource
    {
        ISearchView searchView { get; }
        SearchContext context { get; }
        QueryBlock AddBlock(string text);
        QueryBlock AddBlock(QueryBlock block);
        QueryBlock AddProposition(in SearchProposition searchProposition);
        void RemoveBlock(in QueryBlock block);
        void BlockActivated(in QueryBlock block);
        void Apply();
        void Repaint();
    }
}
#endif

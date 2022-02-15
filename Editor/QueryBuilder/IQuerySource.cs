#if USE_QUERY_BUILDER
namespace UnityEditor.Search
{
    public interface IQuerySource
    {
        ISearchView searchView { get; }
        SearchContext context { get; }
        internal QueryBlock AddBlock(string text);
        internal QueryBlock AddBlock(QueryBlock block);
        internal QueryBlock AddProposition(in SearchProposition searchProposition);
        internal void RemoveBlock(in QueryBlock block);
        internal void BlockActivated(in QueryBlock block);
        void Apply();
        void Repaint();
    }
}
#endif

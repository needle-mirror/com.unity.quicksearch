using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Unity.QuickSearch
{
    public class SearchSelection : IReadOnlyCollection<SearchItem>
    {
        private ISearchList m_List;
        private IList<int> m_Selection;
        private List<SearchItem> m_Items;

        public SearchSelection(IList<int> selection, ISearchList filteredItems)
        {
            m_Selection = selection;
            m_List = filteredItems;
        }

        public int Count => m_Selection.Count;

        public int MinIndex()
        {
            return m_Selection.Min();
        }

        public int MaxIndex()
        {
            return m_Selection.Max();
        }

        public SearchItem First()
        {
            if (m_Selection.Count == 0)
                return null;
            if (m_Items == null)
                BuildSelection();
            return m_Items[0];
        }

        public SearchItem Last()
        {
            if (m_Selection.Count == 0)
                return null;
            if (m_Items == null)
                BuildSelection();
            return m_Items[m_Items.Count - 1];
        }

        public IEnumerator<SearchItem> GetEnumerator()
        {
            if (m_Items == null)
                BuildSelection();
            return m_Items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void BuildSelection()
        {
            m_Items = new List<SearchItem>(m_Selection.Count);
            foreach (var s in m_Selection)
                m_Items.Add(m_List.ElementAt(s));
        }
    }
}
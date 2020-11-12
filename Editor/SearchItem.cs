using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace UnityEditor.Search
{
    /// <summary>
    /// Indicates how the search item description needs to be formatted when presented to the user.
    /// </summary>
    [Flags]
    public enum SearchItemOptions
    {
        /// <summary>Use default description.</summary>
        None = 0,
        /// <summary>If the description is too long truncate it and add an ellipsis.</summary>
        Ellipsis = 1 << 0,
        /// <summary>If the description is too long keep the right part.</summary>
        RightToLeft = 1 << 1,
        /// <summary>Highlight parts of the description that match the Search Query</summary>
        Highlight = 1 << 2,
        /// <summary>Highlight parts of the description that match the Fuzzy Search Query</summary>
        FuzzyHighlight = 1 << 3,
        /// <summary>Use Label instead of description for shorter display.</summary>
        Compacted = 1 << 4
    }

    internal enum SearchItemSorting
    {
        Id,
        Label,
        Description,
        Score,
        Complete,

        Default = Id
    }

    /// <summary>
    /// Search items are returned by the search provider when some results need to be shown to the user after a search is made.
    /// The search item holds all the data that will be used to sort and present the search results.
    /// </summary>
    [DebuggerDisplay("{id} | {label} | {score}")]
    public class SearchItem : IEquatable<SearchItem>, IComparable<SearchItem>, IComparable
    {
        private Dictionary<Type, UnityEngine.Object> m_Objects;

        /// <summary>
        /// Unique id of this item among this provider items.
        /// </summary>
        public readonly string id;

        /// <summary>
        /// The item score can affect how the item gets sorted within the same provider.
        /// </summary>
        public int score;

        /// <summary>
        /// Display name of the item
        /// </summary>
        public string label;

        /// <summary>
        /// If no description is provided, SearchProvider.fetchDescription will be called when the item is first displayed.
        /// </summary>
        public string description;

        /// <summary>
        /// Various flags that dictates how the search item is displayed and used.
        /// </summary>
        public SearchItemOptions options;

        /// <summary>
        /// If no thumbnail are provider, SearchProvider.fetchThumbnail will be called when the item is first displayed.
        /// </summary>
        public Texture2D thumbnail;

        /// <summary>
        /// Large preview of the search item. Usually cached by fetchPreview.
        /// </summary>
        public Texture2D preview;

        /// <summary>
        /// Search provider defined content. It can be used to transport any data to custom search provider handlers (i.e. `fetchDescription`).
        /// </summary>
        public object data;

        /// <summary>
        /// Used to map value to a search item
        /// </summary>
        internal object value { get => m_Value ?? id; set => m_Value = value; }
        private object m_Value = null;

        /// <summary>
        /// Back pointer to the provider.
        /// </summary>
        public SearchProvider provider;

        /// <summary>
        /// Context used to create that item.
        /// </summary>
        public SearchContext context;

        private static readonly SearchProvider defaultProvider = new SearchProvider("default", "Default")
        {
            priority = int.MinValue,
            toObject = (item, type) => null,
            fetchLabel = (item, context) => item.label ?? item.id,
            fetchDescription = (item, context) => item.label ?? item.id,
            fetchThumbnail = (item, context) => item.thumbnail ?? Icons.logInfo,
            actions = new List<SearchAction> { new SearchAction("select", "select", null, null, (SearchItem item) => {}) }
        };

        /// <summary>
        /// A search item representing none, usually used to clear the selection.
        /// </summary>
        public static readonly SearchItem none = new SearchItem(Guid.NewGuid().ToString())
        {
            label = "None",
            description = "Clear the current value",
            score = int.MinValue,
            thumbnail = Icons.clear,
            provider = defaultProvider
        };

        /// <summary>
        /// Construct a search item. Minimally a search item need to have a unique id for a given search query.
        /// </summary>
        /// <param name="_id"></param>
        public SearchItem(string _id)
        {
            id = _id;
            provider = defaultProvider;
        }

        /// <summary>
        /// Fetch and format label.
        /// </summary>
        /// <param name="context">Any search context for the item provider.</param>
        /// <param name="stripHTML">True if any HTML tags should be dropped.</param>
        /// <returns>The search item label</returns>
        public string GetLabel(SearchContext context, bool stripHTML = false)
        {
            if (label == null)
                label = provider?.fetchLabel?.Invoke(this, context);
            if (!stripHTML || string.IsNullOrEmpty(label))
                return label;
            return Utils.StripHTML(label);
        }

        /// <summary>
        /// Fetch and format description
        /// </summary>
        /// <param name="context">Any search context for the item provider.</param>
        /// <param name="stripHTML">True if any HTML tags should be dropped.</param>
        /// <returns>The search item description</returns>
        public string GetDescription(SearchContext context, bool stripHTML = false)
        {
            if (options.HasFlag(SearchItemOptions.Compacted) && provider?.fetchDescription != null)
                return provider?.fetchDescription?.Invoke(this, context);
            if (description == null)
                description = provider?.fetchDescription?.Invoke(this, context);
            if (!stripHTML || string.IsNullOrEmpty(description))
                return description;
            return Utils.StripHTML(description);
        }

        /// <summary>
        /// Fetch the item thumbnail.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="cacheThumbnail"></param>
        /// <returns></returns>
        public Texture2D GetThumbnail(SearchContext context, bool cacheThumbnail = false)
        {
            if (cacheThumbnail && thumbnail)
                return thumbnail;
            var tex = provider?.fetchThumbnail?.Invoke(this, context);
            if (cacheThumbnail)
                thumbnail = tex;
            return tex;
        }

        /// <summary>
        /// Fetch the item preview if any.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="size"></param>
        /// <param name="options"></param>
        /// <param name="cacheThumbnail"></param>
        /// <returns></returns>
        public Texture2D GetPreview(SearchContext context, Vector2 size, FetchPreviewOptions options = FetchPreviewOptions.Normal, bool cacheThumbnail = false)
        {
            if (cacheThumbnail && preview)
                return preview;
            var tex = provider?.fetchPreview?.Invoke(this, context, size, options);
            if (cacheThumbnail)
                preview = tex;
            return tex;
        }

        /// <summary>
        /// Check if 2 SearchItems have the same id.
        /// </summary>
        /// <param name="other"></param>
        /// <returns>Returns true if SearchItem have the same id.</returns>
        public int CompareTo(SearchItem other)
        {
            return Comparer.Compare(this, other, SearchItemSorting.Default);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="other"></param>
        /// <param name="sortBy"></param>
        /// <returns></returns>
        internal int CompareTo(SearchItem other, SearchItemSorting sortBy)
        {
            return Comparer.Compare(this, other, sortBy);
        }

        /// <summary>
        /// Generic item compare.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public int CompareTo(object other)
        {
            return Comparer.Compare(this, (SearchItem)other);
        }

        /// <summary>
        /// Check if 2 SearchItems have the same id.
        /// </summary>
        /// <param name="other"></param>
        /// <returns>>Returns true if SearchItem have the same id.</returns>
        public override bool Equals(object other)
        {
            return other is SearchItem l && Equals(l);
        }

        /// <summary>
        /// Default Hash of a SearchItem
        /// </summary>
        /// <returns>A hash code for the current SearchItem</returns>
        public override int GetHashCode()
        {
            return id.GetHashCode();
        }

        /// <summary>
        /// Check if 2 SearchItems have the same id.
        /// </summary>
        /// <param name="other"></param>
        /// <returns>>Returns true if SearchItem have the same id.</returns>
        public bool Equals(SearchItem other)
        {
            return string.Equals(id, other.id, StringComparison.Ordinal);
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            if (label == null)
                return id;
            return $"{id} | {label} | {score}";
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public UnityEngine.Object ToObject()
        {
            return ToObject(typeof(UnityEngine.Object));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public UnityEngine.Object ToObject(Type type)
        {
            if (m_Objects == null)
                m_Objects = new Dictionary<Type, UnityEngine.Object>();
            if (!m_Objects.TryGetValue(type, out var obj))
            {
                obj = provider?.toObject?.Invoke(this, type);
                m_Objects[type] = obj;
            }
            return obj;
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T ToObject<T>() where T : UnityEngine.Object
        {
            return ToObject(typeof(T)) as T;
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public string ToGlobalId()
        {
            return $"{provider?.id ?? "unknown"}:{id}";
        }

        /// <summary>
        /// Insert new search item keeping the list sorted and preventing duplicated.
        /// </summary>
        /// <param name="list"></param>
        /// <param name="item"></param>
        /// <param name="sortBy"></param>
        /// <returns></returns>
        internal static bool Insert(IList<SearchItem> list, SearchItem item, IComparer<SearchItem> comparer)
        {
            var insertAt = BinarySearch(list, item, comparer);
            if (insertAt < 0)
            {
                list.Insert(~insertAt, item);
                return true;
            }

            if (comparer.Compare(item, list[insertAt]) < 0)
                list[insertAt] = item;

            return false;
        }

        internal static bool Insert(IList<SearchItem> list, SearchItem item)
        {
            return Insert(list, item, DefaultComparer);
        }

        private static int BinarySearch(IList<SearchItem> list, SearchItem value, IComparer<SearchItem> comparer = null)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            comparer = comparer ?? DefaultComparer;

            int lower = 0;
            int upper = list.Count - 1;

            while (lower <= upper)
            {
                int middle = lower + (upper - lower) / 2;
                int comparisonResult = comparer.Compare(value, list[middle]);
                if (comparisonResult == 0)
                    return middle;
                else if (comparisonResult < 0)
                    upper = middle - 1;
                else
                    lower = middle + 1;
            }

            return ~lower;
        }

        internal static readonly Comparer DefaultComparer = new Comparer(SearchItemSorting.Id);

        internal readonly struct Comparer : IComparer<SearchItem>
        {
            public readonly SearchItemSorting sortBy;

            public Comparer(SearchItemSorting sortBy)
            {
                this.sortBy = sortBy;
            }

            public int Compare(SearchItem x, SearchItem y)
            {
                return Compare(x, y, sortBy);
            }

            public static int Compare(SearchItem x, SearchItem y, SearchItemSorting sortBy = SearchItemSorting.Default)
            {
                if (sortBy == SearchItemSorting.Id)
                    return string.CompareOrdinal(x.id, y.id);

                if (sortBy == SearchItemSorting.Label)
                    return string.Compare(x.GetLabel(x.context, true), y.GetLabel(y.context, true), StringComparison.InvariantCulture);

                if (sortBy == SearchItemSorting.Description)
                    return string.Compare(x.GetDescription(x.context, true), y.GetDescription(y.context, true), StringComparison.InvariantCulture);

                if (sortBy == SearchItemSorting.Score)
                {
                    int c = string.CompareOrdinal(x.id, y.id);
                    if (c != 0)
                        return c;
                    return x.score.CompareTo(y.score);
                }

                return string.CompareOrdinal(x.id, y.id);
            }
        }
    }
}

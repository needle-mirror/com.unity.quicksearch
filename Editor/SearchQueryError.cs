using System.Collections.Generic;

namespace UnityEditor.Search
{
    /// <summary>
    /// Enum representing the possible types of query errors.
    /// </summary>
    public enum SearchQueryErrorType
    {
        /// <summary>
        /// Represents an error.
        /// </summary>
        Error,

        /// <summary>
        /// Represents a warning.
        /// </summary>
        Warning
    }

    /// <summary>
    /// Class that represents a query parsing error.
    /// </summary>
    public class SearchQueryError
    {
        /// <summary>
        /// Index where the error happened.
        /// </summary>
        public int index { get; }

        /// <summary>
        /// Length of the block that was being parsed.
        /// </summary>
        public int length { get; }

        /// <summary>
        /// The type of this query error.
        /// </summary>
        public SearchQueryErrorType type { get; }

        /// <summary>
        /// The context on which this error was logged.
        /// </summary>
        public SearchContext context { get; }

        /// <summary>
        /// Which provider logged this error.
        /// </summary>
        public SearchProvider provider { get; }

        /// <summary>
        /// What is the reason for the error.
        /// </summary>
        public string reason { get; }

        /// <summary>
        /// Creates a new SearchQueryError.
        /// </summary>
        /// <param name="index">Index where the error happened.</param>
        /// <param name="length">Length of the block that was being parsed.</param>
        /// <param name="reason">What is the reason for the error.</param>
        /// <param name="context">The context on which this error was logged.</param>
        /// <param name="provider">Which provider logged this error.</param>
        /// <param name="fromSearchQuery">Set to true if this error comes from parsing the searchQuery. This will correctly offset the index with respect to the raw text.</param>
        /// <param name="type">The type of this query error.</param>
        public SearchQueryError(int index, int length, string reason, SearchContext context, SearchProvider provider, bool fromSearchQuery = true, SearchQueryErrorType type = SearchQueryErrorType.Error)
        {
            this.index = fromSearchQuery ? index + context.searchQueryOffset : index;
            this.length = length;
            this.reason = reason;
            this.type = type;
            this.context = context;
            this.provider = provider;
        }

        internal SearchQueryError(QueryError error, SearchContext context, SearchProvider provider, bool fromSearchQuery = true)
            : this(error.index, error.length, error.reason, context, provider, fromSearchQuery, error.type)
        {}

        /// <summary>
        /// Get the hashcode of this error.
        /// </summary>
        /// <returns>The hashcode of this error.</returns>
        public override int GetHashCode()
        {
            return (index.GetHashCode() * 397) ^ length.GetHashCode();
        }

        internal static int Compare(SearchQueryError x, SearchQueryError y)
        {
            if (x.type == SearchQueryErrorType.Error && y.type == SearchQueryErrorType.Warning)
                return -1;
            if (x.type == SearchQueryErrorType.Warning && y.type == SearchQueryErrorType.Error)
                return 1;

            return x.length.CompareTo(y.length);
        }

        internal bool Overlaps(SearchQueryError other)
        {
            if (index + length <= other.index)
                return false;
            if (other.index + other.length <= index)
                return false;
            return true;
        }
    }
}

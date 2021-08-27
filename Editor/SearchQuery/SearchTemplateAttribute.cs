using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.Search
{
    class SearchTemplateAttribute : Attribute
    {
        static List<SearchTemplateAttribute> s_QueryProviders;

        private string providerId;
        private string description;

        private Func<IEnumerable<string>> multiEntryHandler;

        public SearchTemplateAttribute(string description, string providerId = null)
        {
            this.providerId = providerId;
            this.description = description;
        }

        internal static IEnumerable<ISearchQuery> GetAllQueries()
        {
            return providers.SelectMany(p => p.CreateQuery());
        }

        IEnumerable<ISearchQuery> CreateQuery()
        {
            var queries = multiEntryHandler();
            foreach(var query in queries)
            {
                var q = new SearchQuery();
                q.searchText = query;
                q.displayName = query;
                q.viewState.providerIds = new[] { providerId };
                q.description = description;
                yield return q;
            }
        }

        internal static IEnumerable<SearchTemplateAttribute> providers
        {
            get
            {
                if (s_QueryProviders == null)
                    RefreshQueryProviders();
                return s_QueryProviders;
            }
        }

        internal static void RefreshQueryProviders()
        {
            s_QueryProviders = new List<SearchTemplateAttribute>();
            var methods = TypeCache.GetMethodsWithAttribute<SearchTemplateAttribute>();
            foreach (var mi in methods)
            {
                try
                {
                    var attr = mi.GetCustomAttributes(typeof(SearchTemplateAttribute), false).Cast<SearchTemplateAttribute>().First();
                    if (mi.ReturnType == typeof(string))
                    {
                        var singleEntryHandler = Delegate.CreateDelegate(typeof(Func<string>), mi) as Func<string>;
                        attr.multiEntryHandler = () => new[] { singleEntryHandler() };
                    }
                    else
                    {
                        attr.multiEntryHandler = Delegate.CreateDelegate(typeof(Func<IEnumerable<string>>), mi) as Func<IEnumerable<string>>;
                    }

                    s_QueryProviders.Add(attr);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Cannot register State provider: {mi.Name}\n{e}");
                }
            }
        }
    }
}

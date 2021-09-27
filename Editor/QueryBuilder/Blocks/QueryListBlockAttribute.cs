#if USE_QUERY_BUILDER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditorInternal;
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryListBlockAttribute : Attribute
    {
        static List<QueryListBlockAttribute> s_Attributes;

        public QueryListBlockAttribute(string category, string name, string id, string op = "=")
            : this(category, name, new []{id}, op)
        {}

        public QueryListBlockAttribute(string category, string name, string[] ids, string op = "=")
        {
            this.ids = ids ?? new string[]{};
            this.category = category;
            this.name = name;
            this.op = op;
        }

        public string[] ids;
        public string name;
        public string category;
        public string op;
        public Type type;

        public string id => ids.Length > 0 ? ids[0] : string.Empty;

        public static QueryListBlock CreateBlock(Type type, IQuerySource source, string value)
        {
            var attr = FindBlock(type);
            if (attr != null)
                return (QueryListBlock)Activator.CreateInstance(type, new object[] { source, attr.id, value, attr });
            return null;
        }

        public static QueryListBlock CreateBlock(string id, string op, IQuerySource source, string value)
        {
            var attr = FindBlock(id);
            QueryMarker.TryParse(value, out var marker);
            var isValidMarker = marker.valid && marker.type == "list";
            if (attr != null)
            {
                if (isValidMarker)
                {
                    return new QueryListMarkerBlock(source, id, marker, attr);
                }

                return (QueryListBlock)Activator.CreateInstance(attr.type, new object[] { source, id, value, attr });
            }
            else if (isValidMarker)
            {
                return new QueryListMarkerBlock(source, id, id, op, marker);
            }
            return null;
        }

        public static IEnumerable<SearchProposition> GetPropositions(Type type)
        {
            var block = CreateBlock(type, null, null);
            if (block != null)
                return block.GetPropositions();
            return new SearchProposition[0];
        }

        internal static QueryListBlockAttribute FindBlock(Type t)
        {
            if (s_Attributes == null)
                RefreshQueryListBlock();
            return s_Attributes.FirstOrDefault(a => a.type == t);
        }

        internal static QueryListBlockAttribute FindBlock(string id)
        {
            if (s_Attributes == null)
                RefreshQueryListBlock();
            return s_Attributes.FirstOrDefault(a => a.ids.Any(matchedId => matchedId.Equals(id, StringComparison.Ordinal)));
        }

        internal static void RefreshQueryListBlock()
        {
            s_Attributes = new List<QueryListBlockAttribute>();
            var types = TypeCache.GetTypesWithAttribute<QueryListBlockAttribute>();
            foreach (var ti in types)
            {
                try
                {
                    var attr = ti.GetCustomAttributes(typeof(QueryListBlockAttribute), false).Cast<QueryListBlockAttribute>().First();
                    attr.type = ti;
                    if (!typeof(QueryListBlock).IsAssignableFrom(ti))
                        continue;
                    s_Attributes.Add(attr);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Cannot register QueryListBlock provider: {ti.Name}\n{e}");
                }
            }
        }

        public static bool TryGetReplacement(string id, string type, ref Type blockType, out string replacement)
        {
            var block = CreateBlock(id, null, null, null);
            if (block != null)
                return block.TryGetReplacement(id, type, ref blockType, out replacement);
            replacement = string.Empty;
            return false;
        }
    }
}
#endif

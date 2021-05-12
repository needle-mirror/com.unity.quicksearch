using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Search;

namespace UnityEditor.Search
{
    [CustomPropertyDrawer(typeof(SearchContextAttribute))]
    sealed class SearchContextPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var searchContextAttribute = (SearchContextAttribute)attribute;
            var objType = fieldInfo.FieldType;
            var searchContextFlags = searchContextAttribute.flags;
            var context = CreateContextFromAttribute(searchContextAttribute);
            ObjectField.DoObjectField(position, property, objType, label, context, searchContextFlags);
        }

        static SearchContext CreateContextFromAttribute(SearchContextAttribute attribute)
        {
            var providers = attribute.providerIds.Select(id => SearchService.GetProvider(id))
                .Concat(attribute.instantiableProviders.Select(type => SearchService.GetProvider(type))).Where(p => p != null);

            if (!providers.Any())
                providers = SearchService.GetObjectProviders();

            var searchText = attribute.query;
            var searchQuery = GetSearchQueryFromFromAttribute(attribute);
            if (searchQuery != null)
            {
                searchText = searchQuery.text;
                providers = QuickSearch.GetMergedProviders(providers, searchQuery.providerIds);
            }

            var context = SearchService.CreateContext(providers, searchText);
            return context;
        }

        static SearchQueryAsset GetSearchQueryFromFromAttribute(SearchContextAttribute attribute)
        {
            return GetSearchQueryFromFromAttribute(attribute.query);
        }

        internal static SearchQueryAsset GetSearchQueryFromFromAttribute(string attributeQuery)
        {
            var pathOrGuid = attributeQuery.Trim();

            // Check if it's a path that exists
            if (File.Exists(pathOrGuid))
                return AssetDatabase.LoadAssetAtPath<SearchQueryAsset>(pathOrGuid);

            // Is it a GUID?
            if (GUID.TryParse(pathOrGuid, out var guid))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                    return null;
                if (!File.Exists(assetPath))
                    return null;
                return AssetDatabase.LoadAssetAtPath<SearchQueryAsset>(assetPath);
            }

            return null;
        }
    }
}

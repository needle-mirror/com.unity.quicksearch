using System.IO;
using System.Linq;
using System.Collections.Generic;
using static UnityEditor.Search.Providers.AssetProvider;

namespace UnityEditor.Search
{
    class AssetSelectors
    {
        [SearchSelector("name", provider: type, priority: 99)]
        static object GetAssetName(SearchItem item)
        {
            var obj = item.ToObject();
            return obj?.name;
        }

        [SearchSelector("filename", provider: type, priority: 99)]
        static object GetAssetFilename(SearchItem item)
        {
            return Path.GetFileName(GetAssetPath(item));
        }

        [SearchSelector("type", provider: type, priority: 99)]
        static string GetAssetType(SearchItem item)
        {
            if (GetAssetPath(item) is string assetPath)
                return AssetDatabase.GetMainAssetTypeAtPath(assetPath)?.Name;
            return null;
        }

        [SearchSelector("extension", provider: type, priority: 99)]
        static string GetAssetExtension(SearchItem item)
        {
            if (GetAssetPath(item) is string assetPath)
                return Path.GetExtension(assetPath).Substring(1);
            return null;
        }

        [SearchSelector("size", provider: type, priority: 99)]
        static object GetAssetFileSize(SearchItem item)
        {
            if (GetAssetPath(item) is string assetPath && !string.IsNullOrEmpty(assetPath))
            {
                var fi = new FileInfo(assetPath);
                return fi.Exists ? fi.Length : 0;
            }
            return null;
        }

        public static IEnumerable<SearchColumn> Enumerate(IEnumerable<SearchItem> items)
        {
            #if USE_SEARCH_MODULE
            return PropertySelectors.Enumerate(FilterItems(items, 5))
                .Concat(MaterialSelectors.Enumerate(FilterItems(items, 20)));
            #else
            return PropertySelectors.Enumerate(FilterItems(items, 5));
            #endif
        }

        static IEnumerable<SearchItem> FilterItems(IEnumerable<SearchItem> items, int count)
        {
            return items.Where(e => e.provider.type == type).Take(count);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UnityEditor.Search
{
    [Serializable]
    class SearchTable
    {
        [SerializeField] public string id;
        [SerializeField] public string name;
        [SerializeField] public SearchColumn[] columns;

        public SearchTable(string id, string name, IEnumerable<SearchColumn> columnModels)
        {
            this.id = id;
            this.name = name;
            columns = columnModels.Where(c => c != null).ToArray();
            InitFunctors();
        }

        public SearchTable(string name, IEnumerable<SearchColumn> columnModels)
            : this(Guid.NewGuid().ToString("N"), name, columnModels)
        {
        }

        public SearchTable(SearchTable other, string newName = null)
            : this(newName ?? other.name, other.columns)
        {
        }

        internal SearchTable Clone(string newName = null)
        {
            return new SearchTable(this, newName);
        }

        internal void InitFunctors()
        {
            // This is called to ensure members not serialized are properly init.
            foreach (var searchColumn in columns)
                searchColumn.InitFunctors();
        }

        internal static SearchTable CreateDefault(IEnumerable<SearchItem> items = null)
        {
            return new SearchTable("Default", ItemSelectors.Enumerate(items)
                .Select(c => { c.options |= SearchColumnFlags.Volatile; return c; }));
        }

        internal static SearchTable Import(string sessionTableConfigData)
        {
            var tc = JsonUtility.FromJson<SearchTable>(sessionTableConfigData);
            tc.InitFunctors();
            return tc;
        }

        internal string Export(bool format = false)
        {
            return JsonUtility.ToJson(this, format);
        }

        public static SearchTable LoadFromFile(string stcPath)
        {
            try
            {
                return Import(File.ReadAllText(stcPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load table configuration {stcPath.Replace("\\", "/")}\r\n{ex}");
            }

            return null;
        }
    }
}

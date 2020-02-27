using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;

namespace Unity.QuickSearch
{
    public class SearchFilter
    {
        private List<SearchProvider> m_Providers;

        [DebuggerDisplay("{name.displayName} enabled:{name.isEnabled}")]
        internal class ProviderDesc
        {
            public ProviderDesc(NameEntry name, SearchProvider provider)
            {
                this.name = name;
                this.provider = provider;
            }

            public int priority => provider.priority;

            public NameEntry name;
            public SearchProvider provider;
        }
        internal IEnumerable<ProviderDesc> providerDescriptors { get; private set; }

        public event Action filterChanged;

        public IEnumerable<SearchProvider> filteredProviders { get; private set; }

        public SearchFilter(IEnumerable<SearchProvider> filterProviders)
        {
            filteredProviders = new List<SearchProvider>();
            m_Providers = filterProviders.ToList();

            providerDescriptors = m_Providers.Where(p => p.active)
                .Select(provider => new ProviderDesc(new NameEntry(provider.name.id, GetProviderNameWithFilter(provider)), provider)).ToList();

            UpdateFilteredProviders();
        }

        public void ResetFilter(bool enableAll)
        {
            foreach (var providerDesc in providerDescriptors)
                SetFilterInternal(enableAll, providerDesc.name.id);
            UpdateFilteredProviders();
        }

        public void SetFilter(bool isEnabled, string providerId)
        {
            if (SetFilterInternal(isEnabled, providerId) != null)
                UpdateFilteredProviders();
        }

        public bool IsEnabled(string providerId)
        {
            var desc = providerDescriptors.FirstOrDefault(pd => pd.name.id == providerId);
            if (desc != null)
            {
                return desc.name.isEnabled;
            }

            return false;
        }

        public static string GetProviderNameWithFilter(SearchProvider provider)
        {
            return string.IsNullOrEmpty(provider.filterId) ? provider.name.displayName : provider.name.displayName + " (" + provider.filterId + ")";
        }

        internal void UpdateFilteredProviders()
        {
            var updatedFiltered = m_Providers.Where(p => IsEnabled(p.name.id)).ToList();
            if (!filteredProviders.SequenceEqual(updatedFiltered))
            {
                filteredProviders = updatedFiltered;
                filterChanged?.Invoke();
            }
        }

        internal ProviderDesc SetFilterInternal(bool isEnabled, string providerId)
        {
            var providerDesc = providerDescriptors.FirstOrDefault(pd => pd.name.id == providerId);
            if (providerDesc != null)
            {
                providerDesc.name.isEnabled = isEnabled;
            }
            return providerDesc;
        }

        internal static bool LoadFilters(SearchFilter filter, string prefKey)
        {
            var filtersStr = EditorPrefs.GetString(prefKey, null);

            return Deserialize(filter, filtersStr);
        }

        internal static void SaveFilters(SearchFilter filter, string prefKey)
        {
            var filterStr = Serialize(filter);
            EditorPrefs.SetString(prefKey, filterStr);
        }

        internal static string Serialize(SearchFilter filter)
        {
            var filters = new List<object>();
            foreach (var providerDesc in filter.providerDescriptors)
            {
                var filterDict = new Dictionary<string, object>
                {
                    ["providerId"] = providerDesc.name.id,
                    ["isEnabled"] = providerDesc.name.isEnabled
                };
                filters.Add(filterDict);
            }

            return Utils.JsonSerialize(filters);
        }

        internal static bool Deserialize(SearchFilter filter, string filtersStr)
        {
            try
            {
                filter.ResetFilter(true);
                if (!string.IsNullOrEmpty(filtersStr))
                {
                    var filters = Utils.JsonDeserialize(filtersStr) as List<object>;
                    foreach (var filterObj in filters)
                    {
                        var filterJson = filterObj as Dictionary<string, object>;
                        if (filterJson == null)
                            continue;

                        var providerId = filterJson["providerId"] as string;
                        var desc = filter.SetFilterInternal(filterJson["isEnabled"].ToString() == "True", providerId);
                        if (desc == null)
                            continue;
                    }
                }

                filter.UpdateFilteredProviders();
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }
    }
}
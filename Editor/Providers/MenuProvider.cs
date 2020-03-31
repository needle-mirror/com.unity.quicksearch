using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Unity.QuickSearch
{
    namespace Providers
    {
        [UsedImplicitly]
        static class MenuProvider
        {
            internal static string type = "menu";
            internal static string displayName = "Menu";

            internal static string[] itemNamesLower;
            internal static List<string> itemNames = new List<string>();
            internal static string[] shortcutIds;

            [UsedImplicitly, SearchItemProvider]
            internal static SearchProvider CreateProvider()
            {
                List<string> shortcuts = new List<string>();
                GetMenuInfo(itemNames, shortcuts);
                itemNamesLower = itemNames.Select(n => n.ToLowerInvariant()).ToArray();

                return new SearchProvider(type, displayName)
                {
                    priority = 80,
                    filterId = "me:",

                    onEnable = () =>
                    {
                        shortcutIds = ShortcutManager.instance.GetAvailableShortcutIds().ToArray();
                    },

                    onDisable = () =>
                    {
                        shortcutIds = new string[0];
                    },

                    fetchItems = (context, items, provider) =>
                    {
                        if (string.IsNullOrEmpty(context.searchQuery))
                            return null;

                        for (int i = 0; i < itemNames.Count; ++i)
                        {
                            var menuName = itemNames[i];
                            if (!SearchUtils.MatchSearchGroups(context, itemNamesLower[i], true))
                                continue;

                            items.Add(provider.CreateItem(menuName, Utils.GetNameFromPath(menuName)));
                        }

                        return null;
                    },

                    fetchDescription = (item, context) =>
                    {
                        if (String.IsNullOrEmpty(item.description))
                            item.description = GetMenuDescription(item.id);
                        return item.description;
                    },

                    fetchThumbnail = (item, context) => Icons.shortcut
                };
            }

            private static string GetMenuDescription(string menuName)
            {
                var sm = ShortcutManager.instance;
                if (sm == null)
                    return menuName;

                var shortcutId = menuName;
                if (!shortcutIds.Contains(shortcutId))
                {
                    shortcutId = "Main Menu/" + menuName;
                    if (!shortcutIds.Contains(shortcutId))
                        return menuName;
                }
                var shortcutBinding = ShortcutManager.instance.GetShortcutBinding(shortcutId);
                if (!shortcutBinding.keyCombinationSequence.Any())
                    return menuName;

                return $"{menuName} ({shortcutBinding.ToString()})";
            }

            [UsedImplicitly, SearchActionsProvider]
            internal static IEnumerable<SearchAction> ActionHandlers()
            {
                return new[]
                {
                    new SearchAction("menu", "exec", null, "Execute shortcut...")
                    {
                        handler = (item, context) =>
                        {
                            var menuId = item.id;
                            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem(menuId);
                        }
                    }
                };
            }

            [UsedImplicitly, Shortcut("Help/Quick Search/Menu", KeyCode.M, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
            private static void OpenQuickSearch()
            {
                var qs = QuickSearch.OpenWithContextualProvider(type);
                qs.itemIconSize = 0; // Open in list view by default.
            }

            private static void GetMenuInfo(List<string> outItemNames, List<string> outItemDefaultShortcuts)
            {
                Assembly assembly = typeof(Menu).Assembly;
                var managerType = assembly.GetTypes().First(t => t.Name == "Menu");
                var method = managerType.GetMethod("GetMenuItemDefaultShortcuts", BindingFlags.NonPublic | BindingFlags.Static);
                var arguments = new object[] { outItemNames, outItemDefaultShortcuts };
                method.Invoke(null, arguments);
            }
        }
    }
}

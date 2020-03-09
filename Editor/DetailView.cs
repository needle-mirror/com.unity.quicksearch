using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    class DetailView
    {
        private readonly ISearchView m_SearchView;
        private string m_LastPreviewItemId;
        private Editor[] m_Editors;
        private int m_EditorTargetID = 0;
        private Vector2 m_ScrollPosition;
        private double m_LastPreviewStamp = 0;
        private Texture2D m_PreviewTexture;
        private Dictionary<string, bool> m_EditorTypeFoldout = new Dictionary<string, bool>();

        public DetailView(ISearchView searchView)
        {
            m_SearchView = searchView;
        }

        public void Draw(SearchContext context, SearchItem item, float width, float height, Action<SearchItem, bool> selectCallback)
        {
            if (item == null || !item.provider.showDetails)
                return;

            var showOptions = item.provider.showDetailsOptions;

            using (var scrollView = new EditorGUILayout.ScrollViewScope(m_ScrollPosition, GUILayout.Width(width), GUILayout.ExpandHeight(true)))
            {
                if (showOptions.HasFlag(ShowDetailsOptions.Preview))
                    DrawPreview(context, item);

                if (showOptions.HasFlag(ShowDetailsOptions.Description))
                    DrawDescription(context, item);

                if (showOptions.HasFlag(ShowDetailsOptions.Inspector))
                    DrawInspector(item, width);
                
                if (showOptions.HasFlag(ShowDetailsOptions.Actions) || selectCallback != null)
                    DrawActions(context, item, selectCallback, showOptions);

                m_ScrollPosition = scrollView.scrollPosition;
            }
        }

        private void DrawActions(SearchContext context, SearchItem item, Action<SearchItem, bool> selectCallback, ShowDetailsOptions showOptions)
        {
            GUILayout.Space(10);

            if (selectCallback == null)
            {
                if (showOptions.HasFlag(ShowDetailsOptions.Actions))
                {
                    foreach (var action in item.provider.actions)
                    {
                        if (action == null || action.Id == "context" || action.content == null || action.handler == null)
                            continue;
                        if (GUILayout.Button(new GUIContent(action.DisplayName, action.content.image, action.content.tooltip), GUILayout.ExpandWidth(true)))
                        {
                            m_SearchView.ExecuteAction(action, item, context, true);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
            else if (GUILayout.Button("Select", GUILayout.ExpandWidth(true)))
            {
                selectCallback(item, false);
                m_SearchView.Close();
                GUIUtility.ExitGUI();
            }
        }

        private void ResetEditors()
        {
            if (m_Editors != null)
            {
                foreach (var e in m_Editors)
                    UnityEngine.Object.DestroyImmediate(e);
            }
            m_Editors = null;
            m_EditorTargetID = 0;
        }

        private void DrawInspector(SearchItem item, float width)
        {
            if (Event.current.type == EventType.Layout)
                SetupEditors(item);

            if (m_Editors != null)
            {
                for (int i = 0; i < m_Editors.Length; ++i)
                {
                    var e = m_Editors[i];

                    EditorGUIUtility.labelWidth = 0.4f * width;
                    bool foldout = false;
                    if (!m_EditorTypeFoldout.TryGetValue(e.GetType().Name, out foldout))
                        foldout = true;
                    using (new EditorGUIUtility.IconSizeScope(new Vector2(16, 16)))
                    {
                        var sectionContent = EditorGUIUtility.ObjectContent(e.target, e.GetType());
                        sectionContent.tooltip = sectionContent.text;
                        foldout = EditorGUILayout.BeginToggleGroup(sectionContent, foldout);
                        if (foldout)
                        {
                            if (e.target is Transform)
                                e.DrawDefaultInspector();
                            else
                                e.OnInspectorGUI();
                        }

                        m_EditorTypeFoldout[e.GetType().Name] = foldout;
                        EditorGUILayout.EndToggleGroup();
                    }
                }
            }
        }

        private void SetupEditors(SearchItem item)
        {
            var itemObject = item.provider.toObject?.Invoke(item, typeof(UnityEngine.Object));
            if (itemObject && itemObject.GetInstanceID() != m_EditorTargetID)
            {
                var targets = new List<UnityEngine.Object>();
                if (itemObject is GameObject go)
                {
                    var components = go.GetComponents<Component>();
                    foreach (var c in components.Skip(components.Length > 1 ? 1 : 0))
                    {
                        if (c.hideFlags.HasFlag(HideFlags.HideInInspector))
                            continue;

                        targets.Add(c);
                    }
                }
                else
                    targets.Add(itemObject);

                ResetEditors();
                m_Editors = targets.Select(t => Editor.CreateEditor(t)).Where(e => e).ToArray();
                m_EditorTargetID = itemObject.GetInstanceID();
            }
            else if (!itemObject)
            {
                ResetEditors();
            }
        }

        private static void DrawDescription(SearchContext context, SearchItem item)
        {
            var description = SearchContent.FormatDescription(item, context, 2048);
            GUILayout.Label(description, Styles.previewDescription);
        }

        private void DrawPreview(SearchContext context, SearchItem item)
        {
            if (item.provider.fetchPreview == null)
                return;
            var now = EditorApplication.timeSinceStartup;
            if (now - m_LastPreviewStamp > 2.5)
                m_PreviewTexture = null;

            if (!m_PreviewTexture || m_LastPreviewItemId != item.id)
            {
                m_LastPreviewStamp = now;
                m_PreviewTexture = item.provider.fetchPreview(item, context, Styles.previewSize, FetchPreviewOptions.Preview2D | FetchPreviewOptions.Large);
                m_LastPreviewItemId = item.id;
            }

            if (m_PreviewTexture == null || AssetPreview.IsLoadingAssetPreviews())
                m_SearchView.Repaint();

            GUILayout.Space(10);
            GUILayout.Label(m_PreviewTexture, Styles.largePreview, GUILayout.MaxWidth(Styles.previewSize.x), GUILayout.MaxHeight(Styles.previewSize.y));
        }
    }
}
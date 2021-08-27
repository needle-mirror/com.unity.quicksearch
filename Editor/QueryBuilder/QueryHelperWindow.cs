#if USE_QUERY_BUILDER
using UnityEngine;

namespace UnityEditor.Search
{
    class QueryHelperWindow : EditorWindow
    {
        QueryHelperWidget m_Widget;
        bool m_ShownAsDropdown;

        public static void Open(Rect r, ISearchView view)
        {
            var screenRect = new Rect(GUIUtility.GUIToScreenPoint(r.position), r.size);
            var window = CreateInstance<QueryHelperWindow>();
            window.m_ShownAsDropdown = true;
            window.m_Widget.BindSearchView(view);
            window.ShowAsDropDown(screenRect, window.m_Widget.GetExpectedSize());
            GUIUtility.ExitGUI();
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            titleContent = new GUIContent("Helper");
            m_Widget = new QueryHelperWidget();
            m_Widget.blockSelected += OnBlockSelected;
        }

        void OnGUI()
        {
            var e = Event.current;
            m_Widget.Draw(e, new Rect(0, 0, position.width, position.height));
            if (m_ShownAsDropdown && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                Close();
        }

        void OnBlockSelected(QueryBuilder builder, QueryBlock block, bool isDoubleClick)
        {
            if (m_ShownAsDropdown)
                Close();
        }

        [MenuItem("Window/Search/Builder Helper Window (Debug)")]
        static void ShowWindowDebug()
        {
            GetWindow<QueryHelperWindow>();
        }
    }
}
#endif

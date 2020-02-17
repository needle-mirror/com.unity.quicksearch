using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch.Providers
{
    [CustomEditor(typeof(ADBIndex))]
    class ADBIndexEditor : Editor
    {
        private ADBIndex m_DB;
        private SerializedProperty m_Settings;

        [SerializeField] private bool m_KeywordsFoldout;
        [SerializeField] private bool m_DocumentsFoldout;
        private GUIContent m_IndexTitleLabel;

        static class Styles
        {
        }

        internal void OnEnable()
        {
            m_DB = (ADBIndex)target;
            m_Settings = serializedObject.FindProperty("settings");
            m_IndexTitleLabel = new GUIContent($"{m_DB.index?.name ?? m_DB.name} ({EditorUtility.FormatBytes(m_DB.bytes?.Length ?? 0)})");
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.PropertyField(m_Settings, m_IndexTitleLabel, true);

            EditorGUILayout.IntField($"Indexes", m_DB.index.indexCount);
            m_DocumentsFoldout = EditorGUILayout.Foldout(m_DocumentsFoldout, $"Documents (Count={m_DB.index.documentCount})", true);
            if (m_DocumentsFoldout)
            {
                foreach (var documentEntry in m_DB.index.GetDocuments().OrderBy(p=>p))
                    EditorGUILayout.LabelField(documentEntry);
            }

            m_KeywordsFoldout = EditorGUILayout.Foldout(m_KeywordsFoldout, $"Keywords (Count={m_DB.index.keywordCount})", true);
            if (m_KeywordsFoldout)
            {
                foreach (var t in m_DB.index.GetKeywords().OrderBy(p => p))
                    EditorGUILayout.LabelField(t);
            }
        }

        protected override bool ShouldHideOpenButton()
        {
            return true;
        }

        public override bool HasPreviewGUI()
        {
            return false;
        }

        public override bool RequiresConstantRepaint()
        {
            return false;
        }

        public override bool UseDefaultMargins()
        {
            return true;
        }
    }
}

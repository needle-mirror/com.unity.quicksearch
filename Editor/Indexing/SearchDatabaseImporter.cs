using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

namespace Unity.QuickSearch
{
    class SearchDatabaseTemplates
    {
        public static readonly string @default =
@"{
    ""name"": ""Assets"",
    ""roots"": [""Assets""],
    ""includes"": [],
    ""excludes"": [""Temp/"", ""External/""],
    ""options"": {
        ""types"": true,
        ""properties"": false,
        ""extended"": false,
        ""dependencies"": false
    },
    ""baseScore"": 999
}";

        public static readonly string assets =
@"{
    ""roots"": [],
    ""includes"": [],
    ""excludes"": [""Temp/"", ""External/""],
    ""options"": {
        ""types"": true,
        ""properties"": false,
        ""extended"": false,
        ""dependencies"": false
    },
    ""baseScore"": 100
}";
        public static readonly string prefabs = @"{
    ""type"": ""prefab"",
    ""roots"": [],
    ""includes"": [],
    ""excludes"": [],
    ""options"": {
        ""types"": true,
        ""properties"": true,
        ""extended"": false,
        ""dependencies"": true
    },
    ""baseScore"": 150
}";
        public static readonly string scenes = @"{
    ""type"": ""scene"",
    ""roots"": [],
    ""includes"": [],
    ""excludes"": [],
    ""options"": {
        ""types"": true,
        ""properties"": true,
        ""extended"": false,
        ""dependencies"": false
    },
    ""baseScore"": 155
}";

        public static readonly Dictionary<string, string> all = new Dictionary<string, string>()
        {
            { "Assets", assets },
            { "Prefabs", prefabs },
            { "Scenes", scenes },
            { "_Default", @default }
        };
    }

    [ExcludeFromPreset, ScriptedImporter(version: SearchDatabase.version, ext: "index", importQueueOffset: 1999)] // kImportOrderPrefabs = 1500
    class SearchDatabaseImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            try
            {
                var db = ScriptableObject.CreateInstance<SearchDatabase>();
                db.Import(ctx.assetPath);
                ctx.AddObjectToAsset("index", db, Icons.quicksearch);
                ctx.SetMainObject(db);

                ctx.DependsOnCustomDependency(nameof(CustomObjectIndexerAttribute));

                hideFlags |= HideFlags.HideInInspector;
            }
            catch (SearchDatabaseException ex)
            {
                ctx.LogImportError(ex.Message, AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(ex.guid)));
            }
        }

        public static string CreateTemplateIndex(string template, string path, string name = null)
        {
            if (!SearchDatabaseTemplates.all.ContainsKey(template))
                return null;

            var dirPath = path;
            var templateContent = SearchDatabaseTemplates.all[template];

            if (File.Exists(path))
            {
                dirPath = Path.GetDirectoryName(path);
                if (Selection.assetGUIDs.Length > 1)
                    path = dirPath;
            }

            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            var indexFileName = string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(path) : name;
            var indexPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(dirPath, $"{indexFileName}.index")).Replace("\\", "/");

            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                $"Creating {template.Trim('_')} index at <a file=\"{indexPath}\">{indexPath}</a>");

            SearchAnalytics.SendEvent(null, SearchAnalytics.GenericEventType.CreateIndexFromTemplate, template);

            File.WriteAllText(indexPath, templateContent);
            AssetDatabase.ImportAsset(indexPath);

            return indexPath;
        }

        private static bool ValidateTemplateIndexCreation<T>() where T : UnityEngine.Object
        {
            var asset = Selection.activeObject as T;
            if (asset)
                return true;
            return CreateIndexProjectValidation();
        }

        [MenuItem("Assets/Create/Search/Project Index")]
        internal static void CreateIndexProject()
        {
            var folderPath = "Assets";
            if (Selection.activeObject != null)
                folderPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            CreateTemplateIndex("Assets", folderPath);
        }

        [MenuItem("Assets/Create/Search/Project Index", validate = true)]
        internal static bool CreateIndexProjectValidation()
        {
            if (Selection.activeObject == null)
                return true;
            var folder = Selection.activeObject as DefaultAsset;
            if (!folder)
                return false;
            return Directory.Exists(AssetDatabase.GetAssetPath(folder));
        }

        [MenuItem("Assets/Create/Search/Prefab Index")]
        internal static void CreateIndexPrefab()
        {
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            CreateTemplateIndex("Prefabs", assetPath);
        }

        [MenuItem("Assets/Create/Search/Prefab Index", validate = true)]
        internal static bool CreateIndexPrefabValidation()
        {
            return ValidateTemplateIndexCreation<GameObject>();
        }

        [MenuItem("Assets/Create/Search/Scene Index")]
        internal static void CreateIndexScene()
        {
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            CreateTemplateIndex("Scenes", assetPath);
        }

        [MenuItem("Assets/Create/Search/Scene Index", validate = true)]
        internal static bool CreateIndexSceneValidation()
        {
            return ValidateTemplateIndexCreation<SceneAsset>();
        }
    }
}

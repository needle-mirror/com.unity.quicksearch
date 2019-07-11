using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    public static class Icons
    {
        public static string iconFolder = $"{QuickSearchTool.packageFolderName}/Editor/Icons";
        public static Texture2D shortcut = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/shortcut.png");
        public static Texture2D quicksearch = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/quicksearch.png");
        public static Texture2D filter = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/filter.png");
        public static Texture2D settings = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/settings.png");
        public static Texture2D search = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/search.png");
        public static Texture2D clear = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/clear.png");
        public static Texture2D more = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/more.png");
        public static Texture2D store = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/store.png");
        public static Texture2D logInfo = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/log_info.png");
        public static Texture2D logWarning = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/log_warning.png");
        public static Texture2D logError = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/log_error.png");
        public static Texture2D packageInstalled = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/package_installed.png");
        public static Texture2D packageUpdate = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/package_update.png");
        public static Texture2D loading = (Texture2D)EditorGUIUtility.Load($"{iconFolder}/loading.png");

        static Icons()
        {
            if (EditorGUIUtility.isProSkin)
            {
                shortcut = LightenTexture(shortcut);
                quicksearch = LightenTexture(quicksearch);
                filter = LightenTexture(filter);
                settings = LightenTexture(settings);
                search = LightenTexture(search);
                clear = LightenTexture(clear);
                more = LightenTexture(more);
                store = LightenTexture(store);
                packageInstalled = LightenTexture(packageInstalled);
                packageUpdate = LightenTexture(packageUpdate);
            }
        }

        private static Texture2D LightenTexture(Texture2D texture)
        {
            Texture2D outTexture = new Texture2D(texture.width, texture.height);
            var outColorArray = outTexture.GetPixels();

            var colorArray = texture.GetPixels();
            for (var i = 0; i < colorArray.Length; ++i)
                outColorArray[i] = LightenColor(colorArray[i]);

            outTexture.hideFlags = HideFlags.HideAndDontSave;
            outTexture.SetPixels(outColorArray);
            outTexture.Apply();

            return outTexture;
        }

        public static Color LightenColor(Color color)
        {
            Color.RGBToHSV(color, out var h, out _, out _);
            var outColor = Color.HSVToRGB((h + 0.5f) % 1, 0f, 0.8f);
            outColor.a = color.a;
            return outColor;
        }
    }
}
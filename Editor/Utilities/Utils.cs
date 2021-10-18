using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditorInternal;
using UnityEngine.UIElements;
using UnityEditor.IMGUI.Controls;

#if USE_SEARCH_MODULE
using UnityEditor.Connect;
using UnityEditor.StyleSheets;
#else
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor-testable")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("com.unity.search.extensions.editor")]
#endif

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("com.unity.quicksearch.tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.Environment.Core.Editor")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.ProceduralGraph.Editor")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.Rendering.Hybrid")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.VisualEffectGraph.Editor")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Unity.Localization.Editor")]

namespace UnityEditor.Search
{
    #if !USE_SEARCH_MODULE
    internal sealed class RequiredSignatureAttribute : Attribute
    {
    }
    #endif

    /// <summary>
    /// This utility class mainly contains proxy to internal API that are shared between the version in trunk and the package version.
    /// </summary>
    static class Utils
    {
        const int k_MaxRegexTimeout = 25;

        #if !USE_SEARCH_MODULE
        const string packageName = "com.unity.quicksearch";
        public static readonly string packageFolderName = $"Packages/{packageName}";

        internal struct InspectorWindowUtils_LayoutGroupChecker : IDisposable
        {
            public void Dispose()
            {
            }
        }
        #endif

        internal static readonly bool isDeveloperBuild = false;

        struct RootDescriptor
        {
            public RootDescriptor(string root)
            {
                this.root = root;
                absPath = CleanPath(new FileInfo(root).FullName);
            }

            public string root;
            public string absPath;

            public override string ToString()
            {
                return $"{root} -> {absPath}";
            }
        }

        public struct ColorScope : IDisposable
        {
            private bool m_Disposed;
            private Color m_PreviousColor;

            public ColorScope(Color newColor)
            {
                m_Disposed = false;
                m_PreviousColor = GUI.color;
                GUI.color = newColor;
            }

            public ColorScope(float r, float g, float b, float a = 1.0f) : this(new Color(r, g, b, a))
            {
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                GUI.color = m_PreviousColor;
            }
        }

        static RootDescriptor[] s_RootDescriptors;

        static RootDescriptor[] rootDescriptors
        {
            get { return s_RootDescriptors ?? (s_RootDescriptors = GetAssetRootFolders().Select(root => new RootDescriptor(root)).OrderByDescending(desc => desc.absPath.Length).ToArray()); }
        }

        private static UnityEngine.Object[] s_LastDraggedObjects;

        #if !USE_SEARCH_MODULE
        static UnityEngine.Object s_MainWindow = null;
        private static MethodInfo s_GetNumCharactersThatFitWithinWidthMethod;
        private static MethodInfo s_GetMainAssetInstanceID;
        private static MethodInfo s_FindTextureMethod;
        private static MethodInfo s_LoadIconMethod;
        private static MethodInfo s_GetFileIDHint;
        private static MethodInfo s_GetIconForObject;
        private static MethodInfo s_CallDelayed;
        private static MethodInfo s_FromUSSMethod;
        private static MethodInfo s_HasCurrentWindowKeyFocusMethod;
        private static Action<string> s_OpenPackageManager;
        private static MethodInfo s_GetSourceAssetFileHash;
        private static PropertyInfo s_CurrentViewWidth;
        private static FieldInfo s_TextEditor_m_HasFocus;
        private static MethodInfo s_ObjectFieldButtonGetter;
        private static MethodInfo s_BeginHorizontal;
        private static MethodInfo s_IsGUIClipEnabled;
        private static MethodInfo s_Unclip;
        private static MethodInfo s_MonoScriptFromScriptedObject;
        private static MethodInfo s_SerializedPropertyIsScript;
        private static MethodInfo s_SerializedPropertyObjectReferenceStringValue;
        private static MethodInfo s_ObjectContent;
        private static string s_CommandDelete;
        private static string s_CommandSoftDelete;
        private static MethodInfo s_PopupWindowWithoutFocus;
        private static MethodInfo s_PopupWindowWithoutFocusTyped;
        private static MethodInfo s_OpenPropertyEditor;
        private static MethodInfo s_MainActionKeyForControl;
        private static MethodInfo s_GUIContentTempS;
        private static MethodInfo s_GUIContentTempSS;
        private static MethodInfo s_GUIContentTempST;
        private static MethodInfo s_SetChildParentReferences;
        private static MethodInfo s_HasInvalidComponent;

        internal static string GetPackagePath(string relativePath)
        {
            return Path.Combine(packageFolderName, relativePath).Replace("\\", "/");
        }

        private static Type[] GetAllEditorWindowTypes()
        {
            return TypeCache.GetTypesDerivedFrom<EditorWindow>().ToArray();
        }

        #endif

        public static GUIStyle objectFieldButton
        {
            get
            {
                #if USE_SEARCH_MODULE
                return EditorStyles.objectFieldButton;
                #else
                if (s_ObjectFieldButtonGetter == null)
                {
                    var type = typeof(EditorStyles);
                    var pi = type.GetProperty(nameof(objectFieldButton), BindingFlags.NonPublic | BindingFlags.Static);
                    s_ObjectFieldButtonGetter = pi.GetMethod;
                }
                return s_ObjectFieldButtonGetter.Invoke(null, null) as GUIStyle;
                #endif
            }
        }

        static Utils()
        {
            #if USE_SEARCH_MODULE
            isDeveloperBuild = Unsupported.IsSourceBuild();
            #else
            isDeveloperBuild = File.Exists($"{packageFolderName}/.dev");
            #endif
        }

        internal static void OpenInBrowser(string baseUrl, List<Tuple<string, string>> query = null)
        {
            var url = baseUrl;

            if (query != null)
            {
                url += "?";
                for (var i = 0; i < query.Count; ++i)
                {
                    var item = query[i];
                    url += item.Item1 + "=" + item.Item2;
                    if (i < query.Count - 1)
                    {
                        url += "&";
                    }
                }
            }

            var uri = new Uri(url);
            Process.Start(uri.AbsoluteUri);
        }

        internal static SettingsProvider[] FetchSettingsProviders()
        {
            #if USE_SEARCH_MODULE
            return SettingsService.FetchSettingsProviders();
            #else
            var type = typeof(SettingsService);
            var method = type.GetMethod("FetchSettingsProviders", BindingFlags.NonPublic | BindingFlags.Static);
            return (SettingsProvider[])method.Invoke(null, null);
            #endif
        }

        internal static string GetNameFromPath(string path)
        {
            var lastSep = path.LastIndexOf('/');
            if (lastSep == -1)
                return path;

            return path.Substring(lastSep + 1);
        }

        internal static Hash128 GetSourceAssetFileHash(string guid)
        {
            #if USE_SEARCH_MODULE
            return AssetDatabase.GetSourceAssetFileHash(guid);
            #else
            if (s_GetSourceAssetFileHash == null)
            {
                var type = typeof(UnityEditor.AssetDatabase);
                s_GetSourceAssetFileHash = type.GetMethod("GetSourceAssetFileHash", BindingFlags.NonPublic | BindingFlags.Static);
                if (s_GetSourceAssetFileHash == null)
                    return default;
            }
            object[] parameters = new object[] { guid };
            return (Hash128)s_GetSourceAssetFileHash.Invoke(null, parameters);
            #endif
        }

        internal static Texture2D GetAssetThumbnailFromPath(string path)
        {
            var thumbnail = GetAssetPreviewFromGUID(AssetDatabase.AssetPathToGUID(path));
            if (thumbnail)
                return thumbnail;
            thumbnail = AssetDatabase.GetCachedIcon(path) as Texture2D;
            return thumbnail ?? InternalEditorUtility.FindIconForFile(path);
        }

        private static Texture2D GetAssetPreviewFromGUID(string guid)
        {
            #if USE_SEARCH_MODULE
            return AssetPreview.GetAssetPreviewFromGUID(guid);
            #else
            return null;
            #endif
        }

        internal static Texture2D GetAssetPreviewFromPath(string path, FetchPreviewOptions previewOptions)
        {
            return GetAssetPreviewFromPath(path, new Vector2(128, 128), previewOptions);
        }

        internal static Texture2D GetAssetPreviewFromPath(string path, Vector2 previewSize, FetchPreviewOptions previewOptions)
        {
            var assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (assetType == typeof(SceneAsset))
                return AssetDatabase.GetCachedIcon(path) as Texture2D;

            if (previewOptions.HasAny(FetchPreviewOptions.Normal))
            {
                if (assetType == typeof(AudioClip))
                    return GetAssetThumbnailFromPath(path);

                try
                { 
                    var fi = new FileInfo(path);
                    if (!fi.Exists)
                        return null;
                    if (fi.Length > 16 * 1024 * 1024)
                        return GetAssetThumbnailFromPath(path);
                }
                catch
                {
                    return null;
                }
            }

            if (!typeof(Texture).IsAssignableFrom(assetType))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex)
                    return tex;
            }

            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null)
                return null;

            #if USE_SEARCH_MODULE
            if (previewOptions.HasAny(FetchPreviewOptions.Large))
            {
                var tex = AssetPreviewUpdater.CreatePreview(obj, null, path, (int)previewSize.x, (int)previewSize.y);
                if (tex)
                    return tex;
            }
            #endif

            return GetAssetPreview(obj, previewOptions) ?? AssetDatabase.GetCachedIcon(path) as Texture2D;
        }

        internal static bool HasInvalidComponent(UnityEngine.Object obj)
        {
            #if USE_SEARCH_MODULE
            return PrefabUtility.HasInvalidComponent(obj);
            #else
            if (s_HasInvalidComponent == null)
            {
                var type = typeof(PrefabUtility);
                s_HasInvalidComponent = type.GetMethod("s_HasInvalidComponent", BindingFlags.NonPublic | BindingFlags.Static);
                if (s_HasInvalidComponent == null)
                    return default;
            }
            return (bool)s_HasInvalidComponent.Invoke(null, new object[] { obj });
            #endif
        }

        internal static int GetMainAssetInstanceID(string assetPath)
        {
            #if USE_SEARCH_MODULE
            return AssetDatabase.GetMainAssetInstanceID(assetPath);
            #else
            if (s_GetMainAssetInstanceID == null)
            {
                var type = typeof(UnityEditor.AssetDatabase);
                s_GetMainAssetInstanceID = type.GetMethod("GetMainAssetInstanceID", BindingFlags.NonPublic | BindingFlags.Static);
                if (s_GetMainAssetInstanceID == null)
                    return default;
            }
            object[] parameters = new object[] { assetPath };
            return (int)s_GetMainAssetInstanceID.Invoke(null, parameters);
            #endif
        }

        internal static GUIContent GUIContentTemp(string text, string tooltip)
        {
            #if USE_SEARCH_MODULE
            return GUIContent.Temp(text, tooltip);
            #else
            if (s_GUIContentTempSS == null)
            {
                var type = typeof(GUIContent);
                s_GUIContentTempSS = type.GetMethod("Temp", BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(string), typeof(string) }, null);
            }
            return (GUIContent)s_GUIContentTempSS.Invoke(null, new object[] { text, tooltip });
            #endif
        }

        internal static GUIContent GUIContentTemp(string text, Texture image)
        {
            #if USE_SEARCH_MODULE
            return GUIContent.Temp(text, image);
            #else
            if (s_GUIContentTempST == null)
            {
                var type = typeof(GUIContent);
                s_GUIContentTempST = type.GetMethod("Temp", BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(string), typeof(Texture) }, null);
            }
            return (GUIContent)s_GUIContentTempST.Invoke(null, new object[] { text, image });
            #endif
        }

        internal static GUIContent GUIContentTemp(string text)
        {
            #if USE_SEARCH_MODULE
            return GUIContent.Temp(text);
            #else
            if (s_GUIContentTempS == null)
            {
                var type = typeof(GUIContent);
                s_GUIContentTempS = type.GetMethod("Temp", BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
            }
            return (GUIContent)s_GUIContentTempS.Invoke(null, new object[] { text });
            #endif
        }

        internal static Texture2D GetAssetPreview(UnityEngine.Object obj, FetchPreviewOptions previewOptions)
        {
            var preview = AssetPreview.GetAssetPreview(obj);
            if (preview == null || previewOptions.HasAny(FetchPreviewOptions.Large))
            {
                var largePreview = AssetPreview.GetMiniThumbnail(obj);
                if (preview == null || (largePreview != null && largePreview.width > preview.width))
                    preview = largePreview;
            }
            return preview;
        }

        internal static void SetChildParentReferences(IList<TreeViewItem> m_Items, TreeViewItem root)
        {
            #if USE_SEARCH_MODULE
            TreeViewUtility.SetChildParentReferences(m_Items, root);
            #else
            if (s_SetChildParentReferences == null)
            {
                var assembly = typeof(TreeView).Assembly;
                var type = assembly.GetTypes().First(t => t.Name == "TreeViewUtility");
                s_SetChildParentReferences = type.GetMethod("SetChildParentReferences", BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(IList<TreeViewItem>), typeof(TreeViewItem)}, null);
            }
            s_SetChildParentReferences.Invoke(null, new object[] { m_Items, root });
            #endif
        }

        internal static bool IsEditorValid(Editor e)
        {
            #if USE_SEARCH_MODULE
            return e && e.serializedObject != null && e.serializedObject.isValid;
            #else
            return e && e.serializedObject != null &&
                (bool)typeof(SerializedObject).GetProperty("isValid", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(e.serializedObject);
            #endif
        }

        internal static int Wrap(int index, int n)
        {
            return ((index % n) + n) % n;
        }

        internal static void SetCurrentViewWidth(float width)
        {
            #if USE_SEARCH_MODULE
            EditorGUIUtility.currentViewWidth = width;
            #endif
        }

        internal static void SelectObject(UnityEngine.Object obj, bool ping = false)
        {
            if (!obj)
                return;
            Selection.activeObject = obj;
            if (ping)
            {
                EditorApplication.delayCall += () =>
                {
                    EditorWindow.FocusWindowIfItsOpen(GetProjectBrowserWindowType());
                    EditorApplication.delayCall += () => EditorGUIUtility.PingObject(obj);
                };
            }
        }

        internal static UnityEngine.Object SelectAssetFromPath(string path, bool ping = false)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            SelectObject(asset, ping);
            return asset;
        }

        internal static void SetTextEditorHasFocus(TextEditor editor, bool hasFocus)
        {
            #if USE_SEARCH_MODULE
            editor.m_HasFocus = hasFocus;
            #else
            if (s_TextEditor_m_HasFocus == null)
                s_TextEditor_m_HasFocus = typeof(TextEditor).GetField("m_HasFocus", BindingFlags.Instance | BindingFlags.NonPublic);
            s_TextEditor_m_HasFocus.SetValue(editor, hasFocus);
            #endif
        }

        internal static void FrameAssetFromPath(string path)
        {
            var asset = SelectAssetFromPath(path);
            if (asset != null)
            {
                EditorApplication.delayCall += () =>
                {
                    EditorWindow.FocusWindowIfItsOpen(GetProjectBrowserWindowType());
                    EditorApplication.delayCall += () => EditorGUIUtility.PingObject(asset);
                };
            }
            else
            {
                EditorUtility.RevealInFinder(path);
            }
        }

        internal static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }

        internal static void GetMenuItemDefaultShortcuts(List<string> outItemNames, List<string> outItemDefaultShortcuts)
        {
            #if USE_SEARCH_MODULE
            Menu.GetMenuItemDefaultShortcuts(outItemNames, outItemDefaultShortcuts);
            #else
            var method = typeof(Menu).GetMethod("GetMenuItemDefaultShortcuts", BindingFlags.NonPublic | BindingFlags.Static);
            var arguments = new object[] { outItemNames, outItemDefaultShortcuts };
            method.Invoke(null, arguments);
            #endif
        }

        internal static string FormatProviderList(IEnumerable<SearchProvider> providers, bool fullTimingInfo = false, bool showFetchTime = true)
        {
            return string.Join(fullTimingInfo ? "\r\n" : ", ", providers.Select(p =>
            {
                var fetchTime = p.fetchTime;
                if (fullTimingInfo)
                    return $"{p.name} ({fetchTime:0.#} ms, Enable: {p.enableTime:0.#} ms, Init: {p.loadTime:0.#} ms)";

                var avgTimeLabel = String.Empty;
                if (showFetchTime && fetchTime > 9.99)
                    avgTimeLabel = $" ({fetchTime:#} ms)";
                return $"<b>{p.name}</b>{avgTimeLabel}";
            }));
        }

        internal static string FormatBytes(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return $"{Math.Sign(byteCount) * num} {suf[place]}";
        }

        internal static string ToGuid(string assetPath)
        {
            string metaFile = $"{assetPath}.meta";
            if (!File.Exists(metaFile))
                return null;

            string line;
            using (var file = new StreamReader(metaFile))
            {
                while ((line = file.ReadLine()) != null)
                {
                    if (!line.StartsWith("guid:", StringComparison.Ordinal))
                        continue;
                    return line.Substring(6);
                }
            }

            return null;
        }

        internal static Rect GetEditorMainWindowPos()
        {
            #if USE_SEARCH_MODULE
            var windows = Resources.FindObjectsOfTypeAll<ContainerWindow>();
            foreach (var win in windows)
            {
                if (win.showMode == ShowMode.MainWindow)
                    return win.position;
            }

            return new Rect(0, 0, 800, 600);
            #else
            if (s_MainWindow == null)
            {
                var containerWinType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ContainerWindow");
                if (containerWinType == null)
                    throw new MissingMemberException("Can't find internal type ContainerWindow. Maybe something has changed inside Unity");
                var showModeField = containerWinType.GetField("m_ShowMode", BindingFlags.NonPublic | BindingFlags.Instance);
                if (showModeField == null)
                    throw new MissingFieldException("Can't find internal fields 'm_ShowMode'. Maybe something has changed inside Unity");
                var windows = Resources.FindObjectsOfTypeAll(containerWinType);
                foreach (var win in windows)
                {
                    var showMode = (int)showModeField.GetValue(win);
                    if (showMode == 4) // main window
                    {
                        s_MainWindow = win;
                        break;
                    }
                }
            }

            if (s_MainWindow == null)
                return new Rect(0, 0, 800, 600);

            var positionProperty = s_MainWindow.GetType().GetProperty("position", BindingFlags.Public | BindingFlags.Instance);
            if (positionProperty == null)
                throw new MissingFieldException("Can't find internal fields 'position'. Maybe something has changed inside Unity.");
            return (Rect)positionProperty.GetValue(s_MainWindow, null);
            #endif
        }

        internal static Rect GetCenteredWindowPosition(Rect parentWindowPosition, Vector2 size)
        {
            var pos = new Rect
            {
                x = 0, y = 0,
                width = Mathf.Min(size.x, parentWindowPosition.width * 0.90f),
                height = Mathf.Min(size.y, parentWindowPosition.height * 0.90f)
            };
            var w = (parentWindowPosition.width - pos.width) * 0.5f;
            var h = (parentWindowPosition.height - pos.height) * 0.5f;
            pos.x = parentWindowPosition.x + w;
            pos.y = parentWindowPosition.y + h;
            return pos;
        }

        internal static Type GetProjectBrowserWindowType()
        {
            #if USE_SEARCH_MODULE
            return typeof(ProjectBrowser);
            #else
            return GetAllEditorWindowTypes().FirstOrDefault(t => t.Name == "ProjectBrowser");
            #endif
        }

        internal static Rect GetMainWindowCenteredPosition(Vector2 size)
        {
            var mainWindowRect = GetEditorMainWindowPos();
            return GetCenteredWindowPosition(mainWindowRect, size);
        }

        internal static void ShowDropDown(this EditorWindow window, Vector2 size)
        {
            window.maxSize = window.minSize = size;
            window.position = GetMainWindowCenteredPosition(size);
            window.ShowPopup();

            #if USE_SEARCH_MODULE
            var parentView = window.m_Parent;
            parentView.AddToAuxWindowList();
            parentView.window.m_DontSaveToLayout = true;
            #else
            Assembly assembly = typeof(EditorWindow).Assembly;

            var editorWindowType = typeof(EditorWindow);
            var hostViewType = assembly.GetType("UnityEditor.HostView");
            var containerWindowType = assembly.GetType("UnityEditor.ContainerWindow");

            var parentViewField = editorWindowType.GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
            var parentViewValue = parentViewField.GetValue(window);

            hostViewType.InvokeMember("AddToAuxWindowList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod, null, parentViewValue, null);

            // Dropdown windows should not be saved to layout
            var containerWindowProperty = hostViewType.GetProperty("window", BindingFlags.Instance | BindingFlags.Public);
            var parentContainerWindowValue = containerWindowProperty.GetValue(parentViewValue);
            var dontSaveToLayoutField = containerWindowType.GetField("m_DontSaveToLayout", BindingFlags.Instance | BindingFlags.NonPublic);
            dontSaveToLayoutField.SetValue(parentContainerWindowValue, true);
            UnityEngine.Debug.Assert((bool)dontSaveToLayoutField.GetValue(parentContainerWindowValue));
            #endif
        }

        internal static string JsonSerialize(object obj)
        {
            #if USE_SEARCH_MODULE
            return Json.Serialize(obj);
            #else
            var assembly = typeof(Selection).Assembly;
            var managerType = assembly.GetTypes().First(t => t.Name == "Json");
            var method = managerType.GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static);
            var jsonString = "";
            var arguments = new object[] { obj, false, "  " };
            jsonString = method.Invoke(null, arguments) as string;
            return jsonString;
            #endif
        }

        internal static object JsonDeserialize(string json)
        {
            #if USE_SEARCH_MODULE
            return Json.Deserialize(json);
            #else
            Assembly assembly = typeof(Selection).Assembly;
            var managerType = assembly.GetTypes().First(t => t.Name == "Json");
            var method = managerType.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static);
            var arguments = new object[] { json };
            return method.Invoke(null, arguments);
            #endif
        }

        internal static int GetNumCharactersThatFitWithinWidth(GUIStyle style, string text, float width)
        {
            #if USE_SEARCH_MODULE
            return style.GetNumCharactersThatFitWithinWidth(text, width);
            #else
            if (s_GetNumCharactersThatFitWithinWidthMethod == null)
            {
                var kType = typeof(GUIStyle);
                s_GetNumCharactersThatFitWithinWidthMethod = kType.GetMethod("Internal_GetNumCharactersThatFitWithinWidth", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            var arguments = new object[] { text, width };
            return (int)s_GetNumCharactersThatFitWithinWidthMethod.Invoke(style, arguments);
            #endif
        }

        internal static string GetNextWord(string src, ref int index)
        {
            // Skip potential white space BEFORE the actual word we are extracting
            for (; index < src.Length; ++index)
            {
                if (!char.IsWhiteSpace(src[index]))
                {
                    break;
                }
            }

            var startIndex = index;
            for (; index < src.Length; ++index)
            {
                if (char.IsWhiteSpace(src[index]))
                {
                    break;
                }
            }

            return src.Substring(startIndex, index - startIndex);
        }

        internal static int LevenshteinDistance<T>(IEnumerable<T> lhs, IEnumerable<T> rhs) where T : System.IEquatable<T>
        {
            if (lhs == null) throw new System.ArgumentNullException("lhs");
            if (rhs == null) throw new System.ArgumentNullException("rhs");

            IList<T> first = lhs as IList<T> ?? new List<T>(lhs);
            IList<T> second = rhs as IList<T> ?? new List<T>(rhs);

            int n = first.Count, m = second.Count;
            if (n == 0) return m;
            if (m == 0) return n;

            int curRow = 0, nextRow = 1;
            int[][] rows = { new int[m + 1], new int[m + 1] };
            for (int j = 0; j <= m; ++j)
                rows[curRow][j] = j;

            for (int i = 1; i <= n; ++i)
            {
                rows[nextRow][0] = i;

                for (int j = 1; j <= m; ++j)
                {
                    int dist1 = rows[curRow][j] + 1;
                    int dist2 = rows[nextRow][j - 1] + 1;
                    int dist3 = rows[curRow][j - 1] +
                        (first[i - 1].Equals(second[j - 1]) ? 0 : 1);

                    rows[nextRow][j] = System.Math.Min(dist1, System.Math.Min(dist2, dist3));
                }
                if (curRow == 0)
                {
                    curRow = 1;
                    nextRow = 0;
                }
                else
                {
                    curRow = 0;
                    nextRow = 1;
                }
            }
            return rows[curRow][m];
        }

        internal static int LevenshteinDistance(string lhs, string rhs, bool caseSensitive = true)
        {
            if (!caseSensitive)
            {
                lhs = lhs.ToLower();
                rhs = rhs.ToLower();
            }
            char[] first = lhs.ToCharArray();
            char[] second = rhs.ToCharArray();
            return LevenshteinDistance(first, second);
        }

        internal static Texture2D GetThumbnailForGameObject(GameObject go)
        {
            var thumbnail = PrefabUtility.GetIconForGameObject(go);
            if (thumbnail)
                return thumbnail;
            return EditorGUIUtility.ObjectContent(go, go.GetType()).image as Texture2D;
        }

        internal static Texture2D FindTextureForType(Type type)
        {
            if (type == null)
                return null;
            #if USE_SEARCH_MODULE
            return EditorGUIUtility.FindTexture(type);
            #else
            if (s_FindTextureMethod == null)
            {
                var t = typeof(EditorGUIUtility);
                s_FindTextureMethod = t.GetMethod("FindTexture", BindingFlags.NonPublic | BindingFlags.Static);
            }
            return (Texture2D)s_FindTextureMethod.Invoke(null, new object[] {type});
            #endif
        }

        internal static Texture2D GetIconForObject(UnityEngine.Object obj)
        {
            #if USE_SEARCH_MODULE
            return EditorGUIUtility.GetIconForObject(obj);
            #else
            if (s_GetIconForObject == null)
            {
                var t = typeof(EditorGUIUtility);
                s_GetIconForObject = t.GetMethod("GetIconForObject", BindingFlags.NonPublic | BindingFlags.Static);
            }
            return (Texture2D)s_GetIconForObject.Invoke(null, new object[] { obj });
            #endif
        }

        internal static void PingAsset(string assetPath)
        {
            #if USE_SEARCH_MODULE
            EditorGUIUtility.PingObject(AssetDatabase.GetMainAssetInstanceID(assetPath));
            #else
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                if (!(asset is GameObject))
                    Resources.UnloadAsset(asset);
            }
            #endif
        }

        internal static T ConvertValue<T>(string value)
        {
            var type = typeof(T);
            var converter = TypeDescriptor.GetConverter(type);
            if (converter.IsValid(value))
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                return (T)converter.ConvertFromString(null, CultureInfo.InvariantCulture, value);
            }
            return (T)Activator.CreateInstance(type);
        }

        internal static bool TryConvertValue<T>(string value, out T convertedValue)
        {
            var type = typeof(T);
            var converter = TypeDescriptor.GetConverter(type);
            try
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                convertedValue = (T)converter.ConvertFromString(null, CultureInfo.InvariantCulture, value);
                return true;
            }
            catch
            {
                convertedValue = default;
                return false;
            }
        }

        internal static void StartDrag(UnityEngine.Object[] objects, string label = null)
        {
            s_LastDraggedObjects = objects;
            if (s_LastDraggedObjects == null)
                return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = s_LastDraggedObjects;
            DragAndDrop.StartDrag(label);
        }

        internal static void StartDrag(UnityEngine.Object[] objects, string[] paths, string label = null)
        {
            s_LastDraggedObjects = objects;
            if (paths == null || paths.Length == 0)
                return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = s_LastDraggedObjects;
            DragAndDrop.paths = paths;
            DragAndDrop.StartDrag(label);
        }

        internal static Type GetTypeFromName(string typeName)
        {
            return TypeCache.GetTypesDerivedFrom<UnityEngine.Object>().FirstOrDefault(t => t.Name == typeName) ?? typeof(UnityEngine.Object);
        }

        internal static string StripHTML(string input)
        {
            return Regex.Replace(input, "<.*?>", String.Empty);
        }

        internal static UnityEngine.Object ToObject(SearchItem item, Type filterType)
        {
            if (item == null || item.provider == null)
                return null;
            return item.provider.toObject?.Invoke(item, filterType);
        }

        internal static bool IsFocusedWindowTypeName(string focusWindowName)
        {
            return EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().ToString().EndsWith("." + focusWindowName);
        }

        internal static string CleanString(string s)
        {
            var sb = s.ToCharArray();
            for (int c = 0; c < s.Length; ++c)
            {
                var ch = s[c];
                if (ch == '_' || ch == '.' || ch == '-' || ch == '/')
                    sb[c] = ' ';
            }
            return new string(sb).ToLowerInvariant();
        }

        internal static string CleanPath(string path)
        {
            return path.Replace("\\", "/");
        }

        internal static bool IsPathUnderProject(string path)
        {
            if (!Path.IsPathRooted(path))
            {
                path = new FileInfo(path).FullName;
            }
            path = CleanPath(path);
            return rootDescriptors.Any(desc => path.StartsWith(desc.absPath));
        }

        internal static string GetPathUnderProject(string path)
        {
            path = CleanPath(path);
            if (!Path.IsPathRooted(path))
            {
                return path;
            }

            foreach (var desc in rootDescriptors)
            {
                if (path.StartsWith(desc.absPath))
                {
                    var relativePath = path.Substring(desc.absPath.Length);
                    return desc.root + relativePath;
                }
            }

            return path;
        }

        internal static Texture2D GetSceneObjectPreview(GameObject obj, Vector2 previewSize, FetchPreviewOptions options, Texture2D defaultThumbnail)
        {
            var sr = obj.GetComponent<SpriteRenderer>();
            if (sr && sr.sprite && sr.sprite.texture)
                return sr.sprite.texture;

            #if PACKAGE_UGUI
            var uii = obj.GetComponent<UnityEngine.UI.Image>();
            if (uii && uii.mainTexture is Texture2D uiit)
                return uiit;
            #endif

            if (!options.HasAny(FetchPreviewOptions.Large))
            {
                var preview = AssetPreview.GetAssetPreview(obj);
                if (preview)
                    return preview;
            }

            var assetPath = SearchUtils.GetHierarchyAssetPath(obj, true);
            if (string.IsNullOrEmpty(assetPath))
                return AssetPreview.GetAssetPreview(obj) ?? defaultThumbnail;
            return GetAssetPreviewFromPath(assetPath, previewSize, options);
        }

        internal static bool TryGetNumber(object value, out double number)
        {
            if (value == null)
            {
                number = double.NaN;
                return false;
            }

            if (value is string s)
            {
                if (TryParse(s, out number))
                    return true;
                else
                {
                    number = double.NaN;
                    return false;
                }
            }

            if (value.GetType().IsPrimitive || value is decimal)
            {
                number = Convert.ToDouble(value);
                return true;
            }

            return TryParse(Convert.ToString(value), out number);
        }

        internal static bool IsRunningTests()
        {
            return !InternalEditorUtility.isHumanControllingUs || InternalEditorUtility.inBatchMode;
        }

        internal static bool IsMainProcess()
        {
            if (AssetDatabaseAPI.IsAssetImportWorkerProcess())
                return false;

            #if USE_SEARCH_MODULE
            if (EditorUtility.isInSafeMode)
                return false;

            if (MPE.ProcessService.level != MPE.ProcessLevel.Main)
                return false;
            #else
            if (MPE.ProcessService.level != MPE.ProcessLevel.Master)
                return false;
            #endif

            return true;
        }

        internal static event EditorApplication.CallbackFunction tick
        {
            add
            {
                #if USE_SEARCH_MODULE
                EditorApplication.tick -= value;
                EditorApplication.tick += value;
                #else
                EditorApplication.update -= value;
                EditorApplication.update += value;
                #endif
            }
            remove
            {
                #if USE_SEARCH_MODULE
                EditorApplication.tick -= value;
                #else
                EditorApplication.update -= value;
                #endif
            }
        }

        internal static Action CallDelayed(EditorApplication.CallbackFunction callback, double seconds = 0)
        {
            #if USE_SEARCH_MODULE
            return EditorApplication.CallDelayed(callback, seconds);
            #else
            if (s_CallDelayed == null)
            {
                var type = typeof(EditorApplication);
                s_CallDelayed = type.GetMethod("CallDelayed", BindingFlags.NonPublic | BindingFlags.Static);
                if (s_CallDelayed == null)
                    throw new Exception("Failed to resolved CallDelayed method");
            }
            object[] parameters = new object[] { callback, seconds };
            return s_CallDelayed.Invoke(null, parameters) as Action;
            #endif
        }

        internal static void SetFirstInspectedEditor(Editor editor)
        {
            #if USE_SEARCH_MODULE
            editor.firstInspectedEditor = true;
            #else
            var firstInspectedEditorProperty = editor.GetType().GetProperty("firstInspectedEditor", BindingFlags.NonPublic | BindingFlags.Instance);
            firstInspectedEditorProperty.SetValue(editor, true);
            #endif
        }

        internal static GUIStyle FromUSS(string name)
        {
            #if USE_SEARCH_MODULE
            return GUIStyleExtensions.FromUSS(GUIStyle.none, name);
            #else
            return FromUSS(GUIStyle.none, name);
            #endif
        }

        internal static GUIStyle FromUSS(GUIStyle @base, string name)
        {
            #if USE_SEARCH_MODULE
            return GUIStyleExtensions.FromUSS(@base, name);
            #else
            if (s_FromUSSMethod == null)
            {
                Assembly assembly = typeof(UnityEditor.EditorStyles).Assembly;
                var type = assembly.GetTypes().First(t => t.FullName == "UnityEditor.StyleSheets.GUIStyleExtensions");
                s_FromUSSMethod = type.GetMethod("FromUSS", BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(GUIStyle), typeof(string), typeof(string), typeof(GUISkin) }, null);
            }
            string ussInPlaceStyleOverride = null;
            GUISkin srcSkin = null;
            return (GUIStyle)s_FromUSSMethod.Invoke(null, new object[] { @base, name, ussInPlaceStyleOverride, srcSkin });
            #endif
        }

        internal static bool HasCurrentWindowKeyFocus()
        {
            #if USE_SEARCH_MODULE
            return EditorGUIUtility.HasCurrentWindowKeyFocus();
            #else
            if (s_HasCurrentWindowKeyFocusMethod == null)
            {
                var type = typeof(EditorGUIUtility);
                s_HasCurrentWindowKeyFocusMethod = type.GetMethod("HasCurrentWindowKeyFocus", BindingFlags.NonPublic | BindingFlags.Static);
                UnityEngine.Debug.Assert(s_HasCurrentWindowKeyFocusMethod != null);
            }
            return (bool)s_HasCurrentWindowKeyFocusMethod.Invoke(null, null);
            #endif
        }

        internal static void AddStyleSheet(VisualElement rootVisualElement, string ussFileName)
        {
            #if USE_SEARCH_MODULE
            rootVisualElement.AddStyleSheetPath($"StyleSheets/QuickSearch/{ussFileName}");
            #else
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(GetPackagePath($"Editor/StyleSheets/{ussFileName}"));
            rootVisualElement.styleSheets.Add(styleSheet);
            #endif
        }

        #if USE_SEARCH_MODULE
        internal static InspectorWindowUtils.LayoutGroupChecker LayoutGroupChecker()
        {
            return new InspectorWindowUtils.LayoutGroupChecker();
        }

        #else
        internal static InspectorWindowUtils_LayoutGroupChecker LayoutGroupChecker()
        {
            return new InspectorWindowUtils_LayoutGroupChecker();
        }

        #endif

        #if !USE_SEARCH_MODULE
        static object s_UnityConnectInstance = null;
        static Type s_CloudConfigUrlEnum = null;
        static object GetUnityConnectInstance()
        {
            if (s_UnityConnectInstance != null)
                return s_UnityConnectInstance;
            var assembly = typeof(Connect.UnityOAuth).Assembly;
            var managerType = assembly.GetTypes().First(t => t.Name == "UnityConnect");
            var instanceAccessor = managerType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
            s_UnityConnectInstance = instanceAccessor.GetValue(null);
            s_CloudConfigUrlEnum = assembly.GetTypes().First(t => t.Name == "CloudConfigUrl");
            return s_UnityConnectInstance;
        }

        #endif

        internal static string GetConnectAccessToken()
        {
            #if USE_SEARCH_MODULE
            return UnityConnect.instance.GetAccessToken();
            #else
            var instance = GetUnityConnectInstance();
            var method = instance.GetType().GetMethod("GetAccessToken");
            return (string)method.Invoke(instance, null);
            #endif
        }

        internal static string GetPackagesKey()
        {
            #if USE_SEARCH_MODULE
            return UnityConnect.instance.GetConfigurationURL(CloudConfigUrl.CloudPackagesKey);
            #else
            var instance = GetUnityConnectInstance();
            var getConfigUrl = instance.GetType().GetMethod("GetConfigurationURL");
            var packmanKey = s_CloudConfigUrlEnum.GetEnumValues().GetValue(12);
            var packageKey = (string)getConfigUrl.Invoke(instance, new[] { packmanKey });
            return packageKey;
            #endif
        }

        internal static void OpenPackageManager(string packageName)
        {
            #if USE_SEARCH_MODULE
            PackageManager.UI.PackageManagerWindow.SelectPackageAndFilterStatic(packageName, PackageManager.UI.Internal.PackageFilterTab.AssetStore);
            #else
            if (s_OpenPackageManager == null)
            {
                // UnityEditor.PackageManager.UI.PackageManagerWindow.SelectPackageAndFilter
                var assembly = typeof(PackageManager.UI.Window).Assembly;
                var managerType = assembly.GetTypes().First(t => t.Name == "PackageManagerWindow");
                var methodInfo = managerType.GetMethod("SelectPackageAndFilter", BindingFlags.Static | BindingFlags.NonPublic);
                var cloudConfigUrlEnum = assembly.GetTypes().First(t => t.Name == "PackageFilterTab");
                var assetStoreTab = cloudConfigUrlEnum.GetEnumValues().GetValue(3);
                s_OpenPackageManager = pkg => methodInfo.Invoke(null, new[] { pkg, assetStoreTab, false, "" });
            }

            s_OpenPackageManager(packageName);
            #endif
        }

        internal static char FastToLower(char c)
        {
            // ASCII non-letter characters and
            // lower case letters.
            if (c < 'A' || (c > 'Z' && c <= 'z'))
            {
                return c;
            }

            if (c >= 'A' && c <= 'Z')
            {
                return (char)(c + 32);
            }

            return Char.ToLower(c, CultureInfo.InvariantCulture);
        }

        internal static string FastToLower(string str)
        {
            int length = str.Length;

            var chars = new char[length];

            for (int i = 0; i < length; ++i)
            {
                chars[i] = FastToLower(str[i]);
            }

            return new string(chars);
        }

        internal static string FormatCount(ulong count)
        {
            if (count < 1000U)
                return count.ToString(CultureInfo.InvariantCulture.NumberFormat);
            if (count < 1000000U)
                return (count / 1000U).ToString(CultureInfo.InvariantCulture.NumberFormat) + "k";
            if (count < 1000000000U)
                return (count / 1000000U).ToString(CultureInfo.InvariantCulture.NumberFormat) + "M";
            return (count / 1000000000U).ToString(CultureInfo.InvariantCulture.NumberFormat) + "G";
        }

        internal static bool TryAdd<K, V>(this Dictionary<K, V> dict, K key, V value)
        {
            if (!dict.ContainsKey(key))
            {
                dict.Add(key, value);
                return true;
            }

            return false;
        }

        internal static string[] GetAssetRootFolders()
        {
            #if USE_SEARCH_MODULE
            return AssetDatabase.GetAssetRootFolders();
            #else
            return new[] { "Assets" };
            #endif
        }

        internal static string ToString(in Vector3 v)
        {
            return $"({FormatFloatString(v.x)},{FormatFloatString(v.y)},{FormatFloatString(v.z)})";
        }

        internal static string ToString(in Vector4 v, int dim)
        {
            switch (dim)
            {
                case 2: return $"({FormatFloatString(v.x)},{FormatFloatString(v.y)})";
                case 3: return $"({FormatFloatString(v.x)},{FormatFloatString(v.y)},{FormatFloatString(v.z)})";
                case 4: return $"({FormatFloatString(v.x)},{FormatFloatString(v.y)},{FormatFloatString(v.z)},{FormatFloatString(v.w)})";
            }
            return null;
        }

        internal static string ToString(in Vector2Int v)
        {
            return $"({(int.MaxValue == v.x ? string.Empty : v.x.ToString())},{(int.MaxValue == v.y ? string.Empty : v.y.ToString())})";
        }

        internal static string ToString(in Vector3Int v)
        {
            return $"({(int.MaxValue == v.x ? string.Empty : v.x.ToString())},{(int.MaxValue == v.y ? string.Empty : v.y.ToString())},{(int.MaxValue == v.z ? string.Empty : v.z.ToString())})";
        }

        internal static string FormatFloatString(in float f)
        {
            if (float.IsNaN(f))
                return string.Empty;
            return f.ToString(CultureInfo.InvariantCulture);
        }

        internal static bool TryParseVectorValue(in object value, out Vector4 vc, out int dim)
        {
            dim = 0;
            vc = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
            if (!(value is string arg))
                return false;
            if (arg.Length < 3 || arg[0] != '(' || arg[arg.Length - 1] != ')' || arg.IndexOf(',') == -1)
                return false;
            var ves = arg.Substring(1, arg.Length - 2);
            var values = ves.Split(',');
            if (values.Length < 2 || values.Length > 4)
                return false;

            dim = values.Length;
            if (values.Length >= 1 && values[0].Length > 0 && (values[0].Length > 1 || values[0][0] != '-') && TryParse<float>(values[0], out var f))
                vc.x = f;
            if (values.Length >= 2 && values[1].Length > 0 && (values[1].Length > 1 || values[1][0] != '-') && TryParse(values[1], out f))
                vc.y = f;
            if (values.Length >= 3 && values[2].Length > 0 && (values[2].Length > 1 || values[2][0] != '-') && TryParse(values[2], out f))
                vc.z = f;
            if (values.Length >= 4 && values[3].Length > 0 && (values[3].Length > 1 || values[3][0] != '-') && TryParse(values[3], out f))
                vc.w = f;

            return true;
        }

        public static bool TryParse<T>(string expression, out T result)
        {
            expression = expression.Replace(',', '.');
            expression = expression.TrimEnd('f');
            expression = expression.ToLowerInvariant();

            bool success = false;
            result = default;
            if (typeof(T) == typeof(float))
            {
                if (expression == "pi")
                {
                    success = true;
                    result = (T)(object)(float)Math.PI;
                }
                else
                {
                    success = float.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out var temp);
                    result = (T)(object)temp;
                }
            }
            else if (typeof(T) == typeof(int))
            {
                success = int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture.NumberFormat, out var temp);
                result = (T)(object)temp;
            }
            else if (typeof(T) == typeof(uint))
            {
                success = uint.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture.NumberFormat, out var temp);
                result = (T)(object)temp;
            }
            else if (typeof(T) == typeof(double))
            {
                if (expression == "pi")
                {
                    success = true;
                    result = (T)(object)Math.PI;
                }
                else
                {
                    success = double.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out var temp);
                    result = (T)(object)temp;
                }
            }
            else if (typeof(T) == typeof(long))
            {
                success = long.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture.NumberFormat, out var temp);
                result = (T)(object)temp;
            }
            else if (typeof(T) == typeof(ulong))
            {
                success = ulong.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture.NumberFormat, out var temp);
                result = (T)(object)temp;
            }
            return success;
        }

        #if UNITY_EDITOR_WIN
        private const string k_RevealInFinderLabel = "Show in Explorer";
        #elif UNITY_EDITOR_OSX
        private const string k_RevealInFinderLabel = "Reveal in Finder";
        #else
        private const string k_RevealInFinderLabel = "Open Containing Folder";
        #endif
        internal static string GetRevealInFinderLabel() { return k_RevealInFinderLabel; }

        public static string TrimText(string text)
        {
            return text.Trim().Replace("\n", " ");
        }

        public static string TrimText(string text, int maxLength)
        {
            text = TrimText(text);
            if (text.Length > maxLength)
            {
                text = Utils.StripHTML(text);
                text = text.Substring(0, Math.Min(text.Length, maxLength) - 1) + "\u2026";
            }
            return text;
        }

        static readonly GUILayoutOption[] s_PanelViewLayoutOptions = new[] { GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(false) };
        public static Vector2 BeginPanelView(Vector2 scrollPosition, GUIStyle panelStyle)
        {
            #if USE_SEARCH_MODULE
            var verticalScrollbar = Styles.scrollbar;
            GUIScrollGroup g = (GUIScrollGroup)GUILayoutUtility.BeginLayoutGroup(panelStyle, null, typeof(GUIScrollGroup));
            if (Event.current.type == EventType.Layout)
            {
                g.resetCoords = true;
                g.isVertical = true;
                g.stretchWidth = 0;
                g.stretchHeight = 1;
                g.consideredForMargin = false;
                g.verticalScrollbar = verticalScrollbar;
                g.horizontalScrollbar = GUIStyle.none;
                g.ApplyOptions(s_PanelViewLayoutOptions);
            }
            return EditorGUIInternal.DoBeginScrollViewForward(g.rect, scrollPosition,
                new Rect(0, 0, g.clientWidth - Styles.scrollbarWidth, g.clientHeight), false, false,
                GUIStyle.none, verticalScrollbar, panelStyle);
            #else
            return GUILayout.BeginScrollView(scrollPosition, false, false, GUIStyle.none, Styles.scrollbar, panelStyle, s_PanelViewLayoutOptions);
            #endif
        }

        public static void EndPanelView()
        {
            EditorGUILayout.EndScrollView();
        }

        public static ulong GetHashCode64(this string strText)
        {
            if (string.IsNullOrEmpty(strText))
                return 0;
            var s1 = (ulong)strText.Substring(0, strText.Length / 2).GetHashCode();
            var s2 = (ulong)strText.Substring(strText.Length / 2).GetHashCode();
            return s1 << 32 | s2;
        }

        public static string RemoveInvalidCharsFromPath(string path, char repl = '/')
        {
            var invalidChars = Path.GetInvalidPathChars();
            foreach (var c in invalidChars)
                path = path.Replace(c, repl);
            return path;
        }

        public static Rect BeginHorizontal(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            #if USE_SEARCH_MODULE
            return EditorGUILayout.BeginHorizontal(content, style, options);
            #else
            if (s_BeginHorizontal == null)
            {
                var type = typeof(EditorGUILayout);
                s_BeginHorizontal = type.GetMethod(nameof(BeginHorizontal), BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(GUIContent), typeof(GUIStyle), typeof(GUILayoutOption[]) }, null);
            }
            return (Rect)s_BeginHorizontal.Invoke(null, new object[] { content, style, options });
            #endif
        }

        public static bool IsGUIClipEnabled()
        {
            #if USE_SEARCH_MODULE
            return GUIClip.enabled;
            #else
            if (s_IsGUIClipEnabled == null)
            {
                var assembly = typeof(GUIUtility).Assembly;
                var type = assembly.GetTypes().First(t => t.Name == "GUIClip");
                var pi = type.GetProperty("enabled", BindingFlags.NonPublic | BindingFlags.Static);
                s_IsGUIClipEnabled = pi.GetMethod;
            }
            return (bool)s_IsGUIClipEnabled.Invoke(null, null);
            #endif
        }

        public static Rect Unclip(in Rect r)
        {
            #if USE_SEARCH_MODULE
            return GUIClip.Unclip(r);
            #else
            if (s_Unclip == null)
            {
                var assembly = typeof(GUIUtility).Assembly;
                var type = assembly.GetTypes().First(t => t.Name == "GUIClip");
                s_Unclip = type.GetMethod("Unclip_Rect", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Rect) }, null);
            }
            return (Rect)s_Unclip.Invoke(null, new object[] { r });
            #endif
        }

        public static MonoScript MonoScriptFromScriptedObject(UnityEngine.Object obj)
        {
            #if USE_SEARCH_MODULE
            return MonoScript.FromScriptedObject(obj);
            #else
            if (s_MonoScriptFromScriptedObject == null)
            {
                var type = typeof(MonoScript);
                s_MonoScriptFromScriptedObject = type.GetMethod("FromScriptedObject", BindingFlags.NonPublic | BindingFlags.Static);
            }
            return s_MonoScriptFromScriptedObject.Invoke(null, new object[] {obj}) as MonoScript;
            #endif
        }

        public static bool SerializedPropertyIsScript(SerializedProperty property)
        {
            #if USE_SEARCH_MODULE
            return property.isScript;
            #else
            if (s_SerializedPropertyIsScript == null)
            {
                var type = typeof(SerializedProperty);
                var pi = type.GetProperty("isScript", BindingFlags.NonPublic | BindingFlags.Instance);
                s_SerializedPropertyIsScript = pi.GetMethod;
            }
            return s_SerializedPropertyIsScript.Invoke(property, null) as MonoScript;
            #endif
        }

        public static string SerializedPropertyObjectReferenceStringValue(SerializedProperty property)
        {
            #if USE_SEARCH_MODULE
            return property.objectReferenceStringValue;
            #else
            if (s_SerializedPropertyObjectReferenceStringValue == null)
            {
                var type = typeof(SerializedProperty);
                var pi = type.GetProperty("objectReferenceStringValue", BindingFlags.NonPublic | BindingFlags.Instance);
                s_SerializedPropertyObjectReferenceStringValue = pi.GetMethod;
            }
            return s_SerializedPropertyObjectReferenceStringValue.Invoke(property, null) as string;
            #endif
        }

        public static GUIContent ObjectContent(UnityEngine.Object obj, Type type, int instanceID)
        {
            #if USE_SEARCH_MODULE
            return EditorGUIUtility.ObjectContent(obj, type, instanceID);
            #else
            if (s_ObjectContent == null)
            {
                var classType = typeof(EditorGUIUtility);
                s_ObjectContent = classType.GetMethod("ObjectContent", BindingFlags.NonPublic | BindingFlags.Static);
            }
            return s_ObjectContent.Invoke(null, new object[] {obj, type, instanceID}) as GUIContent;
            #endif
        }

        public static bool IsCommandDelete(string commandName)
        {
            #if USE_SEARCH_MODULE
            return commandName == EventCommandNames.Delete || commandName == EventCommandNames.SoftDelete;
            #else
            if (string.IsNullOrEmpty(s_CommandDelete) || string.IsNullOrEmpty(s_CommandSoftDelete))
            {
                var assembly = typeof(GUIUtility).Assembly;
                var classType = assembly.GetTypes().First(t => t.Name == "EventCommandNames");
                var fi = classType.GetField("Delete", BindingFlags.Public | BindingFlags.Static);
                s_CommandDelete = (string)fi.GetRawConstantValue();
                fi = classType.GetField("SoftDelete", BindingFlags.Public | BindingFlags.Static);
                s_CommandSoftDelete = (string)fi.GetRawConstantValue();
            }
            return commandName == s_CommandDelete || commandName == s_CommandSoftDelete;
            #endif
        }

        public static void PopupWindowWithoutFocus(Rect position, PopupWindowContent windowContent)
        {
            #if USE_SEARCH_MODULE
            UnityEditor.PopupWindowWithoutFocus.Show(
                position,
                windowContent,
                new[] { UnityEditor.PopupLocation.Left, UnityEditor.PopupLocation.Below, UnityEditor.PopupLocation.Right });
            #else
            if (s_PopupWindowWithoutFocusTyped == null)
            {
                var assembly = typeof(EditorGUILayout).Assembly;
                var popupLocationType = assembly.GetTypes().First(t => t.Name == "PopupLocation");
                var thisClassType = typeof(Utils);
                var method = thisClassType.GetMethod("PopupWindowWithoutFocusTyped", BindingFlags.NonPublic | BindingFlags.Static);
                s_PopupWindowWithoutFocusTyped = method.MakeGenericMethod(new[] {popupLocationType});
            }

            s_PopupWindowWithoutFocusTyped.Invoke(null, new object[] { position, windowContent });
            #endif
        }

        public static void OpenPropertyEditor(UnityEngine.Object target)
        {
            #if USE_SEARCH_MODULE
            PropertyEditor.OpenPropertyEditor(target);
            #else
            if (s_OpenPropertyEditor == null)
            {
                var assembly = typeof(EditorWindow).Assembly;
                var type = assembly.GetTypes().First(t => t.Name == "PropertyEditor");
                s_OpenPropertyEditor = type.GetMethod("OpenPropertyEditor", BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(UnityEngine.Object), typeof(bool) }, null);
            }
            s_OpenPropertyEditor.Invoke(null, new object[] { target, true });
            #endif
        }

        public static bool MainActionKeyForControl(Event evt, int id)
        {
            #if USE_SEARCH_MODULE
            return evt.MainActionKeyForControl(id);
            #else
            if (s_MainActionKeyForControl == null)
            {
                var assembly = typeof(MathUtils).Assembly;
                var type = assembly.GetTypes().First(t => t.Name == "EditorExtensionMethods");
                s_MainActionKeyForControl = type.GetMethod("MainActionKeyForControl", BindingFlags.NonPublic | BindingFlags.Static);
            }
            return (bool)s_MainActionKeyForControl.Invoke(null, new object[] { evt, id });
            #endif
        }

        #if !USE_SEARCH_MODULE
        private static void PopupWindowWithoutFocusTyped<T>(Rect position, PopupWindowContent windowContent)
        {
            var popupLocationType = typeof(T);
            if (s_PopupWindowWithoutFocus == null)
            {
                var assembly = typeof(EditorGUILayout).Assembly;
                var type = assembly.GetTypes().First(t => t.Name == "PopupWindowWithoutFocus");
                s_PopupWindowWithoutFocus = type.GetMethod("Show", BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(Rect), typeof(PopupWindowContent), popupLocationType.MakeArrayType() }, null);
            }
            var enumValues = popupLocationType.GetFields().Where(fi => !fi.Name.Equals("value__")).ToDictionary(fi => fi.Name, fi => (T)fi.GetRawConstantValue());
            var popupLocationArray = new T[] { enumValues["Left"], enumValues["Below"], enumValues["Right"] };
            s_PopupWindowWithoutFocus.Invoke(null, new object[] { position, windowContent, popupLocationArray });
        }

        #endif

        public static bool IsNavigationKey(in Event evt)
        {
            if (!evt.isKey)
                return false;

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                    return true;
            }

            return false;
        }

        public static Texture2D LoadIcon(string name)
        {
            #if USE_SEARCH_MODULE
            return EditorGUIUtility.LoadIcon(name);
            #else
            if (s_LoadIconMethod == null)
            {
                var t = typeof(EditorGUIUtility);
                s_LoadIconMethod = t.GetMethod("LoadIcon", BindingFlags.NonPublic | BindingFlags.Static);
            }
            return (Texture2D)s_LoadIconMethod.Invoke(null, new object[] {name});
            #endif
        }

        public static ulong GetFileIDHint(in UnityEngine.Object obj)
        {
            #if USE_SEARCH_MODULE
            return Unsupported.GetFileIDHint(obj);
            #else
            if (s_GetFileIDHint == null)
            {
                var t = typeof(Unsupported);
                s_GetFileIDHint = t.GetMethod("GetFileIDHint", BindingFlags.NonPublic | BindingFlags.Static);
            }
            return (ulong)s_GetFileIDHint.Invoke(null, new object[] {obj});
            #endif
        }

        public static bool IsEditingTextField()
        {
            #if USE_SEARCH_MODULE
            return GUIUtility.textFieldInput || EditorGUI.IsEditingTextField();
            #else
            return EditorGUIUtility.editingTextField;
            #endif
        }

        static readonly Regex trimmer = new Regex(@"(\s\s+)|(\r\n|\r|\n)+");
        public static string Simplify(string text)
        {
            return trimmer.Replace(text, " ").Replace("\r\n", " ").Replace('\n', ' ').Trim();
        }

        public static void OpenGraphViewer(in string searchQuery)
        {
            #if USE_GRAPH_VIEWER
            var gv = EditorWindow.GetWindow<QueryGraphViewWindow>();
            gv.SetView(searchQuery, GraphType.Query);
            #endif
        }

        internal static void WriteTextFileToDisk(in string path, in string content)
        {
            #if USE_SEARCH_MODULE
            FileUtil.WriteTextFileToDisk(path, content);
            #else
            System.IO.File.WriteAllText(path, content);
            #endif
        }

        internal static bool ParseRx(string pattern, bool exact, out Regex rx)
        {
            try
            {
                rx = new Regex(!exact ? pattern : $"^{pattern}$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(k_MaxRegexTimeout));
            }
            catch (ArgumentException)
            {
                rx = null;
                return false;
            }

            return true;
        }

        internal static bool ParseGlob(string pattern, bool exact, out Regex rx)
        {
            try
            {
                pattern = Regex.Escape(RemoveDuplicateAdjacentCharacters(pattern, '*')).Replace(@"\*", ".*").Replace(@"\?", ".");
                rx = new Regex(!exact ? pattern : $"^{pattern}$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(k_MaxRegexTimeout));
            }
            catch (ArgumentException)
            {
                rx = null;
                return false;
            }

            return true;
        }

        static string RemoveDuplicateAdjacentCharacters(string pattern, char c)
        {
            for (int i = pattern.Length - 1; i >= 0; --i)
            {
                if (pattern[i] != c || i == 0)
                    continue;

                if (pattern[i - 1] == c)
                    pattern = pattern.Remove(i, 1);
            }

            return pattern;
        }

        internal static T GetAttribute<T>(this MethodInfo mi) where T : System.Attribute
        {
            var attrs = mi.GetCustomAttributes(typeof(T), false);
            if (attrs == null || attrs.Length == 0)
                return null;
            return attrs[0] as T;
        }

        internal static T GetAttribute<T>(this Type mi) where T : System.Attribute
        {
            var attrs = mi.GetCustomAttributes(typeof(T), false);
            if (attrs == null || attrs.Length == 0)
                return null;
            return attrs[0] as T;
        }

        internal static bool IsBuiltInResource(UnityEngine.Object obj)
        {
            var resPath = AssetDatabase.GetAssetPath(obj);
            return IsBuiltInResource(resPath);
        }

        internal static bool IsBuiltInResource(in string resPath)
        {
            return string.Equals(resPath, "Library/unity editor resources", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resPath, "resources/unity_builtin_extra", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resPath, "library/unity default resources", StringComparison.OrdinalIgnoreCase);
        }
    }

    static class SerializedPropertyExtension
    {
        readonly struct Cache : IEquatable<Cache>
        {
            readonly Type host;
            readonly string path;

            public Cache(Type host, string path)
            {
                this.host = host;
                this.path = path;
            }

            public bool Equals(Cache other)
            {
                return Equals(host, other.host) && string.Equals(path, other.path, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;
                return obj is Cache && Equals((Cache)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((host != null ? host.GetHashCode() : 0) * 397) ^ (path != null ? path.GetHashCode() : 0);
                }
            }
        }

        class MemberInfoCache
        {
            public MemberInfo fieldInfo;
            public Type type;
        }

        static Type s_NativePropertyAttributeType;
        static Dictionary<Cache, MemberInfoCache> s_MemberInfoFromPropertyPathCache = new Dictionary<Cache, MemberInfoCache>();

        public static Type GetManagedType(this SerializedProperty property)
        {
            var host = property.serializedObject?.targetObject?.GetType();
            if (host == null)
                return null;

            var path = property.propertyPath;
            var cache = new Cache(host, path);

            if (s_MemberInfoFromPropertyPathCache.TryGetValue(cache, out var infoCache))
                return infoCache?.type;

            const string arrayData = @"\.Array\.data\[[0-9]+\]";
            // we are looking for array element only when the path ends with Array.data[x]
            var lookingForArrayElement = Regex.IsMatch(path, arrayData + "$");
            // remove any Array.data[x] from the path because it is prevents cache searching.
            path = Regex.Replace(path, arrayData, ".___ArrayElement___");

            MemberInfo memberInfo = null;
            var type = host;
            string[] parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                string member = parts[i];
                string alternateName = null;
                if (member.StartsWith("m_", StringComparison.Ordinal))
                    alternateName = member.Substring(2);

                foreach (MemberInfo f in type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if ((f.MemberType & (MemberTypes.Property | MemberTypes.Field)) == 0)
                        continue;
                    var memberName = f.Name;
                    if (f is PropertyInfo pi)
                    {
                        if (!pi.CanRead)
                            continue;

                        #if USE_SEARCH_MODULE
                        s_NativePropertyAttributeType = typeof(UnityEngine.Bindings.NativePropertyAttribute);
                        #else
                        if (s_NativePropertyAttributeType == null)
                        {
                            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                            {
                                foreach (Type t in a.GetTypes())
                                {
                                    if (string.Equals(t.Name, "NativePropertyAttribute", StringComparison.Ordinal))
                                    {
                                        s_NativePropertyAttributeType = t;
                                        break;
                                    }
                                }

                                if (s_NativePropertyAttributeType != null)
                                    break;
                            }
                        }
                        #endif
                        var nattr = pi.GetCustomAttribute(s_NativePropertyAttributeType);
                        if (nattr != null)
                            memberName = s_NativePropertyAttributeType.GetProperty("Name").GetValue(nattr) as string ?? string.Empty;
                    }
                    if (string.Equals(member, memberName, StringComparison.Ordinal) ||
                        (alternateName != null && string.Equals(alternateName, memberName, StringComparison.OrdinalIgnoreCase)))
                    {
                        memberInfo = f;
                        break;
                    }
                }

                if (memberInfo is FieldInfo fi)
                    type = fi.FieldType;
                else if (memberInfo is PropertyInfo pi)
                    type = pi.PropertyType;
                else
                    continue;

                #if USE_SEARCH_MODULE
                // we want to get the element type if we are looking for Array.data[x]
                if (i < parts.Length - 1 && parts[i + 1] == "___ArrayElement___" && type.IsArrayOrList())
                {
                    i++; // skip the "___ArrayElement___" part
                    type = type.GetArrayOrListElementType();
                }
                #endif
            }

            if (memberInfo == null)
            {
                s_MemberInfoFromPropertyPathCache.Add(cache, null);
                return null;
            }

            #if USE_SEARCH_MODULE
            // we want to get the element type if we are looking for Array.data[x]
            if (lookingForArrayElement && type != null && type.IsArrayOrList())
                type = type.GetArrayOrListElementType();
            #endif

            s_MemberInfoFromPropertyPathCache.Add(cache, new MemberInfoCache
            {
                type = type,
                fieldInfo = memberInfo
            });
            return type;
        }
    }

    #if !USE_SEARCH_MODULE
    static class Hash128Ex
    {
        static PropertyInfo s_Property_u64_0;
        static PropertyInfo s_Property_u64_1;

        static Hash128Ex()
        {
            s_Property_u64_0 = typeof(Hash128).GetProperty("u64_0", BindingFlags.NonPublic | BindingFlags.Instance);
            s_Property_u64_1 = typeof(Hash128).GetProperty("u64_1", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static ulong Getu64_0(this Hash128 h) => (ulong)s_Property_u64_0.GetValue(h);
        public static ulong Getu64_1(this Hash128 h) => (ulong)s_Property_u64_1.GetValue(h);
    }
    #endif
}

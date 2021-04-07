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

#if USE_SEARCH_MODULE
using UnityEditor.Connect;
using UnityEditor.StyleSheets;
#endif

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("com.unity.quicksearch.tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("com.unity.search.extensions.editor")]

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
        private static MethodInfo s_GetIconForObject;
        private static MethodInfo s_CallDelayed;
        private static MethodInfo s_FromUSSMethod;
        private static MethodInfo s_HasCurrentWindowKeyFocusMethod;
        private static Action<string> s_OpenPackageManager;
        private static MethodInfo s_GetSourceAssetFileHash;
        private static PropertyInfo s_CurrentViewWidth;

        internal static string GetPackagePath(string relativePath)
        {
            return Path.Combine(packageFolderName, relativePath).Replace("\\", "/");
        }

        private static Type[] GetAllEditorWindowTypes()
        {
            return TypeCache.GetTypesDerivedFrom<EditorWindow>().ToArray();
        }

        #endif

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

            //UnityEngine.Debug.Log($"Generate preview for {path}, {previewSize}, {previewOptions}");

            if (previewOptions.HasAny(FetchPreviewOptions.Normal))
            {
                if (assetType == typeof(AudioClip))
                    return GetAssetThumbnailFromPath(path);

                var fi = new FileInfo(path);
                if (!fi.Exists)
                    return null;
                if (fi.Length > 16 * 1024 * 1024)
                    return GetAssetThumbnailFromPath(path);
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
            return new GUIContent(text, tooltip);
            #endif
        }

        internal static GUIContent GUIContentTemp(string text, Texture2D image)
        {
            #if USE_SEARCH_MODULE
            return GUIContent.Temp(text, image);
            #else
            return new GUIContent(text, image);
            #endif
        }

        internal static GUIContent GUIContentTemp(string text)
        {
            #if USE_SEARCH_MODULE
            return GUIContent.Temp(text);
            #else
            return new GUIContent(text);
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

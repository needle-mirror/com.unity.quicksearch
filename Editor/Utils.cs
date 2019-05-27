// #define QUICKSEARCH_DEBUG
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

#if QUICKSEARCH_DEBUG
using UnityEditorInternal;
#endif

namespace Unity.QuickSearch
{
    internal static class Utils
    {
        private static string[] _ignoredAssemblies =
        {
            "^UnityScript$", "^System$", "^mscorlib$", "^netstandard$",
            "^System\\..*", "^nunit\\..*", "^Microsoft\\..*", "^Mono\\..*", "^SyntaxTree\\..*"
        };

        private static Type[] GetAllEditorWindowTypes()
        {
            var result = new List<Type>();
            var AS = AppDomain.CurrentDomain.GetAssemblies();
            var editorWindow = typeof(EditorWindow);
            foreach (var A in AS)
            {
                var types = A.GetLoadableTypes();
                foreach (var T in types)
                {
                    if (T.IsSubclassOf(editorWindow))
                        result.Add(T);
                }
            }
            return result.ToArray();
        }

        internal static Type GetProjectBrowserWindowType()
        {
            return GetAllEditorWindowTypes().FirstOrDefault(t => t.Name == "ProjectBrowser");
        }

        internal static string GetNameFromPath(string path)
        {
            var lastSep = path.LastIndexOf('/');
            if (lastSep == -1)
                return path;

            return path.Substring(lastSep + 1);
        }

        public static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
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

        internal static Type[] GetAllDerivedTypes(this AppDomain aAppDomain, Type aType)
        {
            var result = new List<Type>();
            var assemblies = aAppDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetLoadableTypes();
                foreach (var type in types)
                {
                    if (type.IsSubclassOf(aType))
                        result.Add(type);
                }
            }
            return result.ToArray();
        }

        private static bool IsIgnoredAssembly(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            return _ignoredAssemblies.Any(candidate => Regex.IsMatch(name, candidate));
        }

        internal static MethodInfo[] GetAllStaticMethods(this AppDomain aAppDomain, bool showInternalAPIs)
        {
            var result = new List<MethodInfo>();
            var assemblies = aAppDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (IsIgnoredAssembly(assembly.GetName()))
                    continue;
                #if QUICKSEARCH_DEBUG
                var countBefore = result.Count;
                #endif
                var types = assembly.GetLoadableTypes();
                foreach (var type in types)
                {
                    var methods = type.GetMethods(BindingFlags.Static | (showInternalAPIs ? BindingFlags.Public | BindingFlags.NonPublic : BindingFlags.Public) | BindingFlags.DeclaredOnly);
                    foreach (var m in methods)
                    {
                        if (m.IsPrivate)
                            continue;

                        if (m.IsGenericMethod)
                            continue;

                        if (m.Name.Contains("Begin") || m.Name.Contains("End"))
                            continue;

                        if (m.GetParameters().Length == 0)
                            result.Add(m);
                    }
                }
                #if QUICKSEARCH_DEBUG
                Debug.Log($"{result.Count - countBefore} - {assembly.GetName()}");
                #endif
            }
            return result.ToArray();
        }

        internal static Rect GetEditorMainWindowPos()
        {
            var containerWinType = AppDomain.CurrentDomain.GetAllDerivedTypes(typeof(ScriptableObject)).FirstOrDefault(t => t.Name == "ContainerWindow");
            if (containerWinType == null)
                throw new MissingMemberException("Can't find internal type ContainerWindow. Maybe something has changed inside Unity");
            var showModeField = containerWinType.GetField("m_ShowMode", BindingFlags.NonPublic | BindingFlags.Instance);
            var positionProperty = containerWinType.GetProperty("position", BindingFlags.Public | BindingFlags.Instance);
            if (showModeField == null || positionProperty == null)
                throw new MissingFieldException("Can't find internal fields 'm_ShowMode' or 'position'. Maybe something has changed inside Unity");
            var windows = Resources.FindObjectsOfTypeAll(containerWinType);
            foreach (var win in windows)
            {
                var showMode = (int)showModeField.GetValue(win);
                if (showMode == 4) // main window
                {
                    var pos = (Rect)positionProperty.GetValue(win, null);
                    return pos;
                }
            }
            throw new NotSupportedException("Can't find internal main window. Maybe something has changed inside Unity");
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
        internal static IEnumerable<MethodInfo> GetAllMethodsWithAttribute<T>(BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        {
            Assembly assembly = typeof(Selection).Assembly;
            var managerType = assembly.GetTypes().First(t => t.Name == "EditorAssemblies");
            var method = managerType.GetMethod("Internal_GetAllMethodsWithAttribute", BindingFlags.NonPublic | BindingFlags.Static);
            var arguments = new object[] { typeof(T), bindingFlags };
            return ((method.Invoke(null, arguments) as object[]) ?? throw new InvalidOperationException()).Cast<MethodInfo>();
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

            Assembly assembly = typeof(EditorWindow).Assembly;

            var editorWindowType = typeof(EditorWindow);
            var hostViewType = assembly.GetType("UnityEditor.HostView");
            var containerWindowType = assembly.GetType("UnityEditor.ContainerWindow");

            var parentViewField = editorWindowType.GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
            var parentViewValue = parentViewField.GetValue(window);

            //m_Parent.AddToAuxWindowList();
            hostViewType.InvokeMember("AddToAuxWindowList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod, null, parentViewValue, null);

            // Dropdown windows should not be saved to layout
            //m_Parent.window.m_DontSaveToLayout = true;
            var containerWindowProperty = hostViewType.GetProperty("window", BindingFlags.Instance | BindingFlags.Public);
            var parentContainerWindowValue = containerWindowProperty.GetValue(parentViewValue);
            var dontSaveToLayoutField = containerWindowType.GetField("m_DontSaveToLayout", BindingFlags.Instance | BindingFlags.NonPublic);
            dontSaveToLayoutField.SetValue(parentContainerWindowValue, true);
            Debug.Assert((bool) dontSaveToLayoutField.GetValue(parentContainerWindowValue));
        }

        internal static string JsonSerialize(object obj)
        {
            var assembly = typeof(Selection).Assembly;
            var managerType = assembly.GetTypes().First(t => t.Name == "Json");
            var method = managerType.GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static);
            var jsonString = "";
            if (UnityVersion.IsVersionGreaterOrEqual(2019, 1, UnityVersion.ParseBuild("0a10")))
            {
                var arguments = new object[] { obj, false, "  " };
                jsonString = method.Invoke(null, arguments) as string;
            }
            else
            {
                var arguments = new object[] { obj };
                jsonString = method.Invoke(null, arguments) as string;
            }
            return jsonString;
        }

        internal static object JsonDeserialize(object obj)
        {
            Assembly assembly = typeof(Selection).Assembly;
            var managerType = assembly.GetTypes().First(t => t.Name == "Json");
            var method = managerType.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static);
            var arguments = new object[] { obj };
            return method.Invoke(null, arguments);
        }

        internal static string GetQuickSearchVersion()
        {
            string version = null;
            try
            {
                var filePath = File.ReadAllText($"{QuickSearchTool.packageFolderName}/package.json");
                if (JsonDeserialize(filePath) is Dictionary<string, object> manifest && manifest.ContainsKey("version"))
                {
                    version = manifest["version"] as string;
                }
            }
            catch (Exception)
            {
                // ignored
            }

            return version ?? "unknown";
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

        internal static bool IsDeveloperMode()
        {
            #if QUICKSEARCH_DEBUG
            return true;
            #else
            return Directory.Exists($"{QuickSearchTool.packageFolderName}/.git");
            #endif
        }

        public static int LevenshteinDistance<T>(IEnumerable<T> lhs, IEnumerable<T> rhs) where T : System.IEquatable<T>
        {
            // Validate parameters
            if (lhs == null) throw new System.ArgumentNullException("lhs");
            if (rhs == null) throw new System.ArgumentNullException("rhs");

            // Convert the parameters into IList instances
            // in order to obtain indexing capabilities
            IList<T> first = lhs as IList<T> ?? new List<T>(lhs);
            IList<T> second = rhs as IList<T> ?? new List<T>(rhs);

            // Get the length of both.  If either is 0, return
            // the length of the other, since that number of insertions
            // would be required.
            int n = first.Count, m = second.Count;
            if (n == 0) return m;
            if (m == 0) return n;

            // Rather than maintain an entire matrix (which would require O(n*m) space),
            // just store the current row and the next row, each of which has a length m+1,
            // so just O(m) space. Initialize the current row.
            int curRow = 0, nextRow = 1;

            int[][] rows = new int[][] { new int[m + 1], new int[m + 1] };
            for (int j = 0; j <= m; ++j)
                rows[curRow][j] = j;

            // For each virtual row (since we only have physical storage for two)
            for (int i = 1; i <= n; ++i)
            {
                // Fill in the values in the row
                rows[nextRow][0] = i;

                for (int j = 1; j <= m; ++j)
                {
                    int dist1 = rows[curRow][j] + 1;
                    int dist2 = rows[nextRow][j - 1] + 1;
                    int dist3 = rows[curRow][j - 1] +
                        (first[i - 1].Equals(second[j - 1]) ? 0 : 1);

                    rows[nextRow][j] = System.Math.Min(dist1, System.Math.Min(dist2, dist3));
                }

                // Swap the current and next rows
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

            // Return the computed edit distance
            return rows[curRow][m];
        }

        public static int LevenshteinDistance(string lhs, string rhs, bool caseSensitive = true)
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
    }

    internal struct DebugTimer : IDisposable
    {
        private bool m_Disposed;
        private string m_Name;
        private Stopwatch m_Timer;

        public double timeMs => m_Timer.Elapsed.TotalMilliseconds;

        public DebugTimer(string name)
        {
            m_Disposed = false;
            m_Name = name;
            m_Timer = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            m_Timer.Stop();
            #if UNITY_2019_1_OR_NEWER
            if (!String.IsNullOrEmpty(m_Name))
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, $"{m_Name} took {timeMs:F1} ms");
            #else
            if (!String.IsNullOrEmpty(m_Name))
                Debug.Log($"{m_Name} took {timeMs} ms");
            #endif
        }
    }

    #if UNITY_EDITOR
    [InitializeOnLoad]
    #endif
    internal static class UnityVersion
    {
        enum Candidate
        {
            Dev = 0,
            Alpha = 1 << 8,
            Beta = 1 << 16,
            Final = 1 << 24
        }

        static UnityVersion()
        {
            var version = Application.unityVersion.Split('.');

            if (version.Length < 2)
            {
                Console.WriteLine("Could not parse current Unity version '" + Application.unityVersion + "'; not enough version elements.");
                return;
            }

            if (int.TryParse(version[0], out Major) == false)
            {
                Console.WriteLine("Could not parse major part '" + version[0] + "' of Unity version '" + Application.unityVersion + "'.");
            }

            if (int.TryParse(version[1], out Minor) == false)
            {
                Console.WriteLine("Could not parse minor part '" + version[1] + "' of Unity version '" + Application.unityVersion + "'.");
            }

            if (version.Length >= 3)
            {
                try
                {
                    Build = ParseBuild(version[2]);
                }
                catch
                {
                    Console.WriteLine("Could not parse minor part '" + version[1] + "' of Unity version '" + Application.unityVersion + "'.");
                }
            }

            #if QUICKSEARCH_DEBUG
            Debug.Log($"Unity {Major}.{Minor}.{Build}");
            #endif
        }

        public static int ParseBuild(string build)
        {
            var rev = 0;
            if (build.Contains("a"))
                rev = (int)Candidate.Alpha;
            else if (build.Contains("b"))
                rev = (int)Candidate.Beta;
            if (build.Contains("f"))
                rev = (int)Candidate.Final;
            var tags = build.Split('a', 'b', 'f', 'p', 'x');
            if (tags.Length == 2)
            {
                rev += Convert.ToInt32(tags[0], 10) << 4;
                rev += Convert.ToInt32(tags[1], 10) << 0;
            }
            return rev;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureLoaded()
        {
            // This method ensures that this type has been initialized before any loading of objects occurs.
            // If this isn't done, the static constructor may be invoked at an illegal time that is not
            // allowed by Unity. During scene deserialization, off the main thread, is an example.
        }

        public static bool IsVersionGreaterOrEqual(int major, int minor)
        {
            if (Major > major)
                return true;
            if (Major == major)
            {
                if (Minor >= minor)
                    return true;
            }

            return false;
        }

        public static bool IsVersionGreaterOrEqual(int major, int minor, int build)
        {
            if (Major > major)
                return true;
            if (Major == major)
            {
                if (Minor > minor)
                    return true;

                if (Minor == minor)
                {
                    if (Build >= build)
                        return true;
                }
            }

            return false;
        }

        public static readonly int Major;
        public static readonly int Minor;
        public static readonly int Build;
    }

    internal struct BlinkCursorScope : IDisposable
    {
        private bool changed;
        private Color oldCursorColor;

        public BlinkCursorScope(bool blink, Color blinkColor)
        {
            changed = false;
            oldCursorColor = Color.white;
            if (blink)
            {
                oldCursorColor = GUI.skin.settings.cursorColor;
                GUI.skin.settings.cursorColor = blinkColor;
                changed = true;
            }
        }

        public void Dispose()
        {
            if (changed)
            {
                GUI.skin.settings.cursorColor = oldCursorColor;
            }
        }
    }
}

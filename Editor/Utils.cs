//#define QUICKSEARCH_DEBUG
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    internal static class Utils
    {
        private static Type[] GetAllEditorWindowTypes()
        {
            var result = new List<Type>();
            var AS = AppDomain.CurrentDomain.GetAssemblies();
            var editorWindow = typeof(EditorWindow);
            foreach (var A in AS)
            {
                var types = A.GetTypes();
                foreach (var T in types)
                {
                    if (T.IsSubclassOf(editorWindow))
                        result.Add(T);
                }
            }
            return result.ToArray();
        }

        public static Type GetProjectBrowserWindowType()
        {
            return GetAllEditorWindowTypes().FirstOrDefault(t => t.Name == "ProjectBrowser");
        }

        public static string GetNameFromPath(string path)
        {
            var lastSep = path.LastIndexOf('/');
            if (lastSep == -1)
                return path;

            return path.Substring(lastSep + 1);
        }

        public static Type[] GetAllDerivedTypes(this AppDomain aAppDomain, Type aType)
        {
            var result = new List<Type>();
            var assemblies = aAppDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    if (type.IsSubclassOf(aType))
                        result.Add(type);
                }
            }
            return result.ToArray();
        }

        public static Rect GetEditorMainWindowPos()
        {
            var containerWinType = AppDomain.CurrentDomain.GetAllDerivedTypes(typeof(ScriptableObject)).FirstOrDefault(t => t.Name == "ContainerWindow");
            if (containerWinType == null)
                throw new MissingMemberException("Can't find internal type ContainerWindow. Maybe something has changed inside Unity");
            var showModeField = containerWinType.GetField("m_ShowMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var positionProperty = containerWinType.GetProperty("position", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
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

        public static void CenterOnMainWin(this EditorWindow window)
        {
            var main = GetEditorMainWindowPos();
            var pos = window.position;
            pos.width = Mathf.Min(window.minSize.x, main.width * 0.90f);
            pos.height = Mathf.Min(window.minSize.y, main.height * 0.90f);
            var w = (main.width - pos.width) * 0.5f;
            var h = (main.height - pos.height) * 0.5f;
            pos.x = main.x + w;
            pos.y = main.y + h;
            window.position = pos;
        }
    }

    #if QUICKSEARCH_DEBUG
    internal struct DebugTimer : IDisposable
    {
        private bool m_Disposed;
        private string m_Name;
        private Stopwatch m_Timer;

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
            TimeSpan timespan = m_Timer.Elapsed;
            Debug.Log($"{m_Name} took {timespan.TotalMilliseconds} ms");
        }
    }
    #endif

    #if UNITY_EDITOR
    [InitializeOnLoad]
    #endif
    public static class UnityVersion
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
                Debug.LogError("Could not parse current Unity version '" + Application.unityVersion + "'; not enough version elements.");
                return;
            }

            if (int.TryParse(version[0], out Major) == false)
            {
                Debug.LogError("Could not parse major part '" + version[0] + "' of Unity version '" + Application.unityVersion + "'.");
            }

            if (int.TryParse(version[1], out Minor) == false)
            {
                Debug.LogError("Could not parse minor part '" + version[1] + "' of Unity version '" + Application.unityVersion + "'.");
            }

            if (version.Length >= 3)
            {
                try
                {
                    Build = ParseBuild(version[2]);
                }
                catch
                {
                    Debug.LogError("Could not parse minor part '" + version[1] + "' of Unity version '" + Application.unityVersion + "'.");
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
            var tags = build.Split(new char[] { 'a', 'b', 'f', 'p', 'x' });
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

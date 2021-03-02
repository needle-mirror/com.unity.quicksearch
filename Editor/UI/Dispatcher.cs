using System;
using System.Collections.Concurrent;

namespace UnityEditor.Search
{
    static class Dispatcher
    {
        private static readonly ConcurrentQueue<Action> s_ExecutionQueue = new ConcurrentQueue<Action>();

        static Dispatcher()
        {
            Utils.tick += Update;
        }

        public static void Enqueue(Action action)
        {
            s_ExecutionQueue.Enqueue(action);
            #if UNITY_EDITOR_WIN && USE_SEARCH_MODULE
            EditorApplication.SignalTick();
            #endif
        }

        public static bool ProcessOne()
        {
            if (!s_ExecutionQueue.TryDequeue(out var action))
                return false;
            action.Invoke();
            return true;
        }

        static void Update()
        {
            if (s_ExecutionQueue.IsEmpty)
                return;

            while (s_ExecutionQueue.TryDequeue(out var action))
                action.Invoke();
        }
    }
}

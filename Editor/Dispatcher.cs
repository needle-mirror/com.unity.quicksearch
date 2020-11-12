using System;
using System.Collections.Concurrent;
using UnityEditor;

namespace UnityEditor.Search
{
    [InitializeOnLoad]
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

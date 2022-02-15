using System;
using System.Collections.Concurrent;

namespace UnityEditor.Search
{
    public static class Dispatcher
    {
        readonly struct Task
        {
            readonly Action handler;
            readonly double delayInSeconds;
            readonly long timestamp;

            public bool valid => handler != null;
            public bool expired => delayInSeconds == 0d || TimeSpan.FromTicks(DateTime.UtcNow.Ticks - timestamp).TotalSeconds >= delayInSeconds;

            public Task(in Action handler, in double delayInSeconds)
            {
                this.handler = handler;
                this.delayInSeconds = delayInSeconds;
                timestamp = DateTime.UtcNow.Ticks;
            }

            public bool Invoke()
            {
                if (!expired)
                    return false;
                handler?.Invoke();
                return true;
            }
        }

        private static readonly ConcurrentQueue<Task> s_ExecutionQueue = new ConcurrentQueue<Task>();

        static Dispatcher()
        {
            Utils.tick += Update;
        }

        public static void Enqueue(Action action)
        {
            Enqueue(action, 0.0d);
        }

        public static void Enqueue(Action action, double delayInSeconds)
        {
            Enqueue(new Task(action, delayInSeconds));
        }

        private static void Enqueue(in Task task)
        {
            s_ExecutionQueue.Enqueue(task);
            #if UNITY_EDITOR_WIN && USE_SEARCH_MODULE
            EditorApplication.SignalTick();
            #endif
        }

        public static bool ProcessOne()
        {
            if (!UnityEditorInternal.InternalEditorUtility.CurrentThreadIsMainThread())
                return false;
            if (!s_ExecutionQueue.TryDequeue(out var task) && task.valid)
                return false;
            return Process(task);
        }

        static bool Process(in Task task)
        {
            var done = task.Invoke();
            if (!done)
                Enqueue(task);
            return done;
        }

        static void Update()
        {
            if (s_ExecutionQueue.IsEmpty)
                return;

            while (s_ExecutionQueue.TryDequeue(out var task) && task.valid)
                if (!Process(task))
                    break;
        }

        public static Action CallDelayed(EditorApplication.CallbackFunction callback, double seconds = 0)
        {
            return Utils.CallDelayed(callback, seconds);
        }
    }
}

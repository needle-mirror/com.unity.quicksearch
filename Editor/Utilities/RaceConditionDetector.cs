// #define USE_RACE_CONDITION_DETECTOR
using System;

#if USE_RACE_CONDITION_DETECTOR
using System.Collections.Concurrent;
#endif

namespace UnityEditor.Search
{
    struct RaceConditionDetector : IDisposable
    {
        #if USE_RACE_CONDITION_DETECTOR
        class InstanceUsage
        {
            public int threadId;
            public int uses;
            public bool raceConditionDetected;

            public InstanceUsage(int threadId)
            {
                uses = 1;
                this.threadId = threadId;
            }
        }


        object m_Resource;
        int m_CurrentThreadId;

        static ConcurrentDictionary<object, InstanceUsage> s_Resources = new ConcurrentDictionary<object, InstanceUsage>();
        #endif

        public RaceConditionDetector(object resource)
        {
            #if USE_RACE_CONDITION_DETECTOR
            var currentThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            m_Resource = resource;
            m_CurrentThreadId = currentThreadId;
            s_Resources.AddOrUpdate(resource, reference => new InstanceUsage(currentThreadId), (reference, usage) =>
            {
                lock (usage)
                {
                    usage.uses++;
                    if (currentThreadId != usage.threadId)
                        usage.raceConditionDetected = true;
                    return usage;
                }
            });
            #endif
        }

        public void Dispose()
        {
            #if USE_RACE_CONDITION_DETECTOR
            if (s_Resources.TryGetValue(m_Resource, out var usage))
            {
                lock (usage)
                {
                    usage.uses--;
                    if (usage.uses == 0)
                        s_Resources.TryRemove(m_Resource, out var removedResource);
                    if (usage.raceConditionDetected)
                        GenerateError();
                }
            }
            #endif
        }

        #if USE_RACE_CONDITION_DETECTOR
        void GenerateError()
        {
            throw new Exception($"Race condition detected. ThreadId={m_CurrentThreadId}");
        }

        #endif
    }
}

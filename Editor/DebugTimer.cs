using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.QuickSearch
{
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
            if (!String.IsNullOrEmpty(m_Name))
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, $"{m_Name} took {timeMs:F2} ms");
        }
    }
}
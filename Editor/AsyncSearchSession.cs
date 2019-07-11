using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;

namespace Unity.QuickSearch
{
    internal class AsyncSearchSession
    {
        public static event Action<IEnumerable<SearchItem>> asyncItemReceived;

        private const long k_MaxTimePerUpdate = 10; // milliseconds

        private IEnumerator<SearchItem> m_ItemsIterator;
        private bool m_IsRunning = false;

        private static int s_RunningSessions = 0;

        public static bool SearchInProgress => s_RunningSessions > 0;

        public void OnUpdate()
        {
            var newItems = new List<SearchItem>();
            var atEnd = !FetchSome(newItems, k_MaxTimePerUpdate);

            if (atEnd)
            {
                Stop();
            }

            if (newItems.Count > 0)
                asyncItemReceived?.Invoke(newItems);
        }

        public void Reset(IEnumerator<SearchItem> itemEnumerator)
        {
            // Remove and add the event handler in case it was already removed.
            Stop();
            m_IsRunning = true;
            ++s_RunningSessions;
            m_ItemsIterator = itemEnumerator;
            EditorApplication.update += OnUpdate;
        }

        public void Stop()
        {
            if (m_IsRunning)
                --s_RunningSessions;
            m_IsRunning = false;
            EditorApplication.update -= OnUpdate;
            m_ItemsIterator = null;
        }

        public bool FetchSome(List<SearchItem> items, int quantity, bool doNotCountNull)
        {
            if (m_ItemsIterator == null)
                return false;

            var atEnd = false;
            for (var i = 0; i < quantity && !atEnd; ++i)
            {
                atEnd = !m_ItemsIterator.MoveNext();
                if (!atEnd)
                {
                    if (m_ItemsIterator.Current == null)
                    {
                        if (doNotCountNull)
                            --i;
                        continue;
                    }
                    items.Add(m_ItemsIterator.Current);
                }
            }

            return !atEnd;
        }

        public bool FetchSome(List<SearchItem> items, int quantity, bool doNotCountNull, long maxFetchTimeMs)
        {
            if (m_ItemsIterator == null)
                return false;

            var atEnd = false;
            var timeToFetch = Stopwatch.StartNew();
            for (var i = 0; i < quantity && !atEnd && timeToFetch.ElapsedMilliseconds < maxFetchTimeMs; ++i)
            {
                atEnd = !m_ItemsIterator.MoveNext();
                if (!atEnd)
                {
                    if (m_ItemsIterator.Current == null)
                    {
                        if (doNotCountNull)
                            --i;
                        continue;
                    }
                    items.Add(m_ItemsIterator.Current);
                }
            }

            return !atEnd;
        }

        public bool FetchSome(List<SearchItem> items, long maxFetchTimeMs)
        {
            if (m_ItemsIterator == null)
                return false;

            var atEnd = false;
            var timeToFetch = Stopwatch.StartNew();
            while (!atEnd && timeToFetch.ElapsedMilliseconds < maxFetchTimeMs)
            {
                atEnd = !m_ItemsIterator.MoveNext();
                if (!atEnd && m_ItemsIterator.Current != null)
                    items.Add(m_ItemsIterator.Current);
            }

            return !atEnd;
        }
    }
}
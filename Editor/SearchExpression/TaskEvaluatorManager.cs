using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditorInternal;

namespace UnityEditor.Search
{
    static class TaskEvaluatorManager
    {
        class MainThreadIEnumerableHandler<T> : BaseAsyncIEnumerableHandler<T>, IDisposable
        {
            List<T> m_Results = new List<T>();
            private bool disposedValue;

            public IEnumerable<T> results => m_Results;
            public EventWaitHandle startEvent { get; private set; } = new EventWaitHandle(false, EventResetMode.AutoReset);
            public EventWaitHandle stopEvent { get; private set; } = new EventWaitHandle(false, EventResetMode.AutoReset);

            public override void SendItems(IEnumerable<T> items)
            {
                m_Results.AddRange(items);
            }

            internal override void Start()
            {
                base.Start();
                stopEvent.Reset();
                startEvent.Set();
            }

            public override void Stop()
            {
                base.Stop();
                stopEvent.Set();
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposedValue)
                    return;
                if (disposing)
                {
                    startEvent.Dispose();
                    stopEvent.Dispose();
                }
                disposedValue = true;
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        public static IEnumerable<SearchItem> Evaluate(SearchExpressionContext c, SearchExpression expression)
        {
            var concurrentList = new ConcurrentBag<SearchItem>();
            var yieldSignal = new EventWaitHandle(false, EventResetMode.AutoReset);
            var cancelToken = c.search.sessions.cancelToken;

            var task = Task.Run(() =>
            {
                var enumerable = expression.Execute(c, SearchExpressionExecutionFlags.ThreadedEvaluation);
                foreach (var searchItem in enumerable)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        if (c.search.options.HasAny(SearchFlags.Debug))
                            UnityEngine.Debug.LogWarning($"Interrupt {c.search.sessionId}");
                        return;
                    }

                    if (searchItem != null)
                    {
                        concurrentList.Add(searchItem);
                        yieldSignal.Set();
                    }
                }
            });

            while (!concurrentList.IsEmpty || !TaskHelper.IsTaskFinished(task))
            {
                if (cancelToken.IsCancellationRequested)
                {
                    if (c.search.options.HasAny(SearchFlags.Debug))
                        UnityEngine.Debug.LogWarning($"Interrupt {c.search.sessionId}");
                    break;
                }

                if (!yieldSignal.WaitOne(0))
                {
                    if (concurrentList.IsEmpty)
                        Dispatcher.ProcessOne();
                    yield return null;
                }
                while (concurrentList.TryTake(out var item))
                    yield return item;
            }

            yieldSignal.Dispose();
            yieldSignal = null;

            if (task.IsFaulted && task.Exception?.InnerException != null)
            {
                if (task.Exception.InnerException is SearchExpressionEvaluatorException sex)
                    throw sex;
                UnityEngine.Debug.LogException(task.Exception.InnerException);
            }
        }

        public static T EvaluateMainThread<T>(Func<T> callback)
        {
            if (InternalEditorUtility.CurrentThreadIsMainThread())
                return callback();

            using (var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset))
            {
                T result = default;
                Dispatcher.Enqueue(() =>
                {
                    result = callback();
                    waitHandle.Set();
                });

                waitHandle.WaitOne();
                return result;
            }
        }

        public static IEnumerable<T> EvaluateMainThread<T>(Action<Action<T>> callback)
        {
            var concurrentList = new ConcurrentBag<T>();
            var yielderHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

            void ItemReceived(T item)
            {
                concurrentList.Add(item);
                yielderHandle.Set();
            }

            if (!InternalEditorUtility.CurrentThreadIsMainThread())
            {
                var finishedHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
                Dispatcher.Enqueue(() =>
                {
                    callback(ItemReceived);
                    finishedHandle.Set();
                });

                while (!finishedHandle.WaitOne(0))
                {
                    if (yielderHandle.WaitOne(0))
                        while (concurrentList.TryTake(out var item))
                            yield return item;
                }

                finishedHandle.Dispose();
                finishedHandle = null;
            }
            else
                callback(ItemReceived);

            yielderHandle.Dispose();
            yielderHandle = null;

            while (concurrentList.Count > 0)
            {
                if (concurrentList.TryTake(out var item))
                    yield return item;
            }
        }

        public static IEnumerable<T> EvaluateMainThread<T>(IEnumerable<T> set, Func<T, T> callback, int minBatchSize = 50) where T : class
        {
            if (InternalEditorUtility.CurrentThreadIsMainThread())
            {
                foreach (var r in set)
                {
                    if (r == null)
                    {
                        yield return null;
                        continue;
                    }

                    yield return callback(r);
                }
                yield break;
            }

            var items = new ConcurrentBag<T>();
            var results = new ConcurrentBag<T>();
            var resultSignal = new EventWaitHandle(false, EventResetMode.AutoReset);

            void ProcessBatch(int batchCount, EventWaitHandle finishedSignal)
            {
                var processedItemCount = 0;
                while (items.TryTake(out var item))
                {
                    var result = callback(item);
                    if (result != null)
                    {
                        results.Add(result);
                        resultSignal.Set();
                    }

                    if (batchCount != -1 && processedItemCount++ >= batchCount)
                        break;
                }

                finishedSignal.Set();
            }

            EventWaitHandle batchFinishedSignal = null;
            foreach (var r in set)
            {
                if (r == null)
                {
                    yield return null;
                    continue;
                }

                items.Add(r);
                if (batchFinishedSignal == null || batchFinishedSignal.WaitOne(0))
                {
                    if (batchFinishedSignal == null)
                        batchFinishedSignal = new EventWaitHandle(false, EventResetMode.AutoReset);
                    Dispatcher.Enqueue(() => ProcessBatch(minBatchSize, batchFinishedSignal));
                }

                if (resultSignal.WaitOne(0))
                    while (results.TryTake(out var item))
                        yield return item;
            }

            var finalBatch = new EventWaitHandle(false, EventResetMode.ManualReset);
            Dispatcher.Enqueue(() => ProcessBatch(-1, finalBatch));
            while (!finalBatch.WaitOne(1) || results.Count > 0)
            {
                while (results.TryTake(out var item))
                    yield return item;
            }

            batchFinishedSignal?.Dispose();
            batchFinishedSignal = null;

            finalBatch.Dispose();
            finalBatch = null;

            resultSignal.Dispose();
            resultSignal = null;
        }

        public static IEnumerable<T> EvaluateMainThreadUnroll<T>(Func<IEnumerable<T>> callback)
        {
            if (InternalEditorUtility.CurrentThreadIsMainThread())
                return callback();

            using (MainThreadIEnumerableHandler<T> enumerableHandler = new MainThreadIEnumerableHandler<T>())
            {
                Dispatcher.Enqueue(() =>
                {
                    var enumerable = callback();
                    enumerableHandler.Reset(enumerable);
                    enumerableHandler.Start();
                });

                enumerableHandler.startEvent.WaitOne();
                enumerableHandler.stopEvent.WaitOne();

                return enumerableHandler.results;
            }
        }
    }
}

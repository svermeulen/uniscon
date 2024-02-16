using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Uniscon.Internal;
using System.Linq;

namespace Uniscon
{
    public class TaskNursery
    {
        readonly Queue<Awaitable> _tasks = new Queue<Awaitable>();

        CancellationTokenSource? _cancellationTokenSource;
        CancellationTokenSource? _timeoutCancellationTokenSource;
        Awaitable? _timeoutTask;
        TimeoutBehaviour? _timeoutBehaviour;
        bool _exceededTimeout = false;

        TaskNursery()
        {
        }

        void Reset(
            CancellationToken cancellationToken = default,
            float? moveOnAfter = null,
            float? failAfter = null
        )
        {
            Assert.That(_cancellationTokenSource == null && _timeoutCancellationTokenSource == null && _timeoutTask == null && _timeoutBehaviour == null && !_exceededTimeout && !_tasks.Any());

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );

            if (moveOnAfter != null || failAfter != null)
            {
                Assert.That(
                    failAfter == null || moveOnAfter == null,
                    "Cannot specify both moveOnAfter and failAfter"
                );

                float timeoutAmount;

                if (moveOnAfter != null)
                {
                    _timeoutBehaviour = TimeoutBehaviour.MoveOn;
                    timeoutAmount = moveOnAfter.Value;
                }
                else
                {
                    Assert.That(failAfter.HasValue);

                    _timeoutBehaviour = TimeoutBehaviour.Fail;
                    timeoutAmount = failAfter!.Value;
                }

                _timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _cancellationTokenSource.Token
                );
                _timeoutTask = RunTimeoutTask(timeoutAmount, _timeoutCancellationTokenSource.Token);
            }
        }

        public CancellationToken CancellationToken => CancellationTokenSource.Token;
        public CancellationTokenSource CancellationTokenSource
        {
            get { return _cancellationTokenSource!; }
        }

        async Awaitable RunTimeoutTask(float timeout, CancellationToken cancellationToken)
        {
            await Awaitable.WaitForSecondsAsync(timeout, cancellationToken);

            if (!_cancellationTokenSource!.IsCancellationRequested)
            {
                _exceededTimeout = true;
                _cancellationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// Note that this will run the given work in the next frame,
        /// (hence the "soon").  If you want to run in the same frame,
        /// then use Run.
        /// </summary>
        public async void RunSoon(Func<TaskNursery, Awaitable> work)
        {
            var subtask = new AwaitableCompletionSource();

            _tasks.Enqueue(subtask.Awaitable);

            try
            {
                await Awaitable.NextFrameAsync();
                await work(this);
            }
            catch (Exception e)
            {
                CancellationTokenSource.Cancel();
                subtask.TrySetException(e);

                // Do not throw, otherwise unity will receive the exception
                // And prevent us from being able to handle it when it is
                // thrown from DisposeAsync
            }

            // Note: Does nothing if TrySetException is already called
            subtask.TrySetResult();
        }

        public async Awaitable<T> Run<T>(Func<TaskNursery, Awaitable<T>> work)
        {
            var subtask = new AwaitableCompletionSource();

            _tasks.Enqueue(subtask.Awaitable);

            T retVal;

            try
            {
                retVal = await work(this);
            }
            catch (Exception e)
            {
                CancellationTokenSource.Cancel();
                subtask.TrySetException(e);
                throw;
            }

            subtask.TrySetResult();
            return retVal;
        }

        public async Awaitable Run(Func<TaskNursery, Awaitable> work)
        {
            var subtask = new AwaitableCompletionSource();

            _tasks.Enqueue(subtask.Awaitable);

            try
            {
                await work(this);
            }
            catch (Exception e)
            {
                CancellationTokenSource.Cancel();
                subtask.TrySetException(e);
                throw;
            }

            subtask.TrySetResult();
        }

        async Awaitable DisposeAsync()
        {
            List<Exception>? exceptions = null;

            while (_tasks.Any())
            {
                try
                {
                    await _tasks.Dequeue();
                }
                catch (Exception ex)
                {
                    CancellationTokenSource.Cancel();

                    if (ex is not OperationCanceledException)
                    {
                        exceptions ??= new List<Exception>();
                        exceptions.Add(ex);
                    }
                }
            }

            DisposeResult disposeResult;

            if (exceptions != null)
            {
                disposeResult = DisposeResult.AggregateException;
            }
            else if (CancellationToken.IsCancellationRequested)
            {
                disposeResult = DisposeResult.CancelledException;
            }
            else
            {
                disposeResult = DisposeResult.None;
            }

            if (_timeoutBehaviour.HasValue)
            {
                if (_exceededTimeout)
                {
                    if (_timeoutBehaviour == TimeoutBehaviour.MoveOn)
                    {
                        Assert.That(CancellationToken.IsCancellationRequested);

                        if (disposeResult != DisposeResult.AggregateException)
                        {
                            disposeResult = DisposeResult.None;
                        }
                    }
                    else
                    {
                        Assert.That(_timeoutBehaviour == TimeoutBehaviour.Fail);

                        if (disposeResult != DisposeResult.AggregateException)
                        {
                            disposeResult = DisposeResult.TimeoutException;
                        }
                    }
                }
                else
                {
                    _timeoutCancellationTokenSource!.Cancel();
                }

                try
                {
                    // Return awaitable to pool
                    await _timeoutTask!;
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }

                _timeoutCancellationTokenSource!.Dispose();
            }
            else
            {
                Assert.That(_timeoutCancellationTokenSource == null);
                Assert.That(_timeoutTask == null);
            }

            CancellationTokenSource.Dispose();

            Pool.Despawn(this);

            switch (disposeResult)
            {
                case DisposeResult.AggregateException:
                {
                    throw new AggregateException(exceptions);
                }
                case DisposeResult.TimeoutException:
                {
                    throw new TimeoutException("TaskNursery timed out");
                }
                case DisposeResult.CancelledException:
                {
                    throw new OperationCanceledException();
                }
                default:
                {
                    Assert.That(disposeResult == DisposeResult.None);
                    break;
                }
            }
        }

        enum DisposeResult
        {
            None,
            AggregateException,
            CancelledException,
            TimeoutException
        }

        public static async Awaitable New(
            CancellationToken cancellationToken,
            Action<TaskNursery> work,
            float? moveOnAfter = null,
            float? failAfter = null
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nursery = Pool.Spawn(
                cancellationToken,
                moveOnAfter: moveOnAfter,
                failAfter: failAfter
            );

            try
            {
                work(nursery);
            }
            finally
            {
                await nursery.DisposeAsync();
            }
        }

        public static async Awaitable New(
            CancellationToken cancellationToken,
            Func<TaskNursery, Awaitable> work,
            float? moveOnAfter = null,
            float? failAfter = null
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nursery = Pool.Spawn(
                cancellationToken,
                moveOnAfter: moveOnAfter,
                failAfter: failAfter
            );

            try
            {
                await work(nursery);
            }
            finally
            {
                await nursery.DisposeAsync();
            }
        }

        public static async Awaitable<T> New<T>(
            CancellationToken cancellationToken,
            Func<TaskNursery, Awaitable<T>> work,
            float? moveOnAfter = null,
            float? failAfter = null
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nursery = Pool.Spawn(
                cancellationToken,
                moveOnAfter: moveOnAfter,
                failAfter: failAfter
            );

            try
            {
                return await nursery.Run(work);
            }
            finally
            {
                await nursery.DisposeAsync();
            }
        }

        enum TimeoutBehaviour
        {
            Fail,
            MoveOn,
        }

        static class Pool
        {
            static readonly Stack<TaskNursery> _pool = new Stack<TaskNursery>();

            public static TaskNursery Spawn(
                CancellationToken cancellationToken = default,
                float? moveOnAfter = null,
                float? failAfter = null)
            {
                TaskNursery nursery;

                if (_pool.Count > 0)
                {
                    nursery = _pool.Pop();
                }
                else
                {
                    nursery = new TaskNursery();
                }

                nursery.Reset(cancellationToken, moveOnAfter, failAfter);
                return nursery;
            }

            public static void Despawn(TaskNursery nursery)
            {
                nursery._cancellationTokenSource = null;
                nursery._timeoutCancellationTokenSource = null;
                nursery._timeoutTask = null;
                nursery._timeoutBehaviour = null;
                nursery._exceededTimeout = false;
                Assert.That(!nursery._tasks.Any());

                _pool.Push(nursery);
            }
        }
    }
}

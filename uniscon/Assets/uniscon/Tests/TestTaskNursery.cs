using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.TestTools;
using Uniscon.Internal;

namespace Uniscon.Tests
{
    public static class AwaitableEx
    {
        public static Awaitable WaitForTimeSpanAsync(
            TimeSpan time,
            CancellationToken cancellationToken = default
        )
        {
            return Awaitable.WaitForSecondsAsync((float)time.TotalSeconds, cancellationToken);
        }
    }

    public static class TestUtil
    {
        public static async Awaitable LogExceptions(Func<Awaitable> work)
        {
            try
            {
                await work();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                throw;
            }
        }
    }

    public class TestNursery
    {
        const float TestIntervalSeconds = 0.1f;
        static readonly TimeSpan TestInterval = TimeSpan.FromSeconds(TestIntervalSeconds);

        class TestException : Exception
        {
            public TestException(string message)
                : base(message) { }
        }

        [UnityTest]
        public IEnumerator TestSimpleWaitInNurseryFunc()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                var startTime = Time.time;

                await TaskNursery.New(
                    default,
                    async (TaskNursery nursery) =>
                    {
                        Debug.LogFormat("Waiting for {0}", TestInterval);
                        await AwaitableEx.WaitForTimeSpanAsync(
                            TestInterval,
                            nursery.CancellationToken
                        );
                        Debug.LogFormat("Done waiting for {0}", TestInterval);
                    }
                );

                var elapsed = TimeSpan.FromSeconds(Time.time - startTime);
                Debug.LogFormat("TestSimpleWait completed in {0}", elapsed);

                Assert.That(
                    elapsed >= TestInterval,
                    "Unexpected elapsed time: {0}",
                    elapsed
                );
            });
        }

        [UnityTest]
        public IEnumerator TestImmediateCancellationEndsNurseryImmediately()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                var cts = new CancellationTokenSource();
                cts.Cancel();

                Exception exception = null;
                var startTime = Time.time;
                var wasRun = false;

                try
                {
                    await TaskNursery.New(
                        cts.Token,
                        async (TaskNursery nursery) =>
                        {
                            wasRun = true;
                            await AwaitableEx.WaitForTimeSpanAsync(TestInterval * 10, cts.Token);
                        }
                    );
                }
                catch (Exception exc)
                {
                    exception = exc;
                }

                var elapsed = TimeSpan.FromSeconds(Time.time - startTime);
                Debug.LogFormat("TestSimpleWaitWithCancellation completed in {0}", elapsed);

                Assert.That(!wasRun);
                Assert.That(elapsed < TestInterval, "Unexpected elapsed time: {0}", elapsed);
                Assert.That(exception != null);
                Assert.That(exception is OperationCanceledException);
            });
        }

        [UnityTest]
        public IEnumerator TestSubTasksRunInParallel()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                var startTime = Time.time;

                await TaskNursery.New(
                    default,
                    (TaskNursery nursery) =>
                    {
                        nursery.RunSoon(async _ =>
                            await AwaitableEx.WaitForTimeSpanAsync(
                                TestInterval,
                                nursery.CancellationToken
                            )
                        );
                        nursery.RunSoon(async _ =>
                            await AwaitableEx.WaitForTimeSpanAsync(
                                TestInterval,
                                nursery.CancellationToken
                            )
                        );
                    }
                );

                var elapsed = TimeSpan.FromSeconds(Time.time - startTime);
                Debug.LogFormat("TestParallelTasks completed in {0}", elapsed);

                Assert.That(
                    elapsed >= TestInterval && elapsed < TestInterval * 2,
                    "Unexpected elapsed time: {0}",
                    elapsed
                );
            });
        }

        [UnityTest]
        public IEnumerator TestSubTaskExceptionIsPropagated()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                Exception exception = null;
                var startTime = Time.time;

                try
                {
                    await TaskNursery.New(
                        default,
                        (TaskNursery nursery) =>
                        {
                            nursery.RunSoon(async _ =>
                            {
                                await AwaitableEx.WaitForTimeSpanAsync(
                                    TestInterval,
                                    nursery.CancellationToken
                                );
                                throw new TestException("foo");
                            });
                        }
                    );
                }
                catch (Exception exc)
                {
                    Debug.LogFormat("Caught exception: {0}", exc);
                    exception = exc;
                }

                var elapsed = TimeSpan.FromSeconds(Time.time - startTime);
                Debug.LogFormat("TestSubTaskException completed in {0}", elapsed);

                Assert.That(
                    elapsed >= TestInterval && elapsed < TestInterval * 2,
                    "Unexpected elapsed time: {0}",
                    elapsed
                );
                Assert.That(exception != null);
                Assert.That(exception is AggregateException);

                var innerExceptions = ((AggregateException)exception).InnerExceptions;

                Assert.That(innerExceptions.Count == 1);
                Assert.That(innerExceptions[0] is TestException);
                Assert.That(((TestException)innerExceptions[0]).Message == "foo");
                Debug.LogFormat("Completed TestSubTaskException");
            });
        }

        [UnityTest]
        public IEnumerator TestSubTaskCancellationIsPropagated()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                var cts = new CancellationTokenSource();

                async void Canceler()
                {
                    await AwaitableEx.WaitForTimeSpanAsync(TestInterval);
                    cts.Cancel();
                }
                ;

                Canceler();

                Exception exception = null;
                var startTime = Time.time;

                try
                {
                    await TaskNursery.New(
                        cts.Token,
                        (TaskNursery nursery) =>
                        {
                            nursery.RunSoon(async _ =>
                            {
                                await AwaitableEx.WaitForTimeSpanAsync(
                                    TestInterval * 10,
                                    nursery.CancellationToken
                                );
                            });
                        }
                    );
                }
                catch (Exception exc)
                {
                    Debug.LogFormat("Caught exception: {0}", exc);
                    exception = exc;
                }

                var elapsed = TimeSpan.FromSeconds(Time.time - startTime);
                Debug.LogFormat("TestSubTaskCancellation completed in {0}", elapsed);

                Assert.That(
                    elapsed >= TestInterval && elapsed < 2 * TestInterval,
                    "Unexpected elapsed time: {0}",
                    elapsed
                );
                Assert.That(exception != null);
                Assert.That(exception is OperationCanceledException);
                Debug.LogFormat("Completed TestSubTaskCancellation");
            });
        }

        [UnityTest]
        public IEnumerator TestNestedNurseryWithSimpleWaitWorks()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                var startTime = Time.time;

                await TaskNursery.New(
                    default,
                    async (TaskNursery nursery1) =>
                    {
                        await TaskNursery.New(
                            nursery1.CancellationToken,
                            async (TaskNursery nursery2) =>
                            {
                                await AwaitableEx.WaitForTimeSpanAsync(
                                    TestInterval,
                                    nursery2.CancellationToken
                                );
                            }
                        );
                    }
                );

                var elapsed = TimeSpan.FromSeconds(Time.time - startTime);
                Debug.LogFormat("TestSubTaskCancellation completed in {0}", elapsed);

                Assert.That(
                    elapsed >= TestInterval && elapsed < 2 * TestInterval,
                    "Unexpected elapsed time: {0}",
                    elapsed
                );
            });
        }

        [UnityTest]
        public IEnumerator TestCancelFromSubTaskWorks()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                var startTime = Time.time;
                var receivedCancelException = false;

                try
                {
                    await TaskNursery.New(
                        default,
                        (TaskNursery nursery) =>
                        {
                            nursery.RunSoon(async _ =>
                            {
                                await AwaitableEx.WaitForTimeSpanAsync(
                                    TestInterval,
                                    nursery.CancellationToken
                                );

                                nursery.CancellationTokenSource.Cancel();
                            });

                            nursery.RunSoon(async _ =>
                                await AwaitableEx.WaitForTimeSpanAsync(
                                    5 * TestInterval,
                                    nursery.CancellationToken
                                )
                            );
                        }
                    );
                }
                catch (OperationCanceledException)
                {
                    receivedCancelException = true;
                }

                Assert.That(receivedCancelException);

                var elapsed = TimeSpan.FromSeconds(Time.time - startTime);

                Assert.That(
                    elapsed >= TestInterval && elapsed < TestInterval * 2,
                    "Unexpected elapsed time: {0}",
                    elapsed
                );
            });
        }

        [UnityTest]
        public IEnumerator TestMoveOnAfterWorks()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                var startTime = Time.time;

                await TaskNursery.New(
                    default,
                    (TaskNursery nursery) =>
                    {
                        nursery.RunSoon(async _ =>
                        {
                            await AwaitableEx.WaitForTimeSpanAsync(
                                TestInterval * 10,
                                nursery.CancellationToken
                            );
                        });
                    },
                    moveOnAfter: TestIntervalSeconds
                );

                var elapsed = TimeSpan.FromSeconds(Time.time - startTime);
                Debug.LogFormat("TestSubTaskCancellation completed in {0}", elapsed);

                Assert.That(
                    elapsed >= TestInterval && elapsed < 2 * TestInterval,
                    "Unexpected elapsed time: {0}",
                    elapsed
                );
            });
        }

        [UnityTest]
        public IEnumerator TestFailAfterWorks()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                var startTime = Time.time;
                var receivedTimeoutException = false;

                try
                {
                    await TaskNursery.New(
                        default,
                        (TaskNursery nursery) =>
                        {
                            nursery.RunSoon(async _ =>
                            {
                                await AwaitableEx.WaitForTimeSpanAsync(
                                    TestInterval * 10,
                                    nursery.CancellationToken
                                );
                            });
                        },
                        failAfter: TestIntervalSeconds
                    );
                }
                catch (TimeoutException)
                {
                    receivedTimeoutException = true;
                }

                var elapsed = TimeSpan.FromSeconds(Time.time - startTime);
                Debug.LogFormat("TestSubTaskCancellation completed in {0}", elapsed);

                Assert.That(
                    elapsed >= TestInterval && elapsed < 2 * TestInterval,
                    "Unexpected elapsed time: {0}",
                    elapsed
                );

                Assert.That(receivedTimeoutException);
            });
        }

        [UnityTest]
        public IEnumerator TestRunSoonRunsLater()
        {
            yield return TestUtil.LogExceptions(async () =>
            {
                var order = new List<int>() { 0 };
                var frameCounts = new Dictionary<int, int> { { 0, Time.frameCount } };

                await TaskNursery.New(
                    default,
                    (TaskNursery nursery) =>
                    {
                        nursery.RunSoon(async _ =>
                        {
                            frameCounts[1] = Time.frameCount;
                            order.Add(1);
                            await AwaitableEx.WaitForTimeSpanAsync(
                                TestInterval,
                                nursery.CancellationToken
                            );
                        });

                        nursery.RunSoon(async _ =>
                        {
                            frameCounts[2] = Time.frameCount;
                            order.Add(2);
                            await AwaitableEx.WaitForTimeSpanAsync(
                                TestInterval,
                                nursery.CancellationToken
                            );
                        });

                        frameCounts[3] = Time.frameCount;
                        order.Add(3);
                    }
                );

                Assert.That(order.Count == 4);
                Assert.That(Enumerable.SequenceEqual(order, new[] { 0, 3, 1, 2 }));

                var startFrameCount = frameCounts[0];
                Assert.That(frameCounts[3] == startFrameCount);
                Assert.That(frameCounts[1] == startFrameCount + 1 && frameCounts[2] == startFrameCount + 1);
            });
        }
    }
}

﻿using System.Diagnostics.CodeAnalysis;

namespace DotNext.Threading
{
    [ExcludeFromCodeCoverage]
    public sealed class LockTests : Test
    {
        [Fact]
        public static void EmptyLock()
        {
            var @lock = default(Lock);
            False(@lock.TryAcquire(out var holder));
            if (holder)
                Fail("Lock is acquired");

            holder = @lock.Acquire();
            if (holder)
                Fail("Lock is acquired");

            Throws<TimeoutException>(() => @lock.Acquire(DefaultTimeout));

            False(@lock.TryAcquire(DefaultTimeout, out holder));

            holder.Dispose();
        }

        [Fact]
        public static void MonitorLock()
        {
            var syncRoot = new object();
            using var @lock = Lock.Monitor(syncRoot);
            True(@lock.TryAcquire(out var holder));
            True(Monitor.IsEntered(syncRoot));
            holder.Dispose();
            False(Monitor.IsEntered(syncRoot));

            holder = @lock.Acquire(DefaultTimeout);
            True(Monitor.IsEntered(syncRoot));
            holder.Dispose();
            False(Monitor.IsEntered(syncRoot));
        }

        [Fact]
        public static void SemaphoreLock()
        {
            using var sem = new SemaphoreSlim(3);
            using var @lock = Lock.Semaphore(sem);
            True(@lock.TryAcquire(out var holder));
            Equal(2, sem.CurrentCount);
            holder.Dispose();
            Equal(3, sem.CurrentCount);
        }
    }
}

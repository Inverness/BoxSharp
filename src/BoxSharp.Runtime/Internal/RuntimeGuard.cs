using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace BoxSharp.Runtime.Internal
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class RuntimeGuard
    {
        private bool _active;

        private int _operationCountLimit;
        private long _stackBytesLimit;
        private long _stackBytesLimitInExceptionHandlers;
        private long _allocatedCountTotalLimit;
        private long _timeLimitStopwatchTicks;
        private Action<IDisposable>? _afterForcedDispose;

        private long _stackBaseline;
        private readonly Stopwatch _stopwatch;
        private int _operationCount;
        private long _allocatedCountTotal;
        [ThreadStatic] private static long t_staticConstructorStackBaseline;
        private HashSet<IDisposable>? _disposables;

        internal RuntimeGuard()
        {
            _stopwatch = new Stopwatch();
        }

        internal void GuardEnter()
        {
            EnsureActive();
            EnsureStack();
            EnsureTime();
            //EnsureRate();
        }

        internal void GuardEnterStaticConstructor()
        {
            EnsureActive();
            t_staticConstructorStackBaseline = GetCurrentStackOffset();
            EnsureTime();
            //EnsureRate();
        }

        internal void GuardExitStaticConstructor()
        {
            t_staticConstructorStackBaseline = 0;
        }

        internal void GuardJump()
        {
            EnsureActive();
            EnsureTime();
            //EnsureRate();
        }

        internal void GuardCount(long count)
        {
            EnsureActive();
            EnsureTime();
            EnsureCount(count);
        }

        // public IEnumerable<T> GuardEnumerableCollected<T>(IEnumerable<T> enumerable)
        // {
        //     EnsureActive();
        //     foreach (T item in enumerable)
        //     {
        //         EnsureTime();
        //         EnsureCount(1);
        //         EnsureRate();
        //         yield return item;
        //     }
        // }

        internal void CollectDisposable(IDisposable disposable)
        {
            if (disposable == null)
                return;

            _disposables ??= new HashSet<IDisposable>();

            _disposables.Add(disposable);
        }

        private void EnsureStack()
        {
            var stackCurrent = GetCurrentStackOffset();

            long stackBaseline;
            if (t_staticConstructorStackBaseline != 0)
            {
                stackBaseline = t_staticConstructorStackBaseline;
            }
            else
            {
                if (_stackBaseline == 0)
                    Interlocked.CompareExchange(ref _stackBaseline, stackCurrent, 0);
                stackBaseline = _stackBaseline;
            }

            var stackBytesCount = stackBaseline - stackCurrent;
            if (stackBytesCount > _stackBytesLimit)
            {
                if (Marshal.GetExceptionCode() == 0)
                    throw new StackGuardException(stackBaseline, stackCurrent, _stackBytesLimit);
                // https://github.com/ashmind/SharpLab/issues/269#issuecomment-383370318
                if (stackBytesCount > _stackBytesLimitInExceptionHandlers)
                    throw new StackGuardException(stackBaseline, stackCurrent, _stackBytesLimitInExceptionHandlers);
            }
        }

        private void EnsureTime()
        {
            if (!_stopwatch.IsRunning)
                _stopwatch.Start();
#if DEBUG
            if (Debugger.IsAttached)
                return;
#endif
            if (_stopwatch.ElapsedTicks > _timeLimitStopwatchTicks)
                ThrowTimeGuardException();
        }

        private void EnsureRate()
        {
            if ((++_operationCount) > _operationCountLimit)
                ThrowRateGuardException();
        }

        private void EnsureActive()
        {
            if (!_active)
                ThrowGuardException();
        }

        private void EnsureCount(long count)
        {
            if (Interlocked.Add(ref _allocatedCountTotal, count) > _allocatedCountTotalLimit)
                ThrowMemoryGuardException();
        }

        private unsafe long GetCurrentStackOffset()
        {
            byte* local = stackalloc byte[1];
            return (long)local;
        }

        internal void Initialize(IRuntimeGuardSettings settings)
        {
            if (_active)
                throw new InvalidOperationException();

            _stackBytesLimit = settings.StackBytesLimit;
            _stackBytesLimitInExceptionHandlers = settings.ExceptionStackBytesLimit;
            _allocatedCountTotalLimit = settings.AllocatedCountTotalLimit;

            var timeLimitStopwatchTicks = (long)(settings.TimeLimit.TotalSeconds * Stopwatch.Frequency);
            if (timeLimitStopwatchTicks < 0) // overflow, e.g. with TimeSpan.MaxValue
                timeLimitStopwatchTicks = long.MaxValue;
            _timeLimitStopwatchTicks = timeLimitStopwatchTicks;

            _operationCountLimit = settings.OperationCountLimit;
            _afterForcedDispose = settings.AfterForcedDispose;

            _stackBaseline = 0;
            _operationCount = 0;

            _disposables?.Clear();
        }

        internal void Start()
        {
            _active = true;

            _stopwatch.Stop();
            _stopwatch.Reset();
        }

        internal void Stop()
        {
            _active = false;
            if (_disposables == null)
                return;
            foreach (IDisposable disposable in _disposables)
            {
                try
                {
                    disposable.Dispose();
                    _afterForcedDispose?.Invoke(disposable);
                }
                catch
                {
                }
            }
        }

        public TimeSpan GetTimeUntilLimit()
        {
            EnsureTime();
            return new TimeSpan(_timeLimitStopwatchTicks - _stopwatch.ElapsedTicks);
        }

        // Putting exception throws in their own methods reduces the IL size of the
        // calling method and makes inlining more likely

        private static void ThrowGuardException()
        {
            throw new GuardException(GuardException.NoScopeMessage);
        }

        private static void ThrowRateGuardException()
        {
            throw new RateGuardException();
        }

        private static void ThrowMemoryGuardException()
        {
            throw new MemoryGuardException();
        }

        private static void ThrowTimeGuardException()
        {
            throw new TimeGuardException();
        }

        // public static class FlowThrough
        // {
        //     public static IntPtr GuardCountIntPtr(IntPtr count, RuntimeGuard guard)
        //     {
        //         guard.GuardCount(count.ToInt64());
        //         return count;
        //     }

        //     public static int GuardCountInt32(int count, RuntimeGuard guard)
        //     {
        //         guard.GuardCount(count);
        //         return count;
        //     }

        //     public static long GuardCountInt64(long count, RuntimeGuard guard)
        //     {
        //         guard.GuardCount(count);
        //         return count;
        //     }

        //     public static IEnumerable<T> GuardEnumerableCollected<T>(IEnumerable<T> enumerable, RuntimeGuard guard)
        //     {
        //         return guard.GuardEnumerableCollected(enumerable);
        //     }

        //     public static TDisposable CollectDisposable<TDisposable>(TDisposable disposable, RuntimeGuard guard)
        //         where TDisposable : IDisposable
        //     {
        //         guard.CollectDisposable(disposable);
        //         return disposable;
        //     }
        // }
    }
}

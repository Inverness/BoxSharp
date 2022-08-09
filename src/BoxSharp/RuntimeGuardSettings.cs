using BoxSharp.Runtime.Internal;
using System;

namespace BoxSharp
{
    public class RuntimeGuardSettings : IRuntimeGuardSettings
    {
        public static readonly RuntimeGuardSettings Default = new(
            IntPtr.Size >= 8 ? 2048 : 1024,
            IntPtr.Size >= 8 ? 16384 : 6144,
            100,
            TimeSpan.FromSeconds(0.5),
            500);

        public RuntimeGuardSettings(
            int stackBytesLimit,
            int exceptionStackBytesLimit,
            long allocatedCountTotalLimit,
            TimeSpan timeLimit,
            int operationCountLimit,
            Action<IDisposable>? afterForcedDispose = null)
        {
            StackBytesLimit = stackBytesLimit;
            ExceptionStackBytesLimit = exceptionStackBytesLimit;
            AllocatedCountTotalLimit = allocatedCountTotalLimit;
            TimeLimit = timeLimit;
            OperationCountLimit = operationCountLimit;
            AfterForcedDispose = afterForcedDispose;
        }

        public RuntimeGuardSettings(RuntimeGuardSettings other)
        {
            StackBytesLimit = other.StackBytesLimit;
            ExceptionStackBytesLimit = other.ExceptionStackBytesLimit;
            AllocatedCountTotalLimit = other.AllocatedCountTotalLimit;
            TimeLimit = other.TimeLimit;
            OperationCountLimit = other.OperationCountLimit;
            AfterForcedDispose = other.AfterForcedDispose;
        }

        public int StackBytesLimit { get; }

        public int ExceptionStackBytesLimit { get; }

        public long AllocatedCountTotalLimit { get; }

        public TimeSpan TimeLimit { get; }

        public int OperationCountLimit { get; }

        public Action<IDisposable>? AfterForcedDispose { get; }
    }
}

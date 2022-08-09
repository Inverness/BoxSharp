using System;

namespace BoxSharp.Runtime.Internal
{
    internal interface IRuntimeGuardSettings
    {
        int StackBytesLimit { get; }

        int ExceptionStackBytesLimit { get; }

        long AllocatedCountTotalLimit { get; }

        TimeSpan TimeLimit { get; }

        int OperationCountLimit { get; }

        Action<IDisposable>? AfterForcedDispose { get; }
    }
}

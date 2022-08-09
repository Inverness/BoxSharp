using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace BoxSharp.Runtime.Internal
{
    /// <summary>
    /// Manages instances of RuntimeGuard for used by the script compiler and compiled script.
    /// </summary>
    internal static class RuntimeGuardInstances
    {
        private static readonly ConcurrentDictionary<int, RuntimeGuard> s_instances = new();
        private static int s_idCounter;

        internal static RuntimeGuard Get(int gid)
        {
            if (!s_instances.TryGetValue(gid, out RuntimeGuard instance))
                throw new ArgumentException($"Invalid runtime guard ID: {gid}", nameof(gid));
            return instance;
        }

        internal static GidReservation Allocate(IRuntimeGuardSettings settings)
        {
            int gid = Interlocked.Increment(ref s_idCounter);

            var rg = new RuntimeGuard();
            rg.Initialize(settings);

            s_instances[gid] = rg;

            return new GidReservation(gid, r => Free(r.Gid));
        }

        private static void Free(int gid)
        {
            if (!s_instances.TryRemove(gid, out _))
                throw new ArgumentException($"Invalid runtime guard ID: {gid}", nameof(gid));
        }
    }
}

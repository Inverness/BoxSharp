using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace BoxSharp.Runtime.Internal
{
    /// <summary>
    /// Manages an array of RuntimeGuard instances for fast access by scripts.
    /// </summary>
    internal static class RuntimeGuardInstances
    {
        private const double ResizeMult = 1.2;

        private static RuntimeGuard?[] s_instances = new RuntimeGuard?[16];
        private static readonly object s_instanceLock = new object();
        private static int s_idCounter;
        private static readonly ConcurrentQueue<int> s_idQueue = new();

        internal static RuntimeGuard Get(int gid)
        {
            lock (s_instanceLock)
            {
                if (gid >= s_instances.Length || s_instances[gid] == null)
                    throw new InvalidOperationException($"Invalid runtime guard ID: {gid}");
                return s_instances[gid]!;
            }
        }

        internal static GidReservation Allocate(IRuntimeGuardSettings settings)
        {
            if (!s_idQueue.TryDequeue(out int gid))
            {
                gid = NewGid();
            }

            var rg = new RuntimeGuard();
            rg.Initialize(settings);

            lock (s_instanceLock)
            {
                s_instances[gid] = rg;
            }

            return new GidReservation(gid, r => Free(r.Gid));
        }

        private static void Free(int gid)
        {
            Debug.Assert(s_instances[gid] != null);

            lock (s_instanceLock)
            {
                s_instances[gid] = null;
            }

            s_idQueue.Enqueue(gid);
        }

        private static int NewGid()
        {
            int gid = Interlocked.Increment(ref s_idCounter);

            if (gid >= s_instances.Length)
            {
                lock (s_instanceLock)
                {
                    while (gid >= s_instances.Length)
                    {
                        Array.Resize(ref s_instances, (int)(s_instances.Length * ResizeMult));
                    }
                }
            }

            Debug.Assert(s_instances[gid] == null);

            return gid;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static RuntimeGuard GetFast(int gid)
        {
            // TODO Consider generating a static field using RuntimeGuardRewriter that stores the instance
            //
            // Reading the instance without holding s_instanceLock is safe because s_instances is only replaced
            // with a new array when growing in size, so reading either the new or old array will return
            // the same RuntimeGuard instance as long as the GID is still in use.
#if DEBUG
            RuntimeGuard? rg = s_instances[gid];
            if (rg == null)
                ThrowInstanceNotFound();
            return rg!;
#else
            return s_instances[gid]!;
#endif
        }

        internal static GidReservation Allocate(IRuntimeGuardSettings settings)
        {
            if (!s_idQueue.TryDequeue(out int gid))
            {
                gid = NewGid();
            }

            var rg = new RuntimeGuard();
            rg.Initialize(settings);

            SetValue(gid, rg);

            return new GidReservation(gid, r => Free(r.Gid));
        }

        private static void Free(int gid)
        {
            Debug.Assert(s_instances[gid] != null);

            SetValue(gid, null);

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

        private static void SetValue(int gid, RuntimeGuard? value)
        {
            lock (s_instanceLock)
            {
                s_instances[gid] = value;
            }
        }

        private static void ThrowInstanceNotFound()
        {
            throw new InvalidOperationException("Invalid RuntimeGuard ID");
        }
    }
}

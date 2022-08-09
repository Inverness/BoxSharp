using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BoxSharp.Runtime.Internal
{
    internal sealed class RuntimeGuardContext
    {
        private readonly IRuntimeGuardSettings _settings;
        private readonly RuntimeGuard _guard;

        private readonly int _gid;

        private static volatile int s_lastGid;

        [ThreadStatic]
        private static RuntimeGuardContext? t_current;

        // private static readonly ConcurrentDictionary<int, RuntimeGuardContext> s_contexts =
        //     new ConcurrentDictionary<int, RuntimeGuardContext>();

        private RuntimeGuardContext(int gid, IRuntimeGuardSettings settings)
        {
            Debug.Assert(gid != 0);
            _gid = gid;
            _settings = settings;
            _guard = new RuntimeGuard();
        }

        // public int Gid => _gid;

        // public RuntimeGuard Guard
        // {
        //     get { return _guard; }
        // }

        internal static RuntimeGuardContext? Current => t_current;

        internal static RuntimeGuard GetCurrentGuardFast()
        {
            return t_current!._guard;
        }

        internal static RuntimeGuard GetCurrentGuard(int gid)
        {
            // This version of the method is used when entering methods
            // To check if there has been a context switch from guarded code to unguarded code
            // s_lastGid allows a fast check for performance
            return gid == s_lastGid ? t_current!._guard : GetCurrentGuardSlow(gid);
        }

        private static RuntimeGuard GetCurrentGuardSlow(int gid)
        {
            RuntimeGuardContext? context = t_current;
            if (context == null)
            {
                throw new InvalidOperationException("No current context");
                // if (!s_contexts.TryGetValue(gid, out context))
                //     throw new InvalidOperationException();

                // Debug.Assert(context._gid == gid);
                // t_currentOpt = context;
            }
            else if (context._gid != gid)
            {
                throw new InvalidOperationException("Illegal context switch");
            }
            s_lastGid = gid;
            return context._guard;
        }

        internal static RuntimeGuardContext Create(int gid, IRuntimeGuardSettings settings)
        {
            if (gid == 0)
                throw new ArgumentOutOfRangeException(nameof(gid));
            return new RuntimeGuardContext(gid, settings);
        }

        internal static void Run(RuntimeGuardContext context, ContextCallback callback, object? state = null)
        {
            RuntimeGuardContext? old = t_current;
            t_current = context;
            RuntimeGuard guard = context._guard;
            try
            {
                guard.Initialize(context._settings);
                guard.Start();
                callback(state);
            }
            finally
            {
                guard.Stop();
                t_current = old;
            }
        }
    }
}

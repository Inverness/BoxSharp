using BoxSharp.Runtime.Internal;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BoxSharp
{
    // public delegate void BoxScriptAction();

    // public delegate void BoxScriptAction<TArg>(TArg arg);

    // public delegate TRes BoxScriptFunc<TRes>();

    // public delegate TRes BoxScriptFunc<TArg, TRes>(TArg arg);

    public abstract class BoxScript
    {
        // This value may be saved on a captured ExecutionContext, in which case
        // we may need to place the BoxScript reference within another class so that
        // the reference to it can be cleared if the script is disposed.
        private static readonly AsyncLocal<BoxScript?> s_current = new();

        /// <summary>
        /// Gets or sets the current script. This is saved using <see cref="AsyncLocal{T}"/>.
        /// </summary>
        public static BoxScript? Current
        {
            get
            {
                return s_current.Value;
            }

            internal set
            {
                s_current.Value = value;
            }
        }

        /// <summary>
        /// Gets the current script. If there is no current script, throws an <see cref="InvalidOperationException"/.
        /// </summary>
        /// <exception cref="InvalidOperationException">There is no current script.</exception>
        public static BoxScript CurrentEnsured
        {
            get
            {
                BoxScript? current = s_current.Value;
                if (current == null)
                    throw new InvalidOperationException("No current script");
                return current;
            }
        }

        public static Action WithScriptContext(Action action)
        {
            BoxScript? current = Current;
            if (current == null)
                return action;

            return () =>
            {
                current.RunInContextAsync<bool>(() =>
                {
                    action();
                    return default;
                }).GetAwaiter().GetResult();
            };
        }

        public static Action<TArg> WithScriptContext<TArg>(Action<TArg> action)
        {
            BoxScript? current = Current;
            if (current == null)
                return action;

            return arg =>
            {
                current.RunInContextAsync<bool>(() =>
                {
                    action(arg);
                    return default;
                }).GetAwaiter().GetResult();
            };
        }

        public static Func<TRes> WithScriptContext<TRes>(Func<TRes> func)
        {
            BoxScript? current = Current;
            if (current == null)
                return func;

            return () => current.RunInContextAsync(() => new ValueTask<TRes>(func())).GetAwaiter().GetResult();
        }

        public static Func<TArg, TRes> WithScriptContext<TArg, TRes>(Func<TArg, TRes> func)
        {
            BoxScript? current = Current;
            if (current == null)
                return func;

            return arg => current.RunInContextAsync(() => new ValueTask<TRes>(func(arg))).GetAwaiter().GetResult();
        }

        internal abstract ValueTask<TRes> RunInContextAsync<TRes>(Func<ValueTask<TRes>> func, object? globals = null);
    }

    public class BoxScript<T> : BoxScript
    {
        private readonly RuntimeGuard _runtimeGuard;
        private readonly Func<Task<object>> _entryPoint;
        private readonly Action<RuntimeGuard> _runtimeGuardSetter;
        private readonly Action<object?>? _globalsSetter;
        private bool _isDisposed;
        private bool _isInitialized;

        internal BoxScript(
            RuntimeGuard runtimeGuard,
            Func<Task<object>> entryPoint,
            Action<RuntimeGuard> runtimeGuardSetter,
            Action<object?>? globalsSetter)
        {
            _runtimeGuard = runtimeGuard;
            _entryPoint = entryPoint;
            _runtimeGuardSetter = runtimeGuardSetter;
            _globalsSetter = globalsSetter;
        }

        public async Task<T> RunAsync(object? globals = null, CancellationToken token = default)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().Name);

            T result = default!;

            await RunInContextAsync(async () => result = (T) await _entryPoint(), globals);

            return result;
        }

        internal override async ValueTask<TRes> RunInContextAsync<TRes>(Func<ValueTask<TRes>> func, object? globals = null)
        {
            if (!_isInitialized)
            {
                // TODO Set the RuntimeGuard field in BoxCompiler?
                _runtimeGuardSetter(_runtimeGuard);

                if (_globalsSetter != null)
                {
                    if (globals == null)
                        throw new InvalidOperationException("Globals required");
                    _globalsSetter(globals);
                }
                else if (globals != null)
                {
                    throw new InvalidOperationException("Script does not use globals");
                }

                _isInitialized = true;
            }

            BoxScript? oldCurrent = Current;
            Current = this;
            _runtimeGuard.Start();
            try
            {
                return await func();
            }
            finally
            {
                _runtimeGuard.Stop();
                Current = oldCurrent;
            }
        }

        //public void Dispose()
        //{
        //    if (!_isDisposed)
        //    {
        //        //_gid.Dispose();
        //        _isDisposed = true;
        //    }
        //}
    }
}

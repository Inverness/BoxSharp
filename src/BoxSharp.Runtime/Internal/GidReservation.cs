using System;

namespace BoxSharp.Runtime.Internal
{
    /// <summary>
    /// Wraps a GID in a disposable object to ensure that the GID is freed when no longer in use.
    /// </summary>
    internal class GidReservation : IDisposable
    {
        private readonly int _gid;
        private readonly Action<GidReservation> _disposeHandler;
        private bool _isDisposed;

        internal GidReservation(int gid, Action<GidReservation> disposeHandler)
        {
            _gid = gid;
            _disposeHandler = disposeHandler;
        }

        ~GidReservation()
        {
            Dispose(disposing: false);
        }

        public int Gid
        {
            get
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(GetType().Name);
                return _gid;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                _disposeHandler(this);
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}

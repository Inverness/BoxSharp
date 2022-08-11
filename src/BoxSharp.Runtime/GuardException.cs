
using System;
using System.Runtime.Serialization;

namespace BoxSharp.Runtime
{
    [Serializable]
    public class GuardException : Exception
    {
        internal GuardException() { }
        internal GuardException(string message) : base(message) { }
        internal GuardException(string message, Exception? inner) : base(message, inner) { }
        protected GuardException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}

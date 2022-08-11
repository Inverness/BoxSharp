
using System;
using System.Runtime.Serialization;

namespace BoxSharp.Runtime
{
    [Serializable]
    public sealed class TimeGuardException : GuardException
    {
        internal TimeGuardException() : base("Time limit reached.") { }
        internal TimeGuardException(Exception? inner) : base("Time limit reached.", inner) { }
        internal TimeGuardException(string message) : base(message) { }
        internal TimeGuardException(string message, Exception? inner) : base(message, inner) { }
        protected TimeGuardException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}


using System;
using System.Runtime.Serialization;

#pragma warning disable RCS1194

namespace BoxSharp.Runtime
{
    [Serializable]
    public class RateGuardException : GuardException {
        internal RateGuardException() : base("Operation limit reached.") {}
        protected RateGuardException(SerializationInfo info, StreamingContext context) : base(info, context) {}
    }
}

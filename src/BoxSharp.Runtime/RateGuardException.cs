
using System;
using System.Runtime.Serialization;

#pragma warning disable RCS1194

namespace BoxSharp.Runtime
{
    [Serializable]
    public sealed class RateGuardException : GuardException
    {
        internal RateGuardException() : base("Operation limit reached.") {}
        public RateGuardException(SerializationInfo info, StreamingContext context) : base(info, context) {}
    }
}

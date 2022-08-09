using System;

namespace BoxSharp
{
    /// <summary>
    /// Represents a whitelisted symbol declaration.
    /// </summary>
    /// <param name="DeclarationId"></param>
    /// <param name="IncludeChildren"></param>
    public record WhitelistSymbol(string DeclarationId, bool IncludeChildren) : IComparable<WhitelistSymbol>
    {
        public bool IsType => DeclarationId.StartsWith("T:");

        public int CompareTo(WhitelistSymbol other)
        {
            int c = DeclarationId.CompareTo(other.DeclarationId);
            if (c != 0)
                return c;
            return IncludeChildren.CompareTo(other.IncludeChildren);
        }

        public string? GetTypeName()
        {
            if (!IsType)
                return null;

            return DeclarationId.Substring(2);
        }
    }
}

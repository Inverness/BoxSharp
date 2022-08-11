using System;

namespace BoxSharp
{
    /// <summary>
    /// Represents a whitelisted symbol declaration.
    /// 
    /// See this page for documentation on the declaration ID format:
    /// https://github.com/dotnet/csharpstandard/blob/draft-v7/standard/documentation-comments.md#d42-id-string-format
    /// </summary>
    /// <param name="DeclarationId"></param>
    /// <param name="IncludeChildren"></param>
    public record WhitelistSymbol(string DeclarationId, bool IncludeChildren) : IComparable<WhitelistSymbol>
    {
        public bool IsType => GetDecType(DeclarationId) == 'T';

        public int CompareTo(WhitelistSymbol other)
        {
            // We compare the declaration ID type second, this is so when outputting a list of
            // whitelist symbols, members will be sorted with their parent type.
            int c = string.CompareOrdinal(DeclarationId, 2, other.DeclarationId, 2, 1024);
            if (c != 0)
                return c;
            c = GetDecTypeOrder(GetDecType(DeclarationId)).CompareTo(GetDecTypeOrder(GetDecType(other.DeclarationId)));
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

        private static int GetDecTypeOrder(char decType)
        {
            // Namespaces are not used
            switch (decType)
            {
                case 'T':
                    return 0;
                case 'F':
                    return 1;
                case 'E':
                    return 2;
                case 'P':
                    return 3;
                case 'M':
                    return 4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(decType));
            }
        }

        private static char GetDecType(string decId)
        {
            if (decId.Length < 3 || decId[1] != ':')
                throw new ArgumentException();
            return decId[0];
        }
    }
}

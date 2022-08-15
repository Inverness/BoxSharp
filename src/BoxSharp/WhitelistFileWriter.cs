using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BoxSharp
{
    /// <summary>
    /// Writes lists of <see cref="WhitelistSymbol"/> to files.
    /// </summary>
    public static class WhitelistFileWriter
    {
        private static readonly Encoding s_encoding = new UTF8Encoding(false);

        public static async Task WriteAsync(string path, IEnumerable<WhitelistSymbol> symbols)
        {
            using FileStream stream = FileUtilities.OpenAsyncWrite(path);

            await WriteAsync(stream, symbols).ConfigureAwait(false);
        }

        public static async Task WriteAsync(Stream stream, IEnumerable<WhitelistSymbol> symbols)
        {
            using var writer = new StreamWriter(stream, s_encoding, 4096, true);

            foreach (WhitelistSymbol symbol in symbols)
            {
                await writer.WriteAsync(symbol.DeclarationId).ConfigureAwait(false);

                if (symbol.IncludeChildren)
                {
                    await writer.WriteAsync(".*").ConfigureAwait(false);
                }

                await writer.WriteLineAsync().ConfigureAwait(false);
            }
        }
    }
}

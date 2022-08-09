using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BoxSharp
{
    internal static class WhilelistFileReader
    {
        public static async Task<List<WhitelistSymbol>> LoadAsync(string path)
        {
            using FileStream stream = FileUtilities.OpenAsyncRead(path);
            return await LoadAsync(stream);
        }

        public static async Task<List<WhitelistSymbol>> LoadAsync(Stream stream)
        {
            var list = new List<WhitelistSymbol>();

            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
            {
                while (true)
                {
                    string? line = await reader.ReadLineAsync().ConfigureAwait(false);

                    if (line == null)
                        break;

                    line = line.Trim();

                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    string decId;
                    bool includeChildren;

                    if (line.EndsWith(".*"))
                    {
                        decId = line.Substring(0, line.Length - ".*".Length);
                        includeChildren = true;
                    }
                    else
                    {
                        decId = line;
                        includeChildren = false;
                    }

                    list.Add(new WhitelistSymbol(decId, includeChildren));
                }
            }

            return list;
        }
    }
}

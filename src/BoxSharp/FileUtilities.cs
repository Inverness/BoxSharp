using System.IO;
using System.Text;

namespace BoxSharp
{
    internal static class FileUtilities
    {
        internal static Encoding Utf8NoBomEncoding = new UTF8Encoding(false);

        public static FileStream OpenAsyncRead(string path)
        {
            return new FileStream(path,
                                  FileMode.Open,
                                  FileAccess.Read,
                                  FileShare.Read,
                                  bufferSize: 4096,
                                  useAsync: true);
        }

        public static FileStream OpenAsyncWrite(string path)
        {
            return new FileStream(path,
                                  FileMode.Create,
                                  FileAccess.Write,
                                  FileShare.None,
                                  bufferSize: 4096,
                                  useAsync: true);
        }
    }
}

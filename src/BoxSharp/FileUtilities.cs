using System.IO;

namespace BoxSharp
{
    internal static class FileUtilities
    {
        public static FileStream OpenAsyncRead(string path)
        {
            return new FileStream(path,
                                  FileMode.Open,
                                  FileAccess.Read,
                                  FileShare.Read,
                                  bufferSize: 4096,
                                  useAsync: true);
        }
    }
}

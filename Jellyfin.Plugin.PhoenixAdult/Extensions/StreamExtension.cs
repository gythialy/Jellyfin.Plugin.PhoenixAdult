using System.IO;

namespace PhoenixAdult.Extensions
{
    public static class StreamExtensions
    {
        public static byte[] ToBytes(this Stream input)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}

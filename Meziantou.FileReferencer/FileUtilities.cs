using System.Text;

namespace Meziantou.FileReferencer;
internal static class FileUtilities
{
    public static Encoding GetEncoding(string path)
    {
        var fs = File.OpenRead(path);
        try
        {
            return GetEncoding(fs);
        }
        finally
        {
            fs.Dispose();
        }
    }

    public static Encoding GetEncoding(Stream stream)
    {
        var bom = new byte[4];
        var readCount = stream.ReadAtLeast(bom, 4, throwOnEndOfStream: false);

        if (readCount >= 3 && bom is [0x2b, 0x2f, 0x76, ..])
#pragma warning disable SYSLIB0001 // Type or member is obsolete
            return Encoding.UTF7;
#pragma warning restore SYSLIB0001

        if (readCount >= 3 && bom is [0xef, 0xbb, 0xbf, ..])
            return Encoding.UTF8;

        if (readCount >= 2 && bom is [0xff, 0xfe, ..])
            return Encoding.Unicode; //UTF-16LE

        if (readCount >= 2 && bom is [0xfe, 0xff, ..])
            return Encoding.BigEndianUnicode; //UTF-16BE

        if (readCount >= 4 && bom is [0, 0, 0xfe, 0xff, ..])
            return Encoding.UTF32;

        return Encoding.Default;
    }
}

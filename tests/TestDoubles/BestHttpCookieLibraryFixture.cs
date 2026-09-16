using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// BestHttp cookie 库（<c>Cookies</c> 文件）的二进制写入夹具。
/// </summary>
/// <remarks>
/// 这个字节布局是被测契约本身（<c>BestHttpCookieLibraryService</c> 读的就是它），所以它必须有
/// 一处定义：手写四遍会让四个套件各自证明一个略有出入的格式，而差异不会被任何断言看见。
/// </remarks>
public static class BestHttpCookieLibraryFixture
{
    /// <summary>固定的 uid 条目字段值：cookie 名、epoch、MaxAge 与 session 标志。</summary>
    private const string UidCookieName = "uid";

    private const long UidMaxAge = 2147483647L;

    /// <summary>
    /// 写入一个含单个 <c>uid</c> 条目的库文件。<paramref name="uid"/> 为空时写零条目
    /// （计数字段为 0），这正是「库存在但没有 uid」的形状。
    /// </summary>
    public static async Task WriteUidAsync(
        string path,
        string uid,
        string domain = "bluearchive.cafe",
        string cookiePath = "/")
    {
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1);
        if (string.IsNullOrEmpty(uid))
        {
            writer.Write(0);
            await stream.FlushAsync();
            return;
        }

        writer.Write(1);
        writer.Write(1);
        writer.Write(UidCookieName);
        writer.Write(uid);
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.FromBinary(0).ToBinary());
        writer.Write(UidMaxAge);
        writer.Write(false);
        writer.Write(domain);
        writer.Write(cookiePath);
        writer.Write(false);
        writer.Write(false);
        writer.Flush();
    }

    /// <summary>
    /// 用字节级回调构造一个库流，供畸形输入（负计数、尾随字节、缺字段）的用例精确控制写入内容。
    /// </summary>
    public static MemoryStream Stream(Action<BinaryWriter> write)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            write(writer);
        }

        stream.Position = 0;
        return stream;
    }
}

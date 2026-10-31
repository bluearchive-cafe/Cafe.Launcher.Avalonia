using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>读取本地 BestHTTP Cookie 库；解析和文件所有权由 Core 实现负责。</summary>
public interface IBestHttpCookieLibraryReader
{
    /// <summary>读取指定库文件；文件不可读或格式损坏时向调用方报告异常。</summary>
    BestHttpCookieLibrary Read(string libraryPath);
}

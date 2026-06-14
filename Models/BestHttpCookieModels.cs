using System;
using System.Collections.Generic;

namespace Cafe.Launcher.Avalonia.Models;

public sealed record BestHttpCookieLibrary(
    int Version,
    IReadOnlyList<BestHttpCookie> Cookies);

public sealed record BestHttpCookie(
    int Version,
    string Name,
    string Value,
    DateTime Date,
    DateTime LastAccess,
    DateTime Expires,
    long MaxAge,
    bool IsSession,
    string Domain,
    string Path,
    bool IsSecure,
    bool IsHttpOnly);

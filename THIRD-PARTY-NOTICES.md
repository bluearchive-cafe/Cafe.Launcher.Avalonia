# Third-Party Notices

This file lists the NuGet packages distributed with Cafe Launcher and their licenses.
Regenerate with `scripts/New-ThirdPartyNotices.ps1` after changing dependencies.

Cafe Launcher itself is licensed under the MIT License; see `LICENSE`.

## Production projects scanned

The table below is the union of the resolved dependency graphs of every production
project, so a package referenced by only one of them is still disclosed:

- `Cafe.Launcher.Avalonia.UI`
- `Cafe.Launcher.Avalonia`
- `Cafe.Launcher.Core`
- `Cafe.Launcher.Updater.Core`
- `Cafe.Launcher.Updater`

## Self-contained .NET runtime

Release archives are self-contained: besides the packages below they redistribute the .NET
runtime and apphost bundled with the publishing SDK — `Microsoft.NETCore.App` from pinned .NET SDK `10.0.302`.
Both are MIT-licensed (https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and are not
resolved as NuGet packages, so they cannot appear in the table below: the table lists exactly
what `dotnet restore` resolves, and the RID-specific publish closure is outside its scope.
The archives carry this file and `LICENSE` next to the binaries.

| Package | Version | License | Required by | Source |
| --- | --- | --- | --- | --- |
| Avalonia | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | LICENSE ([text](https://aka.ms/deprecateLicenseUrl)) | Avalonia | https://avaloniaui.net/ |
| Avalonia.BuildServices | 11.3.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia | https://avaloniaui.net/ |
| Avalonia.Controls.ColorPicker | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.Desktop | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.FreeDesktop | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.FreeDesktop.AtSpi | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.HarfBuzz | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.Native | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.Remote.Protocol | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.Skia | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.Themes.Fluent | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.Win32 | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| Avalonia.X11 | 12.1.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | see package | Avalonia | https://avaloniaui.net/ |
| CommunityToolkit.Mvvm | 8.4.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia | https://github.com/CommunityToolkit/dotnet |
| HarfBuzzSharp | 8.3.1.3 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.3 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.3 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.3 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| Markdig | 1.3.2 | BSD-2-Clause ([text](https://licenses.nuget.org/BSD-2-Clause)) | Avalonia.UI, Avalonia | https://xoofx.github.io/markdig |
| MarkView.Avalonia | 12.2.1 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia | https://github.com/Kryptos-FR/MarkView.Avalonia |
| Material.Icons | 3.0.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia | https://github.com/SKProCH/Material.Icons/ |
| Material.Icons.Avalonia | 3.0.2 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia | https://github.com/AvaloniaUtils/Material.Icons.Avalonia/ |
| MicroCom.Runtime | 0.11.6 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia | - |
| Microsoft.Extensions.DependencyInjection | 10.0.12 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia, Core | https://dot.net/ |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.12 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia.UI, Avalonia, Core | https://dot.net/ |
| Microsoft.Extensions.Logging.Abstractions | 8.0.0 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://dot.net/ |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://github.com/Microsoft/Microsoft.IO.RecyclableMemoryStream |
| Serilog | 4.4.0 | Apache-2.0 ([text](https://licenses.nuget.org/Apache-2.0)) | Avalonia.UI, Avalonia, Core | https://serilog.net/ |
| Serilog.Sinks.Async | 2.1.0 | Apache-2.0 ([text](https://licenses.nuget.org/Apache-2.0)) | Avalonia.UI, Avalonia, Core | https://serilog.net/ |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 ([text](https://licenses.nuget.org/Apache-2.0)) | Avalonia.UI, Avalonia, Core | https://github.com/serilog/serilog-sinks-file |
| Shirasagi0012.MaterialColorUtilities | 0.2.0 | Apache-2.0 ([text](https://licenses.nuget.org/Apache-2.0)) | Avalonia.UI, Avalonia | https://github.com/Shirasagi0012/MaterialColorUtilities |
| SkiaSharp | 3.119.4 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.WebAssembly | 3.119.4 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | https://go.microsoft.com/fwlink/?linkid=868515 |
| Tmds.DBus.Protocol | 0.94.1 | MIT ([text](https://licenses.nuget.org/MIT)) | Avalonia | - |

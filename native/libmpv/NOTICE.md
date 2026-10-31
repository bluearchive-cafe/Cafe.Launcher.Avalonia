# Windows video background runtime

Cafe dynamically loads the unmodified `win-x64/libmpv-2.dll` next to this file.
Users may replace that DLL with an ABI-compatible libmpv build. Cafe does not
embed the library in its assemblies or download libraries during application use.

- Binary: [zhongfly/mpv-winbuild, 2026-10-31-9e2ed65eb0, LGPL x86_64 dev build](https://github.com/zhongfly/mpv-winbuild/releases/tag/2026-10-31-9e2ed65eb0).
- Archive and DLL SHA-256 values: `manifest.json` in this directory.
- mpv source: https://github.com/mpv-player/mpv/tree/9e2ed65eb0
- FFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/4811625b5
- Build recipes, dependency definitions and LGPL build patches: https://github.com/zhongfly/mpv-winbuild/tree/02153edbac08b812b9e8076df43051e1e735cc4c

mpv is copyright its contributors and is built with `-Dgpl=false` in this
archive. Its LGPL-2.1-or-later terms are in `COPYING.LGPL`; the associated
GPL text is in `COPYING.GPL`. FFmpeg and other libraries linked into the DLL
retain their respective copyrights. The LGPL FFmpeg variant enables version 3;
the combined native binary is distributed under LGPL-3.0-or-later. See
`COPYING.LGPLv3` and `COPYING.GPLv3`. Cafe's own source remains MIT-licensed.

To reproduce or modify the native binary, obtain the sources at the revisions
above and the upstream build recipes, including their dependency sources.
Keep this notice, the manifest and license texts with any redistributed DLL.

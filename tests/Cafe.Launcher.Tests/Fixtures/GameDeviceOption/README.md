# GameDeviceOption fixtures

`DeviceOption` 是 Blue Archive 客户端写在用户配置目录下的设备级选项文件
（`AppData/LocalLow/YostarJP/BlueArchive/DeviceOption`）。这些夹具只用于只读解析的回归测试，
不代表启动器已获得写入该文件的许可。

- `device-option-1.74.json`：2026-11-21 实机 1.74.0 文件的字段与值转写；用于钉住
  `Mute`、`ToggleValue` 的反向语义以及 PC 字段的原始值。
- `device-option-calibration-1.74.json`：2026-11-22 手动改动后的快照；确认
  `ScreenModeType=1` 为窗口、`ScreenRatioType=0` 为 4:3、`FPS=1` 为 30 FPS。
- `device-option-pre-pc.json`：1.7x 旧结构，缺少 `Master` 与 PC 窗口字段；用于验证旧文件仍可读。
- `device-option-future-field.json`：在 1.74 基础上追加未知字段；用于验证前向兼容只报告字段名。
- `device-option-uncalibrated-enum.json`：`FPS = 7`；用于钉住「未知枚举必须整份拒绝」。
- `device-option-invalid-audio.json`：`BGM.Volume = 1.5`；用于钉住音量范围校验。
- `device-option-missing-required.json`：缺少 `Resolution`；用于钉住必需字段校验。

字段语义来自 `E:\Repos\_bluearchive\Blue-Archive-JP-Downloader\dump.cs`（2026-07 构建）与
2026-11-21 / 2026-11-22 的 1.74 实机文件。`ScreenModeType`、`ScreenRatioType` 与 `FPS` 已校准；
`renderScale.scale`、`VSync`、`BackgroundSound`、`ControlDisplay`、`MXMouseCursor` 尚未校准，
模型保留原始整数或原始值。

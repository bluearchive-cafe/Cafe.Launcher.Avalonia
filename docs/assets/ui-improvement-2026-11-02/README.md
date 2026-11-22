# 通用回退背景

- 日期：2026-11-02。
- 状态：2026-11-02 随整体方案确认采用，已接入 UI 资源作为最终回退背景。
- 文件：[shared-fallback-candidate.png](shared-fallback-candidate.png)。
- 实际尺寸：1672×941，PNG，1,805,469 字节；生成尺寸与提示词中的理想尺寸不同，运行时沿用原始分辨率并按背景适配规则呈现。
- SHA-256：`9aace3649b5d976eaf766109ff2d405b782fc6ad3877a809cd988fb7eb0e386f`。
- 用途：当前游戏自身内置图也不可用时，所有游戏共享的最终回退背景；远程／自定义异常优先回退该游戏自己的内置图。各游戏内置图和远程壁纸另行配置。
- 生成方式：内置 imagegen 工具；全新生成，没有输入参考图。
- 视觉检查：蓝灰色抽象层叠山形，中央与右侧留白，没有文字、图标、界面或游戏角色；仅完成资产画面检查，尚未叠加真实控件验证浅深主题、窗口裁切或对比度。
- 方案：[整体界面改进方案](../../design/design-system-spec.md#41-主窗口标题栏与任务表面)。

## 最终生成提示词

```text
Use case: stylized-concept. Asset type: a universal fallback wallpaper for Cafe Launcher, a Material 3 desktop game launcher. Generate a polished, original, game-neutral raster artwork, landscape 16:9 composition, ideally 2560x1440. An atmospheric, minimal abstract landscape made from broad softly folded matte paper-like surfaces, cool desaturated blue-grey and quiet slate with a very subtle mist-blue glow. Elegant soft studio lighting and fine natural material variation; calm, reassuring, restrained, not dramatic. Rich enough to look intentional when a game wallpaper fails, quiet enough to sit behind a desktop application. Large uncluttered central negative space, low contrast at the left edge for a navigation rail, upper-right and lower-right areas especially calm for translucent info and launch panels. Subtle abstract forms may concentrate in the lower-left and distant middle-left. This is a background asset alone, not an application screenshot or UI mockup. No text, no letters, no words, no logo, no watermark, no error symbol, no icons, no buttons, no interface panels, no characters, no game imagery, no bright neon, no high-contrast streaks. Opaque full-bleed background.
```

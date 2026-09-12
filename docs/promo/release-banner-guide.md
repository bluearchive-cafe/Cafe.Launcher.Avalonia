# 发布横幅生成流程

本仓库每个发行版本配一张 2000×1125 的发布横幅，位于 `docs/assets/release-banners/`，
文件名固定为 `cafe-launcher-<tag>-release-banner.png`，由 `CHANGELOG_RELEASE.md` 引用。
`release.yml` 的「Verify release banner」步骤在打 tag 时校验该文件存在，缺失即构建失败。

横幅由 `promotional-image` skill 的声明式管线产出：spec 描述设计，渲染器把 spec 变成
PNG，manifest 记录这次渲染的证据。**操作说明书在 skill 自己那里，不在本仓库**
（`%USERPROFILE%\.agents\skills\promotional-image\`），本文只记录本仓库的模板怎么用、
踩过哪些坑。参考按 skill 自己的顺序读：先 `SKILL.md`；**创作前**读
`references/material-poster-guidance.md`（配色与版式语言）；**写 v2 字段时**读
`references/campaign-spec.md`（字段含义）；**交付前**读 `references/visual-integrity.md`。

本文对应 `docs/promo/specs/release-banner.template.json`，它已是 **schema v2 的 scene 规格**。
v2 是 skill 的默认创作路径；v1 只能用来重跑老海报，它的档位被渲染器钉死在 `legacy`，
拿不到 `release` 档位的字体门禁（见「校验」一节），因此本仓库不再用 v1。此前（含
`v1.1.0-beta.8`）的横幅是临时手写 HTML + 无头 Chrome 截图产出的，不走本文流程：那种做法没有
spec 可留档、没有字体可用性与版式报告、重跑结果无法比对。

## 为什么用 spec 驱动

一条命令截一张图很快，但复现不了：定稿的只有 PNG 本身，配色、字号、定位全在一次性脚本里。
声明式 spec 把设计意图变成可 diff、可回溯、可重跑的源文件，并顺带提供三项此前没有的保证：

- **确定性**：同一 spec 连续渲染两次逐字节一致，写入 manifest 供事后核验。
- **字体证据**：记录声明字体栈中每个字体是否真的被解析到，避免中文字形静默回退。
- **版式报告**：记录文字溢出、越界、元素碰撞，即使判定为可接受也留痕。

## 前置条件

管线依赖 Python（实测 3.14）与 skill 的依赖组：

```powershell
pip install -r "$env:USERPROFILE\.agents\skills\promotional-image\requirements.txt"
playwright install chromium
```

- `requirements.txt`：`Pillow>=10`、`jsonschema>=4.20`、`playwright>=1.40`。
  Pillow 在 `inspect` 阶段就会被导入（`inspect_campaign.py` 复用 `prepare_assets.py` 的函数），
  因此 **零素材也必须装**。
- 渲染器默认用 Playwright 的 Chromium（本机装在
  `%LOCALAPPDATA%\ms-playwright\chromium-1234\chrome-win64\`）；取不到时才退到用 `shutil.which`
  在 **PATH** 上找 `chromium` / `chromium-browser` / `google-chrome` / `google-chrome-stable` /
  `msedge` / `microsoft-edge`。注意「装了」不等于「找得到」：本机 Chrome
  （`C:\Program Files\Google\Chrome\Application\chrome.exe`）与 Edge
  （`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`）**都装着，但都不在 PATH 上**，
  实测 `find_system_chromium()` 返回 `None`。要用系统浏览器就把完整路径写进 `CHROMIUM_EXECUTABLE`
  ——该变量只在路径确实指向文件时才生效，写错会静默回落到 PATH 查找。
- **Node 只在用种子色时才需要**：`material.seed_color` / `material.source_asset` 会调用
  `scripts/material_theme.mjs`，用 Google 的 `@material/material-color-utilities` 生成明暗两套
  方案，需要先在 skill 目录 `npm install`。本模板不用种子色，走显式令牌，因此 **不需要 Node**。

skill 根目录记为 `$SKILL = "$env:USERPROFILE\.agents\skills\promotional-image"`。流程里的脚本都以
skill 根为基准定位模板与 schema，可从任意工作目录调用（必须按路径调用，不要以模块方式导入）。

## 步骤

### 1. 复制 spec 模板

```powershell
Copy-Item docs\promo\specs\release-banner.template.json docs\promo\specs\v1.1.0-beta.9.spec.json
```

每个发版都留一份自己的 spec 并入库，这是「设计可复现」的全部意义所在——只留 PNG 的话，
配色、字号、定位就全变成不可追溯的一次性结果了。

替换占位符用 PowerShell 7（`pwsh`）的 `-Encoding utf8NoBOM`——本机实测唯一可靠的写法：

```powershell
$spec = "docs\promo\specs\v1.1.0-beta.9.spec.json"
(Get-Content $spec -Raw) -replace "vX\.Y\.Z-beta\.N", "v1.1.0-beta.9" |
    Set-Content $spec -NoNewline -Encoding utf8NoBOM
```

**不要用 Windows PowerShell 5.1 做这一步**：它的 `Get-Content` 会按系统 ANSI 代码页解码无 BOM 的
UTF-8，正文先变成乱码，随后两种写法都会坏，只是坏法不同：

| 5.1 的写法 | 实测结果 |
| --- | --- |
| `Get-Content -Raw` + `Set-Content`（默认编码） | 乱码再按同一代码页写回，GBK 往返对部分字节序列有损：spec 变成**非法 UTF-8**，脚本报 `spec … is not valid UTF-8: 'utf-8' codec can't decode bytes in position N-N`（位数随文件内容变） |
| `Get-Content -Raw` + `Set-Content -Encoding UTF8` | BOM 之外，那份乱码还被**二次编码**成 UTF-8，产生 C1 控制字符：报 `spec … is not valid JSON: line L, column C: Invalid control character`；侥幸解析出来文案也是坏的 |

**BOM 本身不是问题**，别把上面第二种坑归因给它：管线用 `utf-8-sig` 读 JSON
（`json_io.load_json_document` 的 docstring 就是 "Load UTF-8 JSON with optional BOM"，
skill 的 `SKILL.md` 也写明 "JSON inputs may be UTF-8 with or without a BOM"）。
致命的是被 ANSI 解码过的正文，而不是文件开头的三个字节。

两种坏法都**由 `json_io` 包成一行干净的 `spec …: 原因` 报出**（`SystemExit`、退出码 1），
不是 Python traceback。`Set-Content` 写出的换行是 CRLF，但 `.gitattributes` 的
`*.json text eol=lf` 会在入库时归一化为 LF，不影响提交。

如果没有 `pwsh`，就用编辑器手工替换。每次发版要改的不止 tag，共三类：

1. `scene → copy-zone → copy → actions → badge` 的文本 `vX.Y.Z-beta.N` → 真实 tag。
2. `output` 四个路径中的 `vX.Y.Z-beta.N`。
3. **本次更新的文案**：副标题与三个胶囊。模板里现在写的是上一次发版的示例内容，发版时必须重写
   （见下方「文案与用词」）。

替换后确认 `badge` 与 `output.image` 都已是真实 tag，且**没有残留** `vX.Y.Z-beta.N`：

```powershell
Select-String -Path $spec -Pattern "vX\.Y\.Z-beta\.N"    # 应该没有任何输出
```

`output` 的相对路径以 **spec 文件所在目录** 为基准解析，因此同目录复制无需改动：
`../../assets/release-banners/…`（成品入库）与 `../../../artifacts/release-banner/…`
（中间产物落在被 gitignore 的 `artifacts/`）。

#### 文案与用词

文案取自 `CHANGELOG_RELEASE.md`：`badge` 是 tag，副标题是本次 `> [!NOTE]` 焦点摘要的一句话，
三个胶囊是这次最重要的三项改进。**模板里现有的副标题与胶囊是上一次发版的示例文案**，
每次发版都要按本次更新重写，别只换 tag 就交付。

胶囊里点名按钮、标签或状态时，以 `Resources/LauncherStrings.zh-Hans.resx` 的实际用词为准。
本仓库实测：资源里有 `logExportIncludeCrashReports = 崩溃报告` 及其说明条目，但没有
「崩溃诊断」「启动提速」这类词——所以模板里那两个胶囊是**编辑性短语**，不是界面上的字面标签。
要写编辑性短语可以，但不能让人误以为界面上有同名按钮；要指代真实控件、状态或提示时，
必须先回资源文件核对用词（例如崩溃窗口在资源里叫「崩溃报告」）。

### 2. 校验 spec

```powershell
python "$SKILL\scripts\inspect_campaign.py" docs\promo\specs\v1.1.0-beta.9.spec.json
```

打印 spec 版本、material、layout 模式与渲染契约、canvas 与 orientation、verification、delivery、
素材数与来源分布（`authentic` / `decorative` / `generative`）、asset records、claims、场景节点数，
以及（若 manifest 已存在）上一次渲染的版式报告。注意：

- schema 违反**不是 traceback**：脚本打印一段以 `schema validation failed:` 开头的消息，
  **一次列出全部错误**（`schema_utils.validate_document` 用 `iter_errors` 收集后按路径排序、去重），
  改完再跑一次即可，不必逐错来回试。
- 本模板预期输出中**没有任何 `notice`**。notice 逻辑整个只对 `schema_version == 1` 生效，
  所以 v2 的 spec 不可能触发它；真出现了说明 spec 被改回了 v1。
- 结尾那行**顺带重放上一次渲染的记录**，不是本次检查的结论。它的前缀是 `previous layout report:`：
  首次渲染前打印 `previous layout report: not rendered yet — it will be written to <manifest 路径>`，
  之后改印上次的计数（本模板实测 `passed=None policy=advisory errors=0 warnings=0 allowed_findings=5
  unchecked=3 images=4`；`passed` 恒为 `None`，因为该键只有 v1 的 manifest 才有）。
  本次的真实结论要看第 6 步。
- 这一步**不做版式校验**：它看不到构图，所以永远不会报溢出、出血或碰撞（脚本 docstring 亦如此声明）。

### 3. 准备素材

```powershell
python "$SKILL\scripts\prepare_assets.py" docs\promo\specs\v1.1.0-beta.9.spec.json
```

把每个素材按声明裁切/做圆角后写入 `output.components_dir`，并产出 `output.prepared`，最后**只把
prepared 的绝对路径打印到 stdout**。素材路径同样以 spec 文件目录为基准解析。零素材的海报也合法，
该步骤仍会建立空的 `components/` 目录。图标都是 PNG（不是 SVG），因此这一步只做格式规整，不改像素
——实测四个图标的 `source_sha256` 与 `component_sha256` 完全相同，连字节都没变。

复制这个打印出来的路径给第 4 步；`render_image.py` 不读 prepared 里的 `output.prepared`，
只认它自己的命令行参数。

### 4. 渲染并校验确定性

```powershell
python "$SKILL\scripts\render_image.py" artifacts\release-banner\v1.1.0-beta.9.prepared.json --verify-determinism
```

`--verify-determinism` 会完整渲染两次（第二次输出到临时目录），要求 PNG 的 sha256 与版式
报告都完全一致，否则以退出码 1 失败。结果记入 `manifest.rendering.deterministic_same_environment`。

**这一步就是 `release` 档位字体闸门的发作点**：任一非通用族 `MISSING` 会让渲染以退出码 1 终止，
不会产出一张字形回退过的 PNG。报错形式实测是 `render_once()` 把 `LayoutValidationError` 重新包成
`RuntimeError`，所以看到的是一段 **traceback**，末行才是关键：

```
RuntimeError: layout validation failed before export: release profile requires available fonts: Noto Sans CJK SC
```

看到这个错误不要去降档位，去改字体栈（见「字体栈必须逐个探针」）。

### 5. 无损压缩（建议做）

```powershell
python "$SKILL\scripts\optimize_export.py" artifacts\release-banner\v1.1.0-beta.9.manifest.json
```

`verify_export.py` 把交付文件绑定到渲染时记录的哈希，所以**手工重新编码（压缩、缩放、改格式）
会静默让校验失效**。这个脚本是唯一许可的再编码入口：用更好的压缩策略重编码，逐像素比对确认
无损后才替换，并把 manifest 里的哈希重绑。实测可省约 13%（约 690 KB → 600 KB，见「实测基线」），
值得跑；`--dry-run` 只报收益不动文件。

### 6. 校验交付物

```powershell
python "$SKILL\scripts\verify_export.py" artifacts\release-banner\v1.1.0-beta.9.manifest.json --require-deterministic
```

独立复核 manifest 身份、PNG 尺寸与 sha256，以及 manifest 中记录的每一个文件哈希（素材、组件、
模板契约与 CSS、渲染器源码、两个 schema、prepared 清单），并打印版式报告与字体可用性。
`--require-deterministic` 额外要求第 4 步确实跑过确定性校验。只想要一行结论时可以加 `--no-report`，
跳过版式报告与字体清单的打印。

注意 manifest 记的是**绝对路径与当时的 schema 哈希**，所以它是「本机、本 skill 版本」的证据：
换机器或 skill 更新后，旧 manifest 会校验失败。spec 才是可重跑的那一份。

### 7. 目视复核并入库

**版式校验只证明「没有越界和碰撞」，不证明「好看」**——对比度、层次、平衡、装饰是否压住文字，
`unchecked` 里那三项自动化永远给不出结论。所以这一步不能省，而且要按「成品尺寸」看，不要缩放后扫一眼。

按顺序确认：

1. 打开 PNG，**先缩到 25% 左右看灰度直觉**：标题是否第一眼就读到，`Cafe Launcher` 与三个胶囊
   的层级是否还成立，右上图标群有没有抢走注意力。
2. 再看 100%：中文字形是否被裁、胶囊文字是否居中、图标旋转后有无不自然的接缝。
3. 交叉核对文案：`badge` 是否是本次真实 tag；胶囊用词是否与
   `Resources/LauncherStrings.zh-Hans.resx` 一致（见第 1 步）。
4. 确认文件在 `docs/assets/release-banners/`，文件名与 `CHANGELOG_RELEASE.md` 里的引用**逐字一致**。

改动构图后不要只看最终这张：skill 建议准备一张**结构验收样本**再相信结构性改动——长中文标题、
三个胶囊、URL/徽章、一张装饰素材，横竖两种画布各跑一次，确认没有溢出与裁切。本仓库只需横版，
但「长中文标题」这一项值得每次发版都试：副标题是唯一会随更新内容变长变短的元素，`text_overflow`
报告为空不代表换行结果好看。

spec 与图标资产一并提交。**入库清单**：`docs/promo/specs/<tag>.spec.json`（新增）、
`docs/assets/release-banners/cafe-launcher-<tag>-release-banner.png`（新增）、必要时
`docs/promo/assets/icons/`（改动时）。`artifacts/` 下的中间产物不入库。

## 配置与输出约定

| 项 | 取值 |
| --- | --- |
| `canvas.preset` | `custom`（尺寸由 width/height 决定，preset 只是标签，不做校验） |
| `canvas.width` / `height` | `2000` / `1125` |
| `canvas.device_scale_factor` | `1` |
| `orientation` | 由宽高自动算出 `landscape`，写进 `prepared` 与 manifest |
| 内部 `scale` | `sqrt((2000×1125)/(1920×1080))` ≈ `1.041667` |

**`device_scale_factor` 的约束在 schema 里，不是约定。** `promo-v2.schema.json` 把它写成
`{"const": 1}`，所以任何非 1 的取值在 `inspect`/`prepare` 阶段就被 schema 拒掉，根本走不到渲染器
（渲染器自己也另有一句 `requires device_scale_factor=1` 兜底）。1 是必需的：非 1 会让导出的
像素尺寸翻倍，而 manifest 的 `output.dimensions` 仍按 `canvas` 原值记录。

`scale` 这个系数**没有命令行开关**。`render_image.py` 的 argparse 只有 `prepared` 和
`--verify-determinism` 两个参数；`scale` 是 `runtime.js` 在 `setVars()` 里自己算出来的，只用来
在 spec 没写时生成三个 CSS 变量的兜底值，函数体就是
`Math.round(基准值 × scale)`，基准值是 **72 / 32 / 26**：

| CSS 变量 | 兜底基准 | 本画布算得 |
| --- | --- | --- |
| `--safe`（`layout.safe_margin` 缺省） | 72 | 75px |
| `--gap`（`layout.gap` 缺省） | 32 | 33px |
| `--radius`（`layout.frame_radius` 缺省） | 26 | 27px |

**v2 没有 `layout` 段**（写了会被 schema 拒），所以这三个兜底在本模板里没有实际作用：
场景里每个节点自己写 `x`/`y`/`width`/`height`，间距靠 `gap` + `padding`。上表只是说明
`75 / 33 / 27` 这几个数字是从哪来的，别把它们当成可以通过参数调整的旋钮。`--radius` 还更绕一层：
`base.css` 里 `.promo[data-material-version="md3"]` 又把它声明成 `var(--configured-radius, 28px)`，
所以海报内部实际看到的兜底是 **28px**，那个 27px 只在 `:root` 上成立。

`runtime.js` 的 `setVars()` 只读 `layout` 里的 `safe_margin` / `gap` / `frame_radius`；v1 的
`renderV1()` 另外读 `mode` / `slots` / `background_asset`。`custom_css` 根本不是 `runtime.js` 读的，
而是 `render_image.py` 在渲染前作为一段 `<style>` 注入。v2 的 `prepared` 里没有 `layout` 键，
所以这些字段在本模板里一个都不会被读到（schema 也不允许它们出现在 v2 spec 上）。

## 配色

### 令牌

`material.version` 必须是 `md3`，`mode` 写 `light`。本模板**显式声明 `material.tokens`**，
不声明 `seed_color` / `source_asset`，因此走的是 `theme_engine.resolve_material()` 的短路分支
（`if not seed and not source_id:`）：`resolved_tokens = tokens`、`theme` 原样返回，**完全不调用 MCU**。

实测 manifest 的 `material` 只有四个键（`version` / `mode` / `tokens` / `resolved_tokens`），
没有 `generated_schemes` / `selected_scheme` / `color_source` / `engine`。这带来两个可见后果：

- 不需要 Node，也就不需要 `npm install`——`material_theme.mjs` 只在有种子色或取样素材时才会被拉起。
- **`mode` 在无种子色时是空转键**：没有生成的明/暗两套方案可选，它只作为标签写进
  `promo.dataset.materialMode`，不改变任何颜色。模板之所以还是写 `light`，是因为它是语义标签，
  而且一旦将来换成种子色，它立刻变成有意义的选择器。

反过来，一旦你改成种子色（`seed_color`）或取样素材（`source_asset`），这条短路分支就不再命中：
MCU 会生成明/暗两套方案，`mode` 负责选其中一套，显式 `tokens` 仍然覆盖生成值，`theme` 里没写的键
也会由生成的方案补齐（`accent` ← `primary`、`text` ← `on_surface` 等）。那时才需要 Node。

`material.tokens` 是**封闭**映射：schema 里 `additionalProperties: false`，合法角色就是那 24 个
snake_case 名。**写 Material 文档里的 camelCase 角色名会被 schema 直接拒**：

```
promotional-image.material.tokens: Additional properties are not allowed ('onPrimary' was unexpected)
```

`surface_container_low` 这类不在 schema 里的角色同理被拒。本模板声明了 11 个，实际被场景或
`theme` 用到的只有 7 个：

| 令牌 | 取值 | 用途 |
| --- | --- | --- |
| `primary` | `#2E7DF6` | 强调色、版本胶囊底色、柔光圆与描边胶囊的色相 |
| `on_primary` | `#FFFFFF` | 版本胶囊文字 |
| `primary_container` | `#D8E2FF` | 右上柔光圆（55% 透明） |
| `surface_container` | `#EEF1FB` | 左下圆斑（95% 透明） |
| `on_surface` | `#191C20` | 标题 |
| `on_surface_variant` | `#43474E` | 副标题、产品名、eyebrow |
| `outline` | `#C3C7CF` | 品牌行分隔点 |

声明了但没被任何节点或 `theme` 引用的四个角色如下。它们是无害的：渲染器会把每个已声明角色都写成
`--md-<kebab>`，未被 CSS 引用而已，不会报错也不会影响取色。

| 令牌 | 取值 | 状态 |
| --- | --- | --- |
| `surface` | `#FDFBFF` | 未被引用（场景没有实体面；`theme.surface` 另有其值） |
| `on_primary_container` | `#001A41` | 未被引用 |
| `secondary` | `#565E71` | 未被引用 |
| `on_secondary` | `#FFFFFF` | 未被引用 |

保留它们的理由是「角色完整」——换配色时不必再回头补 schema 键。想精简也可以整行删掉，
schema 不要求 `tokens` 里出现哪些角色。

`#2E7DF6` 取自 `Constants/LauncherConstants.cs` 的 `DefaultThemeColor`，也是
`App.axaml` 的 `Launcher.Color.Info`。应用主色 `Launcher.Color.Primary` 跟随系统强调色
（`DynamicResource SystemAccentColor`），会随机器变化，**不能用作横幅品牌色**——横幅必须每台机器
渲染一致。

一处例外：两个描边胶囊的文字色 `#1A5FD0` 是设计上手工挑的深蓝，没有对应令牌，直接写在节点
`style.color` 里；要换配色时记得它也要一起看。

### `theme`：背景与兜底

`theme` 共 7 个键，本模板全给：`background_top` `#FDFBFF`、`background_bottom` `#E9EFFC`、
`accent` `#2E7DF6`、`text` `#191C20`、`muted` `#43474E`、`surface` `#FFFFFF`、
`outline` `#C3C7CF`。前两个经渲染器写成 `--background-top` / `--background-bottom`，
其余是相同名目的 `--md-*` 的兜底。

`base.css` 给 `#promo` 的默认背景是**深色**的（`#07131f` → `#171028`，外加一层
`rgba(5,10,18,.74)` 的深色遮罩）。本模板的 scene 根节点用这行把它整个盖掉：

```
radial-gradient(circle at 82% 16%, color-mix(in srgb, $primary 25%, transparent), transparent 31%),
linear-gradient(145deg, var(--background-top), var(--background-bottom))
```

即：右上 82% 16% 的柔光由 `--md-primary` 驱动，底衬渐变**引用 theme 写出的两个 CSS 变量**。
所以 `theme.background_top/bottom` 在这套模板里不是装饰性配置，删了会掉回深色底。
`inspect_campaign.py` 也会提醒这件事：`mode: light` 却既没有配色来源、也没有 theme 背景色时，
它会打印 `notice: without a color source or explicit theme, v1 light mode keeps the legacy dark
background.`——本模板不该出现这条 notice。

`#promo` 自带 `overflow:hidden`，出血的图形会被裁切而不会报错——这是有意的，不是缺陷。
本模板的几何点缀（两个柔光圆、两个细描边圆环、一张浅色卡片）都改用 scene 里的 `shape` 节点，
不再用 `::before` / `::after`：
伪元素对版式校验不可见，而 scene 节点可以被检查（见「出血与碰撞」）。

## 排版

`typography.font_family` 是唯一被识别的排版键，写入 `--font` 并作用于 `body`。管线
**从不加载网络字体**，只用系统字体，因此字体栈要按渲染机上真实存在的字体排：

```
Segoe UI, Microsoft YaHei UI, Microsoft YaHei, Noto Sans SC, sans-serif
```

中文字形由 `Microsoft YaHei UI` 承接，兜底 `Microsoft YaHei`，再兜底 `Noto Sans SC`（西文走 Segoe UI）。
`font_availability` 会为栈里**每一个非通用族**记一条，缺一个就是一条 `MISSING`。

本模板把 `validation.profile` 设为 `release`，这是交付档位：**缺字体从警告升级为硬失败**，
渲染直接以非零码退出。也就是说这份模板**绑定 Windows 字体**，必须在装有上述字体的机器上渲染；
换到 macOS/Linux 会被档位拦下，而不是悄悄产出一张字形回退过的横幅（这正是档位存在的意义）。

#### 字体栈必须逐个探针，不能凭品牌名推

这是本流程最容易翻车的一步，因为它不是「写错名字」而是「写了一个看起来合理但这台机器上不存在的名字」。
`font_availability` 的判定不是名字匹配，而是**用 Canvas 光栅比对字体指纹**：把一个探针字符串分别用
`"<族名>", <通用族>` 和纯通用族渲染，只要像素指纹相同就判为「不可用」。因此「族名拼写正确」不等于
`ok`——机器上没装就是 `MISSING`，而 `release` 档位下 `MISSING` 直接终止渲染。

模板早期写的是 `Noto Sans CJK SC`，本机（Windows）实测 **`MISSING`**，于是在自己的开发机上
就渲染不出成图——`LayoutValidationError` 里写的就是这句，外面还包着第 4 步那层 `RuntimeError`：

```
release profile requires available fonts: Noto Sans CJK SC
```

`Noto Sans CJK SC` 是 **Linux** 侧的打包名；Windows 上装 Noto 的话族名是 `Noto Sans SC`（无 `CJK`）。
改用 `Noto Sans SC` 后四项全 `ok`。

新增或更换字体族前，先用渲染器自己的检测函数探一遍，别等渲染到一半才发现：

```python
# 从 skill 的 templates/shared/runtime.js 里取 fontSignature + localFontAvailable 两个函数
import json, sys; from pathlib import Path
sys.path.insert(0, str(Path.home() / ".agents/skills/promotional-image/scripts"))
import render_image as ri                      # 借它挑 Chromium（含系统浏览器兜底）
from playwright.sync_api import sync_playwright

js = (Path.home() / ".agents/skills/promotional-image/templates/shared/runtime.js").read_text(encoding="utf-8")
helpers = js[js.index("function fontSignature"):js.index("function ancestorViolations")]
names = ["Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans SC", "Noto Sans CJK SC"]
probe = "(() => {" + helpers + "\nreturn Object.fromEntries(" + json.dumps(names) + ".map(n => [n, localFontAvailable(n)]));})()"

with sync_playwright() as p:
    browser = ri.launch_browser(p)
    page = browser.new_context().new_page()
    page.set_content("<html><body>x</body></html>")
    for name, ok in page.evaluate(probe).items():
        print(f"{'ok     ' if ok else 'MISSING'}  {name}")
    browser.close()
```

本机实测结果（可作为换机器时的对照）：

| 族名 | 本机 | 备注 |
| --- | --- | --- |
| `Segoe UI` | ok | 西文主体 |
| `Microsoft YaHei UI` | ok | 中文主体 |
| `Microsoft YaHei` | ok | 中文兜底 |
| `Noto Sans SC` | ok | 中文末位兜底 |
| `Noto Sans CJK SC` | **MISSING** | 模板早期误用，Linux 侧命名 |
| `Meiryo` | **MISSING** | 日文，本机未装 |
| `Yu Gothic UI` / `MS Gothic` | ok | 日文可用 |

**字体是族级证据，不是字形级证明**：`ok` 只说明这个族被解析到了，不代表其中的每个字形都存在
（例如某个生僻汉字可能仍回退到别的族）。这一点渲染器自己也列在 `unchecked` 里。

模板中的字号（2000×1125）：`brand` / `eyebrow` 21px、`product` 34px、`headline` 152px、
`subtitle` 36px、胶囊 29px（外高 78px）。

### 样式值的类型：会咬人的是单位，不是类型

渲染器对样式值的处理是「数字一律当长度，补 `px` 后缀」：

```js
const px = (value) => typeof value === "number" ? `${value}px` : value;
```

于是数值与字符串的差别**只落在「这个键最终会不会被补上 `px`」**上，判断依据是渲染器里的一个
白名单，凡在里面的一律走 `String(value)` 不加单位：

```js
const unitlessStyle = new Set(["opacity", "z_index", "font_weight", "line_height"]);
```

实测（同一个 800×450 探针 spec，读回 `computed_styles`）：

| 写法 | 实测结果 | 结论 |
| --- | --- | --- |
| `font_weight: 800`（数字） | `800` | 正确——白名单走 `String()`，**不会**变成 `800px` |
| `font_weight: "800"` | `800` | 与数字写法等价 |
| `line_height: 1.22`（数字） | `39.04px`（= 32 × 1.22） | 正确 |
| `line_height: "1.22"` | `39.04px` | 与数字写法等价 |
| `font_size: 32`（数字） | 行盒 `39.04px`（字号 32） | 正确——长度键由 `px()` 补单位 |
| `font_size: "32"`（无单位字符串） | 行盒 `19.52px`（字号掉回 16） | **坏**：`px()` 不补单位，`font-size: 32` 非法被丢弃 |
| `font_size: "32px"` | 行盒 `39.04px` | 正确 |
| `line_height: "1.22px"` | 行盒 `1.22px` | **坏**：合法长度，行盒压塌，文字照画在盒外 |

所以真正会咬人的是**单位**，不是类型：

- **长度键**（`font_size`、`letter_spacing`、`x`/`y`、`width`/`height`、`padding`、`gap`、`radius`）
  要么写**数字**，要么写**带单位的字符串**。最危险的是「长度键 + 无单位字符串」（`"32"`、`"400"`）：
  `px()` 原样透传，浏览器判定非法后丢弃，**版式报告不会出声**——上面那个字号掉回 16px 的探针
  就是 `text_overflow` 全 0 跑出来的。
- **白名单那四个键**（`opacity` / `z_index` / `font_weight` / `line_height`）数字和字符串都行，
  但它们**永远不会被补单位**，所以别再自己写 `px`：`line_height: "1.22px"` 是个合法长度，
  行盒被压到 1.22px，而行盒不会被裁切，文字照常压在盒外显示，观感上很「正常」。
- `rotation` 走另一条路——渲染器专门拼成 `rotate(<n>deg)`，写数字。

本模板的写法因此是：长度用数字；白名单键写不带单位的字符串（`"800"` / `"1.22"`）表明「这里没有
单位」；只有确实需要指定长度的地方才写带单位的字符串（胶囊的 `"78px"` / `"74px"`、
四值 `padding` 的 `"0 0 24px 0"`）。这与数字写法功能等价，属于显式约定，不是必需。

**`opacity` 在 v2 里可用，但只对「整棵子树该一起变淡」的形状有意义**：schema 要求它是
`0..1` 的数字，而渲染器的 `unitlessStyle` 白名单里正好包含它，所以数字会被原样写成不带 `px` 的
`opacity`。实测（一个 800×450 探针 spec，形状 `opacity: 0.13`、文字 `opacity: 0.5`）读回的
`computed_styles` 就是 `"0.13"` / `"0.5"`，不是被丢弃后的 `"1"`：

```
half-disc  -> {"opacity": "0.13", ...}
probe-text -> {"opacity": "0.5",  ...}
```

写法必须是**数字** `0.13`；字符串 `"0.13"` 会被 schema 以类型不符直接拒掉。两个后果要知道：

- `opacity` 作用于节点**整棵子树**并新建层叠上下文，所以给容器写它会连带把里面的文字一起变淡。
  要只淡一个形状，就把 `opacity` 写在那个叶节点上。
- 它对图片节点虽然生效，但本模板**不用**：图标的透明度已经烘进素材 alpha 通道（原因见「图标资产」），
  再叠一层数值 `opacity` 会得到两倍衰减，而且会给图片新建层叠上下文、平白多一层合成。

所以本模板的**首选**仍是前一条：把透明度写进颜色本身，例如
`"background": "color-mix(in srgb, $primary_container 55%, transparent)"`。
这与「不透明色 + 0.55 透明度」合成结果一致，语义也更明确（是颜色的属性，不是盒子的状态）。

skill 自带的 `examples/promo-v2-scene.example.json` 和迁移脚本 `scripts/migrate_v1_to_v2.py` 都用
**数值**写 `font_weight` / `line_height`（实测：示例里是 `760` / `800` / `680` / `720` 与
`1.02` / `1.35`；迁移脚本为 `760` / `680` / `650` / `800` / `720` 与 `1.02` / `1.35`）。
按本节结论这是**对的**，不必去「修」它们，也别因此怀疑迁移产物——它们与字符串写法功能等价，
只是与本模板的显式风格不同。真正该从本节记住的是：**别给白名单键补单位，别给长度键写无单位字符串。**

## 场景结构

v2 把海报表达成一棵递归的节点树，只有 `text` / `image` / `shape` / `group` 四种节点，
每个节点一个稳定唯一的 `id`。**管线不支持注入 HTML**，所有元素都来自固定模板与节点树本身。

本模板的结构（`scene.children`）：

1. `glow-disc`、`base-disc` — 两个柔光圆，`shape: circle`，各带 `intent.allow_bleed`，提供大面积底色。
2. `ring-top-right`、`ring-bottom-left` — 两个细描边圆环，分别与上面两个柔光圆同心：只给 `border`
   不给 `background`，因此只有一圈描边；同样各带 `intent.allow_bleed`。
3. `surface-card` — 承载诊断图标的浅色卡片，`shape: rect` + `radius: 56` + `rotation: -14`，
   `background` 取 `$primary 5%`、`border` 取 `$primary 14%`，与 `icon-log` 同心同角度。
4. `icon-repair`、`icon-network`、`icon-log`、`icon-shield` — 四个图标素材，绝对定位 + 旋转；
   只有 `icon-shield` 出血。
5. `copy-zone` — 文案区，`layout: stack`，`x:150 y:0 width:1080 height:1125` + `justify:"center"`，
   对应旧版 `.header` 的 flex 垂直居中；块内元素增长时它会自动重新居中。

`copy-zone` 内部是一层 `copy`（`layout: stack`，`gap: 10.4167` = 10 × scale）+ 五个子块：
`brand-row`、`product`、`headline`、`subtitle`、`actions`。要点：

- **缩进用 padding 不用 margin**：节点样式里没有 margin，块间距靠 `gap` + 四值 `padding`
  字符串（如 `"padding": "0 0 24px 0"`）拼出设计节奏。
- **换行只能靠拆节点**：v2 没有 `white-space`，副标题的断行就是两个 `p` 节点装在一个无
  `gap` 的 stack 里，行高 1.55 让它与旧版 `white-space: pre-line` 的两行完全一致。
- **胶囊高度对齐**：三个胶囊都写死 `height: 78`（`box-sizing: border-box` 全局生效），
  文字垂直居中靠 `line-height`——没有边框的版本胶囊用 `"78px"`，有 2px 边框的用 `"74px"`，
  这样三者外高都是 78，不会因为边框差 4px 而参差（实测读回就是 `78px` / `74px`）。
  这是**有意**给白名单键补单位的少数场景：那里说的是别拿它做行距——把 `line-height` 写成等于盒高
  正是居中手段，而给正文行距写 `px` 才会压塌行盒（见「样式值的类型」）。
- `$role` 引用：`color` / `background` / `border` 里写 `$primary` 这类角色名即可（也支持在
  `color-mix()` 内部引用）。
- `tag` 用 `h1` / `p` / `div`；组节点会带上 `.scene-group`，`base.css` 已把其中的
  `h1`/`p` 外边距归零。

### 出血与碰撞

- 两处柔光圆、两处细描边圆环与右下角出血的盾牌图标都声明了 `intent.allow_bleed: true`，它们的
  越界因此记入 `allowed_findings` 而不是 `warnings`（实测五处，见「实测基线」）。
- **本模板不给装饰图标声明 `no_overlap_group`**：这几个图标互相叠压是设计意图，声明了反而
  产生两条假的 collision 警告。`no_overlap_group` 是留给「绝不能重叠」的内容的。

## 图标资产

`docs/promo/assets/icons/` 下的四个 PNG 是从 Material Icons 官方仓库光栅化来的，已入库，
正常情况下无需重新生成：

| 文件 | 图标 | 用途 | 上色 |
| --- | --- | --- | --- |
| `icon-repair.png` | `action/build` | 启动校验一键修复 | `#2E7DF6` |
| `icon-log.png` | `action/history` | 诊断导出的时间范围 | `#2E7DF6` |
| `icon-network.png` | `notification/network_check` | 网络与代理更稳 | `#2E7DF6` |
| `icon-shield.png` | `action/verified_user` | 安全与隐私 | `#7A5AF8` |

这组图标是**装饰**，不承载事实：一套四个按当版更新的主题挑，换主题时按下面的命令重新生成
并把新文件入库，同时更新本表。

两点必须知道：

1. **必须光栅化，不能直接用 SVG**：素材准备阶段用 Pillow 处理，Pillow 读不了 SVG。
   源地址遵循 `<分类>/<名称>/materialicons/24px.svg`，但分类不能靠猜——`image/wallpaper`
   实测返回 404，每个图标都要看 HTTP 状态。光栅化用无头 Chrome 输出带透明通道的 512×512，
   `--default-background-color=00000000` 是透明背景的关键，缺了会得到白底方块。
2. **当前入库的四个文件是「已上色 + 已烘入透明度」的成品**：光栅化那一步得到的是纯黑 alpha 蒙版
   （`fill` 没被带进来，字形本身是对的）；随后按上表着色，并把设计要求的透明度
   （`0.13` / `0.11` / `0.10` / `0.12`，对应 repair / log / network / shield）乘进 alpha 通道。
   两步都只改 RGB 与 alpha，字形几何完全不变。

之所以把透明度烘进素材而不是在节点上写 `opacity: 0.13`，有两个具体理由（不是说 `opacity` 不能用，
它对形状是好用的，见「样式值的类型」）：

- 图标是 `decorative` 素材，烘 alpha 让它们在**渲染前**就已经是最终观感，任何查看 PNG 的人
  （包括 diff 工具）看到的都是真实结果，不必依赖 CSS 叠加。
- 数值 `opacity` 会给节点新建层叠上下文；图片节点本来就已经通过 alpha 通道表达了透明度，
  再叠一层数值 `opacity` 只会得到两倍衰减。**两者只能用一种**，本模板选 alpha。
  如果将来改成用 `opacity`，必须先把素材里的 alpha 还原成不透明，否则亮度会算错。

要重做图标时：先按上面的命令光栅化成纯黑图，再上色 + 烘透明度。用 Pillow 即可，两步都是
确定性变换：

```python
from PIL import Image
im = Image.open("icon-repair.png").convert("RGBA")
alpha = im.getchannel("A").point(lambda v: round(v * 0.13))   # 设计要求的透明度
im = Image.merge("RGBA", (*[Image.new("L", im.size, c) for c in (0x2E, 0x7D, 0xF6)], alpha))
im.save("icon-repair.png", format="PNG", optimize=True)
```

改完随手验一下「上色 + 烘透明度」是否真的生效（尺寸 512×512，RGB 三通道应全等于上表颜色，
alpha 最大值应约等于 `透明度 × 255`）：

```python
from PIL import Image
from pathlib import Path
for path in sorted(Path("docs/promo/assets/icons").glob("*.png")):
    with Image.open(path) as im:
        print(path.name, im.size, im.mode, im.getchannel("A").getextrema())
```

本仓库四个文件的实测值，可据此对照：

| 文件 | 尺寸 / 模式 | alpha 范围 | 对应透明度 |
| --- | --- | --- | --- |
| `icon-repair.png` | 512×512 / RGBA | `(0, 33)` | 0.13 |
| `icon-log.png` | 512×512 / RGBA | `(0, 28)` | 0.11 |
| `icon-network.png` | 512×512 / RGBA | `(0, 26)` | 0.10 |
| `icon-shield.png` | 512×512 / RGBA | `(0, 31)` | 0.12 |

alpha 上界是**故意压得很低**的：图标只是背景点缀，最大值 26–33 意味着最不透明的像素也只有约
10%–13% 不透明度。这正是设计意图，不要为了「看得更清楚」把它调亮。

图标是 `origin_class: "decorative"`，`immutable` 因此为 `false`：旋转、透明度这类改动不触发
完整性硬失败（`authentic` 素材则相反，任何 `transform` / `opacity` / `filter` 都会在 advisory
与 strict 两种模式下硬失败）。改动后重跑第 3–6 步，并确认 `verify_export.py` 仍打印
`images: 4/4 healthy`。

## 校验

`validation` 段只有两个键（v1 的 `layout` 段在 v2 里不存在）：

- `profile`：`draft`（构图阶段）/ `review`（普通目视）/ `release`（交付）。**`draft` 与 `review`
  的 `release_eligible` 恒为 false**；`release` 档位下缺字体是硬失败。本模板用 `release`。
- `layout`：`advisory` 保留警告供人判断，`strict` 让警告直接阻塞。本模板用 `advisory`。

渲染器把发现分成几类，别混为一谈：

- `errors`：图片加载失败、`authentic` 素材被改动——**任何档位都硬失败**；`release` 档位下
  字体不可用也归入这一类（skill 的 `SKILL.md` 把 release-font failures 与前两项并列为 errors）。
- `warnings`：文字溢出、未声明的越界、声明了不重叠却发生碰撞。
- `allowed_findings`：节点 `intent` 明确允许的行为（本模板就是那五处出血）。
- `unchecked`：自动化证明不了的视觉事实——渐变/图片上的对比度、字形级回退、视觉层次与平衡。

`manifest.validation.layout` 里 `passed` **只在 v1 里出现**，且它只是「能写出 manifest 就说明
没有硬失败」的记录，不是计算结论；要读真实结论请看 `report` 各项数组与 `checks_passed` /
`release_eligible`。**version 1 的 profile 恒为 `legacy`**（渲染器写死），所以 v1 永远拿不到
`release` 档位的字体闸门——这是本仓库改用 v2 的另一个理由。

自动化看不见的还有：版式校验只检查带 `data-check-bounds` 的节点，`background-image` 之类
纯装饰不在其中，因此构图最终仍需肉眼确认（第 7 步）。

### 实测基线（本仓库模板，2000×1125）

以下数值是 2026-09 在本机（Windows 10、Python 3.14.6、Playwright 1.62.0 + Chromium 1234）
实跑**由本模板复制出的发布 spec**（`v1.1.0-beta.9`）的记录，用来判断「这次跑出来是否正常」。
计数、百分比与出血矩形是稳定的（只由场景几何决定）；**绝对字节数与哈希只对「同一份 spec +
同一环境」有意义**，tag 文案变长变短都会让它们变。

| 项 | 值 |
| --- | --- |
| 确定性 | `true` |
| 尺寸 / 格式 | `[2000, 1125]` / PNG |
| `profile` / `policy` | `release` / `advisory` |
| `checks_passed` / `release_eligible` | `true` / `true` |
| `errors` / `warnings` | 0 / 0 |
| `font_availability` | 四项全 `true` |
| `text_overflow` / `canvas_overflow` / `collisions` | 0 / 0 / 0 |
| `images` | `4/4 healthy` |
| `allowed_findings` | 5 |
| `unchecked` | 3（对比度、字形级回退、层次与平衡——恒为此三项） |
| 无损压缩收益 | 12.9%（688114 → 599249 字节，逐像素一致） |

五条 `allowed_findings` 的具体节点（都由 `intent.allow_bleed: true` 释放）：

| 节点 | 越界矩形 |
| --- | --- |
| `glow-disc` | `[1280, -320, 2180, 580]` |
| `base-disc` | `[-330, 755, 510, 1595]` |
| `ring-top-right` | `[1390, -210, 2070, 470]` |
| `ring-bottom-left` | `[-210, 875, 390, 1475]` |
| `icon-shield` | `[1789.7, 673.7, 2174.3, 1058.3]` |

`verify_export.py` 在本模板上**不会**打印「N actionable layout finding(s) were reported but NOT
enforced」那一行：该计数只累加 `errors` + `warnings` + 不健康的图片，**故意排除 `allowed_findings`**
（`describe_layout_report()` 里那句 `if key != "allowed_findings"` 就是为此写的），本模板这三项全是 0，
合计为 0，提示行因此不出现。反过来说——**看到那行提示说明有真问题要复核**，它不是预期噪音。

它会打印 `allowed finding: 5` 并在下列出五处出血，随后打印三条 `unchecked`：那三项是渲染器
**恒定**列出的、自动化无法证明的视觉事实，不是本次的问题信号。

## 常见陷阱

- **spec 里不能写注释**：顶层 `additionalProperties: false`，任何未声明的键都会被拒
  （`layout` 段、`notes` 之类的额外字段同理）。设计意图只能靠本文与提交信息承载。
- **改 spec 只能用 `pwsh -Encoding utf8NoBOM` 或编辑器**：Windows PowerShell 5.1 的 `Get-Content`
  会按系统 ANSI 代码页解码无 BOM 的 UTF-8，正文先坏；随后默认编码写回得到非法 UTF-8，
  `-Encoding UTF8` 写回则把乱码**二次编码**成控制字符。**BOM 本身无害**（管线用 `utf-8-sig` 读，
  skill 也明说 BOM 可有可无），别把锅算在它头上——详见「步骤 1」。
- **schema 会一次报出全部错误**（`schema_utils.validate_document` 用 `iter_errors` 收集、按路径排序、
  去重，拼成一段多行消息），所以一次 `inspect` 就能拿到完整清单，不必逐个错误来回试。
- **`material.tokens` / `theme` / `typography` 都是封闭对象**：拼错或写 camelCase 会被拒，
  不是被忽略。合法角色只有 schema 里那 24 个 snake_case 名。
- **样式值看单位不看类型**：长度键（`font_size` / `width` / `padding` / `gap` …）写数字，或写
  **带单位**的字符串；「长度键 + 无单位字符串」（`"32"`）会被浏览器丢弃，而且版式报告不出声
  （实测字号静默掉回 16px）。`font_weight` / `line_height` 在白名单里，数字和字符串都行，
  但**别再补 `px`**——`"1.22px"` 是合法长度，会把行盒压塌而文字照旧压在盒外（**看不出**）。
  `opacity` 必须写数字，写字符串被 schema 拒。详见「样式值的类型」。
- **fonts 必须逐个探针**（本流程最容易翻车的一项）：族名拼写正确不等于可用，`release` 档位下
  任一 `MISSING` 直接终止渲染（见「字体栈必须逐个探针」）。换机器渲染前先跑一次探针。
- **v2 没有 `layout` 段**，`safe_margin` / `frame_radius` / `slots` / `background_asset` /
  `custom_css` 都是 v1 的东西，写到 v2 spec 上会被 schema 拒。
- **`custom_css` 只属于 v1**，且 v1 的 `custom_css` 无法被结构化校验，也不会被
  `migrate_v1_to_v2.py` 自动迁移（迁移脚本只转换已知的 campaign 字段与素材引用，
  绝不解析任意 CSS；它的报告是一份人工重建清单）。
- **`prepared` 产物不能反向当输入**：它在原 spec 上加了一层准备结果——顶层多了 `orientation`、
  `preparation`（实测 schema 就是报这两个 unexpected），`material` 多了 `resolved_tokens`，
  每个 asset 换成带 `component_file` / `component_sha256` / `source_sha256` 等字段的对象。
  直接喂给 `prepare_assets.py` 会 schema 失败，且会一次列出全部问题。
- **`manifest` 是本机证据**：里面全是绝对路径与当时的 schema 哈希，换机器或 skill 更新后
  `verify_export.py` 会失败。留档可以，当可移植交付物不行。
- **报错形式分两类**：`inspect` / `prepare` / `optimize` / `verify_export` 的失败都由各自的 `main()`
  捕获后打成一行干净的 `spec …: 原因`（退出码 1），**不是 traceback**；`render_image.py` 的四类
  `SystemExit`（未知 schema 版本、`device_scale_factor != 1`、模板契约版本不匹配、确定性校验失败）
  同样是干净的。**唯一的例外是字体闸门**：它以 `RuntimeError` traceback 的形式抛出（见第 4 步）。
- **相对路径全部以「spec 文件所在目录」为基准**，不是当前工作目录。所以脚本可以从仓库任意目录调用，
  但**spec 文件本身不能随便挪**：把模板复制到 `%TEMP%` 再跑，`../assets/icons/…` 会解析到
  `%TEMP%` 的上一级并报 `asset 'icon-repair' source does not exist`。每版 spec 必须留在
  `docs/promo/specs/` 下。
- **渲染器没有 `--scale` 之类的参数**，唯一的开关是 `--verify-determinism`；画布相关的旋钮只能通过
  spec 的 `canvas` 段调整，而且 `device_scale_factor` 被 schema 钉死为 1。
- **`render_image.py` 只读它自己的命令行参数**，不会去读 prepared 里 `output.prepared` 的路径；
  必须先跑 `prepare_assets.py`，再把打印出来的那个路径原样传给 `render_image.py`（第 3、4 步）。
  manifest 里的 `output.prepared` 是这两个路径的重合点，只作记录。
- **渲染前先确认 `output.image` 指向 `docs/assets/release-banners/`**，文件名与
  `CHANGELOG_RELEASE.md` 的引用逐字一致；`ReleaseBannerContractTests` 会在 CI 里守住模板的
  这一路径与画布约定。
- **`artifacts/` 不进版本库**：中间产物（prepared / manifest / components）都落在
  `artifacts/release-banner/`，已被 gitignore。入库的只有 spec、图标和最终 PNG。

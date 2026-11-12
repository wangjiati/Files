# 预览功能增强调研：Office、图片、压缩包与图纸

- 日期：2026-11-11
- 适用项目：Files（WinUI 3 / .NET 10 / Native AOT / MSIX）
- 调研范围：信息窗格预览、空格键快速预览（Popup），以及两者可复用的预览管线
- 结论状态：技术选型建议，尚未进入实现

## 0. 结论速览

1. **当前问题不只是“少一个渲染库”。** 预览入口是 `InfoPaneViewModel.GetBuiltInPreviewControlAsync` 里的一串硬编码分支，没有统一的 provider、优先级、超时、资源上限和缓存；新增格式会继续把这些分支撑大。
2. **推荐分层预览，而不是把所有格式塞进一个引擎。**
   - 原生层（AOT 安全、零/小依赖）：WIC 图片、`SvgImageSource`、`Windows.Data.Pdf`、现有 7z、文本/代码/Markdown/RTF/媒体。
   - WebView2 层（AOT 安全、包体小）：DOCX、XLSX/CSV 表格、DXF、3D。
   - Shell 层（系统已安装应用时保真度最高）：Office、Visio、CAD 的已注册 Preview Handler。
   - 可选外部转换层（不默认捆绑）：LibreOffice 或商业 SDK，统一转 PDF/PNG 后交给原生 PDF/图片查看器。
3. **Office 的现实结论：**
   - 装有 Office：优先用现有 Shell Preview Handler，保真度最高、零包体。
   - 没装 Office：DOCX 用 `docx-preview`，XLSX/CSV 用 SheetJS 做表格预览；PPTX 和旧版 `.doc/.xls/.ppt` 没有达标的纯开源渲染方案，只做缩略图/大纲/元数据。
   - 要“无 Office 也完整保真”，只有 LibreOffice（约 1.5 GB 安装体积）或许可商业 SDK 两条路。前者适合做成**可选组件**，不适合默认塞进 MSIX。
4. **图片：** WIC + `SvgImageSource` 足够覆盖主流格式；WebP/HEIC/AVIF/RAW 依赖系统编解码器，不能假定每台机器都有。不要为了图片预览引入 ImageSharp（许可证）或默认引入 42 MB 的 Magick.NET。
5. **压缩包：** 继续用现有 7z.dll 26.00 + SevenZipSharp 分支，补“条目列表 + 单条目内嵌预览”，不要新增第二套压缩引擎。
6. **图纸：**
   - DXF 可以用 `dxf-viewer` 在 WebView2 中预览，但它是 586 星、单人维护的项目，且纸空间、线型、宽线等功能不完整。
   - DWG 没有同时满足“主流、持续维护、可商用、纯 .NET 渲染”的开源库。`ACadSharp.Image` 只有 10 星、0.1.x，不能用于生产；`netDxf` 已归档；LibreDWG 是 GPL 且渲染质量有限。可行路线只有三条：Shell（装了 CAD）、`ACadSharp` 转 DXF 后再用 `dxf-viewer`（需 spike 验证）、商业 SDK（ODA/Aspose/GroupDocs）。
   - Visio 没有可信的开源渲染器；装了 Visio 走 Shell，否则商业 SDK 或“打开”。
7. **默认增量包体可以控制在 8 MB 以内**（docx-preview + JSZip + SheetJS + dxf-viewer/three 的裁剪版 + ACadSharp），代价是 Office/PPTX/DWG 只做到“可用预览”，不是“像素级还原”。完整保真作为可选组件。

### 调研方法

- 审计现有预览代码、打包配置和 AOT 发布配置，确认扩展点和限制。
- 用 GitHub API、shields.io、npm registry、NuGet 获取星数、最近提交、许可证、包体和周下载量；星数/活跃度以 2026-11-11 为准。
- 用官方文档核对能力边界：微软 Preview Handler/WIC/PDF、LibreOffice 命令行与系统要求、GroupDocs/Syncfusion/Aspose 支持格式。
- 在本机 Office 16 环境核对 Office Preview Handler 注册情况；未做性能基准，文中“秒级/约多少 MB”均为量级估算。

## 1. 现状审计

### 1.1 代码路径

| 能力 | 位置 | 现状 |
| --- | --- | --- |
| 预览分发 | [`InfoPaneViewModel.cs`](../../src/Files.App/ViewModels/UserControls/InfoPaneViewModel.cs:263) | 按扩展名顺序判断，命中即返回；没有 provider/优先级抽象 |
| 预览加载 | [`BasePreviewModel.cs`](../../src/Files.App/ViewModels/UserControls/Previews/BasePreviewModel.cs:53) | `Task.Run` 加载，取消令牌只覆盖部分路径；没有统一超时/内存上限 |
| Shell 处理器 | [`ShellPreviewViewModel.cs`](../../src/Files.App/ViewModels/UserControls/Previews/ShellPreviewViewModel.cs:58) | 用 `AssocQueryString` 查 CLSID，`CLSCTX_LOCAL_SERVER` 创建；Office 三个 CLSID 被硬编码用于鼠标穿透处理 |
| Shell 宿主 | [`PreviewHandler.cs`](../../src/Files.App/Utils/Shell/PreviewHandler.cs:84) | 通过 `prevhost.exe` 代理承载，UI 用 DComp 合成到 XAML |
| 图片 | [`ImagePreviewViewModel.cs`](../../src/Files.App/ViewModels/UserControls/Previews/ImagePreviewViewModel.cs:26) | `BitmapImage.SetSourceAsync`；只支持 9 个扩展名 |
| 图片扩展名 | [`FileExtensionHelpers.cs`](../../src/Files.Shared/Helpers/FileExtensionHelpers.cs:378) | `.png/.jpg/.jpeg/.bmp/.gif/.tiff/.ico/.webp/.jxr`，不含 SVG/AVIF/HEIC/RAW |
| 压缩包 | [`ArchivePreviewViewModel.cs`](../../src/Files.App/ViewModels/UserControls/Previews/ArchivePreviewViewModel.cs:17) | 只统计条目数、文件数、解压后大小；不列条目 |
| 压缩包引擎 | [`StorageArchiveService.cs`](../../src/Files.App/Services/Storage/StorageArchiveService.cs:486) | 7z.dll 26.00（x86/x64/ARM64 三份），SevenZipSharp 封装 |
| PDF | [`PDFPreviewViewModel.cs`](../../src/Files.App/ViewModels/UserControls/Previews/PDFPreviewViewModel.cs:36) | `Windows.Data.Pdf` 实现仍在，但分发分支被注释；当前只能落到 Shell |
| PDF 分支 | [`InfoPaneViewModel.cs`](../../src/Files.App/ViewModels/UserControls/InfoPaneViewModel.cs:358) | 注释状态 |
| HTML | [`HtmlPreview.xaml.cs`](../../src/Files.App/UserControls/FilePreviews/HtmlPreview.xaml.cs:22) | 把文件所在目录整体映射为虚拟主机，安全性不足；分支也被注释 |
| 快速预览 | [`LaunchPreviewPopupAction.cs`](../../src/Files.App/Actions/Content/PreviewPopup/LaunchPreviewPopupAction.cs:39) | 仅委托给 QuickLook / Seer Pro / PowerToys Peek；三者都没有时按空格无反应 |
| WebView2 环境 | [`WebViewEnvironment.cs`](../../src/Files.App/Helpers/WebViewEnvironment.cs:26) | 已有共享环境，可复用 |
| 限制常量 | [`Constants.cs`](../../src/Files.App/Constants.cs:136) | PDF 最多 10 页、文本 10,000 字符、未知文本 500 KB；对“增强预览”明显偏小 |

### 1.2 当前能力矩阵

| 类别 | 现在能预览 | 主要缺口 |
| --- | --- | --- |
| 图片 | PNG/JPEG/BMP/GIF/TIFF/ICO/WebP/JXR | SVG 走 Shell 或文本回退；AVIF/HEIC/RAW 无保证；无缩放/平移/透明棋盘格 |
| PDF | 有实现，但分支关闭；通常由 Edge 的 Shell handler 接管 | 依赖系统 handler；没有原生密码/懒加载/页导航 |
| Office（现代） | 仅在装了 Office 时通过 Shell | 无 Office 时 DOCX/XLSX/PPTX 都没有可用预览 |
| Office（旧版） | 仅在装了 Office 时通过 Shell | `.doc/.xls/.ppt` 无开源渲染方案 |
| 压缩包 | 只显示条目数/大小；可进入压缩包按文件夹浏览 | 不列条目、不能直接预览包内文件、无压缩炸弹限制 |
| 文本/代码/Markdown/RTF | 有原生控件 | 10,000 字符上限过低；Markdown 的本地图片/链接处理有限 |
| 音频/视频 | `MediaPlayerElement` | 编解码器依赖系统；不是本次重点 |
| 图纸 | 仅在装了 CAD/Visio 时通过 Shell | 无 DXF/DWG/Visio 内置预览；文本回退会显示二进制乱码 |
| 3D | 无 | 无 STL/OBJ/GLTF/3MF |
| 快速预览 | 依赖 QuickLook / Seer Pro / PowerToys | 没有内置 Popup；第三方未安装时功能缺失 |

### 1.3 关键问题

1. **没有 provider 抽象。** 新格式只能继续往 `GetBuiltInPreviewControlAsync` 里加 `if`，无法表达“Office 已安装时 Shell 优先、否则内置优先”这类策略。
2. **PDF 和 HTML 明明有控件却被注释。** 当前 PDF 预览实际依赖 Edge 的 Shell handler；在精简系统、ARM64、或 handler 注册损坏时会直接没有预览。
3. **压缩包预览太浅。** 只显示统计信息，用户看不到包里有什么；而项目其实已经具备 7z 浏览/解压能力。
4. **Office 完全依赖本机 Office。** 这是最大的体验缺口，也是“增强预览”最值得投入的部分。
5. **图纸没有内置能力。** `.dwg/.dxf` 在没有 CAD 的机器上基本只能看图标。
6. **没有统一的资源与安全边界。** 没有文件大小/解压后大小/像素数/页数/超时/内存的统一限制；恶意压缩包、超大图片、畸形文档都可能卡死或拖垮进程。
7. **WebView2 生命周期和隔离需要重做。** 当前每个 HTML 预览创建一个 `WebView2` 控件，且把父目录整体映射；不适合直接复用到 Office/CAD。
8. **没有缓存。** 同一文件反复选中会重复解析/转换；Office 转换尤其需要缓存。
9. **没有内置 Popup 复用预览管线。** 空格键预览依赖第三方，体验和格式支持都不一致。

## 2. 评估标准

“不能使用冷门仓库”在本次调研中按以下标准执行：

| 维度 | 要求 |
| --- | --- |
| 主流程度 | GitHub ≥ 1,000 星，或官方平台组件，或有明确的大规模下载/使用证据 |
| 维护状态 | 近 12 个月有提交/发布；归档、停更项目直接排除 |
| 许可证 | MIT/BSD/Apache/MPL 优先；LGPL 需动态链接/独立进程；GPL/AGPL 需法务确认；商业库需预算 |
| AOT | 主进程是 Native AOT（[`win-x64.pubxml`](../../src/Files.App/Properties/PublishProfiles/win-x64.pubxml) 中 `PublishAot=true`），反射/动态代码生成库不能直接进主进程 |
| 离线 | 不依赖云端上传、登录或网络 |
| 体积 | 默认包体增量可控；大体积依赖必须可选 |
| 保真度 | 区分“能解析”“能显示数据”“能还原版式”三种能力 |
| 安全 | 不可信文件解析必须可隔离、可超时、可限额 |
| 平台 | x64 / ARM64；Windows 10 及以上 |

> 说明：少数类别（CAD、PPTX）没有同时满足全部条件的开源项目。报告会明确标注“低于 1,000 星但为类别事实标准”的例外，并给出不接受时的替代路线。

## 3. 分格式方案

### 3.1 Office 系列

#### 方案对比

| 方案 | 覆盖 | 保真度 | 体积/依赖 | 许可 | AOT | 结论 |
| --- | --- | --- | --- | --- | --- | --- |
| Office Shell Preview Handler | DOC/DOCX/XLS/XLSX/PPT/PPTX（装了 Office 时） | 最高，接近 Office 本身 | 0（依赖本机 Office） | 需要 Office 授权 | 现有 COM/CsWin32 路径可用 | **有 Office 时首选** |
| LibreOffice headless 转 PDF | 全部现代 + 旧版 Office、CSV、部分 Visio | 高，但不保证 Word/Excel 像素级一致 | 安装约 1.5 GB；冷启动数秒 | MPL-2.0 | 独立进程，无 AOT 问题 | **无 Office 时唯一免费全保真路线；做可选组件** |
| `docx-preview`（WebView2） | DOCX | 中；HTML 语义还原，复杂版式有限 | ~1 MB JS | Apache-2.0 | WebView2，AOT 安全 | **无 Office 时 DOCX 默认** |
| SheetJS（WebView2/原生表格） | XLSX/XLS/CSV/ODS 解析 | 低-中；只还原单元格表格 | ~1 MB 压缩后 | Apache-2.0 | WebView2/纯 JS | **XLSX/CSV 快速预览** |
| PPTX 纯 JS 渲染 | PPTX | 无达标方案 | — | — | — | **不做“假渲染”** |
| 商业 SDK（GroupDocs/Aspose/Syncfusion） | 视产品而定，GroupDocs 覆盖最广 | 高 | 数十 MB + 运行时/授权 | 商业 | 不支持主进程 AOT，需独立 helper | **预算允许时最省研发** |
| ONLYOFFICE / Collabora | 服务端/桌面套件 | 高 | 服务器栈/完整应用 | AGPL / MPL | 不适用 | **不适合本地预览** |
| Office Online 嵌入 | 常见 Office | 高 | 需要上传/公网/账号 | Microsoft 服务条款 | 不适用 | **拒绝：本地上传隐私问题** |

#### 关键事实

- 微软文档明确：Preview Handler **始终在进程外运行**，默认低完整性级别；当前项目的 `PreviewHandler` 用 `CLSCTX_LOCAL_SERVER` 创建，方向是对的。仍然需要加超时/失败回退，因为 handler 可能挂起或弹修复对话框。
- 在本机（Office 16）验证：`.docx/.doc`、`.xlsx/.xls`、`.pptx/.ppt` 都有 Office handler；**`.csv` 没有**。因此 CSV 必须自己预览。
- 32 位 Office 注册的 32 位 handler 不能被 64 位主进程加载；ARM64 上还要考虑 x64 Office 的 handler。需要按架构做回退。
- LibreOffice 官方系统要求写明 Windows 最多需要 **1.5 GB** 磁盘；MPL-2.0。它的保真度是“LibreOffice 的版式”，不是“Word 的版式”，字体替换、复杂浮动对象、SmartArt 可能偏移。
- `docx-preview` 只支持 DOCX；README 明确说目录（TOC）没有高效支持、分页依赖文档里已有的分页标记，不能做到实时重排。它适合“快速看内容”，不适合“打印级预览”。它的 npm 周下载约 380 万（2026-11-11），不是冷门仓库。
- SheetJS 的 `sheet_to_html` 只输出单元格表格；图表、图片、条件格式、数据透视不要期待。ExcelJS 也没有内置的页面渲染器，需要自己遍历单元格画表。两者周下载分别约 1,570 万和 2,700 万，生态成熟；但 SheetJS 的 GitHub 仓库自 2024-04 起没有新提交，需接受“稳定但维护停滞”的风险。
- **PPTX 没有达标开源渲染器。** `@vue-office/pptx` 包装的 `pptx-preview` 源码是付费/不公开的；PPTXjs 2022 年后停更；`pptx-renderer` 等新项目星数/使用量不足。不要为了“支持 PPTX”引入一个不可维护的闭源包。
- 商业 SDK 里，**GroupDocs.Viewer** 覆盖 Office、PDF、CAD、Visio、压缩包、邮件等 190+ 格式，输出 HTML/PDF/图片；**Aspose.Total** 的 Word/Slides/Cells 保真度高；**Syncfusion** 有社区许可（需符合其营收/团队条件），但 PowerPoint 只支持 PPTX/PPTM，不支持旧版 `.ppt`。Aspose 明确不支持 .NET Native AOT，其他家也未承诺；只能放独立进程。

#### 推荐路由

| 扩展名 | 第一优先 | 第二优先 | 保底 |
| --- | --- | --- | --- |
| `.docx` | Shell（Office 已安装） | LibreOffice 可选组件 | `docx-preview`；再失败显示 OOXML 缩略图/元数据 |
| `.xlsx` / `.csv` | Shell（仅 XLSX；CSV 无 handler） | LibreOffice 可选组件 | SheetJS 表格（限制行列/工作表数） |
| `.pptx` | Shell（Office 已安装） | LibreOffice 可选组件 | `docProps/thumbnail.jpeg` + 幻灯片标题大纲；再失败元数据 |
| `.doc` / `.xls` / `.ppt` | Shell（Office 已安装） | LibreOffice 可选组件 | 缩略图/元数据；不承诺内容预览 |
| `.docm/.xlsm/.pptm` | 同对应格式 | 同对应格式 | 显示“宏已禁用”提示；绝不执行宏 |

补充：

- 所有 OOXML 文件都可以先尝试读取 `docProps/thumbnail.jpeg`，这是零依赖、零风险的快速兜底；PPTX 通常带缩略图，DOCX/XLSX 不一定。
- `.csv` 用原生表格控件或轻量解析器即可，不必进 WebView2；要处理分隔符、编码（UTF-8/GBK）、超大文件截断。
- 密码保护/IRM/受保护视图：Shell 可能失败或弹窗；内置方案要显示明确错误，不要无限重试。

### 3.2 图片

| 方案 | 覆盖 | 体积 | 许可 | 结论 |
| --- | --- | --- | --- | --- |
| WIC / `BitmapImage` | BMP/GIF/ICO/JPEG/PNG/TIFF/JPEG-XR/DDS 等系统编解码器 | 0 | Windows 平台 | **默认** |
| `SvgImageSource` | 静态 SVG 1.1（安全静态模式，不支持动画/交互） | 0 | Windows App SDK | **默认，替代当前 SVG 走文本/Shell** |
| 系统扩展（HEIF/AVIF/RAW/WebP） | 取决于用户是否安装 Microsoft Store 编解码器 | 0 | 系统/商店 | **检测可用性，失败给安装提示** |
| SkiaSharp | PNG/JPEG/WebP/GIF 等；高性能缩放 | 原生资产约 12.7 MB | MIT | 需要可靠 WebP/统一解码时可选 |
| Magick.NET | 200+ 格式，含 HEIC/AVIF/RAW 等委托 | x64 包约 42 MB | Apache-2.0（含第三方委托许可） | **仅作为可选“全格式图片组件”，不默认** |
| ImageSharp | 常见格式 | ~1.3 MB 托管 | Six Labors Split License，商业使用有门槛 | **不采用**：许可证复杂，且与 WIC 重复 |
| LibRaw | 相机 RAW | 小 | LGPL/CDDL | 不单独引入；需要 RAW 时由 Magick.NET/系统 RAW 扩展覆盖 |

建议：

- 图片预览加 `ScrollViewer` + 缩放/平移 + 适应窗口/100%；透明图加棋盘格；GIF/动态 WebP 保留动画但提供暂停。
- 解码前检查像素数（例如上限 100 MP）和文件大小；用 `DecodePixelWidth` 限制到预览尺寸，避免超大图 OOM。
- 色彩配置交给 WIC；HDR/广色域图不要自己转色域。
- WebP 在干净 Windows 上不保证有 WIC 编解码器，当前扩展名列表不能等于“一定可预览”。失败时给出“安装系统 WebP/HEIF 扩展”或“使用可选图片组件”的提示。
- SVG 用 `SvgImageSource` 的静态安全模式，不要用 WebView2 加载 SVG，避免脚本/外链风险。

### 3.3 PDF

| 方案 | 保真度 | 体积 | 许可 | 结论 |
| --- | --- | --- | --- | --- |
| `Windows.Data.Pdf` | 系统 PDF 渲染器；支持密码、页渲染 | 0 | Windows 平台 | **默认，重新启用现有实现** |
| PDFium 包装（PDFtoImage / Docnet.Core） | 高；更接近 Chrome | PDFtoImage 约 18 MB 依赖；Docnet 约 18 MB | MIT/BSD 类 | 原生 API 不够时再评估；Docnet 近两年未更新 |
| PDF.js（WebView2） | 高；有文本层/搜索 | ~2-4 MB 构建产物 | Apache-2.0 | 需要文本选择/搜索时可选；纯预览不必 |
| MuPDF | 高 | 中等 | AGPL/商业 | 不采用 |

建议：

- 恢复 `PDFPreviewViewModel` 分支，但改成**首屏只渲染第 1 页，后续页按需加载**；不要一次预渲染 10 页。
- 用 `PdfPageRenderOptions` 限制输出宽高/DPI；页面位图用 `BitmapImage.DecodePixelWidth` 控制内存。
- 支持密码输入；失败区分“加密”“损坏”“不支持”。
- 页码/缩放/适应宽度/旋转做进预览工具栏；Popup 复用同一个页面源。
- 如果后续需要文本搜索/选择，再引入 PDF.js，而不是现在就把 PDF 全部搬到 WebView2。

### 3.4 压缩包

| 方案 | 覆盖 | 许可 | 结论 |
| --- | --- | --- | --- |
| 7z.dll 26.00 + SevenZipSharp 分支 | ZIP/7z/RAR/TAR/GZ/XZ/LZH 等 | LGPL-2.1+（含 unRAR 限制） | **继续使用**；项目已带 x86/x64/ARM64 三份 DLL |
| SharpCompress | ZIP/TAR/GZ/BZ2/XZ/7z/RAR 等，纯托管 | MIT | 可选备选；不作为第二套默认引擎 |
| SharpZipLib | ZIP/TAR/GZ/BZ2 | MIT | 已有 ZIP 回退路径，保留 |
| libarchive | 多格式 | BSD | 不引入；与 7z 重复 |

建议：

- 压缩包预览从“统计卡片”升级为“条目列表”：名称、大小、压缩后大小、修改时间、是否加密；首屏最多 200 条，支持搜索/排序。
- 选中条目后按需解压到临时文件/流，再走统一 provider；限制单条目解压后大小（例如 100 MB）、总解压大小（例如 1 GB）、条目数（例如 10,000）、压缩比（例如 1000:1）。
- 嵌套压缩包最多展开 2 层；阻止路径穿越（`..\`、绝对路径、符号链接）。
- 加密压缩包复用现有密码流程；密码不写日志、不缓存。
- 7z 的解析/解压是同步 native 调用，放 `Task.Run`；如果要严格防挂起，后续把压缩包解析放进独立 worker 进程。

### 3.5 图纸与 3D

#### DXF / DWG

| 方案 | 能力 | 许可/维护 | 结论 |
| --- | --- | --- | --- |
| `ACadSharp` | 读写 DWG/DXF，解析实体/图层/块；**不自带渲染器**；DWG 读取覆盖 R13–R2018，R12 DWG 不支持 | MIT，925 星，2.2M NuGet 下载，2026-11 活跃；纯托管但需验证 AOT/trimming | **可用于 DWG→DXF 转换的 spike**；AOT 不过就放 helper |
| `ACadSharp.Image` | DWG/DXF→PNG/SVG，功能看起来正好 | MIT，但仅 **10 星、0.1.8、约 4k 下载**，依赖 ImageSharp | **拒绝用于生产**：太新、太小众 |
| `netDxf` | DXF 读写，无渲染 | MIT，1.1k 星，**2023 归档** | 拒绝 |
| `dxf-viewer` | WebGL DXF 2D 查看器，图层/缩放/大文件优化；**只保证 ASCII DXF，二进制 DXF 未验证** | MPL-2.0，586 星，135k 周下载，2026-10 活跃 | **DXF 预览的务实选择**；需接受功能不完整 |
| `three-dxf` | DXF→three.js | MIT，646 星，npm 最后发布 2021，仓库 2024 后停更 | 拒绝 |
| LibreDWG | DWG 读写、`dwg2dxf`/`dwg2SVG`；可读全部 DWG，但高级 R2010+ 对象可能跳过，SVG 是“initial”质量 | GPL-3.0，1.6k 星，活跃 | 只适合独立进程 + 法务确认 |
| ODA Drawings SDK | 工业级 DWG/DGN，官方 .NET 包装 | 商业；公开定价约 3,000 美元/年起、100 座分发上限，不限座更贵（以官方报价为准） | 保真度最稳，但对免费 OSS 分发不现实 |
| Aspose.CAD | DWG/DXF/DGN/IGES/STL/OBJ 等，转 PDF/PNG；不含 STEP | 商业，NuGet 约 72 MB | 预算允许时的快速保真方案 |
| GroupDocs.Viewer | DWG/DXF/Visio/Office/压缩包等 190+ 格式 | 商业 | 可作为“图纸 + Office”统一方案 |

`dxf-viewer` 的 README 明确列出未完成项：纸空间/布局/视口、线型图案、宽线、部分填充、部分标注、非 UTF-8 编码等。它适合“看清图形”，不适合“施工图级审阅”。它 npm 周下载约 13.5 万，是 DXF 这个细分领域的常用选择；但如果严格遵守“≥1,000 星”，它不达标，需要把 DXF 也降级为可选/商业方案。如果产品承诺“常见图纸都能看”，需要把预期写清楚。

**DWG 建议路线：**

1. 装了 AutoCAD/DWG TrueView 等：Shell handler 优先。
2. 没装：先做 `ACadSharp` 读 DWG → 写临时 DXF → `dxf-viewer` 渲染的 spike；用真实样本覆盖 R14/R2000/R2004/R2007/R2010/R2018，确认块/图层/中文/标注/代理对象丢失情况。注意 ACadSharp 不支持 R12 DWG，且二进制 DXF 的兼容性要单独验证。
3. spike 不达标：商业 SDK（GroupDocs/ODA/Aspose.CAD），或 LibreDWG 独立进程（GPL 合规 + 质量评估）。
4. 不建议自研 DWG 渲染器；工作量远超预览功能本身。

#### Visio

- `.vsd/.vsdx` 没有可信的开源渲染器。LibreOffice 通过 libvisio 能导入一部分 Visio，但版式/形状支持有限，不能作为“完整预览”承诺。
- 装了 Visio：Shell handler 优先；本机若注册了 “Microsoft Visio previewer” CLSID `{21E17C2F-AD3A-4b89-841F-09CFE02D16B7}` 就能直接复用现有 Shell 路径。
- 没装：商业 SDK（GroupDocs/Aspose.Diagram 支持 Visio）或“用默认应用打开”；不要用 XML 自己画形状。

#### 3D / STEP / IGES

- STL/OBJ/GLTF/GLB/3MF/PLY：`three.js`（118k 星，MIT）在 WebView2 中加载最稳；`model-viewer`（8.3k，Apache-2.0）适合 GLTF/GLB 的成品查看器。
- 原生方案：HelixToolkit.WinUI（2.3k，MIT）可以渲染 D3D11 场景，但它的 WinUI 包仍依赖已停止维护的 SharpDX，且没有现成的 glTF/3MF loader；**不建议作为新工作的首选**。
- STEP/IGES/BREP：Open CASCADE（OCCT，3.1k，LGPL-2.1）是唯一成熟开源内核，但 C++ 体积大、没有维护良好的免费 .NET 绑定。务实路线是 `occt-import-js`（296 星，LGPL-2.1，约 11 MB WASM）在 WebView2 中把 STEP/IGES 转成三角网格，再交给 three.js；它低于 1k 星且 2024 后更新较慢，只能作为 P2 试验。
- 3D 预览要限制三角形数/文件大小，默认关闭自动旋转，避免风扇狂转。

### 3.6 其他常见格式

| 格式 | 建议 |
| --- | --- |
| 文本/代码/JSON/XML | 保留现有原生控件；把 10k 字符上限提高到可配置（如 200k），但仍截断并提示 |
| Markdown | 保留 `MarkdownTextBlock`；本地图片/相对链接后续增强；不要为 Markdown 引入 WebView2 |
| RTF | 保留 `RichEditBox`；大文件/畸形 RTF 加超时 |
| 音频/视频 | 保留 `MediaPlayerElement`；不引入 LibVLC（+体积/许可），除非有明确的 MKV/ASS 等需求 |
| 邮件 `.eml/.msg` | P2：MimeKit（MIT，2k 星）解析 EML；MSGReader（MIT，557 星）解析 MSG；HTML 正文必须净化后显示 |
| 字体 | P2：DirectWrite 渲染字样；现有字体缩略图逻辑可复用 |
| HTML | P2：WebView2 沙箱 + 禁用脚本 + 禁止外链；不要直接复用当前“映射父目录”的实现 |
| 无扩展名/未知 | 保留文本探测；二进制显示十六进制/元数据，不显示乱码文本 |

## 4. 推荐架构

### 4.1 分层

```text
PreviewRequest (path, entry, size, mtime, theme, max size)
        │
        ▼
PreviewCoordinator (debounce, cancellation, cache, limits)
        │
        ├─ NativePreviewProvider (AOT, in-proc)
        │    ├─ Image (WIC / SvgImageSource)
        │    ├─ Pdf (Windows.Data.Pdf, lazy pages)
        │    ├─ Archive (7z list + entry stream)
        │    ├─ Text/Code/Markdown/Rtf/Media
        │    └─ OoxmlThumbnail
        │
        ├─ WebPreviewProvider (WebView2, sandboxed)
        │    ├─ Docx (docx-preview)
        │    ├─ Xlsx/Csv (SheetJS table)
        │    ├─ Dxf (dxf-viewer)
        │    └─ 3D (three.js)
        │
        ├─ ShellPreviewProvider (prevhost.exe)
        │    └─ Office / Visio / CAD / other registered handlers
        │
        └─ ExternalPreviewProvider (optional helper)
             ├─ LibreOffice → PDF
             └─ Commercial SDK → PDF/PNG
```

### 4.2 Provider 接口

不需要一次做成大框架；先定义一个最小接口，把现有分支迁进去：

```csharp
public interface IPreviewProvider
{
    string Id { get; }
    int Priority { get; }
    bool CanPreview(PreviewItem item);
    Task<PreviewResult> CreateAsync(PreviewItem item, PreviewContext context, CancellationToken ct);
}
```

`PreviewResult` 至少包含：

- `UIElement? Content`（原生控件）
- `PreviewKind Kind`（Native / WebView / Shell / External）
- `IReadOnlyList<FileProperty> Details`
- `PreviewCapabilities`（zoom、page navigation、search、open）
- `PreviewError? Error`（unsupported / encrypted / timeout / parse failed）

调度规则：

- 默认按 `Priority` 从高到低；Office/CAD 这类“系统 handler 保真度更高”的格式，用扩展名策略覆盖默认顺序。
- 每个 provider 自己声明 `MaxFileSize`、`Timeout`、`Isolation`，协调器统一执行。
- `Shell` 失败（handler 缺失/超时/崩溃）时，继续尝试下一个 provider；不要只 `return null`。
- Popup 和 InfoPane 共用同一个 `PreviewCoordinator`，只是尺寸/工具栏不同。

### 4.3 宿主选择

| 内容 | 宿主 | 原因 |
| --- | --- | --- |
| 图片、PDF、文本、媒体、压缩包列表 | 原生 XAML | 启动快、内存低、AOT 安全 |
| DOCX/XLSX/DXF/3D | 单个复用的 WebView2 宿主 | JS 生态成熟、包体小；避免每次选中新建 WebView2 |
| Office/Visio/CAD Shell | `prevhost.exe` + DComp 子窗口 | 保真度高、进程外隔离 |
| LibreOffice/商业 SDK | 独立 helper 进程 | 体积/许可/AOT 隔离；不可信文档不进入主进程 |

WebView2 建议：

- 复用 `WebViewEnvironment.GetAsync()`，但每个预览宿主只创建一个 `WebView2`，切换文件时导航到新的本地 HTML，而不是新建控件。
- 只映射“本次预览的临时目录”，不要映射父目录；生成 HTML 时把图片/字体复制到临时目录或转 data URL。
- 设置 `AreDevToolsEnabled=false`、`AreDefaultContextMenusEnabled=false`、`IsScriptEnabled=false`（仅静态 HTML）；DOCX/XLSX/DXF 这类必须执行本地 JS 的页面，只从应用包/临时目录加载脚本，禁止内联事件和外部脚本。
- 用 `WebResourceRequested` 白名单只允许虚拟主机和 `data:`/`blob:`；其余请求直接取消；同时加 CSP，禁止 `connect-src` 外联。
- 通过 `WebMessageReceived` 接收“渲染完成/失败/需要密码”，不要靠固定延时。

### 4.4 缓存

- 键：规范化路径 + 文件大小 + 修改时间 + provider 版本 + 关键选项（目标尺寸/页码）。
- 内存：解码后的缩略图/首屏位图，LRU，建议 64 MB。
- 磁盘：转换后的 PDF、Office/CAD 缩略图，LRU，建议 512 MB，放在 `LocalCache` 下。
- 清理：设置页提供“清除预览缓存”；退出时可保留（提升二次打开速度），但要在隐私说明里写清楚。
- 不缓存密码、不缓存明文敏感信息到日志。

### 4.5 资源与安全限制（建议默认值）

| 限制 | 建议值 | 说明 |
| --- | --- | --- |
| 单文件大小（原生预览） | 200 MB | 超过显示“文件过大”，仍可打开 |
| 文本/代码读取 | 200k 字符 | 超出截断并提示 |
| PDF 页数 | 首屏 1 页，按需加载，最多 200 页 | 不一次渲染全部 |
| PDF 渲染尺寸 | 最大边 2000 px | 控制内存 |
| 图片像素 | 100 MP | 解码前检查 |
| 压缩包条目 | 10,000 | 列表最多显示 200 条 |
| 单条目解压 | 100 MB | 超出拒绝预览 |
| 总解压 | 1 GB | 超出拒绝 |
| 压缩比 | 1000:1 | 防 zip bomb |
| Office 转换 | 30 秒/文件 | helper 超时 kill |
| CAD 转换 | 30 秒/文件 | 独立 helper 或 WebView2 worker |
| WebView2 渲染 | 15 秒 | 超时显示错误，不阻塞 UI |
| 预览内存 | 约 200 MB/会话 | 超出释放/降级 |

### 4.6 线程与生命周期

- 选择变化做 150-250 ms 防抖；沿用现有 `previewRequestVersion` 思路，旧请求必须作废。
- 文件 IO/解析放线程池；UI 控件创建/更新回 DispatcherQueue。
- Shell COM 的启动要继续走现有的延迟入队边界，避免在 `ContainerContentChanging`/布局回调里直接调 COM。
- WebView2 初始化在 UI 线程；导航/JS 调用不要在渲染回调里同步等待。
- 预览控件卸载时取消令牌、停止媒体、释放 WebView2/子窗口/DComp 资源；现有 `UnloadPreview` 的清理顺序要保持。

### 4.7 包体与部署

| 组件 | 增量（量级） | 默认 | 备注 |
| --- | --- | --- | --- |
| 原生 API + 现有 7z | 0 | 是 | 7z 已随包 |
| docx-preview + JSZip | ~1 MB | 是 | 压缩后更小 |
| SheetJS | ~1 MB | 是 | 只取需要的构建 |
| dxf-viewer + three | ~1.5 MB | 是（若做 DXF） | 需裁剪 |
| ACadSharp | ~3.8 MB | 是（若做 DWG→DXF） | 纯托管 |
| SkiaSharp | ~12.7 MB 原生资产 | 否 | 仅 WebP/统一解码需要时 |
| Magick.NET | ~42 MB | 否 | 全格式图片可选组件 |
| LibreOffice | ~1.5 GB 安装 | 否 | 检测已安装或按需下载 |
| 商业 SDK + helper | 约 50-150 MB + 运行时 | 否 | 需授权 |

> 以上是包体/安装体积的量级估算，最终以裁剪、压缩和实际发布为准。

## 5. 分阶段路线图

| 阶段 | 内容 | 估算 | 风险 |
| --- | --- | --- | --- |
| P0 基础 | Provider 接口 + 协调器 + 限制/超时/缓存骨架；Popup 复用；PDF 重启用；SVG/图片增强；压缩包条目列表 | 8-12 人日 | 重构触及现有预览分支，需回归测试 |
| P1 Office 快速路径 | Shell 优先策略；DOCX `docx-preview`；XLSX/CSV SheetJS；OOXML 缩略图；PPTX 大纲；PDF 密码/页导航 | 10-15 人日 | WebView2 生命周期/安全；大文件性能 |
| P2 可选完整 Office | 检测/调用 LibreOffice；独立进程、超时、缓存；设置页“安装/启用完整预览” | 8-12 人日 | 1.5 GB 体积、法务/更新、冷启动 |
| P3 图纸/3D | DXF `dxf-viewer`；STL/OBJ/GLTF 用 three.js；DWG `ACadSharp`→DXF spike；Visio/商业 SDK 评估 | 8-15 人日 | CAD 保真度不确定；开源方案功能不完整 |
| P4 长尾 | STEP/IGES（`occt-import-js`）、DGN、EML/MSG、字体、HTML 沙箱 | 按需 | 不要挤占 P0/P1 |

粗略总计 6-10 周（单人），不含商业授权采购和法务评估。建议先做 P0 + P1 的 DOCX/XLSX/PDF/压缩包，这四个能覆盖大多数日常文件；PPTX/DWG/Visio 明确标注“依赖外部应用或可选组件”，避免过度承诺。DXF + 常见 3D 网格可以作为一个独立的 1-2 周小迭代，不必等 Office 全部完成。

## 6. 风险与合规

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| 不可信文档解析漏洞 | RCE/崩溃/信息泄露 | 原生系统编解码器优先；第三方解析放 helper/WebView2；及时更新 7z/PDFium/LibreOffice |
| 压缩炸弹/解压路径穿越 | 磁盘/内存耗尽、写到意外位置 | 大小/条目/压缩比限制；规范化路径；禁止符号链接 |
| WebView2 XSS/外链 | 本地文件泄露、网络请求 | 只映射临时目录；禁用脚本/导航/下载；CSP + 请求白名单 |
| Shell handler 挂起 | 预览卡死 | 超时、取消、失败回退；不要把 handler 当唯一路径 |
| Office handler 位数不匹配 | 部分机器无预览 | 检测架构；回退内置；ARM64 重点测试 |
| LibreOffice 体积/冷启动 | 安装包膨胀、首屏慢 | 可选组件；检测已安装；缓存转换结果 |
| GPL/AGPL 合规 | 分发义务 | LibreDWG/OnlyOffice 独立进程并法务确认；优先 MIT/Apache/MPL |
| 商业 SDK 许可/AOT | 成本、无法进主进程 | 先谈授权；独立 helper；接口可替换 |
| 预览缓存隐私 | 敏感文件残留 | 可清理、可关闭、不缓存密码/日志 |
| 云文件/网络路径 | 下载大文件、延迟 | 尊重现有 cloud 逻辑；超时/取消；大小限制 |

许可证备注：

- 7z.dll：LGPL-2.1+，含 unRAR 限制；项目已在分发，保留 LICENSE/ notice。
- LibreOffice：MPL-2.0；如果捆绑，保留许可证与源码获取方式，不修改商标。
- `docx-preview`/SheetJS/PDF.js：Apache-2.0；保留 NOTICE/LICENSE。
- `dxf-viewer`：MPL-2.0；捆绑压缩后的 JS 也要提供对应源码。
- ACadSharp/three.js/SkiaSharp/Magick.NET：MIT/Apache-2.0；保留版权声明。
- LibreDWG：GPL-3.0；独立进程并不自动消除 GPL 义务，需法务确认。
- ImageSharp：Six Labors Split License，不是 MIT；商业使用有营收/授权条件，按现项目许可不应默认引入。

## 7. 需要拍板的问题

1. **是否允许商业 SDK 预算？** 如果“Office + Visio + DWG 全部高保真”是硬指标，GroupDocs.Viewer 或 Aspose.Total 比自研/拼开源便宜。
2. **LibreOffice 是否作为可选组件？** 默认安装 1.5 GB 不现实；检测用户已安装 + 设置页引导，是折中方案。
3. **PPTX/旧版 Office 的最低要求是什么？** 缩略图 + 大纲是否可接受？如果不能接受，就需要 LibreOffice/商业 SDK。
4. **DWG 是否必须支持？** 如果只要求 DXF，`dxf-viewer` 可以；DWG 需要接受“转 DXF 可能丢内容”或使用商业 SDK。
5. **Visio/STEP/IGES/3D 是否在首版范围？** 建议放 P3/P4，不要挤占 Office/PDF/压缩包。
6. **默认包体预算多少？** 建议默认增量 ≤ 10 MB；超过 20 MB 的依赖一律可选。
7. **缓存策略？** 是否允许把转换后的 PDF/缩略图留在本机？默认开启但可清理，还是默认关闭？

## 8. 仓库与组件清单

> 星数/活跃度截至 2026-11-11；来源为 GitHub API、shields.io、npm registry、NuGet。部分项目低于 1,000 星但为类别事实标准，已标注“例外”。

| 项目 | 星数 | 许可 | 最近活动 | 角色 | 结论 |
| --- | ---: | --- | --- | --- | --- |
| Windows WIC / `SvgImageSource` / `Windows.Data.Pdf` | 官方 | 平台 | 持续 | 图片/SVG/PDF | **采用** |
| 7-Zip (`ip7z/7zip`) | 4,475 | LGPL + unRAR | 2026-11 | 压缩包 | **采用（已在用）** |
| `files-community/SevenZipSharp` | 17 | LGPL-3.0 | 2026-08 | 7z 封装 | **保留**（项目自有分支，非新依赖） |
| SharpCompress | 2,595 | MIT | 2026-11 | 压缩包备选 | 备选，不新增默认 |
| SharpZipLib | 3,935 | MIT | 2024-09 | ZIP 回退 | 保留 |
| docx-preview | 2,147 | Apache-2.0 | 2026-09 | DOCX | **采用（无 Office 时）** |
| mammoth.js | 6,340 | BSD-2 | 2026-10 | DOCX 语义 HTML | 备选，保真度更低 |
| SheetJS | 36,343 | Apache-2.0 | 2024-04 | XLSX/CSV | **采用，但注意维护停滞** |
| ExcelJS | 15,513 | MIT | 2025-01 | XLSX 解析 | 可替代 SheetJS 的解析层，无渲染器 |
| vue-office | 5,754 | MIT | 2025-10 | 多格式 Vue 封装 | 仅参考；PPTX 模块依赖闭源 |
| PPTXjs | 279 | — | 2022-03 | PPTX | **拒绝（停更）** |
| pptx-preview | 136 | 源码付费 | 2025-10 | PPTX | **拒绝（源码不开放）** |
| LibreOffice | 4,624 | MPL-2.0 | 2026-11 | Office 全保真 | **可选组件** |
| ONLYOFFICE DocumentServer | 7,109 | AGPL-3.0 | 2026-10 | 服务端 Office | **拒绝（服务器栈）** |
| Collabora Online | 3,397 | MPL-2.0 | 2026-11 | 服务端 Office | **拒绝（WOPI 服务器）** |
| ACadSharp | 925 | MIT | 2026-11 | DWG/DXF 解析 | **例外可用；仅做 DWG→DXF spike** |
| ACadSharp.Image | 10 | MIT | 2026-11 | DWG/DXF 渲染 | **拒绝（0.1.x/太小众）** |
| netDxf | 1,148 | MIT | 2023-10 | DXF | **拒绝（归档）** |
| dxf-viewer | 586 | MPL-2.0 | 2026-10 | DXF 渲染 | **例外可用；功能有限** |
| three-dxf | 646 | MIT | 2024-01 | DXF→three | 拒绝（停更） |
| LibreDWG | 1,665 | GPL-3.0 | 2026-11 | DWG 转换 | 条件使用（独立进程+法务） |
| ODA Drawings SDK | 官方 | 商业 | 2026 | DWG/DGN | 仅预算允许 |
| Aspose.CAD | 官方 | 商业 | 2026-10 | DWG/DXF/DGN/IGES | 仅预算允许（约 72 MB） |
| three.js | 118,082 | MIT | 2026-11 | 3D/DXF 底层 | **采用（3D）** |
| model-viewer | 8,325 | Apache-2.0 | 2026-11 | GLTF/GLB | 3D 备选 |
| HelixToolkit | 2,293 | MIT | 2026-11 | 原生 3D | 不优先（SharpDX EOL） |
| Open CASCADE | 3,153 | LGPL-2.1 | 2026-11 | STEP/IGES | P4，不默认 |
| occt-import-js | 296 | LGPL-2.1 | 2024-12 | STEP/IGES WASM | P4 试验（低于 1k 星） |
| SkiaSharp | 5,638 | MIT | 2026-11 | 图片/PDF 渲染 | 可选 |
| Magick.NET | 4,001 | Apache-2.0 | 2026-11 | 全格式图片 | 可选，42 MB |
| ImageSharp | 8,049 | Six Labors Split | 2026-11 | 图片 | **不采用（许可）** |
| PDF.js | 54,123 | Apache-2.0 | 2026-11 | PDF（文本/搜索） | 可选 |
| PDFtoImage | 379 | MIT | 2026-11 | PDFium 渲染 | 条件可选（低于 1k 星） |
| Docnet.Core | 590 | MIT | 2024-05 | PDFium | 不采用（停更） |
| MimeKit | 2,010 | MIT | 2026-11 | EML | P2 |
| MSGReader | 557 | MIT | 2026-11 | MSG | P2（低于 1k 星） |
| QuickLook | 26,000 | 开源 | 活跃 | 快速预览参考 | 仅参考；不嵌入 |

## 9. 参考链接

- 微软 Preview Handler 架构：[Preview Handlers and Shell Preview Host](https://learn.microsoft.com/en-us/windows/win32/shell/preview-handlers)
- WIC 原生编解码器：[Windows Imaging Component Overview](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-about-windows-imaging-codec)
- `SvgImageSource`：[Windows App SDK 文档](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.svgimagesource)
- `Windows.Data.Pdf`：[PdfDocument](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfdocument)
- LibreOffice 命令行：[Starting LibreOffice Software With Parameters](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html)
- LibreOffice 系统要求（1.5 GB）：[System Requirements](https://www.libreoffice.org/system-requirements/)
- LibreOffice 许可证：[Licenses](https://www.libreoffice.org/about-us/licenses/)
- docx-preview：[GitHub](https://github.com/VolodymyrBaydalka/docxjs) · [npm](https://www.npmjs.com/package/docx-preview)
- SheetJS：[GitHub](https://github.com/SheetJS/sheetjs)
- ExcelJS：[GitHub](https://github.com/exceljs/exceljs)
- mammoth.js：[GitHub](https://github.com/mwilliamson/mammoth.js)
- SharpCompress：[GitHub](https://github.com/adamhathcock/sharpcompress)
- 7-Zip：[GitHub](https://github.com/ip7z/7zip)
- ACadSharp：[GitHub](https://github.com/DomCR/ACadSharp) · [NuGet](https://www.nuget.org/packages/ACadSharp)
- ACadSharp.Image：[NuGet](https://www.nuget.org/packages/ACadSharp.Image) · [GitHub](https://github.com/slaveoftime/ACadSharp.Image)
- dxf-viewer：[GitHub](https://github.com/vagran/dxf-viewer)
- LibreDWG：[GitHub](https://github.com/LibreDWG/libredwg)
- ODA Drawings SDK / 定价：[Products](https://www.opendesign.com/products/drawings) · [Pricing](https://www.opendesign.com/pricing)
- Aspose.CAD：[GitHub](https://github.com/aspose-cad/Aspose.CAD-for-.NET) · [NuGet](https://www.nuget.org/packages/Aspose.CAD)
- occt-import-js：[GitHub](https://github.com/kovacsv/occt-import-js)
- HelixToolkit：[GitHub](https://github.com/helix-toolkit/helix-toolkit)
- GroupDocs.Viewer 支持格式：[Supported file formats](https://docs.groupdocs.com/viewer/net/supported-document-formats/)
- Syncfusion Word→PDF：[DocIO Word to PDF](https://help.syncfusion.com/document-processing/word/conversions/word-to-pdf/net/word-to-pdf)
- Aspose.Words：[Product page](https://products.aspose.com/words/net/) · [Native AOT 不支持说明](https://forum.aspose.com/t/support-net-8-native-aot-deployment/275963)
- ONLYOFFICE：[DocumentServer](https://github.com/ONLYOFFICE/DocumentServer)
- Collabora Online：[GitHub](https://github.com/CollaboraOnline/online)
- QuickLook：[GitHub](https://github.com/QL-Win/QuickLook)
- three.js：[GitHub](https://github.com/mrdoob/three.js)
- PDF.js：[GitHub](https://github.com/mozilla/pdf.js)
- SkiaSharp：[GitHub](https://github.com/mono/SkiaSharp)
- Magick.NET：[GitHub](https://github.com/dlemstra/Magick.NET)

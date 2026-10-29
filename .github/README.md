<p align="center">
  <img alt="Files hero image" src="./assets/ReadmeHero.png" />
</p>

<p align="center">
  <a style="text-decoration:none" href="https://files.community/">
    <img src="https://img.shields.io/badge/Files-Website-F9B81F" alt="Files Website" /></a>
  <a style="text-decoration:none" href="https://github.com/files-community/Files/actions/workflows/ci.yml">
    <img src="https://github.com/files-community/Files/actions/workflows/ci.yml/badge.svg" alt="Files CI Status" /></a>
  <a style="text-decoration:none" href="https://crowdin.com/project/files-app">
    <img src="https://badges.crowdin.net/files-app/localized.svg" alt="Files Localization Status" /></a>
  <a style="text-decoration:none" href="https://discord.gg/files">
    <img src="https://img.shields.io/discord/725513575971684472?label=Discord&color=7289da" alt="Files Discord" /></a>
</p>

Files is a modern file manager that helps users organize their files and folders. Our mission with Files is to build the best file manager for Windows, and we’re proud to be building it out in the open so everyone can participate. User feedback helps shape the features we work on, & the bug reports on GitHub help to make Files more reliable. Built and maintained by the open-source community, Files features robust multitasking experiences, file tags, deep integrations, and an intuitive design.

## 本 Fork 与原版的区别

本仓库 fork 自 [files-community/Files](https://github.com/files-community/Files)，已同步上游至 `d8eb92f66`。在保留原版文件管理能力的基础上，本 Fork 增加了以下差异。便携版安装包请从 [Releases](https://github.com/wangjiati/Files/releases) 下载。

### 功能对比

| 领域 | 上游原版 | 本 Fork |
| --- | --- | --- |
| 分发方式 | MSIX/Store 与经典安装包 | 新增 `Distribution=Msix` 与 `Distribution=Portable`；提供完整目录便携 ZIP |
| 数据位置 | 包数据或用户目录 | 便携版必要数据位于程序目录 `Data`，可随目录复制和移动 |
| 运行依赖 | 依赖已安装的运行时 | 便携版自带 .NET 与 Windows App SDK，采用 Native AOT，无需额外安装或管理员权限 |
| 系统集成 | 安装后提供文件关联、协议、Shell/COM 等集成 | 便携版不注册这些集成；本 Fork 的 MSIX 版默认不接管，显式开启后显示状态，并可恢复原值 |
| 更新方式 | 官方更新源 | 手动更新，不访问官方更新源，不依赖后台更新服务 |
| 时间格式 | 全局日期格式预设 | 支持全局、按列、按目录及递归目录规则，支持自定义格式、秒、毫秒和实时预览 |
| 目录筛选 | 名称包含过滤 | 列头筛选（名称、类型、扩展名、标签、大小、修改/创建日期）及安全正则匹配 |
| SQL 查询 | 无 | 只读 SQLite SQL 工作台，支持 CTE、窗口函数、连接和聚合，文件与表格双结果视图 |
| 新建文件夹命名 | 固定默认名称 | 命名方案（名称、日期、时间、年月、顺序、分隔符及预设） |
| 本地化 | 上游语言包 | 补充简体中文与繁体中文的命名方案等界面文本 |

### 便携版特性

- 解压整个 ZIP 后运行 `Files.exe`，支持含空格或中文的路径。
- 设置、标签、布局、日志和缓存位于程序目录的 `Data` 文件夹。
- 同一 `Data` 目录默认只允许一个实例；数据目录不可写时会明确报错，不会静默回退到用户目录。
- 便携版与本 Fork 的 MSIX 版、原版 Files 的数据默认隔离。
- 首版仅提供 Windows x64 完整目录便携包，不提供单文件版本。

### 高级功能说明

- **时间格式**：优先级为目录列规则 > 目录默认 > 全局列规则 > 全局默认；目录规则可选择应用到子目录。排序和筛选始终使用原始时间值。
- **筛选与正则**：只作用于当前已加载目录，不递归扫描磁盘；支持大小写选项、匹配超时和取消，非法或超时正则不会导致应用崩溃。
- **SQL 工作台**：仅允许只读 `SELECT`，禁止写入、DDL、多语句、`ATTACH`、扩展和文件访问；查询在私有内存快照上执行。
- **MSIX 版**：使用独立包身份 `Files.Internal` 和独立证书签名，默认不接管原版系统集成；不支持通过官方渠道自动更新。

## Installing and running Files

Files is a community-driven project that depends on your support to grow and improve. Please consider purchasing Files through the Microsoft Store or supporting us on GitHub if you use the classic installer.

You can also use the preview version alongside the stable release to get early access to new features and improvements.

<p align="left">
  <!-- Store Badge -->
  <a style="text-decoration:none" href="https://apps.microsoft.com/detail/9NGHP3DX8HDX?launch=true&mode=full">
    <picture>
      <source media="(prefers-color-scheme: light)" srcset="./assets/StoreBadge-dark.png" height="80" />
      <img src="./assets/StoreBadge-light.png" height="80" /></picture></a>
  &ensp;
  <!-- Classic Installer Badge -->
  <a style="text-decoration:none" href="https://files.community/download">
    <picture>
      <source media="(prefers-color-scheme: light)" srcset="./assets/ClassicInstallerBadge-dark.png" height="80" />
      <img src="./assets/ClassicInstallerBadge-light.png" height="80" /></picture></a>
</p>

## Building from source

Instructions for building the source code can be found on our [documentation site](https://files.community/docs/contributing/building-from-source).


## Contributing to Files

Want to contribute to this project? Let us know with an [issue](https://github.com/files-community/Files/issues) that communicates your intent to create a [pull request](https://github.com/files-community/Files/pulls). Also, view our [contributing guidelines](https://github.com/files-community/Files/blob/main/.github/CONTRIBUTING.md) to make sure you're up to date on the coding conventions.

Looking for a place to start? Check out the [task board](https://github.com/orgs/files-community/projects/3/views/2), where you can sort tasks by size and priority.

## Screenshots

![Files](./assets/FilesScreenshot.png)

<p align="center">
  <img alt="Files hero image" src="./assets/ReadmeHero.png" />
</p>

Files 是一个现代文件管理器，目标是打造 Windows 上最好用的文件管理器。项目由开源社区公开构建，功能与问题反馈共同塑造它的方向。

## 本 Fork

本仓库 fork 自 [files-community/Files](https://github.com/files-community/Files)，已同步上游至 `d8eb92f66`。在保留上游文件管理能力的基础上，本分支主要增加了：

- 完整目录便携版：解压即用，自带运行时，数据随目录迁移
- 全局、按列、按目录的时间格式
- 详细信息视图列头筛选、正则匹配与可复制的视图地址
- 只读 SQL 查询工作台与 12 列 CSV / Excel 导出
- 带预览、变量、子文件夹深度和整批撤销的批量重命名
- 新建文件夹命名方案、文件系统链接可视化

完整的功能对照、使用方式、限制与界面截图见 **[功能差异说明](https://wangjiati.github.io/Files/)**。

## 下载与安装

本 Fork 仅通过 [GitHub Releases](https://github.com/wangjiati/Files/releases) 分发。

- **便携版**：下载 `Files.Internal_<version>_x64-portable.zip`，解压后运行 `Files.exe`。
- **MSIX**：在 Releases 中下载安装包，并按发布说明信任内部证书。

便携版的数据位于程序目录的 `Data` 文件夹，可随目录复制和移动。升级为手动操作，不访问官方更新源，也不自动接管原版 Files 的系统集成。

## 从源码构建

构建说明见上游文档站：[Building from source](https://files.community/docs/contributing/building-from-source)。

## 贡献

欢迎通过 [Issues](https://github.com/wangjiati/Files/issues) 反馈问题或提出建议。提交 PR 前请阅读 [贡献指南](https://github.com/wangjiati/Files/blob/main/.github/CONTRIBUTING.md)。

## 许可

Files 原项目采用 [MIT](https://github.com/wangjiati/Files/blob/main/LICENSE-MIT) 许可证；部分组件与资源遵循仓库中的 [MPL](https://github.com/wangjiati/Files/blob/main/LICENSE-MPL) 双许可说明。本分支保留原项目的版权与许可声明。

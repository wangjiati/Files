# Files - Internal 安装说明

适用版本：`4.3.1.2`　平台：Windows x64

这是一份纯文字版说明。同一份内容的图解版见同目录的 `install-guide.png`，可以直接转发给要安装的人。

---

## 一、为什么需要先跑一次 .bat

内部版使用自签名证书签名，不是公网 CA 签发的。Windows 默认不信任这张证书，于是会出现两种看起来无关、实际同源的现象：

| 现象 | 真实原因 |
| --- | --- |
| 安装 MSIX 时报 `0x800B010A`「无法验证此应用包的发布者证书」 | 证书链构建不到受信任的根 |
| 便携版运行后输入法没有候选词预览 | 启动器无法注册身份包，退回无身份启动 |

`Trust-InternalCertificate.bat` 把随包发布的 `.cer` 导入本机「受信任人」存储，两个问题一起解决。**每台机器只需要执行一次**，证书有效期到 2029-11-03，期间升级版本不用重复导入。

> 只导入公钥证书（`.cer`），不涉及私钥。私钥始终留在打包机上，不会随发布包分发。

---

## 二、安装前准备

### 系统要求

- Windows 10 版本 2004（内部版本 19041）或更高，或 Windows 11
- x64
- 安装版额外需要 Microsoft Windows App Runtime `2.6.4`（x64）

### 需要准备的文件

发布目录分成两个文件夹，各自独立，拿走其中一个就能用：

```text
msix\       安装版：安装包 + 证书 + 信任脚本 + 说明
portable\   便携版：便携压缩包 + 说明
```

| 文件夹 | 文件 | 用途 |
| --- | --- | --- |
| `msix\` | `Files.Internal_4.3.1.2_x64.msix` | 安装版安装包 |
| `msix\` | `Files.Internal_4.3.1.2_x64.cer` | 签名证书公钥，导入用 |
| `msix\` | `Trust-InternalCertificate.bat` | 一键导入证书的脚本 |
| `msix\` | `Files.Internal_4.3.1.2_x64-msix.zip` | 上述文件的打包，便于整包转发 |
| `portable\` | `Files.Internal_4.3.1.2_x64-portable.zip` | 便携版本体，解压即用 |
| 两者 | `install-guide.md`、`install-guide.png` | 本说明 |
| 两者 | `SHA256SUMS.txt`、`RELEASE-INFO.txt` | 完整性校验与版本信息 |

两个文件夹的 `SHA256SUMS.txt` 各自覆盖本文件夹内的文件，可以分别校验。

### 校验文件完整性（可选）

在发布目录打开 PowerShell：

```powershell
Get-FileHash .\Files.Internal_4.3.1.2_x64.msix -Algorithm SHA256
Get-FileHash .\Files.Internal_4.3.1.2_x64-portable.zip -Algorithm SHA256
```

结果与 `SHA256SUMS.txt` 中对应行一致即可。命令行的等价写法：

```bat
certutil -hashfile Files.Internal_4.3.1.2_x64.msix SHA256
```

---

## 三、安装步骤

### 第 1 步：准备文件

在 `msix\` 文件夹里这两个文件本来就在一起，直接在该目录运行即可。

脚本只认**自己所在目录**的 `.cer`，所以不要把 `.bat` 单独拷到别处。

### 第 2 步：运行脚本

双击 `Trust-InternalCertificate.bat`。

- 脚本会检测权限，未提权时自动通过 UAC 重新拉起自己
- 在 UAC 提示中点「是」
- 如果 UAC 窗口没有出现，右键脚本 →「以管理员身份运行」

### 第 3 步：确认成功

窗口显示下面这行即完成：

```text
Done. The certificate is now trusted for this machine.
```

失败时脚本以退出码 `1` 结束，并打印 `certutil` 的原始错误。最常见的是没有管理员权限导致的 `0x80070005`。

### 第 4 步：安装（安装版与便携版二选一）

**方式 A — 安装版（推荐日常使用）**

1. 双击 `Files.Internal_4.3.1.2_x64.msix`
2. 在应用安装程序窗口点「安装」
3. 如果提示缺少框架，先安装 Microsoft Windows App Runtime `2.6.4`（x64）

**方式 B — 便携版（不写注册表，数据随目录走）**

1. 把 `Files.Internal_4.3.1.2_x64-portable.zip` 解压到一个固定目录
2. 不要单独移动里面的文件，整个目录可以整体拷贝或改名
3. 配置、日志、缓存都在同级的 `Data` 目录内
4. 根目录那个 `Files.Internal.Portable.Identity.msix` **不要删**，启动器要它来恢复输入法候选窗

### 第 5 步：启动

- 安装版：开始菜单搜索 `Files - Internal`
- 便携版：运行解压目录**根部**的 `Files.exe`

> 便携版根部那个 `Files.exe` 是启动器，它在首次启动时注册身份包（输入法候选窗依赖它）。直接运行 `lib\Files.exe` 也能开，但没有包身份，输入法可能没有候选词。

### 第 6 步：以后更新

下载新版本后重复第 1～4 步覆盖即可。证书不需要重新导入；只有换了签名证书的版本才需要重新执行第 2 步。

---

## 四、常见问题

| 现象 | 处理 |
| --- | --- |
| `0x800B010A` 无法验证发布者证书 | 证书未进入「受信任人」。用管理员身份重跑第 2 步，再安装 |
| `0x80073CF3` 或提示缺少框架 | 安装 Microsoft Windows App Runtime `2.6.4`（x64）后重试 |
| 便携版输入法没有候选词 | 身份包未注册成功。重跑第 2 步，然后**重新启动** `Files.exe` |
| 双击 `.bat` 没有反应 | 右键 →「以管理员身份运行」。若仍无反应，见下方手动导入 |
| 提示证书与发布者不匹配 | `.cer` 和 `.msix` 不是同一次构建。用发布目录里的原配文件 |
| 安装版启动即闪退 | 检查 Windows App Runtime 版本；查看 `%LOCALAPPDATA%\Files` 下的日志 |

### 手动导入证书（脚本不可用时）

管理员命令提示符：

```bat
certutil -addstore -f "TrustedPeople" "Files.Internal_4.3.1.2_x64.cer"
```

图形界面方式：

1. 双击 `.cer` →「安装证书」
2. 存储位置选「本地计算机」→ 下一步
3. 选「将所有的证书都放入下列存储」→ 浏览 →「受信任人」→ 确定
4. 完成 → 是

确认证书是否已进入本机存储（普通权限即可执行）：

```bat
certutil -store TrustedPeople
```

输出中能看到 `CN=Files` 及指纹 `479604f7 d446c7e8 959902e6...` 即成功。

---

## 五、卸载

**安装版**

设置 → 应用 → 已安装的应用 → `Files - Internal` → 卸载。

内置的默认文件管理器接管如果开过，卸载时会只撤销仍属于本产品的修改。

**便携版**

先反注册身份包，再删除目录。注册项会记住目录路径，目录删掉了那条注册还在，会指向一个不存在的位置：

```powershell
Get-AppxPackage -Name Files.Internal.Portable.Identity | Remove-AppxPackage
```

然后直接删除解压出来的整个目录。没有服务、没有开机项。设置和标签数据都在同级的 `Data` 目录里，需要保留就先备份该目录。

证书导入是可选的系统级状态：卸载应用不会自动移除证书。如果这台机器以后不再安装内部版，可以手动删除：

```bat
certutil -delstore TrustedPeople 479604F7D446C7E8959902E6076572634EA1A7A1
```

---

## 附：脚本做了什么

`Trust-InternalCertificate.bat` 的全部动作只有三步：

1. 用 `whoami /groups` 检查当前是否具有管理员完整性级别（查 `S-1-16-12288`）
2. 未提权时通过 `mshta` 调用 `ShellExecute ... runas` 重新拉起自己
3. 执行 `certutil -addstore -f "TrustedPeople" <证书>` 写入本机证书存储

不写注册表、不改系统设置、不联网、不安装任何服务。

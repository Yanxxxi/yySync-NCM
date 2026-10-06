# yySync-NCM

## BetterNCM 插件版 0.2.0

将网易云音乐的歌曲、歌手和播放进度同步至 Steam 好友状态。插件通过 [InfLink-rs](https://github.com/apoint123/inflink-rs) 获取播放信息，在网易云进程内运行 SteamKit2；没有额外应用程序、登录弹窗或托盘。登录与显示设置均集成到 BetterNCM 的 **yySync-NCM** 页面，采用卡片布局。

### 安装

1. 使用 **网易云音乐 3.x x64**，安装 [BetterNCM](https://github.com/std-microblock/chromatic/tree/v2) 和 InfLink-rs，并确认后者正常读取播放信息。
2. 从个人仓库 Actions 的 **Build BetterNCM plugin** 产物下载并解压，取得 `yySyncNCM.plugin`。
3. 将 `.plugin` 文件放入 BetterNCM 数据目录的 `plugins` 文件夹，彻底退出网易云后重新启动。包内已包含所需 .NET 运行库 DLL，无需另装 .NET。
4. 打开 BetterNCM → **yySync-NCM**，输入 Steam 用户名和密码，按页面提示完成手机确认或验证码。首次登录后会保存授权，重启自动登录。

**从 0.1.x 升级：**先关闭网易云，再从托盘退出旧版 yySync；找不到托盘图标时在任务管理器结束 `yySync.exe`。删除旧 `.plugin` 包并放入 0.2.0，最后重启。旧辅助进程仍运行会导致 `plugins_runtime` 拒绝访问，也会争用 Steam 会话。保留 `%LOCALAPPDATA%\yySync\config.json`，新版会沿用已有凭据。

### 登录和设置

- 密码不保存；刷新令牌和设备验证数据保存在 `%LOCALAPPDATA%\yySync\config.json`，不会返回设置页。
- 认证成功后立即保存令牌。连接超时、断网等错误会保留凭据，不再因临时网络问题要求重新手机授权。
- Steam 明确撤销或拒绝令牌后需要重新登录；首次授权及令牌失效时仍可能要求手机验证。
- 支持歌手、播放进度、暂停时显示状态、自定义前缀和长度不足时的保留优先级，显示实时状态预览。
- 关闭 Steam 同步、停止播放或正常退出网易云时清空状态。原生 DLL 更新需要完全退出并重启网易云，仅重载页面不足以更新 DLL。

### 本地构建

安装 .NET 9 x64 SDK，以及 MSVC x64 编译工具或 MinGW x64，在仓库根目录运行：

```powershell
./scripts/build-plugin.ps1
# 产物：build/yySyncNCM.plugin
```

MSVC 在开发者终端运行，可加 `-Compiler cl`；MinGW 可加 `-Compiler 'g++'`。开发调试可把完整 `build/inprocess` 目录放到 `plugins_dev`。自动构建见 [.github/workflows/plugin.yml](.github/workflows/plugin.yml)，架构及与 InfLink-rs 集成的难度评估见 [PLUGIN_DESIGN.md](PLUGIN_DESIGN.md)。

已验证原生接口、CLR 加载与进程退出、跨进程凭据保存、设置页交互及 UTF-8 长度限制。真实 Steam 手机授权、网易云内运行及重启自动登录需要安装后实机验证。

## 原独立程序

仓库保留独立版 `yySync.exe` 的源码，支持网易云音乐、QQ 音乐和洛雪音乐，以及图形设置、托盘、开机启动。其行为与 0.2.0 进程内插件不同，请只运行其中一个版本。

独立版使用 .NET 9；首次运行需登录 Steam。洛雪音乐需在设置中启用开放 API 服务，允许来自局域网的访问。BetterNCM 插件版只同步网易云音乐。

## 致谢

- [InfLink-rs](https://github.com/apoint123/inflink-rs)
- [BetterNCM](https://github.com/std-microblock/chromatic/tree/v2)
- [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm)
- [Music-DiscordRPC](https://github.com/kriYamiHikari/Music-DiscordRPC)
- [NetEase-Cloud-Music-DiscordRPC](https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC)

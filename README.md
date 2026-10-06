# Music Steam RPC

## BetterNCM 插件版

本仓库现提供 `yySyncNCM.plugin`。它通过 [InfLink-rs](https://github.com/apoint123/inflink-rs) 获取网易云音乐的当前歌曲、暂停状态与进度，并由随插件打包的 `yySync.exe` 使用 SteamKit2 同步至 Steam。插件版只同步网易云音乐；原独立程序仍保留 QQ 音乐和洛雪音乐支持。

1. 安装 [BetterNCM](https://github.com/std-microblock/chromatic/tree/v2) 和 InfLink-rs，并确认 InfLink-rs 正常读取播放信息。
2. 从本仓库 Actions 的 **Build BetterNCM plugin** 构建产物下载 `yySyncNCM.plugin`，复制到 BetterNCM 数据目录下的 `plugins` 文件夹。
3. 重启网易云音乐。第一次启动时随插件运行的组件会打开 Steam 登录窗口，之后会保留在系统托盘。可以在托盘的“显示设置”中调整歌手、进度条与前缀。
4. 如果先前已运行独立版 yySync，请先退出它，避免两个实例争用同一个 Steam 会话。插件模式由网易云音乐启动，不需要启用独立版的开机自启。

播放快照位于 BetterNCM 数据目录的 `yySyncNCM-state.json`。停止播放或退出网易云音乐后，Steam 状态会清除；异常退出时最多等待 5 秒清除，辅助进程在快照持续失效 1 分钟后退出。Steam 登录信息仍保存在 `%LOCALAPPDATA%\yySync\config.json`。

开发者可直接将 `plugin` 目录复制到 BetterNCM 的 `plugins_dev`，但需要把 `dotnet publish` 生成的 `yySync.exe` 放到同一目录。完整打包和自包含构建见 `.github/workflows/plugin.yml`。架构取舍与后续工作见 [PLUGIN_DESIGN.md](PLUGIN_DESIGN.md)。

### ✨ 主要功能

- 🎵 **多平台支持**: 网易云音乐、QQ 音乐、洛雪音乐 PC 客户端
- 📡 **Steam 同步**: 通过 SteamKit2 连接 Steam 网络，将播放状态显示为非 Steam 游戏
- 🎨 **高度自定义**:
  - 图形化设置界面，支持实时预览
  - 可选显示歌手名、进度条、自定义前缀文本
  - 支持开机自启、最小化到托盘
  
---

### 📥 安装与使用

1. 确保系统已安装 **[.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0)** 运行库  
2. 下载最新版 `yySync.exe` 运行  
3. 首次运行时需登录 Steam 帐号（支持 Steam Guard / 手机验证）  
4. 登录成功后，打开音乐播放器即可自动同步状态到 Steam
5. 洛雪音乐用户请注意 请在洛雪音乐 - 设置 - 开放API - 启用开放API服务，允许来自局域网的访问

---

### 🙏 致谢

本项目在开发过程中参考了以下优秀开源项目的设计思路与实现方式：

- [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm)
- [Music-DiscordRPC](https://github.com/kriYamiHikari/Music-DiscordRPC)
- [NetEase-Cloud-Music-DiscordRPC](https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC)

感谢原作者们对开源社区的贡献 ❤️

# BetterNCM 改造方案

## 选择

当前实现为独立 BetterNCM 插件，依赖 InfLink-rs 的公开 `window.InfLinkApi` 获取播放状态。插件每秒把最小播放快照写入自己的运行目录；原 .NET 程序新增 `--betterncm <state.json>` 模式，读取快照并沿用 SteamKit2 登录、状态格式化与托盘设置。插件模式不会扫描网易云进程内存，也不会启动原来的多播放器轮询。

SteamKit2 是 .NET 库，而 BetterNCM 的普通 JavaScript 插件无法直接承载它。因此，在不重新实现 Steam 协议的前提下，插件包需要包含一个原生辅助进程。辅助进程由 BetterNCM 的 `app.exec` 启动，保持现有 Steam 登录流程；无需修改 inflink-rs 仓库。

## 两种路径的难度

| 路径 | 工作量 | 依赖和维护 | 结论 |
| --- | --- | --- | --- |
| 独立插件 + InfLink-rs API + 现有 .NET 辅助进程 | 中等 | 依赖 InfLink-rs 公共 API；需要打包 Windows x64 自包含程序 | 已实现，复用现有 SteamKit2 代码和设置 |
| 将 Steam 同步直接集成 inflink-rs | 高 | 要在其 Rust/TypeScript 工作区引入 Steam 协议客户端或桥接 .NET；要协调上游维护、插件商店审核及 GPL-3.0 授权 | 适合上游愿意维护 Steam 功能且计划重写 Steam 连接层时再做 |

若要求**不依赖 InfLink-rs**的独立插件，还须自行适配网易云 2.x/3.x 的播放事件与进度接口；这部分需要多版本实机验证，维护成本高于当前方案。

## 数据与生命周期

`index.js` 读取 `getCurrentSong()`、`getPlaybackStatus()` 和 `getTimeline()`，写入 BetterNCM 数据目录的 `yySyncNCM-state.json`。辅助进程只接受 5 秒内的新快照，把毫秒换算为秒后交给 `SteamStatusManager`。没有歌曲、禁用 Steam 同步或快照过期时清空状态。快照持续失效 1 分钟后辅助进程退出。

插件更新前后的辅助进程使用相同快照路径；Steam 登录互斥锁阻止同时运行两个 yySync 实例。若要立即使用更新后的辅助进程，需要从托盘退出旧实例并重启网易云音乐。插件商店上架前还需提供预览图，并用 Windows 2.x/3.x 网易云客户端、Steam Guard 登录及插件更新流程做实机测试。

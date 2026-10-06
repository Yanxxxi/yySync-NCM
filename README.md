# yySync-NCM

将网易云音乐的歌曲、歌手和播放进度同步到 Steam 好友状态的 **BetterNCM 插件**。

当前版本 **0.2.2**。Steam 连接运行在网易云进程内，登录、验证码和显示设置都位于 BetterNCM 的 yySync-NCM 页面。插件包不包含或启动独立应用程序，也没有托盘窗口。

## 功能与兼容性

- 支持网易云音乐 **2.10.13**；3.x 需要使用 InfLink-rs 已适配的客户端版本。
- 同一个包提供 x86/x64 后端，由 BetterNCM 按客户端位数加载。
- 支持显示歌手、进度条、暂停状态、自定义前缀、长度优先级和实时预览。
- 保存 Steam 刷新令牌与设备验证数据，重启自动登录；临时连接失败保留凭据。
- 通过 [InfLink-rs](https://github.com/apoint123/inflink-rs) 的 `window.InfLinkApi` 获取播放信息。

## 安装与使用

1. 安装 [BetterNCM](https://github.com/std-microblock/chromatic/tree/v2) 和 InfLink-rs，确认 InfLink-rs 能正常获取播放信息。
2. 从本仓库 [Actions](https://github.com/Yanxxxi/yySync-NCM/actions) 中成功的 **Build BetterNCM plugin** 下载构建产物，解压后取得 `yySyncNCM.plugin`。
3. 彻底退出网易云，将插件包放入 BetterNCM 数据目录的 `plugins` 文件夹；更新时删除旧 yySync 插件包，只保留一个版本。
4. 重新启动网易云，打开 **BetterNCM → yySync-NCM**，登录 Steam 并调整显示设置。

插件包已包含所需运行库 DLL，无需另装 .NET。原生 DLL 更新必须完全退出网易云后重启，仅重载页面不能更新 DLL。若曾使用旧独立版，请先退出旧 `yySync.exe`。

### Steam 登录

密码仅用于本次登录，不保存。刷新令牌、设备验证数据和显示配置继续使用 `%LOCALAPPDATA%\yySync\config.json`，兼容旧配置，更新时无需删除它。

首次授权或 Steam 拒绝、撤销令牌后仍可能需要手机确认或验证码。超时、断网等临时错误不会清除令牌。点击“退出登录”会清除已保存的授权。

## 项目结构

```text
plugin/             BetterNCM manifest、设置页面和播放信息读取
backend/managed/    Steam 会话、状态格式化、配置及原生调用入口
backend/native/     BetterNCM 原生接口与进程内 .NET 宿主
scripts/            双架构构建和测试宿主构建
tests/              原生加载、设置交互及登录配置测试
licenses/           随包依赖的许可证和版权声明
.github/workflows/  GitHub Actions 自动构建
```

本仓库已移除独立版启动入口、WinForms 窗体、托盘、自启动、多播放器适配、进程内存读取、内存优化工具、旧辅助程序桥接和独立版工程。保留的 Steam 核心已整理到插件后端，测试宿主仅用于开发验证，不进入插件包。

## 构建与验证

构建环境：Windows、.NET 9 x64 SDK、.NET 9 x86 运行库、MSVC x86/x64 C++ 工具与 Windows SDK。

```powershell
./scripts/build-plugin.ps1 -Compiler cl
# 产物：build/yySyncNCM.plugin
```

脚本自动选择两套 MSVC 工具链。自定义运行库目录可传 `-RuntimeRootX86` / `-RuntimeRootX64`。MinGW 可构建单一架构，例如 `-Compiler 'g++' -Architecture x64`；该产物不包含另一架构。开发调试可把完整 `build/inprocess` 目录放入 `plugins_dev`。

```powershell
./scripts/build-native-hosts.ps1
python tests/native-smoke.py
node tests/ui-flow.cjs

dotnet build tests/session/SessionChecks.csproj -c Release -p:Platform=AnyCPU --output build/session-checks
$env:YYSYNC_CONFIG_DIRECTORY = Join-Path $PWD 'build/session-fixture'
dotnet build/session-checks/SessionChecks.dll write
dotnet build/session-checks/SessionChecks.dll verify
```

测试覆盖 x86/x64 原生接口和导出函数、进程内 CLR 加载与退出、旧配置兼容、凭据保存、UTF-8 长度限制和设置页交互。2.10.13 上的基本功能已由使用者验证；其他客户端版本的兼容性取决于 BetterNCM 与 InfLink-rs。

## 修改来源声明

本仓库由 [wuyan1337/yySync](https://github.com/wuyan1337/yySync) fork 而来，是由 **Yanxxxi** 维护的 BetterNCM 插件改造版本。原项目为独立 Windows 应用，提供基于 SteamKit2 的 Steam 状态同步。

插件版沿用并修改了原项目的 Steam 会话、状态格式化、配置与播放数据模型；新增 BetterNCM JavaScript 设置页、原生接口、进程内 .NET 宿主和 x86/x64 打包。登录流程增加了令牌持久化、临时连接错误保留凭据和重连恢复，并将原独立程序代码移出当前版本。

InfLink-rs 为外部依赖，通过公开 API 提供播放信息；其设置页布局及原生接口组织方式为本次开发提供参考。本仓库没有合并 InfLink-rs 的 Rust/TypeScript 源码。BetterNCM 为插件宿主，相关项目的贡献不属于本仓库原创。

本仓库保留原 [MIT 许可证](LICENSE) 和其中的 Kyle 版权声明。各第三方组件沿用自身许可证，详细来源与随包声明见 [NOTICE.md](NOTICE.md) 和 [licenses](licenses/)。

## 致谢

- [wuyan1337/yySync](https://github.com/wuyan1337/yySync)：本项目的直接代码来源和 Steam 同步基础。
- [BetterNCM / std-microblock](https://github.com/std-microblock/chromatic/tree/v2)：插件平台及原生接口。
- [InfLink-rs / apoint123](https://github.com/apoint123/inflink-rs)：网易云播放 API 和插件实现参考。
- [SteamKit2 / SteamRE](https://github.com/SteamRE/SteamKit)：Steam 网络和授权客户端库。
- [Microsoft .NET](https://github.com/dotnet/runtime)：进程内运行库及宿主接口。
- [protobuf-net](https://github.com/protobuf-net/protobuf-net)、[ZstdSharp](https://github.com/oleg-st/ZstdSharp)：SteamKit2 使用的序列化和压缩依赖。

同时保留原项目对 [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm)、[Music-DiscordRPC / kriYamiHikari](https://github.com/kriYamiHikari/Music-DiscordRPC)、[NetEase-Cloud-Music-DiscordRPC / Kyle (Kxnrl)](https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC) 的致谢，感谢原作者及后续维护者。

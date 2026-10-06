/* global plugin, betterncm, betterncm_native */
(() => {
  const previous = window.__yySyncNcmBridge;
  if (previous) previous.stop();

  const statePath = `${betterncm_native.app.datapath()}\\yySyncNCM-state.json`;
  const executablePath = `${plugin.pluginPath}\\yySync.exe`;
  let interval;
  let started = false;
  let message = "等待 InfLink-rs 初始化";
  const status = document.createElement("p");

  const setMessage = (value) => {
    message = value;
    status.textContent = value;
  };

  const writeSnapshot = () => {
    const api = window.InfLinkApi;
    if (!api) {
      try {
        betterncm_native.fs.writeFileText(statePath, JSON.stringify({
          updatedAt: new Date().toISOString(), song: null
        }));
      } catch (error) {
        setMessage(`写入播放状态失败：${String(error)}`);
        return;
      }
      setMessage("InfLink-rs 未就绪，请确认已安装并启用。 ");
      return;
    }

    try {
      const current = api.getCurrentSong();
      const timeline = api.getTimeline();
      const snapshot = {
        updatedAt: new Date().toISOString(),
        song: current ? {
          id: String(current.trackId || current.ncmId || ""),
          title: current.songName || "",
          artists: current.authorName || "",
          album: current.albumName || "",
          cover: current.cover?.url || ""
        } : null,
        paused: api.getPlaybackStatus() !== "Playing",
        currentTimeMs: timeline?.currentTime || 0,
        durationMs: timeline?.totalTime || current?.duration || 0
      };
      betterncm_native.fs.writeFileText(statePath, JSON.stringify(snapshot));
      setMessage(current ? `正在同步：${current.songName}` : "等待网易云音乐播放歌曲");
    } catch (error) {
      setMessage(`同步失败：${String(error)}`);
    }
  };

  const stop = () => {
    if (interval) clearInterval(interval);
    window.removeEventListener("beforeunload", stop);
    try {
      betterncm_native.fs.writeFileText(statePath, JSON.stringify({
        updatedAt: new Date().toISOString(), song: null
      }));
    } catch (_) { /* The helper clears stale state after five seconds. */ }
  };

  window.__yySyncNcmBridge = { stop };

  plugin.onLoad(() => {
    writeSnapshot();
    interval = setInterval(writeSnapshot, 1000);
    window.addEventListener("beforeunload", stop);

    if (!betterncm_native.fs.exists(executablePath)) {
      setMessage("缺少 yySync.exe，请安装完整的 .plugin 构建包。 ");
      return;
    }
    if (started) return;
    started = true;
    const command = `"${executablePath}" --betterncm "${statePath}"`;
    Promise.resolve(betterncm.app.exec(command, false, false))
      .then((ok) => { if (!ok) setMessage("无法启动 Steam 同步组件，请检查插件文件。 "); })
      .catch((error) => setMessage(`启动同步组件失败：${String(error)}`));
  });

  plugin.onConfig(() => {
    const container = document.createElement("div");
    container.style.padding = "16px";
    container.append(status);

    const note = document.createElement("p");
    note.textContent = "首次使用会打开 Steam 登录窗口。同步组件运行时可在系统托盘中打开设置。";
    container.append(note);

    const link = document.createElement("a");
    link.href = "https://github.com/Yanxxxi/yySync-NCM";
    link.textContent = "源代码与问题反馈";
    link.addEventListener("click", (event) => {
      event.preventDefault();
      betterncm.ncm.openUrl(link.href);
    });
    container.append(link);
    status.textContent = message;
    return container;
  });
})();

const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
class Element {
  constructor(tag) { this.tagName = tag; this.children = []; this.listeners = {}; this.style = {}; this.dataset = {}; this.value = ""; this.classList = { add() {} }; }
  append(...items) { for (const item of items) { item.parentElement = this; this.children.push(item); } }
  setAttribute(name, value) { this[name] = value; }
  addEventListener(name, fn) { this.listeners[name] = fn; }
  trigger(name) { this.listeners[name]?.({ preventDefault() {} }); }
}
let onLoad, onConfig, interval;
const requests = [];
const state = { ok: true, loggedOn: false, busy: false, username: "saved-account", hasToken: true,
  preview: "测试歌曲 - 歌手", settings: { enableSteamSync: true, showArtistName: true,
  showProgressBar: true, showPausedStatus: true, enableCustomPrefix: false,
  customPrefix: "", statusPriority: "Artist" } };
const context = {
  window: { addEventListener() {}, removeEventListener() {}, InfLinkApi: {
    getCurrentSong: () => ({ songName: "测试歌曲", authorName: "歌手", ncmId: 42, duration: 180000 }),
    getTimeline: () => ({ currentTime: 12000, totalTime: 180000 }), getPlaybackStatus: () => "Playing"
  } },
  document: { createElement: tag => new Element(tag) },
  plugin: { onLoad: fn => onLoad = fn, onConfig: fn => onConfig = fn },
  betterncm: { ncm: { openUrl() {} } },
  betterncm_native: { native_plugin: { call: (name, args) => {
    assert.equal(name, "yysync.dispatch");
    const request = JSON.parse(args[0]); requests.push(request);
    if (request.type === "login") { state.busy = true; state.guard = { kind: "deviceCode" }; }
    if (request.type === "guard") { state.busy = false; state.guard = null; state.loggedOn = true; }
    if (request.type === "configure") Object.assign(state.settings, request.settings);
    return JSON.stringify(state);
  }, getRegisteredAPIs: () => ["yysync.dispatch"] } },
  setInterval: fn => { interval = fn; return 1; }, clearInterval() {}
};
vm.runInNewContext(fs.readFileSync("plugin/index.js", "utf8"), context);
onLoad();
const root = onConfig();
const all = node => [node, ...node.children.flatMap(all)];
const nodes = all(root);
const username = nodes.find(n => n["aria-label"] === "Steam 用户名");
const password = nodes.find(n => n["aria-label"] === "Steam 密码");
assert.equal(username.value, "saved-account");
username.value = "another-account"; password.value = " password-with-spaces ";
password.parentElement.trigger("submit");
assert.equal(requests.at(-1).password, " password-with-spaces ");
assert.equal(password.value, "", "Clear password immediately after sending it to native API");
const guard = nodes.find(n => n["aria-label"] === "Steam Guard 验证码");
assert.equal(guard.parentElement.hidden, false, "Device codes must be collected, not mistaken for mobile confirmation");
guard.value = "12345"; guard.parentElement.trigger("submit");
assert.equal(requests.at(-1).type, "guard");
const toggle = nodes.find(n => n["aria-label"] === "显示歌手");
toggle.checked = false; toggle.trigger("change");
assert.equal(requests.at(-1).settings.showArtistName, false);
interval();
assert.equal(requests.at(-1).type, "playback");
assert.equal(requests.at(-1).currentTimeMs, 12000);
context.window.__yySyncNcmPlugin.stop();
assert.equal(requests.at(-1).type, "shutdown");
assert.doesNotMatch(fs.readFileSync("plugin/index.js", "utf8"), /app\.exec|writeFileText|yySync\.exe/);
// A failed native DLL load must show a useful diagnostic and prevent login calls.
context.betterncm_native.native_plugin.getRegisteredAPIs = () => ["inflink.dispatch"];
const beforeMissingApi = requests.length;
vm.runInNewContext(fs.readFileSync("plugin/index.js", "utf8"), context);
onLoad();
const unavailable = all(onConfig());
assert.equal(requests.length, beforeMissingApi);
assert.equal(unavailable.find(n => n.textContent === "登录").disabled, true);
assert.equal(unavailable.find(n => n["aria-label"] === "显示歌手").disabled, true);
assert.match(unavailable.find(n => n.className === "ys-error").textContent, /原生接口未注册/);
context.betterncm_native.native_plugin.getRegisteredAPIs = () => ["yysync.dispatch"];
unavailable.find(n => n.textContent === "重试加载组件").trigger("click");
assert.equal(unavailable.find(n => n.textContent === "登录").disabled, false);
context.window.__yySyncNcmPlugin.stop();
console.log("Settings UI, native-only transport, password handling and guard-code flow passed");

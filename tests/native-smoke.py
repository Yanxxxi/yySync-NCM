"""Load the packaged plugin in a native host, without using a Steam account.
The host exercises BetterNCM's actual ABI; Python only coordinates assertions.
"""
import subprocess
import json
import os
from pathlib import Path
import tempfile

root = Path(__file__).resolve().parents[1]
package = root / "build" / "inprocess"
assert not list(package.rglob("*.exe")), "Plugin must contain no application launcher"

with tempfile.TemporaryDirectory(prefix="yysync-test-", dir=root / "build") as directory:
    os.environ["YYSYNC_CONFIG_DIRECTORY"] = directory
    config = Path(directory) / "config.json"
    config.write_text(json.dumps({
        "SteamUsername": "test-account", "SteamRefreshToken": "fake-persisted-token",
        "SteamGuardData": "fake-guard-data", "EnableSteamSync": True
    }), encoding="utf-8")
    host = subprocess.Popen([str(root / "build" / "native-host.exe"), str(package / "backend.dll")],
                            stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True,
                            encoding="utf-8", creationflags=subprocess.CREATE_NO_WINDOW)
    def call(request):
        host.stdin.write(json.dumps(request) + "\n")
        host.stdin.flush()
        raw = host.stdout.readline()
        assert raw, f"Native host failed with exit {host.poll()}"
        assert "fake-persisted-token" not in raw and "fake-guard-data" not in raw
        return json.loads(raw)
    initial = call({"type": "initialize", "connect": False})
    assert initial["ok"], initial
    assert initial["hasToken"] and initial["username"] == "test-account"
    configured = call({"type": "configure", "settings": {
        "showArtistName": False, "showProgressBar": True, "enableCustomPrefix": True,
        "customPrefix": "🎵" * 30, "statusPriority": "ProgressBar",
        "steamRefreshToken": "must-not-overwrite"
    }})
    assert configured["settings"]["showArtistName"] is False
    playback = call({"type": "playback", "song": {"id": "42", "title": "测试歌曲", "artists": "歌手"},
                     "currentTimeMs": 61000, "durationMs": 180000, "paused": False})
    assert len(playback["preview"].encode("utf-8")) <= 63
    assert "�" not in playback["preview"]
    saved = json.loads(config.read_text(encoding="utf-8-sig"))
    assert saved["SteamRefreshToken"] == "fake-persisted-token"
    assert saved["SteamGuardData"] == "fake-guard-data"
    assert saved["ShowArtistName"] is False
    assert not (Path(directory) / "config.json.tmp").exists()
    assert call({"type": "unknown"})["ok"] is False
    assert call({"type": "shutdown"})["ok"]
    assert call({"type": "initialize", "connect": False})["hasToken"]
    assert call({"type": "logout"})["hasToken"] is False
    saved = json.loads(config.read_text(encoding="utf-8-sig"))
    assert not saved["SteamRefreshToken"] and not saved["SteamGuardData"]
    assert call({"type": "shutdown"})["ok"]
    host.stdin.close()
    assert host.wait(timeout=15) == 0, "Native host must exit cleanly"
print("Native ABI, in-process CLR loading, credential persistence and UTF-8 limits passed")

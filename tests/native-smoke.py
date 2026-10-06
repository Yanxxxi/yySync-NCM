"""Test each packaged architecture in a native child process; never host CLR in Python."""
import argparse
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
package = root / "build" / "inprocess"
parser = argparse.ArgumentParser()
parser.add_argument("--architecture", choices=["both", "x86", "x64"], default="both")
architectures = ["x86", "x64"] if (arch := parser.parse_args().architecture) == "both" else [arch]
assert not list(package.rglob("*.exe")), "Plugin must contain no application launcher"

def machine(path):
    data = path.read_bytes()
    assert data[:2] == b"MZ", path
    offset = struct.unpack_from("<I", data, 0x3C)[0]
    assert data[offset:offset + 4] == b"PE\0\0", path
    return struct.unpack_from("<H", data, offset + 4)[0]

for arch in architectures:
    expected = 0x14C if arch == "x86" else 0x8664
    backend = package / ("backend.dll" if arch == "x86" else "backend.dll.x64.dll")
    executable = root / "build" / f"native-host-{arch}.exe"
    assert machine(backend) == machine(executable) == expected, "Host/DLL architecture mismatch"
    payload = package / arch
    assert machine(payload / "nethost.dll") == expected
    for dependency in [*payload.glob("runtime/host/fxr/*/hostfxr.dll"),
                       *payload.glob("runtime/shared/Microsoft.NETCore.App/*/coreclr.dll")]:
        assert machine(dependency) == expected, f"Wrong runtime architecture: {dependency}"
    assert list(payload.glob("runtime/host/fxr/*/hostfxr.dll"))
    assert list(payload.glob("runtime/shared/Microsoft.NETCore.App/*/coreclr.dll"))
    with tempfile.TemporaryDirectory(prefix=f"yysync-{arch}-", dir=root / "build") as directory:
        environment = os.environ.copy()
        environment["YYSYNC_CONFIG_DIRECTORY"] = directory
        config = Path(directory) / "config.json"
        config.write_text(json.dumps({
            "SteamUsername": "test-account", "SteamRefreshToken": "fake-persisted-token",
            "SteamGuardData": "fake-guard-data", "EnableSteamSync": True
        }), encoding="utf-8")
        host = subprocess.Popen([str(executable), str(backend)], env=environment,
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True,
                                encoding="utf-8", creationflags=subprocess.CREATE_NO_WINDOW)
        def call(request):
            host.stdin.write(json.dumps(request) + "\n")
            host.stdin.flush()
            raw = host.stdout.readline()
            assert raw, f"Native {arch} host failed with exit {host.poll()}"
            assert "fake-persisted-token" not in raw and "fake-guard-data" not in raw
            return json.loads(raw)
        try:
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
        finally:
            if host.poll() is None:
                host.kill()
                host.wait(timeout=15)
        print(f"{arch}: Native ABI, exports, CLR loading, credentials and UTF-8 limits passed")

"""Verify the shipped archive and the files retained by the store's ignore rules."""
import json
import re
from pathlib import Path
from zipfile import ZipFile

package = Path(__file__).resolve().parents[1] / "build/yySyncNCM.plugin"
with ZipFile(package) as archive:
    files = {item.filename.replace("\\", "/") for item in archive.infolist() if not item.is_dir()}
    manifest = json.loads(archive.read("manifest.json"))
    required = {
        "manifest.json", "index.js", manifest["preview"], "backend.dll", "backend.dll.x64.dll",
        "LICENSE", "NOTICE.md", ".betterncm-ignore",
    }
    dependencies = {
        "yySync.Managed.dll", "yySync.Managed.deps.json", "yySync.Managed.runtimeconfig.json",
        "SteamKit2.dll", "protobuf-net.dll", "protobuf-net.Core.dll", "ZstdSharp.dll", "System.IO.Hashing.dll",
    }
    for arch in ("x86", "x64"):
        required.add(f"{arch}/nethost.dll")
        required.update(f"{arch}/managed/{name}" for name in dependencies)
        required.update(f"{arch}/runtime/{name}" for name in ("LICENSE.txt", "ThirdPartyNotices.txt"))
        for name in ("coreclr.dll", "clrjit.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "Microsoft.NETCore.App.deps.json"):
            matches = {f for f in files if re.fullmatch(rf"{arch}/runtime/shared/Microsoft\.NETCore\.App/9\.[^/]+/{re.escape(name)}", f)}
            assert len(matches) == 1, f"Missing or ambiguous framework file: {arch}/{name}"
            required.update(matches)
        fxr = {f for f in files if re.fullmatch(rf"{arch}/runtime/host/fxr/9\.[^/]+/hostfxr\.dll", f)}
        assert len(fxr) == 1, f"Missing or ambiguous hostfxr: {arch}"
        required.update(fxr)
    assert required <= files, f"Required package files missing: {required - files}"
    licenses = {f for f in files if f.startswith("licenses/")}
    assert len(licenses) == 9 and all(f.endswith(".txt") for f in licenses), "Dependency license files missing"
    allowed = required | licenses
    for name in files - allowed:
        assert re.fullmatch(r"(x86|x64)/runtime/shared/Microsoft\.NETCore\.App/9\.[^/]+/[^/]+\.dll", name), f"Unnecessary package file: {name}"

    # Match the current BetterNCM-Plugins updater's rule conversion, so ignore
    # patterns cannot silently remove a DLL, its dependency map or a license.
    patterns = []
    for rule in archive.read(".betterncm-ignore").decode("utf-8").splitlines() + [".betterncm-ignore"]:
        rule = rule.strip().replace("\\", "/")
        if not rule:
            continue
        expression = rule.replace("*", "(.*)").replace("?", "(.*)").replace("/", r"\/")
        if rule.startswith("/"):
            expression = "^" + expression
        if not rule.endswith("/"):
            expression += r"\/?"
        patterns.append(re.compile(expression))
    retained = {name for name in files if not any(p.search("/" + name) for p in patterns)}
    assert files - retained == {".betterncm-ignore"}, f"Store ignores required files: {files - retained}"
    for unnecessary in ("/x86/managed/yySync.Managed.pdb", "/x64/runtime/shared/Microsoft.NETCore.App/9.0.0/.version", "/README.md", "/BUILD-INFO.json", "/tests/native-host.cpp"):
        assert any(p.search(unnecessary) for p in patterns), f"Ignore rules do not filter {unnecessary}"
print(f"Store package layout passed: {len(retained)} files; runtime dependencies intact; no debug, SDK, source or build files")

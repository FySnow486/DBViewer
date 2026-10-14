"""Package self-contained publish outputs without local data or debug symbols."""
import hashlib
import pathlib
import stat
import sys
import zipfile

root = pathlib.Path(__file__).resolve().parent.parent
version = sys.argv[1]
release_dir = root / "artifacts" / "release"
outputs = []
for rid in ("win-x64", "linux-x64", "osx-arm64", "osx-x64"):
    source = release_dir / rid
    executable = source / ("DBViewer.exe" if rid == "win-x64" else "DBViewer")
    if not executable.is_file() or not (source / "wwwroot" / "index.html").is_file():
        raise SystemExit(f"Incomplete publish output: {rid}")
    target = release_dir / f"DBViewer-{version}-{rid}.zip"
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(source.rglob("*")):
            if not path.is_file() or path.suffix == ".pdb":
                continue
            relative = path.relative_to(source)
            if "Data" in relative.parts or "keys" in relative.parts:
                raise SystemExit(f"Unexpected local data in publish output: {path}")
            entry = zipfile.ZipInfo(str(pathlib.Path("DBViewer") / relative))
            entry.create_system = 3
            mode = 0o755 if path == executable and rid != "win-x64" else 0o644
            entry.external_attr = (stat.S_IFREG | mode) << 16
            entry.compress_type = zipfile.ZIP_DEFLATED
            with path.open("rb") as content, archive.open(entry, "w") as output:
                while chunk := content.read(1024 * 1024):
                    output.write(chunk)
    with zipfile.ZipFile(target) as archive:
        if archive.testzip() is not None:
            raise SystemExit(f"Corrupt release archive: {target}")
    outputs.append(target)

checksum_file = release_dir / "SHA256SUMS.txt"
checksum_file.write_text(
    "".join(f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.name}\n" for path in outputs),
    encoding="utf-8",
)
for path in [*outputs, checksum_file]:
    print(f"{path.relative_to(root)} ({path.stat().st_size:,} bytes)")

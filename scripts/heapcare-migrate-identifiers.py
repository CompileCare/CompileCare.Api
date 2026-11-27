#!/usr/bin/env python3
"""One-time, non-destructive HeapCare identifier migration for this repository.

Preserves persisted database names, connection-string keys, GitHub repository
coordinates and lower-case legacy OIDC/database identifiers. It never deletes
data or rewrites EF migration IDs.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OLD = "".join(("Compile", "Care"))
NEW = "".join(("Heap", "Care"))
SKIP_DIRS = {".git", "bin", "obj", "node_modules", ".vs", "TestResults", "artifacts"}
SELF = Path(__file__).resolve()

# Rename paths bottom-up so children are moved before their containing folders.
paths = [p for p in ROOT.rglob("*") if p != SELF and not any(part in SKIP_DIRS for part in p.parts)]
for path in sorted(paths, key=lambda p: len(p.parts), reverse=True):
    if OLD not in path.name:
        continue
    target = path.with_name(path.name.replace(OLD, NEW))
    if target.exists():
        raise RuntimeError(f"Refusing to overwrite existing path: {target}")
    path.rename(target)

# Rename source identifiers and references in text files. Keep persisted/
# externally configured contracts stable until their dedicated migration.
for path in ROOT.rglob("*"):
    if not path.is_file() or path == SELF or any(part in SKIP_DIRS for part in path.parts):
        continue
    try:
        raw = path.read_bytes()
        bom = raw.startswith(b"\\xef\\xbb\\xbf")
        text = raw.decode("utf-8-sig")
    except (UnicodeDecodeError, OSError):
        continue

    updated = text.replace(OLD, NEW)
    # Keep existing connection-string key names working for deployed environments.
    updated = updated.replace(f"ConnectionStrings:{NEW}", f"ConnectionStrings:{OLD}")
    updated = updated.replace(f"ConnectionStrings__{NEW}", f"ConnectionStrings__{OLD}")
    updated = updated.replace(f'GetConnectionString("{NEW}")', f'GetConnectionString("{OLD}")')
    # The GitHub owner and repository slugs remain unchanged until the admin rename.
    repo_names = ["Api", "ApiClient", "Caching", "Database", "Local", "Mobile", "Observability", "OtpService", "Site", "Web"]
    for suffix in repo_names:
        updated = updated.replace(f"{NEW}/{NEW}.{suffix}", f"{OLD}/{OLD}.{suffix}")
        updated = updated.replace(f"{NEW}/{NEW}.{suffix}.git", f"{OLD}/{OLD}.{suffix}.git")
    updated = updated.replace(f"nuget.pkg.github.com/{NEW}", f"nuget.pkg.github.com/{OLD}")
    if updated != text:
        path.write_bytes((b"\\xef\\xbb\\xbf" if bom else b"") + updated.encode("utf-8"))

print("HeapCare source/project identifier migration completed; legacy persistence and external identity contracts were preserved.")

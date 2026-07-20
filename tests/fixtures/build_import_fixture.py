"""Build a realistic, messy import-test folder from a real .var repo.

Usage: python build_import_fixture.py <SRC_REPO> <DEST_FOLDER>
No hardcoded paths — reusable for the import-feature E2E fixture.
Copies real vars (never moves — source repo stays intact) and fabricates every
classification lane (New / Exact-dup / Conflict / Name!=meta / Corrupt / CJK)
plus archives (valid zip/7z, nested, password-protected, broken) and junk files,
in a multi-level tree.
"""
import os, sys, json, zipfile, shutil, subprocess, random

SRC, DEST = sys.argv[1], sys.argv[2]
SEVENZIP = r"C:\Program Files\7-Zip\7z.exe"
random.seed(42)

def find_vars(root):
    out = {}
    for dirpath, _, files in os.walk(root):
        for f in files:
            if f.lower().endswith(".var"):
                out.setdefault(f, os.path.join(dirpath, f))
    return out

VARS = find_vars(SRC)
print(f"source vars found: {len(VARS)}")

def pick(name):
    if name in VARS: return VARS[name]
    # fallback: any var
    return next(iter(VARS.values()))

def ensure(d):
    os.makedirs(d, exist_ok=True); return d

def meta_bytes(creator, package):
    return json.dumps({"licenseType":"FC","creatorName":creator,"packageName":package,
                       "standardReferenceVersionOption":"Latest","dependencies":{}}, ensure_ascii=False).encode("utf-8")

def copy_rewrite_meta(src, dst, creator, package):
    """Copy a var but replace meta.json -> a chosen identity (rebuilds the zip)."""
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
        wrote_meta = False
        for it in zin.infolist():
            if it.filename.lower() == "meta.json":
                zout.writestr("meta.json", meta_bytes(creator, package)); wrote_meta = True
            elif not it.is_dir():
                zout.writestr(it.filename, zin.read(it.filename))
        if not wrote_meta:
            zout.writestr("meta.json", meta_bytes(creator, package))

def copy_add_entry(src, dst):
    """Exact copy + one extra entry -> same identity, different ContentSignature (Conflict)."""
    shutil.copy(src, dst)
    with zipfile.ZipFile(dst, "a", zipfile.ZIP_DEFLATED) as z:
        z.writestr("Custom/Scripts/_importtest_conflict_marker.txt",
                   b"content changed so ContentSignature differs from the repo copy")

def find_gbk_var():
    """Find a real var with a legacy-encoded (GBK/ShiftJIS) entry name: no UTF-8 flag + non-ascii bytes."""
    for name, path in VARS.items():
        try:
            with zipfile.ZipFile(path) as z:
                for zi in z.infolist():
                    if not (zi.flag_bits & 0x800):
                        raw = zi.filename.encode("cp437", "replace")
                        if any(b > 127 for b in raw):
                            try: raw.decode("utf-8")
                            except UnicodeDecodeError: return path  # non-utf8 legacy name
        except Exception: pass
    return None

def make_cjk_var(dst):
    """Fallback: fabricate a valid var whose one entry name is raw GBK bytes without the UTF-8 flag."""
    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("meta.json", meta_bytes("GbkCreator", "OldPack"))
        raw = "衣装/裙子_A.vam".encode("gbk")            # legacy Chinese bytes
        zi = zipfile.ZipInfo(raw.decode("latin1"))       # 1 byte -> 1 codepoint
        zi.flag_bits &= ~0x800                            # clear UTF-8 flag
        z.writestr(zi, b"placeholder vam content")
    # if zipfile re-set the utf8 flag (it does for non-ascii), patch the flag byte in the file
    _patch_clear_utf8_flags(dst)

def _patch_clear_utf8_flags(path):
    """Best-effort: clear bit 11 (UTF-8) in every local + central header general-purpose flag."""
    data = bytearray(open(path, "rb").read())
    for sig in (b"PK\x03\x04", b"PK\x01\x02"):
        i = 0
        while True:
            i = data.find(sig, i)
            if i < 0: break
            flag_off = i + (6 if sig == b"PK\x03\x04" else 8)
            data[flag_off] &= ~0x08  # bit 11 is the high bit of the low flag byte (0x0800 -> byte+1 0x08)
            data[flag_off + 1] &= ~0x08
            i += 4
    open(path, "wb").write(data)

# ── build the messy tree ─────────────────────────────────────────────────────
if os.path.exists(DEST):
    for e in os.listdir(DEST):
        p = os.path.join(DEST, e)
        shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
ensure(DEST)
created = []

# junk + loose root
open(os.path.join(DEST, "readme note.txt"), "w").write("dropped a bunch of downloads here, sort later")
shutil.copy(pick("Fallen.Masako.7.var"), os.path.join(DEST, "loose_at_root.var")); created.append("loose_at_root.var (exact-dup)")

# Downloads/2024-11/new  -> New (novel identity, meta matches filename)
d = ensure(os.path.join(DEST, "Downloads", "2024-11", "new"))
copy_rewrite_meta(pick("caelryn.Dae_free.2.var"), os.path.join(d, "ImportTest.FreshLook.1.var"), "ImportTest", "FreshLook"); created.append("Downloads/2024-11/new/ImportTest.FreshLook.1.var (NEW)")
copy_rewrite_meta(pick("Damarmau.Muscle_normals_v3.1.var"), os.path.join(d, "ImportTest.NightScene.2.var"), "ImportTest", "NightScene"); created.append("Downloads/2024-11/new/ImportTest.NightScene.2.var (NEW)")
open(os.path.join(d, "Thumbs.db"), "wb").write(b"\x00\x01junk")

# Downloads/2024-11/dupes -> Exact dup (unchanged repo vars)
d = ensure(os.path.join(DEST, "Downloads", "2024-11", "dupes"))
for n in ("BaGe.VAM_BaGe_Shoe41.1.var", "BooMoon.Piercings.2.var"):
    shutil.copy(pick(n), os.path.join(d, n)); created.append(f"Downloads/2024-11/dupes/{n} (EXACT-DUP)")

# incoming_mess/conflicts -> Conflict (same identity, content changed)
d = ensure(os.path.join(DEST, "incoming_mess", "conflicts"))
copy_add_entry(pick("Barbarossa.Tatsumaki.2.var"), os.path.join(d, "Barbarossa.Tatsumaki.2.var")); created.append("incoming_mess/conflicts/Barbarossa.Tatsumaki.2.var (CONFLICT)")

# incoming_mess/weird names -> Name!=meta  +  BadName
d = ensure(os.path.join(DEST, "incoming_mess", "weird names"))
copy_rewrite_meta(pick("Eros.fengluan.1.var"), os.path.join(d, "SomeGuy.RenamedByMistake.1.var"), "AcidBubbles", "Timeline"); created.append("incoming_mess/weird names/SomeGuy.RenamedByMistake.1.var (NAME!=META)")
shutil.copy(pick("bqbq.bqbq_morph_Add.1.var"), os.path.join(d, "download (3).var")); created.append("incoming_mess/weird names/download (3).var (BAD-NAME)")

# incoming_mess/broken -> Corrupt (truncated / no-meta / not-a-zip / empty)
d = ensure(os.path.join(DEST, "incoming_mess", "broken"))
with open(pick("Archer.jingjue2333.1.var"), "rb") as f: head = f.read(3072)
open(os.path.join(d, "truncated.var"), "wb").write(head); created.append("incoming_mess/broken/truncated.var (CORRUPT-ZIP)")
with zipfile.ZipFile(os.path.join(d, "no_meta.var"), "w") as z:
    z.writestr("Custom/Clothing/x.vam", b"has content but no meta.json"); z.writestr("Custom/Clothing/x.vaj", b"{}")
created.append("incoming_mess/broken/no_meta.var (MISSING-META)")
open(os.path.join(d, "not_a_zip.var"), "w").write("this is plain text, not a zip archive at all\n" * 20); created.append("incoming_mess/broken/not_a_zip.var (CORRUPT-ZIP)")
open(os.path.join(d, "empty.var"), "wb").write(b""); created.append("incoming_mess/broken/empty.var (CORRUPT-ZIP)")

# archives -> extract & validate; incl. nested, 7z, password, broken
a = ensure(os.path.join(DEST, "archives"))
tmp = ensure(os.path.join(DEST, "_stage"))
for n in ("CuteSvetlana.FemaleCloth-NooneOdalisqueDress.1.var", "FO.FO23.1.var"):
    src = pick(n)
    if os.path.exists(src): shutil.copy(src, os.path.join(tmp, os.path.basename(src)))
# valid zip
with zipfile.ZipFile(os.path.join(a, "looks_pack.zip"), "w", zipfile.ZIP_DEFLATED) as z:
    for f in os.listdir(tmp): z.write(os.path.join(tmp, f), f)
created.append("archives/looks_pack.zip (ZIP -> 2 vars)")
# nested zip (vars inside a subfolder)
nd = ensure(os.path.join(a, "nested"))
with zipfile.ZipFile(os.path.join(nd, "deep_pack.zip"), "w", zipfile.ZIP_DEFLATED) as z:
    for f in os.listdir(tmp): z.write(os.path.join(tmp, f), f"creator_dump/looks/{f}")
created.append("archives/nested/deep_pack.zip (ZIP nested -> 2 vars)")
# valid 7z
files = [os.path.join(tmp, f) for f in os.listdir(tmp)]
subprocess.run([SEVENZIP, "a", "-t7z", os.path.join(a, "mixed_pack.7z"), *files], capture_output=True)
created.append("archives/mixed_pack.7z (7Z -> 2 vars)")
# password-protected zip (one var) -> failed source
one = pick("FO.FO25.1.var")
if os.path.exists(one):
    subprocess.run([SEVENZIP, "a", "-tzip", "-pSECRET123", "-mem=AES256", os.path.join(a, "secret_premium.zip"), one], capture_output=True)
    created.append("archives/secret_premium.zip (PASSWORD zip -> failed source)")
# broken 7z (truncate a valid one)
subprocess.run([SEVENZIP, "a", "-t7z", os.path.join(a, "_full.7z"), *files], capture_output=True)
full = os.path.join(a, "_full.7z")
if os.path.exists(full):
    b = open(full, "rb").read(); open(os.path.join(a, "broken_archive.7z"), "wb").write(b[: max(64, len(b) // 3)]); os.remove(full)
    created.append("archives/broken_archive.7z (BROKEN archive -> failed source)")
shutil.rmtree(tmp, ignore_errors=True)

# cjk_encoding -> CJK lane (real GBK var if found, else fabricate)
d = ensure(os.path.join(DEST, "cjk_encoding"))
gbk = find_gbk_var()
if gbk:
    shutil.copy(gbk, os.path.join(d, os.path.basename(gbk))); created.append(f"cjk_encoding/{os.path.basename(gbk)} (CJK — real GBK var)")
else:
    make_cjk_var(os.path.join(d, "GbkCreator.OldPack.1.var")); created.append("cjk_encoding/GbkCreator.OldPack.1.var (CJK — fabricated GBK entry)")

print("\n=== created ===")
for c in created: print(" •", c)
print(f"\nDEST = {DEST}")

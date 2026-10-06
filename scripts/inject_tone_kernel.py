# Inject lab compute kernels onto SEGIATrousFilterBeefEdit from pristine Workshop copy.

from __future__ import annotations

import copy
import sys
from pathlib import Path

import UnityPy

ROOT = Path(__file__).resolve().parents[1]
PRISTINE = ROOT / "scripts" / "segibeefedit.asset.pristine"
OUT = ROOT / "Content" / "segibeefedit.asset"
HERE = Path(__file__).resolve().parent

KERNELS = [
    ("LabGIToneMap", HERE / "LabGITone.dxbc"),
    ("LabGIBlur", HERE / "LabGIBlur.dxbc"),
    ("LabScreenMix", HERE / "LabScreenMix.dxbc"),
    ("LabScreenGamma", HERE / "LabScreenGamma.dxbc"),
    ("LabChannelGain", HERE / "LabChannelGain.dxbc"),
    # Reuses the existing _SigmaDepth cbuffer slot (dither strength) and the _NormalTexture
    # bind slot (blue noise tile) - no NEW_CB_PARAMS entry needed, unlike LabChannelGain.
    ("LabDither", HERE / "LabDither.dxbc"),
]

# $Globals is a single name->offset table shared by every kernel in this asset (verified via
# typetree inspection - it lives once per variant, not per kernel). Cloning a kernel's code does
# NOT register any new names in it, so ComputeShader.SetFloat/SetVector on a name that isn't
# already here is a silent no-op; the shader still reads whatever real, recognized-name value
# currently occupies that byte offset. New named parameters (that aren't just reusing one of the
# six names ATrousFilter already has) must be appended here, with fresh offsets past the existing
# 48-byte buffer, or they will never actually reach the shader.
NEW_CB_PARAMS = [
    {"name": "_ChanGainR", "type": 0, "offset": 48, "arraySize": 0, "rowCount": 1, "colCount": 1},
    {"name": "_ChanGainG", "type": 0, "offset": 52, "arraySize": 0, "rowCount": 1, "colCount": 1},
    {"name": "_ChanGainB", "type": 0, "offset": 56, "arraySize": 0, "rowCount": 1, "colCount": 1},
]
NEW_CB_BYTESIZE = 64


def load_sm5(path: Path) -> bytes:
    if not path.is_file():
        raise FileNotFoundError(path)
    dxbc = path.read_bytes()
    if dxbc[:4] != b"DXBC":
        raise RuntimeError(f"{path.name} is not DXBC")
    if b"DXIL" in dxbc and b"SHEX" not in dxbc and b"SHEXP" not in dxbc:
        raise RuntimeError(f"{path.name} looks like DXIL; recompile with compile_labtone_sm5.py")
    if b"SHEX" not in dxbc and b"SHEXP" not in dxbc:
        raise RuntimeError(f"{path.name} missing SHEX/SHEXP")
    return dxbc


def main() -> int:
    if not PRISTINE.is_file():
        print("missing pristine:", PRISTINE, file=sys.stderr)
        return 1

    try:
        payloads = [(name, load_sm5(path)) for name, path in KERNELS]
    except Exception as ex:
        print(ex, file=sys.stderr)
        return 1

    env = UnityPy.load(str(PRISTINE))
    patched = False
    drop = {name for name, _ in payloads}
    for obj in env.objects:
        if obj.type.name != "ComputeShader":
            continue
        tree = obj.read_typetree()
        if tree.get("m_Name") != "SEGIATrousFilterBeefEdit":
            continue

        dxbc_variants = 0
        for var in tree.get("variants") or []:
            kernels = var.get("kernels") or []
            kernels[:] = [k for k in kernels if k.get("name") not in drop]
            if not kernels:
                raise RuntimeError("ATrous has no kernels")
            base = kernels[0]
            if base.get("name") != "ATrousFilter":
                raise RuntimeError("expected first kernel ATrousFilter, got " + str(base.get("name")))

            for name, dxbc in payloads:
                tone = copy.deepcopy(base)
                tone["name"] = name
                for uv in tone.get("uniqueVariants") or []:
                    code = uv.get("code")
                    code_b = bytes(code) if not isinstance(code, (bytes, bytearray)) else bytes(code)
                    if isinstance(code, list):
                        code_b = bytes(code)
                    if code_b[:4] == b"DXBC":
                        uv["code"] = list(dxbc)
                        uv["threadGroupSize"] = [8, 8, 1]
                        dxbc_variants += 1
                kernels.append(tone)
            var["kernels"] = kernels

            cbs = var.get("constantBuffers") or []
            if not cbs:
                raise RuntimeError("variant has no constantBuffers")
            cb = cbs[0]  # name varies by render target ($Globals, CGlobals, ...)
            existing_names = {p.get("name") for p in cb.get("params") or []}
            for p in NEW_CB_PARAMS:
                if p["name"] not in existing_names:
                    cb["params"].append(dict(p))
            if cb.get("byteSize", 0) < NEW_CB_BYTESIZE:
                cb["byteSize"] = NEW_CB_BYTESIZE

        obj.save_typetree(tree)
        patched = True
        print("patched SEGIATrousFilterBeefEdit; DXBC kernel variants touched:", dxbc_variants)

    if not patched:
        print("SEGIATrousFilterBeefEdit not found", file=sys.stderr)
        return 1

    raw = env.file.save(packer="lz4")
    OUT.write_bytes(raw)
    print("wrote", OUT, "bytes", len(raw))

    need = {"ATrousFilter"} | {n for n, _ in payloads}
    env2 = UnityPy.load(str(OUT))
    for obj in env2.objects:
        if obj.type.name != "ComputeShader":
            continue
        d = obj.read()
        if d.m_Name != "SEGIATrousFilterBeefEdit":
            continue
        names = [[kp.name for kp in (var.kernels or [])] for var in d.variants]
        print("verify kernel names per variant:", names)
        if not all(need.issubset(set(n)) for n in names):
            print("verify failed: missing kernels", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

# Compile lab compute HLSL to classic SM5 DXBC via d3dcompiler_47.

from __future__ import annotations

import ctypes
import sys
from ctypes import POINTER, byref, c_char_p, c_size_t, c_uint, c_void_p
from pathlib import Path

HERE = Path(__file__).resolve().parent
D3DCOMPILE_OPTIMIZATION_LEVEL3 = (1 << 15)

JOBS = [
    ("LabGITone.hlsl", b"LabGIToneMap", "LabGITone.dxbc"),
    ("LabGIBlur.hlsl", b"LabGIBlur", "LabGIBlur.dxbc"),
    ("LabScreenMix.hlsl", b"LabScreenMix", "LabScreenMix.dxbc"),
    ("LabScreenGamma.hlsl", b"LabScreenGamma", "LabScreenGamma.dxbc"),
    ("LabChannelGain.hlsl", b"LabChannelGain", "LabChannelGain.dxbc"),
    ("LabDither.hlsl", b"LabDither", "LabDither.dxbc"),
]


class ID3DBlobVtbl(ctypes.Structure):
    _fields_ = [
        ("QueryInterface", c_void_p),
        ("AddRef", c_void_p),
        ("Release", ctypes.WINFUNCTYPE(c_uint, c_void_p)),
        ("GetBufferPointer", ctypes.WINFUNCTYPE(c_void_p, c_void_p)),
        ("GetBufferSize", ctypes.WINFUNCTYPE(c_size_t, c_void_p)),
    ]


class ID3DBlob(ctypes.Structure):
    _fields_ = [("lpVtbl", POINTER(ID3DBlobVtbl))]


def compile_hlsl(hlsl_path: Path, entry: bytes, out_path: Path) -> int:
    if not hlsl_path.is_file():
        print("missing", hlsl_path, file=sys.stderr)
        return 1

    src = hlsl_path.read_bytes()
    dll = ctypes.WinDLL("d3dcompiler_47.dll")
    D3DCompile = dll.D3DCompile
    D3DCompile.argtypes = [
        c_void_p, c_size_t, c_char_p, c_void_p, c_void_p,
        c_char_p, c_char_p, c_uint, c_uint,
        POINTER(POINTER(ID3DBlob)), POINTER(POINTER(ID3DBlob)),
    ]
    D3DCompile.restype = ctypes.HRESULT

    code = POINTER(ID3DBlob)()
    errors = POINTER(ID3DBlob)()
    hr = D3DCompile(
        src, len(src), str(hlsl_path).encode("utf-8"), None, None,
        entry, b"cs_5_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0,
        byref(code), byref(errors),
    )

    if errors:
        err_vt = errors.contents.lpVtbl.contents
        ptr = err_vt.GetBufferPointer(errors)
        size = err_vt.GetBufferSize(errors)
        print(ctypes.string_at(ptr, size).decode("utf-8", errors="replace"), file=sys.stderr)
        err_vt.Release(errors)

    if hr != 0 or not code:
        print(f"D3DCompile failed for {hlsl_path.name} HRESULT=0x{hr & 0xFFFFFFFF:08X}", file=sys.stderr)
        return 1

    vt = code.contents.lpVtbl.contents
    blob = ctypes.string_at(vt.GetBufferPointer(code), vt.GetBufferSize(code))
    vt.Release(code)

    if blob[:4] != b"DXBC" or (b"DXIL" in blob and b"SHEX" not in blob and b"SHEXP" not in blob):
        print(f"{out_path.name}: not SM5 SHEX DXBC", file=sys.stderr)
        return 1
    if b"SHEX" not in blob and b"SHEXP" not in blob:
        print(f"{out_path.name}: missing SHEX", file=sys.stderr)
        return 1

    out_path.write_bytes(blob)
    print("wrote", out_path, "bytes", len(blob), "entry", entry.decode())
    return 0


def main() -> int:
    for hlsl, entry, dxbc in JOBS:
        rc = compile_hlsl(HERE / hlsl, entry, HERE / dxbc)
        if rc != 0:
            return rc
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

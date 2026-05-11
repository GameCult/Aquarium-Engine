#!/usr/bin/env python3
"""Bake a Radiance HDR latlong map into a GGX-prefiltered DDS cubemap."""

from __future__ import annotations

import argparse
import math
import struct
from pathlib import Path

import numpy as np


DXGI_FORMAT_R16G16B16A16_FLOAT = 10
D3D10_RESOURCE_DIMENSION_TEXTURE2D = 3
D3D11_RESOURCE_MISC_TEXTURECUBE = 0x4


def read_radiance_hdr(path: Path) -> np.ndarray:
    data = path.read_bytes()
    cursor = 0
    while True:
        end = data.index(b"\n", cursor)
        line = data[cursor:end].decode("ascii", errors="replace").strip()
        cursor = end + 1
        if not line:
            break

    while True:
        end = data.index(b"\n", cursor)
        line = data[cursor:end].decode("ascii", errors="replace").strip()
        cursor = end + 1
        if line:
            resolution = line.split()
            break

    if len(resolution) != 4 or resolution[0] != "-Y" or resolution[2] != "+X":
        raise ValueError(f"Unsupported Radiance orientation in {path}: {' '.join(resolution)}")

    height = int(resolution[1])
    width = int(resolution[3])
    rgbe = np.empty((height, width, 4), dtype=np.uint8)

    for y in range(height):
        if data[cursor] != 2 or data[cursor + 1] != 2:
            raise ValueError(f"Unsupported non-RLE Radiance scanline {y}")
        scan_width = (data[cursor + 2] << 8) | data[cursor + 3]
        cursor += 4
        if scan_width != width:
            raise ValueError(f"Radiance scanline {y} has width {scan_width}, expected {width}")

        scanline = np.empty((4, width), dtype=np.uint8)
        for channel in range(4):
            x = 0
            while x < width:
                code = data[cursor]
                cursor += 1
                if code > 128:
                    count = code - 128
                    scanline[channel, x : x + count] = data[cursor]
                    cursor += 1
                else:
                    count = code
                    scanline[channel, x : x + count] = np.frombuffer(data, dtype=np.uint8, count=count, offset=cursor)
                    cursor += count
                x += count

        rgbe[y] = scanline.T

    exponent = rgbe[..., 3].astype(np.int32)
    scale = np.zeros((height, width), dtype=np.float32)
    valid = exponent > 0
    scale[valid] = np.ldexp(np.ones(np.count_nonzero(valid), dtype=np.float32), exponent[valid] - (128 + 8))
    return rgbe[..., :3].astype(np.float32) * scale[..., None]


def radical_inverse_vdc(bits: int) -> float:
    bits = (bits << 16) | (bits >> 16)
    bits = ((bits & 0x55555555) << 1) | ((bits & 0xAAAAAAAA) >> 1)
    bits = ((bits & 0x33333333) << 2) | ((bits & 0xCCCCCCCC) >> 2)
    bits = ((bits & 0x0F0F0F0F) << 4) | ((bits & 0xF0F0F0F0) >> 4)
    bits = ((bits & 0x00FF00FF) << 8) | ((bits & 0xFF00FF00) >> 8)
    return bits * 2.3283064365386963e-10


def hammersley(count: int) -> np.ndarray:
    values = np.empty((count, 2), dtype=np.float32)
    for i in range(count):
        values[i, 0] = (i + 0.5) / count
        values[i, 1] = radical_inverse_vdc(i)
    return values


def face_directions(face: int, size: int) -> np.ndarray:
    coord = (np.arange(size, dtype=np.float32) + 0.5) / size * 2.0 - 1.0
    u, v = np.meshgrid(coord, coord)
    if face == 0:  # +X
        d = np.stack([np.ones_like(u), -v, -u], axis=-1)
    elif face == 1:  # -X
        d = np.stack([-np.ones_like(u), -v, u], axis=-1)
    elif face == 2:  # +Y
        d = np.stack([u, np.ones_like(u), v], axis=-1)
    elif face == 3:  # -Y
        d = np.stack([u, -np.ones_like(u), -v], axis=-1)
    elif face == 4:  # +Z
        d = np.stack([u, -v, np.ones_like(u)], axis=-1)
    elif face == 5:  # -Z
        d = np.stack([-u, -v, -np.ones_like(u)], axis=-1)
    else:
        raise ValueError(face)
    return normalize(d)


def normalize(v: np.ndarray) -> np.ndarray:
    return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-20)


def tangent_basis(n: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    up = np.zeros_like(n)
    up[..., 1] = 1.0
    alt = np.zeros_like(n)
    alt[..., 2] = 1.0
    use_alt = np.abs(n[..., 1:2]) > 0.98
    up = np.where(use_alt, alt, up)
    tangent = normalize(np.cross(up, n))
    bitangent = np.cross(n, tangent)
    return tangent, bitangent


def sample_latlong(env: np.ndarray, directions: np.ndarray) -> np.ndarray:
    height, width, _ = env.shape
    d = normalize(directions)
    u = np.mod(np.arctan2(d[..., 2], d[..., 0]) / (2.0 * math.pi) + 0.5, 1.0) * width - 0.5
    v = np.arccos(np.clip(d[..., 1], -1.0, 1.0)) / math.pi * height - 0.5

    x0 = np.floor(u).astype(np.int32)
    y0 = np.floor(v).astype(np.int32)
    tx = (u - x0)[..., None]
    ty = (v - y0)[..., None]
    x0 %= width
    x1 = (x0 + 1) % width
    y0 = np.clip(y0, 0, height - 1)
    y1 = np.clip(y0 + 1, 0, height - 1)

    c00 = env[y0, x0]
    c10 = env[y0, x1]
    c01 = env[y1, x0]
    c11 = env[y1, x1]
    return (c00 * (1.0 - tx) + c10 * tx) * (1.0 - ty) + (c01 * (1.0 - tx) + c11 * tx) * ty


def prefilter_face(env: np.ndarray, face: int, size: int, roughness: float, sample_count: int) -> np.ndarray:
    n = face_directions(face, size)
    if roughness <= 0.0001:
        return sample_latlong(env, n).astype(np.float32)

    tangent, bitangent = tangent_basis(n)
    v = n
    alpha = max(roughness * roughness, 0.001)
    result = np.zeros((size, size, 3), dtype=np.float32)
    weight_sum = np.zeros((size, size, 1), dtype=np.float32)

    for xi1, xi2 in hammersley(sample_count):
        phi = 2.0 * math.pi * float(xi1)
        cos_theta = math.sqrt((1.0 - float(xi2)) / (1.0 + (alpha * alpha - 1.0) * float(xi2)))
        sin_theta = math.sqrt(max(0.0, 1.0 - cos_theta * cos_theta))
        h = normalize(
            tangent * (math.cos(phi) * sin_theta)
            + bitangent * (math.sin(phi) * sin_theta)
            + n * cos_theta
        )
        l = normalize(2.0 * np.sum(v * h, axis=-1, keepdims=True) * h - v)
        ndotl = np.maximum(np.sum(n * l, axis=-1, keepdims=True), 0.0).astype(np.float32)
        result += sample_latlong(env, l) * ndotl
        weight_sum += ndotl

    return result / np.maximum(weight_sum, 1e-6)


def dds_header(width: int, height: int, mip_count: int) -> bytes:
    flags = 0x1 | 0x2 | 0x4 | 0x8 | 0x1000 | 0x20000
    caps = 0x1000 | 0x8 | 0x400000
    caps2 = 0x200 | 0x400 | 0x800 | 0x1000 | 0x2000 | 0x4000 | 0x8000
    pitch = width * 8
    pixel_format = struct.pack("<8I", 32, 0x4, int.from_bytes(b"DX10", "little"), 0, 0, 0, 0, 0)
    header = struct.pack(
        "<7I11I",
        124,
        flags,
        height,
        width,
        pitch,
        0,
        mip_count,
        *([0] * 11),
    )
    header += pixel_format
    header += struct.pack("<5I", caps, caps2, 0, 0, 0)
    dx10 = struct.pack(
        "<5I",
        DXGI_FORMAT_R16G16B16A16_FLOAT,
        D3D10_RESOURCE_DIMENSION_TEXTURE2D,
        D3D11_RESOURCE_MISC_TEXTURECUBE,
        1,
        0,
    )
    return b"DDS " + header + dx10


def write_dds(path: Path, mip_faces: list[list[np.ndarray]]) -> None:
    base_size = mip_faces[0][0].shape[0]
    payload = bytearray(dds_header(base_size, base_size, len(mip_faces)))
    for face in range(6):
        for mip in mip_faces:
            rgb = np.maximum(mip[face], 0.0).astype(np.float16)
            alpha = np.ones(rgb.shape[:2] + (1,), dtype=np.float16)
            payload.extend(np.concatenate([rgb, alpha], axis=-1).tobytes())
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--size", type=int, default=256)
    parser.add_argument("--samples", type=int, default=128)
    args = parser.parse_args()

    if args.size <= 0 or args.size & (args.size - 1):
        raise ValueError("--size must be a power of two")

    env = read_radiance_hdr(args.input)
    mip_count = int(math.log2(args.size)) + 1
    mip_faces: list[list[np.ndarray]] = []
    for mip_index in range(mip_count):
        size = max(1, args.size >> mip_index)
        roughness = mip_index / max(1, mip_count - 1)
        sample_count = 1 if mip_index == 0 else max(32, int(args.samples * (0.5 + roughness * 0.5)))
        print(f"mip {mip_index}/{mip_count - 1}: {size}x{size}, roughness={roughness:.3f}, samples={sample_count}")
        mip_faces.append([
            prefilter_face(env, face, size, roughness, sample_count)
            for face in range(6)
        ])

    write_dds(args.output, mip_faces)
    print(f"wrote {args.output}")


if __name__ == "__main__":
    main()

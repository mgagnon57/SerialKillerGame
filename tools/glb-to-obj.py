#!/usr/bin/env python3
"""GLB -> OBJ+MTL, for house-made models on their way into Assets/Noir/Models.

Unity imports OBJ natively and GLB not at all, and every model the owner makes in
Designer comes out as GLB with flat-color materials and no textures - which OBJ+MTL
carries losslessly. Usage:

    python tools/glb-to-obj.py "C:/Users/mgagn/Downloads/desktop-pc-1991.glb" DesktopPC1991
    python tools/glb-to-obj.py "C:/.../police-cruiser-1991.glb" PoliceCruiser1991 -90

writes <Name>.obj + <Name>.mtl into Assets/Noir/Models/. Faces are grouped by
material, so Unity imports ONE mesh with one submesh per material - a single
renderer, ready for the chunker. Node transforms (TRS or matrix) are baked into
world space. Textured GLBs are refused rather than half-converted.

The optional third argument is a YAW in degrees about +Y, baked into the verts -
for models authored facing the wrong axis. The game's vehicles must face +Z
(CityResponse.LengthOf's own note); the owner's cruiser came out facing +X, and
-90 turns +X into +Z.

First used 2026-08-16 for the owner's desktop-pc-1991 - his first model, and it
went straight in.
"""
import json
import os
import struct
import sys


def load_glb(path):
    data = open(path, 'rb').read()
    magic, version, _ = struct.unpack('<III', data[:12])
    if magic != 0x46546C67:
        raise SystemExit(f"{path} is not a GLB (bad magic)")
    clen, = struct.unpack('<I', data[12:16])
    doc = json.loads(data[20:20 + clen])
    return doc, data[20 + clen + 8:]


def accessor(doc, buf, i):
    a = doc['accessors'][i]
    bv = doc['bufferViews'][a['bufferView']]
    off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    comp = {5120: 'b', 5121: 'B', 5122: 'h', 5123: 'H', 5125: 'I', 5126: 'f'}[a['componentType']]
    n = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    vals = struct.unpack_from('<' + comp * n * a['count'], buf, off)
    return [vals[j * n:(j + 1) * n] for j in range(a['count'])]


def matmul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def node_matrix(n):
    if 'matrix' in n:
        m = n['matrix']
        return [[m[0], m[4], m[8], m[12]], [m[1], m[5], m[9], m[13]],
                [m[2], m[6], m[10], m[14]], [m[3], m[7], m[11], m[15]]]
    t = n.get('translation', [0, 0, 0])
    s = n.get('scale', [1, 1, 1])
    x, y, z, w = n.get('rotation', [0, 0, 0, 1])
    r = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
         [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
         [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    return [[r[0][0] * s[0], r[0][1] * s[1], r[0][2] * s[2], t[0]],
            [r[1][0] * s[0], r[1][1] * s[1], r[1][2] * s[2], t[1]],
            [r[2][0] * s[0], r[2][1] * s[1], r[2][2] * s[2], t[2]],
            [0, 0, 0, 1]]


def main():
    if len(sys.argv) not in (3, 4):
        raise SystemExit(__doc__)
    src, name = sys.argv[1], sys.argv[2]
    yaw = float(sys.argv[3]) if len(sys.argv) == 4 else 0.0
    doc, buf = load_glb(src)

    if doc.get('textures'):
        raise SystemExit("this GLB carries textures - the flat-color OBJ path would drop "
                         "them silently, which is exactly what this tool refuses to do")

    groups = {}

    def walk(ni, parent):
        n = doc['nodes'][ni]
        m = matmul(parent, node_matrix(n))
        if 'mesh' in n:
            for prim in doc['meshes'][n['mesh']]['primitives']:
                pos = accessor(doc, buf, prim['attributes']['POSITION'])
                idx = ([v[0] for v in accessor(doc, buf, prim['indices'])]
                       if 'indices' in prim else list(range(len(pos))))
                world = [(m[0][0] * p[0] + m[0][1] * p[1] + m[0][2] * p[2] + m[0][3],
                          m[1][0] * p[0] + m[1][1] * p[1] + m[1][2] * p[2] + m[1][3],
                          m[2][0] * p[0] + m[2][1] * p[1] + m[2][2] * p[2] + m[2][3])
                         for p in pos]
                groups.setdefault(prim.get('material', 0), []).append((world, idx))
        for c in n.get('children', []):
            walk(c, m)

    import math
    rad = math.radians(yaw)
    c, s = math.cos(rad), math.sin(rad)
    root_m = [[c, 0, s, 0], [0, 1, 0, 0], [-s, 0, c, 0], [0, 0, 0, 1]]
    for root in doc['scenes'][doc.get('scene', 0)]['nodes']:
        walk(root, root_m)

    outdir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                          'Assets', 'Noir', 'Models')
    os.makedirs(outdir, exist_ok=True)
    mats = doc.get('materials', [])

    with open(os.path.join(outdir, name + '.mtl'), 'w') as mtl:
        for mi, mat in enumerate(mats):
            c = mat.get('pbrMetallicRoughness', {}).get('baseColorFactor', [0.8, 0.8, 0.8, 1])
            mtl.write(f"newmtl {mat.get('name', 'mat' + str(mi))}\n"
                      f"Kd {c[0]:.4f} {c[1]:.4f} {c[2]:.4f}\nKs 0.05 0.05 0.05\nNs 10\n\n")

    vbase, tris = 1, 0
    with open(os.path.join(outdir, name + '.obj'), 'w') as f:
        f.write(f"mtllib {name}.mtl\no {name}\n")
        for mi in sorted(groups):
            mat_name = mats[mi].get('name', 'mat' + str(mi)) if mi < len(mats) else 'default'
            f.write(f"usemtl {mat_name}\n")
            for verts, idx in groups[mi]:
                for v in verts:
                    f.write(f"v {v[0]:.5f} {v[1]:.5f} {v[2]:.5f}\n")
                for t in range(0, len(idx), 3):
                    f.write(f"f {idx[t] + vbase} {idx[t + 1] + vbase} {idx[t + 2] + vbase}\n")
                tris += len(idx) // 3
                vbase += len(verts)

    print(f"{name}: {vbase - 1} verts, {tris} tris, {len(groups)} material group(s) "
          f"-> Assets/Noir/Models/{name}.obj")


if __name__ == '__main__':
    main()

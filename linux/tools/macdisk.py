#!/usr/bin/env python3
"""Minimal stdlib reader for the recovered Mac disk images.

DiskCopy 4.2 image -> HFS volume -> a file's data/resource fork -> the
resources in a fork.  Just enough to pull PICT and FONT resources out of
the shipped ChipWits+ application and its System file; stdlib only.

    python3 tools/macdisk.py IMAGE.dc42            # list files
    python3 tools/macdisk.py IMAGE.dc42 NAME       # list NAME's resources
"""
import struct, sys


def dc42_volume(path):
    raw = open(path, "rb").read()
    n = struct.unpack(">I", raw[64:68])[0]     # data size; header is 84 bytes
    return raw[84:84 + n]


class HFS:
    def __init__(self, vol):
        self.vol = vol
        mdb = vol[1024:1024 + 162]
        if mdb[:2] != b"BD":
            raise ValueError("not an HFS volume (MFS 400K disks are not supported)")
        self.blksz = struct.unpack(">I", mdb[20:24])[0]
        self.alst = struct.unpack(">H", mdb[28:30])[0]          # in 512-byte sectors
        self.cat = self._extents(mdb[150:162], struct.unpack(">I", mdb[146:150])[0])
        self.files = self._walk()

    def _extents(self, rec, length):
        out = b""
        for i in range(3):
            start, count = struct.unpack(">HH", rec[4 * i:4 * i + 4])
            off = self.alst * 512 + start * self.blksz
            out += self.vol[off:off + count * self.blksz]
        if len(out) < length:
            raise ValueError("fork uses the extents-overflow file (not supported)")
        return out[:length]

    def _walk(self):
        cat = self.cat
        nodesz = struct.unpack(">H", cat[32:34])[0]
        node = struct.unpack(">I", cat[24:28])[0]                # first leaf
        files, dirs = {}, {}
        while node:
            nd = cat[node * nodesz:(node + 1) * nodesz]
            flink, _, kind, _, nrecs = struct.unpack(">IIbbH", nd[:12])
            for i in range(nrecs):
                off = struct.unpack(">H", nd[nodesz - 2 * (i + 1):nodesz - 2 * i])[0]
                klen = nd[off]
                parent = struct.unpack(">I", nd[off + 2:off + 6])[0]
                name = nd[off + 7:off + 7 + nd[off + 6]].decode("mac_roman")
                roff = off + 1 + klen
                roff += roff & 1
                rtype = nd[roff]
                if rtype == 1:                                   # directory
                    dirs[struct.unpack(">I", nd[roff + 6:roff + 10])[0]] = (parent, name)
                elif rtype == 2:                                 # file
                    r = nd[roff:roff + 102]
                    ftype, creator = r[4:8], r[8:12]
                    dlen = struct.unpack(">I", r[26:30])[0]
                    rlen = struct.unpack(">I", r[36:40])[0]
                    files[(parent, name)] = (ftype, creator, r[74:86], dlen, r[86:98], rlen)
            node = flink
        out = {}
        for (parent, name), f in files.items():
            parts = [name]
            while parent in dirs and dirs[parent][0] != 1:       # 1 = root's parent
                parent, pname = dirs[parent]
                parts.insert(0, pname)
            out[":".join(parts)] = f
        return out

    def data(self, name):
        f = self.files[name]
        return self._extents(f[2], f[3])

    def rsrc(self, name):
        f = self.files[name]
        return self._extents(f[4], f[5])


def resources(fork):
    """{(type, id): (name, bytes)} for a resource fork."""
    doff, moff, _, mlen = struct.unpack(">4I", fork[:16])
    m = fork[moff:moff + mlen]
    tlo, nlo = struct.unpack(">HH", m[24:28])
    ntypes = struct.unpack(">H", m[tlo:tlo + 2])[0] + 1
    out = {}
    for i in range(ntypes):
        t, cnt, ro = struct.unpack(">4sHH", m[tlo + 2 + 8 * i:tlo + 10 + 8 * i])
        for j in range(cnt + 1):
            e = tlo + ro + 12 * j
            rid, no = struct.unpack(">hh", m[e:e + 4])
            off = int.from_bytes(m[e + 5:e + 8], "big")
            ln = struct.unpack(">I", fork[doff + off:doff + off + 4])[0]
            name = None
            if no != -1:
                k = nlo + no
                name = m[k + 1:k + 1 + m[k]].decode("mac_roman")
            out[(t.decode("mac_roman"), rid)] = (name, fork[doff + off + 4:doff + off + 4 + ln])
    return out


if __name__ == "__main__":
    hfs = HFS(dc42_volume(sys.argv[1]))
    if len(sys.argv) == 2:
        for name, f in sorted(hfs.files.items()):
            print(f"{name:40} {f[0].decode('mac_roman')} {f[1].decode('mac_roman')} "
                  f"data {f[3]:7} rsrc {f[5]:7}")
    else:
        for (t, rid), (n, d) in sorted(resources(hfs.rsrc(sys.argv[2])).items()):
            print(f"{t} {rid:6} {len(d):7}  {n or ''}")

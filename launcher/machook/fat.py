"""fat.py OUT ARM64 X86_64 - merge two thin Mach-O files into a universal (fat) file."""
import struct, sys
out, arm, x86 = sys.argv[1:4]
slices = [(0x0100000C, 0, open(arm, "rb").read()), (0x01000007, 3, open(x86, "rb").read())]
align = 14  # 16 KiB
hdr = struct.pack(">II", 0xCAFEBABE, len(slices))
off, body, archs = 1 << align, b"", b""
for cpu, sub, data in slices:
    archs += struct.pack(">iiIII", cpu, sub, off, len(data), align)
    pad = ((off + len(data) + (1 << align) - 1) & ~((1 << align) - 1)) - off - len(data)
    body += data + b"\0" * pad
    off += len(data) + pad
head = hdr + archs
open(out, "wb").write(head + b"\0" * ((1 << align) - len(head)) + body)

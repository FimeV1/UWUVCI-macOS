using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace UWUVCI.Core.Tools
{
    /// <summary>
    /// Compresses / decompresses Wii U RPX files. Port of wiiurpxtool 1.3 by CBH (GPLv3), which the Windows app runs as wiiurpxtool.exe.
    /// </summary>
    public static class RpxTool
    {
        private const uint SHF_RPL_ZLIB = 0x08000000;
        private const uint SHT_RPL_CRCS = 0x80000003;
        private const uint SHT_RPL_FILEINFO = 0x80000004;
        private const int CHUNK = 16384;

        private sealed class Section
        {
            public uint Name, Type, Flags, Addr, Offset, Size, Link, Info, AddrAlign, EntSize;
        }

        public static void Decompress(string rpxPath) => File.WriteAllBytes(rpxPath, Convert(File.ReadAllBytes(rpxPath), compress: false));

        public static void Compress(string rpxPath) => File.WriteAllBytes(rpxPath, Convert(File.ReadAllBytes(rpxPath), compress: true));

        public static byte[] Convert(byte[] input, bool compress)
        {
            if (input.Length < 0x34 || BinaryPrimitives.ReadUInt32BigEndian(input) != 0x7F454C46)
                throw new InvalidDataException("Not an RPX/RPL file (no ELF header).");
            if (BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(0x10)) != 0xFE01)
                throw new InvalidDataException("Not an RPX/RPL file (wrong ELF type).");

            uint shoff = BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(0x20));
            ushort shentsize = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(0x2E));
            ushort shnum = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(0x30));
            long sectionDataStart = shoff + shnum * shentsize;

            using var output = new MemoryStream(input.Length * (compress ? 1 : 3));
            // ELF header is copied unchanged, followed by 12 zero bytes and the (later rewritten) section table.
            output.Write(input, 0, 0x34);
            output.Write(new byte[12], 0, 12);
            while (output.Position < sectionDataStart)
                output.WriteByte(0);

            var sections = new Section[shnum];
            var crcs = new uint[shnum];
            for (int i = 0; i < shnum; i++)
            {
                var s = input.AsSpan((int)(shoff + i * shentsize));
                sections[i] = new Section
                {
                    Name = BinaryPrimitives.ReadUInt32BigEndian(s),
                    Type = BinaryPrimitives.ReadUInt32BigEndian(s.Slice(4)),
                    Flags = BinaryPrimitives.ReadUInt32BigEndian(s.Slice(8)),
                    Addr = BinaryPrimitives.ReadUInt32BigEndian(s.Slice(12)),
                    Offset = BinaryPrimitives.ReadUInt32BigEndian(s.Slice(16)),
                    Size = BinaryPrimitives.ReadUInt32BigEndian(s.Slice(20)),
                    Link = BinaryPrimitives.ReadUInt32BigEndian(s.Slice(24)),
                    Info = BinaryPrimitives.ReadUInt32BigEndian(s.Slice(28)),
                    AddrAlign = BinaryPrimitives.ReadUInt32BigEndian(s.Slice(32)),
                    EntSize = BinaryPrimitives.ReadUInt32BigEndian(s.Slice(36)),
                };
            }

            long crcDataOffset = 0;
            foreach (int index in Enumerable.Range(0, shnum).Where(i => sections[i].Offset != 0).OrderBy(i => sections[i].Offset))
            {
                Section section = sections[index];
                int pos = (int)section.Offset;
                section.Offset = (uint)output.Position;
                bool isZlib = (section.Flags & SHF_RPL_ZLIB) == SHF_RPL_ZLIB;

                if (!compress)
                {
                    if (isZlib)
                    {
                        int dataSize = (int)section.Size - 4;
                        section.Size = BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(pos));
                        byte[] raw = Inflate(input, pos + 4, dataSize, (int)section.Size);
                        output.Write(raw, 0, raw.Length);
                        crcs[index] = Crc32(raw, 0, raw.Length);
                        section.Flags &= ~SHF_RPL_ZLIB;
                    }
                    else
                    {
                        output.Write(input, pos, (int)section.Size);
                        crcs[index] = Crc32(input, pos, (int)section.Size);
                    }
                }
                else
                {
                    bool keep = (section.Type & SHT_RPL_FILEINFO) == SHT_RPL_FILEINFO
                             || (section.Type & SHT_RPL_CRCS) == SHT_RPL_CRCS
                             || isZlib;
                    int dataSize = (int)section.Size;
                    crcs[index] = Crc32(input, pos, dataSize);
                    if (keep)
                        output.Write(input, pos, dataSize);
                    else
                    {
                        byte[] packed = Deflate(input, pos, dataSize);
                        // Small sections are only stored compressed when that actually saves space.
                        if (dataSize < CHUNK && packed.Length + 4 >= dataSize)
                            output.Write(input, pos, dataSize);
                        else
                        {
                            WriteUInt32(output, (uint)dataSize);
                            output.Write(packed, 0, packed.Length);
                            section.Size = (uint)packed.Length + 4;
                            section.Flags |= SHF_RPL_ZLIB;
                        }
                    }
                }

                while (output.Position % 0x40 != 0)
                    output.WriteByte(0);

                if ((section.Type & SHT_RPL_CRCS) == SHT_RPL_CRCS)
                {
                    crcs[index] = 0;
                    crcDataOffset = section.Offset;
                }
            }

            output.Position = shoff;
            foreach (Section s in sections)
                foreach (uint value in new[] { s.Name, s.Type, s.Flags, s.Addr, s.Offset, s.Size, s.Link, s.Info, s.AddrAlign, s.EntSize })
                    WriteUInt32(output, value);

            output.Position = crcDataOffset;
            foreach (uint crc in crcs)
                WriteUInt32(output, crc);

            return output.ToArray();
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        private static byte[] Inflate(byte[] data, int offset, int length, int expectedSize)
        {
            using var source = new MemoryStream(data, offset, length);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            using var result = new MemoryStream(expectedSize);
            zlib.CopyTo(result);
            return result.ToArray();
        }

        private static byte[] Deflate(byte[] data, int offset, int length)
        {
            using var result = new MemoryStream();
            using (var zlib = new ZLibStream(result, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(data, offset, length);
            return result.ToArray();
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
                crc = (crc >> 8) ^ CrcTable[(crc ^ data[i]) & 0xFF];
            return ~crc;
        }
    }
}

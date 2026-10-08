using System;
using System.IO;
using System.Text;

namespace UWUVCI_AIO_WPF.Classes
{
    public static class N64FrameLayoutPatcher
    {
        public static void Apply(byte[] archive, bool widescreen, bool removeDarkFilter)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));
            if (!widescreen && !removeDarkFilter)
                return;

            RequireRange(archive, 0, 0x40);
            if (!HasTag(archive, 0, "SARC") || ReadUInt16(archive, 6) != 0xFEFF)
                throw new InvalidDataException("The N64 base has an unsupported FrameLayout.arc archive.");

            // These are the archive and pane offsets used before the official dbbad4a refactor.
            int dataOffset = ReadOffset(archive, 0x0C);
            int layoutRelativeOffset = ReadOffset(archive, 0x38);
            RequireRange(archive, dataOffset, layoutRelativeOffset);
            int layoutOffset = dataOffset + layoutRelativeOffset;
            RequireRange(archive, layoutOffset, 0x14);
            if (!HasTag(archive, layoutOffset, "FLYT") || ReadUInt16(archive, layoutOffset + 4) != 0xFEFF)
                throw new InvalidDataException("The N64 base's frame layout is not a supported FLYT layout.");

            int headerSize = ReadUInt16(archive, layoutOffset + 6);
            int layoutSize = ReadOffset(archive, layoutOffset + 0x0C);
            RequireRange(archive, layoutOffset, layoutSize);
            if (headerSize < 0x14 || headerSize > layoutSize)
                throw new InvalidDataException("The N64 frame layout has an invalid header size.");

            int frameOffset = -1;
            int maskOffset = -1;
            int layoutEnd = layoutOffset + layoutSize;
            int offset = layoutOffset + headerSize;
            while (offset < layoutEnd)
            {
                if (layoutEnd - offset < 8)
                    throw new InvalidDataException("The N64 frame layout has a truncated section header.");

                int sectionSize = ReadOffset(archive, offset + 4);
                if (sectionSize < 8 || sectionSize > layoutEnd - offset)
                    throw new InvalidDataException("The N64 frame layout has an invalid section size.");

                if (HasTag(archive, offset, "pic1"))
                {
                    if (sectionSize < 0x24)
                        throw new InvalidDataException("The N64 frame layout has a truncated picture pane.");

                    int nameStart = offset + 0x0C;
                    int nameEnd = Array.IndexOf(archive, (byte)0, nameStart, 0x18);
                    string name = Encoding.ASCII.GetString(archive, nameStart,
                        nameEnd < 0 ? 0x18 : nameEnd - nameStart);

                    if (name == "frame")
                    {
                        if (sectionSize < 0x50)
                            throw new InvalidDataException("The N64 frame pane is too short to patch.");
                        frameOffset = offset;
                    }
                    else if (name == "frame_mask")
                    {
                        maskOffset = offset;
                    }
                }

                if (frameOffset >= 0 && maskOffset >= 0)
                    break;
                offset += sectionSize;
            }

            if (frameOffset < 0 || maskOffset < 0)
                throw new InvalidDataException("The N64 base's frame layout is missing its frame or dark-filter pane.");

            // Validate both panes before changing any bytes; Wii U floats are big-endian.
            WriteUInt32(archive, frameOffset + 0x2C, 0);
            WriteUInt32(archive, frameOffset + 0x30, 0);
            WriteUInt32(archive, frameOffset + 0x44, 0x3F800000);
            WriteUInt32(archive, frameOffset + 0x48, 0x3F800000);
            WriteUInt32(archive, frameOffset + 0x4C, widescreen ? 0x44F00000u : 0x44B40000u);
            archive[maskOffset + 0x08] = removeDarkFilter ? (byte)0 : (byte)1;
        }

        private static bool HasTag(byte[] data, int offset, string tag)
        {
            for (int index = 0; index < tag.Length; index++)
                if (data[offset + index] != tag[index])
                    return false;
            return true;
        }

        private static int ReadUInt16(byte[] data, int offset)
        {
            RequireRange(data, offset, 2);
            return data[offset] << 8 | data[offset + 1];
        }

        private static int ReadOffset(byte[] data, int offset)
        {
            RequireRange(data, offset, 4);
            uint value = (uint)data[offset] << 24 | (uint)data[offset + 1] << 16 |
                         (uint)data[offset + 2] << 8 | data[offset + 3];
            if (value > int.MaxValue)
                throw new InvalidDataException("The N64 frame layout contains an invalid offset or size.");
            return (int)value;
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        private static void RequireRange(byte[] data, int offset, int length)
        {
            if (offset < 0 || offset > data.Length || length < 0 || length > data.Length - offset)
                throw new InvalidDataException("The N64 base's FrameLayout.arc is truncated or contains an invalid offset.");
        }
    }
}

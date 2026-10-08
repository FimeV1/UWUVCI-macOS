using System;
using System.Buffers.Binary;
using System.IO;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.Core.Tools
{
    /// <summary>Creates bootSound.btsnd. Replaces sox.exe (via the built-in afconvert on macOS) and wav2btsnd.</summary>
    public static class BootSoundTool
    {
        private const int SampleRate = 48000, Channels = 2, MaxSeconds = 6;

        public static void Create(string soundPath, string btsndPath, string tempDirectory)
        {
            string extension = Path.GetExtension(soundPath).ToLowerInvariant();
            if (extension.Contains("btsnd"))
            {
                File.Copy(soundPath, btsndPath, true);
                return;
            }

            byte[] pcm = extension.Contains("wav") ? TryReadPcm(soundPath) : null;
            if (pcm == null)
            {
                // Anything that is not already 48 kHz 16 bit stereo PCM goes through the system converter first.
                string converted = Path.Combine(tempDirectory, "bootSound.wav");
                Convert(soundPath, converted);
                pcm = TryReadPcm(converted) ?? throw new Exception("The boot sound could not be converted to 48 kHz 16 bit stereo.");
                File.Delete(converted);
            }

            int limit = SampleRate * Channels * 2 * MaxSeconds;
            int length = Math.Min(pcm.Length, limit) & ~1;

            using var output = new FileStream(btsndPath, FileMode.Create, FileAccess.Write);
            output.Write(new byte[] { 0, 0, 0, 2, 0, 0, 0, 0 }, 0, 8);
            // btsnd is big-endian, WAV is little-endian.
            for (int i = 0; i < length; i += 2)
            {
                output.WriteByte(pcm[i + 1]);
                output.WriteByte(pcm[i]);
            }
        }

        /// <summary>True when the file is a RIFF WAVE the Wii U format can take as is (the check the Windows app warns about).</summary>
        public static bool IsRiffWave(string path)
        {
            try
            {
                var header = new byte[12];
                using var stream = File.OpenRead(path);
                return stream.Read(header, 0, 12) == 12
                    && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                    && header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E';
            }
            catch
            {
                return false;
            }
        }

        private static void Convert(string input, string output)
        {
            if (!AppPaths.IsMac)
                throw new Exception("Boot sounds must be 48 kHz 16 bit stereo WAV files on this system.");

            ProcessResult result = ProcessRunner.Run("/usr/bin/afconvert",
                new[] { "-f", "WAVE", "-d", "LEI16@" + SampleRate, "-c", Channels.ToString(), input, output });
            if (!result.Ok || !File.Exists(output))
                throw new Exception("The boot sound could not be converted: " + result.Error.Trim());
        }

        /// <summary>Returns the sample data when the WAV is 48 kHz 16 bit stereo PCM, otherwise null.</summary>
        private static byte[] TryReadPcm(string path)
        {
            byte[] wav = File.ReadAllBytes(path);
            if (wav.Length < 12 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
                return null;

            bool formatOk = false;
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
                long size = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(pos + 4));
                int body = pos + 8;
                if (id == "fmt " && body + 16 <= wav.Length)
                {
                    int format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body));
                    int channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 2));
                    int rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(body + 4));
                    int bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 14));
                    // 0xFFFE is WAVE_FORMAT_EXTENSIBLE, which afconvert emits for plain PCM too.
                    formatOk = (format == 1 || format == 0xFFFE) && channels == Channels && rate == SampleRate && bits == 16;
                    if (!formatOk)
                    {
                        Logger.Log($"Boot sound is {rate} Hz, {bits} bit, {channels} channel(s); converting.");
                        return null;
                    }
                }
                else if (id == "data")
                {
                    if (!formatOk)
                        return null;
                    int available = (int)Math.Min(size, wav.Length - body);
                    var pcm = new byte[available];
                    Buffer.BlockCopy(wav, body, pcm, 0, available);
                    return pcm;
                }
                pos = body + (int)Math.Min(size + (size & 1), int.MaxValue - body);
            }
            return null;
        }
    }
}

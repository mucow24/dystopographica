using System;
using System.IO;
using System.IO.Compression;

namespace Dystopographica
{
    /// <summary>
    /// Reads a PNG heightmap straight from disk. The decoder is our own because Unity's
    /// (Texture2D.LoadImage) cuts 16-bit files down to 8 bits, which turns flat land
    /// into terraces, and its texture importer resizes and compresses as well.
    /// </summary>
    public static class HeightmapFile
    {
        const string Damaged = "The PNG file is damaged or incomplete.";
        static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static Heightmap Load(string path) => Decode(File.ReadAllBytes(path));

        /// <summary>
        /// Decodes an 8-bit or 16-bit PNG. A colour image with blue or green areas is a
        /// painted map (see PaintedMap). Any other colour image contributes its red
        /// channel. An alpha channel is ignored.
        /// </summary>
        public static Heightmap Decode(byte[] file)
        {
            int bitDepth = BitDepth(file);
            if (bitDepth == 0) throw new InvalidDataException("The heightmap is not a PNG file.");
            if (bitDepth != 8 && bitDepth != 16)
                throw new InvalidDataException($"{bitDepth}-bit PNG files are not supported. Save the heightmap as 8-bit or 16-bit greyscale.");
            int colourType = file[25];
            if (colourType == 3)
                throw new InvalidDataException("Indexed-colour PNG files are not supported. Save the heightmap as greyscale.");
            if (file[28] != 0)
                throw new InvalidDataException("Interlaced PNG files are not supported. Save the heightmap without interlacing.");

            int width = ReadInt(file, 16), height = ReadInt(file, 20);
            int channels = colourType == 0 ? 1 : colourType == 2 ? 3 : colourType == 4 ? 2 : 4;
            int bytesPerPixel = channels * bitDepth / 8;
            int bytesPerChannel = bitDepth / 8;
            bool colour = channels >= 3;
            var values = new float[width * height];
            float[] green = colour ? new float[width * height] : null, blue = colour ? new float[width * height] : null;
            byte[] row = new byte[width * bytesPerPixel], above = new byte[width * bytesPerPixel];

            try
            {
                using (var inflate = new DeflateStream(PixelData(file), CompressionMode.Decompress))
                {
                    for (int y = 0; y < height; y++)
                    {
                        int filter = inflate.ReadByte();
                        for (int read = 0; read < row.Length;)
                        {
                            int count = inflate.Read(row, read, row.Length - read);
                            if (count <= 0) throw new InvalidDataException(Damaged);
                            read += count;
                        }
                        Unfilter(filter, row, above, bytesPerPixel);

                        for (int x = 0; x < width; x++)
                        {
                            int i = x * bytesPerPixel;
                            values[y * width + x] = Sample(row, i, bitDepth);
                            if (!colour) continue;
                            green[y * width + x] = Sample(row, i + bytesPerChannel, bitDepth);
                            blue[y * width + x] = Sample(row, i + 2 * bytesPerChannel, bitDepth);
                        }
                        (row, above) = (above, row);
                    }
                }
            }
            catch (Exception e) when (e.Message != Damaged)
            {
                // DeflateStream reports bad data in its own words. Say it in ours.
                throw new InvalidDataException(Damaged, e);
            }
            Heightmap painted = colour ? PaintedMap.Read(width, height, values, green, blue) : null;
            return painted ?? new Heightmap(width, height, values);
        }

        static float Sample(byte[] row, int at, int bitDepth) =>
            bitDepth == 16 ? ((row[at] << 8) | row[at + 1]) / 65535f : row[at] / 255f;

        /// <summary>
        /// Writes a 16-bit colour PNG. `rgb` holds three numbers for each pixel, red, green
        /// and blue, row by row from the top. Unity's own encoder is not used, for the
        /// decoder's reason: this file is read back as heights and wants all sixteen bits.
        /// </summary>
        public static byte[] EncodeRgb16(int width, int height, ushort[] rgb)
        {
            if (rgb.Length != width * height * 3)
                throw new ArgumentException($"A {width} x {height} picture needs {width * height * 3} numbers, not {rgb.Length}.");

            // Each row is a filter byte and then its pixels, high byte first. Filter 1
            // stores each byte as its difference from the same byte of the pixel to the
            // left, which is what makes flat areas and slopes compress.
            const int bytesPerPixel = 6;
            int stride = 1 + width * bytesPerPixel;
            var rows = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int start = y * stride;
                rows[start] = 1;
                for (int i = 0; i < width * 3; i++)
                {
                    ushort value = rgb[y * width * 3 + i];
                    rows[start + 1 + i * 2] = (byte)(value >> 8);
                    rows[start + 2 + i * 2] = (byte)value;
                }
                for (int i = stride - 1; i > bytesPerPixel; i--) rows[start + i] -= rows[start + i - bytesPerPixel];
            }

            var header = new byte[13];
            WriteInt(header, 0, (uint)width);
            WriteInt(header, 4, (uint)height);
            header[8] = 16; // bits per channel
            header[9] = 2;  // colour type: red, green, blue

            var file = new MemoryStream();
            file.Write(Signature, 0, Signature.Length);
            WriteChunk(file, "IHDR", header);
            WriteChunk(file, "IDAT", Zlib(rows));
            WriteChunk(file, "IEND", new byte[0]);
            return file.ToArray();
        }

        /// <summary>Deflate-compressed data in the zlib wrapper PNG asks for: a two-byte header before, a checksum after.</summary>
        static byte[] Zlib(byte[] data)
        {
            var packed = new MemoryStream();
            packed.WriteByte(0x78);
            packed.WriteByte(0x9C);
            using (var deflate = new DeflateStream(packed, CompressionMode.Compress, true)) deflate.Write(data, 0, data.Length);

            // Adler-32.
            uint a = 1, b = 0;
            foreach (byte value in data)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }
            var checksum = new byte[4];
            WriteInt(checksum, 0, (b << 16) | a);
            packed.Write(checksum, 0, 4);
            return packed.ToArray();
        }

        static void WriteChunk(Stream file, string type, byte[] data)
        {
            var named = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) named[i] = (byte)type[i];
            Array.Copy(data, 0, named, 4, data.Length);

            // CRC-32 of the chunk's name and contents.
            uint crc = 0xFFFFFFFF;
            foreach (byte value in named)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }

            var number = new byte[4];
            WriteInt(number, 0, (uint)data.Length);
            file.Write(number, 0, 4);
            file.Write(named, 0, named.Length);
            WriteInt(number, 0, ~crc);
            file.Write(number, 0, 4);
        }

        static void WriteInt(byte[] into, int at, uint value)
        {
            into[at] = (byte)(value >> 24);
            into[at + 1] = (byte)(value >> 16);
            into[at + 2] = (byte)(value >> 8);
            into[at + 3] = (byte)value;
        }

        /// <summary>Bits per channel from the PNG header, or 0 when the file is not a PNG.</summary>
        public static int BitDepth(byte[] file)
        {
            if (file == null || file.Length < 33) return 0;
            for (int i = 0; i < Signature.Length; i++)
                if (file[i] != Signature[i]) return 0;
            return file[24];
        }

        static int ReadInt(byte[] file, int at) => (file[at] << 24) | (file[at + 1] << 16) | (file[at + 2] << 8) | file[at + 3];

        /// <summary>
        /// The compressed pixels: the contents of every IDAT chunk joined together, placed
        /// just past the two-byte zlib header so DeflateStream can read them.
        /// </summary>
        static Stream PixelData(byte[] file)
        {
            var data = new MemoryStream();
            for (int at = 8; at + 8 <= file.Length;)
            {
                int length = ReadInt(file, at);
                bool isPixelData = file[at + 4] == 'I' && file[at + 5] == 'D' && file[at + 6] == 'A' && file[at + 7] == 'T';
                if (length < 0 || at + 8 + length > file.Length) length = file.Length - at - 8;
                if (isPixelData) data.Write(file, at + 8, length);
                at += 12 + length;
            }
            data.Position = Math.Min(2, data.Length);
            return data;
        }

        /// <summary>
        /// PNG stores each row as the difference from a prediction made from the pixel to
        /// the left and the row above. This puts the real bytes back.
        /// </summary>
        static void Unfilter(int filter, byte[] row, byte[] above, int bytesPerPixel)
        {
            if (filter == 0) return;
            if (filter < 0 || filter > 4) throw new InvalidDataException(Damaged);

            for (int i = 0; i < row.Length; i++)
            {
                int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                int up = above[i];
                int upLeft = i >= bytesPerPixel ? above[i - bytesPerPixel] : 0;
                int predicted =
                    filter == 1 ? left :
                    filter == 2 ? up :
                    filter == 3 ? (left + up) / 2 :
                    Paeth(left, up, upLeft);
                row[i] = (byte)(row[i] + predicted);
            }
        }

        static int Paeth(int left, int up, int upLeft)
        {
            int estimate = left + up - upLeft;
            int toLeft = Math.Abs(estimate - left), toUp = Math.Abs(estimate - up), toUpLeft = Math.Abs(estimate - upLeft);
            if (toLeft <= toUp && toLeft <= toUpLeft) return left;
            return toUp <= toUpLeft ? up : upLeft;
        }
    }
}

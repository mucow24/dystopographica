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
        /// Decodes an 8-bit or 16-bit PNG. A colour image contributes its red channel,
        /// and any alpha channel is ignored.
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
            var values = new float[width * height];
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
                            values[y * width + x] = bitDepth == 16 ? ((row[i] << 8) | row[i + 1]) / 65535f : row[i] / 255f;
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
            return new Heightmap(width, height, values);
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

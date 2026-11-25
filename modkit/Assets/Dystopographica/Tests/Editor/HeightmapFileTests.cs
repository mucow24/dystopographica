using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Dystopographica.Tests
{
    public class HeightmapFileTests
    {
        // Four 4 x 2 PNG files, written with Python's struct and zlib modules. Top row first.
        //   16-bit pixels: 0 1 255 256 / 257 32768 65534 65535
        //    8-bit pixels: 0 1 127 128 / 200 254 255 64
        const string Grey16 = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACEAAAAAAKU/78AAAAGUlEQVR42mNgYGBgZPjPCCQZGxj+//v/HwAZIQV/YlgM4AAAAABJRU5ErkJggg==";
        const string Grey8 = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAAAAABawyK/AAAAEklEQVR42mNgYKxvYDjx778DAA7jBAa6dV1iAAAAAElFTkSuQmCC";
        const string Rgb8 = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAIAAADwyuo0AAAAIUlEQVR42gXBAQEAAAwBMJppdvGk4hsAkLw720jSdpukB3OsDBBuYvTaAAAAAElFTkSuQmCC";
        const string Rgba16 = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACEAYAAAAvOKEgAAAAJklEQVR42j2JMRIAMAjC8Od9ualmMBk4IBFIrZMKVneBl3We1uMD/CUga/DjQycAAAAASUVORK5CYII=";

        // Two more from the same script, which also applies PNG's row filters. Each file
        // uses all of them (none, sub, up, average, Paeth), and the first is split
        // across two data chunks.
        //   Filtered16:    5 x 6, 16-bit grey, pixel(x, y) = (x * 7919 + y * 10473 + x * y * 311) % 65536
        //   FilteredRgba8: 5 x 5, 8-bit RGBA, red(x, y) = (x * 53 + y * 101 + x * y * 7) % 256,
        //                  green and blue the same as red, so that it is not a painted map
        const string Filtered16 ="iVBORw0KGgoAAAANSUhEUgAAAAUAAAAGEAAAAAB+ANfUAAAACXRFWHRDb21tZW50AHjX9HQIAAAAFElEQVR42mNgYJB7b3sv5mz1HkaNl4pqCupOb/QAAAAzSURBVGDIpPlSS0E7XKdP7yhzUJD6LfUyjYmaa1k0X6ooqISrnNY9DWSqKqgq6USqHgUAtggU4qMING4AAAAASUVORK5CYII=";
        const string FilteredRgba8 = "iVBORw0KGgoAAAANSUhEUgAAAAUAAAAFCAYAAACNbyblAAAACXRFWHRDb21tZW50AHjX9HQIAAAAWklEQVR42mNgAAJTU1ONrKysgPnz51dcuXJlAWNqaiqnjY2NBjJmAgnm5ORwFhcXc1ZVVXE2NjZyMp86dUooOjpaMj4+XjIpKUny2bNnkiwglYGBgWAMUgmiAdxaIeB9xlAkAAAAAElFTkSuQmCC";

        static readonly int[] Pixels16 = { 0, 1, 255, 256, 257, 32768, 65534, 65535 };
        static readonly int[] Pixels8 = { 0, 1, 127, 128, 200, 254, 255, 64 };

        static void AssertPixels(string base64, int[] expected, int fullScale)
        {
            Heightmap map = HeightmapFile.Decode(Convert.FromBase64String(base64));

            Assert.AreEqual(4, map.Width);
            Assert.AreEqual(2, map.Height);
            var actual = new int[map.Values.Length];
            for (int i = 0; i < actual.Length; i++) actual[i] = Mathf.RoundToInt(map.Values[i] * fullScale);
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void Decode_Grey16_KeepsAllSixteenBits() => AssertPixels(Grey16, Pixels16, 65535);

        [Test]
        public void Decode_Grey8_ReadsEveryPixelTopRowFirst() => AssertPixels(Grey8, Pixels8, 255);

        [Test]
        public void Decode_Rgb8_UsesTheFirstChannel() => AssertPixels(Rgb8, Pixels8, 255);

        [Test]
        public void Decode_Rgba16_KeepsAllSixteenBits() => AssertPixels(Rgba16, Pixels16, 65535);

        [Test]
        public void Decode_Filtered16_UndoesEveryRowFilter()
        {
            Heightmap map = HeightmapFile.Decode(Convert.FromBase64String(Filtered16));

            Assert.AreEqual(5, map.Width);
            Assert.AreEqual(6, map.Height);
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 5; x++)
                    Assert.AreEqual((x * 7919 + y * 10473 + x * y * 311) % 65536, Mathf.RoundToInt(map[x, y] * 65535), $"pixel {x}, {y}");
        }

        [Test]
        public void Decode_FilteredRgba8_UndoesEveryRowFilter()
        {
            Heightmap map = HeightmapFile.Decode(Convert.FromBase64String(FilteredRgba8));

            Assert.AreEqual(5, map.Width);
            Assert.AreEqual(5, map.Height);
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 5; x++)
                    Assert.AreEqual((x * 53 + y * 101 + x * y * 7) % 256, Mathf.RoundToInt(map[x, y] * 255), $"pixel {x}, {y}");
        }

        [TestCase(24, 4, "4-bit")]
        [TestCase(25, 3, "Indexed")]
        [TestCase(28, 1, "Interlaced")]
        public void Decode_UnsupportedKindOfPng_SaysWhich(int headerByte, byte value, string expected)
        {
            byte[] file = Convert.FromBase64String(Grey8);
            file[headerByte] = value;

            var error = Assert.Throws<InvalidDataException>(() => HeightmapFile.Decode(file));

            StringAssert.Contains(expected, error.Message);
        }

        [Test]
        public void Decode_CutOffFile_SaysItIsDamaged()
        {
            byte[] whole = Convert.FromBase64String(Filtered16);
            var file = new byte[whole.Length - 40];
            Array.Copy(whole, file, file.Length);

            var error = Assert.Throws<InvalidDataException>(() => HeightmapFile.Decode(file));

            StringAssert.Contains("damaged", error.Message);
        }

        [Test]
        public void Decode_WhiteIsOne()
        {
            Heightmap map = HeightmapFile.Decode(Convert.FromBase64String(Grey16));

            Assert.AreEqual(1f, map.Values[7]);
        }

        [Test]
        public void Decode_NotAPng_SaysSo()
        {
            var error = Assert.Throws<InvalidDataException>(() => HeightmapFile.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));

            StringAssert.Contains("PNG", error.Message);
        }

        // 3 x 2: red, green, blue / black, mid-grey, white.
        static readonly ushort[] Colours =
        {
            65535, 0, 0, 0, 65535, 0, 0, 0, 65535,
            0, 0, 0, 32768, 32768, 32768, 65535, 65535, 65535,
        };

        [Test]
        public void EncodeRgb16_WritesAPngThatUnityReads()
        {
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(texture.LoadImage(HeightmapFile.EncodeRgb16(3, 2, Colours)));

                Assert.AreEqual(3, texture.width);
                Assert.AreEqual(2, texture.height);
                // Unity's rows run from the bottom up.
                Color32 green = texture.GetPixel(1, 1), grey = texture.GetPixel(1, 0);
                Assert.AreEqual(new Color32(0, 255, 0, 255), green);
                Assert.AreEqual(new Color32(128, 128, 128, 255), grey);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void EncodeRgb16_KeepsAllSixteenBits()
        {
            // Nothing in it is blue or green, so it reads back as a heightmap: its red channel.
            var grey = new ushort[4 * 2 * 3];
            for (int i = 0; i < grey.Length; i++) grey[i] = (ushort)Pixels16[i / 3];

            Heightmap map = HeightmapFile.Decode(HeightmapFile.EncodeRgb16(4, 2, grey));

            Assert.IsFalse(map.IsPainted);
            for (int i = 0; i < Pixels16.Length; i++) Assert.AreEqual(Pixels16[i], Mathf.RoundToInt(map.Values[i] * 65535), $"pixel {i}");
        }

        [Test]
        public void Decode_APictureWithBlueOrGreenInIt_IsAPaintedMap()
        {
            Heightmap map = HeightmapFile.Decode(HeightmapFile.EncodeRgb16(3, 2, Colours));

            Assert.IsTrue(map.IsPainted);
            Assert.IsTrue(map.HasPaintedWater);
            Assert.That(map[1, 0], Is.InRange(PaintedMap.PlaneStart, PaintedMap.PlaneEnd), "green");
            Assert.Less(map[2, 0], PaintedMap.Water, "blue");
            Assert.AreEqual(1f, map[2, 1], "white");
        }

        [Test]
        public void BitDepth_ComesFromTheHeader()
        {
            Assert.AreEqual(16, HeightmapFile.BitDepth(Convert.FromBase64String(Grey16)));
            Assert.AreEqual(8, HeightmapFile.BitDepth(Convert.FromBase64String(Rgb8)));
        }
    }
}

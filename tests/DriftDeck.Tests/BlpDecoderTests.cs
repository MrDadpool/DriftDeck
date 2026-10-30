using System.Buffers.Binary;
using System.IO;
using DriftDeck.Services;
using Xunit;

namespace DriftDeck.Tests;

/// <summary>
/// There is no real game art to test against, so these build tiny BLP2 files byte by byte. That
/// also lets each case pin exact pixel values, which a sample file from a game client never
/// could. The rejection cases matter as much: a theme folder is user-supplied, so bad files have
/// to fail with a plain error instead of crashing the app.
/// </summary>
public class BlpDecoderTests
{
    private const int PayloadOffset = 148 + 1024;

    private const ushort Red565 = 0xF800;
    private const ushort Blue565 = 0x001F;

    /// <summary>
    /// Builds a BLP2 file: header, palette, then the payload as mip 0.
    /// </summary>
    private static byte[] Build(
        byte encoding,
        byte alphaDepth,
        byte alphaEncoding,
        int width,
        int height,
        byte[] payload,
        byte[]? palette = null,
        uint? offsetOverride = null)
    {
        var data = new byte[PayloadOffset + payload.Length];
        data[0] = (byte)'B';
        data[1] = (byte)'L';
        data[2] = (byte)'P';
        data[3] = (byte)'2';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), 1);
        data[8] = encoding;
        data[9] = alphaDepth;
        data[10] = alphaEncoding;
        data[11] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12, 4), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, 4), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), offsetOverride ?? (uint)PayloadOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(84, 4), (uint)payload.Length);

        if (palette != null)
        {
            Array.Copy(palette, 0, data, 148, palette.Length);
        }

        Array.Copy(payload, 0, data, PayloadOffset, payload.Length);
        return data;
    }

    private static byte[] TwoColorPalette()
    {
        var palette = new byte[1024];
        // Entry 0 is B=10 G=20 R=30; entry 1 is B=40 G=50 R=60. Palette alpha is junk on purpose.
        palette[0] = 10;
        palette[1] = 20;
        palette[2] = 30;
        palette[3] = 7;
        palette[4] = 40;
        palette[5] = 50;
        palette[6] = 60;
        palette[7] = 7;
        return palette;
    }

    private static byte[] Px(BlpImage image, int x, int y)
    {
        int o = ((y * image.Width) + x) * 4;
        return new[] { image.Bgra[o], image.Bgra[o + 1], image.Bgra[o + 2], image.Bgra[o + 3] };
    }

    /// <summary>
    /// A DXT1-style color block: two RGB565 endpoints (little-endian) and four index rows.
    /// </summary>
    private static byte[] ColorBlock(ushort c0, ushort c1, byte row0, byte row1 = 0, byte row2 = 0, byte row3 = 0)
    {
        return new byte[]
        {
            (byte)(c0 & 0xFF), (byte)(c0 >> 8),
            (byte)(c1 & 0xFF), (byte)(c1 >> 8),
            row0, row1, row2, row3,
        };
    }

    // Index row 0,1,2,3 across the top row of a block: 0b11_10_01_00.
    private const byte AllFourIndices = 0xE4;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void Palette_ReportsSize(int alphaDepth)
    {
        var image = BlpDecoder.Decode(PaletteFile((byte)alphaDepth));

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(16, image.Bgra.Length);
    }

    private static byte[] PaletteFile(byte alphaDepth)
    {
        byte[] indices = { 0, 1, 1, 0 };
        byte[] alpha = alphaDepth switch
        {
            1 => new byte[] { 0x0D },
            4 => new byte[] { 0x81, 0x0F },
            8 => new byte[] { 10, 20, 30, 40 },
            _ => Array.Empty<byte>(),
        };

        var payload = new byte[indices.Length + alpha.Length];
        Array.Copy(indices, payload, indices.Length);
        Array.Copy(alpha, 0, payload, indices.Length, alpha.Length);
        return Build(1, alphaDepth, 0, 2, 2, payload, TwoColorPalette());
    }

    [Fact]
    public void Palette_NoAlpha_IsOpaqueAndUsesPaletteColor()
    {
        var image = BlpDecoder.Decode(PaletteFile(0));

        Assert.Equal(new byte[] { 10, 20, 30, 255 }, Px(image, 0, 0));
        Assert.Equal(new byte[] { 40, 50, 60, 255 }, Px(image, 1, 0));
        Assert.Equal(new byte[] { 40, 50, 60, 255 }, Px(image, 0, 1));
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, Px(image, 1, 1));
    }

    [Fact]
    public void Palette_OneBitAlpha_IsLsbFirst()
    {
        // 0x0D = 0b1101: pixels 0, 2, 3 set, pixel 1 clear.
        var image = BlpDecoder.Decode(PaletteFile(1));

        Assert.Equal(255, Px(image, 0, 0)[3]);
        Assert.Equal(0, Px(image, 1, 0)[3]);
        Assert.Equal(255, Px(image, 0, 1)[3]);
        Assert.Equal(255, Px(image, 1, 1)[3]);
    }

    [Fact]
    public void Palette_FourBitAlpha_IsLowNibbleFirstAndScaled()
    {
        // Nibbles 1, 8, F, 0.
        var image = BlpDecoder.Decode(PaletteFile(4));

        Assert.Equal(17, Px(image, 0, 0)[3]);
        Assert.Equal(136, Px(image, 1, 0)[3]);
        Assert.Equal(255, Px(image, 0, 1)[3]);
        Assert.Equal(0, Px(image, 1, 1)[3]);
    }

    [Fact]
    public void Palette_EightBitAlpha_IsOneBytePerPixel()
    {
        var image = BlpDecoder.Decode(PaletteFile(8));

        Assert.Equal(new byte[] { 10, 20, 30, 10 }, Px(image, 0, 0));
        Assert.Equal(new byte[] { 40, 50, 60, 20 }, Px(image, 1, 0));
        Assert.Equal(new byte[] { 40, 50, 60, 30 }, Px(image, 0, 1));
        Assert.Equal(new byte[] { 10, 20, 30, 40 }, Px(image, 1, 1));
    }

    [Fact]
    public void Uncompressed_CopiesBgraStraight()
    {
        byte[] pixels = { 1, 2, 3, 4, 5, 6, 7, 8 };
        var image = BlpDecoder.Decode(Build(3, 8, 0, 2, 1, pixels));

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(pixels, image.Bgra);
    }

    [Fact]
    public void Dxt1_FourColorMode_InterpolatesBetweenEndpoints()
    {
        // c0 (red) > c1 (blue), so index 2 and 3 are the 2:1 and 1:2 blends.
        var file = Build(2, 0, 0, 4, 4, ColorBlock(Red565, Blue565, AllFourIndices));
        var image = BlpDecoder.Decode(file);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Px(image, 0, 0));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Px(image, 1, 0));
        Assert.Equal(new byte[] { 85, 0, 170, 255 }, Px(image, 2, 0));
        Assert.Equal(new byte[] { 170, 0, 85, 255 }, Px(image, 3, 0));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Px(image, 0, 1));
    }

    [Fact]
    public void Dxt1_ThreeColorMode_IndexThreeIsTransparentWhenTextureHasAlpha()
    {
        // c0 (blue) < c1 (red): three-color mode.
        var file = Build(2, 1, 0, 4, 4, ColorBlock(Blue565, Red565, AllFourIndices));
        var image = BlpDecoder.Decode(file);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Px(image, 0, 0));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Px(image, 1, 0));
        Assert.Equal(new byte[] { 127, 0, 127, 255 }, Px(image, 2, 0));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Px(image, 3, 0));
    }

    [Fact]
    public void Dxt1_ThreeColorMode_IndexThreeIsOpaqueBlackWhenTextureHasNoAlpha()
    {
        var file = Build(2, 0, 0, 4, 4, ColorBlock(Blue565, Red565, AllFourIndices));
        var image = BlpDecoder.Decode(file);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Px(image, 3, 0));
    }

    [Fact]
    public void Dxt1_SizeNotMultipleOfFour_ClipsEdgeBlocks()
    {
        // 5x3 needs two blocks across. Left block is all red, right block all blue; only the
        // first column of the right block is inside the image.
        var left = ColorBlock(Red565, 0, 0);
        var right = ColorBlock(Blue565, 0, 0);
        var payload = new byte[16];
        Array.Copy(left, 0, payload, 0, 8);
        Array.Copy(right, 0, payload, 8, 8);

        var image = BlpDecoder.Decode(Build(2, 0, 0, 5, 3, payload));

        Assert.Equal(5, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(5 * 3 * 4, image.Bgra.Length);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(new byte[] { 0, 0, 255, 255 }, Px(image, x, y));
            }

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Px(image, 4, y));
        }
    }

    [Fact]
    public void Dxt3_UsesExplicitAlphaAndAlwaysFourColorMode()
    {
        var payload = new byte[16];
        // Alpha nibbles, low first: pixel 0 = 0, pixel 1 = 8, everything else = F.
        for (int i = 0; i < 8; i++)
        {
            payload[i] = 0xFF;
        }

        payload[0] = 0x80;
        // c0 (blue) < c1 (red) would be three-color in DXT1, but DXT3 must still blend four.
        Array.Copy(ColorBlock(Blue565, Red565, AllFourIndices), 0, payload, 8, 8);

        var image = BlpDecoder.Decode(Build(2, 8, 1, 4, 4, payload));

        Assert.Equal(new byte[] { 255, 0, 0, 0 }, Px(image, 0, 0));
        Assert.Equal(new byte[] { 0, 0, 255, 136 }, Px(image, 1, 0));
        Assert.Equal(new byte[] { 170, 0, 85, 255 }, Px(image, 2, 0));
        Assert.Equal(new byte[] { 85, 0, 170, 255 }, Px(image, 3, 0));
    }

    [Fact]
    public void Dxt5_EightAlphaMode_InterpolatesWhenFirstAlphaIsLarger()
    {
        var payload = new byte[16];
        payload[0] = 255;
        payload[1] = 0;
        // Pixel alpha indices 0, 1, 2, 3 at 3 bits each = 0x688, little-endian.
        payload[2] = 0x88;
        payload[3] = 0x06;
        Array.Copy(ColorBlock(Red565, 0, 0), 0, payload, 8, 8);

        var image = BlpDecoder.Decode(Build(2, 8, 7, 4, 4, payload));

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Px(image, 0, 0));
        Assert.Equal(0, Px(image, 1, 0)[3]);
        Assert.Equal(218, Px(image, 2, 0)[3]);
        Assert.Equal(182, Px(image, 3, 0)[3]);
        Assert.Equal(255, Px(image, 0, 1)[3]);
    }

    [Fact]
    public void Dxt5_SixAlphaMode_AddsZeroAndOpaque()
    {
        var payload = new byte[16];
        payload[0] = 0;
        payload[1] = 255;
        // Pixel alpha indices 6, 7, 2, 1 = 0x2BE, little-endian.
        payload[2] = 0xBE;
        payload[3] = 0x02;
        Array.Copy(ColorBlock(Red565, 0, 0), 0, payload, 8, 8);

        var image = BlpDecoder.Decode(Build(2, 8, 7, 4, 4, payload));

        Assert.Equal(0, Px(image, 0, 0)[3]);
        Assert.Equal(255, Px(image, 1, 0)[3]);
        Assert.Equal(51, Px(image, 2, 0)[3]);
        Assert.Equal(255, Px(image, 3, 0)[3]);
    }

    [Fact]
    public void Rejects_Blp1Magic()
    {
        var file = Build(3, 8, 0, 1, 1, new byte[4]);
        file[3] = (byte)'1';

        Assert.Throws<InvalidDataException>(() => BlpDecoder.Decode(file));
    }

    [Fact]
    public void Rejects_UnknownMagic()
    {
        var file = Build(3, 8, 0, 1, 1, new byte[4]);
        file[0] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => BlpDecoder.Decode(file));
    }

    [Fact]
    public void Rejects_TruncatedHeader()
    {
        var file = Build(3, 8, 0, 1, 1, new byte[4]);

        Assert.Throws<InvalidDataException>(() => BlpDecoder.Decode(file.AsSpan(0, 100).ToArray()));
    }

    [Fact]
    public void Rejects_TruncatedPixelData()
    {
        var file = Build(3, 8, 0, 2, 1, new byte[8]);
        var cut = file.AsSpan(0, file.Length - 1).ToArray();

        Assert.Throws<InvalidDataException>(() => BlpDecoder.Decode(cut));
    }

    [Fact]
    public void Rejects_OffsetPastEnd()
    {
        var file = Build(3, 8, 0, 1, 1, new byte[4], offsetOverride: 0xFFFFFFF0u);

        Assert.Throws<InvalidDataException>(() => BlpDecoder.Decode(file));
    }

    [Fact]
    public void Rejects_UnknownEncoding()
    {
        var file = Build(9, 8, 0, 1, 1, new byte[4]);

        Assert.Throws<InvalidDataException>(() => BlpDecoder.Decode(file));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(8193, 1)]
    [InlineData(1, 8193)]
    public void Rejects_BadDimensions(int width, int height)
    {
        var file = Build(3, 8, 0, width, height, new byte[4]);

        Assert.Throws<InvalidDataException>(() => BlpDecoder.Decode(file));
    }
}

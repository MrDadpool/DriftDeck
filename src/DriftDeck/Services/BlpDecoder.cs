using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DriftDeck.Services;

/// <summary>
/// A decoded texture: straight-alpha BGRA32, row-major, stride = <c>Width * 4</c>.
/// </summary>
public sealed record BlpImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// Reads Blizzard's BLP2 texture format, because WPF cannot and a theme that uses art exported
/// from the user's own game client arrives as .blp. Only the full-size image (mip 0) is decoded;
/// the smaller mips are of no use to a panel that scales a single bitmap. Anything that is not a
/// well-formed BLP2 fails with <see cref="InvalidDataException"/> rather than a crash or a
/// half-decoded picture.
/// </summary>
public static class BlpDecoder
{
    private const int HeaderSize = 148;
    private const int PaletteOffset = 148;
    private const int PaletteBytes = 1024;
    private const int MaxDimension = 8192;

    private const int EncodingPalette = 1;
    private const int EncodingDxt = 2;
    private const int EncodingBgra = 3;

    private const int KindDxt1 = 1;
    private const int KindDxt3 = 3;
    private const int KindDxt5 = 5;

    /// <summary>
    /// Decodes BLP2 bytes into pixels. Kept free of WPF types so the tests can run it on plain
    /// byte arrays.
    /// </summary>
    public static BlpImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("The file is too small to be a BLP texture.");
        }

        if (data[0] != 'B' || data[1] != 'L' || data[2] != 'P')
        {
            throw new InvalidDataException("This is not a BLP texture (bad magic).");
        }

        if (data[3] != '2')
        {
            throw new InvalidDataException("Only BLP2 textures are supported (this is an older BLP version).");
        }

        int encoding = data[8];
        int alphaDepth = data[9];
        int alphaEncoding = data[10];

        uint rawWidth = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
        uint rawHeight = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));
        if (rawWidth == 0 || rawHeight == 0 || rawWidth > MaxDimension || rawHeight > MaxDimension)
        {
            throw new InvalidDataException("The texture has zero or unreasonably large dimensions.");
        }

        int width = (int)rawWidth;
        int height = (int)rawHeight;
        long pixelCount = (long)width * height;

        uint mipOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
        uint mipSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(84, 4));

        long needed;
        int dxtKind = 0;
        switch (encoding)
        {
            case EncodingPalette:
                if (alphaDepth != 0 && alphaDepth != 1 && alphaDepth != 4 && alphaDepth != 8)
                {
                    throw new InvalidDataException("The texture has an unsupported alpha depth.");
                }

                needed = pixelCount + ((pixelCount * alphaDepth) + 7) / 8;
                break;
            case EncodingDxt:
                if (alphaEncoding == 0)
                {
                    dxtKind = KindDxt1;
                }
                else if (alphaEncoding == 1)
                {
                    dxtKind = KindDxt3;
                }
                else if (alphaEncoding == 7)
                {
                    dxtKind = KindDxt5;
                }
                else
                {
                    throw new InvalidDataException("The texture uses an unsupported compression type.");
                }

                long blocks = (long)((width + 3) / 4) * ((height + 3) / 4);
                needed = blocks * (dxtKind == KindDxt1 ? 8 : 16);
                break;
            case EncodingBgra:
                needed = pixelCount * 4;
                break;
            default:
                throw new InvalidDataException("The texture uses an unsupported encoding.");
        }

        if (mipSize < needed || (long)mipOffset + needed > data.Length)
        {
            throw new InvalidDataException("The texture data is truncated or points past the end of the file.");
        }

        ReadOnlySpan<byte> mip = data.Slice((int)mipOffset, (int)needed);

        byte[] bgra;
        switch (encoding)
        {
            case EncodingPalette:
                if (data.Length < PaletteOffset + PaletteBytes)
                {
                    throw new InvalidDataException("The texture is missing its palette.");
                }

                bgra = DecodePalette(mip, data.Slice(PaletteOffset, PaletteBytes), width, height, alphaDepth);
                break;
            case EncodingDxt:
                bgra = DecodeDxt(mip, width, height, dxtKind, alphaDepth > 0);
                break;
            default:
                bgra = mip.ToArray();
                break;
        }

        return new BlpImage(width, height, bgra);
    }

    /// <summary>
    /// Reads a .blp file into a frozen bitmap. Frozen so it can be shared across threads and
    /// bound from any panel without WPF copying it.
    /// </summary>
    public static BitmapSource Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var image = Decode(bytes);
        var bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            image.Bgra,
            image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Palettized textures store one index byte per pixel and then a separate alpha plane, so the
    /// palette's own alpha byte is deliberately ignored.
    /// </summary>
    private static byte[] DecodePalette(
        ReadOnlySpan<byte> mip,
        ReadOnlySpan<byte> palette,
        int width,
        int height,
        int alphaDepth)
    {
        int count = width * height;
        var bgra = new byte[count * 4];
        ReadOnlySpan<byte> alphaPlane = mip.Slice(count);

        for (int i = 0; i < count; i++)
        {
            int p = mip[i] * 4;
            int o = i * 4;
            bgra[o] = palette[p];
            bgra[o + 1] = palette[p + 1];
            bgra[o + 2] = palette[p + 2];

            int alpha;
            switch (alphaDepth)
            {
                case 1:
                    alpha = ((alphaPlane[i >> 3] >> (i & 7)) & 1) != 0 ? 255 : 0;
                    break;
                case 4:
                    alpha = ((alphaPlane[i >> 1] >> ((i & 1) * 4)) & 0x0F) * 17;
                    break;
                case 8:
                    alpha = alphaPlane[i];
                    break;
                default:
                    alpha = 255;
                    break;
            }

            bgra[o + 3] = (byte)alpha;
        }

        return bgra;
    }

    private static byte[] DecodeDxt(ReadOnlySpan<byte> src, int width, int height, int kind, bool hasAlpha)
    {
        var bgra = new byte[width * height * 4];
        var colors = new byte[16];
        var alphas = new byte[8];
        int blockSize = kind == KindDxt1 ? 8 : 16;
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        int pos = 0;

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                ReadOnlySpan<byte> block = src.Slice(pos, blockSize);
                pos += blockSize;

                // DXT3/DXT5 put the color block after 8 bytes of alpha.
                ReadOnlySpan<byte> colorBlock = kind == KindDxt1 ? block : block.Slice(8);
                BuildColors(colorBlock, kind == KindDxt1, hasAlpha, colors);

                ulong alphaBits = 0;
                if (kind == KindDxt5)
                {
                    BuildAlphas(block[0], block[1], alphas);
                    for (int b = 0; b < 6; b++)
                    {
                        alphaBits |= (ulong)block[2 + b] << (8 * b);
                    }
                }

                for (int py = 0; py < 4; py++)
                {
                    int y = (by * 4) + py;
                    if (y >= height)
                    {
                        break;
                    }

                    int row = colorBlock[4 + py];
                    for (int px = 0; px < 4; px++)
                    {
                        int x = (bx * 4) + px;
                        if (x >= width)
                        {
                            break;
                        }

                        int ci = ((row >> (2 * px)) & 3) * 4;
                        int o = ((y * width) + x) * 4;
                        int i = (py * 4) + px;
                        bgra[o] = colors[ci];
                        bgra[o + 1] = colors[ci + 1];
                        bgra[o + 2] = colors[ci + 2];

                        if (kind == KindDxt3)
                        {
                            int packed = block[i >> 1];
                            int nibble = (i & 1) == 0 ? packed & 0x0F : packed >> 4;
                            bgra[o + 3] = (byte)(nibble * 17);
                        }
                        else if (kind == KindDxt5)
                        {
                            int index = (int)((alphaBits >> (3 * i)) & 7UL);
                            bgra[o + 3] = alphas[index];
                        }
                        else
                        {
                            bgra[o + 3] = colors[ci + 3];
                        }
                    }
                }
            }
        }

        return bgra;
    }

    /// <summary>
    /// Fills <paramref name="colors"/> with the four BGRA palette entries of a DXT color block.
    /// Only DXT1 may use the three-color mode; DXT3/DXT5 always interpolate four colors.
    /// </summary>
    private static void BuildColors(ReadOnlySpan<byte> block, bool isDxt1, bool hasAlpha, byte[] colors)
    {
        int c0 = block[0] | (block[1] << 8);
        int c1 = block[2] | (block[3] << 8);

        int r0 = Expand5((c0 >> 11) & 31);
        int g0 = Expand6((c0 >> 5) & 63);
        int b0 = Expand5(c0 & 31);
        int r1 = Expand5((c1 >> 11) & 31);
        int g1 = Expand6((c1 >> 5) & 63);
        int b1 = Expand5(c1 & 31);

        SetColor(colors, 0, b0, g0, r0, 255);
        SetColor(colors, 1, b1, g1, r1, 255);

        if (!isDxt1 || c0 > c1)
        {
            SetColor(colors, 2, ((2 * b0) + b1) / 3, ((2 * g0) + g1) / 3, ((2 * r0) + r1) / 3, 255);
            SetColor(colors, 3, (b0 + (2 * b1)) / 3, (g0 + (2 * g1)) / 3, (r0 + (2 * r1)) / 3, 255);
        }
        else
        {
            SetColor(colors, 2, (b0 + b1) / 2, (g0 + g1) / 2, (r0 + r1) / 2, 255);

            // Index 3 is "transparent" only when the texture says it carries alpha; otherwise it
            // is plain black.
            SetColor(colors, 3, 0, 0, 0, hasAlpha ? 0 : 255);
        }
    }

    private static void BuildAlphas(byte a0, byte a1, byte[] alphas)
    {
        alphas[0] = a0;
        alphas[1] = a1;
        if (a0 > a1)
        {
            for (int k = 2; k < 8; k++)
            {
                alphas[k] = (byte)((((8 - k) * a0) + ((k - 1) * a1)) / 7);
            }
        }
        else
        {
            for (int k = 2; k < 6; k++)
            {
                alphas[k] = (byte)((((6 - k) * a0) + ((k - 1) * a1)) / 5);
            }

            alphas[6] = 0;
            alphas[7] = 255;
        }
    }

    private static void SetColor(byte[] colors, int index, int b, int g, int r, int a)
    {
        int o = index * 4;
        colors[o] = (byte)b;
        colors[o + 1] = (byte)g;
        colors[o + 2] = (byte)r;
        colors[o + 3] = (byte)a;
    }

    private static int Expand5(int v) => (v << 3) | (v >> 2);

    private static int Expand6(int v) => (v << 2) | (v >> 4);
}

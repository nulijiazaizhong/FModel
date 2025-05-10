using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;

namespace FModel.Views.Snooper.Textures;

public class BitmapTexture : AbstractTexture
{
    private readonly byte[] _bytes;
    private readonly SKColorType _type;
    private readonly bool _isSrgb;

    private const int DisabledChannel = (int)BlendingFactor.Zero;
    private readonly bool[] _values = [true, true, true, true];
    private readonly string[] _labels = ["R", "G", "B", "A"];
    public int[] SwizzleMask =
    [
        (int) PixelFormat.Red,
        (int) PixelFormat.Green,
        (int) PixelFormat.Blue,
        (int) PixelFormat.Alpha
    ];

    public readonly string Path;
    public readonly EPixelFormat Format;

    public BitmapTexture(SKBitmap bitmap, UTexture texture) : base(TextureType.Normal)
    {
        Guid = texture.LightingGuid;
        Name = texture.Name;
        Width = bitmap.Width;
        Height = bitmap.Height;

        Path = texture.GetPathName();
        Format = texture.Format;

        _bytes = bitmap.Bytes;
        _type = bitmap.ColorType;
        _isSrgb = texture.SRGB;

        bitmap.Dispose();
    }

    public override void Setup()
    {
        base.Setup();

        var internalFormat = _type switch
        {
            SKColorType.Gray8 => PixelInternalFormat.R8,
            _ => _isSrgb ? PixelInternalFormat.Srgb : PixelInternalFormat.Rgb
        };

        var pixelFormat = _type switch
        {
            SKColorType.Gray8 => PixelFormat.Red,
            SKColorType.Bgra8888 => PixelFormat.Bgra,
            _ => PixelFormat.Rgba
        };

        GL.TexImage2D(Target, 0, internalFormat, Width, Height, 0, pixelFormat, PixelType.UnsignedByte, _bytes);
        GL.TexParameter(Target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(Target, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
        GL.TexParameter(Target, TextureParameterName.TextureBaseLevel, 0);
        GL.TexParameter(Target, TextureParameterName.TextureMaxLevel, 8);

        GL.TexParameter(Target, TextureParameterName.TextureSwizzleRgba, SwizzleMask);

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
    }

    protected override void ImGuiInspectorHeader()
    {
        base.ImGuiInspectorHeader();

        if (ImGui.BeginTable("texture_inspector", 2, ImGuiTableFlags.SizingStretchProp))
        {
            SnimGui.NoFramePaddingOnY(() =>
            {
                SnimGui.Layout("Type");ImGui.Text($" :  ({Format}) {Name}");
                SnimGui.TooltipCopy("(?) Click to Copy Path", Path);
                SnimGui.Layout("Guid");ImGui.Text($" :  {Guid.ToString(EGuidFormats.UniqueObjectGuid)}");
                SnimGui.Layout("Size");
                ImGui.Text($" :  {Width}x{Height}");

                SnimGui.Layout("Swizzle");
                for (int c = 0; c < SwizzleMask.Length; c++)
                {
                    if (ImGui.Checkbox(_labels[c], ref _values[c]))
                    {
                        Bind();
                        GL.TexParameter(Target, TextureParameterName.TextureSwizzleR + c, _values[c] ? SwizzleMask[c] : DisabledChannel);
                    }
                    ImGui.SameLine();
                }

                ImGui.EndTable();
            });
        }
    }

    public void FixChannels(string project)
    {
        switch (project)
        {
            // R: Whatever (AO / S / E / ...)
            // G: Roughness
            // B: Metallic
            case "GAMEFACE":
            case "HK_PROJECT":
            case "COSMICSHAKE":
            case "PHOENIX":
            case "ATOMICHEART":
            case "MULTIVERSUS":
            case "BODYCAM":
            {
                SwizzleMask =
                [
                    (int) PixelFormat.Red,
                    (int) PixelFormat.Blue,
                    (int) PixelFormat.Green,
                    (int) PixelFormat.Alpha
                ];
                break;
            }
            // R: Metallic
            // G: Roughness
            // B: Whatever (AO / S / E / ...)
            case "SHOOTERGAME":
            case "DIVINEKNOCKOUT":
            case "MOONMAN":
            {
                SwizzleMask =
                [
                    (int) PixelFormat.Blue,
                    (int) PixelFormat.Red,
                    (int) PixelFormat.Green,
                    (int) PixelFormat.Alpha
                ];
                break;
            }
            // R: Roughness
            // G: Metallic
            // B: Whatever (AO / S / E / ...)
            case "CCFF7R":
            case "PJ033":
            {
                SwizzleMask =
                [
                    (int) PixelFormat.Blue,
                    (int) PixelFormat.Green,
                    (int) PixelFormat.Red,
                    (int) PixelFormat.Alpha
                ];
                break;
            }
        }
    }
}

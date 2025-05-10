using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Core.Misc;
using OpenTK.Graphics.OpenGL4;

namespace FModel.Views.Snooper.Textures;

public class LinearColorTexture : AbstractTexture
{
    private FLinearColor _color;

    public LinearColorTexture(FLinearColor color) : base(TextureType.Normal)
    {
        _color = color;

        // Guid = new FGuid(_color.Hex);
        Name = _color.Hex;
        Width = 1;
        Height = 1;
    }

    public override void Setup()
    {
        base.Setup();

        GL.TexImage2D(Target, 0, PixelInternalFormat.Rgba, Width, Height, 0, PixelFormat.Rgba, PixelType.Float, ref _color);
        GL.TexParameter(Target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(Target, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
        GL.TexParameter(Target, TextureParameterName.TextureBaseLevel, 0);
        GL.TexParameter(Target, TextureParameterName.TextureMaxLevel, 8);

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
    }
}

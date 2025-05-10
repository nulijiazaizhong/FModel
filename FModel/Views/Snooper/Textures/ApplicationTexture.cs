using System;
using System.Windows;
using OpenTK.Graphics.OpenGL4;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FModel.Views.Snooper.Textures;

public class ApplicationTexture : AbstractTexture
{
    private readonly Image<Rgba32>[] _images;

    public ApplicationTexture(string texture) : base(TextureType.Normal)
    {
        _images = [LoadImage(texture)];
        Width = _images[0].Width;
        Height = _images[0].Height;
    }

    public ApplicationTexture(string[] textures) : base(TextureType.Cubemap)
    {
        _images = new Image<Rgba32>[textures.Length];
        for (var i = 0; i < _images.Length; i++)
        {
            _images[i] = LoadImage(textures[i]);
        }

        Width = _images[0].Width;
        Height = _images[0].Height;
    }

    public override void Setup()
    {
        base.Setup();

        var target = Type == TextureType.Cubemap ? TextureTarget.TextureCubeMapPositiveX : Target;
        GL.TexImage2D(target, 0, PixelInternalFormat.Rgba8, Width, Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        for (var i = 0; i < _images.Length; i++)
        {
            _images[i].ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    GL.TexSubImage2D(target + i, 0, 0, y, accessor.Width, 1, PixelFormat.Rgba, PixelType.UnsignedByte, accessor.GetRowSpan(y).ToArray());
                }
            });
        }

        if (Type == TextureType.Cubemap)
        {
            GL.TexParameter(Target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.LinearMipmapLinear);
        }
        else
        {
            GL.TexParameter(Target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.Linear);
        }

        GL.TexParameter(Target, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
        GL.TexParameter(Target, TextureParameterName.TextureWrapR, (int) TextureWrapMode.ClampToEdge);
        GL.TexParameter(Target, TextureParameterName.TextureWrapS, (int) TextureWrapMode.ClampToEdge);
        GL.TexParameter(Target, TextureParameterName.TextureWrapT, (int) TextureWrapMode.ClampToEdge);

        if (Type == TextureType.Cubemap)
        {
            GL.GenerateMipmap(GenerateMipmapTarget.TextureCubeMap);
        }
    }

    private Image<Rgba32> LoadImage(string texture)
    {
        var info = Application.GetResourceStream(new Uri($"/FModel;component/Resources/{texture}.png", UriKind.Relative));
        return Image.Load<Rgba32>(info.Stream);
    }
}

using System;
using OpenTK.Graphics.OpenGL4;

namespace FModel.Views.Snooper.Textures;

public class FramebufferTexture : AbstractTexture
{
    public FramebufferTexture(int width, int height) : base(TextureType.Framebuffer)
    {
        Width = width;
        Height = height;
    }

    public override void Setup()
    {
        base.Setup();

        GL.TexImage2D(Target, 0, PixelInternalFormat.Rgb, Width, Height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);

        GL.TexParameter(Target, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.Linear);
        GL.TexParameter(Target, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
        GL.TexParameter(Target, TextureParameterName.TextureWrapS, (int) TextureWrapMode.ClampToEdge);
        GL.TexParameter(Target, TextureParameterName.TextureWrapT, (int) TextureWrapMode.ClampToEdge);

        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, Target, Handle, 0);
    }
}

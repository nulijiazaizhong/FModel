using OpenTK.Graphics.OpenGL4;

namespace FModel.Views.Snooper.Textures;

public class MsaaTexture : AbstractTexture
{
    public MsaaTexture(uint width, uint height) : base(TextureType.MsaaFramebuffer)
    {
        Width = (int) width;
        Height = (int) height;
    }

    public override void Setup()
    {
        base.Setup();

        GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, Constants.SAMPLES_COUNT, PixelInternalFormat.Rgb, Width, Height, true);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, Target, Handle, 0);
    }
}

using System;
using CUE4Parse.UE4.Objects.Core.Misc;
using FModel.Views.Snooper.Models;
using OpenTK.Graphics.OpenGL4;

namespace FModel.Views.Snooper.Textures;

public interface ITexture : IDelayedSetup
{
    public int Handle { get; }
    public TextureType Type { get; }
    public TextureTarget Target { get; }

    public string Name { get; }
    public int Width { get; }
    public int Height { get; }

    public void Bind(TextureUnit unit);
    public void Bind(TextureTarget target);
    public IntPtr GetPointer();

    public void ImGuiInspector();
}

public enum TextureType
{
    Normal,
    Cubemap,
    Framebuffer,
    MsaaFramebuffer
}

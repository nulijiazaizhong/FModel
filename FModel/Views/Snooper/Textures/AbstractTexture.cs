using System;
using System.Numerics;
using CUE4Parse.UE4.Objects.Core.Misc;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;

namespace FModel.Views.Snooper.Textures;

public abstract class AbstractTexture : ITexture
{
    public int Handle { get; private set; }
    public TextureType Type { get; }
    public TextureTarget Target { get; }
    public FGuid Guid { get; protected init; }
    public string Name { get; protected init; }
    public int Width { get; protected set; }
    public int Height { get; protected set; }

    public AbstractTexture(TextureType type)
    {
        Type = type;
        Target = Type switch
        {
            TextureType.Cubemap => TextureTarget.TextureCubeMap,
            TextureType.MsaaFramebuffer => TextureTarget.Texture2DMultisample,
            _ => TextureTarget.Texture2D
        };

        Guid = FGuid.Random();
    }

    public virtual void Setup()
    {
        Handle = GL.GenTexture();
        Bind(TextureUnit.Texture0);
    }

    protected virtual void ImGuiInspectorHeader()
    {

    }

    public void Bind(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        Bind(Target);
    }

    public void Bind(TextureTarget target)
    {
        GL.BindTexture(target, Handle);
    }

    public void Bind()
    {
        GL.BindTexture(Target, Handle);
    }

    public IntPtr GetPointer() => Handle;

    public void WindowResized(int width, int height)
    {
        Width = width;
        Height = height;

        Bind();
        switch (Type)
        {
            case TextureType.MsaaFramebuffer:
                GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, Constants.SAMPLES_COUNT, PixelInternalFormat.Rgb, Width, Height, true);
                break;
            case TextureType.Framebuffer:
                GL.TexImage2D(Target, 0, PixelInternalFormat.Rgb, Width, Height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
                break;
            default:
                throw new NotSupportedException();
        }

        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, Target, Handle, 0);
    }

    public void Dispose()
    {
        GL.DeleteTexture(Handle);
    }

    private Vector3 _scrolling = new (0.0f, 0.0f, 1.0f);
    public void ImGuiInspector()
    {
        ImGuiInspectorHeader();

        var io = ImGui.GetIO();
        var canvasP0 = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();
        if (canvasSize.X < 50.0f) canvasSize.X = 50.0f;
        if (canvasSize.Y < 50.0f) canvasSize.Y = 50.0f;
        var canvasP1 = canvasP0 + canvasSize;
        var origin = new Vector2(canvasP0.X + _scrolling.X, canvasP0.Y + _scrolling.Y);
        var absoluteMiddle = canvasSize / 2.0f;

        ImGui.InvisibleButton("texture_inspector_canvas", canvasSize, ImGuiButtonFlags.MouseButtonLeft);
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _scrolling.X += io.MouseDelta.X;
            _scrolling.Y += io.MouseDelta.Y;
        }
        else if (ImGui.IsItemHovered() && io.MouseWheel != 0.0f)
        {
            var zoomFactor = 1.0f + io.MouseWheel * 0.1f;
            var mousePosCanvas = io.MousePos - origin;

            _scrolling.X -= (mousePosCanvas.X - absoluteMiddle.X) * (zoomFactor - 1);
            _scrolling.Y -= (mousePosCanvas.Y - absoluteMiddle.Y) * (zoomFactor - 1);
            _scrolling.Z *= zoomFactor;
            origin = new Vector2(canvasP0.X + _scrolling.X, canvasP0.Y + _scrolling.Y);
        }

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(canvasP0, canvasP1, 0xFF242424);
        drawList.PushClipRect(canvasP0, canvasP1, true);
        {
            var sensitivity = _scrolling.Z * 25.0f;
            for (float x = _scrolling.X % sensitivity; x < canvasSize.X; x += sensitivity)
                drawList.AddLine(canvasP0 with { X = canvasP0.X + x }, canvasP1 with { X = canvasP0.X + x }, 0x28C8C8C8);
            for (float y = _scrolling.Y % sensitivity; y < canvasSize.Y; y += sensitivity)
                drawList.AddLine(canvasP0 with { Y = canvasP0.Y + y }, canvasP1 with { Y = canvasP0.Y + y }, 0x28C8C8C8);
        }
        drawList.PopClipRect();

        drawList.PushClipRect(canvasP0, canvasP1, true);
        {
            var relativeMiddle = origin + absoluteMiddle;
            var ratio = Math.Min(canvasSize.X / Width, canvasSize.Y / Height) * 0.95f * _scrolling.Z;
            var size = new Vector2(Width, Height) * ratio / 2f;

            drawList.AddImage(GetPointer(), relativeMiddle - size, relativeMiddle + size);
            drawList.AddRect(relativeMiddle - size, relativeMiddle + size, 0xFFFFFFFF);
        }
        drawList.PopClipRect();
    }
}

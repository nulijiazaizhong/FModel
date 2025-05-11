using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Core.Misc;
using FModel.Extensions;
using FModel.Settings;
using FModel.Views.Snooper.Models;
using FModel.Views.Snooper.Textures;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;

namespace FModel.Views.Snooper.Shading;

public class Material : IDisposable
{
    private int _handle;

    public readonly CMaterialParams2 Parameters;
    public string Name;
    public string Path;
    public int SelectedChannel;
    public int SelectedTexture;
    public bool IsUsed;

    public FGuid[] Diffuse;
    public FGuid[] Normals;
    public FGuid[] SpecularMasks;
    public FGuid[] Emissive;

    public Vector4[] DiffuseColor;
    public Vector4[] EmissiveColor;
    public Vector4 EmissiveRegion;

    public AoParams Ao;
    public bool HasAo;

    public float RoughnessMin = 0f;
    public float RoughnessMax = 1f;
    public float EmissiveMult = 1f;

    public Material()
    {
        Parameters = new CMaterialParams2();
        Name = "";
        Path = "None";
        IsUsed = false;

        Diffuse = [];
        Normals = [];
        SpecularMasks = [];
        Emissive = [];

        DiffuseColor = [];
        EmissiveColor = [];
        EmissiveRegion = new Vector4(0, 0, 1, 1);
    }

    public Material(UMaterialInterface unrealMaterial) : this()
    {
        SwapMaterial(unrealMaterial);
    }

    public void SwapMaterial(UMaterialInterface unrealMaterial)
    {
        Name = unrealMaterial.Name;
        Path = unrealMaterial.GetPathName();
        unrealMaterial.GetParams(Parameters, UserSettings.Default.MaterialExportFormat);
    }

    public void Validate(int uvCount)
    {
        if (uvCount < 1 || Parameters.IsNull)
        {
            Diffuse = [new LinearColorTexture(FLinearColor.Gray).Guid];
            Normals = [new LinearColorTexture(new FLinearColor(0.5f, 0.5f, 1f, 1f)).Guid];
            SpecularMasks = [];
            Emissive = [];
            DiffuseColor = FillColors(1,CMaterialParams2.DiffuseColors, Vector4.One);
            EmissiveColor = [Vector4.One];
        }
        else
        {
            // textures
            Diffuse = FillTextures(uvCount, Parameters.HasTopDiffuse, CMaterialParams2.Diffuse, CMaterialParams2.FallbackDiffuse, true);
            Normals = FillTextures(uvCount, Parameters.HasTopNormals, CMaterialParams2.Normals, CMaterialParams2.FallbackNormals);
            SpecularMasks = FillTextures(uvCount, Parameters.HasTopSpecularMasks, CMaterialParams2.SpecularMasks, CMaterialParams2.FallbackSpecularMasks);
            Emissive = FillTextures(uvCount, true, CMaterialParams2.Emissive, CMaterialParams2.FallbackEmissive);

            // colors
            DiffuseColor = FillColors(Diffuse.Length, CMaterialParams2.DiffuseColors, Vector4.One);
            EmissiveColor = FillColors(Emissive.Length,CMaterialParams2.EmissiveColors, Vector4.One);

            {   // ambient occlusion + color boost
                if (Parameters.TryGetTexture2d(out var original, "M", "AEM", "AO") && !original.Name.Equals("T_BlackMask"))
                {
                    AssetPool.Get().AddTexture(original, false);

                    HasAo = true;
                    Ao = new AoParams { Texture = original.LightingGuid };
                    if (Parameters.TryGetLinearColor(out var l, "Skin Boost Color And Exponent"))
                    {
                        Ao.HasColorBoost = true;
                        Ao.ColorBoost = new Boost { Color = new Vector3(l.R, l.G, l.B), Exponent = l.A };
                    }
                }

                if (Parameters.TryGetScalar(out var roughnessMin, "RoughnessMin", "SpecRoughnessMin"))
                    RoughnessMin = roughnessMin;
                if (Parameters.TryGetScalar(out var roughnessMax, "RoughnessMax", "SpecRoughnessMax"))
                    RoughnessMax = roughnessMax;
                if (Parameters.TryGetScalar(out var roughness, "Rough", "Roughness", "Ro Multiplier", "RO_mul", "Roughness_Mult"))
                {
                    var d = roughness / 2;
                    RoughnessMin = roughness - d;
                    RoughnessMax = roughness + d;
                }

                if (Parameters.TryGetScalar(out var emissiveMultScalar, "emissive mult", "Emissive_Mult", "EmissiveIntensity", "EmissionIntensity"))
                    EmissiveMult = emissiveMultScalar;
                else if (Parameters.TryGetLinearColor(out var emissiveMultColor, "Emissive Multiplier", "EmissiveMultiplier"))
                    EmissiveMult = emissiveMultColor.R;

                if (Parameters.TryGetLinearColor(out var EmissiveUVs,
                        "EmissiveUVs_RG_UpperLeftCorner_BA_LowerRightCorner",
                        "Emissive Texture UVs RG_TopLeft BA_BottomRight",
                        "Emissive 2 UV Positioning (RG)UpperLeft (BA)LowerRight",
                        "EmissiveUVPositioning (RG)UpperLeft (BA)LowerRight"))
                    EmissiveRegion = new Vector4(EmissiveUVs.R, EmissiveUVs.G, EmissiveUVs.B, EmissiveUVs.A);

                if ((Parameters.TryGetSwitch(out var swizzleRoughnessToGreen, "SwizzleRoughnessToGreen") && swizzleRoughnessToGreen) || Parameters.Textures.ContainsKey("SRM"))
                {
                    foreach (var specMask in SpecularMasks)
                    {
                        if (AssetPool.Get().TryGetBitmap(specMask, out var bitmap))
                        {
                            bitmap.SwizzleMask =
                            [
                                (int) PixelFormat.Red,
                                (int) PixelFormat.Blue,
                                (int) PixelFormat.Green,
                                (int) PixelFormat.Alpha
                            ];
                        }
                    }
                }
            }
        }
    }

    public void Setup(bool broken)
    {
        _handle = GL.CreateProgram();
    }

    /// <param name="uvCount">number of item in the array</param>
    /// <param name="top">has at least 1 clearly defined texture, else will go straight to fallback</param>
    /// <param name="triggers">list of texture parameter names by uv channel</param>
    /// <param name="fallback">fallback texture name to use if no top texture found</param>
    /// <param name="first">if no top texture, no fallback texture, then use the first texture found</param>
    private FGuid[] FillTextures(int uvCount, bool top, string[][] triggers, string fallback, bool first = false)
    {
        UTexture original;
        var fix = fallback == CMaterialParams2.FallbackSpecularMasks;
        var textures = new FGuid[uvCount];

        if (top)
        {
            for (int i = 0; i < textures.Length; i++)
            {
                if (Parameters.TryGetTexture2d(out original, triggers[i]))
                {
                    AssetPool.Get().AddTexture(original, fix);
                    textures[i] = original.LightingGuid;
                }
                else if (i > 0)
                    textures[i] = textures[i - 1];
            }
        }
        else if (Parameters.TryGetTexture2d(out original, fallback))
        {
            AssetPool.Get().AddTexture(original, fix);
            for (int i = 0; i < textures.Length; i++)
                textures[i] = original.LightingGuid;
        }
        else if (first && Parameters.TryGetFirstTexture2d(out original))
        {
            AssetPool.Get().AddTexture(original, fix);
            for (int i = 0; i < textures.Length; i++)
                textures[i] = original.LightingGuid;
        }
        return textures;
    }

    /// <param name="uvCount">number of item in the array</param>
    /// <param name="triggers">list of color parameter names by uv channel</param>
    /// <param name="fallback">fallback color to use if no trigger was found</param>
    private Vector4[] FillColors(int uvCount, string[][] triggers, Vector4 fallback)
    {
        var colors = new Vector4[uvCount];
        for (int i = 0; i < colors.Length; i++)
        {
            if (Parameters.TryGetLinearColor(out var color, triggers[i]) && color is { A: > 0 })
            {
                colors[i] = new Vector4(color.R, color.G, color.B, color.A);
            }
            else colors[i] = fallback;
        }
        return colors;
    }

    public void Render(Shader shader)
    {
        var unit = 0;
        ITexture bitmap;

        for (var i = 0; i < Diffuse.Length; i++)
        {
            shader.SetUniform($"uParameters.Diffuse[{i}].Color", DiffuseColor[i]);
            if (Diffuse[i].IsValid() && AssetPool.Get().TryGetTexture(Diffuse[i], out bitmap))
            {
                shader.SetUniform($"uParameters.Diffuse[{i}].Sampler", unit);
                bitmap?.Bind(TextureUnit.Texture0 + unit++);
            }
        }

        for (var i = 0; i < Normals.Length; i++)
        {
            if (Normals[i].IsValid() && AssetPool.Get().TryGetTexture(Normals[i], out bitmap))
            {
                shader.SetUniform($"uParameters.Normals[{i}].Sampler", unit);
                bitmap?.Bind(TextureUnit.Texture0 + unit++);
            }
        }

        for (var i = 0; i < SpecularMasks.Length; i++)
        {
            if (SpecularMasks[i].IsValid() && AssetPool.Get().TryGetTexture(SpecularMasks[i], out bitmap))
            {
                shader.SetUniform($"uParameters.SpecularMasks[{i}].Sampler", unit);
                bitmap?.Bind(TextureUnit.Texture0 + unit++);
            }
        }

        // for (var i = 0; i < Emissive.Length; i++)
        // {
        //     if (Emissive[i].IsValid() && AssetPool.Get().TryGetTexture(Emissive[i], out bitmap))
        //     {
        //         shader.SetUniform($"uParameters.Emissive[{i}].Color", EmissiveColor[i]);
        //         shader.SetUniform($"uParameters.Emissive[{i}].Sampler", unit);
        //         bitmap?.Bind(TextureUnit.Texture0 + unit++);
        //     }
        // }

        if (Ao.Texture.IsValid() && AssetPool.Get().TryGetTexture(Ao.Texture, out bitmap))
        {
            bitmap?.Bind(TextureUnit.Texture31);
            shader.SetUniform("uParameters.Ao.Sampler", 31);
            shader.SetUniform("uParameters.Ao.HasColorBoost", Ao.HasColorBoost);
            shader.SetUniform("uParameters.Ao.ColorBoost.Color", Ao.ColorBoost.Color);
            shader.SetUniform("uParameters.Ao.ColorBoost.Exponent", Ao.ColorBoost.Exponent);
            shader.SetUniform("uParameters.HasAo", HasAo);
        }

        shader.SetUniform("uParameters.EmissiveRegion", EmissiveRegion);
        shader.SetUniform("uParameters.RoughnessMin", RoughnessMin);
        shader.SetUniform("uParameters.RoughnessMax", RoughnessMax);
        shader.SetUniform("uParameters.EmissiveMult", EmissiveMult);
    }

    private const string _mult = "x %.2f";
    private const float _step = 0.01f;
    private const float _zero = 0.000001f; // doesn't actually work if _infinite is used as max value /shrug
    private const float _infinite = 0.0f;
    private const ImGuiSliderFlags _clamp = ImGuiSliderFlags.AlwaysClamp;
    public void ImGuiParameters()
    {
        if (ImGui.BeginTable("parameters", 2))
        {
            var id = 1;
            SnimGui.Layout("Roughness Min");ImGui.PushID(id++);
            ImGui.DragFloat("", ref RoughnessMin, _step, _zero, 1.0f, _mult, _clamp);
            ImGui.PopID();SnimGui.Layout("Roughness Max");ImGui.PushID(id++);
            ImGui.DragFloat("", ref RoughnessMax, _step, _zero, 1.0f, _mult, _clamp);
            ImGui.PopID();SnimGui.Layout("Emissive Multiplier");ImGui.PushID(id++);
            ImGui.DragFloat("", ref EmissiveMult, _step, _zero, _infinite, _mult, _clamp);
            ImGui.PopID();

            if (HasAo && Ao.HasColorBoost)
            {
                SnimGui.Layout("Color Boost");ImGui.PushID(id++);
                ImGui.ColorEdit3("", ref Ao.ColorBoost.Color);ImGui.PopID();
                SnimGui.Layout("Color Boost Exponent");ImGui.PushID(id++);
                ImGui.DragFloat("", ref Ao.ColorBoost.Exponent, _step, _zero, _infinite, _mult, _clamp);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
    }

    public void ImGuiBaseProperties(string id)
    {
        if (ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingStretchProp))
        {
            Layout("Blend", Parameters.BlendMode.GetDescription(), true, true);
            Layout("Shading", Parameters.ShadingModel.GetDescription(), true, true);
            ImGui.EndTable();
        }
    }

    public void ImGuiDictionaries<T>(string id, Dictionary<string, T> dictionary, bool center = false, bool wrap = false)
    {
        if (ImGui.BeginTable(id, 2))
        {
            foreach ((string key, T value) in dictionary.Reverse())
            {
                Layout(key, value, center, wrap);
            }
            ImGui.EndTable();
        }
    }

    public void ImGuiColors(Dictionary<string, FLinearColor> colors)
    {
        foreach ((string key, FLinearColor value) in colors.Reverse())
        {
            ImGui.ColorButton(key, new Vector4(value.R, value.G, value.B, value.A), ImGuiColorEditFlags.None, new Vector2(16));
            ImGui.SameLine();ImGui.Text(key);SnimGui.TooltipCopy(key);
        }
    }

    public bool ImGuiTextures(Dictionary<string, ITexture> icons, UModel model)
    {
        if (ImGui.BeginTable("material_textures", 2))
        {
            SnimGui.Layout("Channel");ImGui.PushID(1); ImGui.BeginDisabled(model.UvCount < 2);
            ImGui.DragInt("", ref SelectedChannel, _step, 0, model.UvCount - 1, "UV %i", ImGuiSliderFlags.AlwaysClamp);
            ImGui.EndDisabled();ImGui.PopID();SnimGui.Layout("Type");ImGui.PushID(2);
            ImGui.Combo("texture_type", ref SelectedTexture, "Diffuse\0Normals\0Specular\0Ambient Occlusion\0Emissive\0");
            ImGui.PopID();

            switch (SelectedTexture)
            {
                case 0 when DiffuseColor.Length > 0:
                    SnimGui.Layout("Color");ImGui.PushID(3);
                    ImGui.ColorEdit4("", ref DiffuseColor[SelectedChannel], ImGuiColorEditFlags.NoAlpha);
                    ImGui.PopID();
                    break;
                case 4 when EmissiveColor.Length > 0:
                    SnimGui.Layout("Color");ImGui.PushID(3);
                    ImGui.ColorEdit4("", ref EmissiveColor[SelectedChannel], ImGuiColorEditFlags.NoAlpha);
                    ImGui.PopID();SnimGui.Layout("Region");ImGui.PushID(4);
                    ImGui.DragFloat4("", ref EmissiveRegion, _step, _zero, 1.0f, "%.2f", _clamp);
                    ImGui.PopID();
                    break;
            }

            ImGui.EndTable();
        }

        if (GetSelectedTexture() is not { } g || !AssetPool.Get().TryGetTexture(g, out var texture))
        {
            texture = icons["noimage"];
        }
        ImGui.Image(texture.GetPointer(),
            new Vector2(ImGui.GetContentRegionAvail().X - ImGui.GetScrollX()),
            Vector2.Zero, Vector2.One);
        return ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
    }

    public FGuid? GetSelectedTexture()
    {
        return SelectedTexture switch
        {
            0 when Diffuse.Length > 0 => Diffuse[SelectedChannel],
            1 when Normals.Length > 0 => Normals[SelectedChannel],
            2 when SpecularMasks.Length > 0 => SpecularMasks[SelectedChannel],
            3 when HasAo => Ao.Texture,
            4 when Emissive.Length > 0 => Emissive[SelectedChannel],
            _ => null
        };
    }

    private void Layout<T>(string key, T value, bool center = false, bool wrap = false)
    {
        SnimGui.Layout(key, true);
        var text = $"{value:N}";
        if (center) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetColumnWidth() - ImGui.CalcTextSize(text).X) / 2);
        if (wrap) ImGui.TextWrapped(text); else ImGui.Text(text);
        SnimGui.TooltipCopy(text);
    }

    public void Dispose()
    {
        GL.DeleteProgram(_handle);
    }
}

public struct AoParams
{
    public FGuid Texture;

    public Boost ColorBoost;
    public bool HasColorBoost;
}

public struct Boost
{
    public Vector3 Color;
    public float Exponent;
}

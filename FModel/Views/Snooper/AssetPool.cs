using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Textures;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Component.SplineMesh;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using FModel.Settings;
using FModel.Views.Snooper.Models;
using FModel.Views.Snooper.Shading;
using FModel.Views.Snooper.Textures;

namespace FModel.Views.Snooper;

public class AssetPool
{
    public static AssetPool Get() => _singleton ??= new AssetPool();
    private static AssetPool _singleton;

    public FGuid SelectedModel { get; private set; }

    private readonly object _lock = new ();
    public readonly ConcurrentBag<IDelayedSetup> Queue;
    public readonly Dictionary<FGuid, UModel> Models;
    public readonly Dictionary<FGuid, ITexture> Textures;

    private readonly string _project;

    private AssetPool()
    {
        Queue = [];
        Models = [];
        Textures = [];

        _project = Services.ApplicationService.ApplicationView.CUE4Parse.Provider.ProjectName.ToUpper();
    }

    public void OnTick()
    {
        while (Queue.TryTake(out var asset))
        {
            asset.Setup();
            switch (asset)
            {
                case UModel uModel:
                    Models.Add(asset.Guid, uModel);
                    break;
                case ITexture texture:
                    Textures.Add(asset.Guid, texture);
                    break;
            }

            break; // debug
        }

        foreach (var model in Models.Values)
        {
            model.Update();
        }
    }

    public void AddModel(UStaticMesh staticMesh)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var guid = staticMesh.LightingGuid;
            lock (_lock)
            {
                if (TryGetModel(guid, out var model))
                {
                    model.AddInstance(Transform.Identity);
                }
                else if (staticMesh.TryConvert(out var mesh))
                {
                    model = new StaticModel(staticMesh, mesh) { Guid = guid };
                    model.ScanMaterials();
                    Queue.Add(model);
                }
            }
        });
    }
    public void AddModel(USkeletalMesh skeletalMesh)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var guid = new FGuid((uint) skeletalMesh.GetFullName().GetHashCode());
            lock (_lock)
            {
                if (!TryGetModel(guid, out var _) && skeletalMesh.TryConvert(out var mesh))
                {
                    var model = new SkeletalModel(skeletalMesh, mesh) { Guid = guid };
                    model.ScanMaterials();
                    Queue.Add(model);
                }
            }
        });
    }
    public void AddModel(USkeleton skeleton)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var guid = skeleton.Guid;
            lock (_lock)
            {
                if (!TryGetModel(guid, out var _) && skeleton.TryConvert(out var _, out var box))
                {
                    var model = new SkeletalModel(skeleton, box) { Guid = guid };
                    model.ScanMaterials();
                    Queue.Add(model);
                }
            }
        });
    }
    public void AddTexture(UTexture texture, bool fix)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var guid = texture.LightingGuid;
            lock (_lock)
            {
                if (!TryGetBitmap(guid, out var _) && texture.Format != EPixelFormat.PF_BC6H) // BC6H is not supported by Decode thus randomly crashes the app
                {
                    var bitmap = texture switch
                    {
                        UTexture2D texture2D => texture2D.Decode(UserSettings.Default.PreviewMaxTextureSize, UserSettings.Default.CurrentDir.TexturePlatform),
                        UTexture2DArray texture2DArray => texture2DArray.DecodeTextureArray(UserSettings.Default.CurrentDir.TexturePlatform)?.FirstOrDefault(),
                        _ => texture.Decode(UserSettings.Default.CurrentDir.TexturePlatform)
                    };

                    if (bitmap is not null)
                    {
                        var t = new BitmapTexture(bitmap.ToSkBitmap(), texture);
                        if (fix) t.FixChannels(_project);

                        Queue.Add(t);
                    }
                }
            }
        });
    }

    public void AddModel(IPropertyHolder actor, UObject staticMeshComponent, UStaticMesh staticMesh, Transform transform)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var bSpline = staticMeshComponent is USplineMeshComponent;
            var guid = staticMesh.LightingGuid;
            lock (_lock)
            {
                if (TryGetModel(guid, out var model))
                {
                    model.AddInstance(transform);
                    if (bSpline && model is SplineModel splineModel)
                        splineModel.AddComponent((USplineMeshComponent)staticMeshComponent);
                }
                else if (staticMesh.TryConvert(out var mesh))
                {
                    model = bSpline ? new SplineModel(staticMesh, mesh, (USplineMeshComponent)staticMeshComponent, transform) : new StaticModel(staticMesh, mesh, transform);
                    model.IsTwoSided = actor.GetOrDefault("bMirrored", staticMeshComponent.GetOrDefault("bDisallowMeshPaintPerInstance", model.IsTwoSided));
                    model.Guid = guid;

                    if (actor.TryGetAllValues(out FPackageIndex[] textureData, "TextureData"))
                    {
                        var material = model.Materials.FirstOrDefault();
                        if (material is { IsUsed: true })
                        {
                            for (int j = 0; j < textureData.Length; j++)
                            {
                                if (textureData[j]?.Load() is not { } textureDataIdx)
                                    continue;

                                if (textureDataIdx.TryGetValue(out FPackageIndex overrideMaterial, "OverrideMaterial") &&
                                    overrideMaterial.TryLoad(out var oMaterial) && oMaterial is UMaterialInterface oUnrealMaterial)
                                    material.SwapMaterial(oUnrealMaterial);

                                WorldTextureData(material, textureDataIdx, "Diffuse", j switch
                                {
                                    0 => "Diffuse",
                                    > 0 => $"Diffuse_Texture_{j + 1}",
                                    _ => CMaterialParams2.FallbackDiffuse
                                });
                                WorldTextureData(material, textureDataIdx, "Normal", j switch
                                {
                                    0 => "Normals",
                                    > 0 => $"Normals_Texture_{j + 1}",
                                    _ => CMaterialParams2.FallbackNormals
                                });
                                WorldTextureData(material, textureDataIdx, "Specular", j switch
                                {
                                    0 => "SpecularMasks",
                                    > 0 => $"SpecularMasks_{j + 1}",
                                    _ => CMaterialParams2.FallbackNormals
                                });
                            }
                        }
                    }

                    if (staticMeshComponent.TryGetValue(out FPackageIndex[] overrideMaterials, "OverrideMaterials"))
                    {
                        for (var j = 0; j < overrideMaterials.Length && j < model.Sections.Length; j++)
                        {
                            var matIndex = model.Sections[j].MaterialIndex;
                            if (matIndex < 0 || matIndex >= model.Materials.Length || matIndex >= overrideMaterials.Length ||
                                overrideMaterials[matIndex].Load() is not UMaterialInterface unrealMaterial) continue;

                            model.Materials[matIndex].SwapMaterial(unrealMaterial);
                        }
                    }

                    model.ScanMaterials();
                    Queue.Add(model);
                }
            }
        });

        void WorldTextureData(Material material, UObject textureData, string name, string key)
        {
            if (textureData.TryGetValue(out FPackageIndex package, name) && package.Load() is UTexture2D texture)
                material.Parameters.Textures[key] = texture;
        }
    }

    private bool TryGet<T>(FGuid guid, [MaybeNullWhen(true)] out T asset)
    {
        foreach (var a in Queue)
        {
            if (a.Guid == guid && a is T value)
            {
                asset = value;
                return true;
            }
        }

        if (Models.TryGetValue(guid, out var m) && m is T model)
        {
            asset = model;
            return true;
        }

        if (Textures.TryGetValue(guid, out var t) && t is T texture)
        {
            asset = texture;
            return true;
        }

        asset = default;
        return false;
    }
    public bool TryGetModel([MaybeNullWhen(false)] out UModel model) => TryGet(SelectedModel, out model);
    public bool TryGetModel(FGuid guid, [MaybeNullWhen(false)] out UModel model) => TryGet(guid, out model);
    public bool TryGetTexture(FGuid guid, [MaybeNullWhen(false)] out ITexture texture) => TryGet(guid, out texture);
    public bool TryGetBitmap(FGuid guid, [MaybeNullWhen(false)] out BitmapTexture texture) => TryGet(guid, out texture);

    public void SelectModel(FGuid guid)
    {
        // unselect old
        if (TryGetModel(out var model))
            model.IsSelected = false;

        // select new
        if (!TryGetModel(guid, out model))
            SelectedModel = Guid.Empty;
        else
        {
            model.IsSelected = true;
            SelectedModel = guid;
        }

        // SelectedSection = 0;
        // SelectedMorph = 0;
    }
}

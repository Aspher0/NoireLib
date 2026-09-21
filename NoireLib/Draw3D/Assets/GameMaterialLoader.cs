using NoireLib.Draw3D.Materials;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Draw3D.Assets;

/// <summary>A game material resolved into its parsed file and loaded textures. Owns the textures.</summary>
public sealed class GameMaterial : IDisposable
{
    private bool disposed;

    internal GameMaterial(GameMaterialFile file, GpuTexture? baseColor, GpuTexture? normal, GpuTexture? specular)
    {
        File = file;
        BaseColor = baseColor;
        Normal = normal;
        Specular = specular;
    }

    /// <summary>The parsed material file.</summary>
    public GameMaterialFile File { get; }

    /// <summary>The shader package of dyeable furniture, the one package whose color map alpha is a stain mask.</summary>
    public const string DyeableFurnitureShader = "bgcolorchange.shpk";

    /// <summary>Whether this material uses <see cref="DyeableFurnitureShader"/>, whose color map alpha masks the stain.</summary>
    public bool IsDyeableFurniture => File.ShaderPackage == DyeableFurnitureShader;

    /// <summary>The display color of stain row 1 (Snow White), rendered on a dyeable surface when no stain or scene default is stated.</summary>
    public static readonly Vector3 UndyedStain = new(228f / 255f, 223f / 255f, 208f / 255f);

    /// <summary>The base color texture, or null when the material names none or it failed to load.</summary>
    public GpuTexture? BaseColor { get; }

    /// <summary>The normal map (tangent-space normal in red and green), or null when the material names none.</summary>
    public GpuTexture? Normal { get; }

    /// <summary>The specular map, or null when the material names none.</summary>
    public GpuTexture? Specular { get; }

    /// <summary>The material's <c>g_DiffuseColor</c> constant, or null. On dyeable furniture see <see cref="UndyedStain"/>.</summary>
    public Vector3? DiffuseColor
    {
        get
        {
            var values = File.ConstantValue("g_DiffuseColor");
            return values is { Length: >= 3 } ? new Vector3(values[0], values[1], values[2]) : null;
        }
    }

    /// <summary>
    /// Builds the opaque, lit material this asset is normally drawn with, confining any dye to the color map's alpha mask.<br/>
    /// Falls back to <see cref="ToLit"/> without a base color texture or with <see cref="GameMaterialPipeline.Unavailable"/> set.
    /// </summary>
    /// <param name="dye">Display color applied to the dyeable area. Null renders <see cref="UndyedStain"/> on dyeable furniture.</param>
    /// <param name="tint">Color multiplied over the whole surface. White leaves it untouched.</param>
    /// <param name="normalStrength">Normal map strength (0 is the geometric normal alone).</param>
    /// <param name="specularStrength">Specular highlight strength. Off by default: measured background surfaces are matte.</param>
    /// <param name="dyeReference">Authored color the dye lands on exactly, or 0 to multiply the authored color like the game.</param>
    /// <param name="ignoreSceneLight">Whether to skip this renderer's lighting and show the texture and dye colors alone.</param>
    public Material ToGameShaded(
        Vector3? dye = null,
        Vector4? tint = null,
        float normalStrength = 1f,
        float specularStrength = 0f,
        float dyeReference = 0f,
        bool ignoreSceneLight = false)
    {
        // Callers rebuild when GameMaterialPipeline.Ready turns true.
        if (BaseColor is null || !GameMaterialPipeline.EnsureRegistered())
            return ToLit(tint);

        // An empty stain slot on a dyeable surface renders the undyed default.
        var applied = dye ?? (IsDyeableFurniture ? UndyedStain : (Vector3?)null);
        var color = ColorHelper.SrgbToLinear(applied ?? Vector3.One);
        var strength = applied is null ? 0f : 1f;

        // The shader never samples an unbound slot.
        var normal = Normal is null ? 0f : Math.Max(normalStrength, 0f);
        var specular = Specular is null ? 0f : Math.Max(specularStrength, 0f);

        return Material.Custom(
            GameMaterialPipeline.Name,
            tint ?? Vector4.One,
            BlendMode.Opaque,
            BaseColor,
            Normal,
            Specular)
            with
        {
            ShapeParams = new Vector4(color.X, color.Y, color.Z, strength),
            SurfaceParams = new Vector4(normal, specular, Math.Max(dyeReference, 0f), ignoreSceneLight ? 1f : 0f),
        };
    }

    /// <summary>Builds a lit material that draws with this material's base color texture.</summary>
    /// <param name="tint">Multiplied over the material's color. White leaves it untouched.</param>
    /// <param name="applyDiffuseColor">Whether to multiply <see cref="DiffuseColor"/> over every pixel.</param>
    public Material ToLit(Vector4? tint = null, bool applyDiffuseColor = false)
    {
        var color = ResolveColor(tint, applyDiffuseColor);
        return BaseColor is null ? Material.Lit(color) : Material.Lit(color) with { Texture = BaseColor };
    }

    /// <summary>Builds an opaque unlit material showing the texture's own colors. The alpha encodes a dye mask.</summary>
    /// <param name="tint">Multiplied over the material's color. White leaves it untouched.</param>
    /// <param name="applyDiffuseColor">Whether to multiply <see cref="DiffuseColor"/> over every pixel.</param>
    public Material ToUnlit(Vector4? tint = null, bool applyDiffuseColor = false)
    {
        var color = ResolveColor(tint, applyDiffuseColor);
        var material = BaseColor is null ? Material.Unlit(color) : Material.UnlitTextured(BaseColor, color);
        return material with { Blend = BlendMode.Opaque };
    }

    private Vector4 ResolveColor(Vector4? tint, bool applyDiffuseColor)
    {
        var color = tint ?? Vector4.One;
        if (applyDiffuseColor && DiffuseColor is { } diffuse)
            color = new Vector4(color.X * diffuse.X, color.Y * diffuse.Y, color.Z * diffuse.Z, color.W);

        return color;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        BaseColor?.Dispose();
        Normal?.Dispose();
        Specular?.Dispose();
    }
}

/// <summary>Loads game materials and their textures. Background materials bind <c>g_SamplerColorMap0</c>, character materials <c>g_SamplerDiffuse</c>.</summary>
public static class GameMaterialLoader
{
    // In order of preference.
    private static readonly string[] BaseColorSamplers = ["g_SamplerDiffuse", "g_SamplerColorMap0"];

    private static readonly string[] NormalSamplers = ["g_SamplerNormal", "g_SamplerNormalMap0"];

    private static readonly string[] SpecularSamplers = ["g_SamplerSpecular", "g_SamplerSpecularMap0"];

    /// <summary>Loads a material and its base color, normal and specular textures.</summary>
    /// <param name="materialGamePath">Archive path of the material.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The resolved material, or null when the file does not exist.</returns>
    public static async Task<GameMaterial?> LoadAsync(string materialGamePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialGamePath);

        var file = await Task.Run(() => NoireService.DataManager.GetFile<GameMaterialFile>(materialGamePath), ct).ConfigureAwait(false);
        if (file is null)
            return null;

        var baseColor = await LoadSlotAsync(file, BaseColorSamplers, ct).ConfigureAwait(false);
        var normal = await LoadSlotAsync(file, NormalSamplers, ct).ConfigureAwait(false);
        var specular = await LoadSlotAsync(file, SpecularSamplers, ct).ConfigureAwait(false);

        return new GameMaterial(file, baseColor, normal, specular);
    }

    /// <summary>The archive path of a material's base color texture, or null when it names none.</summary>
    /// <param name="file">The parsed material.</param>
    public static string? BaseColorPath(GameMaterialFile file) => SlotPath(file, BaseColorSamplers);

    /// <summary>The archive path of a material's normal map, or null when it names none.</summary>
    /// <param name="file">The parsed material.</param>
    public static string? NormalPath(GameMaterialFile file) => SlotPath(file, NormalSamplers);

    /// <summary>The archive path of a material's specular map, or null when it names none.</summary>
    /// <param name="file">The parsed material.</param>
    public static string? SpecularPath(GameMaterialFile file) => SlotPath(file, SpecularSamplers);

    private static string? SlotPath(GameMaterialFile file, string[] samplers)
    {
        ArgumentNullException.ThrowIfNull(file);

        foreach (var sampler in samplers)
        {
            var texture = file.TextureFor(sampler);
            if (texture is { Path.Length: > 0 })
                return texture.Value.IsDx11 ? GamePathHelper.Dx11TexturePath(texture.Value.Path) : texture.Value.Path;
        }

        return null;
    }

    private static async Task<GpuTexture?> LoadSlotAsync(GameMaterialFile file, string[] samplers, CancellationToken ct)
    {
        var path = SlotPath(file, samplers);
        return path is null ? null : await TextureLoader.FromGamePathAsync(path, ct).ConfigureAwait(false);
    }

    /// <summary>Loads every distinct material a model references, keyed by the path each was resolved from.</summary>
    /// <param name="modelGamePath">Archive path of the model.</param>
    /// <param name="materialPaths">Material paths as the model stores them.</param>
    /// <param name="variant">Variant directory for relative paths.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static async Task<Dictionary<string, GameMaterial>> LoadForModelAsync(
        string modelGamePath,
        IEnumerable<string> materialPaths,
        int variant = 1,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(materialPaths);

        var loaded = new Dictionary<string, GameMaterial>(StringComparer.Ordinal);
        foreach (var raw in materialPaths)
        {
            if (loaded.ContainsKey(raw))
                continue;

            var resolved = GamePathHelper.ResolveMaterialPath(modelGamePath, raw, variant);
            if (resolved is null)
                continue;

            var material = await LoadAsync(resolved, ct).ConfigureAwait(false);

            // An unresolved relative name belongs to another owner, such as the wearer's skin material.
            if (material is null && raw.StartsWith('/'))
            {
                foreach (var candidate in GamePathHelper.ResolveMaterialByOwnerName(raw, variant))
                {
                    material = await LoadAsync(candidate, ct).ConfigureAwait(false);
                    if (material is not null)
                        break;
                }
            }

            if (material is not null)
                loaded[raw] = material;
        }

        return loaded;
    }
}

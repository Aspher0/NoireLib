using NoireLib.Draw3D.Materials;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// A game material resolved into something drawable: the parsed file, plus the base color texture it
/// names, loaded and ready to bind.<br/>
/// <b>Ownership:</b> this owns the textures it loaded and releases them on dispose.
/// </summary>
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

    /// <summary>The parsed material, for everything this convenience does not surface.</summary>
    public GameMaterialFile File { get; }

    /// <summary>The shader package of dyeable furniture, the one package whose color map alpha is a stain mask.</summary>
    public const string DyeableFurnitureShader = "bgcolorchange.shpk";

    /// <summary>Whether this material's dyeable mask is live, so a stain (or the undyed default) applies to it.</summary>
    public bool IsDyeableFurniture => File.ShaderPackage == DyeableFurnitureShader;

    /// <summary>
    /// The color a dyeable surface renders when nothing states a stain (stain row 1, Snow White, the
    /// table's own display color) - the fallback below <see cref="StainHelper.DefaultStainForModel"/>, used only
    /// when a scene states no default stain either (see docs/Draw3D Game Assets Status.md).
    /// </summary>
    public static readonly Vector3 UndyedStain = new(228f / 255f, 223f / 255f, 208f / 255f);

    /// <summary>The base color texture, or null when the material names none or it failed to load.</summary>
    public GpuTexture? BaseColor { get; }

    /// <summary>
    /// The normal map, or null when the material names none; red and green carry the tangent-space normal,
    /// blue carries a further channel whose meaning depends on the shader package, and alpha is unused on
    /// the background materials measured so far.
    /// </summary>
    public GpuTexture? Normal { get; }

    /// <summary>
    /// The specular map, or null when the material names none; on background materials red is graded and
    /// weighted high (read as reflectivity/occlusion), green sits in a narrow band (read as gloss), and blue
    /// is effectively unused, both exposed rather than assumed.
    /// </summary>
    public GpuTexture? Specular { get; }

    /// <summary>
    /// The material's diffuse color constant, or null when it sets none; on dyeable furniture it holds an
    /// exact stain-table color that is not what actually renders (an undyed placement shows the scene's
    /// default stain, <see cref="StainHelper.DefaultStainForModel"/>, or <see cref="UndyedStain"/> when none is
    /// stated), exposed here as parsed data (see docs/Draw3D Game Assets Status.md).
    /// </summary>
    public Vector3? DiffuseColor
    {
        get
        {
            var values = File.ConstantValue("g_DiffuseColor");
            return values is { Length: >= 3 } ? new Vector3(values[0], values[1], values[2]) : null;
        }
    }

    /// <summary>
    /// Builds the material this asset should normally be drawn with: opaque, lit, and with any dye color
    /// confined to the area the color map's alpha marks as dyeable (<see cref="ToLit"/> and <see cref="ToUnlit"/>
    /// remain as diagnostics only); <b>falls back to <see cref="ToLit"/></b> when the material has no base color
    /// texture or the mask pipeline is unavailable, silently ignoring <paramref name="dye"/> since the lit
    /// shader cannot confine it - check <see cref="GameMaterialPipeline.Unavailable"/> to tell that apart from
    /// a dye that simply had nothing to color.
    /// </summary>
    /// <param name="dye">
    /// Color applied to the dyeable area only, as a display color (matching a color picker and the game's dye
    /// table); null renders <see cref="UndyedStain"/>, the game's fallback for an empty stain slot, or pass the
    /// scene's default stain (<see cref="StainHelper.DefaultStainForModel"/>) to render an item exactly as an undyed
    /// placement shows it.
    /// </param>
    /// <param name="tint">Multiplied over the whole surface afterwards; white leaves it untouched.</param>
    /// <param name="normalStrength">How far the normal map bends the surface normal (0 = geometric normal alone, above 1 exaggerates it).</param>
    /// <param name="specularStrength">
    /// How strongly the specular map contributes a highlight (green channel read as roughness); <b>off by
    /// default</b>, since measured background surfaces are matte in game and this map's channels are not
    /// fully understood.
    /// </param>
    /// <param name="dyeReference">
    /// How the dye meets the masked area: 0 multiplies the authored color by the dye (matches the game); a
    /// positive value is the authored color the dye should land on exactly, dividing the area by it first so
    /// the texture carries only relative shading - an authoring tool, not a model of the game.
    /// </param>
    /// <param name="ignoreSceneLight">
    /// Takes this renderer's lighting out of the picture, leaving the surface at the colors its texture and
    /// dye give it; this is the absence of our light rather than the presence of the game's, letting a color
    /// difference and a lighting difference be told apart.
    /// </param>
    public Material ToGameShaded(
        Vector3? dye = null,
        Vector4? tint = null,
        float normalStrength = 1f,
        float specularStrength = 0f,
        float dyeReference = 0f,
        bool ignoreSceneLight = false)
    {
        // Falling back still draws the texture, just without dye, normal map or specular; it does not
        // self-repair once the device arrives, so callers holding materials watch GameMaterialPipeline.Ready
        // and rebuild on the transition.
        if (BaseColor is null || !GameMaterialPipeline.EnsureRegistered())
            return ToLit(tint);

        // A dyeable surface always has a color multiplied in; an empty stain slot renders the undyed default,
        // not the raw texture. Every color here is display-encoded - the stain table, a picker, the default -
        // so the conversion to linear happens once, here, where the encoding is known.
        var applied = dye ?? (IsDyeableFurniture ? UndyedStain : (Vector3?)null);
        var color = ColorHelper.SrgbToLinear(applied ?? Vector3.One);
        var strength = applied is null ? 0f : 1f;

        // A strength is only meaningful when the map behind it exists, so an absent texture zeroes its term
        // rather than leaving the shader to sample an unbound slot.
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
    /// <param name="tint">Multiplied over the material's color; white leaves it untouched.</param>
    /// <param name="applyDiffuseColor">Multiply <see cref="DiffuseColor"/> over every pixel; right for the areas the game tints, too dark for the areas it does not.</param>
    public Material ToLit(Vector4? tint = null, bool applyDiffuseColor = false)
    {
        var color = ResolveColor(tint, applyDiffuseColor);
        return BaseColor is null ? Material.Lit(color) : Material.Lit(color) with { Texture = BaseColor };
    }

    /// <summary>
    /// Builds an unlit material showing the texture's own colors with no shading applied, useful for telling a
    /// texture problem from a lighting one (matching the game means the textures are right and the difference
    /// is this renderer's light); <b>drawn opaque</b>, unlike the general unlit material, since these surfaces'
    /// alpha is a dyeable mask rather than coverage and blending on it would both erase the fixed detail it
    /// marks and drop the depth test that keeps the model's near faces in front of its far ones.
    /// </summary>
    /// <param name="tint">Multiplied over the material's color; white leaves it untouched.</param>
    /// <param name="applyDiffuseColor">Multiply <see cref="DiffuseColor"/> over every pixel.</param>
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

/// <summary>
/// Loads game materials and the textures they name.<br/>
/// Which texture is the base color is decided by the sampler a material actually binds rather than by
/// its shader package, so a renamed or unfamiliar package still resolves: background materials bind
/// <c>g_SamplerColorMap0</c>, character materials bind <c>g_SamplerDiffuse</c>, and either is used
/// wherever it is found.
/// </summary>
public static class GameMaterialLoader
{
    /// <summary>Sampler names that carry base color, in the order they are preferred.</summary>
    private static readonly string[] BaseColorSamplers = ["g_SamplerDiffuse", "g_SamplerColorMap0"];

    /// <summary>Sampler names that carry the normal map, in the order they are preferred.</summary>
    private static readonly string[] NormalSamplers = ["g_SamplerNormal", "g_SamplerNormalMap0"];

    /// <summary>Sampler names that carry the specular map, in the order they are preferred.</summary>
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

    /// <summary>The archive path behind the first of these samplers the material actually binds.</summary>
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

    /// <summary>Loads the texture behind the first of these samplers the material binds, if any.</summary>
    private static async Task<GpuTexture?> LoadSlotAsync(GameMaterialFile file, string[] samplers, CancellationToken ct)
    {
        var path = SlotPath(file, samplers);
        return path is null ? null : await TextureLoader.FromGamePathAsync(path, ct).ConfigureAwait(false);
    }

    /// <inheritdoc cref="GamePathHelper.ResolveMaterialPath"/>
    public static string? ResolvePath(string modelGamePath, string materialPath, int variant = 1)
        => GamePathHelper.ResolveMaterialPath(modelGamePath, materialPath, variant);

    /// <inheritdoc cref="GamePathHelper.ResolveMaterialByOwnerName"/>
    public static IReadOnlyList<string> ResolveByOwnerName(string materialPath, int variant = 1)
        => GamePathHelper.ResolveMaterialByOwnerName(materialPath, variant);

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

            var resolved = ResolvePath(modelGamePath, raw, variant);
            if (resolved is null)
                continue;

            var material = await LoadAsync(resolved, ct).ConfigureAwait(false);

            // A relative name that did not resolve beside the model belongs to another owner - an equipment
            // model naming its wearer's skin material is the everyday case - and the name says whose it is.
            if (material is null && raw.StartsWith('/'))
            {
                foreach (var candidate in ResolveByOwnerName(raw, variant))
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

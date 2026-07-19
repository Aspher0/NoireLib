using NoireLib.Draw3D.Materials;
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

    /// <summary>
    /// The authored color of a dyeable area, measured across sampled background materials: those texels average
    /// around this value, so it is the starting point for the dye reference in <see cref="ToGameShaded"/>.
    /// </summary>
    public const float MeasuredDyeReference = 0.78f;

    /// <summary>The base color texture, or null when the material names none or it failed to load.</summary>
    public GpuTexture? BaseColor { get; }

    /// <summary>
    /// The normal map, or null when the material names none.<br/>
    /// Red and green carry the tangent-space normal; blue carries a further channel whose meaning depends on
    /// the shader package, and alpha is unused on the background materials measured so far.
    /// </summary>
    public GpuTexture? Normal { get; }

    /// <summary>
    /// The specular map, or null when the material names none.<br/>
    /// On background materials red is graded and weighted high, green sits in a narrow band, and blue is
    /// effectively unused. Red reads as a reflectivity or occlusion term and green as a gloss term; both are
    /// exposed rather than assumed, because a statistical shape is not a proof of meaning.
    /// </summary>
    public GpuTexture? Specular { get; }

    /// <summary>
    /// The material's diffuse color constant, or null when it sets none. Most materials set it to white,
    /// where it changes nothing; dyeable furniture is where it carries a real color.<br/>
    /// <b>Applying it uniformly is wrong.</b> The color map's alpha channel is a binary dyeable mask:
    /// where it is high the color is authored near-neutral and takes a tint, and where it is low the
    /// texture carries its own final color, which on furniture is most of the visible surface.
    /// Multiplying a color over every pixel, as <see cref="ToLit"/> does when asked to, darkens that
    /// fixed detail. Confining the tint to the masked area needs a shading path that reads alpha as data
    /// rather than as coverage.
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
    /// confined to the area the color map's alpha marks as dyeable.<br/>
    /// This is the correct path for game assets. <see cref="ToLit"/> and <see cref="ToUnlit"/> remain as
    /// diagnostics, and are what the standard shaders can express without reading the mask.<br/>
    /// <b>Falls back to <see cref="ToLit"/></b> when the material has no base color texture or the mask
    /// pipeline is unavailable, in which case <paramref name="dye"/> is silently ignored because the lit
    /// shader cannot confine it. Check <see cref="GameMaterialPipeline.Unavailable"/> to tell that apart
    /// from a dye that simply had nothing to color.
    /// </summary>
    /// <param name="dye">
    /// Color applied to the dyeable area only, as a display color. Null leaves that area at the near-neutral color it
    /// was authored with, which is the closest match to an unstained item currently available.
    /// </param>
    /// <param name="tint">Multiplied over the whole surface afterwards. White leaves it untouched.</param>
    /// <param name="normalStrength">How far the normal map bends the surface normal. 0 draws with the geometric normal alone, and values above 1 exaggerate it.</param>
    /// <param name="specularStrength">
    /// How strongly the specular map contributes a highlight, with its green channel read as roughness.<br/>
    /// <b>Off by default</b>: the background surfaces measured so far are matte in game, and the community shader
    /// reference marks this map's mask channels as not fully understood.
    /// </param>
    /// <param name="dyeReference">
    /// How the dye meets the masked area. 0 multiplies the authored color by the dye. A positive value is the
    /// authored color the dye should land on exactly: the area is divided by it first, so the texture carries only
    /// relative shading and the dye carries the color. <see cref="MeasuredDyeReference"/> is the measured starting point.<br/>
    /// <b>Which of the two the game does is unresolved</b>, and the two differ by more than a shade, so this is a
    /// control rather than a setting with a known-correct value.
    /// </param>
    /// <param name="ignoreSceneLight">
    /// Takes this renderer's lighting out of the picture, leaving the surface at the colors its texture and dye give it.<br/>
    /// This is the absence of our light rather than the presence of the game's: it exists so a color difference and a
    /// lighting difference can be told apart, which is otherwise impossible by eye.
    /// </param>
    public Material ToGameShaded(
        Vector3? dye = null,
        Vector4? tint = null,
        float normalStrength = 1f,
        float specularStrength = 0f,
        float dyeReference = 0f,
        bool ignoreSceneLight = false)
    {
        if (BaseColor is null || !GameMaterialPipeline.EnsureRegistered())
            return ToLit(tint);

        // No dye by default: the masked area keeps the near-neutral colour it was authored with. Using the
        // material's diffuse constant here was tried and measured against the game, and it came out far too
        // dark even confined to the mask, so that constant is not the undyed appearance.
        var color = dye ?? Vector3.One;
        var strength = dye is null ? 0f : 1f;

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
    /// <param name="tint">Multiplied over the material's color. White leaves it untouched.</param>
    /// <param name="applyDiffuseColor">Multiply <see cref="DiffuseColor"/> over every pixel. See its remarks: this is right for the areas the game tints and too dark for the areas it does not.</param>
    public Material ToLit(Vector4? tint = null, bool applyDiffuseColor = false)
    {
        var color = ResolveColor(tint, applyDiffuseColor);
        return BaseColor is null ? Material.Lit(color) : Material.Lit(color) with { Texture = BaseColor };
    }

    /// <summary>
    /// Builds an unlit material, showing the texture's own colors with no shading applied.<br/>
    /// Useful for telling a texture problem apart from a lighting one: unlit output that matches the game
    /// means the textures are right and the difference is this renderer's light, not the asset.<br/>
    /// <b>Drawn opaque</b>, unlike the general unlit material. These surfaces are opaque and their alpha is a
    /// dyeable mask rather than coverage, so blending on it both erases the fixed detail it marks and drops
    /// the depth test that keeps the model's own near faces in front of its far ones.
    /// </summary>
    /// <param name="tint">Multiplied over the material's color. White leaves it untouched.</param>
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

    /// <summary>Loads a material and its base color texture.</summary>
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
                return VariantPath(texture.Value);
        }

        return null;
    }

    /// <summary>Loads the texture behind the first of these samplers the material binds, if any.</summary>
    private static async Task<GpuTexture?> LoadSlotAsync(GameMaterialFile file, string[] samplers, CancellationToken ct)
    {
        var path = SlotPath(file, samplers);
        return path is null ? null : await TextureLoader.FromGamePathAsync(path, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a material path taken from a model into a loadable archive path.<br/>
    /// Background models store the path outright. Character models store it relative, beginning with a
    /// slash, and it resolves against the model's own folder plus a numbered variant directory.
    /// </summary>
    /// <param name="modelGamePath">Archive path of the model that referenced the material.</param>
    /// <param name="materialPath">The path as the model stores it.</param>
    /// <param name="variant">Variant directory to resolve a relative path against.</param>
    /// <returns>An absolute archive path, or null when the inputs cannot form one.</returns>
    public static string? ResolvePath(string modelGamePath, string materialPath, int variant = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelGamePath);

        if (string.IsNullOrWhiteSpace(materialPath))
            return null;

        if (!materialPath.StartsWith('/'))
            return materialPath;

        // Character models keep their materials beside the model directory rather than under it, so the
        // model's own folder is dropped before the variant folder is appended.
        var modelFolder = modelGamePath.LastIndexOf('/');
        if (modelFolder < 0)
            return null;

        var parent = modelGamePath[..modelFolder];
        var parentFolder = parent.LastIndexOf('/');
        if (parentFolder < 0)
            return null;

        return $"{parent[..parentFolder]}/material/v{variant:D4}{materialPath}";
    }

    /// <summary>
    /// The archive path of a texture slot, accounting for the DirectX 11 variant marker: the file for
    /// those slots sits beside the named one with a doubled dash prefixed to the file name.
    /// </summary>
    private static string VariantPath(GameMaterialTexture texture)
    {
        if (!texture.IsDx11)
            return texture.Path;

        var slash = texture.Path.LastIndexOf('/');
        return slash < 0 ? $"--{texture.Path}" : $"{texture.Path[..(slash + 1)]}--{texture.Path[(slash + 1)..]}";
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

            var resolved = ResolvePath(modelGamePath, raw, variant);
            if (resolved is null)
                continue;

            var material = await LoadAsync(resolved, ct).ConfigureAwait(false);
            if (material is not null)
                loaded[raw] = material;
        }

        return loaded;
    }
}

using NoireLib.Draw3D.Materials;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// A compiled VS+PS pair with its input layout. Failed pipelines render nothing (self-disable rung 1).
/// </summary>
internal sealed unsafe class ShaderPipeline : IDisposable
{
    internal ComPtr<ID3D11VertexShader> VsPtr;
    internal ComPtr<ID3D11PixelShader> PsPtr;
    internal ComPtr<ID3D11InputLayout> LayoutPtr;

    /// <summary>Small id used in sort keys (grouping only).</summary>
    public byte Id { get; init; }

    /// <summary>Pipeline name (diagnostics).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>True when this pipeline consumes the per-instance stream (input slot 1).</summary>
    public bool Instanced { get; init; }

    /// <summary>The vertex shader (null when failed).</summary>
    public ID3D11VertexShader* Vs => VsPtr.Get();

    /// <summary>The pixel shader (null when failed).</summary>
    public ID3D11PixelShader* Ps => PsPtr.Get();

    /// <summary>The input layout (null for the composite pipeline).</summary>
    public ID3D11InputLayout* Layout => LayoutPtr.Get();

    /// <inheritdoc/>
    public void Dispose()
    {
        LayoutPtr.Dispose();
        PsPtr.Dispose();
        VsPtr.Dispose();
    }
}

/// <summary>
/// Named pipeline cache over the embedded HLSL sources. Variants are #define permutations; a compile
/// error disables only the owning pipeline (log-once, ladder rung 1) and never throws into the frame.
/// </summary>
internal sealed unsafe class ShaderLibrary : IDisposable
{
    private readonly Dictionary<string, ShaderPipeline?> cache = new();
    private readonly Dictionary<string, string> customSources = new();
    private readonly Dictionary<string, string> embedded = new(StringComparer.OrdinalIgnoreCase);
    private byte nextId = 1;

    /// <summary>Loads and include-resolves the embedded shader sources.</summary>
    public ShaderLibrary()
    {
        var assembly = typeof(ShaderLibrary).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.Contains(".Draw3D.Shaders.", StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null)
                continue;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var fileName = resource[(resource.IndexOf(".Draw3D.Shaders.", StringComparison.OrdinalIgnoreCase) + ".Draw3D.Shaders.".Length)..];
            embedded[fileName] = reader.ReadToEnd();
        }
    }

    /// <summary>Gets the standard pipeline for a material configuration, or null when its compile failed.</summary>
    public ShaderPipeline? GetStandard(RenderDevice device, MaterialDomain domain, bool textured, bool instanced, bool opaqueDomain)
    {
        var key = $"{domain}|{(textured ? "T" : "-")}|{(instanced ? "I" : "-")}|{(opaqueDomain ? "O" : "-")}";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var (file, prefix) = domain switch
        {
            MaterialDomain.Lit => ("Lit.hlsl", "LIT"),
            MaterialDomain.GroundDecal => ("GroundDecal.hlsl", "DECAL"),
            _ => ("Unlit.hlsl", "UNLIT"),
        };

        var defines = new List<(string, string)>();
        if (textured)
            defines.Add(($"{prefix}_TEXTURED", "1"));
        if (instanced && domain != MaterialDomain.GroundDecal)
            defines.Add(($"{prefix}_INSTANCED", "1"));
        if (opaqueDomain && domain != MaterialDomain.GroundDecal)
            defines.Add(("OPAQUE_DOMAIN", "1"));

        var pipeline = Compile(device, key, GetSource(file), defines, instanced, createLayout: true);
        cache[key] = pipeline;
        return pipeline;
    }

    /// <summary>Gets the composite pipeline (fullscreen triangle, no input layout), or null when its compile failed.</summary>
    public ShaderPipeline? GetComposite(RenderDevice device)
    {
        const string key = "Composite";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var pipeline = Compile(device, key, GetSource("Composite.hlsl"), null, instanced: false, createLayout: false);
        cache[key] = pipeline;
        return pipeline;
    }

    /// <summary>Gets the outline coverage-mask pipeline for a solid mesh silhouette (non-instanced, standard vertex layout), or null on compile failure.</summary>
    /// <summary>
    /// Gets the pipeline that writes a mesh into the GAME's G-buffer, so the game's own lighting pass lights
    /// it. Null on compile failure, which leaves the object on its normal path rather than failing the frame.
    /// </summary>
    /// <param name="device">The render device.</param>
    /// <param name="textured">Whether the mesh samples a base texture into its albedo.</param>
    /// <param name="maps">Whether the material's normal and specular maps are bound, giving per-pixel detail.</param>
    public ShaderPipeline? GetGameGBuffer(RenderDevice device, bool textured, bool maps)
    {
        var key = $"GameGBuffer{(textured ? "_T" : string.Empty)}{(maps ? "_M" : string.Empty)}";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var defineList = new List<(string, string)>(2);
        if (textured)
            defineList.Add(("GBUFFER_TEXTURED", "1"));
        if (maps)
            defineList.Add(("GBUFFER_MAPS", "1"));

        var defines = defineList.Count > 0 ? defineList : null;
        var pipeline = Compile(device, key, GetSource("GameGBuffer.hlsl"), defines, instanced: false, createLayout: true);
        cache[key] = pipeline;
        return pipeline;
    }

    public ShaderPipeline? GetOutlineMaskMesh(RenderDevice device)
    {
        const string key = "OutlineMaskMesh";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var pipeline = Compile(device, key, GetSource("OutlineMaskMesh.hlsl"), null, instanced: false, createLayout: true);
        cache[key] = pipeline;
        return pipeline;
    }

    /// <summary>Gets the outline composite pipeline (fullscreen triangle dilating the coverage mask into a rim), or null on compile failure.</summary>
    public ShaderPipeline? GetOutline(RenderDevice device)
    {
        const string key = "Outline";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var pipeline = Compile(device, key, GetSource("Outline.hlsl"), null, instanced: false, createLayout: false);
        cache[key] = pipeline;
        return pipeline;
    }

    /// <summary>Gets the top-down collision height-map pipeline (standard vertex layout; MAX-blended world Y), or null on compile failure.</summary>
    public ShaderPipeline? GetWorldHeight(RenderDevice device)
    {
        const string key = "WorldHeight";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var pipeline = Compile(device, key, GetSource("WorldHeight.hlsl"), null, instanced: false, createLayout: true);
        cache[key] = pipeline;
        return pipeline;
    }

    /// <summary>Registers a custom pipeline by name, the seam through which callers supply their own HLSL. The source may include "Common.hlsli".</summary>
    public bool RegisterCustom(string name, string hlslSource)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        customSources[name] = hlslSource;
        cache.Remove($"custom:{name}"); // recompile on next use
        return true;
    }

    /// <summary>Gets a registered custom pipeline (non-instanced, standard vertex layout), or null.</summary>
    public ShaderPipeline? GetCustom(RenderDevice device, string name)
    {
        var key = $"custom:{name}";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        if (!customSources.TryGetValue(name, out var source))
        {
            cache[key] = null;
            NoireLogger.LogError<ShaderLibrary>($"Material references unregistered custom pipeline '{name}'.", "Draw3D");
            return null;
        }

        var pipeline = Compile(device, key, source, null, instanced: false, createLayout: true);
        cache[key] = pipeline;
        return pipeline;
    }

    private string GetSource(string file)
        => ResolveIncludes(embedded.TryGetValue(file, out var text) ? text : throw new InvalidOperationException($"Draw3D: embedded shader '{file}' not found."), 0);

    private string ResolveIncludes(string source, int depth)
    {
        if (depth > 8)
            throw new InvalidOperationException("Draw3D: shader include depth exceeded (cycle?).");

        return Regex.Replace(source, "^\\s*#include\\s+\"([^\"]+)\"\\s*$", match =>
        {
            var file = match.Groups[1].Value;
            return embedded.TryGetValue(file, out var included)
                ? ResolveIncludes(included, depth + 1)
                : throw new InvalidOperationException($"Draw3D: shader include '{file}' not found.");
        }, RegexOptions.Multiline);
    }

    private ShaderPipeline? Compile(RenderDevice device, string name, string source, IReadOnlyList<(string, string)>? defines, bool instanced, bool createLayout)
    {
        // Custom sources may also carry includes.
        source = ResolveIncludes(source, 0);

        if (!ShaderCompiler.TryCompile(name, source, "vs", "vs_5_0", defines, out var vsBlob, out var vsError))
        {
            NoireLogger.LogError<ShaderLibrary>($"Pipeline '{name}' vertex shader failed to compile:\n{vsError}", "Draw3D");
            return null;
        }

        using (vsBlob)
        {
            if (!ShaderCompiler.TryCompile(name, source, "ps", "ps_5_0", defines, out var psBlob, out var psError))
            {
                NoireLogger.LogError<ShaderLibrary>($"Pipeline '{name}' pixel shader failed to compile:\n{psError}", "Draw3D");
                return null;
            }

            using (psBlob)
            {
                var pipeline = new ShaderPipeline { Id = nextId++, Name = name, Instanced = instanced };

                if (device.Device->CreateVertexShader(vsBlob.Get()->GetBufferPointer(), vsBlob.Get()->GetBufferSize(), null, pipeline.VsPtr.GetAddressOf()) < 0
                    || device.Device->CreatePixelShader(psBlob.Get()->GetBufferPointer(), psBlob.Get()->GetBufferSize(), null, pipeline.PsPtr.GetAddressOf()) < 0)
                {
                    NoireLogger.LogError<ShaderLibrary>($"Pipeline '{name}': shader object creation failed.", "Draw3D");
                    pipeline.Dispose();
                    return null;
                }

                if (createLayout && !TryCreateLayout(device, vsBlob.Get(), instanced, pipeline))
                {
                    NoireLogger.LogError<ShaderLibrary>($"Pipeline '{name}': input layout creation failed.", "Draw3D");
                    pipeline.Dispose();
                    return null;
                }

                return pipeline;
            }
        }
    }

    private static bool TryCreateLayout(RenderDevice device, ID3DBlob* vsBlob, bool instanced, ShaderPipeline pipeline)
    {
        ReadOnlySpan<byte> position = "POSITION\0"u8;
        ReadOnlySpan<byte> normal = "NORMAL\0"u8;
        ReadOnlySpan<byte> texcoord = "TEXCOORD\0"u8;
        ReadOnlySpan<byte> color = "COLOR\0"u8;
        ReadOnlySpan<byte> tangent = "TANGENT\0"u8;
        ReadOnlySpan<byte> iworld = "IWORLD\0"u8;
        ReadOnlySpan<byte> icolor = "ICOLOR\0"u8;

        fixed (byte* pPosition = position)
        fixed (byte* pNormal = normal)
        fixed (byte* pTexcoord = texcoord)
        fixed (byte* pColor = color)
        fixed (byte* pTangent = tangent)
        fixed (byte* pIWorld = iworld)
        fixed (byte* pIColor = icolor)
        {
            // Every element of Vertex3D is declared for every pipeline; a shader that does not read a
            // semantic simply leaves it unconsumed, which D3D11 permits, so only the shaders that use the
            // tangent had to change when it was added.
            var elements = stackalloc D3D11_INPUT_ELEMENT_DESC[10];
            var count = 0u;
            elements[count++] = Element(pPosition, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT, 0, 0);
            elements[count++] = Element(pNormal, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT, 0, 12);
            elements[count++] = Element(pTexcoord, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT, 0, 24);
            elements[count++] = Element(pColor, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 32);
            elements[count++] = Element(pTangent, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 48);

            if (instanced)
            {
                for (uint i = 0; i < 4; i++)
                    elements[count++] = InstanceElement(pIWorld, i, i * 16);
                elements[count++] = InstanceElement(pIColor, 0, 64);
            }

            return device.Device->CreateInputLayout(elements, count, vsBlob->GetBufferPointer(), vsBlob->GetBufferSize(), pipeline.LayoutPtr.GetAddressOf()) >= 0;
        }
    }

    private static D3D11_INPUT_ELEMENT_DESC Element(byte* semantic, uint index, DXGI_FORMAT format, uint slot, uint offset) => new()
    {
        SemanticName = (sbyte*)semantic,
        SemanticIndex = index,
        Format = format,
        InputSlot = slot,
        AlignedByteOffset = offset,
        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
    };

    private static D3D11_INPUT_ELEMENT_DESC InstanceElement(byte* semantic, uint index, uint offset) => new()
    {
        SemanticName = (sbyte*)semantic,
        SemanticIndex = index,
        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
        InputSlot = 1,
        AlignedByteOffset = offset,
        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_INSTANCE_DATA,
        InstanceDataStepRate = 1,
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var pipeline in cache.Values)
            pipeline?.Dispose();
        cache.Clear();
    }
}

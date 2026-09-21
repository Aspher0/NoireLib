using NoireLib.Draw3D.Materials;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// Failed pipelines render nothing.
internal sealed unsafe class ShaderPipeline : IDisposable
{
    internal ComPtr<ID3D11VertexShader> VsPtr;
    internal ComPtr<ID3D11PixelShader> PsPtr;
    internal ComPtr<ID3D11InputLayout> LayoutPtr;

    public byte Id { get; init; }

    public string Name { get; init; } = string.Empty;

    // Consumes the per-instance stream in input slot 1.
    public bool Instanced { get; init; }

    public ID3D11VertexShader* Vs => VsPtr.Get();

    public ID3D11PixelShader* Ps => PsPtr.Get();

    // Null for fullscreen pipelines.
    public ID3D11InputLayout* Layout => LayoutPtr.Get();

    public void Dispose()
    {
        LayoutPtr.Dispose();
        PsPtr.Dispose();
        VsPtr.Dispose();
    }
}

// A compile error disables only the owning pipeline, logged once.
internal sealed unsafe class ShaderLibrary : IDisposable
{
    private readonly Dictionary<string, ShaderPipeline?> cache = new();
    private readonly Dictionary<string, string> customSources = new();
    private readonly Dictionary<string, string> embedded = new(StringComparer.OrdinalIgnoreCase);
    private byte nextId = 1;

    public ShaderLibrary()
    {
        const string Folder = ".Draw3D.Shaders.";
        var assembly = typeof(ShaderLibrary).Assembly;

        foreach (var resource in FileHelper.FindEmbeddedResources(assembly, Folder))
        {
            var source = FileHelper.ReadEmbeddedText(assembly, resource);
            if (source == null)
                continue;

            var fileName = resource[(resource.IndexOf(Folder, StringComparison.OrdinalIgnoreCase) + Folder.Length)..];
            embedded[fileName] = source;
        }
    }

    // Every Get* returns null when the pipeline failed to compile.
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

    public ShaderPipeline? GetComposite(RenderDevice device)
    {
        const string key = "Composite";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var pipeline = Compile(device, key, GetSource("Composite.hlsl"), null, instanced: false, createLayout: false);
        cache[key] = pipeline;
        return pipeline;
    }

    public ShaderPipeline? GetTemporalResolve(RenderDevice device)
    {
        const string key = "TemporalResolve";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var pipeline = Compile(device, key, GetSource("TemporalResolve.hlsl"), null, instanced: false, createLayout: false);
        cache[key] = pipeline;
        return pipeline;
    }

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

    public ShaderPipeline? GetShadowDepth(RenderDevice device)
    {
        const string key = "ShadowDepth";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var pipeline = Compile(device, key, GetSource("ShadowDepth.hlsl"), null, instanced: false, createLayout: true);
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

    public ShaderPipeline? GetOutline(RenderDevice device)
    {
        const string key = "Outline";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var pipeline = Compile(device, key, GetSource("Outline.hlsl"), null, instanced: false, createLayout: false);
        cache[key] = pipeline;
        return pipeline;
    }

    // World Y, MAX-blended.
    public ShaderPipeline? GetWorldHeight(RenderDevice device)
    {
        const string key = "WorldHeight";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var pipeline = Compile(device, key, GetSource("WorldHeight.hlsl"), null, instanced: false, createLayout: true);
        cache[key] = pipeline;
        return pipeline;
    }

    // The source may include "Common.hlsli".
    public bool RegisterCustom(string name, string hlslSource)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        customSources[name] = hlslSource;
        cache.Remove($"custom:{name}");
        return true;
    }

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
            // D3D11 permits unconsumed semantics.
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

    public void Dispose()
    {
        foreach (var pipeline in cache.Values)
            pipeline?.Dispose();
        cache.Clear();
    }
}

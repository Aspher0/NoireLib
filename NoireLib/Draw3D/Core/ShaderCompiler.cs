using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.DirectX;

namespace NoireLib.Draw3D.Core;

internal static unsafe class ShaderCompiler
{
    private const uint D3DCompileDebug = 1u << 0;              // D3DCOMPILE_DEBUG
    private const uint D3DCompileOptimizationLevel3 = 1u << 15; // D3DCOMPILE_OPTIMIZATION_LEVEL3
    private const uint D3DCompileWarningsAreErrors = 1u << 18;  // D3DCOMPILE_WARNINGS_ARE_ERRORS

    public static bool TryCompile(
        string name,
        string source,
        string entryPoint,
        string profile,
        IReadOnlyList<(string Name, string Value)>? defines,
        out TerraFX.Interop.Windows.ComPtr<ID3DBlob> bytecode,
        out string? error)
    {
        bytecode = default;
        error = null;

        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var entryBytes = Encoding.UTF8.GetBytes(entryPoint + "\0");
        var profileBytes = Encoding.UTF8.GetBytes(profile + "\0");

        var flags = D3DCompileOptimizationLevel3 | D3DCompileWarningsAreErrors;
#if DEBUG
        flags |= D3DCompileDebug;
#endif

        var pins = new List<GCHandle>();
        try
        {
            var defineCount = defines?.Count ?? 0;
            var macros = stackalloc D3D_SHADER_MACRO[defineCount + 1];
            for (var i = 0; i < defineCount; i++)
            {
                var nameHandle = GCHandle.Alloc(Encoding.UTF8.GetBytes(defines![i].Name + "\0"), GCHandleType.Pinned);
                var valueHandle = GCHandle.Alloc(Encoding.UTF8.GetBytes(defines[i].Value + "\0"), GCHandleType.Pinned);
                pins.Add(nameHandle);
                pins.Add(valueHandle);
                macros[i] = new D3D_SHADER_MACRO
                {
                    Name = (sbyte*)nameHandle.AddrOfPinnedObject(),
                    Definition = (sbyte*)valueHandle.AddrOfPinnedObject(),
                };
            }

            macros[defineCount] = default;

            // A using over the struct would dispose a copy taken while the pointer was still null.
            TerraFX.Interop.Windows.ComPtr<ID3DBlob> errors = default;
            try
            {
                fixed (byte* src = sourceBytes)
                fixed (byte* entry = entryBytes)
                fixed (byte* prof = profileBytes)
                {
                    var hr = D3DCompile(
                        src, (nuint)sourceBytes.Length,
                        null,
                        defineCount > 0 ? macros : null,
                        null,
                        (sbyte*)entry, (sbyte*)prof,
                        flags, 0,
                        bytecode.GetAddressOf(), errors.GetAddressOf());

                    if (hr < 0)
                    {
                        error = GetErrorText(errors.Get()) + $" (hr=0x{(int)hr:X8}, shader '{name}', entry '{entryPoint}')";
                        bytecode.Dispose();
                        bytecode = default;
                        return false;
                    }
                }
            }
            finally
            {
                errors.Dispose();
            }

            return true;
        }
        finally
        {
            foreach (var pin in pins)
                pin.Free();
        }
    }

    private static string GetErrorText(ID3DBlob* errors)
    {
        if (errors == null)
            return "No compiler diagnostics were returned.";

        return Encoding.UTF8.GetString((byte*)errors->GetBufferPointer(), (int)errors->GetBufferSize()).TrimEnd('\0', '\r', '\n');
    }
}

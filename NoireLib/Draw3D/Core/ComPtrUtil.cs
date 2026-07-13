using TerraFX.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// COM reference-count helpers. The one place the "QueryInterface already AddRef'd" rule is encoded,
/// so it is never hand-rolled (and hand-rolled wrong) anywhere else.
/// </summary>
internal static unsafe class ComPtrUtil
{
    /// <summary>
    /// QueryInterfaces <paramref name="unknown"/> for <typeparamref name="T"/> and wraps the result with exactly one net reference.<br/>
    /// Returns false (and an empty ComPtr) when the pointer is null or does not implement the interface.
    /// </summary>
    public static bool TryQi<T>(IUnknown* unknown, out ComPtr<T> result) where T : unmanaged, INativeGuid, IUnknown.Interface
    {
        result = default;
        if (unknown == null)
            return false;

        T* typed = null;
        if (unknown->QueryInterface(__uuidof<T>(), (void**)&typed) < 0 || typed == null)
            return false;

        result.Attach(typed); // QI already AddRef'd; Attach takes ownership without another AddRef.
        return true;
    }

    /// <summary>Releases a raw COM pointer if non-null and nulls the reference.</summary>
    public static void Release<T>(ref T* ptr) where T : unmanaged
    {
        if (ptr != null)
        {
            ((IUnknown*)ptr)->Release();
            ptr = null;
        }
    }
}

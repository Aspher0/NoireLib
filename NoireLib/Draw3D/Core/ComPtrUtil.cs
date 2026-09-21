using TerraFX.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace NoireLib.Draw3D.Core;

// QueryInterface already AddRef'd its result. It is attached, never AddRef'd again.
internal static unsafe class ComPtrUtil
{
    public static bool TryQi<T>(IUnknown* unknown, out ComPtr<T> result) where T : unmanaged, INativeGuid, IUnknown.Interface
    {
        result = default;
        if (unknown == null)
            return false;

        T* typed = null;
        if (unknown->QueryInterface(__uuidof<T>(), (void**)&typed) < 0 || typed == null)
            return false;

        result.Attach(typed);
        return true;
    }

    public static void Release<T>(ref T* ptr) where T : unmanaged
    {
        if (ptr != null)
        {
            ((IUnknown*)ptr)->Release();
            ptr = null;
        }
    }
}

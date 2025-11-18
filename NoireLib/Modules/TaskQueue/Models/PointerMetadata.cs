using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Helper class for storing unsafe pointers in task metadata.<br/>
/// Since pointers cannot be directly stored as objects, this class wraps them as IntPtr.
/// </summary>
/// <typeparam name="T">The pointer type (must be an unmanaged type).</typeparam>
public class PointerMetadata<T> where T : unmanaged
{
    /// <summary>
    /// The pointer address stored as IntPtr.
    /// </summary>
    public IntPtr Address { get; }

    /// <summary>
    /// Creates a new PointerMetadata from an unsafe pointer.
    /// </summary>
    /// <param name="pointer">The pointer to store.</param>
    public unsafe PointerMetadata(T* pointer)
    {
        Address = new IntPtr(pointer);
    }

    /// <summary>
    /// Gets the pointer back from the stored address.
    /// </summary>
    /// <returns>The original pointer.</returns>
    public unsafe T* GetPointer()
    {
        return (T*)Address;
    }

    /// <summary>
    /// Checks if the pointer is null.
    /// </summary>
    public bool IsNull => Address == IntPtr.Zero;

    /// <summary>
    /// Creates a PointerMetadata from a pointer.
    /// </summary>
    /// <param name="pointer">The pointer to wrap.</param>
    /// <returns>A new PointerMetadata instance.</returns>
    public static unsafe PointerMetadata<T> FromPointer(T* pointer)
    {
        return new PointerMetadata<T>(pointer);
    }

    /// <summary>
    /// Implicit conversion from pointer to PointerMetadata.
    /// </summary>
    public static unsafe implicit operator PointerMetadata<T>(T* pointer)
    {
        return new PointerMetadata<T>(pointer);
    }

    /// <summary>
    /// Implicit conversion from PointerMetadata to pointer.
    /// </summary>
    public static unsafe implicit operator T*(PointerMetadata<T> metadata)
    {
        return metadata.GetPointer();
    }

    /// <summary>
    /// Returns a string representation of the PointerMetadata.
    /// </summary>
    /// <returns>The string representation of the PointerMetadata.</returns>
    public override string ToString()
    {
        return $"PointerMetadata<{typeof(T).Name}>(0x{Address.ToInt64():X})";
    }
}

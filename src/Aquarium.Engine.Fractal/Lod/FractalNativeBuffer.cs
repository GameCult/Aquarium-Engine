using System.Runtime.InteropServices;

namespace Aquarium.Engine.Fractal.Lod;

public sealed unsafe class FractalNativeBuffer<T> : IDisposable
    where T : unmanaged
{
    private readonly nuint byteCount;
    private bool disposed;

    public FractalNativeBuffer(int length, nuint alignment = 64)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Native buffer length must not be negative.");
        }

        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Native buffer alignment must be a power of two.");
        }

        Length = length;
        byteCount = checked((nuint)length * (nuint)sizeof(T));
        Pointer = byteCount == 0
            ? null
            : (T*)NativeMemory.AlignedAlloc(byteCount, alignment);
        if (Pointer is null && byteCount > 0)
        {
            throw new OutOfMemoryException($"Could not allocate {byteCount} bytes for native buffer.");
        }

        if (Pointer is not null)
        {
            NativeMemory.Clear(Pointer, byteCount);
        }
    }

    public int Length { get; }

    public T* Pointer { get; private set; }

    public Span<T> Span
    {
        get
        {
            ThrowIfDisposed();
            return new Span<T>(Pointer, Length);
        }
    }

    public ref T this[int index]
    {
        get
        {
            ThrowIfDisposed();
            if ((uint)index >= (uint)Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Native buffer index is outside the allocated range.");
            }

            return ref Pointer[index];
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (Pointer is not null)
        {
            NativeMemory.AlignedFree(Pointer);
            Pointer = null;
        }

        GC.SuppressFinalize(this);
    }

    ~FractalNativeBuffer()
    {
        Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(FractalNativeBuffer<T>));
        }
    }
}

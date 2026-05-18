using Aquarium.Engine.Render;
using SharpGen.Runtime;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Aquarium.Engine.Render;

internal sealed class D3D12ExternalSensorTexture : IDisposable
{
    private D3D12ExternalSensorTexture(
        string key,
        IntPtr sharedHandle,
        ID3D12Resource resource,
        int width,
        int height,
        Format format,
        long timestampNs)
    {
        Key = key;
        SharedHandle = sharedHandle;
        Resource = resource;
        Width = width;
        Height = height;
        Format = format;
        TimestampNs = timestampNs;
    }

    public string Key { get; }

    public IntPtr SharedHandle { get; }

    public ID3D12Resource Resource { get; }

    public int Width { get; }

    public int Height { get; }

    public Format Format { get; }

    public long TimestampNs { get; private set; }

    public bool Matches(AquariumExternalGpuTexture texture)
    {
        return SharedHandle == texture.SharedHandle
            && string.Equals(Key, CacheKey(texture), StringComparison.Ordinal)
            && Width == texture.Width
            && Height == texture.Height
            && Format == ToDxgiFormat(texture.PixelFormat);
    }

    public void UpdateTimestamp(long timestampNs)
    {
        TimestampNs = timestampNs;
    }

    public void CreateShaderResourceView(ID3D12Device device, D3D12DescriptorSlot descriptor)
    {
        device.CreateShaderResourceView(
            Resource,
            new ShaderResourceViewDescription
            {
                Format = Format,
                ViewDimension = ShaderResourceViewDimension.Texture2D,
                Shader4ComponentMapping = ShaderComponentMapping.Default,
                Texture2D = new Texture2DShaderResourceView { MipLevels = 1 },
            },
            descriptor.Cpu);
    }

    public static bool TryOpen(ID3D12Device device, AquariumExternalGpuTexture texture, out D3D12ExternalSensorTexture externalTexture)
    {
        externalTexture = null!;
        if ((texture.SharedHandle == IntPtr.Zero && string.IsNullOrWhiteSpace(texture.SharedHandleName))
            || texture.Width <= 0
            || texture.Height <= 0)
        {
            return false;
        }

        var format = ToDxgiFormat(texture.PixelFormat);
        if (format == Format.Unknown)
        {
            return false;
        }

        try
        {
            var sharedHandle = texture.SharedHandle;
            if (sharedHandle == IntPtr.Zero)
            {
                sharedHandle = device.OpenSharedHandleByName(texture.SharedHandleName);
            }

            var resource = device.OpenSharedHandle<ID3D12Resource>(sharedHandle);
            resource.Name = $"Aquarium external sensor texture {texture.Handle.Name}";
            externalTexture = new D3D12ExternalSensorTexture(
                CacheKey(texture),
                sharedHandle,
                resource,
                texture.Width,
                texture.Height,
                format,
                texture.TimestampNs);
            return true;
        }
        catch (SharpGenException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static string CacheKey(AquariumExternalGpuTexture texture)
    {
        return texture.SharedHandle != IntPtr.Zero
            ? $"handle:{texture.SharedHandle.ToInt64():x}"
            : $"name:{texture.SharedHandleName}";
    }

    public static Format ToDxgiFormat(AquariumGpuSensorPixelFormat format)
    {
        return format switch
        {
            AquariumGpuSensorPixelFormat.Bgra8Unorm => Format.B8G8R8A8_UNorm,
            AquariumGpuSensorPixelFormat.Rgba8Unorm => Format.R8G8B8A8_UNorm,
            AquariumGpuSensorPixelFormat.R8Unorm => Format.R8_UNorm,
            AquariumGpuSensorPixelFormat.R16Unorm => Format.R16_UNorm,
            AquariumGpuSensorPixelFormat.R16Float => Format.R16_Float,
            AquariumGpuSensorPixelFormat.Rg8Unorm => Format.R8G8_UNorm,
            AquariumGpuSensorPixelFormat.LeapPackedMap => Format.R8G8B8A8_UNorm,
            _ => Format.Unknown,
        };
    }

    public void Dispose()
    {
        Resource.Dispose();
    }
}

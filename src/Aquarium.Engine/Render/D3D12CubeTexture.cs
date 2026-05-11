using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Aquarium.Engine.Render;

internal sealed unsafe class D3D12CubeTexture : IDisposable
{
    private const uint DdsMagic = 0x20534444;
    private const uint DdsHeaderSize = 124;
    private const uint DdsPixelFormatSize = 32;
    private const uint DdsFourCcDx10 = 0x30315844;
    private const int DxgiFormatR16G16B16A16Float = 10;
    private const int D3D10ResourceDimensionTexture2D = 3;
    private const int D3D11ResourceMiscTextureCube = 0x4;
    private const int BytesPerPixel = 8;
    private const int FaceCount = 6;
    private const int DdsDx10PayloadOffset = 148;

    private D3D12CubeTexture(ID3D12Resource resource, int size, int mipCount, string name)
    {
        Resource = resource;
        Size = size;
        MipCount = mipCount;
        Resource.Name = name;
    }

    public ID3D12Resource Resource { get; }

    public int Size { get; }

    public int MipCount { get; }

    public ResourceStates State { get; private set; } = ResourceStates.CopyDest;

    public static D3D12CubeTexture LoadRgba16FloatDds(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        string path,
        string name,
        out ID3D12Resource uploadResource)
    {
        var bytes = File.ReadAllBytes(path);
        var dds = ParseDds(bytes, path);
        var texture = device.CreateCommittedResource(
            HeapType.Default,
            ResourceDescription.Texture2D(
                Format.R16G16B16A16_Float,
                (uint)dds.Width,
                (uint)dds.Height,
                FaceCount,
                (ushort)dds.MipCount,
                1,
                0,
                ResourceFlags.None),
            ResourceStates.CopyDest,
            null);
        var cubeTexture = new D3D12CubeTexture(texture, dds.Width, dds.MipCount, name);

        uploadResource = CreateUploadResource(device, dds, bytes);
        CopySubresources(commandList, cubeTexture.Resource, uploadResource, dds);
        cubeTexture.Transition(commandList, ResourceStates.PixelShaderResource);
        return cubeTexture;
    }

    public void CreateShaderResourceView(ID3D12Device device, D3D12DescriptorSlot descriptor)
    {
        device.CreateShaderResourceView(
            Resource,
            new ShaderResourceViewDescription
            {
                Format = Format.R16G16B16A16_Float,
                ViewDimension = ShaderResourceViewDimension.TextureCube,
                Shader4ComponentMapping = ShaderComponentMapping.Default,
                TextureCube = new TextureCubeShaderResourceView
                {
                    MostDetailedMip = 0,
                    MipLevels = (uint)MipCount,
                    ResourceMinLODClamp = 0.0f,
                },
            },
            descriptor.Cpu);
    }

    public void Dispose()
    {
        Resource.Dispose();
    }

    private void Transition(ID3D12GraphicsCommandList commandList, ResourceStates nextState)
    {
        if (State == nextState)
        {
            return;
        }

        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(Resource, State, nextState));
        State = nextState;
    }

    private static DdsLayout ParseDds(ReadOnlySpan<byte> bytes, string path)
    {
        if (bytes.Length < DdsDx10PayloadOffset)
        {
            throw new InvalidDataException($"DDS file '{path}' is too small.");
        }

        if (ReadUInt32(bytes, 0) != DdsMagic || ReadUInt32(bytes, 4) != DdsHeaderSize)
        {
            throw new InvalidDataException($"DDS file '{path}' is not a supported DDS texture.");
        }

        var height = checked((int)ReadUInt32(bytes, 12));
        var width = checked((int)ReadUInt32(bytes, 16));
        var mipCount = checked((int)ReadUInt32(bytes, 28));
        var pixelFormatSize = ReadUInt32(bytes, 76);
        var fourCc = ReadUInt32(bytes, 84);
        var dxgiFormat = checked((int)ReadUInt32(bytes, 128));
        var resourceDimension = checked((int)ReadUInt32(bytes, 132));
        var miscFlags = checked((int)ReadUInt32(bytes, 136));
        var arraySize = checked((int)ReadUInt32(bytes, 140));

        if (width <= 0 || height != width || mipCount <= 0)
        {
            throw new InvalidDataException($"DDS file '{path}' must be a square cubemap with mips.");
        }

        if (pixelFormatSize != DdsPixelFormatSize
            || fourCc != DdsFourCcDx10
            || dxgiFormat != DxgiFormatR16G16B16A16Float
            || resourceDimension != D3D10ResourceDimensionTexture2D
            || (miscFlags & D3D11ResourceMiscTextureCube) == 0
            || arraySize != 1)
        {
            throw new InvalidDataException($"DDS file '{path}' must be a DX10 R16G16B16A16_FLOAT cubemap.");
        }

        var subresources = new DdsSubresource[FaceCount * mipCount];
        var sourceOffset = DdsDx10PayloadOffset;
        for (var face = 0; face < FaceCount; face++)
        {
            for (var mip = 0; mip < mipCount; mip++)
            {
                var mipWidth = Math.Max(1, width >> mip);
                var mipHeight = Math.Max(1, height >> mip);
                var rowBytes = mipWidth * BytesPerPixel;
                var dataBytes = rowBytes * mipHeight;
                if (sourceOffset + dataBytes > bytes.Length)
                {
                    throw new InvalidDataException($"DDS file '{path}' ended before face {face}, mip {mip}.");
                }

                subresources[(face * mipCount) + mip] = new DdsSubresource(sourceOffset, mipWidth, mipHeight, rowBytes);
                sourceOffset += dataBytes;
            }
        }

        if (sourceOffset != bytes.Length)
        {
            throw new InvalidDataException($"DDS file '{path}' has {bytes.Length - sourceOffset} unexpected trailing bytes.");
        }

        return new DdsLayout(width, height, mipCount, subresources);
    }

    private static ID3D12Resource CreateUploadResource(ID3D12Device device, DdsLayout dds, ReadOnlySpan<byte> source)
    {
        var uploadBytes = 0L;
        foreach (var subresource in dds.Subresources)
        {
            uploadBytes = Align(uploadBytes, D3D12.TextureDataPlacementAlignment);
            subresource.UploadOffset = uploadBytes;
            subresource.UploadRowPitch = checked((int)Align(subresource.RowBytes, D3D12.TextureDataPitchAlignment));
            uploadBytes += (long)subresource.UploadRowPitch * subresource.Height;
        }

        var upload = device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer((ulong)uploadBytes),
            ResourceStates.GenericRead,
            null);
        upload.Name = "Aquarium D3D12 Studio PMREM Upload";

        var mapped = (byte*)upload.Map<byte>(0);
        try
        {
            foreach (var subresource in dds.Subresources)
            {
                for (var row = 0; row < subresource.Height; row++)
                {
                    var sourceStart = subresource.SourceOffset + (row * subresource.RowBytes);
                    var destinationStart = subresource.UploadOffset + ((long)row * subresource.UploadRowPitch);
                    source.Slice(sourceStart, subresource.RowBytes).CopyTo(new Span<byte>(mapped + destinationStart, subresource.RowBytes));
                }
            }
        }
        finally
        {
            upload.Unmap(0, null);
        }

        return upload;
    }

    private static void CopySubresources(ID3D12GraphicsCommandList commandList, ID3D12Resource texture, ID3D12Resource upload, DdsLayout dds)
    {
        for (var subresourceIndex = 0; subresourceIndex < dds.Subresources.Length; subresourceIndex++)
        {
            var subresource = dds.Subresources[subresourceIndex];
            var source = new TextureCopyLocation(
                upload,
                new PlacedSubresourceFootPrint
                {
                    Offset = (ulong)subresource.UploadOffset,
                    Footprint = new SubresourceFootPrint(
                        Format.R16G16B16A16_Float,
                        (uint)subresource.Width,
                        (uint)subresource.Height,
                        1,
                        (uint)subresource.UploadRowPitch),
                });
            var destination = new TextureCopyLocation(texture, (uint)subresourceIndex);
            commandList.CopyTextureRegion(destination, 0, 0, 0, source, null);
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        return BitConverter.ToUInt32(bytes.Slice(offset, sizeof(uint)));
    }

    private static long Align(long value, long alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    private sealed record DdsLayout(int Width, int Height, int MipCount, DdsSubresource[] Subresources);

    private sealed record DdsSubresource(int SourceOffset, int Width, int Height, int RowBytes)
    {
        public long UploadOffset { get; set; }

        public int UploadRowPitch { get; set; }
    }
}

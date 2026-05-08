using System.Numerics;
using GameCult.Caching;
using GameCult.Caching.MessagePack;
using GameCult.Networking;

namespace Aquarium.Engine.State;

public sealed class AquariumCultStateStore : IDisposable
{
    public const string DefaultRelativePath = "artifacts/dev-reload/cultcache/aquarium-client.msgpack";
    private static readonly CultRecordKey LiveStateKey = new("aquarium-client/live-state");

    private readonly CultCache cache;
    private readonly SingleFileMessagePackBackingStore backingStore;

    private AquariumCultStateStore(string path)
    {
        CachePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);

        cache = new CultCache();
        backingStore = new SingleFileMessagePackBackingStore(CachePath);
        cache.AddBackingStore(backingStore);
        cache.PullAllBackingStoresAsync().GetAwaiter().GetResult();
        Hello = CreateHello(cache.Registry);
    }

    public string CachePath { get; }

    public CultNetHelloMessage Hello { get; }

    public static AquariumCultStateStore Open(string? path)
    {
        return new AquariumCultStateStore(string.IsNullOrWhiteSpace(path) ? DefaultRelativePath : path);
    }

    public AquariumLiveState LoadOrCreate()
    {
        var state = cache.Get<AquariumLiveState>(LiveStateKey)
            ?? cache.GetByName<AquariumLiveState>(AquariumLiveState.PrimaryName);

        if (state != null)
        {
            return state;
        }

        state = new AquariumLiveState();
        Save(state);
        return state;
    }

    public void Save(AquariumLiveState state)
    {
        state.Name = AquariumLiveState.PrimaryName;
        state.SaveGeneration++;
        cache.AddAsync(state, new CultRecordHandle<AquariumLiveState>(LiveStateKey)).GetAwaiter().GetResult();
        backingStore.PushAll();
    }

    public void Dispose()
    {
        cache.Dispose();
    }

    private static CultNetHelloMessage CreateHello(CultDocumentRegistry registry)
    {
        return new CultNetHelloMessage
        {
            RuntimeId = "aquarium-client",
            RuntimeKind = "aquarium-engine",
            Role = "visual-client",
            DisplayName = "Epiphany Aquarium",
            SupportsSchemaCatalog = true,
            SupportedDocumentTypes = registry.AllDescriptors
                .Select(descriptor => descriptor.SchemaName)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            SupportedMessageVersions =
            [
                CultNetSchemaVersions.Hello,
                CultNetSchemaVersions.DocumentPutRaw,
                CultNetSchemaVersions.DocumentDelete,
                CultNetSchemaVersions.SnapshotRequest,
                CultNetSchemaVersions.SnapshotResponseRaw,
                CultNetSchemaVersions.SchemaCatalogRequest,
                CultNetSchemaVersions.SchemaCatalogResponse
            ]
        };
    }
}

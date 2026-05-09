using System.Numerics;
using GameCult.Caching;
using GameCult.Caching.MessagePack;
using GameCult.Networking;
using MessagePack;

namespace Aquarium.Engine.State;

public sealed class AquariumCultStateStore : IDisposable
{
    public const string DefaultRelativePath = "artifacts/dev-reload/cultcache/aquarium-client.msgpack";
    private static readonly CultRecordKey LiveStateKey = new("aquarium-client/live-state");
    private static readonly CultRecordKey GraphicsSettingsKey = new("aquarium-client/graphics-settings");

    private readonly CultCache cache;
    private readonly SingleFileMessagePackBackingStore backingStore;

    private AquariumCultStateStore(string path)
    {
        CachePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);

        cache = new CultCache();
        backingStore = new SingleFileMessagePackBackingStore(CachePath);
        cache.AddBackingStore(backingStore);
        try
        {
            cache.PullAllBackingStoresAsync().GetAwaiter().GetResult();
        }
        catch (Exception error) when (IsRecoverableBackingStoreFailure(error))
        {
            QuarantineUnreadableCache(CachePath, error);
        }

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

    public AquariumGraphicsSettingsState LoadOrCreateGraphicsSettings()
    {
        var state = cache.Get<AquariumGraphicsSettingsState>(GraphicsSettingsKey)
            ?? cache.GetByName<AquariumGraphicsSettingsState>(AquariumGraphicsSettingsState.PrimaryName);

        if (state != null)
        {
            return state;
        }

        state = AquariumGraphicsSettingsState.FromSettings(GraphicsSettings.Default);
        SaveGraphicsSettings(state);
        return state;
    }

    public void Save(AquariumLiveState state)
    {
        state.Name = AquariumLiveState.PrimaryName;
        state.SaveGeneration++;
        cache.AddAsync(state, new CultRecordHandle<AquariumLiveState>(LiveStateKey)).GetAwaiter().GetResult();
        backingStore.PushAll();
    }

    public void SaveGraphicsSettings(AquariumGraphicsSettingsState state)
    {
        var normalized = state.ToSettings();
        state.Name = AquariumGraphicsSettingsState.PrimaryName;
        state.RenderDebugMode = normalized.RenderDebugMode;
        state.SceneExposure = normalized.SceneExposure;
        state.BloomIntensity = normalized.BloomIntensity;
        state.BloomVeilIntensity = normalized.BloomVeilIntensity;
        state.SaveGeneration++;
        cache.AddAsync(state, new CultRecordHandle<AquariumGraphicsSettingsState>(GraphicsSettingsKey)).GetAwaiter().GetResult();
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

    private static bool IsRecoverableBackingStoreFailure(Exception error)
    {
        return error is EndOfStreamException
            or InvalidDataException
            or MessagePackSerializationException;
    }

    private static void QuarantineUnreadableCache(string cachePath, Exception error)
    {
        if (!File.Exists(cachePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(cachePath)!;
        var fileName = Path.GetFileName(cachePath);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var quarantinePath = Path.Combine(directory, $"{fileName}.corrupt-{stamp}");

        try
        {
            File.Move(cachePath, quarantinePath);
            Console.Error.WriteLine(
                $"CultCache snapshot was unreadable and has been quarantined: {quarantinePath}. {error.GetType().Name}: {error.Message}");
        }
        catch (Exception quarantineError)
        {
            var fallbackPath = Path.Combine(directory, $"{fileName}.corrupt-{stamp}-{Guid.NewGuid():N}");
            File.Move(cachePath, fallbackPath);
            Console.Error.WriteLine(
                $"CultCache snapshot was unreadable and quarantine path was busy; moved it to {fallbackPath}. "
                + $"{error.GetType().Name}: {error.Message}; quarantine retry after {quarantineError.GetType().Name}: {quarantineError.Message}");
        }
    }
}

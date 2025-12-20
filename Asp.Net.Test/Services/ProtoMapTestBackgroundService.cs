using CacheManager.Core;
using AspNetTest.Protos;

namespace Asp.Net.Test.Services;

/// <summary>
/// Simple background loop that writes/reads a protobuf-backed map entry to verify pooling end-to-end.
/// </summary>
public sealed class ProtoMapTestBackgroundService : BackgroundService
{
    private const string MapName = "test-records";
    private const int BatchFlushInterval = 5;
    private static readonly TimeSpan DefaultLoopDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BatchFlushDelay = TimeSpan.FromSeconds(6);

    private readonly ILogger<ProtoMapTestBackgroundService> _logger;
    private readonly ICacheStorage _cacheStorage;
    private IMap<string, TestRecord>? _map;
    private int _counter;

    public ProtoMapTestBackgroundService(ICacheStorage cacheStorage, ILogger<ProtoMapTestBackgroundService> logger)
    {
        _cacheStorage = cacheStorage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _map = _cacheStorage.GetOrCreateMapProtoBuf<string, TestRecord>(MapName, TimeSpan.FromMinutes(10));

        _map.OnBatchUpdate(entries =>
        {
            foreach (var entry in entries)
            {
                _logger.LogInformation("Batch updated entry: {Key}", entry.GetKey());
            }

            return Task.CompletedTask;
        });


        while (!stoppingToken.IsCancellationRequested)
        {
            await RunIterationAsync(stoppingToken);

            try
            {
                var currentCount = Volatile.Read(ref _counter);
                var shouldFlushBatch = currentCount > 0 && currentCount % BatchFlushInterval == 0;
                var delay = shouldFlushBatch ? BatchFlushDelay : DefaultLoopDelay;

                if (shouldFlushBatch)
                {
                    _logger.LogInformation(
                        "Batch cooldown hit after {UpdateCount} updates. Waiting {DelaySeconds}s so OnBatchUpdate can emit entries.",
                        currentCount,
                        delay.TotalSeconds);
                }

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunIterationAsync(CancellationToken cancellationToken)
    {
        if (_map == null)
        {
            return;
        }

        var key = "test-item";
        var value = new TestRecord
        {
            Id = key,
            Payload = $"Loop-{DateTime.UtcNow:O}",
            Counter = Interlocked.Increment(ref _counter),
            UpdatedAtTicks = DateTime.UtcNow.Ticks
        };

        await _map.SetValueAsync(key, value);

        var stored = await _map.GetValueAsync(key);
        if (stored == null)
        {
            return;
        }

        try
        {
            _logger.LogInformation(
                "Proto map round-trip succeeded (key: {Key}, counter: {Counter}, payload: {Payload})",
                stored.Id,
                stored.Counter,
                stored.Payload);
        }
        catch
        {
            // Ignore logging exceptions to keep loop alive.
        }
        finally
        {
            ProtobufObjectPool.Return(stored);
        }
    }
}

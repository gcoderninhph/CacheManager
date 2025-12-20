using System;
using System.Collections.Generic;

namespace CacheManager.Core;

internal sealed partial class RedisMap<TKey, TValue>
{
    private async Task TriggerAddHandlers(TKey key, TValue value)
    {
        await _lockObj.WaitAsync();
        try
        {
            foreach (var handler in _onAddHandlers)
            {
                await TryExecuteHandler(() => handler(key, value));
            }
        }
        finally
        {
            _lockObj.Release();
        }
    }

    private async Task TriggerUpdateHandlers(TKey key, TValue value)
    {
        await _lockObj.WaitAsync();
        try
        {
            foreach (var handler in _onUpdateHandlers)
            {
                await TryExecuteHandler(() => handler(key, value));
            }
        }
        finally
        {
            _lockObj.Release();
        }
    }

    private async Task TriggerRemoveHandlers(TKey key, TValue value)
    {
        await _lockObj.WaitAsync();
        try
        {
            foreach (var handler in _onRemoveHandlers)
            {
                await TryExecuteHandler(() => handler(key, value));
            }
        }
        finally
        {
            _lockObj.Release();
        }
    }

    private async Task TriggerClearHandlers()
    {
        await _lockObj.WaitAsync();
        try
        {
            foreach (var handler in _onClearHandlers)
            {
                await TryExecuteHandler(handler);
            }
        }
        finally
        {
            _lockObj.Release();
        }
    }

    private async Task TriggerBatchUpdateHandlers(IEnumerable<IEntry<TKey, TValue>> entries)
    {
        await _lockObj.WaitAsync();
        try
        {
            foreach (var handler in _onBatchUpdateHandlers)
            {
                await TryExecuteHandler(() => handler(entries));
            }
        }
        finally
        {
            _lockObj.Release();
        }
    }

    private async Task TriggerExpiredHandlers(TKey key, TValue value)
    {
        await _lockObj.WaitAsync();
        try
        {
            foreach (var handler in _onExpiredHandlers)
            {
                await TryExecuteHandler(async () => await handler(key, value));
            }
        }
        finally
        {
            _lockObj.Release();
        }   
    }

    private static async Task TryExecuteHandler(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            // Ignore handler exceptions
        }
    }
}

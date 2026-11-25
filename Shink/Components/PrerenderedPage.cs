using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Memory;

namespace Shink.Components;

public abstract class PrerenderedPage : ComponentBase
{
    [Inject] private IMemoryCache Cache { get; set; } = default!;

    // Only this small identifier crosses the wire. Catalogues can exceed the circuit message limit.
    [PersistentState] public Guid PageLoadId { get; set; }

    protected async Task<T> ReadPageDataAsync<T>(string name, string? account, Func<Task<T>> load)
    {
        if (PageLoadId == Guid.Empty) PageLoadId = Guid.NewGuid();
        var key = (GetType(), PageLoadId, name, account);
        if (RendererInfo.IsInteractive && Cache.TryGetValue<T>(key, out var restored))
        {
            Cache.Remove(key);
            return restored!;
        }

        var result = await load();
        if (!RendererInfo.IsInteractive)
        {
            Cache.Set(key, result, TimeSpan.FromMinutes(1));
        }

        return result;
    }
}

using StackExchange.Redis;

namespace JobForge.Orchestrator.Scheduling;

public sealed class LeaseService(IConnectionMultiplexer redis)
{
    private const string AcquireScript = """
        if redis.call('SET', KEYS[1], ARGV[1], 'NX', 'PX', ARGV[2]) then return 1 else return 0 end
        """;
    private const string RenewScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            redis.call('PEXPIRE', KEYS[1], ARGV[2]); return 1
        else return 0 end
        """;

    private readonly IDatabase _db = redis.GetDatabase();

    /// <summary>Adquire (NX) ou renova (compare-and-extend); devolve o leaseId detido ou null.</summary>
    public async Task<string?> AcquireOrRenewAsync(string name, string leaseId, TimeSpan ttl, CancellationToken ct)
    {
        var ms = (long)ttl.TotalMilliseconds;
        var renewed = (int)await _db.ScriptEvaluateAsync(RenewScript,
            [(RedisKey)name], [(RedisValue)leaseId, ms]);
        if (renewed == 1) return leaseId;

        var acquired = (int)await _db.ScriptEvaluateAsync(AcquireScript,
            [(RedisKey)name], [(RedisValue)leaseId, ms]);
        return acquired == 1 ? leaseId : null;
    }
}

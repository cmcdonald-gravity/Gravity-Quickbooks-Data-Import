using QbAuthServr.Models;

namespace QbAuthServr.Services.Storage;

public interface ITokenStore
{
    Task SaveAsync(string? realmId, RealmAuth data);
    Task<RealmAuth?> GetAsync(string? realmId);
}
using MongoDB.Driver;

namespace RaceDay.Shared.Infrastructure.Persistence.Mongo;

public interface IMongoDbContext
{
    IMongoDatabase Database { get; }
    IMongoCollection<T> GetCollection<T>(string name);
}

public sealed class MongoDbContext(IMongoDatabase database) : IMongoDbContext
{
    public IMongoDatabase Database { get; } = database;

    public IMongoCollection<T> GetCollection<T>(string name)
        => Database.GetCollection<T>(name);
}

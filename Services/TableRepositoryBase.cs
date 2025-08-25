using Azure;
using Azure.Data.Tables;
using System.Linq.Expressions;

namespace retailMvcDemo.Services
{
    public class TableRepositoryBase<T> where T : class, ITableEntity, new()
    {
        protected readonly TableClient _table;

        public TableRepositoryBase(string connStr, string tableName)
        {
            _table = new TableClient(connStr, tableName);
            _table.CreateIfNotExists();
        }

        public Task AddAsync(T entity) => _table.AddEntityAsync(entity);

        public async Task<T?> GetAsync(string pk, string rk)
        {
            try { var r = await _table.GetEntityAsync<T>(pk, rk); return r.Value; }
            catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
        }

        // NEW: get everything (no filter)
        public AsyncPageable<T> QueryAllAsync() => _table.QueryAsync<T>();

        // NEW: fast partition scan by equality
        public IAsyncEnumerable<T> QueryByPartitionAsync(string partitionKey) =>
            _table.QueryAsync<T>(e => e.PartitionKey == partitionKey);

        // Keep this for other equality/range filters you may need
        public IAsyncEnumerable<T> QueryAsync(Expression<Func<T, bool>> filter) =>
            _table.QueryAsync<T>(filter);

        public Task UpdateAsync(T entity) =>
            _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);

        public Task DeleteAsync(string pk, string rk) =>
            _table.DeleteEntityAsync(pk, rk);
    }
}

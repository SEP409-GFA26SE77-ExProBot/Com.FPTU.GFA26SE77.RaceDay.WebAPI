using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using RaceDay.Shared.Domain.Exceptions;
using Npgsql;

namespace RaceDay.Modules.Event.Infrastructure.Persistence;

public sealed class EventUnitOfWork(EventDbContext dbContext) : IEventUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu đã thay đổi. Tải lại trạng thái trước khi tiếp tục; nếu retry hãy giữ nguyên requestId.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23505", TableName: "runner_operations" })
        {
            throw new ConflictException("Thao tác đang được xử lý đồng thời. Retry với cùng requestId và nội dung ban đầu.");
        }
    }
}

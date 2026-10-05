using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Event.Application.Abstractions;

/// <summary>Event-module unit of work (own Postgres schema) — do not share IAM IUnitOfWork.</summary>
public interface IEventUnitOfWork : IUnitOfWork;

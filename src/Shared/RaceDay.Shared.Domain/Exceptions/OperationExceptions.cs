namespace RaceDay.Shared.Domain.Exceptions;

public sealed class ConflictException(string message) : DomainException(message);
public sealed class ForbiddenException(string message) : DomainException(message);
public sealed class NotFoundException(string message) : DomainException(message);

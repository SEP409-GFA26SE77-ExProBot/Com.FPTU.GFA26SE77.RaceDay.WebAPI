using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations;

internal static class RaceMonitoringConfiguration
{
    public static EntityTypeBuilder<TEntity> AsMonitoringRecord<TEntity>(this EntityTypeBuilder<TEntity> builder, string table)
        where TEntity : Entity
    {
        builder.ToTable(table);
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.DomainEvents);
        return builder;
    }

    public static EntityTypeBuilder<TEntity> AsMonitoringRecord<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string table,
        Action<TableBuilder<TEntity>> tableBuilder)
        where TEntity : Entity
    {
        builder.ToTable(table, tableBuilder);
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.DomainEvents);
        return builder;
    }

    public static PropertyBuilder<TEnum> AsStoredEnum<TEnum>(this PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum
        => property.HasConversion<string>().HasMaxLength(CoordinatorFieldLengths.EnumValue).IsRequired();

    public static PropertyBuilder<TEnum?> AsStoredEnum<TEnum>(this PropertyBuilder<TEnum?> property)
        where TEnum : struct, Enum
        => property.HasConversion<string>().HasMaxLength(CoordinatorFieldLengths.EnumValue);
}

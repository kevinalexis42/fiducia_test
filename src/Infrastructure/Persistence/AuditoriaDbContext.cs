using CoreFid.Auditoria.Domain.Auditoria;
using CoreFid.Auditoria.Domain.Auditoria.ValueObjects;
using CoreFid.Auditoria.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CoreFid.Auditoria.Infrastructure.Persistence;

public sealed class AuditoriaDbContext : DbContext
{
    public AuditoriaDbContext(DbContextOptions<AuditoriaDbContext> options) : base(options)
    {
    }

    public DbSet<RegistroAuditoria> Auditorias => Set<RegistroAuditoria>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegistroAuditoria>(builder =>
        {
            builder.ToTable("auditoria");
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Id).HasColumnName("id").UseIdentityAlwaysColumn();

            builder.Property(a => a.CodigoEmpresa)
                .HasColumnName("codigo_empresa")
                .HasMaxLength(CodigoEmpresa.LongitudMaxima)
                .HasConversion(v => v.Valor, v => CodigoEmpresa.Desde(v))
                .IsRequired();

            builder.Property(a => a.CodigoModulo)
                .HasColumnName("codigo_modulo")
                .HasMaxLength(2)
                .HasConversion(v => v!.Valor, v => CodigoModulo.Desde(v))
                .IsRequired(false);

            builder.Property(a => a.CodigoTransaccion).HasColumnName("codigo_transaccion").HasMaxLength(10);
            builder.Property(a => a.Usuario).HasColumnName("usuario").HasMaxLength(30).IsRequired();
            builder.Property(a => a.Entidad).HasColumnName("entidad").HasMaxLength(60).IsRequired();
            builder.Property(a => a.ClaveEntidad).HasColumnName("clave_entidad").HasMaxLength(60).IsRequired();

            builder.Property(a => a.Accion)
                .HasColumnName("accion")
                .HasMaxLength(1)
                .HasConversion(v => v.Valor, v => Accion.Desde(v))
                .IsRequired();

            builder.Property(a => a.ValorAnterior).HasColumnName("valor_anterior").HasColumnType("text");
            builder.Property(a => a.ValorNuevo).HasColumnName("valor_nuevo").HasColumnType("text");
            builder.Property(a => a.DireccionIp).HasColumnName("direccion_ip").HasMaxLength(45);

            builder.Property(a => a.Canal)
                .HasColumnName("canal")
                .HasMaxLength(5)
                .HasConversion(v => v.Valor, v => Canal.Desde(v))
                .IsRequired();

            builder.Property(a => a.FechaRegistro).HasColumnName("fecha_registro").IsRequired();

            builder.Ignore(a => a.DomainEvents);
            builder.Ignore(a => a.EsAccionSensible);

            builder.HasIndex(a => new { a.CodigoEmpresa, a.FechaRegistro })
                .HasDatabaseName("ix_auditoria_empresa_fecha")
                .IsDescending(false, true);
            builder.HasIndex(a => new { a.CodigoEmpresa, a.Usuario }).HasDatabaseName("ix_auditoria_empresa_usuario");
        });

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(m => m.Tipo).HasColumnName("tipo").HasMaxLength(200).IsRequired();
            builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            builder.Property(m => m.OccurredAt).HasColumnName("occurred_at").IsRequired();
            builder.Property(m => m.ProcessedAt).HasColumnName("processed_at");
            builder.Property(m => m.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            builder.Property(m => m.Error).HasColumnName("error").HasMaxLength(2000);
            builder.Property(m => m.NextAttemptAt).HasColumnName("next_attempt_at");

            builder.HasIndex(m => new { m.NextAttemptAt, m.OccurredAt })
                .HasDatabaseName("ix_outbox_pendientes")
                .HasFilter("processed_at IS NULL");
        });
    }
}

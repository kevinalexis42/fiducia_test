using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Application.Auditoria.Commands;
using CoreFid.Auditoria.Domain.Auditoria;
using CoreFid.Auditoria.Domain.Auditoria.Events;
using CoreFid.Auditoria.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CoreFid.Auditoria.Tests.Unit.Application;

public class RegistrarAuditoriaHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 16, 15, 30, 0, TimeSpan.Zero);

    private sealed class RepositorioEnMemoria : IAuditoriaRepository, IUnitOfWork
    {
        private long _secuencia;
        public List<RegistroAuditoria> Registros { get; } = new();
        public List<IDomainEvent> EventosEnOutbox { get; } = new();
        public int Commits { get; private set; }

        public void Agregar(RegistroAuditoria registro) => Registros.Add(registro);

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            foreach (var registro in Registros.Where(r => r.Id == 0))
            {
                typeof(RegistroAuditoria).GetProperty(nameof(RegistroAuditoria.Id))!.SetValue(registro, ++_secuencia);
                registro.ConfirmarRegistro();
                EventosEnOutbox.AddRange(registro.DomainEvents);
                registro.ClearDomainEvents();
            }
            Commits++;
            return Task.CompletedTask;
        }
    }

    private static RegistrarAuditoriaCommand ComandoValido(string accion = "C", string? valorAnterior = null, string? canal = "API") =>
        new("0001", "06", "V062014", "jperez", "SolicitudRescate", "RES-2026-004871",
            accion, valorAnterior, "{\"estado\":\"APROBADA\"}", "10.0.4.77", canal);

    private static (RegistrarAuditoriaHandler handler, RepositorioEnMemoria repo) CrearHandler()
    {
        var repo = new RepositorioEnMemoria();
        var reloj = new FakeTimeProvider(Ahora);
        var handler = new RegistrarAuditoriaHandler(repo, repo, reloj, NullLogger<RegistrarAuditoriaHandler>.Instance);
        return (handler, repo);
    }

    [Fact]
    public async Task Comando_valido_persiste_confirma_y_devuelve_el_id()
    {
        var (handler, repo) = CrearHandler();

        var id = await handler.Handle(ComandoValido(), CancellationToken.None);

        Assert.Equal(1, id);
        Assert.Equal(1, repo.Commits);
        var registro = Assert.Single(repo.Registros);
        Assert.Equal(Ahora, registro.FechaRegistro);
        var evento = Assert.IsType<AuditoriaRegistrada>(Assert.Single(repo.EventosEnOutbox));
        Assert.Equal(id, evento.AuditoriaId);
        Assert.True(evento.EsAccionSensible);
    }

    [Fact]
    public async Task La_fecha_viene_del_reloj_inyectado_no_del_sistema()
    {
        var repo = new RepositorioEnMemoria();
        var reloj = new FakeTimeProvider(new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var handler = new RegistrarAuditoriaHandler(repo, repo, reloj, NullLogger<RegistrarAuditoriaHandler>.Instance);

        await handler.Handle(ComandoValido(), CancellationToken.None);

        Assert.Equal(2001, repo.Registros.Single().FechaRegistro.Year);
    }

    [Fact]
    public async Task Comando_invalido_no_toca_el_repositorio_ni_hace_commit()
    {
        var (handler, repo) = CrearHandler();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(ComandoValido(accion: "U", valorAnterior: null), CancellationToken.None));

        Assert.Equal(CodigosError.ValorAnteriorRequerido, ex.Codigo);
        Assert.Empty(repo.Registros);
        Assert.Equal(0, repo.Commits);
        Assert.Empty(repo.EventosEnOutbox);
    }

    [Fact]
    public async Task Regla_BATCH_Q_vive_en_el_dominio_no_en_el_controlador()
    {
        var (handler, repo) = CrearHandler();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(ComandoValido(accion: "Q", canal: "BATCH"), CancellationToken.None));

        Assert.Equal(CodigosError.BatchNoRegistraConsultas, ex.Codigo);
        Assert.Equal(0, repo.Commits);
    }
}

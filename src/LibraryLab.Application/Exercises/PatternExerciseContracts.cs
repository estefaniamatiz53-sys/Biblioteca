using System.Security.Cryptography;
using System.Text;
using LibraryLab.Application.Contracts;
using LibraryLab.Application.Services;
using LibraryLab.Domain.Entities;
using LibraryLab.Domain.Enums;
using LibraryLab.Domain.Exceptions;

namespace LibraryLab.Application.Exercises;

// ============================================================================
// EJERCICIO 1 - FACTORY METHOD (creacional)
//
// El dominio exige crear un Loan a partir de un CreateLoanCommand, pero el
// comando no transporta el Id ni el instante del préstamo. La decisión de como
// se produce ese Loan (y de que variante depende segun el tipo de recurso)
// queda aislada detras de un Creator, de modo que el cliente dependa del
// contrato y no del proceso de construccion.
// ============================================================================

/// <summary>
/// Creator abstracto. Expone el metodo de plantilla y delega la construccion
/// real en la operacion de fabrica <see cref="CreateLoan"/>, que cada Creator
/// concreto redefine.
/// </summary>
public abstract class LoanCreatorExercise
{
    private readonly TimeProvider _timeProvider;
    private readonly Func<Guid> _idFactory;

    protected LoanCreatorExercise(TimeProvider timeProvider, Func<Guid> idFactory)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _idFactory = idFactory ?? throw new ArgumentNullException(nameof(idFactory));
    }

    public Loan Create(CreateLoanCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CreateLoan(command, _idFactory(), _timeProvider.GetUtcNow());
    }

    /// <summary>Metodo de fabrica: cada Creator concreto decide que Loan produce.</summary>
    protected abstract Loan CreateLoan(CreateLoanCommand command, Guid id, DateTimeOffset borrowedAt);
}

/// <summary>Creator concreto para prestamos de libros.</summary>
public sealed class BookLoanCreator(TimeProvider timeProvider, Func<Guid> idFactory)
    : LoanCreatorExercise(timeProvider, idFactory)
{
    protected override Loan CreateLoan(CreateLoanCommand command, Guid id, DateTimeOffset borrowedAt) =>
        new(id, command.UserId, command.ResourceId, ResourceKind.Book, borrowedAt, command.DueAt);
}

/// <summary>Creator concreto para prestamos de equipos.</summary>
public sealed class EquipmentLoanCreator(TimeProvider timeProvider, Func<Guid> idFactory)
    : LoanCreatorExercise(timeProvider, idFactory)
{
    protected override Loan CreateLoan(CreateLoanCommand command, Guid id, DateTimeOffset borrowedAt) =>
        new(id, command.UserId, command.ResourceId, ResourceKind.Equipment, borrowedAt, command.DueAt);
}

public interface ILoanFactoryExercise
{
    Loan Create(CreateLoanCommand command, ResourceKind resourceKind);
}

/// <summary>
/// Cliente del patron: resuelve el Creator concreto segun el tipo de recurso y
/// delega en el. El reloj y la generacion de Id se inyectan para que el instante
/// del prestamo sea determinista y verificable en pruebas.
/// </summary>
public sealed class LoanFactoryExercise : ILoanFactoryExercise
{
    private readonly TimeProvider _timeProvider;
    private readonly Func<Guid> _idFactory;

    public LoanFactoryExercise() : this(TimeProvider.System, Guid.NewGuid) { }

    public LoanFactoryExercise(TimeProvider timeProvider, Func<Guid> idFactory)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _idFactory = idFactory ?? throw new ArgumentNullException(nameof(idFactory));
    }

    public Loan Create(CreateLoanCommand command, ResourceKind resourceKind)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CreatorFor(resourceKind).Create(command);
    }

    private LoanCreatorExercise CreatorFor(ResourceKind resourceKind) => resourceKind switch
    {
        ResourceKind.Book => new BookLoanCreator(_timeProvider, _idFactory),
        ResourceKind.Equipment => new EquipmentLoanCreator(_timeProvider, _idFactory),
        _ => throw new DomainRuleException($"El tipo de recurso {resourceKind} no esta soportado.")
    };
}

// ============================================================================
// EJERCICIO 2 - BUILDER (creacional)
//
// El recibo de prestamo se arma por etapas y su invariante se verifica en el
// paso final, no en cada llamada intermedia. La API fluida permite dejar
// expressly pendiente un dato y fallar una sola vez, al construir.
// ============================================================================

public interface ILoanReceiptBuilderExercise
{
    LoanReceipt Build(Guid loanId, Guid resourceId, DateTimeOffset dueAt);
}

/// <summary>
/// Builder por etapas. Los pasos son fluidos y el estado se valida en Build(),
/// que es el unico punto donde se conoce el recibo completo.
/// </summary>
public sealed class LoanReceiptBuilderExercise : ILoanReceiptBuilderExercise
{
    private Guid? _loanId;
    private Guid? _resourceId;
    private DateTimeOffset? _dueAt;

    public LoanReceiptBuilderExercise WithLoan(Guid loanId)
    {
        _loanId = loanId;
        return this;
    }

    public LoanReceiptBuilderExercise WithResource(Guid resourceId)
    {
        _resourceId = resourceId;
        return this;
    }

    public LoanReceiptBuilderExercise DueOn(DateTimeOffset dueAt)
    {
        _dueAt = dueAt;
        return this;
    }

    /// <summary>Paso final: valida la invariante completa y produce el recibo.</summary>
    public LoanReceipt Build()
    {
        var loanId = _loanId ?? throw new InvalidOperationException("Falta el identificador del prestamo.");
        var resourceId = _resourceId ?? throw new InvalidOperationException("Falta el identificador del recurso.");
        var dueAt = _dueAt ?? throw new InvalidOperationException("Falta la fecha limite del prestamo.");

        if (loanId == Guid.Empty)
            throw new InvalidOperationException("El identificador del prestamo no puede estar vacio.");
        if (resourceId == Guid.Empty)
            throw new InvalidOperationException("El identificador del recurso no puede estar vacio.");

        return new LoanReceipt(loanId, resourceId, dueAt);
    }

    /// <summary>
    /// Atajo del contrato: encadena las tres etapas y construye en un paso.
    /// Mismo invariante, mismo punto de validacion.
    /// </summary>
    public LoanReceipt Build(Guid loanId, Guid resourceId, DateTimeOffset dueAt) =>
        new LoanReceiptBuilderExercise()
            .WithLoan(loanId)
            .WithResource(resourceId)
            .DueOn(dueAt)
            .Build();
}

// ============================================================================
// EJERCICIO 3 - ADAPTER (estructural)
//
// El catalogo antiguo entrega InventoryCode/DisplayName/Category; la importacion
// de LibraryLab exige Id/InventoryCode/Name/Category. El Adapter traduce el
// contrato sin que ninguno de los dos lados conozca al otro.
// ============================================================================

public sealed record LegacyCatalogRecord(string InventoryCode, string DisplayName, string Category);
public sealed record CatalogImport(Guid Id, string InventoryCode, string Name, string Category);

/// <summary>
/// Deriva un identificador estable a partir de una clave de negocio, de modo que
/// reimportar el mismo registro produzca el mismo Id y la importacion sea
/// idempotente. Usa SHA-256 y marca el Guid como version 5; no se usa
/// new Guid(hashString) porque es un MD5 heredado y produce colisiones.
/// </summary>
internal static class DeterministicGuid
{
    public static Guid From(string seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(seed), hash);

        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5 (basada en nombre)
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // variante RFC 4122
        return new Guid(guidBytes);
    }
}

public interface ILegacyCatalogAdapterExercise
{
    CatalogImport Adapt(LegacyCatalogRecord record);
}

/// <summary>
/// Adapter de un solo sentido. Renombra el campo de presentacion y genera el Id
/// que el registro antiguo no trae, a partir de su codigo de inventario.
/// </summary>
public sealed class LegacyCatalogAdapterExercise : ILegacyCatalogAdapterExercise
{
    public CatalogImport Adapt(LegacyCatalogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.InventoryCode))
            throw new ArgumentException(
                "El codigo de inventario del registro antiguo es obligatorio.", nameof(record));

        return new CatalogImport(
            DeterministicGuid.From(record.InventoryCode),
            record.InventoryCode,
            record.DisplayName,
            record.Category);
    }
}

// ============================================================================
// EJERCICIO 4 - DECORATOR (estructural)
//
// El aviso de prestamo necesita una concern adicional: dejar auditoria. El
// Decorator implementa la misma interfaz que decora, de modo que el cliente no
// distingue el emisor real de su envoltorio y puede encadenar más capas.
// ============================================================================

public sealed record LoanReceipt(Guid LoanId, Guid ResourceId, DateTimeOffset DueAt);
public sealed record LoanNotice(Guid LoanId, string Recipient);

public interface ILoanNoticeSender
{
    Task SendAsync(LoanNotice notice, CancellationToken cancellationToken);
}

public interface ILoanNoticeAudit
{
    Task RecordAsync(LoanNotice notice, CancellationToken cancellationToken);
}

/// <summary>
/// Decorator que anade auditoria al envio. Delega primero y registra despues:
/// la auditoria refleja lo que ocurrio, no lo que se intento. Si el envio falla,
/// no queda registro de nada enviado.
/// </summary>
public sealed class LoanNoticeDecoratorExercise(ILoanNoticeSender sender, ILoanNoticeAudit audit) : ILoanNoticeSender
{
    public async Task SendAsync(LoanNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(notice);

        await sender.SendAsync(notice, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(notice, cancellationToken).ConfigureAwait(false);
    }
}

// ============================================================================
// EJERCICIO 5 - FACADE (estructural)
//
// El resumen de un miembro exige combinar usuario, prestamos y recursos. La
// fachada coordina esas consultas y entrega un unico resultado coherente,
// ocultando el grafo de repositorios al cliente.
// ============================================================================

public sealed record MemberOverview(Guid UserId, string UserName, IReadOnlyList<LoanReceipt> ActiveLoans);

public interface IMemberOverviewFacadeExercise
{
    Task<MemberOverview> GetAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Fachada de solo lectura. Resuelve el miembro, toma sus prestamos activos y
/// descarta los que apuntan a un recurso que ya no existe, para no devolver
/// referencias colgantes.
/// </summary>
public sealed class MemberOverviewFacadeExercise(
    ILibraryUserRepository users,
    ILoanRepository loans,
    ILibraryResourceRepository resources) : IMemberOverviewFacadeExercise
{
    public async Task<MemberOverview> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(loans);
        ArgumentNullException.ThrowIfNull(resources);

        var user = await users.FindAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApplicationNotFoundException($"El usuario {userId} no existe.");

        var loansDelUsuario = await loans.ListAsync(cancellationToken).ConfigureAwait(false);

        var receipts = new List<LoanReceipt>();
        foreach (var loan in loansDelUsuario)
        {
            if (loan.UserId != userId || loan.Status != LoanStatus.Active)
                continue;

            var recurso = await resources.FindAsync(loan.ResourceId, cancellationToken).ConfigureAwait(false);
            if (recurso is null)
                continue; // referencia colgante: el recurso fue eliminado.

            receipts.Add(new LoanReceipt(loan.Id, loan.ResourceId, loan.DueAt));
        }

        return new MemberOverview(user.Id, user.Name, receipts);
    }
}
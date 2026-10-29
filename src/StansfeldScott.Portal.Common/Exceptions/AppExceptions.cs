namespace StansfeldScott.Portal.Common.Exceptions;

/// <summary>Thrown by a Repository when a requested record does not exist.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>Thrown by a Repository when input fails a business rule
/// (e.g. bulk-upload SKU validation, case-pack limits).</summary>
public class ValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ValidationException(string message, IReadOnlyList<string>? errors = null) : base(message)
    {
        Errors = errors ?? new List<string> { message };
    }
}

/// <summary>Thrown when a call through ICrmRepository / CrmApiClient to the
/// CRM's API (via Azure API Management) fails or times out.</summary>
public class CrmIntegrationException : Exception
{
    public CrmIntegrationException(string message, Exception? inner = null) : base(message, inner) { }
}

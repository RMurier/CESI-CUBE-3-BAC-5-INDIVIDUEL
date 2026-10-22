namespace Collector.Observability;

/// <summary>
/// EF Core (et beaucoup d'autres couches) enrobent systématiquement l'exception réelle dans un
/// wrapper au message générique et inutile ("An error occurred while saving the entity changes.
/// See the inner exception for details.") — la vraie cause est dans l'InnerException, parfois
/// plusieurs niveaux plus bas (EF Core -> SqlException -> Win32Exception, par ex.). Centralise
/// l'extraction pour que message et stack trace envoyés à Omnia soient systématiquement exploitables,
/// sans avoir à aller chercher manuellement dans le payload à chaque fois.
/// </summary>
public static class ExceptionDetails
{
    /// <summary>Message de l'exception la plus profonde (la cause racine), pas celui du wrapper externe.</summary>
    public static string RootMessage(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }
        return current.Message;
    }
}

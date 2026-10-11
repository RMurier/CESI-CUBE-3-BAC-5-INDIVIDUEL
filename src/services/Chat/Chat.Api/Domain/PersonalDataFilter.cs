using System.Text.RegularExpressions;

namespace Chat.Api.Domain;

/// <summary>
/// Exigence contexte Collector : "le système doit empêcher l'échange d'informations personnelles
/// telles que l'email, un numéro de téléphone" (anti-contournement du paiement via la plateforme).
/// Heuristique regex pour le POC — pas une garantie absolue (ex: obfuscation volontaire "gm ail"),
/// à durcir dans le plan de remédiation sécurité si jugé prioritaire.
/// </summary>
public static partial class PersonalDataFilter
{
    public static string Sanitize(string content)
    {
        content = EmailPattern().Replace(content, "[email masqué]");
        content = PhonePattern().Replace(content, "[téléphone masqué]");
        return content;
    }

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(\+33|0)[\s.-]?[1-9]([\s.-]?\d{2}){4}")]
    private static partial Regex PhonePattern();
}

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// A freshly-issued invite, returned to the parent who asked for it and to
/// nobody else, ever again. Only the selector and a PBKDF2 hash of the secret
/// half are stored, so <see cref="Code"/> cannot be re-read from the database
/// afterwards — if it is lost, the parent issues another one.
/// </summary>
/// <param name="Code">The whole code, selector + secret, unformatted.</param>
/// <param name="ExpiresAt">UTC. Shown to the parent so they know how long they have.</param>
public record DeviceInviteIssued(string Code, DateTime ExpiresAt);

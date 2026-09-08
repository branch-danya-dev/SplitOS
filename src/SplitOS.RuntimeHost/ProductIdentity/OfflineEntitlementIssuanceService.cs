using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SplitOS.Persistence.ProtectedSecrets;

namespace SplitOS.RuntimeHost.ProductIdentity;

public sealed record OfflineEntitlementIssuanceConfiguration(Uri Endpoint)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        if (!Endpoint.IsAbsoluteUri ||
            !string.Equals(Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(Endpoint.UserInfo) ||
            !string.IsNullOrEmpty(Endpoint.Query) ||
            !string.IsNullOrEmpty(Endpoint.Fragment) ||
            !string.Equals(Endpoint.AbsolutePath, "/v1/entitlements/offline-assertion", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Offline entitlement endpoint must be the exact release-owned HTTPS /v1/entitlements/offline-assertion endpoint.",
                nameof(Endpoint));
        }
    }
}

public sealed record OfflineEntitlementIssuanceContext(
    string AccessToken,
    string ClientVersion,
    string InstallationId,
    Guid CorrelationId,
    string AccountId,
    string AssociationId,
    long MinimumEntitlementVersion,
    IReadOnlyList<string> RequestedCapabilities)
{
    public void Validate()
    {
        ValidateText(AccessToken, 32 * 1024, nameof(AccessToken));
        ValidateText(ClientVersion, 128, nameof(ClientVersion));
        ValidateText(InstallationId, 256, nameof(InstallationId));
        ValidateText(AccountId, 512, nameof(AccountId));
        ValidateText(AssociationId, 128, nameof(AssociationId));
        if (CorrelationId == Guid.Empty) throw new ArgumentException("Correlation id is required.", nameof(CorrelationId));
        if (MinimumEntitlementVersion < 1) throw new ArgumentOutOfRangeException(nameof(MinimumEntitlementVersion));
        if (RequestedCapabilities is null || RequestedCapabilities.Count == 0 || RequestedCapabilities.Count > 16)
        {
            throw new ArgumentException("At least one and no more than sixteen requested capabilities are required.", nameof(RequestedCapabilities));
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in RequestedCapabilities)
        {
            ValidateText(capability, 128, nameof(RequestedCapabilities));
            if (capability.Any(char.IsWhiteSpace) || !unique.Add(capability))
            {
                throw new ArgumentException("Requested capabilities are malformed or duplicated.", nameof(RequestedCapabilities));
            }
        }
    }

    public override string ToString()
        => $"OfflineEntitlementIssuanceContext(AccessToken=<redacted>, AccountId={AccountId}, AssociationId={AssociationId}, InstallationId={InstallationId}, CorrelationId={CorrelationId:D})";

    private static void ValidateText(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new ArgumentException($"{parameterName} is missing or outside supported bounds.", parameterName);
        }
    }
}

public enum OfflineEntitlementIssuanceDisposition
{
    Stored,
    AuthRequired,
    NotEligible,
    InstallationLimitReached,
    EntitlementNotActive,
    CapabilityNotAllowed,
    BackendUnavailable,
    Rejected,
    MalformedResponse,
    LocalSecretUnavailable,
    ValidationRejected,
    PersistenceFailed
}

public sealed record OfflineEntitlementIssuanceResult(
    OfflineEntitlementIssuanceDisposition Disposition,
    string ProductCode,
    bool Retryable,
    long? EntitlementVersion,
    DateTimeOffset? ExpiresUtc)
{
    public bool IsStored => Disposition == OfflineEntitlementIssuanceDisposition.Stored;
}

public sealed class OfflineEntitlementIssuanceService
{
    private const int MaximumResponseBytes = 96 * 1024;
    private const int MaximumErrorBytes = 32 * 1024;
    private const int MaximumAssertionCharacters = 32 * 1024;
    private readonly HttpClient _httpClient;
    private readonly OfflineEntitlementIssuanceConfiguration _configuration;
    private readonly OfflineEntitlementAssertionValidator _validator;
    private readonly IAccountSecretStore _secretStore;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _allowedClockSkew;
    private int _issuanceInProgress;

    public OfflineEntitlementIssuanceService(
        HttpClient httpClient,
        OfflineEntitlementIssuanceConfiguration configuration,
        OfflineEntitlementAssertionValidator validator,
        IAccountSecretStore secretStore,
        TimeProvider? timeProvider = null,
        TimeSpan? allowedClockSkew = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _allowedClockSkew = allowedClockSkew ?? TimeSpan.FromMinutes(5);
        _configuration.Validate();
        if (_allowedClockSkew < TimeSpan.Zero || _allowedClockSkew > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(allowedClockSkew));
        }
    }

    public async Task<OfflineEntitlementIssuanceResult> IssueAndStoreAsync(
        OfflineEntitlementIssuanceContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();
        if (Interlocked.CompareExchange(ref _issuanceInProgress, 1, 0) != 0)
        {
            return Reject(OfflineEntitlementIssuanceDisposition.Rejected, "OFFLINE_ASSERTION_ISSUANCE_ALREADY_IN_PROGRESS", false);
        }

        try
        {
            return await IssueAndStoreCoreAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _issuanceInProgress, 0);
        }
    }

    private async Task<OfflineEntitlementIssuanceResult> IssueAndStoreCoreAsync(
        OfflineEntitlementIssuanceContext context,
        CancellationToken cancellationToken)
    {
        AccountSecretReadResult secretRead;
        try
        {
            secretRead = await _secretStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsLocalFailure(exception))
        {
            return Reject(OfflineEntitlementIssuanceDisposition.LocalSecretUnavailable, "LOCAL_SECRET_UNREADABLE", false);
        }

        if (secretRead.Status != AccountSecretReadStatus.Available || secretRead.Secret is null)
        {
            return Reject(OfflineEntitlementIssuanceDisposition.LocalSecretUnavailable,
                secretRead.Status == AccountSecretReadStatus.Missing ? "LOCAL_SECRET_MISSING" : "LOCAL_SECRET_UNREADABLE", false);
        }

        var secret = secretRead.Secret;
        if (!string.Equals(secret.AccountId, context.AccountId, StringComparison.Ordinal))
        {
            return Reject(OfflineEntitlementIssuanceDisposition.LocalSecretUnavailable, "LOCAL_SECRET_ACCOUNT_MISMATCH", false);
        }

        HttpResponseMessage response;
        try
        {
            using var request = CreateRequest(context);
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or OperationCanceledException)
        {
            return Reject(OfflineEntitlementIssuanceDisposition.BackendUnavailable, "TEMPORARILY_UNAVAILABLE", true);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await MapFailureAsync(response, cancellationToken).ConfigureAwait(false);
            }

            OfflineAssertionEnvelope envelope;
            try
            {
                envelope = await ParseSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return Reject(OfflineEntitlementIssuanceDisposition.MalformedResponse, "OFFLINE_ASSERTION_RESPONSE_INVALID", false);
            }

            var now = _timeProvider.GetUtcNow();
            if (envelope.ServerUtc > now.Add(_allowedClockSkew))
            {
                return Reject(OfflineEntitlementIssuanceDisposition.MalformedResponse, "OFFLINE_ASSERTION_SERVER_CLOCK_INVALID", false);
            }

            if (secret.LastTrustedServerUtc is DateTimeOffset previousTrusted &&
                envelope.ServerUtc < previousTrusted.Subtract(_allowedClockSkew))
            {
                return Reject(OfflineEntitlementIssuanceDisposition.ValidationRejected, "TRUSTED_SERVER_TIME_ROLLBACK", false);
            }

            var validation = await _validator.ValidateAsync(
                envelope.Assertion,
                new OfflineEntitlementValidationContext(
                    context.AccountId,
                    context.AssociationId,
                    context.InstallationId,
                    context.RequestedCapabilities[0],
                    Math.Max(context.MinimumEntitlementVersion, envelope.EntitlementVersion),
                    secret.LastTrustedServerUtc,
                    secret.LastTrustedServerObservationLocalUtc),
                cancellationToken).ConfigureAwait(false);

            if (!validation.IsAccepted || validation.Claims is null)
            {
                return Reject(OfflineEntitlementIssuanceDisposition.ValidationRejected, validation.ProductCode, false);
            }

            var claims = validation.Claims;
            if (claims.EntitlementVersion != envelope.EntitlementVersion ||
                claims.IssuedUtc != envelope.IssuedUtc ||
                claims.ExpiresUtc != envelope.ExpiresUtc ||
                context.RequestedCapabilities.Any(capability => !claims.HasCapability(capability)))
            {
                return Reject(OfflineEntitlementIssuanceDisposition.ValidationRejected, "OFFLINE_ASSERTION_RESPONSE_MISMATCH", false);
            }

            var trustedServerUtc = secret.LastTrustedServerUtc is DateTimeOffset existing && existing > envelope.ServerUtc
                ? existing
                : envelope.ServerUtc;

            var updated = new AccountSecretEnvelope
            {
                AccountId = secret.AccountId,
                RefreshToken = secret.RefreshToken,
                RefreshTokenFamilyId = secret.RefreshTokenFamilyId,
                RefreshIssuedUtc = secret.RefreshIssuedUtc,
                RefreshAbsoluteExpiryUtc = secret.RefreshAbsoluteExpiryUtc,
                LastTrustedServerUtc = trustedServerUtc,
                LastTrustedServerObservationLocalUtc = now,
                LastValidAssertionJti = claims.AssertionId,
                OfflineEntitlementAssertion = envelope.Assertion,
                OfflineAssertionStoredUtc = now
            };

            try
            {
                await _secretStore.WriteAsync(updated, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsLocalFailure(exception))
            {
                return Reject(OfflineEntitlementIssuanceDisposition.PersistenceFailed, "OFFLINE_ASSERTION_LOCAL_STORE_FAILED", false);
            }

            return new OfflineEntitlementIssuanceResult(
                OfflineEntitlementIssuanceDisposition.Stored,
                "OFFLINE_ASSERTION_STORED",
                false,
                envelope.EntitlementVersion,
                envelope.ExpiresUtc);
        }
    }

    private HttpRequestMessage CreateRequest(OfflineEntitlementIssuanceContext context)
    {
        var body = JsonSerializer.Serialize(new
        {
            installationId = context.InstallationId,
            associationId = context.AssociationId,
            requestedCapabilities = context.RequestedCapabilities
        });
        var request = new HttpRequestMessage(HttpMethod.Post, _configuration.Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-SplitOS-Client-Version", context.ClientVersion);
        request.Headers.TryAddWithoutValidation("X-SplitOS-Installation-Id", context.InstallationId);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", context.CorrelationId.ToString("D"));
        return request;
    }

    private static async Task<OfflineAssertionEnvelope> ParseSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var root = await ReadBoundedJsonAsync(response, MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        RequireObject(root);
        EnsureNoDuplicateProperties(root);
        var assertion = RequiredString(root, "assertion", MaximumAssertionCharacters);
        if (assertion.Any(char.IsWhiteSpace)) throw new InvalidDataException("Assertion contains whitespace.");
        var issued = RequiredUtc(root, "issuedUtc");
        var expires = RequiredUtc(root, "expiresUtc");
        if (expires <= issued) throw new InvalidDataException("Assertion response lifetime is inconsistent.");
        var version = RequiredPositiveInt64(root, "entitlementVersion");
        var serverUtc = RequiredUtc(root, "serverUtc");
        return new OfflineAssertionEnvelope(assertion, issued, expires, version, serverUtc);
    }

    private static async Task<OfflineEntitlementIssuanceResult> MapFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var code = await TryReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return Reject(OfflineEntitlementIssuanceDisposition.AuthRequired, code == "TOKEN_REVOKED" ? code : "AUTH_REQUIRED", false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            return Reject(OfflineEntitlementIssuanceDisposition.BackendUnavailable, code ?? "TEMPORARILY_UNAVAILABLE", true);

        return code switch
        {
            "OFFLINE_NOT_ELIGIBLE" => Reject(OfflineEntitlementIssuanceDisposition.NotEligible, code, false),
            "INSTALLATION_LIMIT_REACHED" => Reject(OfflineEntitlementIssuanceDisposition.InstallationLimitReached, code, false),
            "ENTITLEMENT_NOT_ACTIVE" => Reject(OfflineEntitlementIssuanceDisposition.EntitlementNotActive, code, false),
            "CAPABILITY_NOT_ALLOWED" => Reject(OfflineEntitlementIssuanceDisposition.CapabilityNotAllowed, code, false),
            _ => Reject(OfflineEntitlementIssuanceDisposition.Rejected, code ?? "OFFLINE_ASSERTION_REQUEST_REJECTED", false)
        };
    }

    private static async Task<string?> TryReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var root = await ReadBoundedJsonAsync(response, MaximumErrorBytes, cancellationToken).ConfigureAwait(false);
            RequireObject(root);
            EnsureNoDuplicateProperties(root);
            if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return null;
            EnsureNoDuplicateProperties(error);
            return RequiredString(error, "code", 128);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static async Task<JsonElement> ReadBoundedJsonAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
            !mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Response is not JSON.");
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Response exceeded supported bounds.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > maxBytes) throw new InvalidDataException("Response exceeded supported bounds.");
                memory.Write(rented, 0, read);
            }

            if (!memory.TryGetBuffer(out var buffer) || buffer.Array is null) throw new InvalidDataException("Response buffer unavailable.");
            using var document = JsonDocument.Parse(new ReadOnlyMemory<byte>(buffer.Array, buffer.Offset, checked((int)memory.Length)),
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 24 });
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Response JSON malformed.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
            if (memory.TryGetBuffer(out var buffer) && buffer.Array is not null)
                CryptographicOperations.ZeroMemory(buffer.Array.AsSpan(buffer.Offset, checked((int)memory.Length)));
        }
    }

    private static void RequireObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Response must be an object.");
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Response contains duplicate properties.");
    }

    private static string RequiredString(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String ||
            element.GetString() is not string value || string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw new InvalidDataException($"{name} is malformed.");
        return value;
    }

    private static long RequiredPositiveInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt64(out var value) || value < 1) throw new InvalidDataException($"{name} is malformed.");
        return value;
    }

    private static DateTimeOffset RequiredUtc(JsonElement root, string name)
    {
        var value = RequiredString(root, name, 64);
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            throw new InvalidDataException($"{name} is malformed.");
        return parsed;
    }

    private static bool IsLocalFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException;

    private static OfflineEntitlementIssuanceResult Reject(OfflineEntitlementIssuanceDisposition disposition, string code, bool retryable)
        => new(disposition, code, retryable, null, null);

    private sealed record OfflineAssertionEnvelope(
        string Assertion,
        DateTimeOffset IssuedUtc,
        DateTimeOffset ExpiresUtc,
        long EntitlementVersion,
        DateTimeOffset ServerUtc);
}

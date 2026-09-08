using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace SplitOS.RuntimeHost.ProductIdentity;

public sealed record ProductApiRequestContext(
    string AccessToken,
    string ClientVersion,
    string InstallationId,
    Guid CorrelationId)
{
    public void Validate()
    {
        ValidateProtocolValue(AccessToken, 32 * 1024, nameof(AccessToken));
        ValidateProtocolValue(ClientVersion, 128, nameof(ClientVersion));
        ValidateProtocolValue(InstallationId, 256, nameof(InstallationId));
        if (CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation id must not be empty.", nameof(CorrelationId));
        }
    }

    public override string ToString()
        => $"ProductApiRequestContext(AccessToken=<redacted>, ClientVersion={ClientVersion}, InstallationId={InstallationId}, CorrelationId={CorrelationId:D})";

    private static void ValidateProtocolValue(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException($"{parameterName} is missing or outside the supported bounds.", parameterName);
        }
    }
}

public enum ProductApiDisposition
{
    Accepted,
    AuthRequired,
    AccountDisabled,
    NotFound,
    RateLimited,
    BackendUnavailable,
    Rejected,
    MalformedResponse
}

public sealed record ProductApiResult<T>(
    ProductApiDisposition Disposition,
    string ProductCode,
    bool Retryable,
    T? Value)
    where T : class;

public sealed record SplitOSAccountProfile(
    string AccountId,
    string Status,
    string? DisplayName,
    string? Email,
    bool EmailVerified,
    DateTimeOffset CreatedUtc);

public sealed record SplitOSEntitlementSnapshot(
    string AccountId,
    long EntitlementVersion,
    string Plan,
    string Status,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    IReadOnlyList<string> Capabilities,
    bool OfflineEligible,
    DateTimeOffset ServerUtc)
{
    public bool HasCapability(string capability)
        => Capabilities.Contains(capability, StringComparer.Ordinal);
}

public sealed class ProductApiClient
{
    private const int MaximumAccountResponseBytes = 64 * 1024;
    private const int MaximumEntitlementResponseBytes = 128 * 1024;
    private const int MaximumErrorResponseBytes = 32 * 1024;
    private const int MaximumAccountIdLength = 512;
    private const int MaximumDisplayValueLength = 1024;
    private const int MaximumCapabilityCount = 64;
    private const int MaximumCapabilityLength = 128;

    private static readonly HashSet<string> SupportedAccountStatuses = new(StringComparer.Ordinal)
    {
        "ACTIVE",
        "DISABLED"
    };

    private static readonly HashSet<string> SupportedPlans = new(StringComparer.Ordinal)
    {
        "FREE",
        "PRO"
    };

    private static readonly HashSet<string> SupportedEntitlementStatuses = new(StringComparer.Ordinal)
    {
        "ACTIVE",
        "EXPIRED",
        "SUSPENDED",
        "CANCELLED_AT_PERIOD_END"
    };

    private readonly HttpClient _httpClient;
    private readonly ProductApiConfiguration _configuration;

    public ProductApiClient(HttpClient httpClient, ProductApiConfiguration configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();
    }

    public async Task<ProductApiResult<SplitOSAccountProfile>> GetAccountAsync(
        ProductApiRequestContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();

        using var request = CreateAuthenticatedGet(_configuration.AccountEndpoint, context);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsBackendFailure(exception))
        {
            return Reject<SplitOSAccountProfile>(
                ProductApiDisposition.BackendUnavailable,
                "TEMPORARILY_UNAVAILABLE",
                retryable: true);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await MapFailureAsync<SplitOSAccountProfile>(response, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var root = await ReadBoundedJsonAsync(
                    response,
                    MaximumAccountResponseBytes,
                    cancellationToken).ConfigureAwait(false);
                var profile = ParseAccount(root);
                return Accept(profile);
            }
            catch (InvalidDataException)
            {
                return Reject<SplitOSAccountProfile>(
                    ProductApiDisposition.MalformedResponse,
                    "ACCOUNT_RESPONSE_INVALID",
                    retryable: false);
            }
        }
    }

    public async Task<ProductApiResult<SplitOSEntitlementSnapshot>> GetCurrentEntitlementAsync(
        ProductApiRequestContext context,
        string expectedAccountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();
        ValidateBoundedText(expectedAccountId, MaximumAccountIdLength, "expectedAccountId");

        using var request = CreateAuthenticatedGet(_configuration.CurrentEntitlementEndpoint, context);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsBackendFailure(exception))
        {
            return Reject<SplitOSEntitlementSnapshot>(
                ProductApiDisposition.BackendUnavailable,
                "TEMPORARILY_UNAVAILABLE",
                retryable: true);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await MapFailureAsync<SplitOSEntitlementSnapshot>(response, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var root = await ReadBoundedJsonAsync(
                    response,
                    MaximumEntitlementResponseBytes,
                    cancellationToken).ConfigureAwait(false);
                var entitlement = ParseEntitlement(root);
                if (!string.Equals(entitlement.AccountId, expectedAccountId, StringComparison.Ordinal))
                {
                    return Reject<SplitOSEntitlementSnapshot>(
                        ProductApiDisposition.MalformedResponse,
                        "ENTITLEMENT_ACCOUNT_MISMATCH",
                        retryable: false);
                }

                return Accept(entitlement);
            }
            catch (InvalidDataException)
            {
                return Reject<SplitOSEntitlementSnapshot>(
                    ProductApiDisposition.MalformedResponse,
                    "ENTITLEMENT_RESPONSE_INVALID",
                    retryable: false);
            }
        }
    }

    private static HttpRequestMessage CreateAuthenticatedGet(Uri endpoint, ProductApiRequestContext context)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-SplitOS-Client-Version", context.ClientVersion);
        request.Headers.TryAddWithoutValidation("X-SplitOS-Installation-Id", context.InstallationId);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", context.CorrelationId.ToString("D"));
        return request;
    }

    private static SplitOSAccountProfile ParseAccount(JsonElement root)
    {
        RequireObject(root, "account response");
        EnsureNoDuplicatePropertiesRecursive(root, "account response");

        var accountId = GetRequiredBoundedString(root, "accountId", MaximumAccountIdLength);
        var status = GetRequiredBoundedString(root, "status", 64);
        if (!SupportedAccountStatuses.Contains(status))
        {
            throw new InvalidDataException("Account response contains an unsupported status.");
        }

        var displayName = GetOptionalBoundedString(root, "displayName", MaximumDisplayValueLength);
        var email = GetOptionalBoundedString(root, "email", MaximumDisplayValueLength);
        var emailVerified = GetRequiredBoolean(root, "emailVerified");
        var createdUtc = GetRequiredUtc(root, "createdUtc");

        return new SplitOSAccountProfile(
            accountId,
            status,
            displayName,
            email,
            emailVerified,
            createdUtc);
    }

    private static SplitOSEntitlementSnapshot ParseEntitlement(JsonElement root)
    {
        RequireObject(root, "entitlement response");
        EnsureNoDuplicatePropertiesRecursive(root, "entitlement response");

        var accountId = GetRequiredBoundedString(root, "accountId", MaximumAccountIdLength);
        if (!root.TryGetProperty("entitlementVersion", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt64(out var entitlementVersion) ||
            entitlementVersion < 1)
        {
            throw new InvalidDataException("Entitlement response omitted a valid positive entitlementVersion.");
        }

        var plan = GetRequiredBoundedString(root, "plan", 64);
        if (!SupportedPlans.Contains(plan))
        {
            throw new InvalidDataException("Entitlement response contains an unsupported plan.");
        }

        var status = GetRequiredBoundedString(root, "status", 64);
        if (!SupportedEntitlementStatuses.Contains(status))
        {
            throw new InvalidDataException("Entitlement response contains an unsupported status.");
        }

        var validFrom = GetOptionalUtc(root, "validFrom");
        var validUntil = GetOptionalUtc(root, "validUntil");
        if (validFrom is not null && validUntil is not null && validUntil <= validFrom)
        {
            throw new InvalidDataException("Entitlement validity interval is inconsistent.");
        }

        var capabilities = GetCapabilities(root);
        var offlineEligible = GetRequiredBoolean(root, "offlineEligible");
        var serverUtc = GetRequiredUtc(root, "serverUtc");

        return new SplitOSEntitlementSnapshot(
            accountId,
            entitlementVersion,
            plan,
            status,
            validFrom,
            validUntil,
            capabilities,
            offlineEligible,
            serverUtc);
    }

    private static IReadOnlyList<string> GetCapabilities(JsonElement root)
    {
        if (!root.TryGetProperty("capabilities", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Entitlement response omitted capabilities array.");
        }

        if (value.GetArrayLength() > MaximumCapabilityCount)
        {
            throw new InvalidDataException("Entitlement response contains too many capabilities.");
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Entitlement capability is malformed.");
            }

            var capability = item.GetString();
            ValidateBoundedText(capability, MaximumCapabilityLength, "capability");
            if (capability!.Any(static character => char.IsWhiteSpace(character)) || !unique.Add(capability))
            {
                throw new InvalidDataException("Entitlement capability list is ambiguous or malformed.");
            }

            result.Add(capability);
        }

        return result;
    }

    private static async Task<ProductApiResult<T>> MapFailureAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : class
    {
        var error = await TryReadErrorEnvelopeAsync(response, cancellationToken).ConfigureAwait(false);
        var code = error?.Code;
        var retryableHint = error?.Retryable == true;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return Reject<T>(
                ProductApiDisposition.AuthRequired,
                code is "TOKEN_REVOKED" ? "TOKEN_REVOKED" : "AUTH_REQUIRED",
                retryable: false);
        }

        if (string.Equals(code, "ACCOUNT_DISABLED", StringComparison.Ordinal))
        {
            return Reject<T>(ProductApiDisposition.AccountDisabled, code, retryable: false);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Reject<T>(ProductApiDisposition.NotFound, code ?? "RESOURCE_NOT_FOUND", retryable: false);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return Reject<T>(ProductApiDisposition.RateLimited, code ?? "RATE_LIMITED", retryable: true);
        }

        if ((int)response.StatusCode >= 500)
        {
            return Reject<T>(
                ProductApiDisposition.BackendUnavailable,
                code ?? "TEMPORARILY_UNAVAILABLE",
                retryable: true);
        }

        return Reject<T>(
            ProductApiDisposition.Rejected,
            code ?? "PRODUCT_API_REQUEST_REJECTED",
            retryableHint);
    }

    private static async Task<ProductApiError?> TryReadErrorEnvelopeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = await ReadBoundedJsonAsync(response, MaximumErrorResponseBytes, cancellationToken).ConfigureAwait(false);
            RequireObject(root, "product API error envelope");
            EnsureNoDuplicatePropertiesRecursive(root, "product API error envelope");
            if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var code = GetRequiredBoundedString(error, "code", 128);
            var retryable = error.TryGetProperty("retryable", out var retryableElement) &&
                            retryableElement.ValueKind == JsonValueKind.True;
            return new ProductApiError(code, retryable);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static async Task<JsonElement> ReadBoundedJsonAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
            !mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Product API response content type is not JSON.");
        }

        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > maximumBytes)
        {
            throw new InvalidDataException("Product API JSON response exceeded the allowed size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream(declaredLength is > 0 and <= int.MaxValue ? (int)declaredLength.Value : 4096);
        var rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > maximumBytes)
                {
                    throw new InvalidDataException("Product API JSON response exceeded the allowed size.");
                }

                memory.Write(rented, 0, read);
            }

            if (!memory.TryGetBuffer(out var segment) || segment.Array is null)
            {
                throw new InvalidDataException("Product API response buffer could not be inspected safely.");
            }

            using var document = JsonDocument.Parse(
                new ReadOnlyMemory<byte>(segment.Array, segment.Offset, checked((int)memory.Length)),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32
                });
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Product API JSON response was malformed.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
            if (memory.TryGetBuffer(out var buffer) && buffer.Array is not null)
            {
                CryptographicOperations.ZeroMemory(buffer.Array.AsSpan(buffer.Offset, checked((int)memory.Length)));
            }
        }
    }

    private static void RequireObject(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{description} must be a JSON object.");
        }
    }

    private static void EnsureNoDuplicatePropertiesRecursive(JsonElement value, string description)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException($"{description} contains duplicate property '{property.Name}'.");
                }

                EnsureNoDuplicatePropertiesRecursive(property.Value, description);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                EnsureNoDuplicatePropertiesRecursive(item, description);
            }
        }
    }

    private static string GetRequiredBoundedString(JsonElement root, string propertyName, int maximumLength)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Required string '{propertyName}' is missing or malformed.");
        }

        var result = value.GetString();
        ValidateBoundedText(result, maximumLength, propertyName);
        return result!;
    }

    private static string? GetOptionalBoundedString(JsonElement root, string propertyName, int maximumLength)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Optional string '{propertyName}' is malformed.");
        }

        var result = value.GetString();
        if (result is null) return null;
        if (result.Length > maximumLength || result.Any(static character => char.IsControl(character)))
        {
            throw new InvalidDataException($"Optional string '{propertyName}' is outside supported bounds.");
        }
        return result;
    }

    private static bool GetRequiredBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Required boolean '{propertyName}' is missing or malformed.");
        }

        return value.GetBoolean();
    }

    private static DateTimeOffset GetRequiredUtc(JsonElement root, string propertyName)
        => GetOptionalUtc(root, propertyName)
           ?? throw new InvalidDataException($"Required timestamp '{propertyName}' is missing.");

    private static DateTimeOffset? GetOptionalUtc(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp))
        {
            throw new InvalidDataException($"Timestamp '{propertyName}' is malformed.");
        }

        return timestamp.ToUniversalTime();
    }

    private static void ValidateBoundedText(string? value, int maximumLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(static character => char.IsControl(character)))
        {
            throw new InvalidDataException($"Field '{fieldName}' is missing or outside supported bounds.");
        }
    }

    private static bool IsBackendFailure(Exception exception)
        => exception is HttpRequestException or IOException or TimeoutException or OperationCanceledException;

    private static ProductApiResult<T> Accept<T>(T value)
        where T : class
        => new(ProductApiDisposition.Accepted, "OK", false, value);

    private static ProductApiResult<T> Reject<T>(
        ProductApiDisposition disposition,
        string productCode,
        bool retryable)
        where T : class
        => new(disposition, productCode, retryable, null);

    private sealed record ProductApiError(string Code, bool Retryable);
}
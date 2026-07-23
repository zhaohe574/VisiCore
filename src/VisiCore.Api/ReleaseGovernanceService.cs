using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisiCore.Persistence;

namespace VisiCore.Api;

/// <summary>
/// 发行治理记录只保存可公开查证的 GitHub 外链；运行时不访问 GitHub，也不持有其凭据。
/// </summary>
public sealed class ReleaseGovernanceOptions
{
    public string RepositoryUrl { get; init; } = "https://github.com/zhaohe574/VisiCore";
}

public sealed record RegisterReleaseGovernanceRecord(
    IReadOnlyList<string>? ChangeIds,
    string? DossierUrl,
    string? ReleaseUrl,
    string? WorkflowRunUrl,
    string? ReleaseEvidenceUrl,
    string? StagingEvidenceUrl,
    string? SbomUrl,
    string? ProvenanceUrl,
    string? VerificationUrl);

public sealed class ReleaseGovernanceService(
    PlatformDbContext dbContext,
    IOptions<ReleaseGovernanceOptions> options)
{
    private static readonly Regex ChangeIdPattern = new("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex WorkflowRunPath = new("/actions/runs/[1-9][0-9]*$", RegexOptions.CultureInvariant);

    public async Task<ReleaseGovernanceRecordEntity> RegisterAsync(
        Guid releaseCatalogId,
        RegisterReleaseGovernanceRecord request,
        string recordedBy,
        CancellationToken cancellationToken)
    {
        var release = await dbContext.ReleaseCatalog.SingleOrDefaultAsync(item => item.Id == releaseCatalogId, cancellationToken)
            ?? throw new ReleaseGovernanceException("release_catalog_not_found");
        if (await dbContext.ReleaseGovernanceRecords.AnyAsync(item => item.ReleaseCatalogId == releaseCatalogId, cancellationToken))
        {
            throw new ReleaseGovernanceException("governance_record_exists");
        }
        if (!ReleaseCatalogService.TryReadDescriptor(release, out var descriptor) || string.IsNullOrWhiteSpace(descriptor.SourceCommit))
        {
            throw new ReleaseGovernanceException("release_descriptor_invalid");
        }
        if (!TryGetRepository(out var repository))
        {
            throw new ReleaseGovernanceException("governance_repository_invalid");
        }

        var changeIds = NormalizeChangeIds(request.ChangeIds);
        var dossierUrl = RequireImmutableDocumentUrl(request.DossierUrl, repository, descriptor.SourceCommit, "dossier_url_invalid");
        var verificationUrl = RequireImmutableDocumentUrl(request.VerificationUrl, repository, descriptor.SourceCommit, "verification_url_invalid");
        var releaseUrl = RequireReleaseUrl(request.ReleaseUrl, repository, descriptor.ReleaseId, "release_url_invalid");
        var workflowRunUrl = RequireWorkflowRunUrl(request.WorkflowRunUrl, repository, "workflow_run_url_invalid");
        var releaseEvidenceUrl = RequireReleaseAssetUrl(request.ReleaseEvidenceUrl, repository, descriptor.ReleaseId, "release_evidence_url_invalid");
        var sbomUrl = RequireReleaseAssetUrl(request.SbomUrl, repository, descriptor.ReleaseId, "sbom_url_invalid");
        var stagingReleaseId = descriptor.Channel == "stable" ? descriptor.PromotedFrom : descriptor.ReleaseId;
        if (string.IsNullOrWhiteSpace(stagingReleaseId))
        {
            throw new ReleaseGovernanceException("stable_promotion_reference_missing");
        }
        var stagingEvidenceUrl = RequireReleaseAssetUrl(request.StagingEvidenceUrl, repository, stagingReleaseId, "staging_evidence_url_invalid");
        var provenanceUrl = RequireProvenanceUrl(request.ProvenanceUrl, repository, "provenance_url_invalid");
        var actor = NormalizeActor(recordedBy);

        var record = new ReleaseGovernanceRecordEntity
        {
            Id = Guid.NewGuid(),
            ReleaseCatalogId = release.Id,
            ChangeIdsJson = JsonSerializer.Serialize(changeIds),
            SourceCommit = descriptor.SourceCommit,
            DossierUrl = dossierUrl,
            ReleaseUrl = releaseUrl,
            WorkflowRunUrl = workflowRunUrl,
            ReleaseEvidenceUrl = releaseEvidenceUrl,
            StagingEvidenceUrl = stagingEvidenceUrl,
            SbomUrl = sbomUrl,
            ProvenanceUrl = provenanceUrl,
            VerificationUrl = verificationUrl,
            RecordedBy = actor,
            RecordedAt = DateTimeOffset.UtcNow
        };
        dbContext.ReleaseGovernanceRecords.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return record;
    }

    public static bool TryReadChangeIds(string? value, out IReadOnlyList<string> changeIds)
    {
        changeIds = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(value);
            if (parsed is null || parsed.Count is < 1 or > 64 ||
                parsed.Any(item => string.IsNullOrWhiteSpace(item) || !ChangeIdPattern.IsMatch(item)) ||
                parsed.Distinct(StringComparer.Ordinal).Count() != parsed.Count)
            {
                return false;
            }
            changeIds = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryGetRepository(out Uri repository)
    {
        repository = null!;
        var value = options.Value.RepositoryUrl;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) || candidate is null) return false;
        repository = candidate;
        return repository.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               repository.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(repository.AbsolutePath.Trim('/')) &&
               string.IsNullOrEmpty(repository.Query) && string.IsNullOrEmpty(repository.Fragment);
    }

    private static IReadOnlyList<string> NormalizeChangeIds(IReadOnlyList<string>? values)
    {
        var changeIds = values?.Select(item => item?.Trim() ?? string.Empty).ToList() ?? [];
        if (changeIds.Count is < 1 or > 64 || changeIds.Any(item => item.Length == 0 || !ChangeIdPattern.IsMatch(item)) ||
            changeIds.Distinct(StringComparer.Ordinal).Count() != changeIds.Count)
        {
            throw new ReleaseGovernanceException("change_ids_invalid");
        }
        return changeIds;
    }

    private static string NormalizeActor(string value)
    {
        var actor = value.Trim();
        if (actor.Length is < 1 or > 128) throw new ReleaseGovernanceException("recorded_by_invalid");
        return actor;
    }

    private static string RequireImmutableDocumentUrl(string? value, Uri repository, string sourceCommit, string failureKind)
    {
        var uri = RequireRepositoryUrl(value, repository, failureKind);
        var expectedPrefix = $"{RepositoryPath(repository)}/blob/{sourceCommit}/docs/releases/";
        if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal) || !uri.AbsolutePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseGovernanceException(failureKind);
        }
        return uri.AbsoluteUri;
    }

    private static string RequireReleaseUrl(string? value, Uri repository, string releaseId, string failureKind)
    {
        var uri = RequireRepositoryUrl(value, repository, failureKind);
        if (!string.Equals(uri.AbsolutePath, $"{RepositoryPath(repository)}/releases/tag/{releaseId}", StringComparison.Ordinal))
        {
            throw new ReleaseGovernanceException(failureKind);
        }
        return uri.AbsoluteUri;
    }

    private static string RequireWorkflowRunUrl(string? value, Uri repository, string failureKind)
    {
        var uri = RequireRepositoryUrl(value, repository, failureKind);
        if (!uri.AbsolutePath.StartsWith($"{RepositoryPath(repository)}/", StringComparison.Ordinal) || !WorkflowRunPath.IsMatch(uri.AbsolutePath))
        {
            throw new ReleaseGovernanceException(failureKind);
        }
        return uri.AbsoluteUri;
    }

    private static string RequireReleaseAssetUrl(string? value, Uri repository, string releaseId, string failureKind)
    {
        var uri = RequireRepositoryUrl(value, repository, failureKind);
        var expectedPrefix = $"{RepositoryPath(repository)}/releases/download/{releaseId}/";
        if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal) || uri.AbsolutePath.Length == expectedPrefix.Length)
        {
            throw new ReleaseGovernanceException(failureKind);
        }
        return uri.AbsoluteUri;
    }

    private static string RequireProvenanceUrl(string? value, Uri repository, string failureKind)
    {
        var uri = RequireRepositoryUrl(value, repository, failureKind);
        var prefix = RepositoryPath(repository);
        if (!uri.AbsolutePath.StartsWith($"{prefix}/attestations/", StringComparison.Ordinal) &&
            !WorkflowRunPath.IsMatch(uri.AbsolutePath))
        {
            throw new ReleaseGovernanceException(failureKind);
        }
        return uri.AbsoluteUri;
    }

    private static Uri RequireRepositoryUrl(string? value, Uri repository, string failureKind)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(repository.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.AbsolutePath.StartsWith($"{RepositoryPath(repository)}/", StringComparison.Ordinal))
        {
            throw new ReleaseGovernanceException(failureKind);
        }
        return uri;
    }

    private static string RepositoryPath(Uri repository) => repository.AbsolutePath.TrimEnd('/');
}

public sealed class ReleaseGovernanceException(string failureKind) : InvalidOperationException(failureKind)
{
    public string FailureKind { get; } = failureKind;
}

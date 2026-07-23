/// <summary>
/// 升级仪表允许的失败码。升级记录保留完整摘要，指标标签只接受有限的稳定集合。
/// </summary>
public static class UpgradeFailureMetricCode
{
    public const string Unspecified = "unspecified_failure";
    public const string Unclassified = "unclassified_failure";

    private static readonly HashSet<string> KnownCodes = new(StringComparer.Ordinal)
    {
        "release_descriptor_expired",
        "core_host_agent_not_initialized",
        "core_upgrade_exchange_invalid",
        "core_host_receipt_invalid",
        "core_upgrade_message_invalid",
        "core_upgrade_failed",
        "edge_upgrade_failed",
        "reboot_required",
        "core_release_not_available_for_host",
        "upgrade_target_invalid",
        "core_known_good_release_missing",
        "core_release_pointer_write_failed",
        "core_upgrade_apply_failed",
        "core_upgrade_failed_rolled_back",
        "core_upgrade_rollback_failed",
        "core_upgrade_descriptor_invalid",
        "core_upgrade_target_invalid",
        "minimum_host_version_not_met",
        "core_volume_continuity_configuration_invalid",
        "core_volume_continuity_current_container_missing",
        "core_volume_continuity_mismatch",
        "docker_start_failed",
        "docker_command_failed",
        "docker_command_timeout"
    };

    public static string Normalize(string? failureSummary)
    {
        if (string.IsNullOrWhiteSpace(failureSummary))
        {
            return Unspecified;
        }

        var normalized = failureSummary.Trim().ToLowerInvariant();
        return KnownCodes.Contains(normalized) ? normalized : Unclassified;
    }
}

namespace knkwebapi_v2.Configuration;

/// <summary>
/// GDPR deletion (KNG-34 D12, DESIGN.md §F.14, L1-18). A request is due
/// <see cref="DeletionDueDays"/> after it is recorded; the daily job executes requests still pending
/// <see cref="AutoExecuteBeforeDueDays"/> days before their due date unless
/// <see cref="AutoExecuteEnabled"/> is false.
/// </summary>
public class PrivacyOptions
{
    public const string SectionName = "Privacy";

    public int DeletionDueDays { get; set; } = 30;

    public bool AutoExecuteEnabled { get; set; } = true;

    public int AutoExecuteBeforeDueDays { get; set; } = 3;
}

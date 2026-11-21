namespace MC.Erp.Common.Infrastructure.Ocr;

/// <summary>
/// 考勤卡（微電腦打卡鐘月卡）的逐行打卡資料。只映射打卡明細需要的欄位，
/// 頂部的年份、月份和姓名通常手寫，置信度普遍低於紅字印刷的打卡時間。
/// </summary>
public sealed record AttendanceCard(
    OcrField<int?> Year,
    OcrField<int?> Month,
    OcrField<string?> Name,
    IReadOnlyList<AttendanceRow> Records,
    IReadOnlyList<string> ReviewFields)
{
    /// <summary>本身或任何一列有欄位需人工複核。</summary>
    public bool NeedsReview => ReviewFields.Count > 0 || Records.Any(row => row.NeedsReview);
}

/// <summary>單日打卡；時間已正規化為 HH:mm，未打卡為 null，每欄都帶置信度。</summary>
public sealed record AttendanceRow(
    int Day,
    OcrField<string?> AmIn,
    OcrField<string?> AmOut,
    OcrField<string?> PmIn,
    OcrField<string?> PmOut,
    OcrField<string?> OtIn,
    OcrField<string?> OtOut)
{
    /// <summary>本列有欄位置信度低於閾值。</summary>
    public bool NeedsReview => ReviewFields.Count > 0;

    /// <summary>低於閾值的欄位名（amIn / amOut / pmIn / pmOut / otIn / otOut，與前端同名）。</summary>
    public IReadOnlyList<string> ReviewFields { get; init; } = [];
}

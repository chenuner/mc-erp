namespace MC.Erp.Common;

/// <summary>統一分頁結果，序列化為 { items, total, page, pageSize }。</summary>
public sealed record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

/// <summary>帶合計的分頁結果，序列化為 { items, total, page, pageSize, totals }。
/// totals 按整份篩選結果計算（不是當前頁），前端合計行直接用它。</summary>
public sealed record PagedResult<T, TTotals>(List<T> Items, int Total, int Page, int PageSize, TTotals Totals);

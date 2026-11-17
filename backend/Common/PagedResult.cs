namespace MC.Erp.Common;

/// <summary>統一分頁結果，序列化為 { items, total, page, pageSize }。</summary>
public sealed record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

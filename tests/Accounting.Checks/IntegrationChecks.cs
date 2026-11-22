using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using MC.Erp.Common.Security;
using MC.Erp.Features.Accounting.Endpoints;
using MC.Erp.Features.Accounting.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

internal static class IntegrationChecks
{
    public static async Task RunAsync()
    {
        var backend = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../backend"));
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(backend, "appsettings.json"))
            .AddJsonFile(Path.Combine(backend, "appsettings.Development.json"), true).AddEnvironmentVariables().Build();
        var connectionString = configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Database configuration missing");
        var databaseName = $"mc_erp_accounting_check_{Guid.NewGuid():N}";
        var temporaryRoot = Path.Combine(Path.GetTempPath(), databaseName);
        Directory.CreateDirectory(temporaryRoot);
        await using var adminConnection = new NpgsqlConnection(connectionString);
        await adminConnection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", adminConnection)) await create.ExecuteNonQueryAsync();
        var isolatedConnection = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = backend, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(backend, "bin/Release/net10.0/MC.Erp.dll"));
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__Default"] = isolatedConnection;
        start.Environment["Storage__RootPath"] = temporaryRoot;
        start.Environment["AZURE_DOC_KEY"] = "";
        start.Environment["AZURE_DOC_ENDPOINT"] = "";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start backend");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var count = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"API check failed: {name}");
            count++;
        }
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/api/"), Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            var ready = false;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (process.HasExited) throw new InvalidOperationException($"Backend exited: {await standardError}");
                try { using var health = await client.GetAsync("../health"); ready = health.IsSuccessStatusCode; }
                catch (HttpRequestException) { }
                if (ready) break;
                await Task.Delay(500);
            }
            Check(ready, "startup and migrations");
            using (var denied = await client.GetAsync("accounting/receipts")) Check(denied.StatusCode == HttpStatusCode.Unauthorized, "anonymous rejected");
            using var login = await client.PostAsJsonAsync("identity/login", new { username = "admin", password = "Admin@123" });
            login.EnsureSuccessStatusCode();
            var auth = await login.Content.ReadFromJsonAsync<JsonElement>();
            var token = auth.GetProperty("token").GetString()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            async Task<JsonElement> Get(string path)
            {
                using var response = await client.GetAsync(path);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<JsonElement>();
            }
            async Task<JsonElement> Post(string path, object data)
            {
                using var response = await client.PostAsJsonAsync(path, data);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"POST {path}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return await response.Content.ReadFromJsonAsync<JsonElement>();
            }
            var status = await Get("accounting/status");
            Check(!status.GetProperty("ocrConfigured").GetBoolean(), "unconfigured OCR state");
            var categories = await Get("system/dicts/group/Accounting");
            Check(categories.GetArrayLength() == 7, "new database category seed");
            var materialId = categories.EnumerateArray().Single(dict => dict.GetProperty("value").GetString() == "materials").GetProperty("id").GetGuid();
            var pendingId = categories.EnumerateArray().Single(dict => dict.GetProperty("value").GetString() == "pending").GetProperty("id").GetGuid();
            var temporaryCategory = await Post("system/dicts", new { groupName = "Accounting", label = "Temporary" });
            using (var deleteTemporary = await client.DeleteAsync($"system/dicts/{temporaryCategory.GetProperty("id").GetGuid()}"))
                Check(deleteTemporary.StatusCode == HttpStatusCode.NoContent, "unreferenced Accounting category can be deleted");
            var documents = await Get("system/dicts/group/AccountingDocumentType");
            var documentId = documents[0].GetProperty("id").GetGuid();
            var secondDocumentId = documents[1].GetProperty("id").GetGuid();

            var customGroup = await Post("system/dict-groups", new { name = "Check Group" });
            var customGroupId = customGroup.GetProperty("id").GetGuid();
            var customGroupCode = customGroup.GetProperty("code").GetString()!;
            var dictOne = await Post("system/dicts", new { groupName = customGroupCode, label = "First" });
            var dictTwo = await Post("system/dicts", new { groupName = customGroupCode, label = "Second" });
            var dictOneId = dictOne.GetProperty("id").GetGuid();
            var dictTwoId = dictTwo.GetProperty("id").GetGuid();
            Check(dictOne.GetProperty("value").GetString() == "1" && dictTwo.GetProperty("value").GetString() == "2", "dict values auto-numbered");
            using (var reorder = await client.PutAsJsonAsync("system/dicts/reorder", new { groupName = customGroupCode, ids = new[] { dictTwoId, dictOneId } }))
            {
                Check(reorder.StatusCode == HttpStatusCode.OK, "reorder accepted");
                var reordered = await Get($"system/dicts/group/{customGroupCode}");
                Check(reordered[0].GetProperty("id").GetGuid() == dictTwoId, "reorder persisted");
            }
            using (var invalidReorder = await client.PutAsJsonAsync("system/dicts/reorder", new { groupName = customGroupCode, ids = new[] { dictOneId } }))
                Check(invalidReorder.StatusCode == HttpStatusCode.BadRequest, "incomplete reorder rejected");
            var groups = await Get("system/dict-groups");
            Check(groups.EnumerateArray().Any(group => group.GetProperty("code").GetString() == customGroupCode
                && group.GetProperty("itemCount").GetInt32() == 2), "group item counts");
            using (var duplicateValue = await client.PostAsJsonAsync("system/dicts", new { groupName = customGroupCode, label = "Duplicate", value = "1" }))
                Check(duplicateValue.StatusCode == HttpStatusCode.BadRequest, "duplicate dict value rejected");
            using (var deleteFilledGroup = await client.DeleteAsync($"system/dict-groups/{customGroupId}"))
                Check(deleteFilledGroup.StatusCode == HttpStatusCode.BadRequest, "filled group cannot be deleted");
            using (var lockSystemValue = await client.PutAsJsonAsync($"system/dicts/{pendingId}", new { value = "changed" }))
                Check(lockSystemValue.StatusCode == HttpStatusCode.BadRequest, "system group dict value locked");
            var systemGroup = groups.EnumerateArray().Single(group => group.GetProperty("code").GetString() == "Accounting");
            using (var deleteSystemGroup = await client.DeleteAsync($"system/dict-groups/{systemGroup.GetProperty("id").GetGuid()}"))
                Check(deleteSystemGroup.StatusCode == HttpStatusCode.BadRequest, "system group cannot be deleted");
            var categoryA = (await Post("system/dicts", new { groupName = "AccountingSiteCategory", label = "Export A", value = "a", enabled = true })).GetProperty("id").GetGuid();
            var categoryB = (await Post("system/dicts", new { groupName = "AccountingSiteCategory", label = "Export B", value = "b", enabled = true })).GetProperty("id").GetGuid();
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWZkAAAAASUVORK5CYII=");
            async Task<ReceiptResponse> Create(Guid document)
            {
                using var response = await client.PostAsJsonAsync("accounting/receipts", new { documentTypeDictId = document });
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Create: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return (await response.Content.ReadFromJsonAsync<ReceiptResponse>())!;
            }
            async Task<ReceiptResponse> Attach(Guid receiptId, byte[] bytes)
            {
                using var form = new MultipartFormDataContent();
                var image = new ByteArrayContent(bytes);
                image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(image, "file", "same.png");
                using var response = await client.PostAsync($"accounting/receipts/{receiptId}/image", form);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Attach: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return (await response.Content.ReadFromJsonAsync<ReceiptResponse>())!;
            }
            async Task<ReceiptResponse> Upload(Guid document, byte[] bytes) => await Attach((await Create(document)).Id, bytes);

            // 手動錄入：沒有原圖也可以先建票據
            var manual = await Create(documentId);
            Check(manual.FileItemId == null && manual.FileName == null, "manual receipt has no image");
            Check(manual.Type == pendingId && manual.TypeLabel == "待分類", "manual receipt starts as pending category");
            Check(manual.OcrStatus == "NotStarted", "manual receipt starts without OCR");
            using (var invalidForm = new MultipartFormDataContent())
            {
                invalidForm.Add(new ByteArrayContent([1, 2, 3]), "file", "bad.png");
                using var invalidImage = await client.PostAsync($"accounting/receipts/{manual.Id}/image", invalidForm);
                Check(invalidImage.StatusCode == HttpStatusCode.BadRequest, "invalid image signature rejected");
            }
            using (var oversizedForm = new MultipartFormDataContent())
            {
                oversizedForm.Add(new ByteArrayContent(new byte[4 * 1024 * 1024 + 1]), "file", "large.png");
                using var oversizedImage = await client.PostAsync($"accounting/receipts/{manual.Id}/image", oversizedForm);
                Check(oversizedImage.StatusCode == HttpStatusCode.BadRequest, "oversized image rejected");
            }
            using (var stillNoImage = await client.GetAsync($"accounting/receipts/{manual.Id}"))
            {
                var unchanged = (await stillNoImage.Content.ReadFromJsonAsync<ReceiptResponse>())!;
                Check(unchanged.FileItemId == null, "rejected image leaves receipt unchanged");
            }
            using (var noImageRecognize = await client.PostAsync($"accounting/receipts/{manual.Id}/recognize", null))
            {
                Check(noImageRecognize.StatusCode == HttpStatusCode.BadRequest, "OCR without original rejected");
                Check((await noImageRecognize.Content.ReadAsStringAsync()).Contains("原圖"), "OCR without original explains missing image");
            }
            using (var noImageDownload = await client.GetAsync($"accounting/receipts/{manual.Id}/image"))
                Check(noImageDownload.StatusCode == HttpStatusCode.NotFound, "missing original returns 404");

            var first = await Upload(documentId, png);
            var second = await Upload(secondDocumentId, png);
            Check(first.Md5 == Convert.ToHexStringLower(MD5.HashData(png)) && second.Md5 == first.Md5, "MD5 retained without deduplication");
            Check(first.Amount == null && first.ReceiptDate == null, "new receipt has no date or amount");
            using (var missing = await client.PostAsync($"accounting/receipts/{first.Id}/recognize", null)) Check(missing.StatusCode == HttpStatusCode.BadRequest, "OCR unavailable preserves draft");
            using (var image = await client.GetAsync($"accounting/receipts/{first.Id}/image")) Check((await image.Content.ReadAsByteArrayAsync()).SequenceEqual(png), "original retained");
            var previousOriginal = second.FileItemId;
            second = await Attach(second.Id, png);
            Check(second.FileItemId != previousOriginal, "image replacement rewrites original");
            using (var replacedImage = await client.GetAsync($"accounting/receipts/{second.Id}/image"))
                Check((await replacedImage.Content.ReadAsByteArrayAsync()).SequenceEqual(png), "replacement image served");
            using (var removedOriginal = await client.GetAsync($"storage/files/{previousOriginal}/download"))
                Check(removedOriginal.StatusCode == HttpStatusCode.NotFound, "replaced original removed");

            async Task<ReceiptResponse> Save(ReceiptResponse receipt, Guid category, DateOnly date)
            {
                var request = new UpdateReceiptRequest(receipt.Version, receipt.DocumentTypeDictId,
                    materialId, category, date, 110m, "HKD", "Supplier", "INV-1", null);
                using var response = await client.PutAsJsonAsync($"accounting/receipts/{receipt.Id}", request);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Save: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return (await response.Content.ReadFromJsonAsync<ReceiptResponse>())!;
            }
            var staleVersion = first.Version;
            first = await Save(first, categoryB, new DateOnly(2026, 1, 1));
            second = await Save(second, categoryA, new DateOnly(2026, 12, 31));
            manual = await Save(manual, categoryB, new DateOnly(2026, 6, 1));
            Check(manual.FileItemId == null && manual.Type == materialId && manual.TypeLabel == "材料", "manual receipt saves category without image");
            using (var staleSave = await client.PutAsJsonAsync($"accounting/receipts/{first.Id}",
                new UpdateReceiptRequest(staleVersion, documentId, materialId, categoryB, new DateOnly(2026, 1, 1), 1m, "HKD", null, null, null)))
                Check(staleSave.StatusCode == HttpStatusCode.Conflict, "stale edit rejected");
            var filtered = await Get($"accounting/receipts?start=2026-01-01&end=2026-01-01&type={materialId}");
            Check(filtered.GetProperty("total").GetInt32() == 1, "inclusive date and category filtering");
            filtered = await Get($"accounting/receipts?siteCategoryDictId={categoryB}");
            Check(filtered.GetProperty("total").GetInt32() == 2
                && filtered.GetProperty("items").EnumerateArray().All(item => item.GetProperty("siteCategoryDictId").GetGuid() == categoryB),
                "site category filtering");
            filtered = await Get($"accounting/receipts?documentTypeDictId={secondDocumentId}");
            Check(filtered.GetProperty("total").GetInt32() == 1, "document type filtering");
            filtered = await Get("accounting/receipts?start=2026-01-01&end=2026-12-31&pageSize=1");
            Check(filtered.GetProperty("total").GetInt32() == 3 && filtered.GetProperty("items").GetArrayLength() == 1, "list pagination and image-free receipt listed");
            // 合計按整份篩選結果算，pageSize=1 時仍然是 3 張的合計
            var totals = filtered.GetProperty("totals");
            var allHkd = totals.GetProperty("amounts").EnumerateArray().Single();
            Check(allHkd.GetProperty("currencyCode").GetString() == "HKD" && allHkd.GetProperty("count").GetInt32() == 3
                && allHkd.GetProperty("amount").GetDecimal() == 330m && totals.GetProperty("missingAmount").GetInt32() == 0,
                "totals cover the whole filtered result, not the current page");
            // 換一種幣種後合計分幣種列出，HKD 和 USD 不會加在一起
            using (var usd = await client.PutAsJsonAsync($"accounting/receipts/{manual.Id}",
                new UpdateReceiptRequest(manual.Version, manual.DocumentTypeDictId, materialId, categoryB,
                    new DateOnly(2026, 6, 1), 110m, "USD", "Supplier", "INV-1", null)))
            {
                Check(usd.StatusCode == HttpStatusCode.OK, "receipt currency changed");
                manual = (await usd.Content.ReadFromJsonAsync<ReceiptResponse>())!;
            }
            var currencyTotals = (await Get("accounting/receipts?start=2026-01-01&end=2026-12-31"))
                .GetProperty("totals").GetProperty("amounts").EnumerateArray().ToList();
            Check(currencyTotals.Count == 2
                && currencyTotals.Single(item => item.GetProperty("currencyCode").GetString() == "HKD").GetProperty("amount").GetDecimal() == 220m
                && currencyTotals.Single(item => item.GetProperty("currencyCode").GetString() == "USD").GetProperty("amount").GetDecimal() == 110m,
                "amount totals are kept per currency");
            await using (var zipStream = await client.GetStreamAsync("accounting/receipts/export?start=2026-01-01&end=2026-12-31"))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                Check(archive.Entries.Count == 2, "ZIP exports only receipts with originals, beyond current page");
                foreach (var entry in archive.Entries)
                {
                    await using var content = entry.Open();
                    using var bytes = new MemoryStream();
                    await content.CopyToAsync(bytes);
                    Check(bytes.ToArray().SequenceEqual(png), "ZIP original bytes unchanged");
                }
            }
            using (var deleted = await client.DeleteAsync($"storage/files/{first.FileItemId!.Value}")) Check(deleted.StatusCode == HttpStatusCode.BadRequest, "linked original cannot be deleted");
            using (var deleted = await client.DeleteAsync($"system/dicts/{materialId}")) Check(deleted.StatusCode == HttpStatusCode.Conflict, "referenced category cannot be deleted");
            using (var invalid = await client.GetAsync("accounting/receipts?start=2026-12-31&end=2026-01-01")) Check(invalid.StatusCode == HttpStatusCode.BadRequest, "invalid date range rejected");
            using (var empty = await client.GetAsync("accounting/receipts/export?start=2025-01-01&end=2025-12-31")) Check(empty.StatusCode == HttpStatusCode.NotFound, "empty ZIP explicitly rejected");
            await using (var database = new NpgsqlConnection(isolatedConnection))
            {
                await database.OpenAsync();
                await using var clearMd5 = new NpgsqlCommand("UPDATE storage_files SET \"Md5\" = NULL WHERE \"Id\" = @id", database);
                clearMd5.Parameters.AddWithValue("id", second.FileItemId!.Value);
                await clearMd5.ExecuteNonQueryAsync();
            }
            await using (var noMd5 = await client.GetStreamAsync($"accounting/receipts/export?siteCategoryDictId={categoryA}"))
            using (var archive = new ZipArchive(noMd5, ZipArchiveMode.Read)) Check(archive.Entries.Count == 1, "historical null MD5 still exports");
            first = await client.GetFromJsonAsync<ReceiptResponse>($"accounting/receipts/{first.Id}") ?? throw new InvalidOperationException();
            first = await Save(first, categoryB, new DateOnly(2026, 1, 1));
            Check(first.Amount == 110m && first.CurrencyCode == "HKD", "saved receipt keeps amount and currency");

            // 必填規則：地盤分類仍必填；日期、金額、幣種可以留空
            var doomed = await Upload(documentId, png);
            using (var incomplete = await client.PutAsJsonAsync($"accounting/receipts/{doomed.Id}",
                new UpdateReceiptRequest(doomed.Version, documentId, materialId, null, null, null, null, null, null, null)))
                Check(incomplete.StatusCode == HttpStatusCode.BadRequest, "site category still required");
            using (var sparse = await client.PutAsJsonAsync($"accounting/receipts/{doomed.Id}",
                new UpdateReceiptRequest(doomed.Version, documentId, materialId, categoryA, null, null, null, null, null, null)))
            {
                Check(sparse.StatusCode == HttpStatusCode.OK, "date, amount and currency are optional");
                doomed = (await sparse.Content.ReadFromJsonAsync<ReceiptResponse>())!;
                Check(doomed.ReceiptDate == null && doomed.Amount == null && doomed.CurrencyCode == null, "optional fields stay empty");
            }
            var sparseTotals = (await Get("accounting/receipts")).GetProperty("totals");
            Check(sparseTotals.GetProperty("missingAmount").GetInt32() == 1
                && sparseTotals.GetProperty("amounts").EnumerateArray().Sum(item => item.GetProperty("count").GetInt32()) == 3,
                "receipts without an amount are counted but not summed");
            using (var confirmGone = await client.PostAsJsonAsync($"accounting/receipts/{doomed.Id}/confirm", new { doomed.Version }))
                Check(confirmGone.StatusCode == HttpStatusCode.NotFound, "confirm endpoint removed");
            doomed = await Save(doomed, categoryA, new DateOnly(2026, 3, 1));
            var doomedFileId = doomed.FileItemId!.Value;
            using (var deleted = await client.DeleteAsync($"accounting/receipts/{doomed.Id}"))
                Check(deleted.StatusCode == HttpStatusCode.NoContent, "receipt deleted");
            using (var gone = await client.GetAsync($"accounting/receipts/{doomed.Id}"))
                Check(gone.StatusCode == HttpStatusCode.NotFound, "deleted receipt hidden");
            using (var goneImage = await client.GetAsync($"accounting/receipts/{doomed.Id}/image"))
                Check(goneImage.StatusCode == HttpStatusCode.NotFound, "deleted receipt image gone");
            using (var goneFile = await client.GetAsync($"storage/files/{doomedFileId}/download"))
                Check(goneFile.StatusCode == HttpStatusCode.NotFound, "deleted receipt original cleaned up");
            using (var again = await client.DeleteAsync($"accounting/receipts/{doomed.Id}"))
                Check(again.StatusCode == HttpStatusCode.NotFound, "deleting missing receipt rejected");

            var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtTokenGenerator(Options.Create(jwt)).Generate(Guid.NewGuid(), "check-worker", ["Worker"], []));
            using (var denied = await client.GetAsync("accounting/receipts")) Check(denied.StatusCode == HttpStatusCode.Forbidden, "worker Accounting rejected");
            using (var denied = await client.GetAsync($"storage/files/{first.FileItemId!.Value}/download")) Check(denied.StatusCode == HttpStatusCode.Forbidden, "worker Storage bypass rejected");
            filtered = await Get("storage/files");
            Check(filtered.GetArrayLength() == 0, "worker list hides Accounting images");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using (var disablePending = await client.PutAsJsonAsync($"system/dicts/{pendingId}", new { enabled = false }))
                Check(disablePending.StatusCode == HttpStatusCode.BadRequest, "pending category cannot be disabled");
            using (var deletePending = await client.DeleteAsync($"system/dicts/{pendingId}"))
                Check(deletePending.StatusCode == HttpStatusCode.Conflict, "pending category cannot be deleted");
            using (var disableDocument = await client.PutAsJsonAsync($"system/dicts/{documentId}", new { enabled = false }))
                disableDocument.EnsureSuccessStatusCode();
            using (var disableLastDocument = await client.PutAsJsonAsync($"system/dicts/{secondDocumentId}", new { enabled = false }))
                Check(disableLastDocument.StatusCode == HttpStatusCode.BadRequest, "last document type cannot be disabled");
            using (var enableDocument = await client.PutAsJsonAsync($"system/dicts/{documentId}", new { enabled = true }))
                enableDocument.EnsureSuccessStatusCode();
            using (var disabled = await client.PutAsJsonAsync($"system/dicts/{materialId}", new { enabled = false })) disabled.EnsureSuccessStatusCode();
            filtered = await Get("accounting/receipts");
            Check(filtered.GetProperty("total").GetInt32() == 3, "disabled category history retained");
            using (var deleteDictOne = await client.DeleteAsync($"system/dicts/{dictOneId}")) deleteDictOne.EnsureSuccessStatusCode();
            using (var deleteDictTwo = await client.DeleteAsync($"system/dicts/{dictTwoId}")) deleteDictTwo.EnsureSuccessStatusCode();
            using (var deleteEmptyGroup = await client.DeleteAsync($"system/dict-groups/{customGroupId}"))
                Check(deleteEmptyGroup.StatusCode == HttpStatusCode.NoContent, "empty group can be deleted");
            File.Delete(Directory.GetFiles(temporaryRoot).First());
            using (var missingImage = await client.GetAsync("accounting/receipts/export"))
                Check(missingImage.StatusCode == HttpStatusCode.InternalServerError, "missing original fails entire archive");
            Console.WriteLine($"PASS: {count} isolated PostgreSQL/API/ZIP checks");
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            await standardOutput;
            await standardError;
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", adminConnection);
            await drop.ExecuteNonQueryAsync();
            Directory.Delete(temporaryRoot, true);
        }
    }
}
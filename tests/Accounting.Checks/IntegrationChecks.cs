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
        start.Environment["Ocr__Key"] = "";
        start.Environment["Ocr__Endpoint"] = "";
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
            var documents = await Get("system/dicts/group/AccountingDocumentType");
            var documentId = documents[0].GetProperty("id").GetGuid();
            var secondDocumentId = documents[1].GetProperty("id").GetGuid();
            var siteA = (await Post("attendance/sites", new { name = "Check A", address = "A" })).GetProperty("id").GetGuid();
            var siteB = (await Post("attendance/sites", new { name = "Check B", address = "B" })).GetProperty("id").GetGuid();
            var categoryA = (await Post("system/dicts", new { groupName = "AccountingSiteCategory", label = "Export A", value = "a", enabled = true })).GetProperty("id").GetGuid();
            var categoryB = (await Post("system/dicts", new { groupName = "AccountingSiteCategory", label = "Export B", value = "b", enabled = true })).GetProperty("id").GetGuid();
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWZkAAAAASUVORK5CYII=");
            async Task<ReceiptResponse> Upload(Guid site, Guid document, byte[] bytes)
            {
                using var form = new MultipartFormDataContent();
                var image = new ByteArrayContent(bytes);
                image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(image, "file", "same.png");
                using var response = await client.PostAsync($"accounting/receipts?siteId={site}&documentTypeDictId={document}", form);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Upload: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return (await response.Content.ReadFromJsonAsync<ReceiptResponse>())!;
            }
            var first = await Upload(siteA, documentId, png);
            var second = await Upload(siteB, secondDocumentId, png);
            using (var invalidForm = new MultipartFormDataContent())
            {
                invalidForm.Add(new ByteArrayContent([1, 2, 3]), "file", "bad.png");
                using var invalidImage = await client.PostAsync($"accounting/receipts?siteId={siteA}&documentTypeDictId={documentId}", invalidForm);
                Check(invalidImage.StatusCode == HttpStatusCode.BadRequest, "invalid image signature rejected");
            }
            using (var oversizedForm = new MultipartFormDataContent())
            {
                oversizedForm.Add(new ByteArrayContent(new byte[4 * 1024 * 1024 + 1]), "file", "large.png");
                using var oversizedImage = await client.PostAsync($"accounting/receipts?siteId={siteA}&documentTypeDictId={documentId}", oversizedForm);
                Check(oversizedImage.StatusCode == HttpStatusCode.BadRequest, "oversized image rejected");
            }
            Check(first.Md5 == Convert.ToHexStringLower(MD5.HashData(png)) && second.Md5 == first.Md5, "MD5 retained without deduplication");
            Check(first.Amount == null && first.ReceiptDate == null && !first.IsConfirmed, "draft null fields");
            using (var missing = await client.PostAsync($"accounting/receipts/{first.Id}/recognize", null)) Check(missing.StatusCode == HttpStatusCode.BadRequest, "OCR unavailable preserves draft");
            using (var image = await client.GetAsync($"accounting/receipts/{first.Id}/image")) Check((await image.Content.ReadAsByteArrayAsync()).SequenceEqual(png), "original retained");
            using (var invalidConfirm = await client.PostAsJsonAsync($"accounting/receipts/{first.Id}/confirm", new { first.Version })) Check(invalidConfirm.StatusCode == HttpStatusCode.BadRequest, "incomplete confirmation rejected");

            async Task<ReceiptResponse> Save(ReceiptResponse receipt, Guid category, DateOnly date)
            {
                var request = new UpdateReceiptRequest(receipt.Version, receipt.SiteId, receipt.DocumentTypeDictId,
                    materialId, category, date, 110m, "HKD", "Supplier", "INV-1", null);
                using var response = await client.PutAsJsonAsync($"accounting/receipts/{receipt.Id}", request);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Save: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return (await response.Content.ReadFromJsonAsync<ReceiptResponse>())!;
            }
            var staleVersion = first.Version;
            first = await Save(first, categoryB, new DateOnly(2026, 1, 1));
            second = await Save(second, categoryA, new DateOnly(2026, 12, 31));
            using (var staleSave = await client.PutAsJsonAsync($"accounting/receipts/{first.Id}",
                new UpdateReceiptRequest(staleVersion, siteA, documentId, materialId, categoryB, new DateOnly(2026, 1, 1), 1m, "HKD", null, null, null)))
                Check(staleSave.StatusCode == HttpStatusCode.Conflict, "stale edit rejected");
            using (var stale = await client.PostAsJsonAsync($"accounting/receipts/{first.Id}/confirm", new { version = staleVersion })) Check(stale.StatusCode == HttpStatusCode.Conflict, "stale version rejected");
            var confirmed = await Post($"accounting/receipts/{first.Id}/confirm", new { first.Version });
            Check(confirmed.GetProperty("isConfirmed").GetBoolean(), "manual confirm");
            var filtered = await Get($"accounting/receipts?start=2026-01-01&end=2026-01-01&type={materialId}&siteId={siteA}");
            Check(filtered.GetProperty("total").GetInt32() == 1, "inclusive date and actual site filtering");
            filtered = await Get($"accounting/receipts?siteCategoryDictId={categoryB}");
            Check(filtered.GetProperty("total").GetInt32() == 1 && filtered.GetProperty("items")[0].GetProperty("siteId").GetGuid() == siteA, "export category independent of actual site");
            filtered = await Get($"accounting/receipts?documentTypeDictId={secondDocumentId}");
            Check(filtered.GetProperty("total").GetInt32() == 1, "document type filtering");
            filtered = await Get("accounting/receipts?start=2026-01-01&end=2026-12-31&pageSize=1");
            Check(filtered.GetProperty("total").GetInt32() == 2 && filtered.GetProperty("items").GetArrayLength() == 1, "list pagination");
            await using (var zipStream = await client.GetStreamAsync("accounting/receipts/export?start=2026-01-01&end=2026-12-31"))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                Check(archive.Entries.Count == 2, "ZIP exports beyond current page and same MD5 records");
                foreach (var entry in archive.Entries)
                {
                    await using var content = entry.Open();
                    using var bytes = new MemoryStream();
                    await content.CopyToAsync(bytes);
                    Check(bytes.ToArray().SequenceEqual(png), "ZIP original bytes unchanged");
                }
            }
            using (var deleted = await client.DeleteAsync($"storage/files/{first.FileItemId}")) Check(deleted.StatusCode == HttpStatusCode.BadRequest, "linked original cannot be deleted");
            using (var deleted = await client.DeleteAsync($"system/dicts/{materialId}")) Check(deleted.StatusCode == HttpStatusCode.Conflict, "referenced category cannot be deleted");
            using (var invalid = await client.GetAsync("accounting/receipts?start=2026-12-31&end=2026-01-01")) Check(invalid.StatusCode == HttpStatusCode.BadRequest, "invalid date range rejected");
            using (var empty = await client.GetAsync("accounting/receipts/export?start=2025-01-01&end=2025-12-31")) Check(empty.StatusCode == HttpStatusCode.NotFound, "empty ZIP explicitly rejected");
            await using (var database = new NpgsqlConnection(isolatedConnection))
            {
                await database.OpenAsync();
                await using var clearMd5 = new NpgsqlCommand("UPDATE storage_files SET \"Md5\" = NULL WHERE \"Id\" = @id", database);
                clearMd5.Parameters.AddWithValue("id", second.FileItemId);
                await clearMd5.ExecuteNonQueryAsync();
            }
            await using (var noMd5 = await client.GetStreamAsync($"accounting/receipts/export?siteId={siteB}"))
            using (var archive = new ZipArchive(noMd5, ZipArchiveMode.Read)) Check(archive.Entries.Count == 1, "historical null MD5 still exports");
            first = await client.GetFromJsonAsync<ReceiptResponse>($"accounting/receipts/{first.Id}") ?? throw new InvalidOperationException();
            first = await Save(first, categoryB, new DateOnly(2026, 1, 1));
            Check(!first.IsConfirmed && first.ManuallyEdited, "editing confirmed receipt requires reconfirmation");
            var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtTokenGenerator(Options.Create(jwt)).Generate(Guid.NewGuid(), "check-worker", ["Worker"], []));
            using (var denied = await client.GetAsync("accounting/receipts")) Check(denied.StatusCode == HttpStatusCode.Forbidden, "worker Accounting rejected");
            using (var denied = await client.GetAsync($"storage/files/{first.FileItemId}/download")) Check(denied.StatusCode == HttpStatusCode.Forbidden, "worker Storage bypass rejected");
            filtered = await Get("storage/files");
            Check(filtered.GetArrayLength() == 0, "worker list hides Accounting images");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using (var disabled = await client.PutAsJsonAsync($"system/dicts/{materialId}", new { enabled = false })) disabled.EnsureSuccessStatusCode();
            filtered = await Get("accounting/receipts");
            Check(filtered.GetProperty("total").GetInt32() == 2, "disabled category history retained");
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
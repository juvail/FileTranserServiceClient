using FileTransferServiceClient.Model;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        BaseAddress = new Uri("https://localhost:7217/")
    };
    private static ConcurrentBag<string> uploadedFiles = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== File Upload/Download Load Tester ===");
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var settings = config.GetSection("LoadTestSettings").Get<LoadTestSettings>();

        string folderPath = settings.FolderPath;
        int uploadCount = settings.UploadCount;
        int downloadCount = settings.DownloadCount;

        var stopwatch = Stopwatch.StartNew();

        await RunUploadsAsync(uploadCount, folderPath, settings);
        await RunDownloadsAsync(downloadCount);

        stopwatch.Stop();

        Console.WriteLine($"\nAll requests completed in {stopwatch.ElapsedMilliseconds} ms");
    }

    // ---------------- UPLOADS ----------------
    private static async Task RunUploadsAsync(int uploadCount, string folderPath, LoadTestSettings settings)
    {
        var files = Directory.GetFiles(folderPath);
        int chunkSize = settings.ChunkSizeMB * 1024 * 1024;

        // here we will get more control it will only upload 10 at a time,
        // So for testing purpose first we can set its value to 10 then try 50 try 100 so that we can check the saturation point and all.
        var semaphore = new SemaphoreSlim(settings.MaxConcurrentUploads); 

        if (files.Length == 0)
        {
            Console.WriteLine("No files found in folder.");
            return;
        }
        var tasks = new List<Task>();

        for (int i = 0; i < uploadCount; i++)
        {
            string fileToUpload = files[i % files.Length];
            
            await semaphore.WaitAsync();

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await UploadFileAsync(fileToUpload, chunkSize);
                }
                finally
                {
                    semaphore.Release();
                }

            }));
        }

        await Task.WhenAll(tasks);
    }


    private static async Task UploadFileAsync(string filePath, int chunkSize)
    {
        string fileId = Guid.NewGuid().ToString();
        string fileName = Path.GetFileName(filePath);
        long fileSize = new FileInfo(filePath).Length;
        int totalChunks = (int)Math.Ceiling((double)fileSize / chunkSize);

        try
        {
            byte[] buffer = new byte[chunkSize];
            int chunkIndex = 0;

            await using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);

            int bytesRead;

            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                using var content = new MultipartFormDataContent();

                var chunkContent = new ByteArrayContent(buffer, 0, bytesRead);
                chunkContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                content.Add(chunkContent, "chunk", fileName);
                content.Add(new StringContent(fileId), "fileId");
                content.Add(new StringContent(chunkIndex.ToString()), "chunkIndex");
                content.Add(new StringContent(totalChunks.ToString()), "totalChunks");

                var response = await SendWithRetry(() => _httpClient.PostAsync("api/files/upload-chunk", content));
                response.EnsureSuccessStatusCode();

                chunkIndex++;
            }

            // Notify server upload completed
            using var completeContent = new MultipartFormDataContent();
            completeContent.Add(new StringContent(fileId), "fileId");
            completeContent.Add(new StringContent(fileName), "fileName");

            var completeResponse = await _httpClient.PostAsync("api/files/upload-complete", completeContent);
            completeResponse.EnsureSuccessStatusCode();

            Console.WriteLine($"Uploaded file: {fileName}");
            uploadedFiles.Add($"{fileId}_{fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Upload failed for {fileName}: {ex.Message}");
        }
    }

    // ---------------- DOWNLOADS ----------------
    private static async Task RunDownloadsAsync(int count)
    {
        var tasks = new List<Task>();

        for (int i = 0; i < count; i++)
        {
            int index = i;
            tasks.Add(DownloadFileAsync(index));
        }

        await Task.WhenAll(tasks);
    }

    private static async Task DownloadFileAsync(int requestId)
    {
        try
        {
            if (uploadedFiles.IsEmpty)
            {
                Console.WriteLine("No uploaded files available for download");
                return;
            }

            var files = uploadedFiles.ToArray(); // snapshot of bag
            string fileName = files[requestId % files.Length]; // rotate

            var response = await _httpClient.GetAsync($"api/files/download/{fileName}");

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadAsByteArrayAsync();
                Console.WriteLine($"Download {requestId} completed ({fileName}) ({data.Length} bytes)");
            }
            else
            {
                Console.WriteLine($"Download {requestId} failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Download {requestId} failed: {ex.Message}");
        }
    }

    private static async Task<HttpResponseMessage> SendWithRetry(Func<Task<HttpResponseMessage>> action)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var response = await action();
                if (response.IsSuccessStatusCode)
                    return response;
            }
            catch { }

            await Task.Delay(200);
        }

        throw new Exception("Request failed after retries");
    }
}
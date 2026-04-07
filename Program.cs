using FileTransferServiceClient.Model;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

class Program
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        BaseAddress = new Uri("https://localhost:5001/")
    };

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

        var uploadTask = RunUploadsAsync(uploadCount, folderPath);
        var downloadTask = RunDownloadsAsync(downloadCount);

        await Task.WhenAll(uploadTask, downloadTask);

        stopwatch.Stop();

        Console.WriteLine($"\nAll requests completed in {stopwatch.ElapsedMilliseconds} ms");
    }

    // ---------------- UPLOADS ----------------
    private static async Task RunUploadsAsync(int uploadCount, string folderPath)
    {
        var files = Directory.GetFiles(folderPath);

        if (files.Length == 0)
        {
            Console.WriteLine("No files found in folder.");
            return;
        }

        var tasks = new List<Task>();

        for (int i = 0; i < uploadCount; i++)
        {
            string fileToUpload = files[i % files.Length];
            tasks.Add(UploadFileAsync(fileToUpload));
        }

        await Task.WhenAll(tasks);
    }

    private static async Task UploadFileAsync(string filePath, int chunkSize = 1024 * 1024)
    {
        string fileId = Guid.NewGuid().ToString();
        string fileName = Path.GetFileName(filePath);

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

                var response = await _httpClient.PostAsync("upload-chunk", content);
                response.EnsureSuccessStatusCode();

                chunkIndex++;
            }

            // Notify server upload completed
            using var completeContent = new MultipartFormDataContent();
            completeContent.Add(new StringContent(fileId), "fileId");
            completeContent.Add(new StringContent(fileName), "fileName");
            completeContent.Add(new StringContent(chunkIndex.ToString()), "totalChunks");

            var completeResponse = await _httpClient.PostAsync("upload-complete", completeContent);
            completeResponse.EnsureSuccessStatusCode();

            Console.WriteLine($"Uploaded file: {fileName}");
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
            tasks.Add(Task.Run(() => DownloadFileAsync(index)));
        }

        await Task.WhenAll(tasks);
    }

    private static async Task DownloadFileAsync(int requestId)
    {
        try
        {
            string fileName = $"testfile_{requestId}.txt";

            var response = await _httpClient.GetAsync($"download/{fileName}");

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadAsByteArrayAsync();
                Console.WriteLine($"Download {requestId} completed ({data.Length} bytes)");
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
}
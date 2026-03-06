using System.Diagnostics;
using System.Net.Http.Headers;

class Program
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        BaseAddress = new Uri("https://localhost:5001/")
    };

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== File Upload/Download Load Tester ===");

        Console.Write("Enter number of upload requests: ");
        int uploadCount = int.Parse(Console.ReadLine()!);

        Console.Write("Enter number of download requests: ");
        int downloadCount = int.Parse(Console.ReadLine()!);

        var stopwatch = Stopwatch.StartNew();

        var uploadTasks = RunUploadsAsync(uploadCount);
        var downloadTasks = RunDownloadsAsync(downloadCount);

        await Task.WhenAll(uploadTasks, downloadTasks);

        stopwatch.Stop();

        Console.WriteLine($"\nAll requests completed in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    // ---------------- UPLOADS ----------------
    private static async Task RunUploadsAsync(int count)
    {
        var tasks = new List<Task>();

        for (int i = 0; i < count; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() => UploadFileAsync(index)));
        }

        await Task.WhenAll(tasks);
    }

    private static async Task UploadFileAsync(int requestId)
    {
        string fileId = Guid.NewGuid().ToString();
        string fileName = $"testfile_{requestId}.txt";
        int totalChunks = 1;

        try
        {
            var content = new MultipartFormDataContent();

            var fileBytes = System.Text.Encoding.UTF8.GetBytes("Sample file content for testing");
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

            content.Add(fileContent, "chunk", fileName);
            content.Add(new StringContent(fileId), "fileId");
            content.Add(new StringContent("0"), "chunkIndex");

            await _httpClient.PostAsync("upload-chunk", content);

            var completeContent = new MultipartFormDataContent();
            completeContent.Add(new StringContent(fileId), "fileId");
            completeContent.Add(new StringContent(fileName), "fileName");
            completeContent.Add(new StringContent(totalChunks.ToString()), "totalChunks");

            await _httpClient.PostAsync("upload-complete", completeContent);

            Console.WriteLine($"Upload {requestId} completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Upload {requestId} failed: {ex.Message}");
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
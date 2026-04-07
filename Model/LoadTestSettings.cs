namespace FileTransferServiceClient.Model
{
    internal class LoadTestSettings
    {
        public string FolderPath { get; set; } = string.Empty;

        public int UploadCount { get; set; }

        public int DownloadCount { get; set; }

        public int MaxConcurrentUploads { get; set; }

        public int ChunkSizeMB { get; set; }
    }
}

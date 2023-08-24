namespace Files.Api.Models
{
	public class UploadFileModel
	{
		public string FileData { get; set; } = null!;

		public string ContentType { get; set; } = null!;

		public string Target { get; set; } = null!;

		public int FileSize { get; set; }

		public int ChunkNumber { get; set; }

		public int ChunkSize { get; set; }

		public int DefaultChunkSize { get; set; }

		public int NumberOfChunks { get; set; }
	}
}

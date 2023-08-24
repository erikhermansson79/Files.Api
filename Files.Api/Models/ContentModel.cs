namespace Files.Api.Models
{
	public class ContentModel
	{
		public object? Data { get; set; }

		public string? ContentType { get; set; }

		public string? FileName { get; internal set; }

		public string? Type { get; set; }
	}
}

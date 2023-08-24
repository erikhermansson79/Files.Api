using System.Text.Json.Serialization;

namespace Files.Api.Models
{
	public class DownloadModel
	{
		[JsonPropertyName("paths")]
		public required string[] Paths { get; set; }
	}
}

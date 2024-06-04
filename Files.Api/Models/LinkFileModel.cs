using System.Text.Json.Serialization;

namespace Files.Api.Models
{
    public enum LinkType
    {
        URL
    }

    public class LinkFileModel
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public LinkType? LinkType { get; set; }

        public string LinkTarget { get; set; } = null!;

        public string DisplayName { get; set; } = null!;

        public string? IconData { get; set; }
    }
}

namespace Files.Api.Models
{
	public record struct PathAndStreamFactory(string Path, Func<Stream> StreamFactory);
}

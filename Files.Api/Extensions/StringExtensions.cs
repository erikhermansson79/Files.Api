using System.Diagnostics.CodeAnalysis;

namespace Files.Api.Extensions
{
	internal static class StringExtensions
	{
		[return: NotNullIfNotNull(nameof(value))]
		public static string? ReplaceReservedCharacters(this string? value)
		{
			if (value == null)
			{
				return null!;
			}

			return value.Replace('+', '_').Replace("&", "_").Replace('%', '_');
		}
	}
}

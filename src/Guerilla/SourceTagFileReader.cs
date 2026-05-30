using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Guerilla
{
	public sealed class SourceTagFileReader
	{
		private readonly string _tagsRoot;
		private readonly TagGroupRegistry _groups;

		public SourceTagFileReader(string tagsRoot, TagGroupRegistry groups)
		{
			_tagsRoot = Path.GetFullPath(tagsRoot);
			_groups = groups;
		}

		public SourceTagFile Read(string filePath, string groupMagic, byte[] bytes)
		{
			var result = new SourceTagFile(DetectDataOffset(bytes, groupMagic));
			ReadHeaderDependencies(bytes, result);
			ReadPathDependencies(bytes, result);
			return result;
		}

		private void ReadHeaderDependencies(byte[] bytes, SourceTagFile result)
		{
			if (bytes.Length < 0x24)
				return;

			var dependencyCount = ReadUInt16(bytes, 0x8);
			var dataFixupCount = ReadUInt16(bytes, 0xA);
			var resourceFixupCount = ReadUInt16(bytes, 0xC);
			var baseDataOffset = ReadUInt32(bytes, 0x10);
			var magic = ReadMagic(bytes, 0x14);

			if (dependencyCount == 0 || dependencyCount > 4096 || baseDataOffset >= bytes.Length || !_groups.HasGroup(magic))
				return;

			result.Messages.Add("Possible source tag header: dependencies=" + dependencyCount +
				", dataFixups=" + dataFixupCount + ", resourceFixups=" + resourceFixupCount +
				", baseDataOffset=0x" + baseDataOffset.ToString("X") + ".");

			// Different toolchains store the dependency records differently. The path scanner below
			// is the authority for now; this header probe is kept as a useful breadcrumb.
		}

		private void ReadPathDependencies(byte[] bytes, SourceTagFile result)
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var candidate in EnumerateAsciiCandidates(bytes))
			{
				var normalized = candidate.Replace('/', '\\').Trim('\\');
				var resolved = ResolveCandidate(normalized);
				if (resolved == null)
					continue;

				var groupMagic = _groups.GetGroupMagicForFile(resolved);
				var extension = _groups.GetExtension(groupMagic);
				var pathWithoutExtension = normalized;
				if (!string.IsNullOrEmpty(extension) &&
					pathWithoutExtension.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase))
					pathWithoutExtension = pathWithoutExtension.Substring(0, pathWithoutExtension.Length - extension.Length - 1);

				var key = groupMagic + ":" + pathWithoutExtension;
				if (seen.Add(key))
				{
					var dependency = new RawTagDependency(groupMagic, pathWithoutExtension, "path-scan");
					dependency.ResolvedFile = resolved;
					result.Dependencies.Add(dependency);
				}
			}
		}

		private string ResolveCandidate(string candidate)
		{
			if (candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
				candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 && !ContainsOnlyPathSeparators(candidate))
				return null;

			foreach (var groupMagic in _groups.GroupMagics)
			{
				var extension = _groups.GetExtension(groupMagic);
				if (string.IsNullOrEmpty(extension))
					continue;

				var relative = candidate.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)
					? candidate
					: candidate + "." + extension;
				try
				{
					var fullPath = Path.GetFullPath(Path.Combine(_tagsRoot, relative));
					if (fullPath.StartsWith(_tagsRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
						return fullPath;
				}
				catch (ArgumentException)
				{
					return null;
				}
			}

			return null;
		}

		private static bool ContainsOnlyPathSeparators(string candidate)
		{
			foreach (var c in Path.GetInvalidFileNameChars())
			{
				if ((c == '\\' || c == '/') && candidate.IndexOf(c) >= 0)
					continue;
				if (candidate.IndexOf(c) >= 0)
					return false;
			}
			return true;
		}

		private static IEnumerable<string> EnumerateAsciiCandidates(byte[] bytes)
		{
			var start = -1;
			for (var i = 0; i <= bytes.Length; i++)
			{
				var isCandidateChar = i < bytes.Length && IsPathChar((char)bytes[i]);
				if (isCandidateChar)
				{
					if (start < 0)
						start = i;
					continue;
				}

				if (start >= 0)
				{
					var length = i - start;
					if (length >= 4 && length <= 260)
					{
						var text = Encoding.ASCII.GetString(bytes, start, length);
						if (LooksLikeTagPath(text))
							yield return text;
					}
					start = -1;
				}
			}
		}

		private static bool LooksLikeTagPath(string text)
		{
			return text.IndexOf('\\') >= 0 || text.IndexOf('/') >= 0;
		}

		private static bool IsPathChar(char c)
		{
			return char.IsLetterOrDigit(c) || c == '\\' || c == '/' || c == '_' || c == '-' || c == ' ' || c == '.';
		}

		private static ushort ReadUInt16(byte[] bytes, int offset)
		{
			return offset + 2 <= bytes.Length ? BitConverter.ToUInt16(bytes, offset) : (ushort)0;
		}

		private static uint ReadUInt32(byte[] bytes, int offset)
		{
			return offset + 4 <= bytes.Length ? BitConverter.ToUInt32(bytes, offset) : 0;
		}

		private static string ReadMagic(byte[] bytes, int offset)
		{
			var raw = ReadUInt32(bytes, offset);
			var chars = new[]
			{
				(char)((raw >> 24) & 0xFF),
				(char)((raw >> 16) & 0xFF),
				(char)((raw >> 8) & 0xFF),
				(char)(raw & 0xFF)
			};
			return new string(chars).TrimEnd('\0');
		}

		private static long DetectDataOffset(byte[] bytes, string groupMagic)
		{
			if (bytes.Length < 0x40)
				return 0;

			var headerText = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 0x40));
			return headerText.IndexOf("blam", StringComparison.OrdinalIgnoreCase) >= 0 ||
				headerText.IndexOf(groupMagic.Trim(), StringComparison.OrdinalIgnoreCase) >= 0
					? 0x40
					: 0;
		}
	}
}

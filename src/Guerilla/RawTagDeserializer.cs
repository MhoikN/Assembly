using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Guerilla
{
	public sealed class RawTagDeserializer
	{
		private const int MaxBlockEntries = 4096;
		private readonly string _tagsRoot;
		private readonly TagGroupRegistry _groups;
		private readonly PluginDefinitionLoader _plugins;
		private readonly RawTagProfile _profile;
		private readonly SourceTagFileReader _sourceTagReader;
		private readonly Dictionary<string, RawTag> _loaded =
			new Dictionary<string, RawTag>(StringComparer.OrdinalIgnoreCase);

		public RawTagDeserializer(string tagsRoot, TagGroupRegistry groups, PluginDefinitionLoader plugins)
			: this(tagsRoot, groups, plugins, RawTagProfile.Halo1())
		{
		}

		public RawTagDeserializer(string tagsRoot, TagGroupRegistry groups, PluginDefinitionLoader plugins, RawTagProfile profile)
		{
			_tagsRoot = Path.GetFullPath(tagsRoot);
			_groups = groups;
			_plugins = plugins;
			_profile = profile;
			_sourceTagReader = new SourceTagFileReader(tagsRoot, groups);
		}

		public IEnumerable<string> FindScenarioFiles()
		{
			return Directory.GetFiles(_tagsRoot, "*.scenario", SearchOption.AllDirectories);
		}

		public RawTag LoadRecursive(string filePath, int maxDepth)
		{
			return LoadRecursive(Path.GetFullPath(filePath), maxDepth, 0);
		}

		private RawTag LoadRecursive(string filePath, int maxDepth, int depth)
		{
			RawTag existing;
			if (_loaded.TryGetValue(filePath, out existing))
				return existing;

			var tag = LoadSingle(filePath);
			_loaded[filePath] = tag;
			if (depth >= maxDepth)
				return tag;

			foreach (var dependency in tag.Dependencies)
			{
				if (dependency.ResolvedFile == null || _loaded.ContainsKey(dependency.ResolvedFile))
					continue;

				tag.Children.Add(LoadRecursive(dependency.ResolvedFile, maxDepth, depth + 1));
			}

			foreach (var reference in tag.References)
			{
				var resolved = ResolveReference(reference);
				reference.ResolvedFile = resolved;
				if (resolved == null || _loaded.ContainsKey(resolved))
					continue;

				tag.Children.Add(LoadRecursive(resolved, maxDepth, depth + 1));
			}

			return tag;
		}

		public RawTag LoadSingle(string filePath)
		{
			var fullPath = Path.GetFullPath(filePath);
			var groupMagic = _groups.GetGroupMagicForFile(fullPath);
			var relative = MakeRelativePath(fullPath);
			var tag = new RawTag(fullPath, relative, groupMagic, _groups.GetGroupName(groupMagic));

			if (groupMagic == null)
			{
				tag.Messages.Add("Unknown tag extension; no group mapping is available.");
				return tag;
			}

			var plugin = _plugins.LoadByGroup(groupMagic);
			if (plugin == null)
			{
				tag.Messages.Add("No Assembly plugin XML found for group " + groupMagic + ".");
				return tag;
			}

			var bytes = File.ReadAllBytes(fullPath);
			var sourceTag = _sourceTagReader.Read(fullPath, groupMagic, bytes);
			tag.DataOffset = sourceTag.DataOffset;
			foreach (var dependency in sourceTag.Dependencies)
				tag.Dependencies.Add(dependency);
			foreach (var message in sourceTag.Messages)
				tag.Messages.Add(message);

			ReadFields(bytes, tag.DataOffset, tag.DataOffset, plugin.Fields, tag.Fields, tag.References);
			ResolveDatumReferences(tag);
			return tag;
		}

		private void ResolveDatumReferences(RawTag tag)
		{
			foreach (var reference in tag.References)
			{
				if (!reference.HasDatumIndex)
					continue;

				var dependencyIndex = (int)(reference.DatumIndex & 0xFFFF);
				if (dependencyIndex < 0 || dependencyIndex >= tag.Dependencies.Count)
					continue;

				var dependency = tag.Dependencies[dependencyIndex];
				if (string.Equals(reference.GroupMagic, dependency.GroupMagic, StringComparison.OrdinalIgnoreCase))
					reference.ResolveFromDependency(dependency);
			}
		}

		private void ReadFields(byte[] bytes, long baseOffset, long dataOffset, IEnumerable<PluginFieldDefinition> definitions,
			IList<RawTagField> fields, IList<RawTagReference> references)
		{
			foreach (var definition in definitions)
			{
				if (!definition.Visible && definition.Kind != "tagref" && definition.Kind != "tagblock")
					continue;

				var absoluteOffset = baseOffset + definition.Offset;
				var field = new RawTagField(definition.Kind, definition.Name, absoluteOffset);
				field.Size = definition.Size;
				fields.Add(field);

				try
				{
					ReadField(bytes, absoluteOffset, dataOffset, definition, field, references);
				}
				catch (Exception ex)
				{
					field.Value = "read error: " + ex.Message;
				}
			}
		}

		private void ReadField(byte[] bytes, long offset, long dataOffset, PluginFieldDefinition definition, RawTagField field,
			IList<RawTagReference> references)
		{
			switch (definition.Kind)
			{
				case "tagref":
					ReadTagReference(bytes, offset, dataOffset, definition, field, references);
					break;
				case "tagblock":
					ReadTagBlock(bytes, offset, dataOffset, definition, field, references);
					break;
				case "ascii":
					field.Value = ReadFixedAscii(bytes, offset, definition.Size);
					break;
				case "utf16":
					field.Value = ReadFixedUtf16(bytes, offset, definition.Size);
					break;
				case "uint8":
				case "byte":
					field.Value = ReadByte(bytes, offset);
					break;
				case "int8":
					field.Value = unchecked((sbyte)ReadByte(bytes, offset));
					break;
				case "uint16":
				case "flags16":
				case "enum16":
					field.Value = ReadUInt16(bytes, offset);
					break;
				case "int16":
					field.Value = unchecked((short)ReadUInt16(bytes, offset));
					break;
				case "uint32":
				case "flags32":
				case "enum32":
				case "datum":
				case "oldstringid":
					field.Value = ReadUInt32(bytes, offset);
					break;
				case "int32":
				case "undefined":
					field.Value = unchecked((int)ReadUInt32(bytes, offset));
					break;
				case "float32":
				case "float":
				case "degree":
					field.Value = ReadFloat(bytes, offset);
					break;
				default:
					field.Value = DescribeScalar(bytes, offset, definition.Kind);
					break;
			}
		}

		private void ReadTagReference(byte[] bytes, long offset, long dataOffset, PluginFieldDefinition definition, RawTagField field,
			IList<RawTagReference> references)
		{
			if (!definition.WithGroup)
			{
				field.Value = IsReadable(bytes, offset, 4) ? "datum=0x" + ReadUInt32(bytes, offset).ToString("X8") : "<outside file>";
				return;
			}

			if (!IsReadable(bytes, offset, _profile.TagReferenceSize))
			{
				field.Value = "<outside file>";
				return;
			}

			var magic = NormalizeMagic(ReadMagic(bytes, offset));
			if (!_groups.HasGroup(magic))
			{
				field.Value = "<null>";
				return;
			}

			if (_profile.TagReferenceSize == 8)
			{
				var datumIndex = ReadUInt32(bytes, offset + 4);
				field.Value = magic + ": datum=0x" + datumIndex.ToString("X8");
				references.Add(new RawTagReference(definition.Name, magic, null, datumIndex, true));
				return;
			}

			var second = ReadUInt32(bytes, offset + 4);
			var third = ReadUInt32(bytes, offset + 8);
			var path = SelectBestReferencePath(bytes, dataOffset, second, third);
			if (string.IsNullOrWhiteSpace(path))
			{
				field.Value = string.IsNullOrWhiteSpace(magic) ? "<null>" : magic + ": <empty>";
				return;
			}

			var reference = new RawTagReference(definition.Name, magic, path.Replace('/', '\\'));
			references.Add(reference);
			field.Value = magic + ":" + path;
		}

		private string SelectBestReferencePath(byte[] bytes, long dataOffset, uint second, uint third)
		{
			var firstLayout = ReadPointerString(bytes, dataOffset, second, third);
			var secondLayout = ReadPointerString(bytes, dataOffset, third, second);

			var firstScore = ScoreTagPath(firstLayout);
			var secondScore = ScoreTagPath(secondLayout);

			if (firstScore == 0 && secondScore == 0)
				return null;
			return secondScore > firstScore ? secondLayout : firstLayout;
		}

		private void ReadTagBlock(byte[] bytes, long offset, long dataOffset, PluginFieldDefinition definition, RawTagField field,
			IList<RawTagReference> references)
		{
			if (!IsReadable(bytes, offset, _profile.TagBlockSize))
			{
				field.Value = "<outside file>";
				return;
			}

			var count = unchecked((int)ReadUInt32(bytes, offset));
			var pointer = ReadUInt32(bytes, offset + 4);
			field.Value = count + " entries @ 0x" + pointer.ToString("X");

			var byteCount = count * (long)definition.ElementSize;
			var resolvedPointer = ResolveDataPointer(bytes, dataOffset, pointer, byteCount);
			if (count <= 0 || count > MaxBlockEntries || definition.ElementSize == 0 || resolvedPointer < 0)
			{
				if (count > 0 && count <= MaxBlockEntries && definition.ElementSize > 0)
					field.Value = "unresolved: " + field.Value + ", bytes=0x" + byteCount.ToString("X");
				return;
			}

			for (var i = 0; i < count; i++)
			{
				var entryOffset = resolvedPointer + i * (long)definition.ElementSize;
				var entry = new RawTagField("tagblock-entry", definition.Name + "[" + i + "]", entryOffset);
				field.Children.Add(entry);
				ReadFields(bytes, entryOffset, dataOffset, definition.Children, entry.Children, references);
			}
		}

		private string ResolveReference(RawTagReference reference)
		{
			if (string.IsNullOrEmpty(reference.Path) || string.IsNullOrEmpty(reference.GroupMagic))
				return null;

			var extension = _groups.GetExtension(reference.GroupMagic);
			if (extension == null)
				return null;

			var relativePath = reference.Path;
			if (!relativePath.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase))
				relativePath += "." + extension;

			var fullPath = Path.GetFullPath(Path.Combine(_tagsRoot, relativePath));
			return File.Exists(fullPath) && fullPath.StartsWith(_tagsRoot, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
		}

		private string ReadPointerString(byte[] bytes, long dataOffset, uint pointer, uint length)
		{
			if (length == 0 || length > 4096)
				return null;
			var resolvedPointer = ResolveDataPointer(bytes, dataOffset, pointer, 1);
			if (resolvedPointer < 0)
				return null;

			var max = (int)Math.Min(length, bytes.Length - resolvedPointer);
			var end = 0;
			while (end < max && bytes[(int)resolvedPointer + end] != 0)
				end++;
			return Encoding.ASCII.GetString(bytes, (int)resolvedPointer, end);
		}

		private long ResolveDataPointer(byte[] bytes, long dataOffset, uint pointer, long size)
		{
			if (IsReadable(bytes, pointer, size))
				return pointer;
			var relative = dataOffset + pointer;
			if (IsReadable(bytes, relative, size))
				return relative;

			if (_profile.VirtualPointerBase != 0 && pointer >= _profile.VirtualPointerBase)
			{
				var virtualOffset = pointer - _profile.VirtualPointerBase;
				if (IsReadable(bytes, virtualOffset, size))
					return virtualOffset;
				var relativeVirtualOffset = dataOffset + virtualOffset;
				if (IsReadable(bytes, relativeVirtualOffset, size))
					return relativeVirtualOffset;
			}

			return -1;
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

		private string MakeRelativePath(string fullPath)
		{
			if (!fullPath.StartsWith(_tagsRoot, StringComparison.OrdinalIgnoreCase))
				return fullPath;
			return fullPath.Substring(_tagsRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}

		private static bool IsReadable(byte[] bytes, long offset, long count)
		{
			return offset >= 0 && count >= 0 && offset + count <= bytes.Length;
		}

		private static byte ReadByte(byte[] bytes, long offset)
		{
			if (!IsReadable(bytes, offset, 1))
				throw new EndOfStreamException();
			return bytes[offset];
		}

		private static ushort ReadUInt16(byte[] bytes, long offset)
		{
			if (!IsReadable(bytes, offset, 2))
				throw new EndOfStreamException();
			return BitConverter.ToUInt16(bytes, (int)offset);
		}

		private static uint ReadUInt32(byte[] bytes, long offset)
		{
			if (!IsReadable(bytes, offset, 4))
				throw new EndOfStreamException();
			return BitConverter.ToUInt32(bytes, (int)offset);
		}

		private static float ReadFloat(byte[] bytes, long offset)
		{
			if (!IsReadable(bytes, offset, 4))
				throw new EndOfStreamException();
			return BitConverter.ToSingle(bytes, (int)offset);
		}

		private static string ReadMagic(byte[] bytes, long offset)
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

		private string NormalizeMagic(string magic)
		{
			if (_groups.HasGroup(magic))
				return magic;

			var chars = magic == null ? new char[0] : magic.ToCharArray();
			Array.Reverse(chars);
			var reversed = new string(chars);
			return _groups.HasGroup(reversed) ? reversed : magic;
		}

		private static int ScoreTagPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path) || path.Length > 256)
				return 0;

			var score = 1;
			for (var i = 0; i < path.Length; i++)
			{
				var c = path[i];
				if (c < 0x20 || c > 0x7E)
					return 0;
				if (char.IsLetterOrDigit(c))
				{
					score += 2;
					continue;
				}
				if (c == '\\' || c == '/' || c == '_' || c == '-' || c == ' ')
				{
					score += 1;
					continue;
				}
				if (c == '.' || c == ':')
					return 0;
			}

			return score;
		}

		private static string ReadFixedAscii(byte[] bytes, long offset, int size)
		{
			if (size <= 0 || !IsReadable(bytes, offset, size))
				return "";
			var end = 0;
			while (end < size && bytes[offset + end] != 0)
				end++;
			return Encoding.ASCII.GetString(bytes, (int)offset, end);
		}

		private static string ReadFixedUtf16(byte[] bytes, long offset, int size)
		{
			if (size <= 0 || !IsReadable(bytes, offset, size))
				return "";
			return Encoding.Unicode.GetString(bytes, (int)offset, size).TrimEnd('\0');
		}

		private static string DescribeScalar(byte[] bytes, long offset, string kind)
		{
			return IsReadable(bytes, offset, 4)
				? kind + " raw=0x" + ReadUInt32(bytes, offset).ToString("X8")
				: kind + " <outside file>";
		}
	}
}

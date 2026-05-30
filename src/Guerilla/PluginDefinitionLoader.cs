using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Guerilla
{
	public sealed class PluginDefinitionLoader
	{
		private readonly string _pluginDirectory;
		private readonly Dictionary<string, PluginDefinition> _cache =
			new Dictionary<string, PluginDefinition>(StringComparer.OrdinalIgnoreCase);

		public PluginDefinitionLoader(string pluginDirectory)
		{
			_pluginDirectory = pluginDirectory;
		}

		public PluginDefinition LoadByGroup(string groupMagic)
		{
			if (string.IsNullOrEmpty(groupMagic))
				return null;

			PluginDefinition cached;
			if (_cache.TryGetValue(groupMagic, out cached))
				return cached;

			var path = Path.Combine(_pluginDirectory, groupMagic.Trim() + ".xml");
			if (!File.Exists(path))
			{
				_cache[groupMagic] = null;
				return null;
			}

			var doc = new XmlDocument();
			doc.Load(path);
			var plugin = doc.DocumentElement;
			if (plugin == null || !string.Equals(plugin.Name, "plugin", StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("Plugin XML does not start with a <plugin> element: " + path);

			var baseSize = ReadInt(plugin, "baseSize", 0);
			var fields = new List<PluginFieldDefinition>();
			foreach (XmlNode child in plugin.ChildNodes)
				ReadField(child, fields);

			cached = new PluginDefinition(groupMagic, baseSize, fields);
			_cache[groupMagic] = cached;
			return cached;
		}

		private static void ReadField(XmlNode node, IList<PluginFieldDefinition> destination)
		{
			if (node.NodeType != XmlNodeType.Element)
				return;
			if (string.Equals(node.Name, "revisions", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(node.Name, "comment", StringComparison.OrdinalIgnoreCase))
				return;

			var kind = NormalizeKind(node.Name);
			var field = new PluginFieldDefinition(kind, ReadString(node, "name", node.Name), ReadUInt(node, "offset", 0),
				ReadBool(node, "visible", true));

			if (kind == "ascii" || kind == "utf16" || kind == "hexstring" || kind == "raw")
				field.Size = ReadInt(node, "size", ReadInt(node, "length", 0));
			if (kind == "tagref")
				field.WithGroup = ReadBool(node, "withGroup", ReadBool(node, "withClass", true));
			if (kind == "tagblock")
			{
				field.ElementSize = ReadUInt(node, "elementSize", ReadUInt(node, "entrySize", 0));
				foreach (XmlNode child in node.ChildNodes)
					ReadField(child, field.Children);
			}

			destination.Add(field);
		}

		private static string NormalizeKind(string name)
		{
			var lower = name.ToLowerInvariant();
			if (lower == "reflexive")
				return "tagblock";
			if (lower == "tagreference")
				return "tagref";
			return lower;
		}

		private static string ReadString(XmlNode node, string name, string fallback)
		{
			var attribute = node.Attributes == null ? null : node.Attributes[name];
			return attribute == null ? fallback : attribute.Value;
		}

		private static bool ReadBool(XmlNode node, string name, bool fallback)
		{
			var attribute = node.Attributes == null ? null : node.Attributes[name];
			if (attribute == null)
				return fallback;
			return attribute.Value == "1" || attribute.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
		}

		private static int ReadInt(XmlNode node, string name, int fallback)
		{
			var attribute = node.Attributes == null ? null : node.Attributes[name];
			return attribute == null ? fallback : ParseInt(attribute.Value);
		}

		private static uint ReadUInt(XmlNode node, string name, uint fallback)
		{
			var attribute = node.Attributes == null ? null : node.Attributes[name];
			return attribute == null ? fallback : (uint)ParseInt(attribute.Value);
		}

		private static int ParseInt(string value)
		{
			if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				return int.Parse(value.Substring(2), NumberStyles.HexNumber);
			if (value.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
				return -int.Parse(value.Substring(3), NumberStyles.HexNumber);
			return int.Parse(value, CultureInfo.InvariantCulture);
		}
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Guerilla
{
	public sealed class TagGroupRegistry
	{
		private readonly Dictionary<string, string> _magicByExtension =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _nameByMagic =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public static TagGroupRegistry LoadHalo1(string groupNamesPath)
		{
			return Load(groupNamesPath);
		}

		public static TagGroupRegistry Load(string groupNamesPath)
		{
			var registry = new TagGroupRegistry();
			var doc = new XmlDocument();
			doc.Load(groupNamesPath);

			foreach (XmlNode node in doc.SelectNodes("/tagGroups/group"))
			{
				var magic = node.Attributes["magic"].Value;
				var name = node.Attributes["name"].Value;
				registry._nameByMagic[magic] = name;
				registry._magicByExtension[name] = magic;
			}

			return registry;
		}

		public string GetGroupMagicForFile(string path)
		{
			var extension = Path.GetExtension(path);
			if (string.IsNullOrEmpty(extension))
				return null;
			extension = extension.Substring(1);

			string magic;
			return _magicByExtension.TryGetValue(extension, out magic) ? magic : null;
		}

		public string GetGroupName(string magic)
		{
			string name;
			return magic != null && _nameByMagic.TryGetValue(magic, out name) ? name : magic;
		}

		public bool HasGroup(string magic)
		{
			return magic != null && _nameByMagic.ContainsKey(magic);
		}

		public string GetExtension(string magic)
		{
			foreach (var pair in _magicByExtension)
				if (pair.Value == magic)
					return pair.Key;
			return null;
		}

		public IEnumerable<string> GroupMagics
		{
			get { return _nameByMagic.Keys; }
		}
	}
}

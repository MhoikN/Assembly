using System.Collections.Generic;

namespace Guerilla
{
	public sealed class PluginDefinition
	{
		public PluginDefinition(string groupMagic, int baseSize, IList<PluginFieldDefinition> fields)
		{
			GroupMagic = groupMagic;
			BaseSize = baseSize;
			Fields = fields;
		}

		public string GroupMagic { get; private set; }
		public int BaseSize { get; private set; }
		public IList<PluginFieldDefinition> Fields { get; private set; }
	}

	public sealed class PluginFieldDefinition
	{
		public PluginFieldDefinition(string kind, string name, uint offset, bool visible)
		{
			Kind = kind;
			Name = name;
			Offset = offset;
			Visible = visible;
			Children = new List<PluginFieldDefinition>();
		}

		public string Kind { get; private set; }
		public string Name { get; private set; }
		public uint Offset { get; private set; }
		public bool Visible { get; private set; }
		public int Size { get; set; }
		public uint ElementSize { get; set; }
		public bool WithGroup { get; set; }
		public IList<PluginFieldDefinition> Children { get; private set; }
	}
}

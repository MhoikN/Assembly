using System.Collections.Generic;

namespace Guerilla
{
	public sealed class RawTagField
	{
		public RawTagField(string kind, string name, long offset)
		{
			Kind = kind;
			Name = name;
			Offset = offset;
			Children = new List<RawTagField>();
		}

		public string Kind { get; private set; }
		public string Name { get; private set; }
		public long Offset { get; private set; }
		public int Size { get; set; }
		public object Value { get; set; }
		public IList<RawTagField> Children { get; private set; }
	}
}

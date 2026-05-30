using System.Collections.Generic;

namespace Guerilla
{
	public sealed class RawTag
	{
		public RawTag(string filePath, string relativePath, string groupMagic, string groupName)
		{
			FilePath = filePath;
			RelativePath = relativePath;
			GroupMagic = groupMagic;
			GroupName = groupName;
			Fields = new List<RawTagField>();
			References = new List<RawTagReference>();
			Dependencies = new List<RawTagDependency>();
			Children = new List<RawTag>();
			Messages = new List<string>();
		}

		public string FilePath { get; private set; }
		public string RelativePath { get; private set; }
		public string GroupMagic { get; private set; }
		public string GroupName { get; private set; }
		public long DataOffset { get; set; }
		public IList<RawTagField> Fields { get; private set; }
		public IList<RawTagReference> References { get; private set; }
		public IList<RawTagDependency> Dependencies { get; private set; }
		public IList<RawTag> Children { get; private set; }
		public IList<string> Messages { get; private set; }
	}
}

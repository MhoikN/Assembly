using System.Collections.Generic;

namespace Guerilla
{
	public sealed class SourceTagFile
	{
		public SourceTagFile(long dataOffset)
		{
			DataOffset = dataOffset;
			Dependencies = new List<RawTagDependency>();
			Messages = new List<string>();
		}

		public long DataOffset { get; private set; }
		public IList<RawTagDependency> Dependencies { get; private set; }
		public IList<string> Messages { get; private set; }
	}
}

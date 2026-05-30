namespace Guerilla
{
	public sealed class RawTagProfile
	{
		public RawTagProfile(int tagReferenceSize, int tagBlockSize, uint virtualPointerBase)
		{
			TagReferenceSize = tagReferenceSize;
			TagBlockSize = tagBlockSize;
			VirtualPointerBase = virtualPointerBase;
		}

		public int TagReferenceSize { get; private set; }
		public int TagBlockSize { get; private set; }
		public uint VirtualPointerBase { get; private set; }

		public static RawTagProfile Halo1()
		{
			return new RawTagProfile(16, 12, 0);
		}

		public static RawTagProfile Halo2()
		{
			return new RawTagProfile(8, 8, 0x0AF00000);
		}
	}
}

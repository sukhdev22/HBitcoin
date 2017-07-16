using System;
using System.IO;

using NTumbleBit.BouncyCastle.Utilities.IO;

namespace NTumbleBit.BouncyCastle.Asn1
{
	internal class DefiniteLengthInputStream
		: LimitedInputStream
	{
		private static readonly byte[] EmptyBytes = new byte[0];

		private readonly int _originalLength;
		private int _remaining;

		internal DefiniteLengthInputStream(
			Stream inStream,
			int length)
			: base(inStream, length)
		{
			if(length < 0)
				throw new ArgumentException("negative lengths not allowed", nameof(length));

			_originalLength = length;
			_remaining = length;

			if(length == 0)
			{
				SetParentEofDetect(true);
			}
		}

		internal int Remaining => _remaining;

		public override int ReadByte()
		{
			if(_remaining == 0)
				return -1;

			var b = _in.ReadByte();

			if (b < 0)
				throw new EndOfStreamException("DEF length " + _originalLength + " object truncated by " + _remaining);

			if(--_remaining == 0)
			{
				SetParentEofDetect(true);
			}

			return b;
		}

		public override int Read(
			byte[] buf,
			int off,
			int len)
		{
			if(_remaining == 0)
				return 0;

			var toRead = System.Math.Min(len, _remaining);
			var numRead = _in.Read(buf, off, toRead);

			if (numRead < 1)
				throw new EndOfStreamException("DEF length " + _originalLength + " object truncated by " + _remaining);

			if((_remaining -= numRead) == 0)
			{
				SetParentEofDetect(true);
			}

			return numRead;
		}

		internal void ReadAllIntoByteArray(byte[] buf)
		{
			if(_remaining != buf.Length)
				throw new ArgumentException("buffer length not right for data");

			if((_remaining -= Streams.ReadFully(_in, buf)) != 0)
				throw new EndOfStreamException("DEF length " + _originalLength + " object truncated by " + _remaining);
			SetParentEofDetect(true);
		}

		internal byte[] ToArray()
		{
			if(_remaining == 0)
				return EmptyBytes;

			var bytes = new byte[_remaining];
			if ((_remaining -= Streams.ReadFully(_in, bytes)) != 0)
				throw new EndOfStreamException("DEF length " + _originalLength + " object truncated by " + _remaining);
			SetParentEofDetect(true);
			return bytes;
		}
	}
}
// Copyright (C) 2013, Tim Rogers <virmitio@gmail.com>
// This file has been modified for use in this project.
// The original copyright has been preserved below.

/*
 * Copyright (C) 2008, Google Inc.
 * Copyright (C) 2009, Gil Ran <gilrun@gmail.com>
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or
 * without modification, are permitted provided that the following
 * conditions are met:
 *
 * - Redistributions of source code must retain the above copyright
 *   notice, this list of conditions and the following disclaimer.
 *
 * - Redistributions in binary form must reproduce the above
 *   copyright notice, this list of conditions and the following
 *   disclaimer in the documentation and/or other materials provided
 *   with the distribution.
 *
 * - Neither the name of the Git Development Community nor the
 *   names of its contributors may be used to endorse or promote
 *   products derived from this software without specific prior
 *   written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
 * ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Text;
using GitSharpImport.Core.Util;

namespace GitSharpImport.Core.Patch
{
	/// <summary>
	/// An error in a patch script.
	/// </summary>
	[Serializable]
	internal class FormatError
	{
		#region Severity enum

		/// <summary>
		/// Classification of an error.
		/// </summary>
		[Serializable]
		internal enum Severity
		{
			/// <summary>
			/// The error is unexpected, but can be worked around.
			/// </summary>
			WARNING,

			/// <summary>
			/// The error indicates the script is severely flawed.
			/// </summary>
			ERROR
		}

		#endregion

		private readonly byte[] _buf;
		private readonly string _message;
		private readonly int _offset;
		private readonly Severity _severity;

		internal FormatError(byte[] buffer, int ptr, Severity sev, string msg)
		{
			_buf = buffer;
			_offset = ptr;
			_severity = sev;
			_message = msg;
		}

		/// <summary>
		/// The severity of the error.
		/// </summary>
		/// <returns></returns>
		internal Severity getSeverity()
		{
			return _severity;
		}

		/// <summary>
		/// A message describing the error.
		/// </summary>
		/// <returns></returns>
		internal string getMessage()
		{
			return _message;
		}

		/// <summary>
		/// The byte buffer holding the patch script.
		/// </summary>
		/// <returns></returns>
		internal byte[] getBuffer()
		{
			return _buf;
		}

		/// <summary>
		/// Byte offset within <see cref="getBuffer()"/> where the error is
		/// </summary>
		/// <returns></returns>
		internal int getOffset()
		{
			return _offset;
		}

		/// <summary>
		/// Line of the patch script the error appears on.
		/// </summary>
		/// <returns></returns>
		internal string getLineText()
		{
			int eol = RawParseUtils.nextLF(_buf, _offset);
			return RawParseUtils.decode(Constants.CHARSET, _buf, _offset, eol);
		}

		public override string ToString()
		{
			var r = new StringBuilder();
			r.Append(Enum.GetName(typeof(Severity), getSeverity()));
			r.Append(": at offset ");
			r.Append(getOffset());
			r.Append(": ");
			r.Append(getMessage());
			r.Append("\n");
			r.Append("  in ");
			r.Append(getLineText());
			return r.ToString();
		}
	}
}
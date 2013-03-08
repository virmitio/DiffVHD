// Copyright (C) 2013, Tim Rogers <virmitio@gmail.com>
// This file has been modified for use in this project.
// The original copyright has been preserved below.

/*
 * Copyright (C) 2010, Henon <meinrad.recheis@gmail.com>
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GitSharpImport.Core.Diff;

namespace GitSharpImport
{

	/// <summary>
	/// Represents a line-based text (delimited with standard line delimiters such as CR and/or LF) 
	/// and allows access of lines by 1-based line number. Text holds a byte array internally which is used 
	/// for the diff algorithms which work on byte level.
	/// <para/>
	/// Note: The first line number in the text is 1. 
	/// </summary>
	internal class Text
	{

		/// <summary>
		/// Create a text instance from a string. The encoding UTF8 is used as default for generating the underlying byte array. 
		/// </summary>
		/// <param name="text"></param>
		internal Text(string text)
			: this(text, Encoding.UTF8)
		{
		}

		/// <summary>
		/// Create a text instance from a string. The encoding is used for generating the underlying byte array. 
		/// </summary>
		/// <param name="text"></param>
		/// <param name="encoding"></param>
		internal Text(string text, Encoding encoding)
			: this(new RawText(encoding.GetBytes(text)), encoding)
		{
		}

		internal Text(byte[] encoded_text)
			: this(new RawText(encoded_text), Encoding.UTF8)
		{
		}

		internal Text(RawText raw_text, Encoding encoding)
		{
			m_raw_text = raw_text;
			Encoding = encoding;
		}

		private readonly RawText m_raw_text;

		internal Encoding Encoding { get; set; }

		public static implicit operator RawText(Text text) // <-- [henon] undocumented cast operator to be able to get the wrapped core object.
		{
			return text.m_raw_text;
		}

		internal byte[] RawContent
		{
			get { return m_raw_text.Content; }
		}

		internal int RawLength
		{
			get
			{
				return RawContent.Length;
			}
		}

		internal int Length
		{
			get
			{
				return ToString().Length;
			}
		}

		internal int NumberOfLines
		{
			get
			{
				return m_raw_text.size();
			}
		}

		/// <summary>
		/// Returns a line of the text as encoded byte array.
		/// <para/>
		/// Note: The first line number in the text is 1
		/// </summary>
		/// <param name="line">1-based line number</param>
		/// <returns></returns>
		internal byte[] GetRawLine(int line)
		{
			if (line <= 0)
				throw new ArgumentOutOfRangeException("line", line, "Line index must not be <= 0");
			if (line > NumberOfLines)
				throw new ArgumentOutOfRangeException("line", line, "Line index is too large");
			int line_start = m_raw_text.LineStartIndices.get(line);
			int line_end = RawContent.Length;
			if (line + 1 < m_raw_text.LineStartIndices.size())
				line_end = m_raw_text.LineStartIndices.get(line + 1);
			return RawContent.Skip(line_start).Take(line_end - line_start).ToArray();
		}

		internal string GetLine(int line)
		{
			return Encoding.GetString(GetRawLine(line));
		}

		/// <summary>
		/// Get a text block by lines as encoded byte array. The text block starts with begin of start_line and ends with start of end_line.
		/// <para/>
		/// Note: The first line number in the text is 1
		/// </summary>
		/// <param name="start_line">1-based line number marking the start of the text block at the start of the specified line</param>
		/// <param name="end_line">1-based line number markign the end of the text block at the start of the specified line</param>
		/// <returns></returns>
		internal byte[] GetRawBlock(int start_line, int end_line)
		{
			if (end_line < start_line)
				throw new ArgumentException("Block end index must be larger than or equal start index", "end_line");
			if (start_line <= 0)
				throw new ArgumentOutOfRangeException("start_line", start_line, "Line index must not be <= 0");
			if (end_line > NumberOfLines + 1)
				throw new ArgumentOutOfRangeException("end_line", end_line, "Line index is too large");
			int block_start = m_raw_text.LineStartIndices.get(start_line);
			int block_end = RawContent.Length;
			if (end_line < m_raw_text.LineStartIndices.size())
				block_end = m_raw_text.LineStartIndices.get(end_line);
			return RawContent.Skip(block_start).Take(block_end - block_start).ToArray();
		}

		internal string GetBlock(int start_line, int end_line)
		{
			return Encoding.GetString(GetRawBlock(start_line, end_line));
		}

		public override string ToString()
		{
			return Encoding.GetString(RawContent);
		}

	}
}

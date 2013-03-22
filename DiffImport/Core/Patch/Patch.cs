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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GitSharpImport.Core.Util;

namespace GitSharpImport.Core.Patch
{

    internal class Patch
    {
        internal static readonly byte[] SigFooter = Constants.encodeASCII("-- \n");

        private List<HunkHeader> _hunks;

        internal List<HunkHeader> Hunks
        {
            get { return new List<HunkHeader>(_hunks); }
        }

        internal Patch()
        {
            _hunks = new List<HunkHeader>();
        }

        //private int ParseHunks(FileHeader fh, int c, int end)
        internal int ParseHunks(byte[] bytes)
        {

            
            int c = 0;
            int end = bytes.Length;
            byte[] buf = bytes;
            while (c < end)
            {
                //if (FileHeader.isHunkHdr(buf, c, end) == fh.ParentCount)
                if ((new int[]{1, 3}).Contains(FileHeader.isHunkHdr(buf, c, end)))
                {
                    //HunkHeader h = fh.newHunkHeader(c);
                    HunkHeader h = new HunkHeader(buf, c);
                    h.parseHeader();
                    c = h.parseBody(this, end);
                    h.EndOffset = c;
                    //fh.addHunk(h);
                    _hunks.Add(h);
                    if (c < end)
                    {
                        switch (buf[c])
                        {
                            case (byte)'@':
                            case (byte)'d':
                            case (byte)'\n':
                                break;

                            default:
                                if (RawParseUtils.match(buf, c, SigFooter) < 0)
                                    throw new FormatException(String.Format("Unexpected hunk trailer.  character #{0} of buffer:\n{1}", c, buf.AsString()));
                                break;
                        }
                    }
                    continue;
                }

                int eol = RawParseUtils.nextLF(buf, c);

                // Skip this line and move to the next. Its probably garbage
                // After the last hunk of a file.
                //
                c = eol;
            }

            return _hunks.Count;
        }

        internal byte[] SimpleApply(byte[] Base)
        {
            var curOldLine = 1;
            int c = 0;
            int prevC = 0;
            List<byte> outBytes = new List<byte>();

            foreach (var hunk in _hunks.OrderBy(h => h.OldStartLine))
            {
                while (curOldLine++ < hunk.OldStartLine)
                    c = RawParseUtils.nextLF(Base, c);
                outBytes.AddRange(Base.Skip(prevC).Take(c - prevC));
                prevC = c;
                curOldLine--; // Corrective decriment from the previous loop.

                int numLines = 0;
                while(numLines++ < hunk.OldLineCount)
                    c = RawParseUtils.nextLF(Base, c);
                string[] oldLines = Base.Skip(prevC).Take(c - prevC).ToArray().AsString().Split('\n');
                curOldLine += numLines-1;

                byte[] hBytes = hunk.ResultLines();
                string[] newLines = hBytes.AsString().Split('\n');

                if (!SimpleContextCheck(oldLines, newLines))
                    throw new InvalidDataException(String.Format("Context test failed.  Hunk:\n{0}", hunk.Buffer.AsString()));
                outBytes.AddRange(hBytes);
                
                prevC = c;

            }

            // Remember to add the tail of the file...
            outBytes.AddRange(Base.Skip(prevC));

            return outBytes.ToArray();
        }

        private bool SimpleContextCheck(string[] Old, string[] New)
        {
            bool startMatch = true, endMatch = true;

            int MaxContextReduction = Math.Min(3, Math.Min(Old.Length-2, New.Length-2));

            for (int i = 0; i < MaxContextReduction; i++)
            {
                startMatch &= Old[i].Equals(New[i]);
                endMatch &= Old[Old.Length - (i + 1)].Equals(New[New.Length - (i + 1)]);
            }

            return startMatch && endMatch;
        }

        /// <summary>
        /// Attempt at a smarter means of applying patches to the file.
        /// </summary>
        /// <param name="Old">Original text lines.</param>
        /// <param name="ChangesWithContext">The lines of text from the diff hunk, with context.</param>
        /// <returns>No idea at the moment.</returns>
        private int SmartContextFind(string[] Old, string[] ChangesWithContext)
        {
            int MaxFuzz = Math.Min(3, Math.Min(Old.Length - 2, ChangesWithContext.Length - 2));
            int Fuzz = 0;

            var expected = ChangesWithContext.Where(s => !s.StartsWith("+")).ToArray();
            Dictionary<int, string> oldLines = new Dictionary<int, string>();
            for (int i = 0; i < expected.Length; i++)
                oldLines.Add(i, expected[i]);
            var removals = oldLines.Where(kvp => kvp.Value.StartsWith("-")).OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            while (removals.Any())
            {
                // base our placements around the removals
                List<string> block = new List<string>();

                var current = removals.First();
                removals.Remove(current.Key);
                block.Add(current.Value);

                while (removals.Any() && removals.First().Key == current.Key + 1)
                {
                    current = removals.First();
                    removals.Remove(current.Key);
                    block.Add(current.Value);
                }



            }


            // Remove the following line when this function works properly.
            return 0;
        }

        
    }

    internal static class ArrExtensions
    {
        public static string AsString(this byte[] bytes)
        {
            return bytes.Aggregate(String.Empty, (current1, b) => current1 + (char) b);
        }
    }

}
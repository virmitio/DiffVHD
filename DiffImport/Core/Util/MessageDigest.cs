// Copyright (C) 2013, Tim Rogers <virmitio@gmail.com>
// This file has been modified for use in this project.
// The original copyright has been preserved below.

/*
 * Copyright (C) 2009, Kevin Thompson <kevin.thompson@theautomaters.com>
 * Copyright (C) 2009, Henon <meinrad.recheis@gmail.com>
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
using System.Security.Cryptography;
using System.IO;

namespace GitSharpImport.Core.Util
{
    internal abstract class MessageDigest : IDisposable
    {
        internal static MessageDigest getInstance(string algorithm)
        {
            switch (algorithm.ToLower())
            {
                case "sha-1":
                    return new MessageDigest<SHA1Managed>();
                case "md5":
                    return new MessageDigest<MD5CryptoServiceProvider>();
                default:
                    throw new NotSupportedException(string.Format("The requested algorithm \"{0}\" is not supported.", algorithm));
            }
        }

        internal abstract byte[] Digest();
        internal abstract byte[] Digest(byte[] input);
        internal abstract void Reset();
        internal abstract void Update(byte input);
        internal abstract void Update(byte[] input);
        internal abstract void Update(byte[] input, int index, int count);
        public abstract void Dispose();
    }

    internal class MessageDigest<TAlgorithm> : MessageDigest where TAlgorithm : HashAlgorithm, new()
    {
        private CryptoStream _stream;
        private TAlgorithm _hash;

        internal MessageDigest()
        {
            Init();
        }

        private void Init()
        {
            _hash = new TAlgorithm();
            _stream = new CryptoStream(Stream.Null, _hash, CryptoStreamMode.Write);
        }

        internal override byte[] Digest()
        {
            _stream.FlushFinalBlock();
            var ret = _hash.Hash;
            Reset();
            return ret;
        }

        internal override byte[] Digest(byte[] input)
        {
            using (var me = new MessageDigest<TAlgorithm>())
            {
                me.Update(input);
                return me.Digest();
            }
        }

        internal override void Reset()
        {
            Dispose();
            Init();
        }

        internal override void Update(byte input)
        {
            _stream.WriteByte(input);
        }

        internal override void Update(byte[] input)
        {
            _stream.Write(input, 0, input.Length);
        }

        internal override void Update(byte[] input, int index, int count)
        {
            _stream.Write(input, index, count);
        }

        public override void Dispose()
        {
            if (_stream != null)
                _stream.Dispose();
            _stream = null;
        }
    }
}

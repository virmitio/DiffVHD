// Copyright (C) 2013, Tim Rogers <virmitio@gmail.com>
// This file has been modified for use in this project.


using System.Collections.Generic;

namespace GitSharpImport.Core.Util
{
    internal static class IListUtil
    {
        internal static bool isEmpty<T>(this ICollection<T> l)
        {
			if (l == null)
				throw new System.ArgumentNullException ("l");
            return l.Count == 0;
        }

        internal static bool isEmpty<TK, TV>(this IDictionary<TK, TV> d)
        {
			if (d == null)
				throw new System.ArgumentNullException ("d");
            return (d.Count == 0);
        }
    }
}
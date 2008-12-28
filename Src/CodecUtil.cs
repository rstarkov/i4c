using System;
using System.Collections.Generic;

namespace i4c
{
    public static class CodecUtil
    {
        public static void Shift(int[] data, int shift)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] += shift;
        }

        public static ulong[] CountValues(int[] data)
        {
            ulong[] result = new ulong[256];
            int maxSym = -1;
            foreach (var sym in data)
            {
                if (maxSym < sym)
                    maxSym = sym;
                if (sym >= result.Length)
                    Array.Resize(ref result, Math.Max(sym+1, result.Length * 2));
                result[sym]++;
            }
            Array.Resize(ref result, maxSym + 1);
            return result;
        }

        public static Dictionary<int, ulong> CountValuesMap(int[] data)
        {
            Dictionary<int, ulong> result = new Dictionary<int, ulong>();
            foreach (var sym in data)
            {
                if (result.ContainsKey(sym))
                    result[sym]++;
                else
                    result.Add(sym, 1);
            }
            return result;
        }
    }
}

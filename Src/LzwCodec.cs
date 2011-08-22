using System;
using System.Collections.Generic;

namespace i4c
{
    public class IntString : IComparable<IntString>
    {
        public int[] Arr;

        public IntString()
        {
            Arr = new int[0];
        }

        public IntString(int sym)
        {
            Arr = new int[] { sym };
        }

        public static IntString operator +(IntString one, int other)
        {
            var res = new IntString();
            res.Arr = new int[one.Arr.Length + 1];
            Array.Copy(one.Arr, res.Arr, one.Arr.Length);
            res.Arr[res.Arr.Length - 1] = other;
            return res;
        }

        public int CompareTo(IntString other)
        {
            var arr1 = Arr;
            var arr2 = other.Arr;
            var i1 = 0;
            var i2 = 0;
            while (true)
            {
                if (i1 == arr1.Length && i2 == arr2.Length)
                    return 0;
                if (i1 == arr1.Length)
                    return 1;
                if (i2 == arr2.Length)
                    return -1;
                if (arr1[i1] > arr2[i2])
                    return 1;
                if (arr1[i1] < arr2[i2])
                    return -1;
                i1++;
                i2++;
            }
        }

        public override bool Equals(object obj)
        {
            return CompareTo((IntString) obj) == 0;
        }

        public override int GetHashCode()
        {
            int a = 1, b = 0;
            int len = Arr.Length;
            int i = 0;

            while (len > 0)
            {
                int tlen = len > 5552 ? 5552 : len;
                len -= tlen;
                do
                {
                    a += Arr[i++];
                    b += a;
                } while (--tlen != 0);

                a %= 65521;
                b %= 65521;
            }

            return (b << 16) | a;
        }
    }

    public class LzwCodec
    {
        int _maxSymbol;

        public LzwCodec(int maxSymbol)
        {
            _maxSymbol = maxSymbol;
        }

        public int[] Encode(int[] data)
        {
            Dictionary<IntString, int> dict = new Dictionary<IntString, int>();
            for (int i = 0; i <= _maxSymbol; i++)
                dict.Add(new IntString(i), i);
            var nextSym = _maxSymbol + 1;
            var result = new List<int>();
            var word = new IntString();

            foreach (var sym in data)
            {
                var wordsym = word + sym;
                if (dict.ContainsKey(wordsym))// && (wordsym.Length < 8))
                    word = wordsym;
                else
                {
                    if (word.Arr.Length > 0)
                        result.Add(dict[word]);
                    //if (wordsym.Arr.All(val => val < 16))
                    //{
                    dict.Add(wordsym, nextSym);
                    nextSym++;
                    //}
                    word = new IntString(sym);
                }
            }

            return result.ToArray();
        }
    }
}

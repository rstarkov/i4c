using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using RT.Util;

namespace i4c
{
    class HashForeseer : Foreseer
    {
        private Size[] _sizes;
        private PatternHashTable _hashtable = new PatternHashTable();
        private Foreseer _fallback;

        public HashForeseer(int maxW, int maxH, Foreseer fallback)
        {
            _fallback = fallback;
            var seq = new[] { 3, 4, 5, 7, 9, 13, 17, 21 };
            var sizes = new List<Size>();
            for (int xi = 0; xi < seq.Length; xi++)
                for (int yi = 0; yi < seq.Length; yi++)
                    if (seq[xi] <= maxW && seq[yi] <= maxH)
                        sizes.Add(new Size(seq[xi], seq[yi]));
            _sizes = sizes.OrderByDescending(sz => sz.Width * sz.Height).ToArray();
        }

        public override int Foresee(IntField image, int x, int y, int p)
        {
            foreach (var size in _sizes)
            {
                ulong hash = HashImageRegion(image, x, y, size.Width, size.Height);
                uint c0, c1, c2, c3;
                _hashtable.GetCounts(hash, out c0, out c1, out c2, out c3);
                if (c0 + c1 + c2 + c3 == 0)
                    continue;
                if (c0 >= c1 && c0 >= c2 && c0 >= c3)
                    return 0;
                else if (c1 >= c2 && c1 >= c3)
                    return 1;
                else if (c2 >= c3)
                    return 2;
                else
                    return 3;
            }
            return _fallback.Foresee(image, x, y, p);
        }

        public override void Learn(IntField image, int x, int y, int p, int actual)
        {
            foreach (var size in _sizes)
            {
                ulong hash = HashImageRegion(image, x, y, size.Width, size.Height);
                _hashtable.AddCount(hash, actual);
            }
        }

        private ulong HashImageRegion(IntField image, int px, int py, int width, int height)
        {
            // Initialize the CRC by hashing a ushort containing the number of pixels in the pattern
            ulong crc64;
            uint length = (uint) (width * height);
            crc64 = crc64_table[(byte) (length)];
            crc64 = crc64_table[(byte) (crc64 ^ (length >> 8))] ^ (crc64 >> 8);

            // We can fit 4 pixels in a byte before we pass it to CRC64, but keep it as an int to save on repeated conversion to byte (as all math is done in int32 anyway)
            uint curByte = 0;
            uint curByteCount = 0;

            // We're iterating over a rectangle whose bottom row of pixels is only half full. This loop is arranged so as to iterate over everything else before reaching the "missing" pixels
            for (int dY = -height + 1; dY <= 0; dY++)
                for (int dX = (width - 1) / 2 - width + 1; dX <= (width - 1) / 2; dX++) // for even widths it's biased to the left, where we have more pixels in the bottommost row
                {
                    if (dY == 0 && dX == 0)
                        goto done;
                    curByte = (curByte << 2) | (uint) image[px + dX, py + dY];
                    curByteCount++;
                    if (curByteCount == 4)
                    {
                        crc64 = crc64_table[(byte) (crc64 ^ curByte)] ^ (crc64 >> 8);
                        curByte = curByteCount = 0;
                    }
                }
            done:;
            if (curByteCount > 0)
                crc64 = crc64_table[(byte) (crc64 ^ curByte)] ^ (crc64 >> 8);
            return crc64;
        }

        #region CRC64

        // from https://github.com/srned/baselib/blob/master/crc64.c

        ulong crc64(ulong crc, byte[] s, ulong l)
        {
            foreach (var b in s)
            {

            }
            return crc;
        }

        static ulong[] crc64_table = new ulong[]
        {
            0x0000000000000000, 0x7ad870c830358979,
            0xf5b0e190606b12f2, 0x8f689158505e9b8b,
            0xc038e5739841b68f, 0xbae095bba8743ff6,
            0x358804e3f82aa47d, 0x4f50742bc81f2d04,
            0xab28ecb46814fe75, 0xd1f09c7c5821770c,
            0x5e980d24087fec87, 0x24407dec384a65fe,
            0x6b1009c7f05548fa, 0x11c8790fc060c183,
            0x9ea0e857903e5a08, 0xe478989fa00bd371,
            0x7d08ff3b88be6f81, 0x07d08ff3b88be6f8,
            0x88b81eabe8d57d73, 0xf2606e63d8e0f40a,
            0xbd301a4810ffd90e, 0xc7e86a8020ca5077,
            0x4880fbd87094cbfc, 0x32588b1040a14285,
            0xd620138fe0aa91f4, 0xacf86347d09f188d,
            0x2390f21f80c18306, 0x594882d7b0f40a7f,
            0x1618f6fc78eb277b, 0x6cc0863448deae02,
            0xe3a8176c18803589, 0x997067a428b5bcf0,
            0xfa11fe77117cdf02, 0x80c98ebf2149567b,
            0x0fa11fe77117cdf0, 0x75796f2f41224489,
            0x3a291b04893d698d, 0x40f16bccb908e0f4,
            0xcf99fa94e9567b7f, 0xb5418a5cd963f206,
            0x513912c379682177, 0x2be1620b495da80e,
            0xa489f35319033385, 0xde51839b2936bafc,
            0x9101f7b0e12997f8, 0xebd98778d11c1e81,
            0x64b116208142850a, 0x1e6966e8b1770c73,
            0x8719014c99c2b083, 0xfdc17184a9f739fa,
            0x72a9e0dcf9a9a271, 0x08719014c99c2b08,
            0x4721e43f0183060c, 0x3df994f731b68f75,
            0xb29105af61e814fe, 0xc849756751dd9d87,
            0x2c31edf8f1d64ef6, 0x56e99d30c1e3c78f,
            0xd9810c6891bd5c04, 0xa3597ca0a188d57d,
            0xec09088b6997f879, 0x96d1784359a27100,
            0x19b9e91b09fcea8b, 0x636199d339c963f2,
            0xdf7adabd7a6e2d6f, 0xa5a2aa754a5ba416,
            0x2aca3b2d1a053f9d, 0x50124be52a30b6e4,
            0x1f423fcee22f9be0, 0x659a4f06d21a1299,
            0xeaf2de5e82448912, 0x902aae96b271006b,
            0x74523609127ad31a, 0x0e8a46c1224f5a63,
            0x81e2d7997211c1e8, 0xfb3aa75142244891,
            0xb46ad37a8a3b6595, 0xceb2a3b2ba0eecec,
            0x41da32eaea507767, 0x3b024222da65fe1e,
            0xa2722586f2d042ee, 0xd8aa554ec2e5cb97,
            0x57c2c41692bb501c, 0x2d1ab4dea28ed965,
            0x624ac0f56a91f461, 0x1892b03d5aa47d18,
            0x97fa21650afae693, 0xed2251ad3acf6fea,
            0x095ac9329ac4bc9b, 0x7382b9faaaf135e2,
            0xfcea28a2faafae69, 0x8632586aca9a2710,
            0xc9622c4102850a14, 0xb3ba5c8932b0836d,
            0x3cd2cdd162ee18e6, 0x460abd1952db919f,
            0x256b24ca6b12f26d, 0x5fb354025b277b14,
            0xd0dbc55a0b79e09f, 0xaa03b5923b4c69e6,
            0xe553c1b9f35344e2, 0x9f8bb171c366cd9b,
            0x10e3202993385610, 0x6a3b50e1a30ddf69,
            0x8e43c87e03060c18, 0xf49bb8b633338561,
            0x7bf329ee636d1eea, 0x012b592653589793,
            0x4e7b2d0d9b47ba97, 0x34a35dc5ab7233ee,
            0xbbcbcc9dfb2ca865, 0xc113bc55cb19211c,
            0x5863dbf1e3ac9dec, 0x22bbab39d3991495,
            0xadd33a6183c78f1e, 0xd70b4aa9b3f20667,
            0x985b3e827bed2b63, 0xe2834e4a4bd8a21a,
            0x6debdf121b863991, 0x1733afda2bb3b0e8,
            0xf34b37458bb86399, 0x8993478dbb8deae0,
            0x06fbd6d5ebd3716b, 0x7c23a61ddbe6f812,
            0x3373d23613f9d516, 0x49aba2fe23cc5c6f,
            0xc6c333a67392c7e4, 0xbc1b436e43a74e9d,
            0x95ac9329ac4bc9b5, 0xef74e3e19c7e40cc,
            0x601c72b9cc20db47, 0x1ac40271fc15523e,
            0x5594765a340a7f3a, 0x2f4c0692043ff643,
            0xa02497ca54616dc8, 0xdafce7026454e4b1,
            0x3e847f9dc45f37c0, 0x445c0f55f46abeb9,
            0xcb349e0da4342532, 0xb1eceec59401ac4b,
            0xfebc9aee5c1e814f, 0x8464ea266c2b0836,
            0x0b0c7b7e3c7593bd, 0x71d40bb60c401ac4,
            0xe8a46c1224f5a634, 0x927c1cda14c02f4d,
            0x1d148d82449eb4c6, 0x67ccfd4a74ab3dbf,
            0x289c8961bcb410bb, 0x5244f9a98c8199c2,
            0xdd2c68f1dcdf0249, 0xa7f41839ecea8b30,
            0x438c80a64ce15841, 0x3954f06e7cd4d138,
            0xb63c61362c8a4ab3, 0xcce411fe1cbfc3ca,
            0x83b465d5d4a0eece, 0xf96c151de49567b7,
            0x76048445b4cbfc3c, 0x0cdcf48d84fe7545,
            0x6fbd6d5ebd3716b7, 0x15651d968d029fce,
            0x9a0d8ccedd5c0445, 0xe0d5fc06ed698d3c,
            0xaf85882d2576a038, 0xd55df8e515432941,
            0x5a3569bd451db2ca, 0x20ed197575283bb3,
            0xc49581ead523e8c2, 0xbe4df122e51661bb,
            0x3125607ab548fa30, 0x4bfd10b2857d7349,
            0x04ad64994d625e4d, 0x7e7514517d57d734,
            0xf11d85092d094cbf, 0x8bc5f5c11d3cc5c6,
            0x12b5926535897936, 0x686de2ad05bcf04f,
            0xe70573f555e26bc4, 0x9ddd033d65d7e2bd,
            0xd28d7716adc8cfb9, 0xa85507de9dfd46c0,
            0x273d9686cda3dd4b, 0x5de5e64efd965432,
            0xb99d7ed15d9d8743, 0xc3450e196da80e3a,
            0x4c2d9f413df695b1, 0x36f5ef890dc31cc8,
            0x79a59ba2c5dc31cc, 0x037deb6af5e9b8b5,
            0x8c157a32a5b7233e, 0xf6cd0afa9582aa47,
            0x4ad64994d625e4da, 0x300e395ce6106da3,
            0xbf66a804b64ef628, 0xc5bed8cc867b7f51,
            0x8aeeace74e645255, 0xf036dc2f7e51db2c,
            0x7f5e4d772e0f40a7, 0x05863dbf1e3ac9de,
            0xe1fea520be311aaf, 0x9b26d5e88e0493d6,
            0x144e44b0de5a085d, 0x6e963478ee6f8124,
            0x21c640532670ac20, 0x5b1e309b16452559,
            0xd476a1c3461bbed2, 0xaeaed10b762e37ab,
            0x37deb6af5e9b8b5b, 0x4d06c6676eae0222,
            0xc26e573f3ef099a9, 0xb8b627f70ec510d0,
            0xf7e653dcc6da3dd4, 0x8d3e2314f6efb4ad,
            0x0256b24ca6b12f26, 0x788ec2849684a65f,
            0x9cf65a1b368f752e, 0xe62e2ad306bafc57,
            0x6946bb8b56e467dc, 0x139ecb4366d1eea5,
            0x5ccebf68aecec3a1, 0x2616cfa09efb4ad8,
            0xa97e5ef8cea5d153, 0xd3a62e30fe90582a,
            0xb0c7b7e3c7593bd8, 0xca1fc72bf76cb2a1,
            0x45775673a732292a, 0x3faf26bb9707a053,
            0x70ff52905f188d57, 0x0a2722586f2d042e,
            0x854fb3003f739fa5, 0xff97c3c80f4616dc,
            0x1bef5b57af4dc5ad, 0x61372b9f9f784cd4,
            0xee5fbac7cf26d75f, 0x9487ca0fff135e26,
            0xdbd7be24370c7322, 0xa10fceec0739fa5b,
            0x2e675fb4576761d0, 0x54bf2f7c6752e8a9,
            0xcdcf48d84fe75459, 0xb71738107fd2dd20,
            0x387fa9482f8c46ab, 0x42a7d9801fb9cfd2,
            0x0df7adabd7a6e2d6, 0x772fdd63e7936baf,
            0xf8474c3bb7cdf024, 0x829f3cf387f8795d,
            0x66e7a46c27f3aa2c, 0x1c3fd4a417c62355,
            0x935745fc4798b8de, 0xe98f353477ad31a7,
            0xa6df411fbfb21ca3, 0xdc0731d78f8795da,
            0x536fa08fdfd90e51, 0x29b7d047efec8728,
        };

        #endregion CRC64
    }

    class PatternHashTable
    {
        private BinaryHashTree[] _hashtrees = new BinaryHashTree[65536];

        public void AddCount(ulong hash, int value)
        {
            int treeindex = (int) (hash & 0xFFFF);
            uint hash0 = (uint) (hash >> 16);
            ushort hash1 = (ushort) (hash >> 48);
            _hashtrees[treeindex].Add(hash0, hash1, value);
        }

        public void GetCounts(ulong hash, out uint count0, out uint count1, out uint count2, out uint count3)
        {
            int treeindex = (int) (hash & 0xFFFF);
            if (_hashtrees[treeindex].Count == 0)
            {
                count0 = count1 = count2 = count3 = 0;
                return;
            }
            uint hash0 = (uint) (hash >> 16);
            ushort hash1 = (ushort) (hash >> 48);
            _hashtrees[treeindex].GetCounts(hash0, hash1, out count0, out count1, out count2, out count3);
        }

        struct BinaryHashTree
        {
            private BinaryHashNode[] _nodes;
            public int Count { get; private set; }

            public void Add(uint hash0, ushort hash1, int value)
            {
                if (Count == 0)
                {
                    _nodes = new BinaryHashNode[8];
                    _nodes[0].Hash0 = hash0;
                    _nodes[0].Hash1 = hash1;
                    _nodes[0].AddCount(value);
                    Count++;
                }
                else
                {
                    int matchType;
                    int index = FindNode(hash0, hash1, out matchType);
                    if (matchType == 0)
                        _nodes[index].AddCount(value);
                    else
                    {
                        int newIndex = Count;
                        Count++;
                        if (newIndex >= 65536)
                            throw new Exception("Tree is full"); // can't fit a reference to this node in a ushort (which is the type of Left/Right)
                        if (newIndex >= _nodes.Length)
                        {
                            var old = _nodes;
                            _nodes = new BinaryHashNode[_nodes.Length + _nodes.Length / 2];
                            Array.Copy(old, _nodes, old.Length);
                        }
                        _nodes[newIndex].Hash0 = hash0;
                        _nodes[newIndex].Hash1 = hash1;
                        if (matchType < 0)
                            _nodes[index].Left = (ushort) newIndex;
                        else
                            _nodes[index].Right = (ushort) newIndex;
                        _nodes[newIndex].AddCount(value);
                    }
                }
            }

            public void GetCounts(uint hash0, ushort hash1, out uint c0, out uint c1, out uint c2, out uint c3)
            {
                int matchType;
                int index = FindNode(hash0, hash1, out matchType);
                if (matchType == 0)
                    _nodes[index].GetCounts(out c0, out c1, out c2, out c3);
                else
                    c0 = c1 = c2 = c3 = 0;
            }

            /// <param name="matchType">0 if a match is found, -1 if a new node needs to be attached to Left, 1 to Right.</param>
            /// <returns>Index of the node with the matching hash, or index of the node to which a new node needs to be attached.</returns>
            private int FindNode(uint hash0, ushort hash1, out int matchType)
            {
                if (Count == 0)
                    throw new Exception("FindNode must not be called on an empty tree");
                ushort cur = 0;
                while (true)
                {
                    ushort next;
                    if (hash0 == _nodes[cur].Hash0 && hash1 == _nodes[cur].Hash1)
                    {
                        matchType = 0;
                        return cur;
                    }
                    else if (hash0 < _nodes[cur].Hash0 || (hash0 == _nodes[cur].Hash0 && hash1 < _nodes[cur].Hash1))
                    {
                        next = _nodes[cur].Left;
                        if (next == 0)
                        {
                            matchType = -1;
                            return cur;
                        }
                    }
                    else
                    {
                        next = _nodes[cur].Right;
                        if (next == 0)
                        {
                            matchType = 1;
                            return cur;
                        }
                    }

                    cur = next;
                }
            }
        }

        struct BinaryHashNode
        {
            public ushort Left; // can use 0 for none because 0 is the root node and is not a leaf of anything anyway
            public ushort Right;
            public uint Hash0;
            public ushort Hash1;
            private ushort Counts1;
            private uint Counts0;

            public void AddCount(int value)
            {
                // Counts1[15] marks saturation
                // Counts0[11..0] for color 0
                // Counts1[11..0] for color 3
                // Counts0[23..12] for color 1
                // Counts0[31..24] + Counts1[14..12]  for color 2
                if ((Counts1 & 0x8000) != 0)
                    return; // saturated; can't count any further without skewing the ratios

                uint count;
                switch (value)
                {

                    case 0: // 12 bits
                        count = Counts0 & 0xFFF;
                        count++;
                        if (count == 0xFFF) Counts1 |= 0x8000;
                        Counts0 = (Counts0 & 0xFFFFF000) | count;
                        return;


                    case 1: // 12 bits
                        count = (Counts0 >> 12) & 0xFFF;
#if DEBUG
                        if (count == 0xFFF) throw new Exception();
#endif
                        count++;
                        if (count == 0xFFF) Counts1 |= 0x8000;
                        Counts0 = (Counts0 & 0xFF000FFF) | (count << 12);
                        return;


                    case 2: // 11 bits
                        count = (Counts0 >> 24) | (((uint) Counts1 & 0x7000) >> 4);
#if DEBUG
                        if (count == 0x7FF) throw new Exception();
#endif
                        count++;
                        if (count == 0x7FF) Counts1 |= 0x8000;
                        Counts0 = (Counts0 & 0x00FFFFFF) | (count << 24);
                        Counts1 = (ushort) (((uint) Counts1 & 0x8FFF) | ((count & 0x700) << 4));
                        return;


                    case 3: // 12 bits
                        count = (uint) (Counts1 & 0xFFF);
#if DEBUG
                        if (count == 0xFFF) throw new Exception();
#endif
                        count++;
                        if (count == 0xFFF) Counts1 |= 0x8000;
                        Counts1 = (ushort) ((ushort) (Counts1 & 0xF000) | count);
                        return;


                    default:
                        throw new Exception();
                }
            }

            public void GetCounts(out uint c0, out uint c1, out uint c2, out uint c3)
            {
                c0 = Counts0 & 0xFFF;
                c1 = (Counts0 >> 12) & 0xFFF;
                c2 = (Counts0 >> 24) | (((uint) Counts1 & 0x7000) >> 4);
                c3 = (uint) (Counts1 & 0xFFF);
            }

            public static void SelfTest()
            {
                for (int tests = 0; tests < 1000; tests++)
                {
                    uint c0 = 0, c1 = 0, c2 = 0, c3 = 0;
                    var node = new BinaryHashNode();
                    bool done = false;
                    var checkCounts = Ut.Lambda(() =>
                    {
                        uint ac0, ac1, ac2, ac3;
                        node.GetCounts(out ac0, out ac1, out ac2, out ac3);
                        if (ac0 != c0 || ac1 != c1 || ac2 != c2 || ac3 != c3)
                            throw new Exception();
                        if (c0 == 0xFFF || c1 == 0xFFF || c2 == 0x7FF || c3 == 0xFFF)
                        {
                            node.AddCount(0);
                            node.AddCount(1);
                            node.AddCount(2);
                            node.AddCount(3);
                            node.GetCounts(out ac0, out ac1, out ac2, out ac3);
                            if (ac0 != c0 || ac1 != c1 || ac2 != c2 || ac3 != c3) // must not have incremented since one of the counters is saturated
                                throw new Exception();
                            done = true;
                        }
                    });
                    do
                    {
                        if (Rnd.NextDouble() < 0.25)
                        {
                            c0++;
                            node.AddCount(0);
                            checkCounts();
                        }
                        else if (Rnd.NextDouble() < 0.33)
                        {
                            c1++;
                            node.AddCount(1);
                            checkCounts();
                        }
                        else if (Rnd.NextDouble() < 0.50)
                        {
                            c2++;
                            node.AddCount(2);
                            checkCounts();
                        }
                        else
                        {
                            c3++;
                            node.AddCount(3);
                            checkCounts();
                        }
                    }
                    while (!done);
                }
            }
        }

        public static void SelfTest()
        {
            BinaryHashNode.SelfTest();
        }
    }
}

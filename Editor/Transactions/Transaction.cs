using System;
using System.IO;
using UnityEngine;

namespace UnityEditor.Search
{
    [Flags]
    enum AssetModification
    {
        Updated = 1,
        Removed = 1 << 1,
        Moved = 1 << 2
    }

    struct Transaction
    {
        public long timestamp;
        public Hash128 guid;
        public int state;

        public static int size
        {
            get
            {
                unsafe
                {
                    return sizeof(long) + sizeof(Hash128) + sizeof(int);
                }
            }
        }

        public Transaction(string guid, int state)
            : this(guid, state, DateTime.Now.ToBinary())
        {
        }

        public Transaction(string guid, int state, long timestamp)
            : this(Hash128.Parse(guid), state, timestamp)
        {
        }

        public Transaction(string guid, AssetModification state)
            : this(guid, (int)state)
        {
        }

        public Transaction(string guid, AssetModification state, long timestamp)
            : this(guid, (int)state, timestamp)
        {
        }

        public Transaction(Hash128 guid, int state, long timestamp)
        {
            this.timestamp = timestamp;
            this.guid = guid;
            this.state = state;
        }

        public override string ToString()
        {
            return $"[{DateTime.FromBinary(timestamp):u}] ({state}) {guid}";
        }

        public AssetModification GetState()
        {
            return (AssetModification)state;
        }

        public void ToBinary(BinaryWriter bw)
        {
            bw.Write(timestamp);
            #if USE_SEARCH_MODULE
            bw.Write(guid.u64_0);
            bw.Write(guid.u64_1);
            #else
            bw.Write(guid.Getu64_0());
            bw.Write(guid.Getu64_1());
            #endif
            bw.Write(state);
        }

        public static Transaction FromBinary(BinaryReader br)
        {
            var timestamp = br.ReadInt64();
            var u640 = br.ReadUInt64();
            var u641 = br.ReadUInt64();
            var state = br.ReadInt32();
            return new Transaction(new Hash128(u640, u641), state, timestamp);
        }
    }
}

using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestWsClient
{
    [ProtoContract]
    public class WsRequestMessage
    {
        [ProtoMember(1)] public int PartitionKey;
        [ProtoMember(2)] public string Operation;
        [ProtoMember(3)] public byte[] Value;
    }
}

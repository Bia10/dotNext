using System.Runtime.InteropServices;

namespace DotNext.Net.Cluster.Consensus.Raft.TransportServices.ConnectionOriented;

internal partial class Client : RaftClusterMember
{
    [StructLayout(LayoutKind.Auto)]
    private readonly struct SynchronizeRequest : IClientExchange<long?>
    {
        private readonly long commitIndex;

        internal SynchronizeRequest(long commitIndex) => this.commitIndex = commitIndex;

        ValueTask IClientExchange<long?>.RequestAsync(ProtocolStream protocol, Memory<byte> buffer, CancellationToken token)
            => protocol.WriteSynchronizeRequestAsync(commitIndex, token);

        ValueTask<long?> IClientExchange<long?>.ResponseAsync(ProtocolStream protocol, Memory<byte> buffer, CancellationToken token)
            => protocol.ReadNullableInt64Async(token);
    }

    private protected sealed override Task<long?> SynchronizeAsync(long commitIndex, CancellationToken token)
        => RequestAsync<SynchronizeRequest, long?>(new(commitIndex), token);
}
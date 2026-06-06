using System.Collections.Generic;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;

namespace Spacecraft.Client
{
    /// <summary>
    /// Client-side cache of chunks received from the server. This is a *view* of the
    /// authoritative world, not the source of truth: edits arrive as server messages.
    /// </summary>
    public sealed class ClientWorld
    {
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new Dictionary<ChunkCoord, ChunkData>();

        // This world's circumference (set from WorldEnvironment) — chunk/block X wrap at the right size.
        private int _circumference = WorldConstants.Circumference;

        public IReadOnlyDictionary<ChunkCoord, ChunkData> Chunks => _chunks;

        /// <summary>Sets the world circumference (per-body size) so the wrap matches the server.</summary>
        public void SetCircumference(int circumference)
            => _circumference = circumference > 0 ? circumference : WorldConstants.Circumference;

        // Longitude wraps: chunks are cached by canonical chunk-X (a chunk a lap away is the same chunk),
        // and block lookups canonicalize X so an unbounded player coordinate still resolves after laps.
        public void StoreChunk(ChunkCoord coord, ushort[] blocks)
        {
            coord = WorldConstants.CanonicalChunk(coord, _circumference);
            _chunks[coord] = ChunkData.FromRaw(coord, blocks);
        }

        /// <summary>Drops all cached chunks (used when travelling to another world).</summary>
        public void Clear() => _chunks.Clear();

        public bool TryGetChunk(ChunkCoord coord, out ChunkData chunk)
            => _chunks.TryGetValue(WorldConstants.CanonicalChunk(coord, _circumference), out chunk);

        public BlockId GetBlock(int wx, int wy, int wz)
        {
            wx = WorldConstants.WrapX(wx, _circumference);
            var coord = WorldConstants.WorldToChunk(new Vector3i(wx, wy, wz));
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                return BlockId.Air;
            }

            var local = WorldConstants.WorldToLocal(new Vector3i(wx, wy, wz));
            return chunk.Get(local.X, local.Y, local.Z);
        }

        /// <summary>Applies a single authoritative block change from the server.</summary>
        public bool ApplyBlockChange(int wx, int wy, int wz, ushort block, out ChunkCoord affected)
        {
            wx = WorldConstants.WrapX(wx, _circumference);
            affected = WorldConstants.WorldToChunk(new Vector3i(wx, wy, wz));
            if (!_chunks.TryGetValue(affected, out var chunk))
            {
                return false;
            }

            var local = WorldConstants.WorldToLocal(new Vector3i(wx, wy, wz));
            chunk.Set(local.X, local.Y, local.Z, new BlockId(block));
            return true;
        }
    }
}

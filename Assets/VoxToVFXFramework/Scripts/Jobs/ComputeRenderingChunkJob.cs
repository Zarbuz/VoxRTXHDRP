using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using VoxToVFXFramework.Scripts.Data;

namespace VoxToVFXFramework.Scripts.Jobs
{
	[BurstCompile]
	public struct ComputeRenderingChunkJob : IJobParallelFor
	{
		[ReadOnly] public NativeList<int> ChunkIndex;
		[ReadOnly] public UnsafeParallelHashMap<int, UnsafeList<VoxelVFX>> Data;
		public NativeList<VoxelVFX>.ParallelWriter Buffer;

		public void Execute(int index)
		{
			int chunkIndex = ChunkIndex[index];
			Buffer.AddRangeNoResize(Data[chunkIndex]);
		}

	}
}

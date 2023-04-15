using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxToVFXFramework.Scripts.Data;

namespace VoxToVFXFramework.Scripts.Jobs
{
	[BurstCompile]
	public struct ComputeVisibleChunkJob : IJobParallelFor
	{
		public NativeArray<ChunkVFX> Chunks;
		[ReadOnly] public float RenderDistance;
		[ReadOnly] public float LodDistanceLod0;
		[ReadOnly] public float LodDistanceLod1;
		[ReadOnly] public float3 PlayerPosition;
		[ReadOnly] public NativeArray<Plane> Planes;

		public NativeList<int>.ParallelWriter ChunkIndex;
		public void Execute(int index)
		{
			ChunkVFX chunkVFX = Chunks[index];
			bool isVisible = TestPlanesAABB(Planes, new Bounds(Chunks[index].CenterWorldPosition, Vector3.one * WorldData.CHUNK_SIZE));
			float distance = math.distance(PlayerPosition, chunkVFX.CenterWorldPosition);

			if (isVisible && distance < RenderDistance)
			{
				if (distance < LodDistanceLod0 && chunkVFX.LodLevel == 1)
				{
					chunkVFX.IsActive = 1;
				}
				else if (distance >= LodDistanceLod0 && distance < LodDistanceLod1 && chunkVFX.LodLevel == 2)
				{
					chunkVFX.IsActive = 1;
				}
				else if (distance >= LodDistanceLod1 && chunkVFX.LodLevel == 4)
				{
					chunkVFX.IsActive = 1;
				}
				else
				{
					chunkVFX.IsActive = 0;
				}
			}
			else
			{
				chunkVFX.IsActive = 0;
			}

			Chunks[index] = chunkVFX;
			if (chunkVFX.IsActive == 1)
			{
				ChunkIndex.AddNoResize(index);
			}
		}

		public bool TestPlanesAABB(NativeArray<Plane> planes, Bounds bounds)
		{
			for (int i = 0; i < planes.Length; i++)
			{
				Plane plane = planes[i];
				float3 normal_sign = math.sign(plane.normal);
				float3 test_point = (float3)(bounds.center) + (bounds.extents * normal_sign);

				float dot = math.dot(test_point, plane.normal);
				if (dot + plane.distance < 0)
					return false;
			}

			return true;
		}
	}
}

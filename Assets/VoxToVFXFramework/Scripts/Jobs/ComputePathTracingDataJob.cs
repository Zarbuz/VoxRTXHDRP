using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxToVFXFramework.Scripts.Data;
using VoxToVFXFramework.Scripts.Extensions;

namespace VoxToVFXFramework.Scripts.Jobs
{
	[BurstCompile]
	public struct ComputePathTracingDataJob : IJobParallelFor
	{
		[ReadOnly] public NativeList<VoxelVFX> Data;
		[ReadOnly] public NativeArray<ChunkVFX> Chunks;
		[ReadOnly] public NativeArray<bool> MaterialAlpha;
		public NativeParallelMultiHashMap<int, Matrix4x4>.ParallelWriter Result;
		public void Execute(int index)
		{
			VoxelVFX voxel = Data[index];
			float4 decodedPosition = voxel.DecodePosition();
			int colorIndex = (int)decodedPosition.w;

			VoxelAdditionalData additionalData = voxel.DecodeAdditionalData();
			ChunkVFX chunk = Chunks[additionalData.ChunkIndex];
			Vector3 worldPosition = chunk.WorldPosition + new Vector3(decodedPosition.x, decodedPosition.y, decodedPosition.z);

			if (MaterialAlpha[colorIndex])
			{
				Matrix4x4 matrix = new Matrix4x4();
				matrix.SetTRS(worldPosition, Quaternion.identity, Vector3.one * chunk.LodLevel);
				Result.Add(colorIndex, matrix);
			}
			else
			{
				float offset = 0.5f * chunk.LodLevel;
				if ((additionalData.VoxelFace & VoxelFace.Top) != 0)
				{
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(worldPosition + new Vector3(0, offset, 0), Quaternion.Euler(90, 0, 0), Vector3.one * chunk.LodLevel);
					Result.Add(colorIndex, matrix);
				}

				if ((additionalData.VoxelFace & VoxelFace.Right) != 0)
				{
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(worldPosition + new Vector3(offset, 0, 0), Quaternion.Euler(0, -90, 0), Vector3.one * chunk.LodLevel);
					Result.Add(colorIndex, matrix);
				}

				if ((additionalData.VoxelFace & VoxelFace.Bottom) != 0)
				{
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(worldPosition + new Vector3(0, -offset, 0), Quaternion.Euler(270, 0, 0), Vector3.one * chunk.LodLevel);
					Result.Add(colorIndex, matrix);
				}

				if ((additionalData.VoxelFace & VoxelFace.Left) != 0)
				{
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(worldPosition + new Vector3(-offset, 0, 0), Quaternion.Euler(0, 90, 0), Vector3.one * chunk.LodLevel);
					Result.Add(colorIndex, matrix);

				}

				if ((additionalData.VoxelFace & VoxelFace.Front) != 0)
				{
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(worldPosition + new Vector3(0, 0, offset), Quaternion.Euler(0, 180, 0), Vector3.one * chunk.LodLevel);
					Result.Add(colorIndex, matrix);
				}

				if ((additionalData.VoxelFace & VoxelFace.Back) != 0)
				{
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(worldPosition + new Vector3(0, 0, -offset), Quaternion.Euler(0, 0, 0), Vector3.one * chunk.LodLevel);
					Result.Add(colorIndex, matrix);
				}
			}
		}
	}
}

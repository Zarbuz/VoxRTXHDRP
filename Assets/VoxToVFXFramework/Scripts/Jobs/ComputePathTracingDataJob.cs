using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
		[ReadOnly] public UnsafeList<VoxelVFX> Data;
		[ReadOnly] public ChunkVFX ChunkData;
		//[ReadOnly] public NativeArray<bool> MaterialAlpha;
		public NativeArray<Matrix4x4> Result;
		public void Execute(int index)
		{
			VoxelVFX voxel = Data[index];
			float4 decodedPosition = voxel.DecodePosition();
			int colorIndex = (int)decodedPosition.w;

			Vector3 worldPosition = ChunkData.WorldPosition + new Vector3(decodedPosition.x, decodedPosition.y, decodedPosition.z);

			//if (MaterialAlpha[colorIndex])
			{
				Matrix4x4 matrix = new Matrix4x4();
				matrix.SetTRS(worldPosition, Quaternion.identity, Vector3.one * ChunkData.LodLevel);
				Result[index] = matrix;
				//Result.Add(colorIndex, matrix);
			}
			//else
			//{
			//	float offset = 0.5f * chunk.LodLevel;
			//	if ((additionalData.VoxelFace & VoxelFace.Top) != 0)
			//	{
			//		Matrix4x4 matrix = new Matrix4x4();
			//		matrix.SetTRS(worldPosition + new Vector3(0, offset, 0), Quaternion.Euler(90, 0, 0), Vector3.one * chunk.LodLevel);
			//		Result.Add(colorIndex, matrix);
			//	}

			//	if ((additionalData.VoxelFace & VoxelFace.Right) != 0)
			//	{
			//		Matrix4x4 matrix = new Matrix4x4();
			//		matrix.SetTRS(worldPosition + new Vector3(offset, 0, 0), Quaternion.Euler(0, -90, 0), Vector3.one * chunk.LodLevel);
			//		Result.Add(colorIndex, matrix);
			//	}

			//	if ((additionalData.VoxelFace & VoxelFace.Bottom) != 0)
			//	{
			//		Matrix4x4 matrix = new Matrix4x4();
			//		matrix.SetTRS(worldPosition + new Vector3(0, -offset, 0), Quaternion.Euler(270, 0, 0), Vector3.one * chunk.LodLevel);
			//		Result.Add(colorIndex, matrix);
			//	}

			//	if ((additionalData.VoxelFace & VoxelFace.Left) != 0)
			//	{
			//		Matrix4x4 matrix = new Matrix4x4();
			//		matrix.SetTRS(worldPosition + new Vector3(-offset, 0, 0), Quaternion.Euler(0, 90, 0), Vector3.one * chunk.LodLevel);
			//		Result.Add(colorIndex, matrix);

			//	}

			//	if ((additionalData.VoxelFace & VoxelFace.Front) != 0)
			//	{
			//		Matrix4x4 matrix = new Matrix4x4();
			//		matrix.SetTRS(worldPosition + new Vector3(0, 0, offset), Quaternion.Euler(0, 180, 0), Vector3.one * chunk.LodLevel);
			//		Result.Add(colorIndex, matrix);
			//	}

			//	if ((additionalData.VoxelFace & VoxelFace.Back) != 0)
			//	{
			//		Matrix4x4 matrix = new Matrix4x4();
			//		matrix.SetTRS(worldPosition + new Vector3(0, 0, -offset), Quaternion.Euler(0, 0, 0), Vector3.one * chunk.LodLevel);
			//		Result.Add(colorIndex, matrix);
			//	}
			//}
		}
	}
}

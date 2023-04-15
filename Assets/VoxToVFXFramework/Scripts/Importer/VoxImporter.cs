﻿using FileToVoxCore.Vox;
using FileToVoxCore.Vox.Chunks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxToVFXFramework.Scripts.Data;
using VoxToVFXFramework.Scripts.Jobs;

namespace VoxToVFXFramework.Scripts.Importer
{
	public class VoxImporter
	{
		private class ShapeModelCount
		{
			public int Total;
			public int Count;
		}

		#region Fields
		public WorldData WorldData { get; private set; }

		private VoxModelCustom mVoxModel;
		private readonly Dictionary<int, Matrix4x4> mModelMatrix = new Dictionary<int, Matrix4x4>();
		private readonly Dictionary<int, ShapeModelCount> mShapeModelCounts = new Dictionary<int, ShapeModelCount>();
		#endregion

		#region PublicMethods

		public void LoadVoxModelAsync(string path, Action<float> onProgressCallback, Action<WorldData> onFinishedCallback)
		{
			CustomVoxReader voxReader = new CustomVoxReader();
			voxReader.LoadModelAsync(path, (progress) => OnReadVoxProgress(progress, onProgressCallback), (model) => OnVoxModelReaded(model, onProgressCallback, onFinishedCallback));
		}

		private void OnVoxModelReaded(VoxModelCustom obj, Action<float> onProgressCallback, Action<WorldData> onFinishedCallback)
		{
			if (obj == null)
			{
				onFinishedCallback?.Invoke(null);
				return;
			}
			mVoxModel = obj;
			VoxelDataCreatorManager.Instance.StartCoroutine(DecodeImportedData(onProgressCallback, onFinishedCallback));
		}


		private IEnumerator DecodeImportedData(Action<float> onProgressCallback, Action<WorldData> onFinishedCallback)
		{
			InitShapeModelCounts();
			WorldData = new WorldData(mVoxModel);
			VoxelDataCreatorManager.Instance.MainStep = 2;

			for (int i = 0; i < mVoxModel.TransformNodeChunks.Count; i++)
			{
				TransformNodeChunk transformNodeChunk = mVoxModel.TransformNodeChunks[i];
				int childId = transformNodeChunk.ChildId;

				if (mModelMatrix.ContainsKey(transformNodeChunk.Id))
				{
					mModelMatrix[transformNodeChunk.Id] *= ReadMatrix4X4FromRotation(transformNodeChunk.RotationAt(), transformNodeChunk.TranslationAt());
				}
				else
				{
					mModelMatrix[transformNodeChunk.Id] = ReadMatrix4X4FromRotation(transformNodeChunk.RotationAt(), transformNodeChunk.TranslationAt());
				}

				GroupNodeChunk groupNodeChunk = mVoxModel.GroupNodeChunks.FirstOrDefault(grp => grp.Id == childId);
				if (groupNodeChunk != null)
				{
					foreach (int child in groupNodeChunk.ChildIds)
					{
						mModelMatrix[child] = ReadMatrix4X4FromRotation(transformNodeChunk.RotationAt(), transformNodeChunk.TranslationAt());
					}
				}
				else
				{
					ShapeNodeChunk shapeNodeChunk = mVoxModel.ShapeNodeChunks.FirstOrDefault(shp => shp.Id == childId);
					if (shapeNodeChunk == null)
					{
						Debug.LogError("Failed to find chunk with ID: " + childId);
					}
					else
					{
						foreach (ShapeModel shapeModel in shapeNodeChunk.Models)
						{
							int modelId = shapeModel.ModelId;
							VoxelDataCustom voxelData = mVoxModel.VoxelFramesCustom[modelId];
							mShapeModelCounts[shapeModel.ModelId].Count++;
							WriteVoxelFrameData(transformNodeChunk.Id, voxelData);
							if (mShapeModelCounts[shapeModel.ModelId].Count == mShapeModelCounts[shapeModel.ModelId].Total)
							{
								voxelData.VoxelNativeArray.Dispose();
							}
						}
					}
				}

				yield return new WaitForEndOfFrame();
				onProgressCallback?.Invoke(i / (float)mVoxModel.TransformNodeChunks.Count);
			}

			foreach (VoxelDataCustom voxelDataCustom in mVoxModel.VoxelFramesCustom.Where(voxelDataCustom => voxelDataCustom.VoxelNativeArray.IsCreated))
			{
				voxelDataCustom.VoxelNativeArray.Dispose();
			}

			onFinishedCallback?.Invoke(WorldData);
		}

		private void OnReadVoxProgress(float progress, Action<float> onProgressCallback)
		{
			onProgressCallback?.Invoke(progress);
		}

		public static int GetGridPos(int x, int y, int z, int3 volumeSize)
			=> volumeSize.x * volumeSize.y * z + volumeSize.x * y + x;

		public static int3 Get3DPos(int idx, int3 volumeSize)
		{
			int3 result = new int3();
			result.z = idx / (volumeSize.x * volumeSize.y);
			idx -= result.z * volumeSize.x * volumeSize.y;
			result.y = idx / volumeSize.x;
			result.x = idx % volumeSize.x;
			return result;
		}
		#endregion

		#region PrivateMethods

		private void InitShapeModelCounts()
		{
			foreach (ShapeModel shapeModel in mVoxModel.ShapeNodeChunks.SelectMany(shapeNodeChunk => shapeNodeChunk.Models))
			{
				if (!mShapeModelCounts.ContainsKey(shapeModel.ModelId))
				{
					mShapeModelCounts[shapeModel.ModelId] = new ShapeModelCount();
				}

				mShapeModelCounts[shapeModel.ModelId].Total++;
			}
		}

		public void Dispose()
		{
			mShapeModelCounts.Clear();
			mModelMatrix.Clear();
			mVoxModel = null;
			WorldData?.Dispose();
			WorldData = null;
			GC.Collect();
		}

		private void WriteVoxelFrameData(int transformChunkId, VoxelDataCustom data)
		{
			int3 initialVolumeSize = new int3(data.VoxelsWide, data.VoxelsTall, data.VoxelsDeep);
			int3 originSize = new int3(initialVolumeSize.x, initialVolumeSize.y, initialVolumeSize.z);
			originSize.y = data.VoxelsDeep;
			originSize.z = data.VoxelsTall;

			Vector3 pivot = new Vector3(originSize.x / 2, originSize.y / 2, originSize.z / 2);
			Vector3 fpivot = new Vector3(originSize.x / 2f, originSize.y / 2f, originSize.z / 2f);

			int maxCapacity = initialVolumeSize.x * initialVolumeSize.y * initialVolumeSize.z;

			if (data.VoxelNativeArray.Length == 0)
			{
				return;
			}

			NativeArray<byte> initialDataClean = new NativeArray<byte>(data.VoxelNativeArray.Length, Allocator.TempJob);
			JobHandle removeInvisibleVoxelJob = new RemoveInvisibleVoxelJob()
			{
				Data = data.VoxelNativeArray,
				VolumeSize = initialVolumeSize,
				Result = initialDataClean,
				Materials = WorldData.Materials
			}.Schedule(initialVolumeSize.z, 64);
			removeInvisibleVoxelJob.Complete();

			NativeList<Vector4> resultLod0 = new NativeList<Vector4>(Allocator.TempJob);
			resultLod0.SetCapacity(maxCapacity);
			JobHandle job = new ComputeVoxelPositionJob
			{
				Matrix4X4 = mModelMatrix[transformChunkId],
				VolumeSize = initialVolumeSize,
				Pivot = pivot,
				FPivot = fpivot,
				Data = initialDataClean,
				Result = resultLod0.AsParallelWriter(),
			}.Schedule(initialVolumeSize.z, 64);
			job.Complete();
			initialDataClean.Dispose();


			WorldData.AddVoxels(resultLod0);
			resultLod0.Dispose();
		}

		public static Matrix4x4 ReadMatrix4X4FromRotation(Rotation rotation, FileToVoxCore.Schematics.Tools.Vector3 transform)
		{
			Matrix4x4 result = Matrix4x4.identity;
			{
				byte r = Convert.ToByte(rotation);
				int indexRow0 = (r & 3);
				int indexRow1 = (r & 12) >> 2;
				bool signRow0 = (r & 16) == 0;
				bool signRow1 = (r & 32) == 0;
				bool signRow2 = (r & 64) == 0;

				result.SetRow(0, Vector4.zero);
				switch (indexRow0)
				{
					case 0: result[0, 0] = signRow0 ? 1f : -1f; break;
					case 1: result[0, 1] = signRow0 ? 1f : -1f; break;
					case 2: result[0, 2] = signRow0 ? 1f : -1f; break;
				}
				result.SetRow(1, Vector4.zero);
				switch (indexRow1)
				{
					case 0: result[1, 0] = signRow1 ? 1f : -1f; break;
					case 1: result[1, 1] = signRow1 ? 1f : -1f; break;
					case 2: result[1, 2] = signRow1 ? 1f : -1f; break;
				}
				result.SetRow(2, Vector4.zero);
				switch (indexRow0 + indexRow1)
				{
					case 1: result[2, 2] = signRow2 ? 1f : -1f; break;
					case 2: result[2, 1] = signRow2 ? 1f : -1f; break;
					case 3: result[2, 0] = signRow2 ? 1f : -1f; break;
				}

				result.SetColumn(3, new Vector4(transform.X, transform.Y, transform.Z, 1f));
			}
			return result;
		}

		#endregion
	}
}

using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using VoxToVFXFramework.Scripts.Data;
using VoxToVFXFramework.Scripts.Jobs;
using VoxToVFXFramework.Scripts.Singleton;

namespace VoxToVFXFramework.Scripts.Managers
{
	[RequireComponent(typeof(UnityEngine.Camera))]
	public class ManualRTASManager : ModuleSingleton<ManualRTASManager>
	{
		#region ScriptParameters

		[SerializeField] private Mesh QuadMesh;
		[SerializeField] private Mesh CubeMesh;
		#endregion

		#region ConstStatic


		#endregion

		#region Fields

		private Dictionary<int, List<int>> mHandleIds = new Dictionary<int, List<int>>();
		private RayTracingAccelerationStructure mRtas;
		private HDCamera mHdCamera;
		private bool mRender;

		#endregion

		#region UnityMethods

		protected override void OnStart()
		{
			mHdCamera = HDCamera.GetOrCreate(GetComponent<UnityEngine.Camera>());
			mRtas = new RayTracingAccelerationStructure();
		}

		private void OnDestroy()
		{
			mRtas?.Dispose();
		}

		private void Update()
		{
			if (Keyboard.current.oKey.wasPressedThisFrame && RuntimeVoxManager.Instance.RenderWithPathTracing)
			{
				DisablePathTracing();
				return;
			}

			if (mRender)
			{
				mRtas.Build(transform.position);
			}
		}

		#endregion

		#region PublicMethods

		public void DisablePathTracing()
		{
			ClearInstances();
			HDRenderPipeline renderPipeline = RenderPipelineManager.currentPipeline as HDRenderPipeline;
			renderPipeline?.ResetPathTracing();
			PostProcessingManager.Instance.SetPathTracing(false);
			RuntimeVoxManager.Instance.RenderWithPathTracing = false;
			RuntimeVoxManager.Instance.ForceRefreshRender = true;
		}

		public void Build()
		{
			if (!RuntimeVoxManager.Instance.RenderWithPathTracing)
			{
				Debug.LogError("Can't render without pathtracing enabled!");
				return;
			}

			for (int index = 0; index < RuntimeVoxManager.Instance.Chunks.Length; index++)
			{
				ChunkVFX chunkVFX = RuntimeVoxManager.Instance.Chunks[index];
				if (chunkVFX.IsActive == 0 && mHandleIds.ContainsKey(index))
				{
					foreach (int handleId in mHandleIds[index])
					{
						mRtas.RemoveInstance(handleId);
					}

					mHandleIds.Remove(index);
				}

				if (chunkVFX.IsActive == 1 && !mHandleIds.ContainsKey(index))
				{
					UnsafeList<VoxelVFX> data = RuntimeVoxManager.Instance.LoadedData[index];

					NativeParallelMultiHashMap<int, Matrix4x4> positionResult = new NativeParallelMultiHashMap<int, Matrix4x4>(data.Length, Allocator.TempJob);
					JobHandle computePathTracingData = new ComputePathTracingDataJob()
					{
						ChunkData = chunkVFX,
						Data = data,
						PositionResult = positionResult.AsParallelWriter(),
					}.Schedule(data.Length, 64);
					computePathTracingData.Complete();

					List<int> handleIds = new List<int>();
					for (int i = 0; i < 255; i++)
					{
						if (!positionResult.ContainsKey(i)) continue;
						RayTracingMeshInstanceConfig config = new RayTracingMeshInstanceConfig(CubeMesh, 0, RuntimeVoxManager.Instance.Materials[i]);

						var enumerator = positionResult.GetValuesForKey(i);
						NativeList<Matrix4x4> position = new NativeList<Matrix4x4>(Allocator.Temp);
						while (enumerator.MoveNext())
						{
							position.Add(enumerator.Current);
						}

						if (position.Length > 0)
						{
							int handleId = mRtas.AddInstances(config, position.AsArray());
							handleIds.Add(handleId);
						}
					}
					
					if (handleIds.Count > 0)
					{
						mHandleIds.Add(index, handleIds);
					}
					positionResult.Dispose();
				}
			}

			// Build the RTAS
			mRtas.Build(transform.position);

			// Assign it to the camera
			mHdCamera.rayTracingAccelerationStructure = mRtas;
			mRender = true;
		}



		#endregion

		#region PrivateMethods

		private void ClearInstances()
		{
			Debug.Log("[ManualRTASManager] ClearInstances");
			foreach (int handleId in mHandleIds.Values.SelectMany(handleIds => handleIds))
			{
				mRtas.RemoveInstance(handleId);
			}
			mHandleIds.Clear();
			//mHdCamera.rayTracingAccelerationStructure = null;
			mRender = false;
		}



		#endregion
	}
}

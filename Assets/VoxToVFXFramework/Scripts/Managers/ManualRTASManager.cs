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

		private const int MAX_INSTANCES_PER_CONFIG = 1048575;

		#endregion

		#region Fields

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
			CustomFrameSettingsManager.Instance.SetRaytracingActive(false);
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
				if (chunkVFX.IsActive == 0 && chunkVFX.PathTracingHandleId != 0)
				{
					mRtas.RemoveInstance(chunkVFX.PathTracingHandleId);
					chunkVFX.PathTracingHandleId = 0;
					RuntimeVoxManager.Instance.Chunks[index] = chunkVFX;
				}

				if (chunkVFX.IsActive == 1 && chunkVFX.PathTracingHandleId == 0)
				{
					UnsafeList<VoxelVFX> data = RuntimeVoxManager.Instance.LoadedData[index];
					RayTracingMeshInstanceConfig config = new RayTracingMeshInstanceConfig(CubeMesh, 0, RuntimeVoxManager.Instance.Materials[0]);
					NativeArray<Matrix4x4> result = new NativeArray<Matrix4x4>(data.Length, Allocator.TempJob);
					JobHandle computePathTracingData = new ComputePathTracingDataJob()
					{
						ChunkData = chunkVFX,
						Data = data,
						Result = result
					}.Schedule(data.Length, 64);
					computePathTracingData.Complete();
					int handleId = mRtas.AddInstances(config, result);
					chunkVFX.PathTracingHandleId = handleId;
					RuntimeVoxManager.Instance.Chunks[index] = chunkVFX;
					result.Dispose();
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
			mRtas.ClearInstances();
			mRender = false;
			//mRtas.Build(transform.position);

			mHdCamera.rayTracingAccelerationStructure = null;
		}



		#endregion
	}
}

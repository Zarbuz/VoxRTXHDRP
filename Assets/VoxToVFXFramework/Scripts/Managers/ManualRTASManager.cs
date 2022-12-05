using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
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
			LightManager.Instance.SetMainLightActive(false);
			CustomFrameSettingsManager.Instance.SetRaytracingActive(false);
			RuntimeVoxManager.Instance.RenderWithPathTracing = false;
			RuntimeVoxManager.Instance.ForceRefreshRender = true;
		}

		public void Build(NativeParallelMultiHashMap<int, Matrix4x4> chunks)
		{
			Debug.Log("[ManualRTASManager] Build");

			if (!RuntimeVoxManager.Instance.RenderWithPathTracing)
			{
				Debug.LogError("Can't render without pathtracing enabled!");
				return;
			}

			ClearInstances();

			for (int i = 0; i < 255; i++)
			{
				if (!chunks.ContainsKey(i))
				{
					continue;
				}

				Mesh mesh = RuntimeVoxManager.Instance.Materials[i].color.a == 1 ? CubeMesh : QuadMesh;
				RayTracingMeshInstanceConfig config = new RayTracingMeshInstanceConfig(mesh, 0, RuntimeVoxManager.Instance.Materials[i]);

				NativeParallelMultiHashMap<int, Matrix4x4>.Enumerator enumerator = chunks.GetValuesForKey(i);
				NativeList<Matrix4x4> list = new NativeList<Matrix4x4>(Allocator.Temp);
				foreach (Matrix4x4 matrix in enumerator)
				{
					if (list.Length < MAX_INSTANCES_PER_CONFIG)
					{
						list.Add(matrix);
					}
					else
					{
						mRtas.AddInstances(config, list.AsArray());
						list.Clear();
					}
				}

				mRtas.AddInstances(config, list.AsArray());
				list.Dispose();
				enumerator.Dispose();
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

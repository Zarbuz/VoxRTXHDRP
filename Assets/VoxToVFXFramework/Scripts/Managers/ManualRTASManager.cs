using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.VFX;
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

		#endregion

		#region UnityMethods

		protected override void OnStart()
		{
			mHdCamera = HDCamera.GetOrCreate(GetComponent<UnityEngine.Camera>());
			mRtas ??= new RayTracingAccelerationStructure();
		}

		private void OnDestroy()
		{
			mRtas?.Dispose();
		}

		private void Update()
		{
			if (Keyboard.current.oKey.wasPressedThisFrame)
			{
				ClearInstances();
			}
		}

		#endregion

		#region PublicMethods

		public void Build(NativeParallelMultiHashMap<int, Matrix4x4> chunks)
		{
			Debug.Log("[ManualRTASManager] Build");

			if (!RuntimeVoxManager.Instance.RenderWithPathTracing)
			{
				Debug.LogError("Can't render without pathtracing enabled!");
				return;
			}

			mRtas = new RayTracingAccelerationStructure();

			//RayTracingInstanceCullingConfig cullingConfig = new RayTracingInstanceCullingConfig();

			//cullingConfig.subMeshFlagsConfig.opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
			//cullingConfig.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Enabled;
			//cullingConfig.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Disabled;

			//RayTracingInstanceCullingTest cullingTest = new RayTracingInstanceCullingTest();
			//cullingTest.allowAlphaTestedMaterials = true;
			//cullingTest.allowOpaqueMaterials = true;
			//cullingTest.allowTransparentMaterials = false;
			//cullingTest.instanceMask = 255;
			//cullingTest.layerMask = -1;
			//cullingTest.shadowCastingModeMask = -1;

			//cullingConfig.instanceTests = new RayTracingInstanceCullingTest[1];
			//cullingConfig.instanceTests[0] = cullingTest;

			//mRtas.CullInstances(ref cullingConfig);

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
		}

		public void ClearInstances()
		{
			Debug.Log("[ManualRTASManager] ClearInstances");
			mRtas.ClearInstances();
			//mRtas.Build(transform.position);

			mHdCamera.rayTracingAccelerationStructure = null;
		}
		#endregion
	}
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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

		[SerializeField] private Mesh Mesh;

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

		#endregion

		#region PublicMethods

		public void Build(Dictionary<int, List<Matrix4x4>> chunks)
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

			foreach (KeyValuePair<int, List<Matrix4x4>> pair in chunks)
			{
				int index = 0;
				int totalTaken = 0;
				do
				{
					RayTracingMeshInstanceConfig config = new RayTracingMeshInstanceConfig(Mesh, 0, RuntimeVoxManager.Instance.Materials[pair.Key]);
					List<Matrix4x4> list = pair.Value.Skip(index * MAX_INSTANCES_PER_CONFIG).Take(MAX_INSTANCES_PER_CONFIG).ToList();
					totalTaken += list.Count;
					mRtas.AddInstances(config, list);
					index++;
				} while (totalTaken != pair.Value.Count);
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

using FileToVoxCore.Utils;
using FileToVoxCore.Vox;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.VFX;
using VoxToVFXFramework.Scripts.Core;
using VoxToVFXFramework.Scripts.Data;
using VoxToVFXFramework.Scripts.Extensions;
using VoxToVFXFramework.Scripts.Importer;
using VoxToVFXFramework.Scripts.Jobs;
using VoxToVFXFramework.Scripts.ScriptableObjects;
using VoxToVFXFramework.Scripts.Singleton;
using VoxToVFXFramework.Scripts.UI;
using Plane = UnityEngine.Plane;

namespace VoxToVFXFramework.Scripts.Managers
{
	public class RuntimeVoxManager : ModuleSingleton<RuntimeVoxManager>
	{
		#region SerializeFields

		[SerializeField] private VisualEffect VisualEffectItemPrefab;
		[SerializeField] private VFXListAsset VFXListAsset;
		[SerializeField] private bool ShowOnlyActiveChunkGizmos;
		[SerializeField] private Transform FreeCameraTransform;
		[SerializeField] private Material TransparentMaterial;
		[SerializeField] private Material OpaqueMaterial;

		#endregion

		#region ConstStatic

		public const int STEP_CAPACITY = 100000;
		public const int MAX_CAPACITY_VFX = 5000000;

		private const float MIN_DIFF_ANGLE_CAMERA = 1f;
		private const float MIN_TIMER_CHECK_CAMERA = 0.1f;

		private const string VFX_BUFFER_KEY = "Buffer";

		private const string MATERIAL_VFX_BUFFER_KEY = "MaterialBuffer";
		private const string CHUNK_VFX_BUFFER_KEY = "ChunkBuffer";
		private const string DEBUG_LOD_KEY = "DebugLod";
		private const string EXPOSURE_WEIGHT_KEY = "ExposureWeight";
		private const string INITIAL_BURST_COUNT_KEY = "InitialBurstCount";

		private static readonly int mMetallic = Shader.PropertyToID("_Metallic");
		private static readonly int mSmoothness = Shader.PropertyToID("_Smoothness");
		private static readonly int mEmissiveExposureWeight = Shader.PropertyToID("_EmissiveExposureWeight");

		#endregion

		#region Fields

		public event Action LoadFinishedCallback;
		public event Action UnloadFinishedCallback;

		public Material[] Materials { get; private set; }
		public bool RenderWithPathTracing { get; set; }
		public Vector2 MinMaxX { get; set; }
		public Vector2 MinMaxY { get; set; }
		public Vector2 MinMaxZ { get; set; }
		public bool IsReady { get; private set; }

		public Wrapped<bool> DebugLod = new Wrapped<bool>(false);
		//public Wrapped<float> ExposureWeight = new Wrapped<float>(-15);
		public Wrapped<float> ColliderDistance = new Wrapped<float>(5);
		public Wrapped<int> LodDistanceLod0 = new Wrapped<int>(300);
		public Wrapped<int> LodDistanceLod1 = new Wrapped<int>(600);
		public Wrapped<int> ExposureWeight = new Wrapped<int>(-15);

		[HideInInspector] public NativeArray<ChunkVFX> Chunks;

		private Transform PlayerPosition => FreeCameraTransform;
		private UnsafeParallelHashMap<int, UnsafeList<VoxelVFX>> mChunksLoaded;
		private VisualEffect mVisualEffect;
		private GraphicsBuffer mPaletteBuffer;
		private GraphicsBuffer mGraphicsBuffer;

		private GraphicsBuffer mChunkBuffer;

		private Plane[] mPlanes;

		private int mPreviousPlayerChunkIndex;
		private int mCurrentChunkWorldIndex;
		private UnityEngine.Camera mCamera;
		private Quaternion mPreviousRotation;
		private float mPreviousCheckTimer;
		private bool mIsRenderLocked;

		#endregion

		#region UnityMethods

		protected override void OnStart()
		{
			mCamera = UnityEngine.Camera.main;

			AssemblyFileVersionAttribute runtimeVersion = typeof(VoxModel)
				.GetTypeInfo()
				.Assembly
				.GetCustomAttribute<AssemblyFileVersionAttribute>();

			Debug.Log("FileToVoxCore version: " + runtimeVersion.Version);


			ExposureWeight.OnValueChanged += RefreshExposureWeight;
			DebugLod.OnValueChanged += RefreshDebugLod;

			LodDistanceLod0.OnValueChanged += RefreshChunksToRender;
			LodDistanceLod1.OnValueChanged += RefreshChunksToRender;
		}

		private void OnDestroy()
		{
			ExposureWeight.OnValueChanged -= RefreshExposureWeight;
			DebugLod.OnValueChanged -= RefreshDebugLod;
			LodDistanceLod0.OnValueChanged -= RefreshChunksToRender;
			LodDistanceLod1.OnValueChanged -= RefreshChunksToRender;

			Release();
		}

		private void Update()
		{
			if (!IsReady || CanvasPlayerPCManager.Instance.CanvasPlayerPcState != CanvasPlayerPCState.Closed)
			{
				return;
			}

			float angle = Quaternion.Angle(mCamera.transform.rotation, mPreviousRotation);
			bool isAnotherChunk = mPreviousPlayerChunkIndex != mCurrentChunkWorldIndex;

			mCurrentChunkWorldIndex = GetPlayerCurrentChunkIndex(PlayerPosition.position);
			mPreviousCheckTimer += Time.unscaledDeltaTime;

			if (Keyboard.current.rKey.wasPressedThisFrame && !RenderWithPathTracing)
			{
				RenderWithPathTracing = true;
				SaveUpdateVars(isAnotherChunk);
				RefreshChunksToRender();
				return;
			}

			if (isAnotherChunk || angle > MIN_DIFF_ANGLE_CAMERA && mPreviousCheckTimer >= MIN_TIMER_CHECK_CAMERA)
			{
				if (RenderWithPathTracing)
				{
					StartCoroutine(DisablePathTracingCo());
				}
				else
				{
					if (!mIsRenderLocked)
					{
						SaveUpdateVars(isAnotherChunk);
						RefreshChunksToRender();
					}
				}
			}
		}

		private IEnumerator DisablePathTracingCo()
		{
			Debug.Log("[RuntimeVoxManager] DisablePathTracingCo...");
			ManualRTASManager.Instance.ClearInstances();
			PostProcessingManager.Instance.SetPathTracingActive(false);
			RenderWithPathTracing = false;
			mIsRenderLocked = true;
			yield return new WaitForSeconds(0.1f);
			mIsRenderLocked = false;
		}

		private void SaveUpdateVars(bool isAnotherChunk)
		{
			mPreviousCheckTimer = 0;
			if (isAnotherChunk)
			{
				mPreviousPlayerChunkIndex = mCurrentChunkWorldIndex;
			}
			else
			{
				mPreviousRotation = mCamera.transform.rotation;
			}
		}

		private void OnDrawGizmos()
		{
			if (!IsReady)
			{
				return;
			}

			Vector3 position = PlayerPosition.position;
			if (ShowOnlyActiveChunkGizmos)
			{
				Gizmos.color = Color.green;

				foreach (var item in Chunks.Where(t => t.IsActive == 1).GroupBy(t => t.ChunkIndex, t => t.CenterWorldPosition, (key, g) => new { ChunkIndex = key, Position = g.First() }))
				{
					Gizmos.DrawWireCube(item.Position, Vector3.one * WorldData.CHUNK_SIZE);
				}
			}
			else
			{
				Gizmos.color = Color.white;
				foreach (var item in Chunks.GroupBy(t => t.ChunkIndex, t => t.CenterWorldPosition, (key, g) => new { ChunkIndex = key, Position = g.First() }))
				{
					Gizmos.DrawWireCube(item.Position, Vector3.one * WorldData.CHUNK_SIZE);
				}
			}

			Gizmos.color = Color.green;
			Gizmos.DrawWireSphere(position, LodDistanceLod0.Value);

			Gizmos.color = Color.yellow;
			Gizmos.DrawWireSphere(position, LodDistanceLod1.Value);

			Gizmos.color = Color.blue;
			Gizmos.DrawWireSphere(position, ColliderDistance.Value);

			//Gizmos.color = Color.red;
			//Gizmos.DrawWireSphere(position, LodDistance.w);
		}

		#endregion

		#region PublicMethods

		public void Release()
		{
			IsReady = false;
			mGraphicsBuffer?.Release();
			mPaletteBuffer?.Release();
			mChunkBuffer?.Release();
			//mRotationBuffer?.Release();
			mPaletteBuffer = null;
			mGraphicsBuffer = null;
			mChunkBuffer = null;
			//mRotationBuffer = null;
			if (mVisualEffect != null)
			{
				Destroy(mVisualEffect.gameObject);
			}

			if (Chunks.IsCreated)
			{
				Chunks.Dispose();
			}


			if (mChunksLoaded.IsCreated)
			{
				//TODO Check this dispose
				foreach (KeyValue<int, UnsafeList<VoxelVFX>> item in mChunksLoaded)
				{
					item.Value.Dispose();
				}
				mChunksLoaded.Dispose();
			}

			UnloadFinishedCallback?.Invoke();
		}

		public void SetMaterials(VoxelMaterialVFX[] materials)
		{
			mPaletteBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, materials.Length, Marshal.SizeOf(typeof(VoxelMaterialVFX)));
			mPaletteBuffer.SetData(materials);

			Materials = new Material[materials.Length];
			for (int i = 0; i < materials.Length; i++)
			{
				VoxelMaterialVFX mat = materials[i];
				if (mat.alpha == 0)
				{
					Materials[i] = new Material(OpaqueMaterial);
				}
				else
				{
					Materials[i] = new Material(TransparentMaterial);
				}

				Materials[i].color = new Color(mat.color.r, mat.color.g, mat.color.b, mat.alpha);
				Materials[i].SetFloat(mMetallic, mat.metallic);
				Materials[i].SetFloat(mSmoothness, mat.smoothness);
				Materials[i].SetFloat(mEmissiveExposureWeight, 0);
				if (mat.emissionPower > 0)
				{
					HDMaterial.SetUseEmissiveIntensity(Materials[i], mat.emissionPower > 0);
					HDMaterial.SetEmissiveColor(Materials[i], mat.color);
					HDMaterial.SetEmissiveIntensity(Materials[i], mat.emissionPower * 10, EmissiveIntensityUnit.Nits);
					HDMaterial.ValidateMaterial(Materials[i]);
				}

			}
		}

		public void SetVoxelChunk(int chunkIndex, UnsafeList<VoxelVFX> list)
		{
			if (!mChunksLoaded.IsCreated)
			{
				mChunksLoaded = new UnsafeParallelHashMap<int, UnsafeList<VoxelVFX>>(Chunks.Length, Allocator.Persistent);
			}

			mChunksLoaded[chunkIndex] = list;
		}

		public void OnChunkLoadedFinished()
		{
			mVisualEffect = Instantiate(VisualEffectItemPrefab);

			mVisualEffect.enabled = true;
			SetPlayerToWorldCenter();
			Debug.Log("[RuntimeVoxController] OnChunkLoadedFinished");
			IsReady = true;
			LoadFinishedCallback?.Invoke();
		}

		public void SetChunks(NativeArray<ChunkVFX> chunks)
		{
			Chunks = chunks;

			mChunkBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, chunks.Length, Marshal.SizeOf(typeof(ChunkVFX)));
			mChunkBuffer.SetData(chunks);
		}

		public void RefreshChunksToRender()
		{
			if (!Chunks.IsCreated)
				return;

			mPlanes = GeometryUtility.CalculateFrustumPlanes(mCamera);

			NativeList<int> chunkIndex = new NativeList<int>(Allocator.TempJob);
			NativeList<ChunkVFX> activeChunks = new NativeList<ChunkVFX>(Allocator.TempJob);
			for (int index = 0; index < Chunks.Length; index++)
			{
				ChunkVFX chunkVFX = Chunks[index];

				chunkVFX.IsActive = GeometryUtility.TestPlanesAABB(mPlanes, new Bounds(Chunks[index].CenterWorldPosition, Vector3.one * WorldData.CHUNK_SIZE)) ? 1 : 0;
				Chunks[index] = chunkVFX;
				if (chunkVFX.IsActive == 1)
				{
					activeChunks.Add(chunkVFX);
					chunkIndex.Add(index);
				}
			}
			int totalActive = Chunks.Count(chunk => chunk.IsActive == 1);
			int totalLength = Chunks.Where(chunk => chunk.IsActive == 1).Sum(chunk => chunk.Length);
			NativeList<VoxelVFX> buffer = new NativeList<VoxelVFX>(totalLength, Allocator.TempJob);
			JobHandle computeRenderingChunkJob = new ComputeRenderingChunkJob()
			{
				LodDistanceLod0 = LodDistanceLod0.Value,
				LodDistanceLod1 = LodDistanceLod1.Value,
				PlayerPosition = PlayerPosition.position,
				Data = mChunksLoaded,
				Chunks = activeChunks,
				Buffer = buffer.AsParallelWriter(),
				ChunkIndex = chunkIndex,
			}.Schedule(totalActive, 64);
			computeRenderingChunkJob.Complete();
			activeChunks.Dispose();
			chunkIndex.Dispose();

			if (buffer.Length > 0)
			{
				RefreshRender(buffer);
			}

			buffer.Dispose();
		}

		public void SetPlayerToWorldCenter()
		{
			FreeCameraTransform.transform.position = new Vector3((MinMaxX.y + MinMaxX.x) / 2, (MinMaxY.y + MinMaxY.x) / 2, (MinMaxZ.y + MinMaxZ.x) / 2);
		}
		#endregion

		#region PrivateMethods


		private void RefreshDebugLod()
		{
			if (!IsReady)
			{
				return;
			}
			mVisualEffect.Reinit();
			mVisualEffect.SetBool(DEBUG_LOD_KEY, DebugLod.Value);
			mVisualEffect.Play();
		}

		private void RefreshExposureWeight()
		{
			if (!IsReady)
			{
				return;
			}
			mVisualEffect.Reinit();
			mVisualEffect.SetFloat(EXPOSURE_WEIGHT_KEY, ExposureWeight.Value);
			mVisualEffect.Play();
		}

		private void RefreshRender(NativeList<VoxelVFX> voxels)
		{
			if (RenderWithPathTracing)
			{
				PostProcessingManager.Instance.SetPathTracingActive(true);

				Destroy(mVisualEffect.gameObject);
				mVisualEffect = null;
				mGraphicsBuffer?.Release();

				Dictionary<int, List<Matrix4x4>> chunks = new Dictionary<int, List<Matrix4x4>>();
				foreach (VoxelVFX voxel in voxels)
				{
					float4 decodedPosition = voxel.DecodePosition();
					int colorIndex = (int)decodedPosition.w;
					if (!chunks.ContainsKey(colorIndex))
					{
						chunks.Add(colorIndex, new List<Matrix4x4>());
					}
					uint voxelChunkIndex = voxel.DecodeChunkIndex();
					ChunkVFX chunk = Chunks[(int)voxelChunkIndex];

					Vector3 worldPosition = chunk.WorldPosition + new Vector3(decodedPosition.x, decodedPosition.y, decodedPosition.z);
					Matrix4x4 matrix = new Matrix4x4();
					matrix.SetTRS(worldPosition, Quaternion.identity, Vector3.one * chunk.LodLevel);
					chunks[colorIndex].Add(matrix);
				}

				ManualRTASManager.Instance.Build(chunks);
			}
			else
			{
				if (mVisualEffect == null)
				{
					mVisualEffect = Instantiate(VisualEffectItemPrefab);
					mVisualEffect.enabled = true;
				}
				mVisualEffect.visualEffectAsset = GetVisualEffectAsset(voxels.Length);
				mVisualEffect.Reinit();

				mGraphicsBuffer?.Release();
				mGraphicsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels.Length, Marshal.SizeOf(typeof(VoxelVFX)));
				mGraphicsBuffer.SetData(voxels.AsArray());

				mVisualEffect.SetInt(INITIAL_BURST_COUNT_KEY, voxels.Length);
				mVisualEffect.SetGraphicsBuffer(VFX_BUFFER_KEY, mGraphicsBuffer);
				mVisualEffect.SetGraphicsBuffer(MATERIAL_VFX_BUFFER_KEY, mPaletteBuffer);
				mVisualEffect.SetGraphicsBuffer(CHUNK_VFX_BUFFER_KEY, mChunkBuffer);
				mVisualEffect.SetFloat(EXPOSURE_WEIGHT_KEY, ExposureWeight.Value);
				mVisualEffect.SetBool(DEBUG_LOD_KEY, DebugLod.Value);

				mVisualEffect.Play();
			}

		}

		private VisualEffectAsset GetVisualEffectAsset(int voxelCount)
		{
			int index = voxelCount / STEP_CAPACITY;
			if (index > VFXListAsset.VisualEffectAssets.Count)
			{
				index = VFXListAsset.VisualEffectAssets.Count - 1;
			}

			VisualEffectAsset asset = VFXListAsset.VisualEffectAssets[index];
			//Debug.Log("[RuntimeVoxManager] Selected VisualEffectAsset: " + asset.name);
			return asset;
		}

		private int GetPlayerCurrentChunkIndex(Vector3 position)
		{
			FastMath.FloorToInt(position.x / WorldData.CHUNK_SIZE, position.y / WorldData.CHUNK_SIZE, position.z / WorldData.CHUNK_SIZE, out int chunkX, out int chunkY, out int chunkZ);
			int chunkIndex = VoxImporter.GetGridPos(chunkX, chunkY, chunkZ, WorldData.RelativeWorldVolume);
			return chunkIndex;
		}

		#endregion
	}
}


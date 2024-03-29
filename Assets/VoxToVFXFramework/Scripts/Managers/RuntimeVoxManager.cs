﻿using FileToVoxCore.Schematics;
using FileToVoxCore.Utils;
using FileToVoxCore.Vox;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.VFX;
using VoxToVFXFramework.Scripts.Core;
using VoxToVFXFramework.Scripts.Data;
using VoxToVFXFramework.Scripts.Importer;
using VoxToVFXFramework.Scripts.Jobs;
using VoxToVFXFramework.Scripts.Singleton;
using VoxToVFXFramework.Scripts.UI;
using Plane = UnityEngine.Plane;

namespace VoxToVFXFramework.Scripts.Managers
{
	public class RuntimeVoxManager : ModuleSingleton<RuntimeVoxManager>
	{
		#region SerializeFields

		[SerializeField] private VisualEffect VisualEffectItemPrefab;
		//[SerializeField] private VFXListAsset VFXListAsset;
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
		private static readonly int mIor = Shader.PropertyToID("_Ior");
		private static readonly int mTransmittanceColor = Shader.PropertyToID("_TransmittanceColor");

		#endregion

		#region Fields

		public event Action LoadFinishedCallback;
		public event Action UnloadFinishedCallback;

		public Material[] Materials;

		public bool RenderWithPathTracing { get; set; }
		public bool ForceRefreshRender { get; set; }
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

		public NativeArray<ChunkVFX> Chunks;
		public UnsafeParallelHashMap<int, UnsafeList<VoxelVFX>> LoadedData;

		private Transform PlayerPosition => FreeCameraTransform;
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
		private bool mIsRenderFinished;

		#endregion

		#region UnityMethods

		protected override void OnStart()
		{
			mCamera = UnityEngine.Camera.main;
			Physics.simulationMode = SimulationMode.Script;
			AssemblyFileVersionAttribute runtimeVersion = typeof(VoxModel)
				.GetTypeInfo()
				.Assembly
				.GetCustomAttribute<AssemblyFileVersionAttribute>();

			Debug.Log("FileToVoxCore version: " + runtimeVersion.Version);
			QualityManager.Instance.Initialize();
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

			if (Keyboard.current.rKey.wasPressedThisFrame && !RenderWithPathTracing && mIsRenderFinished)
			{
				RenderWithPathTracing = true;
				RefreshChunksToRender();
				return;
			}

			//if (RenderWithPathTracing)
			//{
			//	return;
			//}

			if (isAnotherChunk || ForceRefreshRender|| angle > MIN_DIFF_ANGLE_CAMERA && mPreviousCheckTimer >= MIN_TIMER_CHECK_CAMERA)
			{
				ForceRefreshRender = false;
				SaveUpdateVars(isAnotherChunk);
				RefreshChunksToRender();
			}
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

			if (LoadedData.IsCreated)
			{
				//TODO Check this dispose
				foreach (KeyValue<int, UnsafeList<VoxelVFX>> item in LoadedData)
				{
					item.Value.Dispose();
				}
				LoadedData.Dispose();
			}

			if (Materials != null)
			{
				foreach (Material mat in Materials)
				{
					Destroy(mat);
				}
			}

			Materials = null;
			if (ManualRTASManager.Instance != null)
			{
				ManualRTASManager.Instance.DisablePathTracing();
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
				if (mat.alpha == 1)
				{
					Materials[i] = new Material(OpaqueMaterial);
					Materials[i].color = new Color(mat.color.r, mat.color.g, mat.color.b);
				}
				else
				{
					Materials[i] = new Material(TransparentMaterial);
					Materials[i].color = new Color(0, 0, 0, mat.alpha);
					//ior values from MV are between 0 and 2, Unity expect value between 1 and 2.5
					float ior = mat.ior * 1.5f / 2f + 1;
					Materials[i].SetFloat(mIor, ior);
					Materials[i].SetColor(mTransmittanceColor, mat.color);
				}

				Materials[i].name = "mat-" + i;
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
			if (!LoadedData.IsCreated)
			{
				LoadedData = new UnsafeParallelHashMap<int, UnsafeList<VoxelVFX>>(Chunks.Length, Allocator.Persistent);
			}

			LoadedData[chunkIndex] = list;
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

			mIsRenderFinished = false;
			mPlanes = GeometryUtility.CalculateFrustumPlanes(mCamera);

			NativeList<int> chunkIndex = new NativeList<int>(Chunks.Length, Allocator.TempJob);
			int renderDistance = QualityManager.Instance.RenderDistance;
			NativeArray<Plane> planes = new NativeArray<Plane>(mPlanes, Allocator.TempJob);
			JobHandle computeVisibleChunkJob = new ComputeVisibleChunkJob()
			{
				Chunks = Chunks,
				PlayerPosition = PlayerPosition.position,
				ChunkIndex = chunkIndex.AsParallelWriter(),
				RenderDistance = renderDistance,
				LodDistanceLod0 = LodDistanceLod0.Value,
				LodDistanceLod1 = LodDistanceLod1.Value,
				Planes = planes
			}.Schedule(Chunks.Length, 32);
			computeVisibleChunkJob.Complete();
			planes.Dispose();

			if (RenderWithPathTracing)
			{
				if (mVisualEffect != null)
				{
					Destroy(mVisualEffect.gameObject);
					mVisualEffect = null;
				}

				mGraphicsBuffer?.Release();
				PostProcessingManager.Instance.SetPathTracing(true);
				ManualRTASManager.Instance.Build();
			}
			else
			{
				int totalActive = Chunks.Count(chunk => chunk.IsActive == 1);
				int totalLength = Chunks.Where(chunk => chunk.IsActive == 1).Sum(chunk => chunk.Length);
				NativeList<VoxelVFX> buffer = new NativeList<VoxelVFX>(totalLength, Allocator.TempJob);

				JobHandle computeRenderingChunkJob = new ComputeRenderingChunkJob()
				{
					Data = LoadedData,
					Buffer = buffer.AsParallelWriter(),
					ChunkIndex = chunkIndex,
				}.Schedule(totalActive, 64);
				computeRenderingChunkJob.Complete();

				if (buffer.Length > 0)
				{
					RefreshVFXRender(buffer);
				}

				buffer.Dispose();
			}
			chunkIndex.Dispose();
			mIsRenderFinished = true;
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

		private void RefreshVFXRender(NativeList<VoxelVFX> voxels)
		{
			if (mVisualEffect == null)
			{
				mVisualEffect = Instantiate(VisualEffectItemPrefab);
				mVisualEffect.enabled = true;
			}
			//mVisualEffect.visualEffectAsset = GetVisualEffectAsset(voxels.Length);
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

		//private VisualEffectAsset GetVisualEffectAsset(int voxelCount)
		//{
		//	int index = voxelCount / STEP_CAPACITY;
		//	if (index >= VFXListAsset.VisualEffectAssets.Count)
		//	{
		//		index = VFXListAsset.VisualEffectAssets.Count - 1;
		//		Debug.LogWarningFormat("[RuntimeVoxManager] index {0} is greater than the max visual effect assets count: {1}", index, VFXListAsset.VisualEffectAssets.Count);
		//	}

		//	VisualEffectAsset asset = VFXListAsset.VisualEffectAssets[index];
		//	//Debug.Log("[RuntimeVoxManager] Selected VisualEffectAsset: " + asset.name);
		//	return asset;
		//}

		private int GetPlayerCurrentChunkIndex(Vector3 position)
		{
			FastMath.FloorToInt(position.x / WorldData.CHUNK_SIZE, position.y / WorldData.CHUNK_SIZE, position.z / WorldData.CHUNK_SIZE, out int chunkX, out int chunkY, out int chunkZ);
			int chunkIndex = VoxImporter.GetGridPos(chunkX, chunkY, chunkZ, WorldData.RelativeWorldVolume);
			return chunkIndex;
		}

		#endregion
	}
}


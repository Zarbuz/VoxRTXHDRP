﻿using FileToVoxCore.Utils;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.VFX;
using VoxToVFXFramework.Scripts.Core;
using VoxToVFXFramework.Scripts.Data;
using VoxToVFXFramework.Scripts.Importer;
using VoxToVFXFramework.Scripts.Jobs;
using VoxToVFXFramework.Scripts.Singleton;
using Plane = UnityEngine.Plane;

namespace VoxToVFXFramework.Scripts.Managers
{
	public class RuntimeVoxManager : ModuleSingleton<RuntimeVoxManager>
	{
		#region SerializeFields

		[SerializeField] private VisualEffect VisualEffectItemPrefab;
		[SerializeField] private bool ShowOnlyActiveChunkGizmos;

		#endregion

		#region ConstStatic

		private const string VFX_BUFFER_KEY = "Buffer";

		private const string MATERIAL_VFX_BUFFER_KEY = "MaterialBuffer";
		private const string CHUNK_VFX_BUFFER_KEY = "ChunkBuffer";
		private const string DEBUG_LOD_KEY = "DebugLod";
		private const string EXPOSURE_WEIGHT_KEY = "ExposureWeight";
		private const string INITIAL_BURST_COUNT_KEY = "InitialBurstCount";

		private const float MIN_DIFF_ANGLE_CAMERA = 10f;
		private const float MIN_TIMER_CHECK_CAMERA = 0.4f;

		#endregion

		#region Fields

		public event Action LoadFinishedCallback;
		public Vector2 MinMaxX { get; set; }
		public Vector2 MinMaxY { get; set; }
		public Vector2 MinMaxZ { get; set; }

		[HideInInspector] public NativeArray<ChunkVFX> Chunks;

		private UnsafeHashMap<int, UnsafeList<VoxelVFX>> mChunksLoaded;
		//private UnsafeHashMap<int, UnsafeList<VoxelVFX>> mChunksLoaded;
		private VisualEffect mVisualEffect;
		private GraphicsBuffer mPaletteBuffer;
		private GraphicsBuffer mGraphicsBuffer;

		private GraphicsBuffer mChunkBuffer;

		//private GraphicsBuffer mRotationBuffer;
		private Plane[] mPlanes;
		private Light mDirectionalLight;
		private HDAdditionalLightData mAdditionalLightData;

		//private Entity mEntityPrefab;
		//private PhysicsShapeQuerySystem mPhysicsShapeQuerySystem;
		//private EndSimulationEntityCommandBufferSystem mEndSimulationEntityCommandBufferSystem;
		//private EntityManager mEntityManager;
		//private EntityArchetype mEntityArchetype;

		private bool mIsLoaded;
		private Transform mVisualItemsParent;

		public Wrapped<bool> DebugLod = new Wrapped<bool>(false);

		public Wrapped<int> ForcedLevelLod = new Wrapped<int>(-1);

		public Wrapped<Vector3> LodDistance = new Wrapped<Vector3>(new Vector3(0, 300, 600));

		public Wrapped<float> ExposureWeight = new Wrapped<float>(0);

		//private int mMaxDistanceColliders = 5;

		//public int MaxDistanceColliders
		//{
		//	get => mMaxDistanceColliders;
		//	set
		//	{
		//		if (mMaxDistanceColliders != value)
		//		{
		//			mMaxDistanceColliders = value;
		//			RefreshChunksColliders();
		//		}
		//	}
		//}

		private int mPreviousPlayerChunkIndex;
		private int mCurrentChunkWorldIndex;
		private int mCurrentChunkIndex;
		private UnityEngine.Camera mCamera;
		private Quaternion mPreviousRotation;
		private Vector3 mPreviousPosition;
		private float mPreviousCheckTimer;
		#endregion

		#region UnityMethods

		protected override void OnStart()
		{
			mCamera = UnityEngine.Camera.main;
			mVisualItemsParent = new GameObject("VisualItemsParent").transform;
			mDirectionalLight = FindObjectOfType<Light>();
			mAdditionalLightData = mDirectionalLight.GetComponent<HDAdditionalLightData>();


			ExposureWeight.OnValueChanged += RefreshExposureWeight;
			DebugLod.OnValueChanged += RefreshDebugLod;
			LodDistance.OnValueChanged += RefreshChunksToRender;
			ForcedLevelLod.OnValueChanged += RefreshChunksToRender;
		}

		private void OnDestroy()
		{

			ExposureWeight.OnValueChanged -= RefreshExposureWeight;
			DebugLod.OnValueChanged -= RefreshDebugLod;
			LodDistance.OnValueChanged -= RefreshChunksToRender;
			ForcedLevelLod.OnValueChanged -= RefreshChunksToRender;

			Release();
		}

		private void Update()
		{
			if (!mIsLoaded)
			{
				return;
			}

			mPlanes = GeometryUtility.CalculateFrustumPlanes(mCamera);
			mCurrentChunkWorldIndex = GetPlayerCurrentChunkIndex(mCamera.transform.position);
			float angle = Quaternion.Angle(mCamera.transform.rotation, mPreviousRotation);
			mPreviousCheckTimer += Time.unscaledDeltaTime;
			bool isAnotherChunk = mPreviousPlayerChunkIndex != mCurrentChunkWorldIndex;
			if (isAnotherChunk || angle > MIN_DIFF_ANGLE_CAMERA && mPreviousCheckTimer >= MIN_TIMER_CHECK_CAMERA)
			{
				mPreviousCheckTimer = 0;
				if (isAnotherChunk)
				{
					mPreviousPlayerChunkIndex = mCurrentChunkWorldIndex;
				}
				mPreviousRotation = mCamera.transform.rotation;
				RefreshChunksToRender();
			}

			if (Vector3.Distance(mCamera.transform.position, mPreviousPosition) > 1.5f || isAnotherChunk)
			{
				mPreviousPosition = mCamera.transform.position;
				RefreshChunksColliders();
			}
		}

		private void OnDrawGizmosSelected()
		{
			if (!Application.isPlaying)
			{
				return;
			}

			Vector3 position = mCamera.transform.position;
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
			Gizmos.DrawWireSphere(position, LodDistance.Value.y);

			Gizmos.color = Color.yellow;
			Gizmos.DrawWireSphere(position, LodDistance.Value.z);

			//Gizmos.color = Color.red;
			//Gizmos.DrawWireSphere(position, LodDistance.w);
		}

		#endregion

		#region PublicMethods

		public void Release()
		{
			mIsLoaded = false;
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
		}

		//public void InitEntities()
		//{
		//	mEntityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
		//	mEntityArchetype = mEntityManager.CreateArchetype(
		//		typeof(Translation),
		//		typeof(PhysicsCollider),
		//		typeof(LocalToWorld),
		//		typeof(PhysicsWorldIndex),
		//		typeof(VoxelPrefabTag));

		//	mPhysicsShapeQuerySystem = World.DefaultGameObjectInjectionWorld.GetExistingSystem<PhysicsShapeQuerySystem>();
		//	mEndSimulationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
		//}

		public void SetMaterials(VoxelMaterialVFX[] materials)
		{
			mPaletteBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, materials.Length, Marshal.SizeOf(typeof(VoxelMaterialVFX)));
			mPaletteBuffer.SetData(materials);
		}

		public void SetVoxelChunk(int chunkIndex, UnsafeList<VoxelVFX> list)
		{
			if (!mChunksLoaded.IsCreated)
			{
				mChunksLoaded = new UnsafeHashMap<int, UnsafeList<VoxelVFX>>(Chunks.Length, Allocator.Persistent);
			}
			mChunksLoaded[chunkIndex] = list;
		}

		public void OnChunkLoadedFinished()
		{
			//InitEntities();
			mVisualEffect = Instantiate(VisualEffectItemPrefab, mVisualItemsParent, false);
			mVisualEffect.transform.SetParent(mVisualItemsParent);
			mVisualEffect.SetGraphicsBuffer(MATERIAL_VFX_BUFFER_KEY, mPaletteBuffer);
			mVisualEffect.SetGraphicsBuffer(CHUNK_VFX_BUFFER_KEY, mChunkBuffer);
			//mVisualEffectItem.OpaqueVisualEffect.SetGraphicsBuffer(ROTATION_VFX_BUFFER_KEY, mRotationBuffer);
			mVisualEffect.enabled = true;
			SetCameraToWorldCenter();
			Debug.Log("[RuntimeVoxController] OnChunkLoadedFinished");
			mIsLoaded = true;
			LoadFinishedCallback?.Invoke();
		}

		public void SetChunks(NativeArray<ChunkVFX> chunks)
		{
			Chunks = chunks;

			mChunkBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, chunks.Length, Marshal.SizeOf(typeof(ChunkVFX)));
			mChunkBuffer.SetData(chunks);
		}

		#endregion

		#region PrivateMethods

		private void SetCameraToWorldCenter()
		{
			mCamera.transform.position = new Vector3((MinMaxX.y + MinMaxX.x) / 2, (MinMaxY.y + MinMaxY.x) / 2 + 1, (MinMaxZ.y + MinMaxZ.x) / 2);
		}

		private void RefreshDebugLod()
		{
			if (!mIsLoaded)
			{
				return;
			}
			mVisualEffect.Reinit();
			mVisualEffect.SetBool(DEBUG_LOD_KEY, DebugLod.Value);
			mVisualEffect.Play();
		}

		private void RefreshExposureWeight()
		{
			if (!mIsLoaded)
			{
				return;
			}
			mVisualEffect.Reinit();
			mVisualEffect.SetFloat(EXPOSURE_WEIGHT_KEY, ExposureWeight.Value);
			mVisualEffect.Play();
		}

		private void RefreshChunksToRender()
		{
			if (!Chunks.IsCreated)
				return;

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
				LodDistance = LodDistance.Value,
				ForcedLevelLod = ForcedLevelLod.Value,
				CameraPosition = mCamera.transform.position,
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

		private void RefreshChunksColliders()
		{
			if (!mIsLoaded)
			{
				return;
			}

			//int lodLevel = ForcedLevelLod switch
			//{
			//	0 => 1,
			//	1 => 2,
			//	2 => 4,
			//	_ => 1
			//};

			//for (int index = 0; index < Chunks.Length; index++)
			//{
			//	ChunkVFX chunkVFX = Chunks[index];
			//	if (chunkVFX.ChunkIndex == mCurrentChunkWorldIndex && chunkVFX.LodLevel == lodLevel)
			//	{
			//		mCurrentChunkIndex = index;
			//	}
			//}
			//ChunkVFX? currentChunk = Chunks.Where(chunk => chunk.ChunkIndex == mCurrentChunkWorldIndex && chunk.LodLevel == lodLevel).Cast<ChunkVFX?>().FirstOrDefault();
			//if (currentChunk == null)
			//{
			//	mEntityManager.DestroyEntity(mPhysicsShapeQuerySystem.EntityQuery);
			//	return;
			//}
			//EntityCommandBuffer ecb = mEndSimulationEntityCommandBufferSystem.CreateCommandBuffer();
			//UnsafeList<VoxelVFX> data = mChunksLoaded[mCurrentChunkIndex];

			//mEntityManager.DestroyEntity(mPhysicsShapeQuerySystem.EntityQuery);
			//mEntityPrefab = mEntityManager.CreateEntity(mEntityArchetype);

			//JobHandle createPhysicsEntityJob = new CreatePhysicsEntityJob()
			//{
			//	Chunk = currentChunk.Value,
			//	ECB = ecb.AsParallelWriter(),
			//	PrefabEntity = mEntityPrefab,
			//	Data = data,
			//	Collider = mPhysicsShapeQuerySystem.GetBlobAssetReference(currentChunk.Value.LodLevel),
			//	PlayerPosition = new float3(mCamera.transform.position.x, mCamera.transform.position.y, mCamera.transform.position.z),
			//	DistanceCheckVoxels = MaxDistanceColliders 
			//}.Schedule(data.Length, 64);

			//createPhysicsEntityJob.Complete();
		}

		private void RefreshRender(NativeList<VoxelVFX> voxels)
		{
			mVisualEffect.Reinit();

			//for (int index = 0; index < voxels.Length && index < 200; index++)
			//{
			//	VoxelVFX voxel = voxels[index];
			//	Debug.Log("pos: " + voxel.DecodePosition());
			//	Debug.Log("additional: " + voxel.DecodeAdditionalData());
			//}

			mGraphicsBuffer?.Release();
			mGraphicsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels.Length, Marshal.SizeOf(typeof(VoxelVFX)));
			mGraphicsBuffer.SetData(voxels.AsArray());

			mVisualEffect.SetInt(INITIAL_BURST_COUNT_KEY, voxels.Length);
			mVisualEffect.SetGraphicsBuffer(VFX_BUFFER_KEY, mGraphicsBuffer);
			mVisualEffect.Play();

			mAdditionalLightData.RequestShadowMapRendering();
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


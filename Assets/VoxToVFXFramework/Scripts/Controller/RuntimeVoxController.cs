﻿using FileToVoxCore.Utils;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.VFX;
using VoxToVFXFramework.Scripts.Data;
using VoxToVFXFramework.Scripts.Importer;

public class RuntimeVoxController : MonoBehaviour
{
	#region SerializeFields

	[Header("Visual Effects")]
	[SerializeField] private VisualEffect OpaqueVisualEffect;
	[SerializeField] private VisualEffect TransparenceVisualEffect;

	[Header("Camera Settings")]
	[SerializeField] private Transform MainCamera;
	[Range(2, 10)]
	[OnValueChanged(nameof(OnChunkLoadDistanceValueChanged))]
	[SerializeField] private int ChunkLoadDistance = 10;

	[Header("Debug Settings")]
	[SerializeField] private bool DebugVisualEffects;

	[Header("VisualEffectAssets")]
	[SerializeField] private List<VisualEffectAsset> OpaqueVisualEffects;
	[SerializeField] private List<VisualEffectAsset> TransparenceVisualEffects;
	#endregion

	#region ConstStatic

	private const int STEP_CAPACITY = 20000;
	private const int COUNT_ASSETS_TO_GENERATE = 500;

	private const string MAIN_VFX_BUFFER = "Buffer";
	private const string MATERIAL_VFX_BUFFER = "MaterialBuffer";
	private const string ROTATION_VFX_BUFFER = "RotationBuffer";
	private const string FRAMEWORK_FOLDER = "VoxToVFXFramework";

	#endregion

	#region Fields

	private GraphicsBuffer mOpaqueBuffer;
	private GraphicsBuffer mTransparencyBuffer;
	private GraphicsBuffer mPaletteBuffer;
	private GraphicsBuffer mRotationBuffer;

	private CustomSchematic mCustomSchematic;
	private BoxCollider[] mBoxColliders;
	private VoxelMaterialVFX[] mMaterials;

	private Vector3 mPreviousPosition;
	private long mPreviousChunkIndex;
	#endregion


	#region UnityMethods

	private void Start()
	{
		bool b = VerifyEffectAssetsList();
		if (!b && !DebugVisualEffects)
		{
			Debug.LogError("[RuntimeVoxController] EffectAssets count is different to COUNT_ASSETS_TO_GENERATE: " + OpaqueVisualEffects.Count + " expect: " + COUNT_ASSETS_TO_GENERATE);
			return;
		}
		//InitBoxColliders();
		VoxImporter voxImporter = new VoxImporter();
		StartCoroutine(voxImporter.LoadVoxModelAsync(Path.Combine(Application.streamingAssetsPath, "Sydney.vox"), OnLoadProgress, OnLoadFinished));
	}

	private void OnDestroy()
	{
		mOpaqueBuffer?.Release();
		mTransparencyBuffer?.Release();
		mPaletteBuffer?.Release();
		mRotationBuffer?.Release();

		mOpaqueBuffer = null;
		mTransparencyBuffer = null;
		mPaletteBuffer = null;
	}

	private void Update()
	{
		if (mCustomSchematic == null)
		{
			return;
		}

		if (mPreviousPosition != MainCamera.position)
		{
			mPreviousPosition = MainCamera.position;
			FastMath.FloorToInt(mPreviousPosition.x / CustomSchematic.CHUNK_SIZE, mPreviousPosition.y / CustomSchematic.CHUNK_SIZE, mPreviousPosition.z / CustomSchematic.CHUNK_SIZE, out int chunkX, out int chunkY, out int chunkZ);
			long chunkIndex = CustomSchematic.GetVoxelIndex(chunkX, chunkY, chunkZ);

			if (mPreviousChunkIndex != chunkIndex)
			{
				mPreviousChunkIndex = chunkIndex;
				//CreateBoxColliderForCurrentChunk(chunkIndex);
				LoadVoxelDataAroundCamera(chunkX, chunkY, chunkZ);
			}
		}
	}

	#endregion

	#region UnityEditor

#if UNITY_EDITOR

	[Button]
	private void GenerateAssets()
	{
		if (WriteAllVisualAssets(Path.Combine(Application.dataPath, FRAMEWORK_FOLDER, "VFX", "VoxImporterV2.vfx"), "Opaque", out List<VisualEffectAsset> l1))
		{
			OpaqueVisualEffects.Clear();
			OpaqueVisualEffects.AddRange(l1);
		}

		if (WriteAllVisualAssets(Path.Combine(Application.dataPath, FRAMEWORK_FOLDER, "VFX", "VoxImporterV2Transparency.vfx"), "Transparency", out List<VisualEffectAsset> l2))
		{
			TransparenceVisualEffects.Clear();
			TransparenceVisualEffects.AddRange(l2);
		}
	}

	private static bool WriteAllVisualAssets(string inputPath, string prefixName, out List<VisualEffectAsset> assets)
	{
		assets = new List<VisualEffectAsset>();
		if (!File.Exists(inputPath))
		{
			Debug.LogError("VFX asset file not found at: " + inputPath);
			return false;
		}

		int capacityLineIndex = 0;
		string[] lines = File.ReadAllLines(inputPath);
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			if (line.Contains("capacity:"))
			{
				capacityLineIndex = index;
				break;
			}
		}

		if (capacityLineIndex == 0)
		{
			Debug.LogError("Failed to found capacity line index in vfx asset! Abort duplicate");
			return false;
		}

		string pathOutput = Path.Combine(Application.dataPath, FRAMEWORK_FOLDER, "VFX", prefixName);
		if (!Directory.Exists(pathOutput))
		{
			Directory.CreateDirectory(pathOutput);
		}
		else
		{
			DirectoryInfo di = new DirectoryInfo(pathOutput);
			foreach (FileInfo file in di.GetFiles())
			{
				file.Delete();
			}
		}

		for (int i = 1; i <= RuntimeVoxController.COUNT_ASSETS_TO_GENERATE; i++)
		{
			uint newCapacity = (uint)(i * RuntimeVoxController.STEP_CAPACITY);
			lines[capacityLineIndex] = "  capacity: " + newCapacity;
			string targetFileName = prefixName + "VFX-" + newCapacity + ".vfx";
			File.WriteAllLines(Path.Combine(pathOutput, targetFileName), lines);

			string relativePath = "Assets/" + FRAMEWORK_FOLDER + "/VFX/" + prefixName + "/" + targetFileName;
			UnityEditor.AssetDatabase.ImportAsset(relativePath);
			VisualEffectAsset visualEffectAsset = (VisualEffectAsset)UnityEditor.AssetDatabase.LoadAssetAtPath(relativePath, typeof(VisualEffectAsset));
			assets.Add(visualEffectAsset);
		}

		return true;
	}

#endif

	#endregion

	#region PrivateMethods

	private bool VerifyEffectAssetsList()
	{
		return OpaqueVisualEffects.Count == COUNT_ASSETS_TO_GENERATE;
	}

	//private void InitBoxColliders()
	//{
	//	mBoxColliders = new BoxCollider[1000];
	//	GameObject boxColliderParent = new GameObject("BoxColliders");
	//	for (int i = 0; i < 1000; i++)
	//	{
	//		GameObject go = new GameObject("BoxCollider " + i);
	//		go.transform.SetParent(boxColliderParent.transform);
	//		BoxCollider boxCollider = go.AddComponent<BoxCollider>();
	//		mBoxColliders[i] = boxCollider;
	//	}
	//}

	private void OnLoadProgress(float progress)
	{
		Debug.Log("[RuntimeVoxController] Load progress: " + progress);
	}

	private void OnLoadFinished(VoxelDataVFX voxelData)
	{
		List<VoxelVFX> voxels = voxelData.CustomSchematic.GetAllVoxels();
		int targetPositionX = voxelData.CustomSchematic.Width / 2;
		int targetPositionY = voxelData.CustomSchematic.Height / 2;
		int targetPositionZ = voxelData.CustomSchematic.Length / 2;
		MainCamera.position = new Vector3(targetPositionX, targetPositionY, targetPositionZ);

		Debug.Log("[RuntimeVoxController] OnLoadFinished: " + voxels.Count);
		mPaletteBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxelData.Materials.Length, Marshal.SizeOf(typeof(VoxelMaterialVFX)));
		mPaletteBuffer.SetData(voxelData.Materials);
		OpaqueVisualEffect.SetGraphicsBuffer(MATERIAL_VFX_BUFFER, mPaletteBuffer);
		TransparenceVisualEffect.SetGraphicsBuffer(MATERIAL_VFX_BUFFER, mPaletteBuffer);

		VoxelRotationVFX[] rotations = new VoxelRotationVFX[2];
		rotations[0] = new VoxelRotationVFX()
		{
			pivot = Vector3.zero,
			rotation = Vector3.zero
		};

		rotations[1] = new VoxelRotationVFX()
		{
			pivot = new Vector3(0, 0, 0.5f),
			rotation = new Vector3(90,0,0)
		};

		mRotationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rotations.Length, Marshal.SizeOf(typeof(VoxelRotationVFX)));
		mRotationBuffer.SetData(rotations);

		OpaqueVisualEffect.SetGraphicsBuffer(ROTATION_VFX_BUFFER, mRotationBuffer);
		TransparenceVisualEffect.SetGraphicsBuffer(ROTATION_VFX_BUFFER, mRotationBuffer);

		OpaqueVisualEffect.enabled = true;
		TransparenceVisualEffect.enabled = true;
		mMaterials = voxelData.Materials;
		mCustomSchematic = voxelData.CustomSchematic;
	}

	private void OnChunkLoadDistanceValueChanged()
	{
		mPreviousPosition = MainCamera.position;
		FastMath.FloorToInt(mPreviousPosition.x / CustomSchematic.CHUNK_SIZE, mPreviousPosition.y / CustomSchematic.CHUNK_SIZE, mPreviousPosition.z / CustomSchematic.CHUNK_SIZE, out int chunkX, out int chunkY, out int chunkZ);
		LoadVoxelDataAroundCamera(chunkX, chunkY, chunkZ);
	}

	private void CreateBoxColliderForCurrentChunk(long chunkIndex)
	{
		int i = 0;

		if (mCustomSchematic.RegionDict.TryGetValue(chunkIndex, out Region region))
		{
			foreach (VoxelVFX voxel in region.BlockDict.Values)
			{
				if (i < mBoxColliders.Length)
				{
					BoxCollider boxCollider = mBoxColliders[i];
					boxCollider.transform.position = voxel.position;
					i++;
				}
				else
				{
					Debug.Log("Capacity of box colliders is too small");
					break;
				}
			}
		}


	}

	private void LoadVoxelDataAroundCamera(int chunkX, int chunkY, int chunkZ)
	{
		mOpaqueBuffer?.Release();
		mTransparencyBuffer?.Release();

		List<VoxelVFX> list = new List<VoxelVFX>();
		int chunkLoadDistanceRadius = ChunkLoadDistance / 2;
		for (int x = chunkX - chunkLoadDistanceRadius; x <= chunkX + chunkLoadDistanceRadius; x++)
		{
			for (int z = chunkZ - chunkLoadDistanceRadius; z <= chunkZ + chunkLoadDistanceRadius; z++)
			{
				for (int y = chunkY - chunkLoadDistanceRadius; y <= chunkY + chunkLoadDistanceRadius; y++)
				{
					long chunkIndexAt = CustomSchematic.GetVoxelIndex(x, y, z);
					if (mCustomSchematic.RegionDict.ContainsKey(chunkIndexAt))
					{
						list.AddRange(mCustomSchematic.RegionDict[chunkIndexAt].BlockDict.Values);
					}
				}
			}
		}

		if (list.Count == 0)
		{
			Debug.Log("[RuntimeVoxController] List is empty, abort");
			return;
		}

		List<VoxelVFX> opaqueList = list.Where(v => !v.IsTransparent(mMaterials)).ToList();
		List<VoxelVFX> transparencyList = list.Where(v => v.IsTransparent(mMaterials)).ToList();

		if (!DebugVisualEffects)
		{
			OpaqueVisualEffect.visualEffectAsset = GetVisualEffectAsset(opaqueList.Count, OpaqueVisualEffects);
			TransparenceVisualEffect.visualEffectAsset = GetVisualEffectAsset(transparencyList.Count, TransparenceVisualEffects);
		}

		if (opaqueList.Count > 0)
		{
			OpaqueVisualEffect.SetInt("InitialBurstCount", opaqueList.Count);
			mOpaqueBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, opaqueList.Count, Marshal.SizeOf(typeof(VoxelVFX)));
			mOpaqueBuffer.SetData(opaqueList);

			OpaqueVisualEffect.SetGraphicsBuffer(MAIN_VFX_BUFFER, mOpaqueBuffer);
			OpaqueVisualEffect.Play();
		}


		if (transparencyList.Count > 0)
		{
			TransparenceVisualEffect.SetInt("InitialBurstCount", transparencyList.Count);
			mTransparencyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, transparencyList.Count, Marshal.SizeOf(typeof(VoxelVFX)));
			mTransparencyBuffer.SetData(transparencyList);

			TransparenceVisualEffect.SetGraphicsBuffer(MAIN_VFX_BUFFER, mTransparencyBuffer);
			TransparenceVisualEffect.Play();
		}
	}


	private VisualEffectAsset GetVisualEffectAsset(int voxels, List<VisualEffectAsset> assets)
	{
		int index = voxels / STEP_CAPACITY;
		if (index > assets.Count)
		{
			index = assets.Count - 1;
		}

		return assets[index];
	}
	#endregion
}

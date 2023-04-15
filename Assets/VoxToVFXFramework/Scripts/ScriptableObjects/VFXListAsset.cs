using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace VoxToVFXFramework.Scripts.ScriptableObjects
{
	[CreateAssetMenu(fileName = "VFXListAsset", menuName = "VoxToVFX/VFXListAsset")]
	public class VFXListAsset : ScriptableObject
	{
		public List<VisualEffectAsset> VisualEffectAssets;
	}
}
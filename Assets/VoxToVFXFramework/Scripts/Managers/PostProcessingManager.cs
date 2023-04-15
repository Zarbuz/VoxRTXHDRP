using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using VoxToVFXFramework.Scripts.Singleton;

namespace VoxToVFXFramework.Scripts.Managers
{
	[RequireComponent(typeof(Volume))]
	public class PostProcessingManager : ModuleSingleton<PostProcessingManager>
	{
		#region Fields

		[HideInInspector] public DepthOfField DepthOfField;

		private Volume mVolume;

		#endregion

		#region UnityMethods

		protected override void OnAwake()
		{
			mVolume = GetComponent<Volume>();
			mVolume.profile.TryGet(typeof(DepthOfField), out DepthOfField);
		}

		protected override void OnStart()
		{
			RuntimeVoxManager.Instance.LoadFinishedCallback += OnVoxLoadFinished;
			RuntimeVoxManager.Instance.UnloadFinishedCallback += OnVoxUnloadFinished;
		}


		private void OnDestroy()
		{
			if (RuntimeVoxManager.Instance != null)
			{
				RuntimeVoxManager.Instance.LoadFinishedCallback -= OnVoxLoadFinished;
				RuntimeVoxManager.Instance.UnloadFinishedCallback -= OnVoxUnloadFinished;
			}
		}

		#endregion

		#region PublicMethods

		public void SetActiveVolume(bool active)
		{
			mVolume.enabled = active;
		}

		public void SetEdgePostProcess(float intensity, Color color)
		{
			if (mVolume.profile.TryGet(typeof(Sobel), out Sobel sobel))
			{
				sobel.intensity.value = Mathf.Clamp01(intensity);
				sobel.outlineColour.value = color;
			}
		}

		public void SetDepthOfFieldActive(bool active)
		{
			DepthOfField.active = active;
		}

		public void SetDepthOfFieldFocus(float distance, bool isHit)
		{
			DepthOfField.active = true;
			DepthOfField.farFocusEnd.value = distance;
			DepthOfField.nearFocusEnd.value = isHit ? 0.2f : 6f;
			DepthOfField.focusMode = new DepthOfFieldModeParameter(DepthOfFieldMode.Manual);
		}

		public void SetPathTracing(bool active)
		{
			mVolume.profile.TryGet(typeof(PathTracing), out PathTracing pathTracing);
			pathTracing.active = active;
		}

		#endregion

		#region PrivateMethods

		private void OnVoxLoadFinished()
		{
			SetActiveVolume(true);
		}

		private void OnVoxUnloadFinished()
		{
			SetActiveVolume(false);
		}

		#endregion
	}
}

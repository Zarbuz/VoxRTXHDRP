using UnityEngine.Rendering.HighDefinition;
using VoxToVFXFramework.Scripts.Singleton;

namespace VoxToVFXFramework.Scripts.Managers
{
	public class CustomFrameSettingsManager : ModuleSingleton<CustomFrameSettingsManager>
	{
		private HDAdditionalCameraData mCameraData;
		private FrameSettings mFrameSettings;

		protected override void OnStart()
		{
			mCameraData = this.GetComponent<HDAdditionalCameraData>();
			mFrameSettings = mCameraData.renderingPathCustomFrameSettings;
			//Make sure Custom Frame Settings are enabled in the camera
			mCameraData.customRenderingSettings = true;
		}

		public void SetRaytracingActive(bool active)
		{
			mFrameSettings.SetEnabled(FrameSettingsField.RayTracing, active);
			mCameraData.renderingPathCustomFrameSettings = mFrameSettings;
		}
	}
}

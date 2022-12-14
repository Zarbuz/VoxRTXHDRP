using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VoxToVFXFramework.Scripts.Managers;
using VoxToVFXFramework.Scripts.Singleton;
using VoxToVFXFramework.Scripts.UI.ImportScene;
using VoxToVFXFramework.Scripts.UI.Photo;
using VoxToVFXFramework.Scripts.UI.Settings;
using VoxToVFXFramework.Scripts.UI.Weather;

namespace VoxToVFXFramework.Scripts.UI
{
	public enum CanvasPlayerPCState
	{
		Closed,
		Pause,
		ImportScene,
		Settings,
		Weather,
		Photo
	}

	public class CanvasPlayerPCManager : ModuleSingleton<CanvasPlayerPCManager>
	{
		#region ScriptParameters

		[SerializeField] private Material BlurMat;
		[SerializeField] private Image BackgroundBlurImage;

		[SerializeField] private PausePanel PausePanel;
		[SerializeField] private ImportScenePanel ImportScenePanel;
		[SerializeField] private SettingsPanel SettingsPanel;
		[SerializeField] private WeatherPanel WeatherPanel;
		[SerializeField] private PhotoPanel PhotoPanel;
		#endregion

		#region Fields

		private CanvasPlayerPCState mCanvasPlayerPcState;

		public CanvasPlayerPCState CanvasPlayerPcState
		{
			get => mCanvasPlayerPcState;
			set
			{
				mCanvasPlayerPcState = value;
				PausePanel.gameObject.SetActive(mCanvasPlayerPcState == CanvasPlayerPCState.Pause);
				ImportScenePanel.gameObject.SetActive(mCanvasPlayerPcState == CanvasPlayerPCState.ImportScene);
				SettingsPanel.gameObject.SetActive(mCanvasPlayerPcState == CanvasPlayerPCState.Settings);
				WeatherPanel.gameObject.SetActive(mCanvasPlayerPcState == CanvasPlayerPCState.Weather);
				PhotoPanel.gameObject.SetActive(mCanvasPlayerPcState == CanvasPlayerPCState.Photo);


				CheckBlurImage();
			}
		}



		public bool PauseLockedState { get; set; }

		private RenderTexture mRenderTexture;
		private UnityEngine.Camera mNewCamera;
		private static readonly int mAltTexture = Shader.PropertyToID("_AltTexture");

		#endregion

		#region UnityMethods

		protected override void OnStart()
		{
			CreateCameraRenderTexture();
			CanvasPlayerPcState = CanvasPlayerPCState.Closed;
		}

		private void Update()
		{
			if (Keyboard.current.escapeKey.wasPressedThisFrame && !PauseLockedState)
			{
				GenericTogglePanel(CanvasPlayerPCState.Pause);
			}
			else if (Keyboard.current.tabKey.wasPressedThisFrame && (CanvasPlayerPcState == CanvasPlayerPCState.Photo || CanvasPlayerPcState == CanvasPlayerPCState.Closed))
			{
				if (RuntimeVoxManager.Instance.IsReady)
				{
					GenericTogglePanel(CanvasPlayerPCState.Photo);
				}
			}
			else if (CanvasPlayerPcState != CanvasPlayerPCState.Closed && !Cursor.visible)
			{
				RefreshCursorState();
			}
		}

		#endregion

		#region PublicMethods

		public void SetCanvasPlayerState(CanvasPlayerPCState state)
		{
			CanvasPlayerPcState = state;
		}

		public void GenericTogglePanel(CanvasPlayerPCState state)
		{
			CanvasPlayerPcState = CanvasPlayerPcState == state ? CanvasPlayerPCState.Closed : state;
			RefreshCursorState();
		}

		public void GenericClosePanel()
		{
			CanvasPlayerPcState = CanvasPlayerPCState.Closed;
			RefreshCursorState();
		}

		public void OpenImportScenePanel(ImportScenePanel.EDataImportType dataImportType)
		{
			ImportScenePanel.Initialize(dataImportType);
			GenericTogglePanel(CanvasPlayerPCState.ImportScene);
		}

		#endregion

		#region PrivateMethods

		private void CreateCameraRenderTexture()
		{
			UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
			Transform transform1 = mainCamera!.transform;
			mNewCamera = Instantiate(mainCamera, transform1.parent, true);

			mNewCamera.transform.localPosition = transform1.localPosition;
			mNewCamera.transform.localRotation = transform1.localRotation;
			mNewCamera.transform.localScale = transform1.localScale;

			mRenderTexture = new RenderTexture(Screen.width, Screen.height, 24);
			mNewCamera.targetTexture = mRenderTexture;
			mNewCamera.name = "Camera_RenderTexture";
			BlurMat.SetTexture(mAltTexture, mRenderTexture);
		}

		private void CheckBlurImage()
		{
			mNewCamera.gameObject.SetActive(mCanvasPlayerPcState != CanvasPlayerPCState.Closed && mCanvasPlayerPcState != CanvasPlayerPCState.Photo);
			BackgroundBlurImage.gameObject.SetActive(mCanvasPlayerPcState != CanvasPlayerPCState.Closed && mCanvasPlayerPcState != CanvasPlayerPCState.Photo);

			if (mNewCamera.gameObject.activeSelf)
			{
				//Just enable the camera one frame
				StartCoroutine(DisableBlurCameraCo());
			}
		}

		private IEnumerator DisableBlurCameraCo()
		{
			yield return new WaitForEndOfFrame();
			mNewCamera.gameObject.SetActive(false);
		}

		private void RefreshCursorState()
		{
			switch (CanvasPlayerPcState)
			{
				case CanvasPlayerPCState.Closed:
					Cursor.visible = false;
					Cursor.lockState = CursorLockMode.Locked;
					Time.timeScale = 1;
					break;
				case CanvasPlayerPCState.Photo:
					Cursor.visible = true;
					Cursor.lockState = CursorLockMode.None;
					Time.timeScale = 1;
					break;
				default:
					Cursor.visible = true;
					Cursor.lockState = CursorLockMode.None;
					Time.timeScale = 0;
					break;
			}

		}

		#endregion
	}
}

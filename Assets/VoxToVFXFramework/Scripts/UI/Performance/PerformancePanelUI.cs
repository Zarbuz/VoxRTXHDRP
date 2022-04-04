using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VoxToVFXFramework.Scripts.Managers;

public class PerformancePanelUI : MonoBehaviour
{
	#region ScriptParamaters

	[SerializeField] private Button TogglePanelButton;
	[SerializeField] private TextMeshProUGUI ForceLevelLODText;
	[SerializeField] private Slider ForceLevelLODSlider;

	[SerializeField] private Toggle ShowLODToggle;
	[SerializeField] private GameObject ContentPanel;

	#endregion

	#region UnityMethods

	private void OnEnable()
	{
		TogglePanelButton.onClick.AddListener(OnTogglePanelClicked);
		ForceLevelLODSlider.onValueChanged.AddListener(OnForceLevelLODValueChanged);
		ShowLODToggle.onValueChanged.AddListener(OnShowLODValueChanged);
		RefreshValues();
	}

	private void OnDisable()
	{
		TogglePanelButton.onClick.RemoveListener(OnTogglePanelClicked);
		ForceLevelLODSlider.onValueChanged.RemoveListener(OnForceLevelLODValueChanged);
		ShowLODToggle.onValueChanged.RemoveListener(OnShowLODValueChanged);
	}

	#endregion

	#region PrivateMethods

	private void OnTogglePanelClicked()
	{
		ContentPanel.SetActive(!ContentPanel.activeSelf);
	}

	private void RefreshValues()
	{
		ForceLevelLODText.text = "Force Level LOD: " + RuntimeVoxManager.Instance.ForcedLevelLod;
		ForceLevelLODSlider.SetValueWithoutNotify(RuntimeVoxManager.Instance.ForcedLevelLod);
		ShowLODToggle.SetIsOnWithoutNotify(RuntimeVoxManager.Instance.DebugLod);
	}

	private void OnForceLevelLODValueChanged(float value)
	{
		RuntimeVoxManager.Instance.SetForceLODValue((int)value);
		ForceLevelLODText.text = "Force Level LOD: " + value;
	}

	private void OnShowLODValueChanged(bool value)
	{
		RuntimeVoxManager.Instance.SetDebugLodValue(value);
	}

	#endregion
}
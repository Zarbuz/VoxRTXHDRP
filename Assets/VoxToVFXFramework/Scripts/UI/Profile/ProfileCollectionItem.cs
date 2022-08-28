﻿using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VoxToVFXFramework.Scripts.Managers;
using VoxToVFXFramework.Scripts.Models;
using VoxToVFXFramework.Scripts.Models.ContractEvent;
using VoxToVFXFramework.Scripts.UI.Atomic;
using VoxToVFXFramework.Scripts.Utils.Image;

namespace VoxToVFXFramework.Scripts.UI.Profile
{
	public class ProfileCollectionItem : AbstractCardItem
	{
		#region ScriptParameters

		[SerializeField] private Image CollectionCoverImage;
		[SerializeField] private Image CollectionLogoImage;
		[SerializeField] private TextMeshProUGUI CollectionNameText;
		[SerializeField] private OpenUserProfileButton OpenUserProfileButton;
		[SerializeField] private TextMeshProUGUI CollectionSymbolText;
		[SerializeField] private Button Button;

		#endregion

		#region PublicMethods

		public async UniTask Initialize(CollectionCreatedEvent collection)
		{
			Button.onClick.AddListener(() => CanvasPlayerPCManager.Instance.OpenCollectionDetailsPanel(collection));
			TransparentButton[] transparentButtons = GetComponentsInChildren<TransparentButton>();

			Models.CollectionDetails collectionDetails = await DataManager.Instance.GetCollectionDetailsWithCache(collection.CollectionContract);

			foreach (TransparentButton transparentButton in transparentButtons)
			{
				transparentButton.ImageBackgroundActive = collectionDetails != null && !string.IsNullOrEmpty(collectionDetails.CoverImageUrl);
			}

			CollectionLogoImage.transform.parent.gameObject.SetActive(collectionDetails != null && !string.IsNullOrEmpty(collectionDetails.LogoImageUrl));
			CollectionCoverImage.gameObject.SetActive(collectionDetails != null && !string.IsNullOrEmpty(collectionDetails.CoverImageUrl));
			if (collectionDetails != null)
			{
				await ImageUtils.DownloadAndApplyImageAndCropAfter(collectionDetails.CoverImageUrl, CollectionCoverImage, 398, 524);
				await ImageUtils.DownloadAndApplyImageAndCropAfter(collectionDetails.LogoImageUrl, CollectionLogoImage, 100, 100);
			}
			CollectionNameText.color = collectionDetails != null && !string.IsNullOrEmpty(collectionDetails.CoverImageUrl) ? Color.white : Color.black;
			CollectionNameText.text = collection.Name;
			CollectionSymbolText.text = collection.Symbol;
			CustomUser creator = await DataManager.Instance.GetUserWithCache(collection.Creator);
			OpenUserProfileButton.Initialize(creator);
		}

		#endregion

	}
}
﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoralisUnity.Web3Api.Models;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VoxToVFXFramework.Scripts.Localization;
using VoxToVFXFramework.Scripts.Managers;
using VoxToVFXFramework.Scripts.Managers.DataManager;
using VoxToVFXFramework.Scripts.Models;
using VoxToVFXFramework.Scripts.Models.ContractEvent;
using VoxToVFXFramework.Scripts.UI.Atomic;
using VoxToVFXFramework.Scripts.Utils.Image;

namespace VoxToVFXFramework.Scripts.UI.NFTDetails
{
	public class NFTDetailsPanel : MonoBehaviour
	{
		#region ScriptParameters

		[Header("Global")]
		[SerializeField] private VerticalLayoutGroup VerticalLayoutGroup;
		[SerializeField] private Image LoadingBackgroundImage;

		[Header("Top")]
		[SerializeField] private Image MainImage;

		[Header("Left")]
		[SerializeField] private TextMeshProUGUI Title;
		[SerializeField] private TextMeshProUGUI DescriptionLabel;
		[SerializeField] private TextMeshProUGUI Description;
		[SerializeField] private Button OpenTransactionButton;
		[SerializeField] private TextMeshProUGUI MintedDateText;
		[SerializeField] private OpenUserProfileButton OpenUserProfileButton;
		[SerializeField] private TextMeshProUGUI CollectionNameText;
		[SerializeField] private Image CollectionImage;
		[SerializeField] private Button ViewEtherscanButton;
		[SerializeField] private Button ViewMetadataButton;
		[SerializeField] private Button ViewIpfsButton;
		[SerializeField] private Button OpenCollectionButton;

		[Header("Right")]
		[SerializeField] private Button LoadVoxModelButton;
		[SerializeField] private NFTDetailsManagePanel NFTDetailsManagePanel;
		[SerializeField] private ProvenanceNFTItem ProvenanceNftItemPrefab;
		[SerializeField] private VerticalLayoutGroup RightPart;

		#endregion

		#region Fields

		private CollectionCreatedEvent mCollectionCreated;
		private CollectionMintedEvent mCollectionMinted;
		private NftOwner mNft;
		private MetadataObject mMetadataObject;

		private readonly List<ProvenanceNFTItem> mProvenanceNFTItemList = new List<ProvenanceNFTItem>();
		#endregion

		#region UnityMethods

		private void OnEnable()
		{
			OpenTransactionButton.onClick.AddListener(OnOpenTransactionClicked);
			ViewEtherscanButton.onClick.AddListener(OnViewEtherscanClicked);
			ViewMetadataButton.onClick.AddListener(OnViewMetadataClicked);
			ViewIpfsButton.onClick.AddListener(OnViewIpfsClicked);
			LoadVoxModelButton.onClick.AddListener(OnLoadVoxModelClicked);
			OpenCollectionButton.onClick.AddListener(OnOpenCollectionClicked);
		}

		private void OnDisable()
		{
			OpenTransactionButton.onClick.RemoveListener(OnOpenTransactionClicked);
			ViewEtherscanButton.onClick.RemoveListener(OnViewEtherscanClicked);
			ViewMetadataButton.onClick.RemoveListener(OnViewMetadataClicked);
			ViewIpfsButton.onClick.RemoveListener(OnViewIpfsClicked);
			LoadVoxModelButton.onClick.RemoveListener(OnLoadVoxModelClicked);
			OpenCollectionButton.onClick.RemoveListener(OnOpenCollectionClicked);
		}

		#endregion

		#region PublicMethods

		public async void Initialize(NftOwner nft)
		{
			mNft = nft;

			LoadingBackgroundImage.gameObject.SetActive(true);

			string creatorAddress = await DataManager.Instance.GetCreatorOfCollection(nft.TokenAddress);
			CustomUser creatorUser = await DataManager.Instance.GetUserWithCache(creatorAddress);
			Models.CollectionDetails details = await DataManager.Instance.GetCollectionDetailsWithCache(nft.TokenAddress);
			mCollectionCreated = await DataManager.Instance.GetCollectionCreatedEventWithCache(nft.TokenAddress);
			List<AbstractContractEvent> events = await DataManager.Instance.GetAllEventsForNFT(nft.TokenAddress, nft.TokenId);
			mCollectionMinted = (CollectionMintedEvent)events.First(e => e is CollectionMintedEvent);
			BuildProvenanceDetails(events);

			MintedDateText.text = string.Format(LocalizationKeys.MINTED_ON_DATE.Translate(), mCollectionMinted.createdAt.Value.ToShortDateString());
			NFTDetailsManagePanel.gameObject.SetActive(nft.OwnerOf == UserManager.Instance.CurrentUserAddress);
			NFTDetailsManagePanel.Initialize(nft, creatorUser);
			OpenUserProfileButton.Initialize(creatorUser);
			CollectionNameText.text = mCollectionCreated.Name;
			try
			{
				mMetadataObject = JsonConvert.DeserializeObject<MetadataObject>(nft.Metadata);
				Title.text = mMetadataObject.Name;
				DescriptionLabel.gameObject.SetActive(!string.IsNullOrEmpty(mMetadataObject.Description));
				Description.gameObject.SetActive(!string.IsNullOrEmpty(mMetadataObject.Description));
				Description.text = mMetadataObject.Description;
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}

			CollectionImage.transform.parent.gameObject.SetActive(details != null && !string.IsNullOrEmpty(details.LogoImageUrl));
			if (details != null)
			{
				if (!string.IsNullOrEmpty(details.LogoImageUrl))
				{
					bool success = await ImageUtils.DownloadAndApplyImageAndCropAfter(details.LogoImageUrl, CollectionImage, 32, 32);
					if (!success)
					{
						CollectionImage.transform.parent.gameObject.SetActive(false);	
					}
				}	
			}

			await ImageUtils.DownloadAndApplyImage(mMetadataObject.Image, MainImage);
			LayoutRebuilder.ForceRebuildLayoutImmediate(VerticalLayoutGroup.GetComponent<RectTransform>());
			LoadingBackgroundImage.gameObject.SetActive(false);
		}

		#endregion

		#region PrivateMethods

		private void BuildProvenanceDetails(List<AbstractContractEvent> events)
		{
			foreach (ProvenanceNFTItem item in mProvenanceNFTItemList)
			{
				Destroy(item.gameObject);
			}

			mProvenanceNFTItemList.Clear();

			for (int index = 0; index < events.Count; index++)
			{
				AbstractContractEvent contractEvent = events[index];
				ProvenanceNFTItem item = Instantiate(ProvenanceNftItemPrefab, RightPart.transform, false);
				if (contractEvent is BuyPriceCanceledEvent buyPriceCanceledEvent)
				{
					//BuyPriceSetEvent is always before BuyPriceCanceledEvent
					buyPriceCanceledEvent.BuyPriceSetEventLinked = (BuyPriceSetEvent)events[index - 1];
				}

				item.Initialize(contractEvent);
				mProvenanceNFTItemList.Add(item);
			}
		}


		private void OnOpenCollectionClicked()
		{
			CanvasPlayerPCManager.Instance.OpenCollectionDetailsPanel(mCollectionCreated);
		}

		private void OnViewEtherscanClicked()
		{
			string url = ConfigManager.Instance.EtherScanBaseUrl + "nft/" + mNft.TokenAddress + "/" + mNft.TokenId;
			Application.OpenURL(url);
		}

		private void OnViewMetadataClicked()
		{
			Application.OpenURL(mNft.TokenUri);
		}

		private void OnViewIpfsClicked()
		{
			Application.OpenURL(mMetadataObject.Image);
		}

		private void OnOpenTransactionClicked()
		{
			string url = ConfigManager.Instance.EtherScanBaseUrl + "tx/" + mCollectionMinted.TransactionHash;
			Application.OpenURL(url);
		}

		private async void OnLoadVoxModelClicked()
		{
			string fileName = mCollectionMinted.TransactionHash + ".zip";
			string zipPath = Path.Combine(Application.persistentDataPath, VoxelDataCreatorManager.VOX_FOLDER_CACHE_NAME, fileName);

			if (File.Exists(zipPath))
			{
				ReadZipPath(zipPath);
			}
			else
			{
				CanvasPlayerPCManager.Instance.OpenLoadingPanel(LocalizationKeys.LOADING_DOWNLOAD_MODEL.Translate());
				await VoxelDataCreatorManager.Instance.DownloadVoxModel(mMetadataObject.FilesUrl, zipPath);
				ReadZipPath(zipPath);
			}
		}

		private void ReadZipPath(string zipPath)
		{
			CanvasPlayerPCManager.Instance.OpenLoadingPanel(LocalizationKeys.LOADING_SCENE_DESCRIPTION.Translate(),
				() =>
				{
					CanvasPlayerPCManager.Instance.GenericClosePanel();
				});

			VoxelDataCreatorManager.Instance.ReadZipFile(zipPath);
		}
		#endregion
	}
}

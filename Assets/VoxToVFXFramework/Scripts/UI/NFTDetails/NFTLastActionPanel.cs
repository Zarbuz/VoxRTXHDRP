using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MoralisUnity;
using MoralisUnity.Web3Api.Models;
using Nethereum.Util;
using Org.BouncyCastle.Math;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VoxToVFXFramework.Scripts.ContractTypes;
using VoxToVFXFramework.Scripts.Localization;
using VoxToVFXFramework.Scripts.Managers.DataManager;
using VoxToVFXFramework.Scripts.Models;
using VoxToVFXFramework.Scripts.Models.ContractEvent;
using VoxToVFXFramework.Scripts.UI.Atomic;
using BigInteger = System.Numerics.BigInteger;

public class NFTLastActionPanel : MonoBehaviour
{
	#region ScriptParameters

	[Header("MakeOfferPanel")]
	[SerializeField] private TextMeshProUGUI LastActionText;
	[SerializeField] private TextMeshProUGUI LastActionPriceText;
	[SerializeField] private Button MakeOfferButton;


	[Header("BuyNowPanel")]
	[SerializeField] private GameObject BuyNowPanel;
	[SerializeField] private TextMeshProUGUI BuyNowPriceText;
	[SerializeField] private Button BuyNowButton;

	[SerializeField] private OpenUserProfileButton OpenOwnerProfileButton;

	#endregion

	#region UnityMethods

	private void OnEnable()
	{
		MakeOfferButton.onClick.AddListener(OnMakeOfferClicked);
		BuyNowButton.onClick.AddListener(OnBuyNowClicked);
	}

	private void OnDisable()
	{
		MakeOfferButton.onClick.RemoveListener(OnMakeOfferClicked);
		BuyNowButton.onClick.RemoveListener(OnBuyNowClicked);
	}

	#endregion

	#region PublicMethods

	public void Initialize(NFTDetailsContractType details, List<AbstractContractEvent> events)
	{
		BuyNowPanel.SetActive(details is { IsInEscrow: true });
		OpenOwnerProfileButton.Initialize(details.OwnerInLowercase);
		BuyNowPriceText.text = details.BuyPriceInEtherFixedPoint + " " + Moralis.CurrentChain.Symbol;

		if (!details.IsInEscrow && events.Any(e => e is BuyPriceAcceptedEvent))
		{
			BuyPriceAcceptedEvent buyPriceAccepted = events.First(e => e is BuyPriceAcceptedEvent) as BuyPriceAcceptedEvent;
			LastActionText.text = LocalizationKeys.LAST_SOLD_LABEL.Translate();

			BigInteger total = buyPriceAccepted.CreatorFee + buyPriceAccepted.ProtocolFee + buyPriceAccepted.SellerRev;
			try
			{
				decimal totalFromWei = UnitConversion.Convert.FromWei(total);
				LastActionPriceText.text = totalFromWei.ToString("F2") + " " + Moralis.CurrentChain.Symbol;
			}
			catch
			{
				// ignored
				LastActionPriceText.text = string.Empty;
			}
		}
		else
		{
			CollectionMintedEvent collectionMintedEvent = events.First(e => e is CollectionMintedEvent) as CollectionMintedEvent;
			LastActionText.text = string.Format(LocalizationKeys.MINTED_ON_DATE.Translate(), collectionMintedEvent.createdAt.Value.ToString("F"));
			LastActionPriceText.text = string.Empty;
		}
	}

	#endregion

	#region PrivateMethods

	private void OnMakeOfferClicked()
	{
		//TODO
	}

	private void OnBuyNowClicked()
	{

	}

	#endregion
}

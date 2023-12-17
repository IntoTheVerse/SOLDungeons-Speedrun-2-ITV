using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NFT : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nftName;
    [SerializeField] private TextMeshProUGUI nftPrice;
    [SerializeField] private Image nftImage;
    [SerializeField] private Button buyButton;
    [SerializeField] private Button equipButton;
    [SerializeField] private Button detailsButton;

    private float price;
    private int type;
    private string id;
    private bool owns;
    private string nftName_;
    private string nftDesc;
    private Sprite nftSprite;
    private NFTDetails details;

    private void Start()
    {
        buyButton.onClick.AddListener(() => BuyNFT());
        equipButton.onClick.AddListener(() => EquipNFT());
        detailsButton.onClick.AddListener(() => ShowDetails());
        details = FindObjectOfType<NFTDetails>(true);
    }

    public void SetupNFT(string name, string desc, int price, Sprite nftSprite, bool owns, int type, string id)
    {
        this.type = type;
        this.price = price;
        this.id = id;
        this.owns = owns;
        this.nftSprite = nftSprite;
        nftDesc = desc;
        nftName_ = name;

        nftName.text = name;
        nftPrice.text = price.ToString();
        nftImage.sprite = nftSprite;

        CheckOwnsAndSetButtons();
    }

    private void CheckOwnsAndSetButtons()
    {
        equipButton.gameObject.SetActive(false);
        buyButton.gameObject.SetActive(false);
        if (owns)
        {
            if (type == 0) equipButton.gameObject.SetActive(true);
        }
        else buyButton.gameObject.SetActive(true);
    }

    private async void BuyNFT()
    {
        if (WalletManager.instance.DunTokenBalance < price)
        {
            InfoDisplay.Instance.ShowInfo("Error", $"Dun Balacne low! You need {price}, but have {WalletManager.instance.DunTokenBalance}.");
            return;
        }
        InfoDisplay.Instance.ShowInfo("Processing", "Minting NFT");

        await WalletManager.instance.SendToken(WalletManager.instance.dunMint, (int)price);
        await WalletManager.instance.RecieveToken(id, 1);

        owns = true;
        CheckOwnsAndSetButtons();
        FindObjectOfType<MainMenuUI>().UpdateUserDunBalance((int)(WalletManager.instance.DunTokenBalance - price));
        InfoDisplay.Instance.HideInfo();
    }

    private void EquipNFT()
    {
        if (type == 0) 
        {
            for (int i = 0; i < WalletManager.instance.metadatas.charactersMetadata.Length; i++)
            {
                if(id == WalletManager.instance.metadatas.charactersMetadata[i].publicKey)
                    FindObjectOfType<CharacterSelectorUI>(true).SwitchToCharacter(i);
            }
        }
    }

    private void ShowDetails()
    {
       details.SetupDetails(nftName_, nftDesc, owns, type, nftSprite, (int)price, () => BuyNFT(), EquipNFT);
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using Solana.Unity.Programs;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Core.Http;
using Solana.Unity.Soar.Program;
using Solana.Unity.Rpc.Types;
using Cysharp.Threading.Tasks;
using SolDungeons.Program;
using SolDungeons;

[System.Serializable]
public struct NFTMetadatas
{
    public NFTDatas[] charactersMetadata;
    public NFTDatas[] weaponsMetadata;
}

public struct OwnedNFTIds
{
    public List<string> ownedCharactersId;
    public List<string> ownedWeaponsId;
}

public class MainMenuUI : MonoBehaviour
{
    [Space(10)]
    [Header("OBJECT REFERENCES")]
    [Tooltip("Populate with the enter the dungeon play button gameobject")]
    [SerializeField] private GameObject playButton;

    [Tooltip("Populate with the quit button gameobject")]
    [SerializeField] private GameObject quitButton;

    [Tooltip("Populate with the high scores button gameobject")]
    [SerializeField] private GameObject highScoresButton;

    [Tooltip("Populate with the instructions button gameobject")]
    [SerializeField] private GameObject instructionsButton;

    [Tooltip("Populate with the return to main menu button gameobject")]
    [SerializeField] private GameObject returnToMainMenuButton;

    [Tooltip("Populate with sign in button gameobject")]
    [SerializeField] private GameObject signInButton;
    [SerializeField] private GameObject MainMenuPanel;
    [SerializeField] private GameObject InstructionMenuPanel;
    [SerializeField] private GameObject HighScorePanel;
    [SerializeField] private GameObject marketplaceButton;

    [SerializeField] private TextMeshProUGUI userPublicKey;
    [SerializeField] private TextMeshProUGUI userDunBalance;
    [SerializeField] private NFT nftPrefab;
    [SerializeField] private Transform characterNftSpawn;
    [SerializeField] private Transform weaponNftSpawn;

    private void Start()
    {
        MusicManager.Instance.PlayMusic(GameResources.Instance.mainMenuMusic, 0f, 2f);
        returnToMainMenuButton.SetActive(false);
    }

    /// <summary>
    /// Called from the Play Game / Enter The Dungeon Button
    /// </summary>
    public void PlayGame()
    {
        SceneManager.LoadScene("MainGameScene");
    }

    public void OnEnable()
    {
        Web3.OnLogin += OnLogin;
        Web3.OnLogout += OnLogout;
    }

    public void OnDisable()
    {
        Web3.OnLogin += OnLogin;
        Web3.OnLogout += OnLogout;
    }

    private void OnLogout()
    {
        
    }

    private async void OnLogin(Account account)
    {
        await WalletManager.instance.GetPDAs();
        SetupPlayer();
    }

    /// <summary>
    /// Sign in using WalletConnect
    /// </summary>
    public async void SignIn()
    {
        await WalletManager.instance.AuthenticateWithWallet();
    }

    /// <summary>
    /// Checks if the User is a new User
    /// </summary>
    private async void SetupPlayer()
    {
        if (!await WalletManager.instance.IsPdaInitialized(WalletManager.instance.playerPDA)) OnSetupNewAccount();
        else
        {
            var client = new SolDungeonsClient(Web3.Rpc, Web3.WsRpc, WalletManager.instance.programId);
            var res = await client.GetUserAsync(WalletManager.instance.playerPDA);
            WalletManager.instance.user = res.ParsedResult;
            HighScoreManager.Instance.Refresh();
            userDunBalance.gameObject.SetActive(true);
            FindObjectOfType<CharacterSelectorUI>().UpdateNameFromWeb3(WalletManager.instance.user.Username);
            userPublicKey.text = $"Public Key: {Web3.Account.PublicKey}";
            signInButton.SetActive(false);
            highScoresButton.SetActive(true);
            playButton.SetActive(true);
            GetTokenBalance();
        }
    }

    private async void OnSetupNewAccount()
    {
        string username = $"User{Random.Range(0, 100000)}";

        var tx = new Transaction()
        {
            FeePayer = Web3.Account,
            Instructions = new List<TransactionInstruction>(),
            RecentBlockHash = (await WalletManager.instance.rpcClient.GetLatestBlockHashAsync()).Result.Value.Blockhash
        };

        var dunRes = await Web3.Rpc.GetAccountInfoAsync(AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(Web3.Account.PublicKey, WalletManager.instance.dunMint));
        if (!dunRes.WasSuccessful || dunRes.Result?.Value == null)
        {
            TransactionInstruction createDunATAIx = AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(Web3.Account.PublicKey, Web3.Account.PublicKey, WalletManager.instance.dunMint);
            tx.Add(createDunATAIx);
        }

        var accountsInitUser = new InitializePlayerAccounts()
        {
            Payer = Web3.Account,
            User = Web3.Account,
            PlayerAccount = WalletManager.instance.playerSoarPDA,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var initPlayerIx = SoarProgram.InitializePlayer(
            accounts: accountsInitUser,
            username: username,
            nftMeta: Web3.Account.PublicKey,
            SoarProgram.ProgramIdKey
        );

        tx.Add(initPlayerIx);

        var userAccounts = new InitializeUserAccounts()
        {
            Signer = Web3.Account,
            User = WalletManager.instance.playerPDA,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var userIx = SolDungeonsProgram.InitializeUser(
            accounts: userAccounts,
            username: username,
            WalletManager.instance.programId
        );

        tx.Add(userIx);

        RequestResult<string> resTx = await Web3.Wallet.SignAndSendTransaction(tx, commitment: Commitment.Finalized);
        Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(resTx)}");

        await WalletManager.instance.RecieveToken(WalletManager.instance.metadatas.charactersMetadata[0].publicKey, 1);
        await WalletManager.instance.RecieveToken(WalletManager.instance.metadatas.weaponsMetadata[0].publicKey, 1);
        SetupPlayer();
    }

    private async void GetTokenBalance()
    {
        var tokens = await Web3.Rpc.GetTokenAccountsByOwnerAsync(Web3.Account.PublicKey, tokenProgramId: "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        foreach (TokenAccount account in tokens.Result?.Value)
        {
            if (account.Account.Data.Parsed.Info.Mint == WalletManager.instance.dunMint)
                UpdateUserDunBalance((int)account.Account.Data.Parsed.Info.TokenAmount.AmountDecimal);
        }

        GetNFTData();
    }

    private async void GetNFTData()
    {
        InfoDisplay.Instance.ShowInfo("Processing", "Fetching NFTs!");
        var tokens = await Web3.Rpc.GetTokenAccountsByOwnerAsync(Web3.Account.PublicKey, tokenProgramId: "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        OwnedNFTIds ownedNftIds = new()
        {
            ownedCharactersId = new(),
            ownedWeaponsId = new()
        };
        for (int i = 0; i < WalletManager.instance.metadatas.charactersMetadata.Length; i++)
        {
            foreach (TokenAccount account in tokens.Result?.Value)
            {
                if (account.Account.Data.Parsed.Info.Mint == WalletManager.instance.metadatas.charactersMetadata[i].publicKey) 
                    ownedNftIds.ownedCharactersId.Add(WalletManager.instance.metadatas.charactersMetadata[i].publicKey);
            }
        }

        for (int i = 0; i < WalletManager.instance.metadatas.weaponsMetadata.Length; i++)
        {
            foreach (TokenAccount account in tokens.Result?.Value)
            {
                if (account.Account.Data.Parsed.Info.Mint == WalletManager.instance.metadatas.weaponsMetadata[i].publicKey)
                    ownedNftIds.ownedWeaponsId.Add(WalletManager.instance.metadatas.weaponsMetadata[i].publicKey);
            }
        }

        WalletManager.instance.ownedNFTIds = ownedNftIds;

        foreach (Transform item in characterNftSpawn)
        {
            Destroy(item.gameObject);
        }

        foreach (Transform item in weaponNftSpawn)
        {
            Destroy(item.gameObject);
        }

        for (int i = 0; i < WalletManager.instance.metadatas.charactersMetadata.Length; i++)
        {
            NFTDatas data = WalletManager.instance.metadatas.charactersMetadata[i];
            Instantiate(nftPrefab, characterNftSpawn).SetupNFT(
                data.name, 
                data.description, 
                data.price, 
                data.sprite, 
                ownedNftIds.ownedCharactersId.Contains(data.publicKey), 
                0, 
                data.publicKey);
        }

        for (int i = 0; i < WalletManager.instance.metadatas.weaponsMetadata.Length; i++)
        {
            NFTDatas data = WalletManager.instance.metadatas.weaponsMetadata[i];
            Instantiate(nftPrefab, weaponNftSpawn).SetupNFT(
                data.name,
                data.description,
                data.price,
                data.sprite,
                ownedNftIds.ownedWeaponsId.Contains(data.publicKey),
                1,
                data.publicKey);
        }

        marketplaceButton.gameObject.SetActive(true);
        InfoDisplay.Instance.HideInfo();
    }

    public void UpdateUserDunBalance(int val)
    {
        WalletManager.instance.DunTokenBalance = val;
        userDunBalance.text = $"$DUN: {val}";
    }

    /// <summary>
    /// Called from the High Scores Button
    /// </summary>
    public void LoadHighScores()
    {
        playButton.SetActive(false);
        quitButton.SetActive(false);
        highScoresButton.SetActive(false);
        marketplaceButton.SetActive(false);
        instructionsButton.SetActive(false);
        returnToMainMenuButton.SetActive(true);

        MainMenuPanel.SetActive(false);
        InstructionMenuPanel.SetActive(false);
        HighScorePanel.SetActive(true);
    }

    /// <summary>
    /// Called from the Return To Main Menu Button
    /// </summary>
    public void LoadCharacterSelector()
    {
        returnToMainMenuButton.SetActive(false);
        MainMenuPanel.SetActive(true);
        InstructionMenuPanel.SetActive(false);
        HighScorePanel.SetActive(false);
        instructionsButton.SetActive(true);

        if (WalletManager.instance.playerPDA != null)
        {
            playButton.SetActive(true);
            highScoresButton.SetActive(true);
            marketplaceButton.SetActive(true);
        }
        else
        { 
            signInButton.SetActive(true);
        }
    }

    /// <summary>
    /// Called from the Instructions Button
    /// </summary>
    public void LoadInstructions()
    {
        playButton.SetActive(false);
        quitButton.SetActive(false);
        highScoresButton.SetActive(false);
        instructionsButton.SetActive(false);
        signInButton.SetActive(false);
        marketplaceButton.SetActive(false);
        returnToMainMenuButton.SetActive(true);

        MainMenuPanel.SetActive(false);
        InstructionMenuPanel.SetActive(true);
        HighScorePanel.SetActive(false);
    }

    /// <summary>
    /// Quit the game - this method is called from the onClick event set in the inspector
    /// </summary>
    public void QuitGame()
    {
        Application.Quit();
    }


    #region Validation
#if UNITY_EDITOR
    // Validate the scriptable object details entered
    private void OnValidate()
    {
        HelperUtilities.ValidateCheckNullValue(this, nameof(playButton), playButton);
        HelperUtilities.ValidateCheckNullValue(this, nameof(quitButton), quitButton);
        HelperUtilities.ValidateCheckNullValue(this, nameof(highScoresButton), highScoresButton);
        HelperUtilities.ValidateCheckNullValue(this, nameof(instructionsButton), instructionsButton);
        HelperUtilities.ValidateCheckNullValue(this, nameof(returnToMainMenuButton), returnToMainMenuButton);
    }
#endif
    #endregion
}
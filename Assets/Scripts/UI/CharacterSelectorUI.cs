using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Solana.Unity.Rpc.Models;
using SolDungeons.Program;
using Solana.Unity.Programs;
using Solana.Unity.Rpc.Core.Http;
using Solana.Unity.Soar.Program;
using Solana.Unity.SDK;

[DisallowMultipleComponent]
public class CharacterSelectorUI : MonoBehaviour
{
    #region Tooltip
    [Tooltip("Populate this with the child CharacterSelector gameobject")]
    #endregion
    [SerializeField] private Transform characterSelector;
    #region Tooltip
    [Tooltip("Populate with the TextMeshPro component on the PlayerNameInput gameobject")]
    #endregion
    public TMP_InputField playerNameInput;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button cancelButton;
    private List<PlayerDetailsSO> playerDetailsList;
    private GameObject playerSelectionPrefab;
    private CurrentPlayerSO currentPlayer;
    private List<GameObject> playerCharacterGameObjectList = new List<GameObject>();
    private Coroutine coroutine;
    private int selectedPlayerIndex = 0;
    private float offset = 4f;

    private void Awake()
    {
        // Load resources
        playerSelectionPrefab = GameResources.Instance.playerSelectionPrefab;
        playerDetailsList = GameResources.Instance.playerDetailsList;
        currentPlayer = GameResources.Instance.currentPlayer;
    }

    private void Start()
    {
        // Instatiate player characters
        for (int i = 0; i < playerDetailsList.Count; i++)
        {
            GameObject playerSelectionObject = Instantiate(playerSelectionPrefab, characterSelector);
            playerCharacterGameObjectList.Add(playerSelectionObject);
            playerSelectionObject.transform.localPosition = new Vector3((offset * i), 0f, 0f);
            PopulatePlayerDetails(playerSelectionObject.GetComponent<PlayerSelectionUI>(), playerDetailsList[i]);
        }

        playerNameInput.text = currentPlayer.playerName;

        // Initialise the current player
        currentPlayer.playerDetails = playerDetailsList[selectedPlayerIndex];

    }

    /// <summary>
    /// Populate player character details for display
    /// </summary>
    private void PopulatePlayerDetails(PlayerSelectionUI playerSelection, PlayerDetailsSO playerDetails)
    {
        playerSelection.playerHandSpriteRenderer.sprite = playerDetails.playerHandSprite;
        playerSelection.playerHandNoWeaponSpriteRenderer.sprite = playerDetails.playerHandSprite;
        playerSelection.playerWeaponSpriteRenderer.sprite = playerDetails.startingWeapon.weaponSprite;
        playerSelection.animator.runtimeAnimatorController = playerDetails.runtimeAnimatorController;
    }

    /// <summary>
    /// Select next character - this method is called from the onClick event set in the inspector
    /// </summary>
    public void NextCharacter()
    {
        if (selectedPlayerIndex >= playerDetailsList.Count - 1)
            return;
        selectedPlayerIndex++;

        currentPlayer.playerDetails = playerDetailsList[selectedPlayerIndex];

        MoveToSelectedCharacter(selectedPlayerIndex);
    }


    /// <summary>
    /// Select previous character - this method is called from the onClick event set in the inspector
    /// </summary>
    public void PreviousCharacter()
    {
        if (selectedPlayerIndex == 0)
            return;

        selectedPlayerIndex--;

        currentPlayer.playerDetails = playerDetailsList[selectedPlayerIndex];

        MoveToSelectedCharacter(selectedPlayerIndex);
    }

    public void SwitchToCharacter(int id)
    {
        selectedPlayerIndex = id;
        currentPlayer.playerDetails = playerDetailsList[selectedPlayerIndex];
        MoveToSelectedCharacter(selectedPlayerIndex);
    }


    private void MoveToSelectedCharacter(int index)
    {
        if (coroutine != null)
            StopCoroutine(coroutine);

        coroutine = StartCoroutine(MoveToSelectedCharacterRoutine(index));
    }

    private IEnumerator MoveToSelectedCharacterRoutine(int index)
    {
        float currentLocalXPosition = characterSelector.localPosition.x;
        float targetLocalXPosition = index * offset * characterSelector.localScale.x * -1f;

        while (Mathf.Abs(currentLocalXPosition - targetLocalXPosition) > 0.01f)
        {
            currentLocalXPosition = Mathf.Lerp(currentLocalXPosition, targetLocalXPosition, Time.deltaTime * 10f);

            characterSelector.localPosition = new Vector3(currentLocalXPosition, characterSelector.localPosition.y, 0f);
            yield return null;
        }

        characterSelector.localPosition = new Vector3(targetLocalXPosition, characterSelector.localPosition.y, 0f);
    }

    /// <summary>
    /// Update player name - this method is called from the field changed event set in the inspector
    /// </summary>
    public void UpdatePlayerName()
    {
        playerNameInput.text = playerNameInput.text.ToUpper();

        if (WalletManager.instance.playerPDA != null)
        {
            applyButton.interactable = true;
            cancelButton.interactable = true;
            applyButton.gameObject.SetActive(true);
            cancelButton.gameObject.SetActive(true);
        }
    }

    public void OnApplyUsername()
    {
        UpdateUsername();
    }

    private async void UpdateUsername()
    {
        applyButton.interactable = false;
        cancelButton.interactable = false;
        InfoDisplay.Instance.ShowInfo("Processing", "Updating username!");
        var tx = new Transaction()
        {
            FeePayer = WalletManager.instance.sessionWallet.Account,
            Instructions = new List<TransactionInstruction>(),
            RecentBlockHash = (await WalletManager.instance.rpcClient.GetLatestBlockHashAsync()).Result.Value.Blockhash
        };

        var userAccounts = new UpdateUsernameAccounts()
        {
            Signer = WalletManager.instance.sessionWallet.Account,
            User = WalletManager.instance.playerPDA,
            SessionToken = WalletManager.instance.sessionWallet.SessionTokenPDA,
            SystemProgram = SystemProgram.ProgramIdKey
        };

        var userIx = SolDungeonsProgram.UpdateUsername(
            accounts: userAccounts,
            username: playerNameInput.text,
            WalletManager.instance.programId
        );

        tx.Add(userIx);

        RequestResult<string> resTx = await WalletManager.instance.sessionWallet.SignAndSendTransaction(tx);
        Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(resTx)}");

        if (!resTx.WasSuccessful || !resTx.WasHttpRequestSuccessful || !resTx.WasRequestSuccessfullyHandled)
        {
            playerNameInput.text = currentPlayer.playerName;
        }
        else
        {
            Transaction soarTx = new()
            {
                FeePayer = Web3.Account,
                Instructions = new List<TransactionInstruction>(),
                RecentBlockHash = (await WalletManager.instance.rpcClient.GetLatestBlockHashAsync()).Result.Value.Blockhash
            };

            var accounts = new UpdatePlayerAccounts()
            {
                User = Web3.Account,
                PlayerAccount = WalletManager.instance.playerSoarPDA,
            };

            var updatePlayerIx = SoarProgram.UpdatePlayer(
                accounts: accounts,
                username: playerNameInput.text,
                nftMeta: Web3.Account.PublicKey,
                SoarProgram.ProgramIdKey
            );

            soarTx.Add(updatePlayerIx);

            RequestResult<string> resSoarTx = await Web3.Wallet.SignAndSendTransaction(soarTx);
            Debug.Log($"Result: {Newtonsoft.Json.JsonConvert.SerializeObject(resSoarTx)}");

            if (!resSoarTx.WasSuccessful || !resSoarTx.WasHttpRequestSuccessful || !resSoarTx.WasRequestSuccessfullyHandled)
            {
                playerNameInput.text = currentPlayer.playerName;
            }
            else
            {
                currentPlayer.playerName = playerNameInput.text;
            }
        }

        InfoDisplay.Instance.HideInfo();
        applyButton.gameObject.SetActive(false);
        cancelButton.gameObject.SetActive(false);
    }

    public void OnCancelUsername()
    {
        applyButton.interactable = false;
        cancelButton.interactable = false;
        playerNameInput.text = currentPlayer.playerName;
        applyButton.gameObject.SetActive(false);
        cancelButton.gameObject.SetActive(false);
    }

    public void UpdateNameFromWeb3(string playerName)
    {
        playerNameInput.text = playerName.ToUpper();
        currentPlayer.playerName = playerNameInput.text;
        SetUserNameInputInteractibility(true);
    }

    public void SetUserNameInputInteractibility(bool val)
    { 
        playerNameInput.interactable = val;
    }
}